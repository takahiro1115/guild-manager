using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 敗北条件の判定。仕様書 03 §8.3 参照。
    ///
    /// - 破産：所持金マイナスが4週連続で解消されない（4週間の猶予あり）。これが唯一の敗北条件。
    ///
    /// 治安崩壊（脅威度100%到達による即時敗北）は撤廃済み（→ 経営破綻への一本化改訂）。
    /// 指示書は本改訂の対象ファイルを WeekProcessingSystem.cs・GameState.cs としていたが、
    /// 実際の敗北判定ロジックはこの DefeatSystem.cs 1箇所に集約されているため、
    /// （両ファイルに敗北判定の分岐は存在しない）ここを直接修正した。
    /// `DefeatReason.SecurityCollapse` 自体は列挙子として削除していない：この理由で既に
    /// 敗北していた旧セーブをロードした際、ParseEnumが未知の値で例外を投げないようにするため
    /// （→ 03 §12のセーブ互換性方針）。新規にこの理由で敗北が確定することはもう無い。
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

            return null;
        }
    }
}
