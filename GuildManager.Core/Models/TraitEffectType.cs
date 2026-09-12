namespace GuildManager.Core.Models
{
    /// <summary>
    /// 特性の効果種別。仕様書 03 §5.3 参照。
    /// v1.4改訂：StatPercentReduction（古傷・トラウマ）・ScoutingModifier（注意深い）・
    /// SurvivalThresholdModifier（豪胆）・CompatibilityGainMultiplier（容姿秀麗）を接続。
    /// GrowthRateModifier（「頑強」用）のみ、対応する特性が無いためpost-MVPで未接続のまま。
    /// </summary>
    public enum TraitEffectType
    {
        /// <summary>実効値をパーセンテージで低下させる（古傷・トラウマで使用）。</summary>
        StatPercentReduction,

        /// <summary>成長率補正（例：「頑強」）。post-MVP、未接続。</summary>
        GrowthRateModifier,

        /// <summary>
        /// 索敵判定補正（「注意深い」で使用）。TargetStatで対象ステータス（DEX）を指定し、
        /// Valueをそのステータスの索敵フェーズ計算（PartyScout、→ 03 §4.1）への
        /// 寄与率補正として使う（例：+0.10で寄与+10%）。
        /// </summary>
        ScoutingModifier,

        /// <summary>
        /// 致死判定のSurvivalThreshold（→ 03 §4.3）への固定加算値（「豪胆」で使用）。
        /// TargetStatは不要（""のまま）。
        /// </summary>
        SurvivalThresholdModifier,

        /// <summary>
        /// 相性（Compatibility、→ 03 §5.3.1）の上昇量に掛かる乗数（「容姿秀麗」で使用）。
        /// TargetStatは不要（""のまま）。下降量には影響しない。
        /// </summary>
        CompatibilityGainMultiplier,
    }
}
