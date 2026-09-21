using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 月次助成金（Subsidy）。仕様書 03 §8.1「助成金額」参照。
    ///
    /// 支給額はギルド格付け（GuildRank）のみで決まる（→ SubsidyBalance.GetBaseAmount）。
    /// かつて存在した「脅威度75%超で50%カット」の分岐は、脅威度システムの撤去（2026年9月）に
    /// 伴い廃止した。増減経路を失った脅威度は初期値0のまま固定されており、カット判定は
    /// 常に不成立＝死に分岐だったため、ランク連動の安定支給へ純化している。
    /// </summary>
    public class SubsidySystem
    {
        /// <summary>
        /// 週次決算処理。4週に1回（月次）のみ助成金を支給する。それ以外の週は何もせず null を返す。
        /// 支給した金額を返す（UI側の週報ログ表示用。支給しなかった週は null）。
        /// </summary>
        public int? ProcessWeeklySubsidy(GameState state)
        {
            if (state.WeekNumber % SubsidyBalance.WeeksPerMonth != 0)
                return null;

            int amount = SubsidyBalance.GetBaseAmount(state.GuildRank);
            state.Gold += amount;
            return amount;
        }
    }
}
