namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 最大HP・負傷・戦闘系の特性・古傷のバランス値。値は docs/04_バランス表/combat.csv から読み込む
    /// （→ 03 §10.1、項目58）。
    ///
    /// 旧通常クエストの戦闘処理（索敵・奇襲／不意打ち・戦闘比率・HP消費率・致死回避閾値・敵CP）の値は、
    /// 旧クエストの撤去後も読み込むだけで使われていなかったため、2026年10月に削除した（→ 03 §0.53）。
    /// </summary>
    public static class CombatBalance
    {
        private const string FileName = "combat.csv";

        // ---- 最大HP算出（→ Adventurer.MaxHP）。最大HP＝VIT×係数＋基礎値＋装備ボーナス ----
        public static readonly int MaxHpBase = BalanceData.GetInt(FileName, "MaxHpBase");
        public static readonly double MaxHpVitCoefficient = BalanceData.GetDouble(FileName, "MaxHpVitCoefficient");

        // ---- 負傷（→ Systems.CriticalInjury、03 §4.3・§0.53） ----

        /// <summary>重傷の全治週数レンジ。→ 03 §2.3「重傷: 全治3〜8週」</summary>
        public static readonly int SevereInjuryWeeksMin = BalanceData.GetInt(FileName, "SevereInjuryWeeksMin");
        public static readonly int SevereInjuryWeeksMax = BalanceData.GetInt(FileName, "SevereInjuryWeeksMax");

        /// <summary>階層ボス戦の後、生き残った隊員のHPが最大HP×この値未満なら重傷（0.25で25%未満）。</summary>
        public static readonly double SevereInjuryHpThresholdPct = BalanceData.GetDouble(FileName, "SevereInjuryHpThresholdPct");

        /// <summary>軽傷の全治週数レンジ（道中・調査・採取でHPが1まで落ちたとき）。</summary>
        public static readonly int LightInjuryWeeksMin = BalanceData.GetInt(FileName, "LightInjuryWeeksMin");
        public static readonly int LightInjuryWeeksMax = BalanceData.GetInt(FileName, "LightInjuryWeeksMax");

        /// <summary>軽傷の間、実効能力値（素の値の部分）を下げる率（0.10で−10%。装備の補正と最大HPは下げない）。</summary>
        public static readonly double LightInjuryStatPenaltyRate = BalanceData.GetDouble(FileName, "LightInjuryStatPenaltyRate");

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
