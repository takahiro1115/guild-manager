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

        /// <summary>豪胆：階層ボス戦で本人が受けるHP損耗率（%）から差し引くポイント（§0.53。旧：致死判定の生存閾値への加算）。</summary>
        public static readonly double BraveSurvivalThresholdBonus = BalanceData.GetDouble(FileName, "BraveSurvivalThresholdBonus");

        /// <summary>注意深い：隠密への本人の寄与（AGI＋DEX）に掛ける補正率（§0.53。0.10で×1.10。旧：索敵フェーズのDEX寄与）。</summary>
        public static readonly double AttentiveScoutingBonus = BalanceData.GetDouble(FileName, "AttentiveScoutingBonus");

        /// <summary>知識人：解析への本人の寄与（INT×係数）に掛ける補正率（§0.53。0.20で×1.20）。</summary>
        public static readonly double ScholarAnalysisBonus = BalanceData.GetDouble(FileName, "ScholarAnalysisBonus");

        /// <summary>容姿秀麗が関与する相性ペアの上昇量に掛かる倍率（下降量には影響しない）。</summary>
        public static readonly double BeautifulCompatibilityGainMultiplier = BalanceData.GetDouble(FileName, "BeautifulCompatibilityGainMultiplier");

        /// <summary>
        /// 田舎育ちの採取スコアボーナス率（→ TraitEffectType.GatheringScoreBonus、GatheringResolver、2026年9月再設計）。
        /// 保有者本人の採取寄与（AGI×係数＋DEX×係数）にこの率を掛けた分を採取スコアへ加算する（0.20で+20%）。
        /// 旧・探索クエストの個人スコア加算（CountryBredExplorationBonus）の後継。
        /// </summary>
        public static readonly double CountryBredGatheringBonusRate = BalanceData.GetDouble(FileName, "CountryBredGatheringBonusRate");

        // ---- 2026年10月・§0.55で新設した10種（→ 03 §5.3.2） ----

        /// <summary>鷹の目：未対策の「飛行」による被ダメージ倍率の加算分を減らす率（部隊に1人いれば効く。→ DungeonResolver）。</summary>
        public static readonly double HawkEyeFlyingDamageReductionRate = BalanceData.GetDouble(FileName, "HawkEyeFlyingDamageReductionRate");

        /// <summary>危機察知：未対策の「即死級」で本人に適用するHP消費率（%。全損の代わり。→ DungeonResolver）。</summary>
        public static readonly int SixthSenseInstantKillHpLossPct = BalanceData.GetInt(FileName, "SixthSenseInstantKillHpLossPct");

        /// <summary>守り手：本人の護衛値に掛ける補正率（0.20で×1.20。→ ScoutingResolver.GetGuardValue）。</summary>
        public static readonly double GuardianGuardBonus = BalanceData.GetDouble(FileName, "GuardianGuardBonus");

        /// <summary>健脚：本人の走破への寄与に掛ける補正率（0.15で×1.15。→ DungeonTraversalResolver.GetMemberTraversalValue）。</summary>
        public static readonly double PathfinderTraversalBonus = BalanceData.GetDouble(FileName, "PathfinderTraversalBonus");

        /// <summary>頑強：最大HPの基礎部分（実効VIT×係数＋基礎値）に掛ける補正率（→ Adventurer.MaxHP）。</summary>
        public static readonly double SturdyMaxHpBonus = BalanceData.GetDouble(FileName, "SturdyMaxHpBonus");

        /// <summary>治りが早い：負傷の回復で毎週消化する週数への加算（→ InjuryRecoverySystem）。</summary>
        public static readonly int QuickHealerInjuryRecoveryBonus = BalanceData.GetInt(FileName, "QuickHealerInjuryRecoveryBonus");

        /// <summary>治りが早い：静養のHP自然回復量に掛ける補正率（→ RestRecoverySystem）。</summary>
        public static readonly double QuickHealerRestRecoveryBonus = BalanceData.GetDouble(FileName, "QuickHealerRestRecoveryBonus");

        /// <summary>快活：本人の満足度の週次変動に加える値（→ SatisfactionSystem）。</summary>
        public static readonly int CheerfulSatisfactionBonus = BalanceData.GetInt(FileName, "CheerfulSatisfactionBonus");

        /// <summary>働き者：研究の手伝いで本人が貯める額に掛ける倍率（→ IdleActivitySystem.GetHelpCredit、§0.73）。</summary>
        public static readonly double HardworkerIdleHelpCreditMultiplier = BalanceData.GetDouble(FileName, "HardworkerIdleHelpCreditMultiplier");

        /// <summary>火の魔術師：INTの素の値部分に掛ける補正率（→ TraitCatalog.FireMage、StatPercentReduction の正の値）。</summary>
        public static readonly double FireMageIntBonus = BalanceData.GetDouble(FileName, "FireMageIntBonus");

        /// <summary>ソードマスター：剣を装備しているときSTR・AGIの素の値部分に掛ける補正率（→ Adventurer.EffectiveStat）。</summary>
        public static readonly double SwordMasterStatBonus = BalanceData.GetDouble(FileName, "SwordMasterStatBonus");

        // ---- 2026年10月・§0.56：地図読み・生まれつきの欠点9種・後天の障害5種（→ 03 §5.3.2） ----

        /// <summary>地図読み：本人の走破への寄与に掛ける補正率（健脚・方向音痴と加算）。</summary>
        public static readonly double MapReaderTraversalBonus = BalanceData.GetDouble(FileName, "MapReaderTraversalBonus");

        /// <summary>臆病：階層ボス戦で本人のHP損耗率に足すポイント（→ SurvivalThresholdModifier の負の値として持つ）。</summary>
        public static readonly double CowardBossHpLossPenalty = BalanceData.GetDouble(FileName, "CowardBossHpLossPenalty");

        /// <summary>猪突猛進：本人の討伐火力の上乗せ率（→ DungeonPowerCalculator.MemberPower）。</summary>
        public static readonly double RecklessPowerBonus = BalanceData.GetDouble(FileName, "RecklessPowerBonus");

        /// <summary>猪突猛進：階層ボス戦で本人のHP損耗率に足すポイント。</summary>
        public static readonly double RecklessBossHpLossPenalty = BalanceData.GetDouble(FileName, "RecklessBossHpLossPenalty");

        /// <summary>不器用：隠密への本人の寄与の補正率（負。→ ScoutingModifier として注意深いと加算）。</summary>
        public static readonly double ClumsyStealthPenalty = BalanceData.GetDouble(FileName, "ClumsyStealthPenalty");

        /// <summary>方向音痴：走破への本人の寄与の補正率（負）。</summary>
        public static readonly double PoorDirectionTraversalPenalty = BalanceData.GetDouble(FileName, "PoorDirectionTraversalPenalty");

        /// <summary>病弱：最大HPの基礎部分の補正率（負。頑強と加算）。</summary>
        public static readonly double SicklyMaxHpPenalty = BalanceData.GetDouble(FileName, "SicklyMaxHpPenalty");

        /// <summary>飽きっぽい：訓練施設での成長判定の基礎確率に足す値（負）。</summary>
        public static readonly double FickleGrowthRatePenalty = BalanceData.GetDouble(FileName, "FickleGrowthRatePenalty");

        /// <summary>気難しい：本人の満足度の週次変動に足す値（負）。</summary>
        public static readonly int MoodySatisfactionPenalty = BalanceData.GetInt(FileName, "MoodySatisfactionPenalty");

        /// <summary>浪費家：採用時の週給と、満足度の賃金判定の適正週給に掛ける倍率。</summary>
        public static readonly double SpendthriftWageMultiplier = BalanceData.GetDouble(FileName, "SpendthriftWageMultiplier");

        /// <summary>毒の後遺症：VITの減少率（負）。</summary>
        public static readonly double PoisonAftereffectVitReduction = BalanceData.GetDouble(FileName, "PoisonAftereffectVitReduction");

        /// <summary>毒の後遺症：ロールの対象になる生還時HPの上限（最大HP比）。</summary>
        public static readonly double PoisonAftereffectHpThresholdPct = BalanceData.GetDouble(FileName, "PoisonAftereffectHpThresholdPct");

        /// <summary>毒の後遺症：付与確率（0〜1）。</summary>
        public static readonly double PoisonAftereffectChance = BalanceData.GetDouble(FileName, "PoisonAftereffectChance");

        /// <summary>足の古傷：AGIの減少率（負）。</summary>
        public static readonly double LegWoundAgiReduction = BalanceData.GetDouble(FileName, "LegWoundAgiReduction");

        /// <summary>腕の古傷：STR・DEXの減少率（負）。</summary>
        public static readonly double ArmWoundStatReduction = BalanceData.GetDouble(FileName, "ArmWoundStatReduction");

        /// <summary>戦慄：付与確率（0〜1）。</summary>
        public static readonly double DreadChance = BalanceData.GetDouble(FileName, "DreadChance");

        /// <summary>戦慄：本人の討伐火力の補正率（負）。</summary>
        public static readonly double DreadPowerPenalty = BalanceData.GetDouble(FileName, "DreadPowerPenalty");

        /// <summary>燃え尽き：ロールの対象になる連続出撃週数。</summary>
        public static readonly int BurnoutConsecutiveWeeks = BalanceData.GetInt(FileName, "BurnoutConsecutiveWeeks");

        /// <summary>燃え尽き：毎週の付与確率（0〜1）。</summary>
        public static readonly double BurnoutChance = BalanceData.GetDouble(FileName, "BurnoutChance");

        /// <summary>燃え尽き：本人の満足度の週次変動に足す値（負）。</summary>
        public static readonly int BurnoutSatisfactionPenalty = BalanceData.GetInt(FileName, "BurnoutSatisfactionPenalty");

        // ---- 2026年10月・§0.57：レア特性8種 ----

        /// <summary>天才：7能力すべての素の値部分に掛ける補正率（0.15で×1.15）。</summary>
        public static readonly double GeniusStatBonus = BalanceData.GetDouble(FileName, "GeniusStatBonus");

        // ---- §0.62：成長・素質系と戦闘系 ----

        /// <summary>大器晩成：全盛期（23歳〜）の成長の確率に掛ける倍率（訓練・出撃の両方、→ GrowthSystem.TraitGrowthMultiplier）。</summary>
        public static readonly double LateBloomerPeakGrowthMultiplier = BalanceData.GetDouble(FileName, "LateBloomerPeakGrowthMultiplier");

        /// <summary>早熟：22歳までの成長の確率に掛ける倍率。</summary>
        public static readonly double EarlyBloomerYouthGrowthMultiplier = BalanceData.GetDouble(FileName, "EarlyBloomerYouthGrowthMultiplier");

        /// <summary>魂魄の申し子：7能力すべての素の値部分に掛ける補正率（0.05で×1.05）。</summary>
        public static readonly double SoulChildStatBonus = BalanceData.GetDouble(FileName, "SoulChildStatBonus");

        /// <summary>魂魄の申し子：成長の確率に掛ける倍率（年齢によらない）。</summary>
        public static readonly double SoulChildGrowthMultiplier = BalanceData.GetDouble(FileName, "SoulChildGrowthMultiplier");

        /// <summary>歴戦の勇士：本人の討伐火力の上乗せ率（→ DungeonPowerCalculator.TraitPowerModifier）。</summary>
        public static readonly double VeteranPowerBonus = BalanceData.GetDouble(FileName, "VeteranPowerBonus");

        /// <summary>鼓舞：部隊の討伐火力の上乗せ率（保有者が何人いても1回分、→ DungeonPowerCalculator.PartyPower）。</summary>
        public static readonly double InspiringPartyPowerBonus = BalanceData.GetDouble(FileName, "InspiringPartyPowerBonus");

        /// <summary>単能力のレア特性（大魔導士・剣聖など7種）：対象能力の素の値部分に掛ける補正率（0.30で×1.30）。</summary>
        public static readonly double RareSingleStatBonus = BalanceData.GetDouble(FileName, "RareSingleStatBonus");

        // 特性伝授の確率（旧 TraitTransmissionBaseRatePercent・TraitTransmissionPeakStatBonusMaxPercent・
        // TraitTransmissionMentorBonusPercent）は、2026年9月（教官深化 Step 1、→ 03 §0.34）で training.csv の
        // TraitInheritanceBaseChance・MentorTraitInheritanceBonus へ移設・置き換えた（→ TrainingBalance）。
    }
}
