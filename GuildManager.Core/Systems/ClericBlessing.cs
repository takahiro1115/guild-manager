using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 神官の加護（2026年10月・§0.72）。部隊に神官（Cleric）がいると、ボス戦と道中の損耗が軽くなり、ボス戦の後に重傷になりにくい。
    /// 火力の重みは控えめ（MND・VIT・LDR）だが、入れると部隊が崩れにくい、という神官の役割を表す（03 §2.1「致死回避・負傷軽減」）。
    ///  - 損耗の軽減：部隊の神官のMND（実効値）の最大×ClericBlessingLossPctPerMnd（%ポイント。MND100で8）
    ///  - 重傷の基準：SevereInjuryHpThresholdPct×ClericBlessingSevereThresholdRate（0.8倍）
    /// 乱数は使わない。
    /// </summary>
    public static class ClericBlessing
    {
        /// <summary>部隊に神官がいるか。</summary>
        public static bool Applies(IEnumerable<Adventurer> members) => members.Any(m => m.JobClass == JobClass.Cleric && m.CurrentHP > 0);

        /// <summary>損耗率から差し引く%ポイント（神官がいなければ0）。</summary>
        public static double LossReductionPct(IEnumerable<Adventurer> members)
        {
            var clerics = members.Where(m => m.JobClass == JobClass.Cleric && m.CurrentHP > 0).ToList();
            return clerics.Count == 0 ? 0 : clerics.Max(c => c.GetEffectiveStat("MND")) * CombatBalance.ClericBlessingLossPctPerMnd;
        }

        /// <summary>重傷の基準に掛ける倍率（神官がいなければ1）。</summary>
        public static double SevereThresholdMultiplier(IEnumerable<Adventurer> members) =>
            Applies(members) ? CombatBalance.ClericBlessingSevereThresholdRate : 1.0;
    }
}
