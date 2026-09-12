using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 敗北条件の判定。仕様書 03 §8.3 参照。
    ///
    /// - 破産：所持金マイナスが4週連続で解消されない（4週間の猶予あり）。
    /// - 治安崩壊：脅威度が100%に到達した週の決算時点で、猶予なく即時敗北
    ///   （破産のような連続週数のカウントは行わない）。
    ///
    /// 事前調査メモ：敗北条件そのものの実装はこれまで存在しなかった
    /// （docs/03 §8.3に記述はあったが未接続。docs/06タスクリストでも
    /// 「治安（脅威度）と敗北条件」は本改訂まで未着手だった）。
    /// </summary>
    public class DefeatSystem
    {
        /// <summary>
        /// 週次決算処理の最後に1回呼ぶ。既に敗北が確定していれば何もしない
        /// （一度確定した敗北理由を上書きしない）。
        /// </summary>
        /// <returns>この週に新たに敗北が確定した場合はその理由。それ以外はnull。</returns>
        public DefeatReason? ProcessWeeklySettlement(GameState state)
        {
            if (state.DefeatReason != null)
                return null; // 既に敗北済み：何もしない

            if (state.Gold < 0)
                state.ConsecutiveNegativeGoldWeeks++;
            else
                state.ConsecutiveNegativeGoldWeeks = 0;

            if (state.ConsecutiveNegativeGoldWeeks >= SecurityBalance.BankruptcyConsecutiveWeeksThreshold)
            {
                state.DefeatReason = DefeatReason.Bankruptcy;
                return DefeatReason.Bankruptcy;
            }

            if (state.ThreatLevel >= SecurityBalance.SecurityCollapseThreshold)
            {
                state.DefeatReason = DefeatReason.SecurityCollapse;
                return DefeatReason.SecurityCollapse;
            }

            return null;
        }
    }
}
