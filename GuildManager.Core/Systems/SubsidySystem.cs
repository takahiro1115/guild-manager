using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 月次助成金（Subsidy）。仕様書 03 §8.1「助成金額」・§4.4 参照。
    ///
    /// 事前調査メモ：月次助成金そのものの実装（格付け連動の収入）はこれまで存在しなかった
    /// （→ SubsidyBalanceのコメント参照）。脅威度75%超での50%カットを意味あるものにするため、
    /// 本クラスで格付け連動の基礎助成金支給もあわせて新規実装した。
    /// </summary>
    public class SubsidySystem
    {
        /// <summary>
        /// 週次決算処理。4週に1回（月次）のみ助成金を支給する。それ以外の週は何もせず null を返す。
        /// 脅威度が SecurityBalance.SubsidyCutThreatThreshold を超えていれば50%カットする。
        /// 支給した金額を返す（UI側の週報ログ表示用。支給しなかった週は null）。
        /// </summary>
        public int? ProcessWeeklySubsidy(GameState state)
        {
            if (state.WeekNumber % SubsidyBalance.WeeksPerMonth != 0)
                return null;

            int baseAmount = SubsidyBalance.GetBaseAmount(state.GuildRank);
            int amount = state.ThreatLevel > SecurityBalance.SubsidyCutThreatThreshold
                ? (int)(baseAmount * SubsidyBalance.ThreatCutMultiplier)
                : baseAmount;

            state.Gold += amount;
            return amount;
        }
    }
}
