using System;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 治安・脅威度（ThreatLevel）の管理。仕様書 03 §4.4 参照。
    ///
    /// 対象は討伐（Subjugation）クエストのみ。調査・探索・護衛は脅威度に影響しない。
    /// - 討伐クエストの放置（期限切れ・未受注）・失敗（苦戦敗退／戦線崩壊）で脅威度上昇。
    /// - 討伐クエストの達成（完全勝利／辛勝）で脅威度減少。
    /// </summary>
    public class SecuritySystem
    {
        private readonly IRng _rng;

        public SecuritySystem(IRng rng)
        {
            _rng = rng;
        }

        /// <summary>
        /// クエスト解決結果（達成/失敗）を脅威度に反映する。討伐クエスト以外は何もしない
        /// （その場合は0を返す）。実際に適用した増減量（達成時は負数）を返す
        /// （UI側の週報ログ表示用）。
        /// </summary>
        public int ApplyQuestResolution(GameState state, Quest quest, bool achieved)
        {
            if (quest.QuestType != QuestType.Subjugation)
                return 0;

            int delta = achieved
                ? -_rng.NextInt(SecurityBalance.ThreatDecreaseMin, SecurityBalance.ThreatDecreaseMax)
                : _rng.NextInt(SecurityBalance.ThreatIncreaseMin, SecurityBalance.ThreatIncreaseMax);

            return ApplyDelta(state, delta);
        }

        /// <summary>
        /// 期限切れ（放置）になった討伐クエスト1件を脅威度上昇に反映する……はずだったが、
        /// 治安度（脅威度100%到達による治安崩壊）を敗北条件から撤廃した改訂に伴い、
        /// この「未出撃週の治安悪化」ロジック自体を無効化した（常に0を返し、ThreatLevelには
        /// 一切触れない）。敗北条件は経営破綻（資金ショート）のみに一本化された
        /// （→ DefeatSystem・03 §8.3）。ThreatLevel・SecurityBalanceそのものは、月次助成金
        /// 50%カットの閾値（→ SubsidySystem・SecurityBalance.SubsidyCutThreatThreshold）に
        /// まだ使われているため削除しない。
        /// </summary>
        public int ApplyAbandonedQuest(GameState state, Quest quest)
        {
            return 0;
        }

        /// <summary>実際に反映された増減量（クランプの影響を受けた分は差し引かれる）を返す。</summary>
        private static int ApplyDelta(GameState state, int delta)
        {
            int before = state.ThreatLevel;
            state.ThreatLevel = Math.Clamp(
                state.ThreatLevel + delta, SecurityBalance.MinThreatLevel, SecurityBalance.MaxThreatLevel);
            return state.ThreatLevel - before;
        }
    }
}
