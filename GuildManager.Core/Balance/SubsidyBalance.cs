using System.Collections.Generic;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 月次助成金（Subsidy）関連の暫定バランス値。仕様書 03 §8.1「助成金額」・§4.4 参照。
    ///
    /// 事前調査メモ：月次助成金そのものの実装（格付け連動の収入）はこれまで存在しなかった
    /// （docs/03 §8.1に「ランクは助成金額に影響する」という記述はあったが未接続）。
    /// 本改訂（脅威度75%超で50%カット）を意味あるものにするため、格付け連動の
    /// 基礎助成金額をあわせて新規導入した。金額は暫定値（→ BAL: 経済/助成金）。
    /// </summary>
    public static class SubsidyBalance
    {
        /// <summary>助成金は月次（4週に1回）支給する（→ 03 §1.2「1ヶ月＝4週」）。</summary>
        public const int WeeksPerMonth = 4;

        private static readonly Dictionary<GuildRank, int> BaseAmount = new()
        {
            [GuildRank.G] = 200,
            [GuildRank.F] = 300,
            [GuildRank.E] = 450,
            [GuildRank.D] = 650,
            [GuildRank.C] = 900,
            [GuildRank.B] = 1300,
            [GuildRank.A] = 1800,
            [GuildRank.S] = 2500,
        };

        public static int GetBaseAmount(GuildRank rank) => BaseAmount[rank];

        /// <summary>脅威度が閾値超過時のカット後倍率（50%カット）。→ SecurityBalance.SubsidyCutThreatThreshold</summary>
        public const double ThreatCutMultiplier = 0.5;
    }
}
