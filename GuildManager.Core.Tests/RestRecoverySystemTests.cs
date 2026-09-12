using System;
using System.Collections.Generic;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 静養・HP自然回復（仕様書 03 §3.5改）のテスト。疲労Fatigue廃止に伴う新規実装。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class RestRecoverySystemTests
    {
        private static readonly IReadOnlySet<Guid> NoDispatch = new HashSet<Guid>();

        [Fact]
        public void ProcessWeeklyRest_HealsNonDispatchedAdventurer()
        {
            // VIT=20 → MaxHP=90。回復率15%固定なので 90*0.15=13.5→13。
            var adventurer = new Adventurer { VIT = 20, CurrentHP = 50 };
            var state = new GameState { Adventurers = { adventurer } };
            var system = new RestRecoverySystem();

            system.ProcessWeeklyRest(state, NoDispatch);

            Assert.Equal(63, adventurer.CurrentHP);
        }

        [Fact]
        public void ProcessWeeklyRest_DoesNotHealDispatchedAdventurer()
        {
            var adventurer = new Adventurer { VIT = 20, CurrentHP = 50 };
            var state = new GameState { Adventurers = { adventurer } };
            var system = new RestRecoverySystem();

            system.ProcessWeeklyRest(state, new HashSet<Guid> { adventurer.Id });

            Assert.Equal(50, adventurer.CurrentHP); // 今週出撃したので静養対象外
        }

        [Fact]
        public void ProcessWeeklyRest_DoesNotHealSeverelyInjuredAdventurer()
        {
            var adventurer = new Adventurer
            {
                VIT = 20,
                CurrentHP = 1,
                Injury = InjurySeverity.Severe,
                InjuryWeeksRemaining = 5,
            };
            var state = new GameState { Adventurers = { adventurer } };
            var system = new RestRecoverySystem();

            system.ProcessWeeklyRest(state, NoDispatch);

            Assert.Equal(1, adventurer.CurrentHP); // 重傷は§3.6（InjuryRecoverySystem）が別管理
        }

        [Fact]
        public void ProcessWeeklyRest_HealsLightlyInjuredAdventurer()
        {
            var adventurer = new Adventurer
            {
                VIT = 20,
                CurrentHP = 50,
                Injury = InjurySeverity.Light,
                InjuryWeeksRemaining = 1,
            };
            var state = new GameState { Adventurers = { adventurer } };
            var system = new RestRecoverySystem();

            system.ProcessWeeklyRest(state, NoDispatch);

            Assert.Equal(63, adventurer.CurrentHP); // 軽傷は対象範囲（重傷のみ除外）
        }

        [Fact]
        public void ProcessWeeklyRest_DoesNotExceedMaxHp()
        {
            var adventurer = new Adventurer { VIT = 20, CurrentHP = 90 }; // MaxHP=90 ちょうど
            var state = new GameState { Adventurers = { adventurer } };
            var system = new RestRecoverySystem();

            system.ProcessWeeklyRest(state, NoDispatch);

            Assert.Equal(90, adventurer.CurrentHP);
        }

        [Fact]
        public void ProcessWeeklyRest_ClampsPartialOverheal_AtMaxHp()
        {
            var adventurer = new Adventurer { VIT = 20, CurrentHP = 85 }; // MaxHP=90, 回復量13だと98になり得る
            var state = new GameState { Adventurers = { adventurer } };
            var system = new RestRecoverySystem();

            system.ProcessWeeklyRest(state, NoDispatch);

            Assert.Equal(90, adventurer.CurrentHP);
        }

        [Fact]
        public void ProcessWeeklyRest_DoesNotHealTrainingAssignedAdventurer()
        {
            // 訓練場配置中はTrainingSystemが別のHP処理を行うため、静養回復の対象外
            // （出撃／訓練場配置／単純待機は互いに排他。→ 03 §3.5改）。
            var adventurer = new Adventurer { VIT = 20, CurrentHP = 50 };
            var state = new GameState { Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id, FacilityType.WarriorHall);
            var system = new RestRecoverySystem();

            system.ProcessWeeklyRest(state, NoDispatch);

            Assert.Equal(50, adventurer.CurrentHP);
        }
    }
}
