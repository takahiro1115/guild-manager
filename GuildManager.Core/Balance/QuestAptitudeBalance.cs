using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// クエスト適性ボーナス（→ 03 §4.2.1）関連の暫定バランス値。
    /// 05技術メモ§3の方針（数値を各Systemクラスへ直書きしない）に沿い、ここへ集約した。
    /// 04_バランス表.xlsx はまだコードから読み込めない（Phase 4で外部化予定）ため、
    /// 現状はすべて仮値の定数。
    /// </summary>
    public static class QuestAptitudeBalance
    {
        public const double MinMultiplier = 1.0; // ペナルティなし（ボーナスのみ）
        public const double MaxMultiplier = 1.3; // → BAL: 戦闘/適性倍率。現状は仮値

        /// <summary>
        /// 平均実効値がこの値に達すると倍率が最大(MaxMultiplier)になる（線形スケール）。
        /// 実効値の理論上限100に合わせた仮値。
        /// </summary>
        public const double ReferenceStatValue = 100.0;

        /// <summary>
        /// クエスト種別ごとの適性判定対象ステータス（→ 03 §4.2.1）。
        /// 討伐は7能力全体が対象＝既存の基礎CPとほぼ同じ考え方になるため、
        /// 適性ボーナスがほぼ乗らない標準クエストという位置づけになる（仕様どおり）。
        /// </summary>
        public static string[] GetAptitudeStats(QuestType type) => type switch
        {
            QuestType.Escort => new[] { "MND", "VIT" },
            QuestType.Exploration => new[] { "AGI", "DEX", "INT" },
            QuestType.Subjugation => new[] { "STR", "AGI", "VIT", "MND", "DEX", "LDR", "INT" },
            _ => new[] { "STR", "AGI", "VIT", "MND", "DEX", "LDR", "INT" },
        };

        /// <summary>
        /// 平均実効値から適性倍率を算出する（1.0〜1.3倍の範囲で線形スケール、→ BAL）。
        /// </summary>
        public static double GetMultiplier(double averageEffectiveStat)
        {
            double raw = MinMultiplier + (averageEffectiveStat / ReferenceStatValue) * (MaxMultiplier - MinMultiplier);
            return System.Math.Clamp(raw, MinMultiplier, MaxMultiplier);
        }
    }
}
