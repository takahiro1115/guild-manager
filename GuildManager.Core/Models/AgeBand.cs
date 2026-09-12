namespace GuildManager.Core.Models
{
    /// <summary>
    /// 年齢帯。仕様書 03 §3.0「年齢帯の統一定義」参照。
    /// 境界は Adventurer.AgeBand で判定する（15〜21 / 22〜27 / 28〜34 / 35〜40）。
    /// </summary>
    public enum AgeBand
    {
        /// <summary>成長期（15〜21）：成長率1.5倍、自律成長ロール有。</summary>
        GrowthPeriod,

        /// <summary>全盛期（22〜27）：上下動が最も安定。昇給要求が発生しやすい。</summary>
        PrimePeriod,

        /// <summary>円熟期（28〜34）：精神系（LDR/DEX/MND）は維持可。年1回フィジカル衰微。</summary>
        MaturePeriod,

        /// <summary>限界期（35〜40）：衰微が年2回・低下量拡大。引退傾向。</summary>
        LimitPeriod
    }
}
