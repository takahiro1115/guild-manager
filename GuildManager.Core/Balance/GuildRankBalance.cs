using System;
using System.Linq;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// ギルド格付け関連の定数・閾値テーブル（→ 03 §8.1・§8.1.1）。
    /// 値は docs/04_バランス表/guild_rank.csv（ランク別閾値テーブル）・
    /// guild_rank_params.csv（key,value形式）から読み込む（→ 03 §10.1、項目58）。
    /// </summary>
    public static class GuildRankBalance
    {
        private const string ThresholdsFileName = "guild_rank.csv";
        private const string ParamsFileName = "guild_rank_params.csv";

        /// <summary>ランクごとの昇格ライン・降格ライン（ヒステリシス）。</summary>
        public readonly record struct RankThreshold(GuildRank Rank, int PromoteAt, int DemoteAt);

        // 昇格ラインより降格ラインを低く設定することで、閾値付近で名声が小さく上下しても
        // ランク表示自体は頻繁に切り替わらないようにする（→ 03 §8.1）。
        // G はゲーム開始時点の最下位ランクのため PromoteAt は参照されない（0のまま）。
        private static readonly RankThreshold[] Thresholds = BuildThresholds();

        private static RankThreshold[] BuildThresholds()
        {
            var (header, rows) = BalanceData.GetTable(ThresholdsFileName);
            var result = new RankThreshold[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (!Enum.TryParse<GuildRank>(row[0], out var rank))
                    throw new BalanceDataException($"{ThresholdsFileName} のrank列「{row[0]}」をGuildRankとして解釈できません。");
                if (!int.TryParse(row[1], out int promoteAt))
                    throw new BalanceDataException($"{ThresholdsFileName} の{row[0]}行のpromote_at「{row[1]}」を整数として解釈できません。");
                if (!int.TryParse(row[2], out int demoteAt))
                    throw new BalanceDataException($"{ThresholdsFileName} の{row[0]}行のdemote_at「{row[2]}」を整数として解釈できません。");

                result[i] = new RankThreshold(rank, promoteAt, demoteAt);
            }
            return result;
        }

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
        public static readonly int ReputationGainOnAchievement = BalanceData.GetInt(ParamsFileName, "ReputationGainOnAchievement");

        /// <summary>クエスト失敗時の名声減算量。→ BAL: 格付け/名声増減</summary>
        public static readonly int ReputationLossOnFailure = BalanceData.GetInt(ParamsFileName, "ReputationLossOnFailure");

        /// <summary>
        /// 現ランク相当のクエストを達成できない状態がこの週数続くと、毎週わずかに名声が減少する
        /// （→ 03 §8.1.1）。§5.1「4週連続遠征なしで満足度-5」と同じ対象週数を踏襲した値。
        /// </summary>
        public static readonly int WeeksWithoutAppropriateQuestThreshold = BalanceData.GetInt(ParamsFileName, "WeeksWithoutAppropriateQuestThreshold");

        /// <summary>名声自然減衰の週あたり減少量。→ BAL: 格付け/名声減衰</summary>
        public static readonly int ReputationDecayPerWeek = BalanceData.GetInt(ParamsFileName, "ReputationDecayPerWeek");
    }
}
