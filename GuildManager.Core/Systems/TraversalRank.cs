namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 道中進軍（→ DungeonTraversalResolver）のランク。2026年9月のリニア進軍モデル以降は、走破力Ratioから
    /// 算出した基礎進軍階層数（→ CalculateBaseFloors）を逆引きして決まる表示用の名前（→ RankFromFloors）で、
    /// 既踏階層のHP消費率（→ RankHpLossRange）にも効く。
    /// </summary>
    public enum TraversalRank
    {
        /// <summary>苦戦進軍：基礎1階層。走破力が不足していても必ずこの分は進む。</summary>
        Struggling,
        /// <summary>通常進軍：基礎2階層。</summary>
        Normal,
        /// <summary>迅速進軍：基礎3階層。</summary>
        Swift,
        /// <summary>電撃進軍：基礎4階層。楽に踏破できた分、消耗も軽い。</summary>
        Lightning,
        /// <summary>疾風進軍：基礎5階層（2026年9月新設）。既踏階層の消耗は電撃と同じ率で底打ち。</summary>
        Gale,
        /// <summary>神速進軍：基礎6階層以上（上限なし。2026年9月新設）。既踏階層の消耗は電撃と同じ率で底打ち。</summary>
        Godspeed,
    }
}
