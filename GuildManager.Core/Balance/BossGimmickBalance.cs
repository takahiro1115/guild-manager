namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 階層ボスのギミック（→ Models.BossGimmickType、Systems.DungeonResolver、03 §4.5.4・§0.68）のバランス値。
    /// 値は docs/04_バランス表/boss_gimmicks.csv から読む（→ 03 §10.1。フォールバックは持たない）。
    /// </summary>
    public static class BossGimmickBalance
    {
        private const string FileName = "boss_gimmicks.csv";

        /// <summary>対策の職業が同行しているときに備えへ足す値（→ DungeonResolver.Readiness）。</summary>
        public static readonly double GimmickRoleReadiness = BalanceData.GetDouble(FileName, "GimmickRoleReadiness");

        /// <summary>ギミックの数の刻み（→ SampleData.GimmickCount）。</summary>
        public static readonly int GimmickCountStep = BalanceData.GetInt(FileName, "GimmickCountStep");

        /// <summary>1体のボスが持つギミックの上限。</summary>
        public static readonly int GimmickCountMax = BalanceData.GetInt(FileName, "GimmickCountMax");

        /// <summary>2個目以降のギミックの危険度を下げる段数（下限1）。</summary>
        public static readonly int ExtraGimmickDangerReduction = BalanceData.GetInt(FileName, "ExtraGimmickDangerReduction");

        /// <summary>重装甲：備えの不足1あたりの部隊火力の低下率。</summary>
        public static readonly double HeavyArmorPowerPenalty = BalanceData.GetDouble(FileName, "HeavyArmorPowerPenalty");

        /// <summary>再生：備えの不足1あたりの要求火力の上昇率。</summary>
        public static readonly double RegenerationRequirementBonus = BalanceData.GetDouble(FileName, "RegenerationRequirementBonus");

        /// <summary>群れ：備えの不足1あたりの後衛の火力の低下率。</summary>
        public static readonly double SwarmRearPowerPenalty = BalanceData.GetDouble(FileName, "SwarmRearPowerPenalty");

        /// <summary>群れ：損耗の加算を、ほかの損耗型のこの倍にする。</summary>
        public static readonly double SwarmDamageFactor = BalanceData.GetDouble(FileName, "SwarmDamageFactor");

        /// <summary>魅了：操られた隊員の損耗に、備えの不足1あたり足す値（%）。</summary>
        public static readonly int CharmExtraHpLossPct = BalanceData.GetInt(FileName, "CharmExtraHpLossPct");

        /// <summary>猛毒：備えの不足1あたりの、毒状態の全能力の低下率。</summary>
        public static readonly double PoisonStatusStatPenalty = BalanceData.GetDouble(FileName, "PoisonStatusStatPenalty");

        /// <summary>毒状態の続く週数。</summary>
        public static readonly int PoisonStatusWeeks = BalanceData.GetInt(FileName, "PoisonStatusWeeks");

        /// <summary>耐毒体質の本人の、毒状態の週数の倍率（切り上げ、最低1週）。</summary>
        public static readonly double ResistPoisonStatusWeeksRate = BalanceData.GetDouble(FileName, "ResistPoisonStatusWeeksRate");
    }
}
