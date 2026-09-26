namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 調査任務（スカウティング）のバランス値（→ Systems.ScoutingResolver）。
    /// 値は docs/04_バランス表/scouting.csv から読み込む（→ 03 §10.1、フォールバックなし）。
    ///
    /// 調査は2つの判定で構成される：
    ///  - 隠密・生還判定：AGI+DEXの隊員平均×人数倍率（＋部隊長LDR・専門職・重装の補正）vs 階層の要求値。
    ///    失敗しても死にはしないが、見つかって手傷を負い、解析も伸びにくい。
    ///  - 解析・情報収集判定：INT合算 vs 階層の要求値。成果に応じて解析率が上昇する。
    /// さらに護衛判定（2026年9月新設）が、解析率上昇量の倍率とHP消費率を決める。
    /// </summary>
    public static class ScoutingBalance
    {
        private const string FileName = "scouting.csv";

        // ---- 隠密適性の重み・補正（2026年9月改訂、→ 03 §4.5.3） ----
        // 旧モデルは「Σ(AGI+DEX)×係数 ＋ 部隊長LDR×係数」だけで、走破力（→ DungeonTraversalBalance）と
        // まったく同じ式を参照していたため、UIの2指標が常に同値になっていた。隠密側は
        // 「専門職ボーナス・人数倍率・重装ペナルティ」を足し、潜伏の精度を測る指標として切り分けた。
        // §0.41：素点を隊員の合計から「平均」へ改め、人数倍率が本当に効く（大所帯ほど下がる）ようにした。
        // 素点の尺度がおおよそ人数分の1になったため、LDRの重み・専門職ボーナス・重装ペナルティ・要求値も縮めてある。

        /// <summary>各員のAGI（身のこなし）への重み。</summary>
        public static readonly double StealthWeightAgi = BalanceData.GetDouble(FileName, "Stealth_Weight_Agi");

        /// <summary>各員のDEX（手先の精度・足音の殺し方）への重み。</summary>
        public static readonly double StealthWeightDex = BalanceData.GetDouble(FileName, "Stealth_Weight_Dex");

        /// <summary>部隊長LDR×この係数を隠密適性へ加算する（パニック・事故防止）。</summary>
        public static readonly double StealthWeightLdr = BalanceData.GetDouble(FileName, "Stealth_Weight_Ldr");

        /// <summary>斥候（Ranger）・盗賊（Thief）1名につき加算する専門職ボーナス。</summary>
        public static readonly double StealthBonusRangerThief = BalanceData.GetDouble(FileName, "Stealth_Bonus_RangerThief");

        /// <summary>
        /// 重装者1名につき隠密適性から**直接減算**する固定ペナルティ（倍率ではない）。
        /// 対象は「重装鎧の装備者」または「JobClassがWarrior/Knightのメンバー」（どちらか一方でも該当すれば1名分）。
        /// </summary>
        public static readonly double StealthHeavyArmorPenalty = BalanceData.GetDouble(FileName, "Stealth_HeavyArmor_Penalty");

        private static readonly double[] StealthPartySizeMultipliers =
        {
            BalanceData.GetDouble(FileName, "Stealth_PartySize_Mult_1"),
            BalanceData.GetDouble(FileName, "Stealth_PartySize_Mult_2"),
            BalanceData.GetDouble(FileName, "Stealth_PartySize_Mult_3"),
            BalanceData.GetDouble(FileName, "Stealth_PartySize_Mult_4"),
        };

        /// <summary>
        /// 人数倍率（大所帯ほど気配を消しにくい）。1〜4名に対応し、範囲外は近い端へ丸める
        /// （0名は1名分、5名以上は4名分。→ Models.Party.MaxSlots は4）。
        /// </summary>
        public static double GetStealthPartySizeMultiplier(int memberCount) =>
            StealthPartySizeMultipliers[System.Math.Clamp(memberCount, 1, StealthPartySizeMultipliers.Length) - 1];

        public static readonly double AnalysisStatCoefficient = BalanceData.GetDouble(FileName, "AnalysisStatCoefficient");

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

        // ---- 護衛判定（2026年9月新設、→ Systems.GuardTier） ----
        // 調査隊の護衛力（主護衛の max(STR,VIT,INT) ＋ 他の隊員の支援分。§0.41）÷ 要求護衛値 の比率で4段階に分け、
        // 段階ごとに解析率上昇量の倍率とHP消費率を決める（HP消費は護衛判定のみで決まる。
        // 調査は低リスク経路：HP下限1、致死・除籍判定には接続しない）。

        public static readonly double GuardRatioAbundant = BalanceData.GetDouble(FileName, "GuardRatio_Abundant");
        public static readonly double GuardRatioSufficient = BalanceData.GetDouble(FileName, "GuardRatio_Sufficient");
        public static readonly double GuardRatioMarginal = BalanceData.GetDouble(FileName, "GuardRatio_Marginal");

        public static readonly double GuardIntelMultiplierAbundant = BalanceData.GetDouble(FileName, "GuardEffect_IntelMultiplier_Abundant");
        public static readonly double GuardIntelMultiplierSufficient = BalanceData.GetDouble(FileName, "GuardEffect_IntelMultiplier_Sufficient");
        public static readonly double GuardIntelMultiplierMarginal = BalanceData.GetDouble(FileName, "GuardEffect_IntelMultiplier_Marginal");
        public static readonly double GuardIntelMultiplierDeficient = BalanceData.GetDouble(FileName, "GuardEffect_IntelMultiplier_Deficient");

        public static readonly int GuardHpLossPercentAbundant = BalanceData.GetInt(FileName, "GuardEffect_HpLossPercent_Abundant");
        public static readonly int GuardHpLossPercentSufficient = BalanceData.GetInt(FileName, "GuardEffect_HpLossPercent_Sufficient");
        public static readonly int GuardHpLossPercentMarginal = BalanceData.GetInt(FileName, "GuardEffect_HpLossPercent_Marginal");
        public static readonly int GuardHpLossPercentDeficient = BalanceData.GetInt(FileName, "GuardEffect_HpLossPercent_Deficient");

        /// <summary>要求護衛値の基準（10F区間）。要求護衛値＝この値×ボス階層÷10。</summary>
        public static readonly double BaseRequiredGuardPower = BalanceData.GetDouble(FileName, "BaseRequiredGuardPower");

        /// <summary>
        /// 護衛力の支援係数（§0.41）。護衛力＝主護衛（隊員中で最大の max(STR,VIT,INT)）＋ 他の隊員の max(STR,VIT,INT) の合計×この値。
        /// 強い護衛役が主役であることは保ちつつ、頭数が多いほど守りが固くなる（隠密は逆に人数で下がる）。
        /// </summary>
        public static readonly double GuardSupportRatio = BalanceData.GetDouble(FileName, "Guard_Support_Ratio");
    }
}
