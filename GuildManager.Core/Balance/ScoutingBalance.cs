namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 調査任務（スカウティング）のバランス値（→ Systems.ScoutingResolver）。
    /// 値は docs/04_バランス表/scouting.csv から読み込む（→ 03 §10.1、フォールバックなし）。
    ///
    /// 調査は2つの判定で構成される：
    ///  - 隠密・生還判定：AGI+DEX合算（＋部隊長LDRによる事故防止）vs 階層の要求値。
    ///    失敗しても死にはしないが、見つかって手傷を負い、解析も伸びにくい。
    ///  - 解析・情報収集判定：INT合算 vs 階層の要求値。成果に応じて解析率が上昇する。
    /// </summary>
    public static class ScoutingBalance
    {
        private const string FileName = "scouting.csv";

        public static readonly double StealthStatCoefficient = BalanceData.GetDouble(FileName, "StealthStatCoefficient");
        public static readonly double AnalysisStatCoefficient = BalanceData.GetDouble(FileName, "AnalysisStatCoefficient");

        /// <summary>部隊長LDR×この係数を隠密判定へ加算する（パニック・事故防止）。</summary>
        public static readonly double LeaderPanicPreventionCoefficient = BalanceData.GetDouble(FileName, "LeaderPanicPreventionCoefficient");

        public static readonly double StealthRequirementPerFloor = BalanceData.GetDouble(FileName, "StealthRequirementPerFloor");
        public static readonly double AnalysisRequirementPerFloor = BalanceData.GetDouble(FileName, "AnalysisRequirementPerFloor");

        // ---- 解析率の上昇量（成果別） ----
        public static readonly double IntelGainGreatSuccess = BalanceData.GetDouble(FileName, "IntelGainGreatSuccess");
        public static readonly double IntelGainSuccess = BalanceData.GetDouble(FileName, "IntelGainSuccess");

        /// <summary>成功に届かなくても持ち帰れる断片情報（調査が完全な無駄骨にはならないようにする）。</summary>
        public static readonly double IntelGainPartial = BalanceData.GetDouble(FileName, "IntelGainPartial");

        public static readonly double RatioThresholdGreatSuccess = BalanceData.GetDouble(FileName, "RatioThreshold_GreatSuccess");
        public static readonly double RatioThresholdSuccess = BalanceData.GetDouble(FileName, "RatioThreshold_Success");

        // ---- 情報開示の段階（→ Models.IntelTier） ----
        public static readonly double IntelTierBasic = BalanceData.GetDouble(FileName, "IntelTier_Basic");
        public static readonly double IntelTierHazards = BalanceData.GetDouble(FileName, "IntelTier_Hazards");
        public static readonly double IntelTierCountermeasures = BalanceData.GetDouble(FileName, "IntelTier_Countermeasures");
        public static readonly double IntelTierComplete = BalanceData.GetDouble(FileName, "IntelTier_Complete");

        // ---- HP消費（調査は低リスク経路：致死判定には接続しない） ----
        public static readonly int DiscoveredHpLossPctMin = BalanceData.GetInt(FileName, "DiscoveredHpLossPctMin");
        public static readonly int DiscoveredHpLossPctMax = BalanceData.GetInt(FileName, "DiscoveredHpLossPctMax");
        public static readonly int StealthHpLossPctMin = BalanceData.GetInt(FileName, "StealthHpLossPctMin");
        public static readonly int StealthHpLossPctMax = BalanceData.GetInt(FileName, "StealthHpLossPctMax");
    }
}
