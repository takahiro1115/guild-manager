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
        /// 田舎育ちの採取スコアボーナス率（→ TraitEffectType.GatheringScoreBonus、GatheringResolver、2026年9月再設計）。
        /// 保有者本人の採取寄与（AGI×係数＋DEX×係数）にこの率を掛けた分を採取スコアへ加算する（0.20で+20%）。
        /// 旧・探索クエストの個人スコア加算（CountryBredExplorationBonus）の後継。
        /// 知識人（Scholar）には単独効果を持たせていない（ペア特性シナジー専用の特性。
        /// → PairSynergyBalance・TraitCatalog.Scholar）。
        /// </summary>
        public static readonly double CountryBredGatheringBonusRate = BalanceData.GetDouble(FileName, "CountryBredGatheringBonusRate");

        // ---- 特性伝授（→ 特性伝授・スロット上限刷新仕様、TrainingSystem.ProcessWeeklyTraitTransmission） ----

        /// <summary>教官からの週次伝授ロールの基礎確率（%）。</summary>
        public static readonly double TraitTransmissionBaseRatePercent = BalanceData.GetDouble(FileName, "TraitTransmissionBaseRatePercent");

        /// <summary>
        /// 教官の対象ステータス（配置先施設が扱うもの。→ FacilityBalance.GetTrainingTargetStats）
        /// 生涯ピーク平均（0〜100）に比例して加算される最大ボーナス（%）。ピーク平均100で満額。
        /// </summary>
        public static readonly double TraitTransmissionPeakStatBonusMaxPercent = BalanceData.GetDouble(FileName, "TraitTransmissionPeakStatBonusMaxPercent");

        /// <summary>教官が「師匠肌」（→ TraitCatalog.Mentor）を持つ場合に加算される固定ボーナス（%）。</summary>
        public static readonly double TraitTransmissionMentorBonusPercent = BalanceData.GetDouble(FileName, "TraitTransmissionMentorBonusPercent");
    }
}
