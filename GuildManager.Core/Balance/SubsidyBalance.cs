using System.Collections.Generic;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 月次助成金（Subsidy）関連のバランス値。仕様書 03 §8.1「助成金額」・§4.4 参照。
    /// 値は docs/04_バランス表/economy.csv から読み込む（→ 03 §10.1、項目58）。
    ///
    /// WeeksPerMonth（1ヶ月＝4週）は構造値のため、economy.csvにも参考として同じ値が
    /// 記載されているが（SubsidyWeeksPerMonth、note欄に「構造値のため通常は変更しない」と
    /// 明記）、CSVからは読まずコード側の定数のまま据え置く（→ docs/04_バランス表/README.md
    /// 「CSV化していない値」）。
    /// </summary>
    public static class SubsidyBalance
    {
        private const string FileName = "economy.csv";

        /// <summary>助成金は月次（4週に1回）支給する（→ 03 §1.2「1ヶ月＝4週」）。構造値のため据え置き。</summary>
        public const int WeeksPerMonth = 4;

        private static readonly Dictionary<GuildRank, int> BaseAmount = new()
        {
            [GuildRank.G] = BalanceData.GetInt(FileName, "SubsidyBaseAmount_G"),
            [GuildRank.F] = BalanceData.GetInt(FileName, "SubsidyBaseAmount_F"),
            [GuildRank.E] = BalanceData.GetInt(FileName, "SubsidyBaseAmount_E"),
            [GuildRank.D] = BalanceData.GetInt(FileName, "SubsidyBaseAmount_D"),
            [GuildRank.C] = BalanceData.GetInt(FileName, "SubsidyBaseAmount_C"),
            [GuildRank.B] = BalanceData.GetInt(FileName, "SubsidyBaseAmount_B"),
            [GuildRank.A] = BalanceData.GetInt(FileName, "SubsidyBaseAmount_A"),
            [GuildRank.S] = BalanceData.GetInt(FileName, "SubsidyBaseAmount_S"),
        };

        public static int GetBaseAmount(GuildRank rank) => BaseAmount[rank];

        /// <summary>脅威度が閾値超過時のカット後倍率（50%カット）。→ SecurityBalance.SubsidyCutThreatThreshold</summary>
        public static readonly double ThreatCutMultiplier = BalanceData.GetDouble(FileName, "SubsidyThreatCutMultiplier");
    }
}
