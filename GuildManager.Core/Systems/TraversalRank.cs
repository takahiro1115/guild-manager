namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 道中進軍（→ DungeonTraversalResolver）のランク。走破力Ratioから決まり、
    /// 進軍階層数・HP消費率の両方に効く（→ DungeonTraversalBalance）。
    /// </summary>
    public enum TraversalRank
    {
        /// <summary>苦戦進軍：+1階層。走破力が不足していても必ずこの分は進む。</summary>
        Struggling,

        /// <summary>通常進軍：+2階層。</summary>
        Normal,

        /// <summary>迅速進軍：+3階層。</summary>
        Swift,

        /// <summary>電撃進軍：+4階層。楽に踏破できた分、消耗も軽い。</summary>
        Lightning,
    }
}
