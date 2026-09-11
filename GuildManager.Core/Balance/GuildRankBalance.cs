using System.Linq;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// ギルド格付け関連の定数・閾値テーブル（→ 03 §8.1・§8.1.1）。
    /// 現状は仮値（→ BAL: 格付け）。将来的に04バランス表からの外部化対象。
    /// </summary>
    public static class GuildRankBalance
    {
        /// <summary>ランクごとの昇格ライン・降格ライン（ヒステリシス）。</summary>
        public readonly record struct RankThreshold(GuildRank Rank, int PromoteAt, int DemoteAt);

        // 昇格ラインより降格ラインを低く設定することで、閾値付近で名声が小さく上下しても
        // ランク表示自体は頻繁に切り替わらないようにする（→ 03 §8.1）。
        // G はゲーム開始時点の最下位ランクのため PromoteAt は参照されない（0のまま）。
        private static readonly RankThreshold[] Thresholds =
        {
            new(GuildRank.G, PromoteAt: 0,    DemoteAt: 0),
            new(GuildRank.F, PromoteAt: 100,  DemoteAt: 60),
            new(GuildRank.E, PromoteAt: 250,  DemoteAt: 180),
            new(GuildRank.D, PromoteAt: 450,  DemoteAt: 350),
            new(GuildRank.C, PromoteAt: 700,  DemoteAt: 580),
            new(GuildRank.B, PromoteAt: 1000, DemoteAt: 850),
            new(GuildRank.A, PromoteAt: 1400, DemoteAt: 1200),
            new(GuildRank.S, PromoteAt: 1900, DemoteAt: 1650),
        };

        public static RankThreshold GetThreshold(GuildRank rank) => Thresholds.First(t => t.Rank == rank);

        /// <summary>1つ上のランク。既に最上位（S）なら null。</summary>
        public static GuildRank? NextRank(GuildRank rank) =>
            rank == GuildRank.S ? null : (GuildRank)((int)rank + 1);

        /// <summary>1つ下のランク。既に最下位（G）なら null。</summary>
        public static GuildRank? PreviousRank(GuildRank rank) =>
            rank == GuildRank.G ? null : (GuildRank)((int)rank - 1);

        /// <summary>
        /// ギルドランクを、クエストランク（QuestRank）と比較可能な等級に変換する（→ 03 §8.1「同ランク以上のクエスト」判定用の簡易実装）。
        /// QuestRankにはG・Fに相当する等級が無いため、G・FはいずれもQuestRank.Eとして扱う。
        /// </summary>
        public static QuestRank ToQuestRankFloor(GuildRank rank) => rank switch
        {
            GuildRank.G => QuestRank.E,
            GuildRank.F => QuestRank.E,
            GuildRank.E => QuestRank.E,
            GuildRank.D => QuestRank.D,
            GuildRank.C => QuestRank.C,
            GuildRank.B => QuestRank.B,
            GuildRank.A => QuestRank.A,
            GuildRank.S => QuestRank.S,
            _ => QuestRank.E,
        };

        /// <summary>クエスト達成時の名声加算量。→ BAL: 格付け/名声増減</summary>
        public const int ReputationGainOnAchievement = 20;

        /// <summary>クエスト失敗時の名声減算量。→ BAL: 格付け/名声増減</summary>
        public const int ReputationLossOnFailure = 15;

        /// <summary>
        /// 現ランク相当のクエストを達成できない状態がこの週数続くと、毎週わずかに名声が減少する
        /// （→ 03 §8.1.1）。§5.1「4週連続遠征なしで満足度-5」と同じ対象週数を踏襲した仮値。
        /// </summary>
        public const int WeeksWithoutAppropriateQuestThreshold = 4;

        /// <summary>名声自然減衰の週あたり減少量。→ BAL: 格付け/名声減衰</summary>
        public const int ReputationDecayPerWeek = 5;
    }
}
