using System;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 敗北条件の判定。仕様書 03 §8.3 参照。
    ///
    /// - 副官解雇：マスターの機嫌（GameState.MasterMood）が0に達すると最後通告。次の月の終わりまでに戻らなければ解雇（§0.136。それまでは猶予なく即時）。
    /// - 破産：所持金マイナスが4週連続で解消されない（4週間の猶予あり）。
    /// 両方が同じ週に成立した場合は副官解雇を優先する。
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

            // 副官解雇（2026年9月新設）：マスターの機嫌が0に達したら最後通告（§0.136。猶予0なら即時敗北）。
            // 最後通告の期限（次の月の終わり）の決算で、機嫌が DismissalRecoverMood に戻っていなければ解雇。
            if (state.DismissalWarningUntilWeek is int until)
            {
                if (state.MasterMood >= MasterMoodBalance.DismissalRecoverMood)
                    state.DismissalWarningUntilWeek = null; // 立て直した
                else if (state.WeekNumber >= until)
                {
                    state.DismissalWarningUntilWeek = null;
                    state.DefeatReason = DefeatReason.DismissedByMaster;
                    return DefeatReason.DismissedByMaster;
                }
            }
            else if (state.MasterMood <= MasterMoodBalance.Min)
            {
                if (MasterMoodBalance.DismissalGraceMonths <= 0)
                {
                    state.DefeatReason = DefeatReason.DismissedByMaster;
                    return DefeatReason.DismissedByMaster;
                }
                state.DismissalWarningUntilWeek = DismissalDeadline(state.WeekNumber);
            }

            if (state.Gold < 0)
                state.ConsecutiveNegativeGoldWeeks++;
            else
                state.ConsecutiveNegativeGoldWeeks = 0;

            if (state.ConsecutiveNegativeGoldWeeks >= EconomyBalance.BankruptcyConsecutiveWeeksThreshold)
            {
                state.DefeatReason = DefeatReason.Bankruptcy;
                return DefeatReason.Bankruptcy;
            }

            return null;
        }

        /// <summary>最後通告の期限（§0.136）：この週の月から DismissalGraceMonths か月先の月の、最後の週。</summary>
        public static int DismissalDeadline(int week) =>
            ((week - 1) / GameCalendar.WeeksPerMonth + 1 + MasterMoodBalance.DismissalGraceMonths) * GameCalendar.WeeksPerMonth;

        /// <summary>破産まであと何週か（所持金がマイナスのときだけ。プラスなら null）。</summary>
        public static int? WeeksUntilBankruptcy(GameState state) =>
            state.Gold >= 0 ? null : Math.Max(0, EconomyBalance.BankruptcyConsecutiveWeeksThreshold - state.ConsecutiveNegativeGoldWeeks);
    }
}
