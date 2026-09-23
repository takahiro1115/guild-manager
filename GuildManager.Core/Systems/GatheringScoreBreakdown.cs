namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 採取スコアの内訳（→ GatheringResolver.BreakDownGatheringScore）。
    /// 採取スコア＝(AGI分＋DEX分＋部隊長LDR分＋盗賊・斥候ボーナス＋田舎育ちボーナス)×部隊の平均HP比率。
    /// </summary>
    public record GatheringScoreBreakdown(double AgiPart, double DexPart, double LdrPart, double ClassBonus, double RuralBonus, double HpRatio)
    {
        /// <summary>HP比率を掛ける前の素点（田舎育ちボーナスを含む）。</summary>
        public double RawScore => AgiPart + DexPart + LdrPart + ClassBonus + RuralBonus;

        /// <summary>採取スコア（HP比率適用後）。</summary>
        public double Total => RawScore * HpRatio;

        /// <summary>田舎育ちボーナスを除いた採取スコア（HP比率適用後。UIの「基礎」表示用）。</summary>
        public double BaseTotal => (RawScore - RuralBonus) * HpRatio;

        /// <summary>田舎育ちボーナスの採取スコアへの寄与（HP比率適用後）。</summary>
        public double RuralTotal => RuralBonus * HpRatio;
    }
}
