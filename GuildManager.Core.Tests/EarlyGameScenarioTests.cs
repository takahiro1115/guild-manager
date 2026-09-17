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
    /// 序盤の通しシナリオテスト。
    ///
    /// 個々のシステム単体ではなく、実運用と同じ WeekProcessingSystem 経由で検証する。
    /// 旧仕様の「採取で資金を貯める → 昇格試験が提示される → 決戦に勝つ → 第2部隊枠が開く」
    /// という受託クエスト経由の昇格試験フローは、2026年9月の大迷宮一本化改訂で無効化した。
    /// ランク昇格・出撃枠拡張は大迷宮のボス撃破（→ DungeonExpeditionSystem。
    /// GuildManager.Core.Tests側の該当テストを参照）のみが唯一のトリガーになった。
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
        public void EarlyGame_PromotionExamIsNeverOffered_EvenAfterMeetingOldConditions()
        {
            var (week, dispatch) = BuildSystems();

            var a = MakeVeteran();
            var b = MakeVeteran();
            var state = new GameState { Gold = 0 };
            foreach (var member in new[] { a, b })
                state.Adventurers.Add(member);

            // ---- 同時出撃枠は1。1枠の中で1〜4名を自由に割り振れる ----
            Assert.Equal(1, state.UnlockedSquadSlots);
            Assert.True(QuestDispatchSystem.CanDispatch(state));

            // 1枠しかないので、2件目の派遣は枠が空くまで受け付けられない。
            var firstQuest = MakeGatheringQuest();
            state.AvailableQuests.Add(firstQuest);
            Assert.True(dispatch.TryDispatch(state, PartyOf(a), firstQuest));
            Assert.False(dispatch.TryDispatch(state, PartyOf(b), MakeGatheringQuest()),
                "同時出撃枠が1の間は2部隊目を派遣できないはず");

            var firstWeek = week.ProcessWeek(state);
            Assert.Null(firstWeek.OfferedPromotionExam); // 大迷宮一本化改訂で無効化済み

            Assert.Empty(state.ActiveDispatches); // 採取は1週で解決し、枠が空く
            Assert.Equal(1, state.TotalDispatchCount);

            // ---- 採取を繰り返し、旧・昇格試験の提示条件（出撃回数・資金）を満たす状態まで進める ----
            while (state.TotalDispatchCount < ProgressionBalance.PromotionExamMinDispatchCount
                   || state.Gold < ProgressionBalance.PromotionExamMinGold)
            {
                var quest = MakeGatheringQuest();
                state.AvailableQuests.Add(quest);
                Assert.True(dispatch.TryDispatch(state, PartyOf(a, b), quest));

                var settlement = week.ProcessWeek(state);
                Assert.Null(settlement.OfferedPromotionExam); // 条件を満たした週でも二度と提示されない
            }

            Assert.True(state.TotalDispatchCount >= ProgressionBalance.PromotionExamMinDispatchCount);
            Assert.True(state.Gold >= ProgressionBalance.PromotionExamMinGold);

            // ---- 旧・昇格試験の条件を満たした後も、受注可能一覧にボスクエストは一切現れない ----
            Assert.DoesNotContain(state.AvailableQuests, q => q.IsBoss);
            Assert.False(state.PromotionExamOffered);
            Assert.Equal(1, state.UnlockedSquadSlots); // 枠拡張は大迷宮ボス撃破のみが唯一のトリガー
            Assert.Equal(GuildRank.G, state.GuildRank); // 名声も昇格試験報奨も無いため初期ランクのまま
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
