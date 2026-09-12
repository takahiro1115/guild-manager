using System;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// ギルド格付け（名声・ランク）の管理。仕様書 03 §8.1・§8.1.1 参照。
    ///
    /// - ランクは双方向に動く（降格あり）。昇格ラインと降格ラインは別の値を持つ
    ///   （ヒステリシス）ため、名声が閾値付近で小さく上下してもランク表示自体は
    ///   頻繁に切り替わらない。
    /// - 現在のランクに見合ったクエスト（同ランク帯以上）を一定週数達成できていないと、
    ///   毎週わずかに名声が自然減衰する（§5.1の「4週連続遠征なしで満足度-5」と同じ発想）。
    ///
    /// 使い方：クエストが解決するたびに ApplyQuestResult を呼んで名声を加減算し、
    /// 週次決算で1回だけ ProcessWeeklySettlement を呼んで自然減衰・昇格降格判定を行う
    /// （SatisfactionSystem.ProcessWeeklySatisfactionと同じ「呼び出し側が今週の実績を
    /// boolで渡す」パターン）。
    /// </summary>
    public class GuildRankSystem
    {
        /// <summary>
        /// クエスト解決結果を名声に反映する（達成で加算、失敗で減算）。0未満にはならない。
        /// ランク自体の昇格・降格判定はここでは行わない（週次決算でまとめて行う → ProcessWeeklySettlement）。
        /// </summary>
        public void ApplyQuestResult(GameState state, bool questAchieved)
        {
            int delta = questAchieved
                ? GuildRankBalance.ReputationGainOnAchievement
                : -GuildRankBalance.ReputationLossOnFailure;
            state.Reputation = Math.Max(0, state.Reputation + delta);
        }

        /// <summary>
        /// 週次決算処理。週に1回だけ呼ぶこと。
        /// 名声自然減衰の判定・適用と、昇格/降格判定（ヒステリシス）を行う。
        /// </summary>
        /// <param name="state">ゲーム状態。</param>
        /// <param name="achievedRankAppropriateQuestThisWeek">
        /// 今週、現ランク相当（同ランク帯以上）のクエストを1件でも達成したか。
        /// </param>
        /// <returns>ランクが変化した場合はその内容。変化がなければ null。</returns>
        public GuildRankChangeEvent? ProcessWeeklySettlement(GameState state, bool achievedRankAppropriateQuestThisWeek)
        {
            if (achievedRankAppropriateQuestThisWeek)
            {
                state.WeeksSinceLastRankAppropriateQuest = 0;
            }
            else
            {
                state.WeeksSinceLastRankAppropriateQuest++;
                if (state.WeeksSinceLastRankAppropriateQuest >= GuildRankBalance.WeeksWithoutAppropriateQuestThreshold)
                    state.Reputation = Math.Max(0, state.Reputation - GuildRankBalance.ReputationDecayPerWeek);
            }

            return UpdateRank(state);
        }

        private static GuildRankChangeEvent? UpdateRank(GameState state)
        {
            var previous = state.GuildRank;

            // 昇格判定：名声が次ランクの昇格ラインに達している限り、繰り返し昇格させる
            // （大量に名声を得た週に複数ランク一気に上がることも許容する）。
            while (true)
            {
                var next = GuildRankBalance.NextRank(state.GuildRank);
                if (next == null) break;
                if (state.Reputation < GuildRankBalance.GetThreshold(next.Value).PromoteAt) break;
                state.GuildRank = next.Value;
            }

            // 降格判定：昇格が発生しなかった場合のみ意味を持つ（同じ週に昇格と降格が
            // 両方起きることは通常ないが、ループにしておけば大幅な名声減少にも対応できる）。
            if (state.GuildRank == previous)
            {
                while (true)
                {
                    var threshold = GuildRankBalance.GetThreshold(state.GuildRank);
                    if (state.Reputation >= threshold.DemoteAt) break;
                    var prev = GuildRankBalance.PreviousRank(state.GuildRank);
                    if (prev == null) break;
                    state.GuildRank = prev.Value;
                }
            }

            // Aランク到達フラグ（→ 03 §8.2、v1.10改訂）：初めてAランク以上に到達した時点で
            // 一度だけtrueにする。降格して割り込んでも取り消さない（既にtrueならこの行は
            // 何もしない）。
            if (!state.FinalQuestUnlocked && state.GuildRank >= GuildRank.A)
                state.FinalQuestUnlocked = true;

            return state.GuildRank == previous ? null : new GuildRankChangeEvent(previous, state.GuildRank);
        }
    }
}
