using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 受注可能クエスト一覧の週次管理（期限切れ・補充）のテスト（仕様書 03 §4.0・§4.4）。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class QuestBoardSystemTests
    {
        /// <summary>NextInt(min, max) が常に min を返すテスト用スタブ。</summary>
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        [Fact]
        public void ProcessWeeklyBoard_DecrementsDeadlineWeeks_WithoutExpiring_WhenStillPositive()
        {
            var quest = new Quest { DeadlineWeeks = 3 };
            var state = new GameState { AvailableQuests = { quest } };
            var system = new QuestBoardSystem(new AlwaysMinRng());

            var expired = system.ProcessWeeklyBoard(state);

            Assert.Equal(2, quest.DeadlineWeeks);
            Assert.Empty(expired);
            Assert.Contains(quest, state.AvailableQuests);
        }

        [Fact]
        public void ProcessWeeklyBoard_ExpiresQuest_WhenDeadlineReachesZero()
        {
            var quest = new Quest { DeadlineWeeks = 1 };
            var state = new GameState { AvailableQuests = { quest } };
            var system = new QuestBoardSystem(new AlwaysMinRng());

            var expired = system.ProcessWeeklyBoard(state);

            Assert.Contains(quest, expired);
            Assert.DoesNotContain(quest, state.AvailableQuests);
        }

        [Fact]
        public void ProcessWeeklyBoard_ReplenishesUpToDesiredCount()
        {
            var state = new GameState(); // AvailableQuests は空
            var system = new QuestBoardSystem(new AlwaysMinRng());

            system.ProcessWeeklyBoard(state);

            Assert.Equal(QuestBalance.DesiredAvailableCount, state.AvailableQuests.Count);
        }

        [Fact]
        public void ProcessWeeklyBoard_DoesNotReplenish_WhenAlreadyAtDesiredCount()
        {
            var state = new GameState();
            for (int i = 0; i < QuestBalance.DesiredAvailableCount; i++)
                state.AvailableQuests.Add(new Quest { DeadlineWeeks = 10 });
            var system = new QuestBoardSystem(new AlwaysMinRng());

            system.ProcessWeeklyBoard(state);

            Assert.Equal(QuestBalance.DesiredAvailableCount, state.AvailableQuests.Count);
        }

        [Fact]
        public void ProcessWeeklyBoard_NewlyGeneratedQuests_DoNotDecrementInTheSameCall()
        {
            // 補充で新規追加されたクエストは、その週のうちにデクリメント対象にならない
            // （foreachはループ開始前のToListスナップショットを走査するため）。
            var state = new GameState();
            var system = new QuestBoardSystem(new AlwaysMinRng());

            system.ProcessWeeklyBoard(state);

            Assert.All(state.AvailableQuests, q => Assert.True(q.DeadlineWeeks > 0));
        }
    }
}
