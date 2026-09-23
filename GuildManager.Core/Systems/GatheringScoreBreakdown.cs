namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 採取スコアの内訳（→ GatheringResolver.BreakDownGatheringScore）。
    /// 採取スコア＝(AGI分＋DEX分＋部隊長LDR分＋盗賊・斥候ボーナス)×部隊の平均HP比率。
    /// </summary>
    public record GatheringScoreBreakdown(double AgiPart, double DexPart, double LdrPart, double ClassBonus, double HpRatio)
    {
        /// <summary>HP比率を掛ける前の素点。</summary>
        public double RawScore => AgiPart + DexPart + LdrPart + ClassBonus;

        /// <summary>採取スコア（HP比率適用後）。</summary>
        public double Total => RawScore * HpRatio;
    }
}
