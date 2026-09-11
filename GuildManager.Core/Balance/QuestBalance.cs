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
    }
}
