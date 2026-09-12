using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 治安・脅威度（ThreatLevel）システムのテスト（仕様書 03 §4.4）。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class SecuritySystemTests
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

        // ---------------- ApplyQuestResolution：討伐クエストのみが対象 ----------------

        [Fact]
        public void ApplyQuestResolution_IncreasesThreat_OnSubjugationFailure()
        {
            var state = new GameState { ThreatLevel = 50 };
            var quest = new Quest { QuestType = QuestType.Subjugation };
            var system = new SecuritySystem(new AlwaysMinRng());

            int delta = system.ApplyQuestResolution(state, quest, achieved: false);

            Assert.Equal(SecurityBalance.ThreatIncreaseMin, delta);
            Assert.Equal(50 + SecurityBalance.ThreatIncreaseMin, state.ThreatLevel);
        }

        [Fact]
        public void ApplyQuestResolution_DecreasesThreat_OnSubjugationAchievement()
        {
            var state = new GameState { ThreatLevel = 50 };
            var quest = new Quest { QuestType = QuestType.Subjugation };
            var system = new SecuritySystem(new AlwaysMinRng());

            int delta = system.ApplyQuestResolution(state, quest, achieved: true);

            Assert.Equal(-SecurityBalance.ThreatDecreaseMin, delta);
            Assert.Equal(50 - SecurityBalance.ThreatDecreaseMin, state.ThreatLevel);
        }

        [Theory]
        [InlineData(QuestType.Exploration)]
        [InlineData(QuestType.Escort)]
        public void ApplyQuestResolution_DoesNothing_ForNonSubjugationQuests(QuestType type)
        {
            var state = new GameState { ThreatLevel = 50 };
            var quest = new Quest { QuestType = type };
            var system = new SecuritySystem(new AlwaysMaxRng());

            int delta = system.ApplyQuestResolution(state, quest, achieved: false);

            Assert.Equal(0, delta);
            Assert.Equal(50, state.ThreatLevel);
        }

        // ---------------- ApplyAbandonedQuest：放置（期限切れ） ----------------

        [Fact]
        public void ApplyAbandonedQuest_IncreasesThreat_ForSubjugation()
        {
            var state = new GameState { ThreatLevel = 10 };
            var quest = new Quest { QuestType = QuestType.Subjugation };
            var system = new SecuritySystem(new AlwaysMaxRng());

            int delta = system.ApplyAbandonedQuest(state, quest);

            Assert.Equal(SecurityBalance.ThreatIncreaseMax, delta);
            Assert.Equal(10 + SecurityBalance.ThreatIncreaseMax, state.ThreatLevel);
        }

        [Fact]
        public void ApplyAbandonedQuest_DoesNothing_ForExploration()
        {
            var state = new GameState { ThreatLevel = 10 };
            var quest = new Quest { QuestType = QuestType.Exploration };
            var system = new SecuritySystem(new AlwaysMaxRng());

            int delta = system.ApplyAbandonedQuest(state, quest);

            Assert.Equal(0, delta);
            Assert.Equal(10, state.ThreatLevel);
        }

        // ---------------- クランプ ----------------

        [Fact]
        public void ApplyQuestResolution_ClampsAtMaxThreatLevel()
        {
            var state = new GameState { ThreatLevel = 99 };
            var quest = new Quest { QuestType = QuestType.Subjugation };
            var system = new SecuritySystem(new AlwaysMaxRng());

            int delta = system.ApplyQuestResolution(state, quest, achieved: false);

            Assert.Equal(100, state.ThreatLevel);
            Assert.Equal(1, delta); // 99→100の実際の増分（クランプで削られた分は含まれない）
        }

        [Fact]
        public void ApplyQuestResolution_ClampsAtMinThreatLevel()
        {
            var state = new GameState { ThreatLevel = 5 };
            var quest = new Quest { QuestType = QuestType.Subjugation };
            var system = new SecuritySystem(new AlwaysMaxRng());

            system.ApplyQuestResolution(state, quest, achieved: true);

            Assert.Equal(0, state.ThreatLevel); // 0未満にはならない
        }
    }
}
