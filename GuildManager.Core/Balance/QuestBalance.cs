using System.Collections.Generic;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// クエスト関連の暫定バランス値。仕様書 03 §4.0 参照。
    /// 05技術メモ§3の方針（数値を各Systemクラスへ直書きしない）に沿い、ここへ集約した。
    /// 04_バランス表.xlsx からの読み込みへの置き換え（Phase 4での外部化）はまだ行っておらず、
    /// 現状はすべて仮値の定数。
    /// </summary>
    public static class QuestBalance
    {
        /// <summary>
        /// 探索規模から拘束週数を導出する。→ BAL: クエスト/探索規模。
        /// 現状は各規模の代表値（幅ではなく単一の仮値）：小=1週、中=2週、大=4週。
        /// </summary>
        public static int GetDurationWeeks(QuestScale scale) => scale switch
        {
            QuestScale.Small => 1,
            QuestScale.Medium => 2,
            QuestScale.Large => 4,
            _ => 1,
        };

        /// <summary>受注可能クエスト一覧が常に維持すべき件数。→ BAL: クエスト/同時掲示数</summary>
        public const int DesiredAvailableCount = 3;

        /// <summary>
        /// クエスト補充用のテンプレート。仕様書03 §4.0「事前調査」時点でクエスト生成ロジックが
        /// 未実装だったため、QuestBoardSystemが受注可能一覧を補充する際にここからランダムに
        /// 1件選んで複製する（SampleData.CreateStarterQuests相当の簡易な値の組み合わせ。
        /// 各値は仮値、→ BAL: クエスト）。
        /// </summary>
        public static readonly (string Name, QuestType Type, QuestRank Rank, int Difficulty, int ScoutRequirement, int RewardGold, int DeadlineWeeks)[] Templates =
        {
            ("ゴブリン討伐", QuestType.Subjugation, QuestRank.E, 10, 10, 90, 3),
            ("山道の盗賊退治", QuestType.Subjugation, QuestRank.D, 22, 18, 180, 3),
            ("野盗のアジト掃討", QuestType.Subjugation, QuestRank.D, 25, 15, 200, 3),
            ("廃坑の魔物調査", QuestType.Exploration, QuestRank.C, 35, 30, 320, 4),
            ("古代遺跡の調査", QuestType.Exploration, QuestRank.C, 30, 35, 300, 4),
            ("商隊の護衛", QuestType.Escort, QuestRank.D, 20, 20, 220, 3),
        };
    }
}
