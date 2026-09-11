using System;
using System.Collections.Generic;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 訓練場配置の運用ルール（仕様書 03 §3.1〜3.4「運用ルール確定」・§3.5改）のテスト。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class TrainingSystemTests
    {
        private static readonly IReadOnlySet<Guid> NoDispatch = new HashSet<Guid>();

        // ---------------- 枠数制約（§6・TrainingBalance.SlotCapacity） ----------------

        [Fact]
        public void TryAssign_SucceedsWhenSlotIsOpen()
        {
            var state = new GameState();
            var system = new TrainingSystem();
            var adventurer = new Adventurer();

            bool result = system.TryAssign(state, adventurer.Id);

            Assert.True(result);
            Assert.Contains(adventurer.Id, state.TrainingAssignments);
        }

        [Fact]
        public void TryAssign_Fails_WhenSlotIsFull()
        {
            var state = new GameState();
            var system = new TrainingSystem();
            var first = new Adventurer();
            var second = new Adventurer();
            system.TryAssign(state, first.Id); // Lv1相当＝1枠を埋める

            bool result = system.TryAssign(state, second.Id);

            Assert.False(result);
            Assert.DoesNotContain(second.Id, state.TrainingAssignments);
        }

        [Fact]
        public void TryAssign_IsIdempotent_WhenAlreadyAssigned()
        {
            var state = new GameState();
            var system = new TrainingSystem();
            var adventurer = new Adventurer();
            system.TryAssign(state, adventurer.Id);

            bool result = system.TryAssign(state, adventurer.Id); // 同じ人をもう一度

            Assert.True(result);
            Assert.Single(state.TrainingAssignments);
        }

        [Fact]
        public void Unassign_RemovesFromTrainingAssignments()
        {
            var state = new GameState();
            var system = new TrainingSystem();
            var adventurer = new Adventurer();
            system.TryAssign(state, adventurer.Id);

            system.Unassign(state, adventurer.Id);

            Assert.DoesNotContain(adventurer.Id, state.TrainingAssignments);
        }

        [Fact]
        public void GetSlotCapacity_MatchesTrainingGroundLevel1ByDefault()
        {
            var system = new TrainingSystem();
            Assert.Equal(1, system.GetSlotCapacity(new GameState()));
        }

        [Fact]
        public void GetSlotCapacity_IncreasesWithTrainingGroundLevel()
        {
            var system = new TrainingSystem();
            var state = new GameState();
            foreach (var f in state.Facilities)
                if (f.Type == FacilityType.TrainingGround) f.CurrentLevel = 3;

            Assert.Equal(3, system.GetSlotCapacity(state));
        }

        // ---------------- 週次費用（都度払い） ----------------

        [Fact]
        public void ProcessWeeklyTraining_DeductsWeeklyCost_ForAssignedAdventurer()
        {
            var adventurer = new Adventurer { END = 20, CurrentHP = 90 };
            var state = new GameState { Gold = 1000, Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id);
            var system = new TrainingSystem();

            system.ProcessWeeklyTraining(state, NoDispatch);

            Assert.Equal(1000 - TrainingBalance.WeeklyCost, state.Gold);
        }

        [Fact]
        public void ProcessWeeklyTraining_DoesNothing_ForUnassignedAdventurer()
        {
            var adventurer = new Adventurer { END = 20, CurrentHP = 90 };
            var state = new GameState { Gold = 1000, Adventurers = { adventurer } };
            var system = new TrainingSystem();

            system.ProcessWeeklyTraining(state, NoDispatch);

            Assert.Equal(1000, state.Gold);
            Assert.Equal(90, adventurer.CurrentHP);
        }

        [Fact]
        public void ProcessWeeklyTraining_ChargesCost_EvenIfDispatchedThatWeek()
        {
            // 配置されている限り費用は都度払い（出撃の有無に関わらず発生する）。
            var adventurer = new Adventurer { END = 20, CurrentHP = 90 };
            var state = new GameState { Gold = 1000, Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id);
            var system = new TrainingSystem();

            system.ProcessWeeklyTraining(state, new HashSet<Guid> { adventurer.Id });

            Assert.Equal(1000 - TrainingBalance.WeeklyCost, state.Gold);
        }

        // ---------------- 訓練週のHP処理（§3.5改） ----------------

        [Fact]
        public void ProcessWeeklyTraining_DecreasesHp_ForAssignedNonDispatchedAdventurer()
        {
            var adventurer = new Adventurer { END = 20, CurrentHP = 90 }; // MaxHP=90
            var state = new GameState { Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id);
            var system = new TrainingSystem();

            system.ProcessWeeklyTraining(state, NoDispatch);

            Assert.Equal(90 - TrainingBalance.WeeklyHpCost, adventurer.CurrentHP);
        }

        [Fact]
        public void ProcessWeeklyTraining_DoesNotDecreaseHp_ForDispatchedAdventurer()
        {
            // 出撃した週は訓練固有のHP処理を行わない（戦闘側のHP処理は別に適用される。§3.5改：排他）。
            var adventurer = new Adventurer { END = 20, CurrentHP = 90 };
            var state = new GameState { Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id);
            var system = new TrainingSystem();

            system.ProcessWeeklyTraining(state, new HashSet<Guid> { adventurer.Id });

            Assert.Equal(90, adventurer.CurrentHP);
        }

        [Fact]
        public void ProcessWeeklyTraining_ClampsHpAtMinimumOfOne()
        {
            var adventurer = new Adventurer { END = 20, CurrentHP = 3 }; // WeeklyHpCost(5)を引くと負になる
            var state = new GameState { Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id);
            var system = new TrainingSystem();

            system.ProcessWeeklyTraining(state, NoDispatch);

            Assert.Equal(TrainingBalance.MinHp, adventurer.CurrentHP);
        }

        [Fact]
        public void ProcessWeeklyTraining_NeverTouchesInjuryStatus()
        {
            // 訓練によるHP減少は負傷（InjurySeverity）を一切発生させない（致死判定と非接続）。
            var adventurer = new Adventurer { END = 20, CurrentHP = 3 };
            var state = new GameState { Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id);
            var system = new TrainingSystem();

            system.ProcessWeeklyTraining(state, NoDispatch);

            Assert.Equal(InjurySeverity.None, adventurer.Injury);
        }
    }
}
