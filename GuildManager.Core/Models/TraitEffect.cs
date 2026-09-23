namespace GuildManager.Core.Models
{
    /// <summary>特性1つが持つ効果1件分（→ 03 §5.3）。</summary>
    public class TraitEffect
    {
        public TraitEffectType EffectType { get; set; }

        /// <summary>
        /// 効果の対象ステータス名（"STR","VIT","AGI","DEX" 等）。GetEffectiveStatの引数と対応する。
        /// 対象ステータスを持たない効果種別（SurvivalThresholdModifier・GatheringScoreBonus等）では""のまま。
        /// </summary>
        public string TargetStat { get; set; } = "";

        /// <summary>効果量。StatPercentReductionの場合、例：-0.15 で15%低下。</summary>
        public double Value { get; set; }
    }
}
