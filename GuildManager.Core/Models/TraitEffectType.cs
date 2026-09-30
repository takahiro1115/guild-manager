namespace GuildManager.Core.Models
{
    /// <summary>
    /// 特性の効果種別。仕様書 03 §5.3 参照。
    /// StatPercentReduction（古傷・トラウマ）・ScoutingModifier（注意深い）・SurvivalThresholdModifier（豪胆）・
    /// AnalysisModifier（知識人）・CompatibilityGainMultiplier（容姿秀麗）・GatheringScoreBonus（田舎育ち）が接続済み。
    /// ScoutingModifier・SurvivalThresholdModifier は旧クエストの索敵・致死判定用だったものを、2026年10月（§0.53）に
    /// 大迷宮の隠密・ボス戦の損耗へ付け直した（名前はセーブ互換とは無関係だが、差分を小さくするため据え置き）。
    /// GrowthRateModifier（「頑強」用）のみ、対応する特性が無いためpost-MVPで未接続のまま。
    /// </summary>
    public enum TraitEffectType
    {
        /// <summary>実効値をパーセンテージで低下させる（古傷・トラウマで使用）。</summary>
        StatPercentReduction,

        /// <summary>成長率補正（例：「頑強」）。post-MVP、未接続。</summary>
        GrowthRateModifier,

        /// <summary>
        /// 隠密への本人の寄与（AGI＋DEX、→ ScoutingResolver.GetStealthValue）に掛ける補正率（「注意深い」で使用、§0.53）。
        /// +0.10で寄与×1.10。TargetStatは不要（""のまま）。旧：索敵フェーズのDEX寄与補正。
        /// </summary>
        ScoutingModifier,

        /// <summary>
        /// 階層ボス戦で本人が受けるHP損耗率（%）から差し引くポイント（「豪胆」で使用、§0.53。→ DungeonResolver）。
        /// TargetStatは不要（""のまま）。旧：致死判定のSurvivalThresholdへの固定加算。
        /// </summary>
        SurvivalThresholdModifier,

        /// <summary>
        /// 解析への本人の寄与（INT×係数、→ ScoutingResolver.GetAnalysisValue）に掛ける補正率（「知識人」で使用、§0.53）。
        /// +0.20で寄与×1.20。TargetStatは不要（""のまま）。
        /// </summary>
        AnalysisModifier,

        /// <summary>
        /// 相性（Compatibility、→ 03 §5.3.1）の上昇量に掛かる乗数（「容姿秀麗」で使用）。
        /// TargetStatは不要（""のまま）。下降量には影響しない。
        /// </summary>
        CompatibilityGainMultiplier,

        /// <summary>
        /// 探索採取の採取スコアへの個人ボーナス率（「田舎育ち」で使用。→ 03 §5.3.2、GatheringResolver、
        /// 2026年9月再設計）。保有者本人の採取寄与（AGI×係数＋DEX×係数）にValueを掛けた分を加算する
        /// （0.20で+20%）。TargetStatは不要（""のまま）。
        /// 旧 QuestTypeScoreBonus（探索クエストの個人スコア加算。旧クエストの撤去で参照先を失っていた）の置き換え。
        /// </summary>
        GatheringScoreBonus,
    }
}
