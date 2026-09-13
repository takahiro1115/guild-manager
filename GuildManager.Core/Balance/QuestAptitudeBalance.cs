using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// クエスト適性ボーナス（→ 03 §4.2.1）関連のバランス値。
    /// 値は docs/04_バランス表/combat.csv から読み込む（→ 03 §10.1、項目58）。
    /// </summary>
    public static class QuestAptitudeBalance
    {
        private const string FileName = "combat.csv";

        public static readonly double MinMultiplier = BalanceData.GetDouble(FileName, "AptitudeMinMultiplier"); // ペナルティなし（ボーナスのみ）
        public static readonly double MaxMultiplier = BalanceData.GetDouble(FileName, "AptitudeMaxMultiplier"); // → BAL: 戦闘/適性倍率

        /// <summary>
        /// 平均実効値がこの値に達すると倍率が最大(MaxMultiplier)になる（線形スケール）。
        /// 実効値の理論上限100に合わせた値。
        /// </summary>
        public static readonly double ReferenceStatValue = BalanceData.GetDouble(FileName, "AptitudeReferenceStatValue");

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
        /// 平均実効値から適性倍率を算出する（線形スケール、→ BAL）。
        /// </summary>
        public static double GetMultiplier(double averageEffectiveStat)
        {
            double raw = MinMultiplier + (averageEffectiveStat / ReferenceStatValue) * (MaxMultiplier - MinMultiplier);
            return System.Math.Clamp(raw, MinMultiplier, MaxMultiplier);
        }
    }
}
