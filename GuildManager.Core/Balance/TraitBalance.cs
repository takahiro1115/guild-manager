namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 特性（Trait）の効果量に関するバランス値。仕様書 03 §4.3・§5.3.2 参照。
    /// 値は docs/04_バランス表/trait.csv から読み込む（→ 03 §10.1、項目58フォローアップ）。
    ///
    /// 事前調査メモ（項目58）：これらの数値は TraitCatalog.cs の各TraitDefinitionへ
    /// 「→ BAL: ...」という外部化予定コメント付きで直書きされていた（項目58時点では
    /// 対応CSVが無かったため対象外とし、本クラスを新設してフォローアップした）。
    /// 特性の構造（Id・EffectType・TargetStat・BlocksDeployment・IsTransmittable）は
    /// 引き続きTraitCatalog.cs側に残す（→ README「CSV化していない値」の方針と同じ）。
    /// ただし表示名・説明・障害フラグは、2026年9月（新規特性4種の追加）でCSVを正本にした
    /// （→ GetDefinitionText）。
    /// </summary>
    public static class TraitBalance
    {
        private const string FileName = "trait.csv";

        /// <summary>
        /// 特性1種分の定義テキストと障害フラグ（→ GetDefinitionText）。2026年9月（特性スロット自由枠・新規特性4種）で
        /// 表示名・説明・障害フラグを trait.csv の `{Id}_DisplayName`・`{Id}_Description`・`{Id}_IsCurseOrInjury` 行へ
        /// 移した（新規特性の定義をCSV正本で管理するため。効果の種別・対象ステータスは引き続き TraitCatalog.cs 側）。
        /// </summary>
        public readonly record struct TraitDefinitionText(string DisplayName, string Description, bool IsCurseOrInjury);

        /// <summary>
        /// 指定Idの特性の表示名・説明・障害フラグを trait.csv から読む。いずれかの行が欠けている、
        /// または IsCurseOrInjury が true/false でなければ BalanceDataException（フォールバックしない）。
        /// </summary>
        public static TraitDefinitionText GetDefinitionText(string traitId)
        {
            string flagKey = $"{traitId}_IsCurseOrInjury";
            string rawFlag = BalanceData.GetString(FileName, flagKey).Trim();
            bool isCurse = rawFlag.ToLowerInvariant() switch
            {
                "true" => true,
                "false" => false,
                _ => throw new BalanceDataException($"{FileName} のキー「{flagKey}」の値「{rawFlag}」は true/false のいずれかにしてください。"),
            };

            return new TraitDefinitionText(
                BalanceData.GetString(FileName, $"{traitId}_DisplayName"),
                BalanceData.GetString(FileName, $"{traitId}_Description"),
                isCurse);
        }

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

        // 特性伝授の確率（旧 TraitTransmissionBaseRatePercent・TraitTransmissionPeakStatBonusMaxPercent・
        // TraitTransmissionMentorBonusPercent）は、2026年9月（教官深化 Step 1、→ 03 §0.34）で training.csv の
        // TraitInheritanceBaseChance・MentorTraitInheritanceBonus へ移設・置き換えた（→ TrainingBalance）。
    }
}
