using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 受注可能クエスト一覧（GameState.AvailableQuests）の週次管理。仕様書 03 §4.0・§4.4 参照。
    ///
    /// 事前調査メモ：クエストの期限切れ検知・補充の仕組みはこれまで存在しなかった
    /// （AvailableQuestsはゲーム開始時に固定3件が設定されるのみで、一切変化しなかった）。
    /// 本クラスを新設し、毎週の決算処理で「受注されないまま残り週数が尽きたクエストを除去し、
    /// 常に一定件数（QuestBalance.DesiredAvailableCount）を維持するよう補充する」処理を行う。
    /// クエスト生成ロジックも未実装だったため、QuestBalance.Templatesから簡易に複製する
    /// 最小実装とした（→ 03 §4.0）。
    /// </summary>
    public class QuestBoardSystem
    {
        private readonly IRng _rng;

        public QuestBoardSystem(IRng rng)
        {
            _rng = rng;
        }

        /// <summary>
        /// 週次決算処理。受注可能一覧の各クエストの残り週数を1減らし、0以下になったものを
        /// 除去する（＝放置による期限切れ）。その後、常に一定件数を保つよう新規クエストで
        /// 補充する。期限切れになったクエストの一覧を返す（呼び出し側で討伐クエストの
        /// 放置ペナルティ判定・週報ログ表示に使う）。
        /// </summary>
        public List<Quest> ProcessWeeklyBoard(GameState state)
        {
            var expired = new List<Quest>();

            foreach (var quest in state.AvailableQuests.ToList())
            {
                quest.DeadlineWeeks--;
                if (quest.DeadlineWeeks > 0)
                    continue;

                state.AvailableQuests.Remove(quest);
                expired.Add(quest);
            }

            while (state.AvailableQuests.Count < QuestBalance.DesiredAvailableCount)
                state.AvailableQuests.Add(GenerateQuest());

            return expired;
        }

        private Quest GenerateQuest()
        {
            var templates = QuestBalance.Templates;
            var template = templates[_rng.NextInt(0, templates.Length - 1)];

            return new Quest
            {
                Name = template.Name,
                QuestType = template.Type,
                Rank = template.Rank,
                Difficulty = template.Difficulty,
                ScoutRequirement = template.ScoutRequirement,
                RewardGold = template.RewardGold,
                DeadlineWeeks = template.DeadlineWeeks,
            };
        }
    }
}
