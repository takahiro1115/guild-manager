namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 特性（Trait）の効果量に関するバランス値。仕様書 03 §4.3・§5.3.2 参照。
    /// 値は docs/04_バランス表/trait.csv から読み込む（→ 03 §10.1、項目58フォローアップ）。
    ///
    /// 事前調査メモ（項目58）：これらの数値は TraitCatalog.cs の各TraitDefinitionへ
    /// 「→ BAL: ...」という外部化予定コメント付きで直書きされていた（項目58時点では
    /// 対応CSVが無かったため対象外とし、本クラスを新設してフォローアップした）。
    /// 特性の構造（Id・DisplayName・EffectType・TargetStat・BlocksDeployment）は
    /// 引き続きTraitCatalog.cs側に残す（→ README「CSV化していない値」の方針と同じ：
    /// 構造を定義する値ではなく、調整対象の数値のみをCSV化する）。
    /// </summary>
    public static class TraitBalance
    {
        private const string FileName = "trait.csv";

        /// <summary>古傷がSTR/VIT/AGI/DEXそれぞれに与える恒久的な減少率（例：-0.15で15%低下）。</summary>
        public static readonly double OldWoundStatReduction = BalanceData.GetDouble(FileName, "OldWoundStatReduction");

        /// <summary>トラウマがMNDに与える恒久的な減少率。</summary>
        public static readonly double TraumaMndReduction = BalanceData.GetDouble(FileName, "TraumaMndReduction");

        /// <summary>豪胆が致死判定のSurvivalThreshold（→ 03 §4.3）に与える固定加算。</summary>
        public static readonly double BraveSurvivalThresholdBonus = BalanceData.GetDouble(FileName, "BraveSurvivalThresholdBonus");

        /// <summary>注意深いが索敵フェーズ（→ 03 §4.1）のDEX寄与に与える補正。</summary>
        public static readonly double AttentiveScoutingBonus = BalanceData.GetDouble(FileName, "AttentiveScoutingBonus");

        /// <summary>容姿秀麗が関与する相性ペアの上昇量に掛かる倍率（下降量には影響しない）。</summary>
        public static readonly double BeautifulCompatibilityGainMultiplier = BalanceData.GetDouble(FileName, "BeautifulCompatibilityGainMultiplier");

        /// <summary>
        /// 田舎育ちが探索クエストの個人スコアに加算する量（→ 03 §4.2.3、項目64）。
        /// 知識人（Scholar）には単独効果を持たせていない（ペア特性シナジー専用の特性。
        /// → PairSynergyBalance・TraitCatalog.Scholar）。
        /// </summary>
        public static readonly double CountryBredExplorationBonus = BalanceData.GetDouble(FileName, "CountryBredExplorationBonus");
    }
}
