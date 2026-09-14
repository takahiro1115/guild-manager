using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 環境ギミック1件に対するパーティの対策達成度（3段階評価）を判定する。
    /// 「環境ギミック」刷新仕様参照。定義データは GimmickBalance（gimmick.csv）が持つ。
    ///
    /// 判定式：
    ///  - パーティ全員の対象実効値（→ GimmickBalance.GetStat）の合算が
    ///    ThresholdFull以上なら+2点、ThresholdPartial以上（ThresholdFull未満）なら+1点、
    ///    それ未満なら+0点。
    ///  - 対応する携行アイテム（→ GimmickBalance.GetCounterItemId）をパーティが
    ///    携行していれば追加で+1点。
    ///  - 合計2点以上でFull（ペナルティ無効）、1点でPartial（半減）、0点でNone（そのまま）。
    ///
    /// PairSynergyCalculatorと同様、乱数もGameStateも使わない純粋な計算のためstaticクラスにしている。
    /// </summary>
    public static class GimmickEvaluator
    {
        /// <summary>指定した環境ギミックへの対策達成度を判定する。</summary>
        public static GimmickMitigationLevel Evaluate(IReadOnlyList<Adventurer> members, EnvironmentTag tag, IReadOnlyList<string> consumableItemIds)
        {
            int score = GetScore(members, tag, consumableItemIds);

            if (score >= 2) return GimmickMitigationLevel.Full;
            if (score == 1) return GimmickMitigationLevel.Partial;
            return GimmickMitigationLevel.None;
        }

        /// <summary>
        /// 判定の内訳点数（0〜3）。public にしてあるのはユニットテストから
        /// 能力値合算とアイテム補填の寄与を個別に確認できるようにするため。
        /// </summary>
        public static int GetScore(IReadOnlyList<Adventurer> members, EnvironmentTag tag, IReadOnlyList<string> consumableItemIds)
        {
            string stat = GimmickBalance.GetStat(tag);
            double statSum = members.Sum(m => m.GetEffectiveStat(stat));

            int score;
            if (statSum >= GimmickBalance.GetThresholdFull(tag)) score = 2;
            else if (statSum >= GimmickBalance.GetThresholdPartial(tag)) score = 1;
            else score = 0;

            if (consumableItemIds.Contains(GimmickBalance.GetCounterItemId(tag)))
                score += 1;

            return score;
        }

        /// <summary>
        /// クエストが持つ全ての環境ギミックのうち、指定フェーズ（→ GimmickPhase）に属するものだけの
        /// ペナルティ係数を積算して返す（属するタグが無ければ1.0＝影響なし）。
        /// QuestResolverが索敵値・点数・損耗のそれぞれに乗算する形で使う。
        /// </summary>
        public static double GetPhaseMultiplier(
            IReadOnlyList<EnvironmentTag> environmentTags, GimmickPhase phase,
            IReadOnlyList<Adventurer> members, IReadOnlyList<string> consumableItemIds)
        {
            double multiplier = 1.0;
            foreach (var tag in environmentTags)
            {
                if (GimmickBalance.GetPhase(tag) != phase)
                    continue;

                var level = Evaluate(members, tag, consumableItemIds);
                multiplier *= GimmickBalance.GetMultiplier(tag, level);
            }
            return multiplier;
        }
    }
}
