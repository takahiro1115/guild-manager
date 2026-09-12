using System;
using System.Collections.Generic;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 訓練施設配置の運用ルール（仕様書 03 §3.1〜3.4「運用ルール確定」・§3.5改・
    /// §6 v1.3改訂「訓練場・道場の4分割」）のテスト。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class TrainingSystemTests
    {
        private static readonly IReadOnlySet<Guid> NoDispatch = new HashSet<Guid>();

        // ---------------- 枠数制約（§6・施設ごとに独立） ----------------

        [Fact]
        public void TryAssign_SucceedsWhenSlotIsOpen()
        {
            var state = new GameState();
            var system = new TrainingSystem();
            var adventurer = new Adventurer();

            bool result = system.TryAssign(state, adventurer.Id, FacilityType.WarriorHall);

            Assert.True(result);
            Assert.True(state.TrainingAssignments.ContainsKey(adventurer.Id));
            Assert.Equal(FacilityType.WarriorHall, state.TrainingAssignments[adventurer.Id]);
        }

        [Fact]
        public void TryAssign_Fails_WhenSlotIsFull()
        {
            var state = new GameState();
            var system = new TrainingSystem();
            var first = new Adventurer();
            var second = new Adventurer();
            system.TryAssign(state, first.Id, FacilityType.WarriorHall); // Lv1相当＝1枠を埋める

            bool result = system.TryAssign(state, second.Id, FacilityType.WarriorHall);

            Assert.False(result);
            Assert.DoesNotContain(second.Id, state.TrainingAssignments.Keys);
        }

        [Fact]
        public void TryAssign_IsIdempotent_WhenAlreadyAssignedToSameFacility()
        {
            var state = new GameState();
            var system = new TrainingSystem();
            var adventurer = new Adventurer();
            system.TryAssign(state, adventurer.Id, FacilityType.WarriorHall);

            bool result = system.TryAssign(state, adventurer.Id, FacilityType.WarriorHall); // 同じ人・同じ施設をもう一度

            Assert.True(result);
            Assert.Single(state.TrainingAssignments);
        }

        [Fact]
        public void TryAssign_SlotsAreIndependentPerFacility()
        {
            // 戦士訓練所の枠(Lv1=1)が埋まっていても、教会の枠には別途配置できる（→ 03 §6）。
            var state = new GameState();
            var system = new TrainingSystem();
            var warrior = new Adventurer();
            var cleric = new Adventurer();
            system.TryAssign(state, warrior.Id, FacilityType.WarriorHall);

            bool result = system.TryAssign(state, cleric.Id, FacilityType.Church);

            Assert.True(result);
            Assert.Equal(FacilityType.Church, state.TrainingAssignments[cleric.Id]);
        }

        [Fact]
        public void TryAssign_ReassignsToNewFacility_ReleasingThePreviousSlot()
        {
            // 既に別施設に配置済みの場合、付け替えると元の施設の枠が解放される。
            var state = new GameState();
            var system = new TrainingSystem();
            var adventurer = new Adventurer();
            system.TryAssign(state, adventurer.Id, FacilityType.WarriorHall);

            bool result = system.TryAssign(state, adventurer.Id, FacilityType.Church);

            Assert.True(result);
            Assert.Equal(FacilityType.Church, state.TrainingAssignments[adventurer.Id]);
            Assert.Equal(0, system.CountAssigned(state, FacilityType.WarriorHall));
            Assert.Equal(1, system.CountAssigned(state, FacilityType.Church));
        }

        [Fact]
        public void Unassign_RemovesFromTrainingAssignments()
        {
            var state = new GameState();
            var system = new TrainingSystem();
            var adventurer = new Adventurer();
            system.TryAssign(state, adventurer.Id, FacilityType.WarriorHall);

            system.Unassign(state, adventurer.Id);

            Assert.DoesNotContain(adventurer.Id, state.TrainingAssignments.Keys);
        }

        [Fact]
        public void GetSlotCapacity_MatchesFacilityLevel1ByDefault()
        {
            var system = new TrainingSystem();
            Assert.Equal(1, system.GetSlotCapacity(new GameState(), FacilityType.WarriorHall));
        }

        [Fact]
        public void GetSlotCapacity_IncreasesWithFacilityLevel()
        {
            var system = new TrainingSystem();
            var state = new GameState();
            foreach (var f in state.Facilities)
                if (f.Type == FacilityType.WarriorHall) f.CurrentLevel = 3;

            Assert.Equal(3, system.GetSlotCapacity(state, FacilityType.WarriorHall));
        }

        // ---------------- 週次費用（都度払い） ----------------

        [Fact]
        public void ProcessWeeklyTraining_DeductsWeeklyCost_ForAssignedAdventurer()
        {
            var adventurer = new Adventurer { VIT = 20, CurrentHP = 90 };
            var state = new GameState { Gold = 1000, Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id, FacilityType.WarriorHall);
            var system = new TrainingSystem();

            system.ProcessWeeklyTraining(state, NoDispatch);

            Assert.Equal(1000 - TrainingBalance.WeeklyCost, state.Gold);
        }

        [Fact]
        public void ProcessWeeklyTraining_DoesNothing_ForUnassignedAdventurer()
        {
            var adventurer = new Adventurer { VIT = 20, CurrentHP = 90 };
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
            var adventurer = new Adventurer { VIT = 20, CurrentHP = 90 };
            var state = new GameState { Gold = 1000, Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id, FacilityType.WarriorHall);
            var system = new TrainingSystem();

            system.ProcessWeeklyTraining(state, new HashSet<Guid> { adventurer.Id });

            Assert.Equal(1000 - TrainingBalance.WeeklyCost, state.Gold);
        }

        // ---------------- 訓練週のHP処理（§3.5改。全施設共通） ----------------

        [Fact]
        public void ProcessWeeklyTraining_DecreasesHp_ForAssignedNonDispatchedAdventurer()
        {
            var adventurer = new Adventurer { VIT = 20, CurrentHP = 90 }; // MaxHP=90
            var state = new GameState { Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id, FacilityType.WarriorHall);
            var system = new TrainingSystem();

            system.ProcessWeeklyTraining(state, NoDispatch);

            Assert.Equal(90 - TrainingBalance.WeeklyHpCost, adventurer.CurrentHP);
        }

        [Fact]
        public void ProcessWeeklyTraining_DoesNotDecreaseHp_ForDispatchedAdventurer()
        {
            // 出撃した週は訓練固有のHP処理を行わない（戦闘側のHP処理は別に適用される。§3.5改：排他）。
            var adventurer = new Adventurer { VIT = 20, CurrentHP = 90 };
            var state = new GameState { Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id, FacilityType.WarriorHall);
            var system = new TrainingSystem();

            system.ProcessWeeklyTraining(state, new HashSet<Guid> { adventurer.Id });

            Assert.Equal(90, adventurer.CurrentHP);
        }

        [Fact]
        public void ProcessWeeklyTraining_ClampsHpAtMinimumOfOne()
        {
            var adventurer = new Adventurer { VIT = 20, CurrentHP = 3 }; // WeeklyHpCost(5)を引くと負になる
            var state = new GameState { Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id, FacilityType.WarriorHall);
            var system = new TrainingSystem();

            system.ProcessWeeklyTraining(state, NoDispatch);

            Assert.Equal(TrainingBalance.MinHp, adventurer.CurrentHP);
        }

        [Fact]
        public void ProcessWeeklyTraining_NeverTouchesInjuryStatus()
        {
            // 訓練によるHP減少は負傷（InjurySeverity）を一切発生させない（致死判定と非接続）。
            var adventurer = new Adventurer { VIT = 20, CurrentHP = 3 };
            var state = new GameState { Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id, FacilityType.WarriorHall);
            var system = new TrainingSystem();

            system.ProcessWeeklyTraining(state, NoDispatch);

            Assert.Equal(InjurySeverity.None, adventurer.Injury);
        }
    }
}
