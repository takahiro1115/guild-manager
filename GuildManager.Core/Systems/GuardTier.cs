namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 調査任務の護衛評価（2026年9月新設、→ ScoutingResolver.ClassifyGuard）。
    /// 部隊護衛力÷要求護衛値の比率で4段階に分け、解析率上昇量の倍率とHP消費率を決める
    /// （→ BAL: scouting.csv GuardRatio_*・GuardEffect_*）。
    /// </summary>
    public enum GuardTier
    {
        /// <summary>余裕：護衛が残党を完封する。解析成果にボーナス、HP消費なし。</summary>
        Abundant,

        /// <summary>十分：護衛が機能する。解析成果は通常どおり、HP消費は軽微。</summary>
        Sufficient,

        /// <summary>充足：護衛の手が回らず被弾する。解析成果が目減りし、HP消費が増える。</summary>
        Marginal,

        /// <summary>不足：魔物の残党に強襲され調査隊が潰走する。解析成果なし、HP消費が重い。</summary>
        Deficient,
    }
}
