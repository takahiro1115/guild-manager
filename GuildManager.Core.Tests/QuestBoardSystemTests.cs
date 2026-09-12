using System.Linq;
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

        // ---------------- 長期遠征クエストの解禁条件（→ 03 §4.0、v1.10改訂） ----------------

        [Fact]
        public void GenerateQuest_NeverProducesMediumOrLarge_WhenRosterBelowThreshold()
        {
            var state = new GameState(); // 初期ロースターは空（0名 < 6名）
            var system = new QuestBoardSystem(new SeededRng(123));

            for (int i = 0; i < 100; i++)
            {
                state.AvailableQuests.Clear();
                system.ProcessWeeklyBoard(state);
                Assert.All(state.AvailableQuests, q => Assert.Equal(QuestScale.Small, q.Scale));
            }
        }

        [Fact]
        public void GenerateQuest_CanProduceMediumOrLarge_WhenRosterAtThreshold()
        {
            var state = new GameState();
            for (int i = 0; i < QuestBalance.LongExpeditionRosterThreshold; i++)
                state.Adventurers.Add(new Adventurer());
            var system = new QuestBoardSystem(new SeededRng(123));

            bool sawNonSmall = false;
            for (int i = 0; i < 200 && !sawNonSmall; i++)
            {
                state.AvailableQuests.Clear();
                system.ProcessWeeklyBoard(state);
                if (state.AvailableQuests.Any(q => q.Scale != QuestScale.Small))
                    sawNonSmall = true;
            }

            Assert.True(sawNonSmall, "現役6名以上なら、中・大規模クエストがいずれ生成されるはず");
        }

        [Fact]
        public void GenerateQuest_RosterOneBelowThreshold_StillExcludesMediumAndLarge()
        {
            var state = new GameState();
            for (int i = 0; i < QuestBalance.LongExpeditionRosterThreshold - 1; i++)
                state.Adventurers.Add(new Adventurer());
            var system = new QuestBoardSystem(new SeededRng(123));

            for (int i = 0; i < 100; i++)
            {
                state.AvailableQuests.Clear();
                system.ProcessWeeklyBoard(state);
                Assert.All(state.AvailableQuests, q => Assert.Equal(QuestScale.Small, q.Scale));
            }
        }

        // ---------------- 期限切れ「1週前」の検出（→ 03 §1.3・自動スキップ停止条件8） ----------------

        [Fact]
        public void GetQuestsExpiringNextWeek_ReturnsSubjugationQuest_WithOneWeekRemaining()
        {
            var quest = new Quest { QuestType = QuestType.Subjugation, DeadlineWeeks = 1 };
            var state = new GameState { AvailableQuests = { quest } };

            var result = QuestBoardSystem.GetQuestsExpiringNextWeek(state).ToList();

            Assert.Contains(quest, result);
        }

        [Fact]
        public void GetQuestsExpiringNextWeek_ExcludesNonSubjugationQuest()
        {
            var quest = new Quest { QuestType = QuestType.Exploration, DeadlineWeeks = 1 };
            var state = new GameState { AvailableQuests = { quest } };

            var result = QuestBoardSystem.GetQuestsExpiringNextWeek(state).ToList();

            Assert.DoesNotContain(quest, result);
        }

        [Fact]
        public void GetQuestsExpiringNextWeek_ExcludesQuest_WithMoreThanOneWeekRemaining()
        {
            var quest = new Quest { QuestType = QuestType.Subjugation, DeadlineWeeks = 2 };
            var state = new GameState { AvailableQuests = { quest } };

            var result = QuestBoardSystem.GetQuestsExpiringNextWeek(state).ToList();

            Assert.DoesNotContain(quest, result);
        }

        [Fact]
        public void ProcessWeeklyBoard_ThenGetQuestsExpiringNextWeek_FiresExactlyOnce_ForItsLifetime()
        {
            // ある討伐クエストが「残り1週」の状態になるのは、その寿命の中でちょうど1週だけ
            // （翌週には除去される）。DeadlineWeeks=2で生成し、2回ProcessWeeklyBoardを呼ぶと、
            // 1回目の直後だけ検出され、2回目の直後（期限切れ後）には既に一覧から消えている
            // ことを確認する。
            var quest = new Quest { QuestType = QuestType.Subjugation, DeadlineWeeks = 2 };
            var state = new GameState { AvailableQuests = { quest } };
            var system = new QuestBoardSystem(new AlwaysMinRng());

            system.ProcessWeeklyBoard(state); // DeadlineWeeks: 2→1
            var afterFirstWeek = QuestBoardSystem.GetQuestsExpiringNextWeek(state).ToList();
            Assert.Contains(quest, afterFirstWeek);

            system.ProcessWeeklyBoard(state); // DeadlineWeeks: 1→0、期限切れで除去される
            var afterSecondWeek = QuestBoardSystem.GetQuestsExpiringNextWeek(state).ToList();
            Assert.DoesNotContain(quest, afterSecondWeek); // 既に一覧から消えているため対象外
        }

        // ---------------- クエスト期限の暫定レンジ（→ 03 §4.0、v1.10改訂） ----------------

        [Fact]
        public void AllTemplates_HaveDeadlineWeeksWithinConfiguredRange()
        {
            Assert.All(QuestBalance.Templates, t =>
            {
                Assert.InRange(t.DeadlineWeeks, QuestBalance.MinDeadlineWeeks, QuestBalance.MaxDeadlineWeeks);
            });
        }

        [Fact]
        public void StarterQuests_HaveDeadlineWeeksWithinConfiguredRange()
        {
            var quests = GuildManager.Core.Data.SampleData.CreateStarterQuests();

            Assert.All(quests, q =>
            {
                Assert.InRange(q.DeadlineWeeks, QuestBalance.MinDeadlineWeeks, QuestBalance.MaxDeadlineWeeks);
            });
        }

        [Fact]
        public void Templates_ContainAtLeastOneMediumAndOneLargeScale()
        {
            // 中・大規模の解禁制御自体が意味を持つよう、対象となるテンプレートが
            // 実在することを確認する。
            Assert.Contains(QuestBalance.Templates, t => t.Scale == QuestScale.Medium);
            Assert.Contains(QuestBalance.Templates, t => t.Scale == QuestScale.Large);
        }
    }
}
