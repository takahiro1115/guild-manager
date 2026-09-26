namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 遠征解決エンジン（QuestResolver）・最大HP算出（Adventurer.MaxHP）関連のバランス値。
    /// 仕様書 03 §4.1〜4.3 参照。値は docs/04_バランス表/combat.csv から読み込む
    /// （→ 03 §10.1、項目58）。
    ///
    /// 事前調査メモ（項目58）：これらの数値はQuestResolver.cs・Adventurer.cs（MaxHP算出）に
    /// 直書きされていた（05技術メモ§3の方針違反）。今回新設したこのクラスへ集約した。
    /// 配置関連（PlacementBalance。個人CPの配置補正は2026年9月に撤廃し、現在は不意打ちの被弾ウェイトのみ）は
    /// combat.csv内の該当キーもそちら側で読む（このクラスには持たない）。
    ///
    /// 項目63改訂：個人CP重み（旧WeightSTR〜WeightLDR）は、クエスト種別ごとの統一点数
    /// 計算式（→ 03 §4.2.3）の一部として QuestScoringBalance（quest_type_weights.csv）へ
    /// 移した。討伐の重みはそのテーブルのSubjugation行が持つ（値は移行前と同一）。
    /// あわせて旧クエスト適性倍率（QuestAptitudeBalance）のキーも廃止した。
    /// </summary>
    public static class CombatBalance
    {
        private const string FileName = "combat.csv";

        /// <summary>
        /// 討伐の要求値（敵CP）＝クエスト難易度×この係数。→ BAL: 戦闘/敵CP係数。
        /// 項目63の統一点数計算式では「討伐の要求係数」も兼ねる
        /// （→ QuestScoringBalance.GetRequirementCoefficient）。
        /// </summary>
        public static readonly double EnemyCpCoefficient = BalanceData.GetDouble(FileName, "EnemyCpCoefficient");

        // ---- 最大HP算出（→ Adventurer.MaxHP）。最大HP＝VIT×係数＋基礎値＋装備ボーナス ----
        public static readonly int MaxHpBase = BalanceData.GetInt(FileName, "MaxHpBase");
        public static readonly double MaxHpVitCoefficient = BalanceData.GetDouble(FileName, "MaxHpVitCoefficient");

        // ---- フェーズ1：索敵・遭遇判定（→ 03 §4.1） ----
        public static readonly double ScoutAvgCoefficient = BalanceData.GetDouble(FileName, "ScoutAvgCoefficient");
        public static readonly double ScoutLeaderLdrCoefficient = BalanceData.GetDouble(FileName, "ScoutLeaderLdrCoefficient");
        public static readonly double SurpriseLowerBoundBase = BalanceData.GetDouble(FileName, "SurpriseLowerBoundBase");
        public static readonly double AmbushUpperBoundBase = BalanceData.GetDouble(FileName, "AmbushUpperBoundBase");
        public static readonly double SurpriseCombatMultiplier = BalanceData.GetDouble(FileName, "SurpriseCombatMultiplier");
        public static readonly double AmbushEnemyMultiplier = BalanceData.GetDouble(FileName, "AmbushEnemyMultiplier");

        // ---- フェーズ2：戦闘比率とHP消費（→ 03 §4.2） ----
        public static readonly double RatioThresholdVictory = BalanceData.GetDouble(FileName, "RatioThreshold_Victory");
        public static readonly double RatioThresholdNarrowWin = BalanceData.GetDouble(FileName, "RatioThreshold_NarrowWin");
        public static readonly double RatioThresholdDefeat = BalanceData.GetDouble(FileName, "RatioThreshold_Defeat");

        public static readonly int HpLossPctVictoryMin = BalanceData.GetInt(FileName, "HpLossPct_Victory_Min");
        public static readonly int HpLossPctVictoryMax = BalanceData.GetInt(FileName, "HpLossPct_Victory_Max");
        public static readonly int HpLossPctNarrowWinMin = BalanceData.GetInt(FileName, "HpLossPct_NarrowWin_Min");
        public static readonly int HpLossPctNarrowWinMax = BalanceData.GetInt(FileName, "HpLossPct_NarrowWin_Max");
        public static readonly int HpLossPctDefeatMin = BalanceData.GetInt(FileName, "HpLossPct_Defeat_Min");
        public static readonly int HpLossPctDefeatMax = BalanceData.GetInt(FileName, "HpLossPct_Defeat_Max");
        public static readonly int HpLossPctRoutMin = BalanceData.GetInt(FileName, "HpLossPct_Rout_Min");
        public static readonly int HpLossPctRoutMax = BalanceData.GetInt(FileName, "HpLossPct_Rout_Max");

        // ---- フェーズ3：負傷・致死判定（→ 03 §4.3） ----
        public static readonly double SurvivalClericMndCoefficient = BalanceData.GetDouble(FileName, "SurvivalClericMndCoefficient");
        public static readonly double SurvivalLeaderLdrCoefficient = BalanceData.GetDouble(FileName, "SurvivalLeaderLdrCoefficient");
        public static readonly double SurvivalThresholdMin = BalanceData.GetDouble(FileName, "SurvivalThresholdMin");
        public static readonly double SurvivalThresholdMax = BalanceData.GetDouble(FileName, "SurvivalThresholdMax");

        /// <summary>致死回避閾値超〜＋この幅が不可逆障害（古傷）。超過は戦死。→ BAL: 戦闘/不可逆帯幅</summary>
        public static readonly int PermanentBand = BalanceData.GetInt(FileName, "PermanentBand");

        /// <summary>重傷の全治週数レンジ。→ 03 §2.3「重傷: 全治3〜8週」</summary>
        public static readonly int SevereInjuryWeeksMin = BalanceData.GetInt(FileName, "SevereInjuryWeeksMin");
        public static readonly int SevereInjuryWeeksMax = BalanceData.GetInt(FileName, "SevereInjuryWeeksMax");

        /// <summary>
        /// 耐毒体質（→ TraitCatalog.ResistPoison、03 §5.3.2）：未対策の「猛毒」ギミックが被ダメージ倍率に足す分
        /// （危険度×UncounteredDamageMultiplierPerDangerLevel）を、部隊に保有者がいればこの率だけ減らす（→ DungeonResolver）。
        /// </summary>
        public static readonly double ResistPoisonDamageReductionRate = BalanceData.GetDouble(FileName, "ResistPoisonDamageReductionRate");

        /// <summary>
        /// 巨獣狩り（→ TraitCatalog.GiantHunter）：ボスが「重装甲」ギミックを持つとき、保有者本人の討伐火力に
        /// 上乗せする率（→ DungeonPowerCalculator.MemberPower）。
        /// </summary>
        public static readonly double GiantHunterDamageBonusRate = BalanceData.GetDouble(FileName, "GiantHunterDamageBonusRate");

        /// <summary>
        /// 重傷生還時に古傷を負う確率（0〜1、→ Systems.CriticalInjury.RollOldWound、03 §4.3）。
        /// 道中進軍でHPが下限1まで落ちた隊員、階層ボス討伐の撤退でHP1で生還した隊員が対象。
        /// </summary>
        public static readonly double OldWoundCriticalChance = BalanceData.GetDouble(FileName, "OldWoundCriticalChance");

        /// <summary>
        /// 階層ボス討伐の撤退で古傷ロールの対象になるHPの上限（最大HPに対する比率、→ DungeonResolver、03 §4.3.2）。
        /// 上限HP＝max(1, floor(最大HP×この値))。ボス戦はHP下限0のためHPちょうど1が残ることが稀で、
        /// HP≤1条件ではほぼ発生しなかった（2026年9月・§0.34で緩和）。
        /// </summary>
        public static readonly double OldWoundRetreatHpThresholdPct = BalanceData.GetDouble(FileName, "OldWoundRetreatHpThresholdPct");

        /// <summary>
        /// 巨獣狩りの後天開眼（→ DungeonResolver、03 §4.5.4・§5.3.2、2026年9月・§0.35）：「重装甲」ギミックを持つ
        /// 階層ボスを撃破したとき、生存した隊員ごとに巨獣狩りを得る確率（0〜1）。
        /// </summary>
        public static readonly double GiantHunterAwakeningChance = BalanceData.GetDouble(FileName, "GiantHunterAwakeningChance");
    }
}
