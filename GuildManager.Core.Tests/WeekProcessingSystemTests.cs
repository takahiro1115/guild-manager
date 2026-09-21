using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 週次決算オーケストレーション（WeekProcessingSystem・AutoSkipService、仕様書 03 §1.3）のテスト。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class WeekProcessingSystemTests
    {
        /// <summary>NextInt(min, max) が常に min を返すテスト用スタブ。</summary>
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        /// <summary>NextInt(min, max) が常に max を返すテスト用スタブ。</summary>
        private class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        /// <summary>NextInt(min, max) が固定値を [min, max] にクランプして返すテスト用スタブ。</summary>
        private class FixedRng : IRng
        {
            private readonly int _value;
            public FixedRng(int value) => _value = value;
            public int NextInt(int min, int max) => Math.Clamp(_value, min, max);
        }

        /// <summary>
        /// 実運用（MainDashboard）と同じ構成でWeekProcessingSystemを組み立てる。
        /// 乱数はAlwaysMinRngを既定にし、個別テストで必要な部分だけ差し替える
        /// （大迷宮の出撃は既定構成＝固定シード。→ WeekProcessingSystemのコンストラクタ）。
        /// </summary>
        private static WeekProcessingSystem BuildSystem(
            IRng? agingRng = null, IRng? growthRng = null, IRng? recruitmentRng = null)
        {
            var growth = new GrowthSystem(growthRng ?? new AlwaysMinRng());
            var economy = new EconomySystem();
            var satisfaction = new SatisfactionSystem();

            return new WeekProcessingSystem(
                guildRankSystem: new GuildRankSystem(),
                economySystem: economy,
                subsidySystem: new SubsidySystem(),
                trainingSystem: new TrainingSystem(),
                injuryRecoverySystem: new InjuryRecoverySystem(),
                restRecoverySystem: new RestRecoverySystem(),
                growthSystem: growth,
                satisfactionSystem: satisfaction,
                agingSystem: new AgingSystem(agingRng ?? new AlwaysMinRng()),
                facilitySystem: new FacilitySystem(),
                defeatSystem: new DefeatSystem(),
                recruitmentSystem: new RecruitmentSystem(recruitmentRng ?? new AlwaysMinRng()));
        }

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            return party;
        }

        // ---------------- 基本動作 ----------------

        [Fact]
        public void ProcessWeek_IncrementsWeekNumber()
        {
            var state = new GameState { WeekNumber = 5 };
            var system = BuildSystem();

            system.ProcessWeek(state);

            Assert.Equal(6, state.WeekNumber);
        }

        [Fact]
        public void ProcessWeek_Flags_CarryOriginalWeekNumber()
        {
            var state = new GameState { WeekNumber = 10 };
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.Equal(10, result.Flags.Week); // 決算処理"前"の週番号
        }

        [Fact]
        public void ProcessWeek_DoesNotThrow_WithEmptyRosterAndNoDispatches()
        {
            var state = new GameState();
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.NotNull(result);
        }

        // ---------------- RecruitmentTrialOccurred ----------------

        [Fact]
        public void ProcessWeek_SetsRecruitmentTrialOccurred_OnFirstWeekOfSecondYear()
        {
            var state = new GameState { WeekNumber = 48 }; // 決算後に49週目（2年目・新年第1週）になる
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.True(result.Flags.RecruitmentTrialOccurred);
        }

        [Fact]
        public void ProcessWeek_DoesNotSetRecruitmentTrialOccurred_OnOrdinaryWeek()
        {
            var state = new GameState { WeekNumber = 10 };
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.False(result.Flags.RecruitmentTrialOccurred);
        }

        [Fact]
        public void ProcessWeek_DoesNotSetRecruitmentTrialOccurred_AfterDefeat()
        {
            var state = new GameState { WeekNumber = 48, DefeatReason = DefeatReason.Bankruptcy };
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.False(result.Flags.RecruitmentTrialOccurred);
        }

        // ---------------- FacilityConstructionCompleted ----------------

        [Fact]
        public void ProcessWeek_SetsFacilityConstructionCompleted_WhenConstructionFinishes()
        {
            var state = new GameState();
            state.UnderConstruction = new FacilityConstruction { Type = FacilityType.Tavern, TargetLevel = 2, WeeksRemaining = 1 };
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.True(result.Flags.FacilityConstructionCompleted);
            Assert.NotNull(result.CompletedFacility);
        }

        [Fact]
        public void ProcessWeek_DoesNotSetFacilityConstructionCompleted_WhenNothingUnderConstruction()
        {
            var state = new GameState();
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.False(result.Flags.FacilityConstructionCompleted);
            Assert.Null(result.CompletedFacility);
        }

        // ---------------- DeathOrPermanentInjuryOccurred ----------------

        [Fact]
        public void ProcessWeek_DoesNotSetDeathOrPermanentInjuryOccurred_WhenNoDispatchResolves()
        {
            var state = new GameState { Adventurers = { new Adventurer() } };
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.False(result.Flags.DeathOrPermanentInjuryOccurred);
        }

        // ---------------- FinalQuestNewlyUnlocked ----------------

        [Fact]
        public void ProcessWeek_SetsFinalQuestNewlyUnlocked_WhenReachingRankAThisWeek()
        {
            var state = new GameState { GuildRank = GuildRank.B, Reputation = GuildRankBalance.GetThreshold(GuildRank.A).PromoteAt };
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.Equal(GuildRank.A, state.GuildRank);
            Assert.True(result.Flags.FinalQuestNewlyUnlocked);
        }

        [Fact]
        public void ProcessWeek_DoesNotSetFinalQuestNewlyUnlocked_WhenAlreadyUnlocked()
        {
            var state = new GameState { GuildRank = GuildRank.A, FinalQuestUnlocked = true, Reputation = GuildRankBalance.GetThreshold(GuildRank.A).PromoteAt };
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.False(result.Flags.FinalQuestNewlyUnlocked);
        }

        [Fact]
        public void ProcessWeek_DoesNotSetFinalQuestNewlyUnlocked_WhenBelowRankA()
        {
            var state = new GameState { GuildRank = GuildRank.G };
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.False(result.Flags.FinalQuestNewlyUnlocked);
        }

        // ---------------- SatisfactionWarningOccurred ----------------

        [Fact]
        public void ProcessWeek_SetsSatisfactionWarningOccurred_WhenNegotiationNewlyTriggered()
        {
            var adventurer = new Adventurer { Satisfaction = SatisfactionBalance.NegotiationThreshold - 1 };
            var state = new GameState { Adventurers = { adventurer } };
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.True(adventurer.NeedsNegotiation);
            Assert.True(result.Flags.SatisfactionWarningOccurred);
        }

        [Fact]
        public void ProcessWeek_DoesNotSetSatisfactionWarningOccurred_WhenAlreadyWarned()
        {
            var adventurer = new Adventurer { Satisfaction = 10, NeedsNegotiation = true, NegotiationWeeksElapsed = 0 };
            var state = new GameState { Adventurers = { adventurer } };
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.False(result.Flags.SatisfactionWarningOccurred);
        }

        [Fact]
        public void ProcessWeek_DoesNotSetSatisfactionWarningOccurred_ForHealthySatisfaction()
        {
            var adventurer = new Adventurer { Satisfaction = 90, WeeklyWage = 60 };
            var state = new GameState { Adventurers = { adventurer } };
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.False(result.Flags.SatisfactionWarningOccurred);
        }

        // ---------------- DefeatOccurred ----------------

        [Fact]
        public void ProcessWeek_SetsDefeatOccurred_OnNewBankruptcy()
        {
            var state = new GameState { Gold = -100, ConsecutiveNegativeGoldWeeks = EconomyBalance.BankruptcyConsecutiveWeeksThreshold - 1 };
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.NotNull(result.NewDefeatReason);
            Assert.True(result.Flags.DefeatOccurred);
        }

        [Fact]
        public void ProcessWeek_DoesNotSetDefeatOccurred_WhenAlreadyDefeated()
        {
            var state = new GameState { DefeatReason = DefeatReason.Bankruptcy };
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.Null(result.NewDefeatReason); // 既に確定済みなので「新たに」ではない
            Assert.False(result.Flags.DefeatOccurred);
        }

        [Fact]
        public void ProcessWeek_DoesNotSetDefeatOccurred_ForHealthyFinances()
        {
            var state = new GameState { Gold = 10000 };
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.False(result.Flags.DefeatOccurred);
        }

        [Fact]
        public void ProcessWeek_DoesNotSetDefeatOccurred_WhenFinancesHealthy()
        {
            // 敗北条件は破産のみ（治安崩壊は撤廃済み）。資金が健全なら週次決算で敗北は成立しない
            // （→ DefeatSystem）。
            var state = new GameState { Gold = 10000 };
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.False(result.Flags.DefeatOccurred);
            Assert.Null(result.NewDefeatReason);
            Assert.Null(state.DefeatReason);
        }

        // ---------------- ShouldStopAutoSkip 複合判定 ----------------

        [Fact]
        public void ProcessWeek_ShouldStopAutoSkip_IsFalse_ForUneventfulWeek()
        {
            var state = new GameState { WeekNumber = 5, Adventurers = { new Adventurer { Satisfaction = 90, WeeklyWage = 60 } } };
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.False(result.Flags.ShouldStopAutoSkip);
        }

        // ---------------- AutoSkipService ----------------

        [Fact]
        public void AutoSkip_StopsImmediately_OnFirstEventfulWeek()
        {
            var state = new GameState { WeekNumber = 48 }; // 次の決算で採用試験週になる
            var autoSkip = new AutoSkipService(BuildSystem());

            var results = autoSkip.AutoSkip(state, maxWeeks: 100);

            Assert.Single(results);
            Assert.True(results[0].RecruitmentTrialOccurred);
        }

        [Fact]
        public void AutoSkip_RunsUntilMaxWeeks_WhenNothingEverStopsIt()
        {
            // 採用試験週(48の倍数+1)・脅威度閾値・満足度警告等のいずれにも該当しない
            // 静かな期間だけを対象に、maxWeeksちょうどで打ち切られることを確認する。
            var state = new GameState { WeekNumber = 2, Adventurers = { new Adventurer { Satisfaction = 90, WeeklyWage = 60 } } };
            var autoSkip = new AutoSkipService(BuildSystem());

            var results = autoSkip.AutoSkip(state, maxWeeks: 10);

            Assert.Equal(10, results.Count);
            Assert.Equal(12, state.WeekNumber); // 2 + 10週
            Assert.All(results, r => Assert.False(r.ShouldStopAutoSkip));
        }

        [Fact]
        public void AutoSkip_ReturnsResultsInProgressOrder_WithCorrectWeekNumbers()
        {
            var state = new GameState { WeekNumber = 2, Adventurers = { new Adventurer { Satisfaction = 90, WeeklyWage = 60 } } };
            var autoSkip = new AutoSkipService(BuildSystem());

            var results = autoSkip.AutoSkip(state, maxWeeks: 3);

            Assert.Equal(new[] { 2, 3, 4 }, results.Select(r => r.Week));
        }

        [Fact]
        public void AutoSkip_StopsOnDefeat_EvenThoughNotInLiteralEightConditions()
        {
            var state = new GameState { Gold = -1, ConsecutiveNegativeGoldWeeks = EconomyBalance.BankruptcyConsecutiveWeeksThreshold - 1 };
            var autoSkip = new AutoSkipService(BuildSystem());

            var results = autoSkip.AutoSkip(state, maxWeeks: 100);

            Assert.Single(results);
            Assert.True(results[0].DefeatOccurred);
            Assert.NotNull(state.DefeatReason);
        }

        [Fact]
        public void AutoSkip_DoesNotDispatchAnyMission()
        {
            // 自動スキップは新しい出撃を自動で行わない（→ 03 §1.3）。WeekProcessingSystem.ProcessWeek自体が
            // 出撃操作を含まない設計であることを、何週進めても出撃中の部隊が生まれない形で確認する。
            var state = new GameState { Adventurers = { new Adventurer { Satisfaction = 90, WeeklyWage = 60 } }, DungeonFields = SampleData.CreateDefaultFields() };
            var autoSkip = new AutoSkipService(BuildSystem());

            autoSkip.AutoSkip(state, maxWeeks: 5);

            Assert.Empty(state.ActiveDungeonMissions); // 誰も出撃していない
            Assert.False(state.Adventurers[0].IsDispatched);
        }
    }
}
