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

        private static QuestDispatchSystem BuildSystem(IRng rng) =>
            new QuestDispatchSystem(
                new QuestResolver(rng),
                new GrowthSystem(rng),
                new EconomySystem(),
                new SatisfactionSystem());

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
            var adventurer = new Adventurer { STR = 40, END = 40 };
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
            var adventurer = new Adventurer { STR = 40, END = 40 };
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
            var adventurer = new Adventurer { STR = 100, AGI = 100, END = 100, MAG = 100, LDR = 100, Satisfaction = 50 };
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
            var adventurer = new Adventurer { Age = 18, JobClass = JobClass.Warrior, STR = 40, PA_STR = 80, END = 50 };
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
    }
}
