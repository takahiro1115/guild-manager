using System.Collections.Generic;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 複数週クエストの派遣・解決オーケストレーション（仕様書 03 §4.0.1）のテスト。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class QuestDispatchSystemTests
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

        /// <summary>呼び出し順に決め打ちの値を1つずつ返すテスト用スタブ（[min,max]にクランプ）。</summary>
        private class SequenceRng : IRng
        {
            private readonly Queue<int> _values;
            public SequenceRng(params int[] values) => _values = new Queue<int>(values);
            public int NextInt(int min, int max) =>
                System.Math.Clamp(_values.Count > 0 ? _values.Dequeue() : min, min, max);
        }

        private static QuestDispatchSystem BuildSystem(IRng rng) =>
            new QuestDispatchSystem(
                new QuestResolver(rng),
                new GrowthSystem(rng),
                new EconomySystem(),
                new SatisfactionSystem(),
                new CompatibilitySystem(rng));

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members)
                party.TryAdd(m);
            return party;
        }

        // ---------------- Dispatch：派遣開始 ----------------

        [Fact]
        public void Dispatch_MarksMembersAsDispatched()
        {
            var adventurer = new Adventurer();
            var party = PartyOf(adventurer);
            var quest = new Quest { Scale = QuestScale.Small };
            var state = new GameState();
            var system = BuildSystem(new AlwaysMinRng());

            system.Dispatch(state, party, quest);

            Assert.True(adventurer.IsDispatched);
            Assert.False(adventurer.IsAvailable);
        }

        [Fact]
        public void Dispatch_RemovesQuestFromAvailableQuests()
        {
            // 受注済みになったクエストは受注可能一覧から外す（→ 03 §4.0・§4.4）。
            // 外しておかないと、拘束期間中もQuestBoardSystemの期限切れ判定の対象になり続けてしまう。
            var quest = new Quest { Scale = QuestScale.Small };
            var party = PartyOf(new Adventurer());
            var state = new GameState { AvailableQuests = { quest } };
            var system = BuildSystem(new AlwaysMinRng());

            system.Dispatch(state, party, quest);

            Assert.DoesNotContain(quest, state.AvailableQuests);
        }

        [Fact]
        public void Dispatch_AddsToActiveDispatches_WithWeeksRemainingFromScale()
        {
            var party = PartyOf(new Adventurer());
            var quest = new Quest { Scale = QuestScale.Large }; // 4週
            var state = new GameState();
            var system = BuildSystem(new AlwaysMinRng());

            system.Dispatch(state, party, quest);

            var dispatch = Assert.Single(state.ActiveDispatches);
            Assert.Equal(4, dispatch.WeeksRemaining);
        }

        // ---------------- 解決タイミング ----------------

        [Fact]
        public void ProcessWeeklyDispatches_ResolvesSingleWeekQuest_OnFirstProcessing()
        {
            // Scale=Small（デフォルト）は1週。既存の単週クエストと同じ挙動（即週で結果が出る）。
            var adventurer = new Adventurer { STR = 40, VIT = 40 };
            adventurer.CurrentHP = adventurer.MaxHP;
            var party = PartyOf(adventurer);
            var quest = new Quest { Scale = QuestScale.Small, Difficulty = 10, ScoutRequirement = 1 };
            var state = new GameState();
            var system = BuildSystem(new AlwaysMinRng());

            system.Dispatch(state, party, quest);
            var resolutions = system.ProcessWeeklyDispatches(state);

            Assert.Single(resolutions);
            Assert.Empty(state.ActiveDispatches);
            Assert.False(adventurer.IsDispatched);
        }

        [Fact]
        public void ProcessWeeklyDispatches_DoesNotResolveMultiWeekQuest_BeforeFinalWeek()
        {
            var adventurer = new Adventurer();
            var party = PartyOf(adventurer);
            var quest = new Quest { Scale = QuestScale.Medium, Difficulty = 10, ScoutRequirement = 1 }; // 2週
            var state = new GameState();
            var system = BuildSystem(new AlwaysMinRng());

            system.Dispatch(state, party, quest);
            var week1 = system.ProcessWeeklyDispatches(state);

            Assert.Empty(week1); // まだ派遣中（移動中）：解決しない
            Assert.Single(state.ActiveDispatches);
            Assert.True(adventurer.IsDispatched); // 派遣中のまま
        }

        [Fact]
        public void ProcessWeeklyDispatches_ResolvesMultiWeekQuest_OnFinalWeek()
        {
            var adventurer = new Adventurer { STR = 40, VIT = 40 };
            adventurer.CurrentHP = adventurer.MaxHP;
            var party = PartyOf(adventurer);
            var quest = new Quest { Scale = QuestScale.Medium, Difficulty = 10, ScoutRequirement = 1 }; // 2週
            var state = new GameState();
            var system = BuildSystem(new AlwaysMinRng());

            system.Dispatch(state, party, quest);
            system.ProcessWeeklyDispatches(state); // 1週目：まだ
            var week2 = system.ProcessWeeklyDispatches(state); // 2週目：満了

            Assert.Single(week2);
            Assert.Empty(state.ActiveDispatches);
            Assert.False(adventurer.IsDispatched);
        }

        [Fact]
        public void ProcessWeeklyDispatches_AppliesRewardAndSatisfactionBonus_OnlyAtResolution()
        {
            var adventurer = new Adventurer { STR = 100, AGI = 100, VIT = 100, MND = 100, LDR = 100, Satisfaction = 50 };
            adventurer.CurrentHP = adventurer.MaxHP;
            var party = PartyOf(adventurer);
            // 圧倒的な戦力差で確実に完全勝利（Bランク以上のクエスト達成）にする。
            var quest = new Quest { Scale = QuestScale.Small, Rank = QuestRank.S, Difficulty = 1, ScoutRequirement = 1, RewardGold = 500 };
            var state = new GameState { Gold = 1000 };
            var system = BuildSystem(new AlwaysMinRng());

            system.Dispatch(state, party, quest);
            var resolutions = system.ProcessWeeklyDispatches(state);

            Assert.True(resolutions[0].Result.QuestAchieved);
            Assert.Equal(1000 + 500, state.Gold); // 報酬が加算されている
            Assert.True(adventurer.Satisfaction > 50); // 勝利・功績ボーナスが乗っている
        }

        [Fact]
        public void ProcessWeeklyDispatches_AppliesGrowthOnlyOnce_ForMultiWeekQuest()
        {
            // 成長ロール経路1（出撃による成長）が、拘束期間中の各週で複数回発生せず、
            // 満了週の解決時にのみ1回だけ呼ばれることを確認する（→ 03 §4.0.1）。
            var adventurer = new Adventurer { Age = 18, JobClass = JobClass.Warrior, STR = 40, PA_STR = 80, VIT = 50 };
            adventurer.CurrentHP = adventurer.MaxHP;
            var party = PartyOf(adventurer);
            var quest = new Quest { Scale = QuestScale.Large, Difficulty = 10, ScoutRequirement = 1 }; // 4週
            var state = new GameState();
            // AlwaysMinRngは成長ロールの閾値を必ず満たす（roll=min<=どの年齢帯の閾値以上）ため、
            // 満了週に1回だけ成長が発生するはず。
            var system = BuildSystem(new AlwaysMinRng());

            system.Dispatch(state, party, quest);

            var week1 = system.ProcessWeeklyDispatches(state);
            var week2 = system.ProcessWeeklyDispatches(state);
            var week3 = system.ProcessWeeklyDispatches(state);
            var week4 = system.ProcessWeeklyDispatches(state);

            Assert.Empty(week1);
            Assert.Empty(week2);
            Assert.Empty(week3);
            var resolution = Assert.Single(week4);
            Assert.Single(resolution.GrowthEvents); // 4週分ではなく1回分だけ成長イベントが記録される
            Assert.Equal(41, adventurer.STR); // +1が1回だけ適用された（4回分なら44になってしまうはず）
        }

        [Fact]
        public void ProcessWeeklyDispatches_ReturnsEmptyList_WhenNoDispatchesExist()
        {
            var state = new GameState();
            var system = BuildSystem(new AlwaysMinRng());

            var resolutions = system.ProcessWeeklyDispatches(state);

            Assert.Empty(resolutions);
        }

        // ---------------- 戦死処理（→ 03 §4.3.1） ----------------

        [Fact]
        public void ProcessWeeklyDispatches_OnDeath_RemovesFromRosterAndRecordsFallen_AndPenalizesSurvivors()
        {
            // リーダー(高VIT)は不可逆障害の判定域に収まり生存、weakling(低VIT)は戦死する
            // ようにAlwaysMaxRngで固定する（致死判定ロールが常に最大値になるため）。
            // VIT以外は低めに抑え、クエスト適性ボーナス（→ 03 §4.2.1）でRatioが0.6以上
            // （戦線崩壊(Rout)ではなく苦戦敗退(Defeat)、HP0に至らない）に押し上がらないようにする。
            var leader = new Adventurer { Name = "リーダー", JobClass = JobClass.Warrior, STR = 1, AGI = 1, VIT = 100, MND = 1, DEX = 1, LDR = 1, Satisfaction = 80 };
            leader.CurrentHP = leader.MaxHP;
            var weakling = new Adventurer { Name = "weakling", STR = 1, AGI = 1, VIT = 1, MND = 1, DEX = 1, LDR = 1 };
            weakling.CurrentHP = weakling.MaxHP;
            var party = PartyOf(leader, weakling);
            var quest = new Quest { Scale = QuestScale.Small, Difficulty = 100, ScoutRequirement = 1 };
            var state = new GameState { Adventurers = { leader, weakling } };
            var system = BuildSystem(new AlwaysMaxRng());

            system.Dispatch(state, party, quest);
            var resolutions = system.ProcessWeeklyDispatches(state);

            var resolution = Assert.Single(resolutions);
            Assert.Contains(weakling.Id, resolution.Result.FallenAdventurerIds);
            Assert.DoesNotContain(leader.Id, resolution.Result.FallenAdventurerIds);

            // ロースターから除外され、戦死者記録へ移されている
            Assert.DoesNotContain(weakling, state.Adventurers);
            Assert.Contains(weakling, state.FallenAdventurers);
            Assert.Equal(1, weakling.FellAtWeek);

            // 生存者（リーダー）は現役のまま、仲間ロストの余波で満足度-30
            Assert.Contains(leader, state.Adventurers);
            Assert.Equal(50, leader.Satisfaction); // 80-30
        }

        [Fact]
        public void ProcessWeeklyDispatches_ExcludesFallenAdventurer_FromGrowthEventReport()
        {
            // 戦死した者に成長イベントが生じても、週報に報告される GrowthEvents には含めない
            // （戦死報告の直前に成長報告が出るのは不自然なため。→ QuestDispatchSystem独自の対応）。
            var adventurer = new Adventurer { Age = 18, JobClass = JobClass.Warrior, STR = 1, AGI = 1, VIT = 1, MND = 1, DEX = 1, LDR = 1 };
            adventurer.CurrentHP = adventurer.MaxHP;
            var party = PartyOf(adventurer);
            var quest = new Quest { Scale = QuestScale.Small, Difficulty = 100, ScoutRequirement = 1 };
            var state = new GameState { Adventurers = { adventurer } };
            // [索敵ロール, HP消費%(強制上限), 致死判定ロール(戦死), 成長ロール(成功), 職業重み抽選, 成長量]
            var system = BuildSystem(new SequenceRng(50, 1000, 100, 1, 1, 1));

            system.Dispatch(state, party, quest);
            var resolutions = system.ProcessWeeklyDispatches(state);

            var resolution = Assert.Single(resolutions);
            Assert.Contains(adventurer.Id, resolution.Result.FallenAdventurerIds);
            Assert.Empty(resolution.GrowthEvents); // 戦死者の成長は報告されない
        }
    }
}
