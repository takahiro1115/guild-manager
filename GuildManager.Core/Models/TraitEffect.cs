namespace GuildManager.Core.Models
{
    /// <summary>特性1つが持つ効果1件分（→ 03 §5.3）。</summary>
    public class TraitEffect
    {
        public TraitEffectType EffectType { get; set; }

        /// <summary>
        /// 効果の対象ステータス名（"STR","VIT","AGI","DEX" 等）。GetEffectiveStatの引数と対応する。
        /// 例外的に QuestTypeScoreBonus（→ 03 §4.2.3、項目64）では、ステータス名ではなく
        /// 対象クエスト種別の名前（QuestTypeの列挙子名。"Exploration"等）を入れる。
        /// </summary>
        public string TargetStat { get; set; } = "";

        /// <summary>効果量。StatPercentReductionの場合、例：-0.15 で15%低下。</summary>
        public double Value { get; set; }
    }
}
