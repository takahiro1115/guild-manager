using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
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
        /// クエスト解決関連はAlwaysMinRng（索敵ロール最小＝奇襲成功しやすい、致死判定ロール
        /// 最小＝常に生存）を既定にし、個別テストで必要な部分だけ差し替える。
        /// </summary>
        private static WeekProcessingSystem BuildSystem(
            IRng? questRng = null, IRng? agingRng = null, IRng? growthRng = null,
            IRng? securityRng = null, IRng? questBoardRng = null, IRng? recruitmentRng = null)
        {
            var growth = new GrowthSystem(growthRng ?? new AlwaysMinRng());
            var economy = new EconomySystem();
            var satisfaction = new SatisfactionSystem();
            var compatibility = new CompatibilitySystem(new AlwaysMinRng());
            var questResolver = new QuestResolver(questRng ?? new AlwaysMinRng());
            var dispatch = new QuestDispatchSystem(questResolver, growth, economy, satisfaction, compatibility);

            return new WeekProcessingSystem(
                questDispatchSystem: dispatch,
                guildRankSystem: new GuildRankSystem(),
                securitySystem: new SecuritySystem(securityRng ?? new AlwaysMinRng()),
                questBoardSystem: new QuestBoardSystem(questBoardRng ?? new AlwaysMinRng()),
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

        // ---------------- MultiWeekQuestReturned ----------------

        [Fact]
        public void ProcessWeek_SetsMultiWeekQuestReturned_ForLargeScaleQuestResolution()
        {
            var member = new Adventurer { STR = 50, AGI = 50, VIT = 50, MND = 50, DEX = 50, LDR = 50 };
            member.CurrentHP = member.MaxHP;
            var state = new GameState { Adventurers = { member } };
            var quest = new Quest { Scale = QuestScale.Large, Difficulty = 10, ScoutRequirement = 10 };
            state.ActiveDispatches.Add(new ActiveDispatch { Party = PartyOf(member), Quest = quest, WeeksRemaining = 1 });
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.True(result.Flags.MultiWeekQuestReturned);
        }

        [Fact]
        public void ProcessWeek_DoesNotSetMultiWeekQuestReturned_ForSmallScaleQuestResolution()
        {
            var member = new Adventurer { STR = 50, AGI = 50, VIT = 50, MND = 50, DEX = 50, LDR = 50 };
            member.CurrentHP = member.MaxHP;
            var state = new GameState { Adventurers = { member } };
            var quest = new Quest { Scale = QuestScale.Small, Difficulty = 10, ScoutRequirement = 10 };
            state.ActiveDispatches.Add(new ActiveDispatch { Party = PartyOf(member), Quest = quest, WeeksRemaining = 1 });
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.False(result.Flags.MultiWeekQuestReturned);
        }

        [Fact]
        public void ProcessWeek_DoesNotSetMultiWeekQuestReturned_WhileStillInTransit()
        {
            var member = new Adventurer();
            var state = new GameState { Adventurers = { member } };
            var quest = new Quest { Scale = QuestScale.Large };
            state.ActiveDispatches.Add(new ActiveDispatch { Party = PartyOf(member), Quest = quest, WeeksRemaining = 3 });
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.False(result.Flags.MultiWeekQuestReturned); // まだ満了していない（残り2週になるだけ）
        }

        // ---------------- DeathOrPermanentInjuryOccurred ----------------

        [Fact]
        public void ProcessWeek_SetsDeathOrPermanentInjuryOccurred_WhenMemberFalls()
        {
            var weakling = new Adventurer { STR = 1, AGI = 1, VIT = 1, MND = 1, DEX = 1, LDR = 1 };
            weakling.CurrentHP = weakling.MaxHP;
            var state = new GameState { Adventurers = { weakling } };
            var quest = new Quest { Difficulty = 100, ScoutRequirement = 1 };
            state.ActiveDispatches.Add(new ActiveDispatch { Party = PartyOf(weakling), Quest = quest, WeeksRemaining = 1 });
            // AlwaysMaxRngで索敵・HP消費%・致死判定ロールをすべて最大にし、確実に戦死させる。
            var system = BuildSystem(questRng: new AlwaysMaxRng());

            var result = system.ProcessWeek(state);

            Assert.True(result.Flags.DeathOrPermanentInjuryOccurred);
        }

        [Fact]
        public void ProcessWeek_DoesNotSetDeathOrPermanentInjuryOccurred_WhenNoDispatchResolves()
        {
            var state = new GameState { Adventurers = { new Adventurer() } };
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.False(result.Flags.DeathOrPermanentInjuryOccurred);
        }

        // ---------------- ThreatThresholdNewlyCrossed ----------------

        [Fact]
        public void ProcessWeek_DoesNotSetThreatThresholdNewlyCrossed_WhenBelowBothThresholds()
        {
            var state = new GameState { ThreatLevel = 10 };
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.False(result.Flags.ThreatThresholdNewlyCrossed);
        }

        [Fact]
        public void ProcessWeek_DoesNotSetThreatThresholdNewlyCrossed_WhenAlreadyAboveThreshold_AndStaysAbove()
        {
            // 既に脅威度80%（75%閾値を超過済み）で、今週も変化が無い（放置クエストも無い）場合、
            // 「新たに跨いだ」わけではないので発火しない。
            var state = new GameState { ThreatLevel = 80 };
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.Equal(80, state.ThreatLevel); // このテストでは脅威度が変化しないことが前提
            Assert.False(result.Flags.ThreatThresholdNewlyCrossed);
        }

        [Fact]
        public void ProcessWeek_SetsThreatThresholdNewlyCrossed_WhenCrossing75PercentThisWeek()
        {
            var state = new GameState { ThreatLevel = 74 };
            var expired = new Quest { QuestType = QuestType.Subjugation, DeadlineWeeks = 1 };
            state.AvailableQuests.Add(expired);
            // AlwaysMaxRngで放置クエストの脅威度上昇量を最大化する。
            var system = BuildSystem(securityRng: new AlwaysMaxRng());

            var result = system.ProcessWeek(state);

            Assert.True(state.ThreatLevel >= SecurityBalance.SubsidyCutThreatThreshold);
            Assert.True(result.Flags.ThreatThresholdNewlyCrossed);
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

        // ---------------- SubjugationQuestExpiringNextWeek ----------------

        [Fact]
        public void ProcessWeek_SetsSubjugationQuestExpiringNextWeek_WhenDeadlineBecomesOne()
        {
            var state = new GameState();
            state.AvailableQuests.Add(new Quest { QuestType = QuestType.Subjugation, DeadlineWeeks = 2 });
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.True(result.Flags.SubjugationQuestExpiringNextWeek);
        }

        [Fact]
        public void ProcessWeek_DoesNotSetSubjugationQuestExpiringNextWeek_WhenDeadlineStillFarAway()
        {
            var state = new GameState();
            state.AvailableQuests.Add(new Quest { QuestType = QuestType.Subjugation, DeadlineWeeks = 10 });
            var system = BuildSystem();

            var result = system.ProcessWeek(state);

            Assert.False(result.Flags.SubjugationQuestExpiringNextWeek);
        }

        [Fact]
        public void ProcessWeek_FiresExpiringNextWeek_OnlyOnceAcrossQuestLifetime()
        {
            var state = new GameState();
            state.AvailableQuests.Add(new Quest { QuestType = QuestType.Subjugation, DeadlineWeeks = 2 });
            var system = BuildSystem();

            var firstWeek = system.ProcessWeek(state); // 2→1：発火するはず
            var secondWeek = system.ProcessWeek(state); // 1→0：期限切れで除去され、対象から消える

            Assert.True(firstWeek.Flags.SubjugationQuestExpiringNextWeek);
            Assert.False(secondWeek.Flags.SubjugationQuestExpiringNextWeek);
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
            var state = new GameState { Gold = -100, ConsecutiveNegativeGoldWeeks = SecurityBalance.BankruptcyConsecutiveWeeksThreshold - 1 };
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
            // questBoardRng=FixedRng(3)で常に非討伐（調査・探索）テンプレートだけを補充させ、
            // 「討伐クエストの期限切れ1週前」（→ 停止条件8）が意図せず割り込まないようにする
            // （討伐クエストは受注可能一覧に常在するため、放置すればいずれ必ず期限切れ間近になる。
            // これは仕様どおりの挙動であり、このテストの対象外にするための構成）。
            var state = new GameState { WeekNumber = 2, Adventurers = { new Adventurer { Satisfaction = 90, WeeklyWage = 60 } } };
            var autoSkip = new AutoSkipService(BuildSystem(questBoardRng: new FixedRng(3)));

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
            var state = new GameState { Gold = -1, ConsecutiveNegativeGoldWeeks = SecurityBalance.BankruptcyConsecutiveWeeksThreshold - 1 };
            var autoSkip = new AutoSkipService(BuildSystem());

            var results = autoSkip.AutoSkip(state, maxWeeks: 100);

            Assert.Single(results);
            Assert.True(results[0].DefeatOccurred);
            Assert.NotNull(state.DefeatReason);
        }

        [Fact]
        public void AutoSkip_DoesNotDispatchAnyQuest_EvenWhenQuestsAreAvailable()
        {
            // 自動スキップは新しいクエストを自動受注しない（→ 03 §1.3）。
            // WeekProcessingSystem.ProcessWeek自体が派遣操作を含まない設計であることを、
            // 受注可能クエストの残数が「補充されるだけ」で「派遣されて減ることはない」形で確認する。
            var state = new GameState { Adventurers = { new Adventurer { Satisfaction = 90, WeeklyWage = 60 } } };
            var autoSkip = new AutoSkipService(BuildSystem(questBoardRng: new FixedRng(3)));

            autoSkip.AutoSkip(state, maxWeeks: 5);

            Assert.Empty(state.ActiveDispatches); // 誰も派遣されていない
        }
    }
}
