using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 序盤の通しシナリオテスト（→ コアシステム刷新仕様「実装開始時の注意：出撃1回目から
    /// ボス撃破・枠拡張までの通しシミュレーションが完全にグリーンになることを優先」）。
    ///
    /// 個々のシステム単体ではなく、実運用と同じ WeekProcessingSystem 経由で
    /// 「採取で資金を貯める → 昇格試験が提示される → 決戦に勝つ → 第2部隊枠が開く」
    /// という一連の流れが繋がっていることを検証する。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class EarlyGameScenarioTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private class FixedRng : IRng
        {
            private readonly int _value;
            public FixedRng(int value) => _value = value;
            public int NextInt(int min, int max) => Math.Clamp(_value, min, max);
        }

        /// <summary>実運用（MainDashboard）と同じ構成で週次決算を組み立てる。</summary>
        private static (WeekProcessingSystem Week, QuestDispatchSystem Dispatch) BuildSystems()
        {
            var growth = new GrowthSystem(new AlwaysMinRng());
            var economy = new EconomySystem();
            var satisfaction = new SatisfactionSystem();
            var compatibility = new CompatibilitySystem(new AlwaysMinRng());
            // 索敵・HP消費ともに中庸な値を返すRNG（極端な奇襲・不意打ちを避ける）。
            var questResolver = new QuestResolver(new FixedRng(50));
            var dispatch = new QuestDispatchSystem(questResolver, growth, economy, satisfaction, compatibility);
            var recruitment = new RecruitmentSystem(new AlwaysMinRng());

            var week = new WeekProcessingSystem(
                questDispatchSystem: dispatch,
                guildRankSystem: new GuildRankSystem(),
                securitySystem: new SecuritySystem(new AlwaysMinRng()),
                questBoardSystem: new QuestBoardSystem(new AlwaysMinRng()),
                economySystem: economy,
                subsidySystem: new SubsidySystem(),
                trainingSystem: new TrainingSystem(),
                injuryRecoverySystem: new InjuryRecoverySystem(),
                restRecoverySystem: new RestRecoverySystem(),
                growthSystem: growth,
                satisfactionSystem: satisfaction,
                agingSystem: new AgingSystem(new AlwaysMinRng()),
                facilitySystem: new FacilitySystem(),
                defeatSystem: new DefeatSystem(),
                recruitmentSystem: recruitment,
                guildProgressionSystem: new GuildProgressionSystem(recruitment));

            return (week, dispatch);
        }

        /// <summary>ボス（難易度28）にも勝てる程度に鍛えられた冒険者。週給は0にして資金変動を単純化する。</summary>
        private static Adventurer MakeVeteran()
        {
            var a = new Adventurer
            {
                STR = 60, AGI = 60, VIT = 60, MND = 60, DEX = 60, LDR = 60, INT = 60,
                WeeklyWage = 0,
                Placement = Placement.Front,
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            return party;
        }

        private static Quest MakeGatheringQuest() => new Quest
        {
            Name = "薬草採取",
            QuestType = QuestType.Gathering,
            Rank = QuestRank.E,
            Difficulty = 8,
            ScoutRequirement = 8,
            RewardGold = 60,
            DeadlineWeeks = 5,
            RecommendedMembers = 1,
        };

        [Fact]
        public void EarlyGame_FromFirstDispatchToSecondSquadSlot()
        {
            var (week, dispatch) = BuildSystems();

            var a = MakeVeteran();
            var b = MakeVeteran();
            var c = MakeVeteran();
            var d = MakeVeteran();
            var state = new GameState { Gold = 0 };
            foreach (var member in new[] { a, b, c, d })
                state.Adventurers.Add(member);

            // ---- Phase 1：同時出撃枠は1。1枠の中で1〜4名を自由に割り振れる ----
            Assert.Equal(1, state.UnlockedSquadSlots);
            Assert.True(QuestDispatchSystem.CanDispatch(state));

            // 1枠しかないので、2件目の派遣は枠が空くまで受け付けられない。
            var firstQuest = MakeGatheringQuest();
            state.AvailableQuests.Add(firstQuest);
            Assert.True(dispatch.TryDispatch(state, PartyOf(a), firstQuest));
            Assert.False(dispatch.TryDispatch(state, PartyOf(b), MakeGatheringQuest()),
                "同時出撃枠が1の間は2部隊目を派遣できないはず");

            // ---- 採取を繰り返して出撃回数と資金を稼ぐ（作業感を断ち切る前の助走） ----
            // 昇格試験は「条件を満たした週の決算」で提示されるため、どの週で提示されたかを拾っておく。
            WeeklySettlementResult? offerWeek = null;

            var firstWeek = week.ProcessWeek(state);
            if (firstWeek.OfferedPromotionExam != null) offerWeek = firstWeek;

            Assert.Empty(state.ActiveDispatches); // 採取は1週で解決し、枠が空く
            Assert.Equal(1, state.TotalDispatchCount);

            for (int i = 0; i < 2; i++)
            {
                var quest = MakeGatheringQuest();
                state.AvailableQuests.Add(quest);
                // 単独ではなく2名で行けば採取量が2倍になる（人数比例。→ 仕様「採取量の変動」）。
                Assert.True(dispatch.TryDispatch(state, PartyOf(a, b), quest));

                var settlement = week.ProcessWeek(state);
                if (settlement.OfferedPromotionExam != null) offerWeek = settlement;
            }

            Assert.Equal(3, state.TotalDispatchCount);
            Assert.True(state.Gold >= ProgressionBalance.PromotionExamMinGold,
                $"採取3回で昇格試験の資金条件({ProgressionBalance.PromotionExamMinGold}G)を満たすはず（実際: {state.Gold}G）");

            // ---- Phase 2：条件（出撃3回・資金100G）を満たした週の決算で昇格試験が提示される ----
            // ＝プレイ開始40〜60分・4〜5回目の出撃で第2部隊枠に手が届く導線になっている。
            Assert.NotNull(offerWeek);
            var examQuest = offerWeek!.OfferedPromotionExam!;
            Assert.True(examQuest.IsBoss);
            Assert.Contains(examQuest, state.AvailableQuests);
            Assert.True(offerWeek.Flags.GuildProgressionEventOccurred,
                "昇格試験の提示は自動スキップを止める重要イベントであるはず");

            // ---- Phase 3：決戦。推奨どおり4名の総力戦で挑む ----
            var progression = new GuildProgressionSystem(new RecruitmentSystem(new AlwaysMinRng()));
            Assert.Equal(GuildProgressionPhase.ExamOffered, progression.GetPhase(state));

            // 4名なら人数不足ペナルティを受けず、勝算のある編成になっている（→ 成功率予測）。
            var examParty = PartyOf(a, b, c, d);
            Assert.True(SuccessRateCalculator.GetConfidence(examParty, examQuest) >= SuccessConfidence.Favorable,
                "総力戦の編成なら「勝算はある」以上と判定されるはず");

            Assert.True(dispatch.TryDispatch(state, examParty, examQuest));
            Assert.Equal(GuildProgressionPhase.ExamInProgress, progression.GetPhase(state));

            int goldBeforeExam = state.Gold;
            var examWeek = week.ProcessWeek(state);

            // ---- Phase 4：突破 → ランクE・第2部隊枠・報奨金・新人4名 ----
            Assert.True(examWeek.DispatchResolutions.Single().Result.QuestAchieved, "総力戦なら昇格試験に勝てるはず");

            Assert.NotNull(examWeek.PromotionExamResult);
            var promotion = examWeek.PromotionExamResult!;

            Assert.Equal(GuildRank.E, state.GuildRank);
            Assert.Equal(2, state.UnlockedSquadSlots);
            Assert.Equal(2, promotion.UnlockedSquadSlots);
            Assert.Equal(GuildRank.E, promotion.NewRank);
            Assert.Equal(ProgressionBalance.PromotionExamNewHireCount, promotion.NewHireOffers.Count);
            Assert.True(state.Gold > goldBeforeExam, "クエスト報酬と昇格報奨金で資金が増えているはず");
            Assert.True(examWeek.Flags.GuildProgressionEventOccurred);

            Assert.Equal(GuildProgressionPhase.Expanded, progression.GetPhase(state));

            // ---- 第2部隊枠が実際に機能する（2部隊を同時に派遣できる） ----
            var questForSquad1 = MakeGatheringQuest();
            var questForSquad2 = MakeGatheringQuest();
            state.AvailableQuests.Add(questForSquad1);
            state.AvailableQuests.Add(questForSquad2);

            Assert.True(dispatch.TryDispatch(state, PartyOf(a, b), questForSquad1));
            Assert.True(dispatch.TryDispatch(state, PartyOf(c, d), questForSquad2),
                "枠が2に拡張された後は2部隊を同時に派遣できるはず");
            Assert.False(QuestDispatchSystem.CanDispatch(state), "2枠とも埋まったら3部隊目は不可");

            Assert.Equal(2, state.ActiveDispatches.Count);
        }

        [Fact]
        public void EarlyGame_NoAdventurerIsLostToLowDangerQuests()
        {
            // 序盤の「即詰み防止」：低危険度任務だけを回している限り、
            // どれだけ失敗してもロースターから人が消えることはない。
            var (week, dispatch) = BuildSystems();

            var rookie = new Adventurer { STR = 5, AGI = 5, VIT = 5, MND = 5, DEX = 5, LDR = 5, INT = 5, WeeklyWage = 0 };
            rookie.CurrentHP = rookie.MaxHP;
            var state = new GameState { Gold = 500, Adventurers = { rookie } };

            for (int i = 0; i < 6; i++)
            {
                // 明らかに実力不足な難易度の採取に繰り返し失敗させる。
                var hopeless = new Quest
                {
                    Name = "危険地帯の採取",
                    QuestType = QuestType.Gathering,
                    Difficulty = 90,
                    ScoutRequirement = 50,
                    RewardGold = 10,
                    DeadlineWeeks = 5,
                };
                state.AvailableQuests.Add(hopeless);

                if (QuestDispatchSystem.CanDispatch(state))
                    dispatch.TryDispatch(state, PartyOf(rookie), hopeless);

                week.ProcessWeek(state);
            }

            Assert.Contains(rookie, state.Adventurers);
            Assert.Empty(state.FallenAdventurers);
            Assert.True(rookie.CurrentHP >= 1);
        }
    }
}
