namespace GuildManager.Core.Models
{
    /// <summary>
    /// 特性の効果種別。仕様書 03 §5.3 参照。
    /// MVPでは StatPercentReduction のみ実際に接続する（古傷）。
    /// 他の値は将来の拡張用に定義のみ用意し、ロジックへの接続はpost-MVP（→ 03 §11）。
    /// </summary>
    public enum TraitEffectType
    {
        /// <summary>実効値をパーセンテージで低下させる（古傷で使用・MVPで接続）。</summary>
        StatPercentReduction,

        /// <summary>成長率補正（例：「頑強」）。post-MVP、未接続。</summary>
        GrowthRateModifier,

        /// <summary>索敵判定補正（例：「注意深い」）。post-MVP、未接続。</summary>
        ScoutingModifier,
    }
}
