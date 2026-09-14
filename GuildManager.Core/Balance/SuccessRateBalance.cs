namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 出撃前の成功率予測（→ Systems.SuccessRateCalculator）に関するバランス値。
    /// → コアシステム刷新仕様「(1) 成功率予測エンジンの計算式」参照。
    /// 値は docs/04_バランス表/success_rate.csv から読み込む（→ 03 §10.1）。
    ///
    /// 注意：ここにある閾値は「プレイヤーへ数値として提示するためのもの」ではない。
    /// 本プロジェクトは情報公開の原則（→ コミットd7c7f39）により、Ratio・難易度・
    /// 成功率の生の数値をUIに出さない方針を採っている。算出した確率は
    /// SuccessConfidence（定性表現）へ丸めてから提示する
    /// （→ SuccessRateCalculator.GetConfidence）。
    /// </summary>
    public static class SuccessRateBalance
    {
        private const string FileName = "success_rate.csv";

        /// <summary>基本成功率＝clamp(Ratio×この係数, MinRate, MaxRate)。Ratio1.0で50%。</summary>
        public static readonly double BaseCoefficient = BalanceData.GetDouble(FileName, "BaseCoefficient");

        public static readonly double MinRate = BalanceData.GetDouble(FileName, "MinRate");
        public static readonly double MaxRate = BalanceData.GetDouble(FileName, "MaxRate");

        /// <summary>採取任務を単独（1名）で行う場合の成功率上限。</summary>
        public static readonly double SoloGatheringMaxRate = BalanceData.GetDouble(FileName, "SoloGatheringMaxRate");

        /// <summary>討伐任務で前衛が1人もいない編成への減算。</summary>
        public static readonly double SubjugationMissingRolePenalty = BalanceData.GetDouble(FileName, "SubjugationMissingRolePenalty");

        /// <summary>ボス（昇格試験）任務で「総力戦」とみなす人数。</summary>
        public static readonly int BossFullMemberCount = BalanceData.GetInt(FileName, "BossFullMemberCount");

        /// <summary>ボス任務で総力戦の人数に1名欠けるごとの減算。</summary>
        public static readonly double BossUndermannedPenaltyPerMember = BalanceData.GetDouble(FileName, "BossUndermannedPenaltyPerMember");

        // ---- 定性表現の閾値（→ Models.SuccessConfidence） ----
        public static readonly double ConfidenceThresholdOverwhelming = BalanceData.GetDouble(FileName, "ConfidenceThreshold_Overwhelming");
        public static readonly double ConfidenceThresholdFavorable = BalanceData.GetDouble(FileName, "ConfidenceThreshold_Favorable");
        public static readonly double ConfidenceThresholdEven = BalanceData.GetDouble(FileName, "ConfidenceThreshold_Even");
        public static readonly double ConfidenceThresholdRisky = BalanceData.GetDouble(FileName, "ConfidenceThreshold_Risky");
    }
}
