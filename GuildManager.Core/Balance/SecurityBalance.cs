namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 治安・脅威度（ThreatLevel）関連のバランス値。仕様書 03 §4.4・§8.3 参照。
    /// 値は docs/04_バランス表/security.csv から読み込む（→ 03 §10.1、項目58）。
    /// Min/MaxThreatLevelは脅威度のクランプ上下限（構造値）のためCSV化せずコードに残す。
    /// </summary>
    public static class SecurityBalance
    {
        private const string FileName = "security.csv";

        /// <summary>脅威度は0〜100でクランプする。</summary>
        public const int MinThreatLevel = 0;
        public const int MaxThreatLevel = 100;

        /// <summary>初期脅威度。→ BAL: 治安/初期値</summary>
        public static readonly int InitialThreatLevel = BalanceData.GetInt(FileName, "InitialThreatLevel");

        /// <summary>討伐クエストの放置・失敗1件あたりの脅威度上昇量。→ BAL: 治安</summary>
        public static readonly int ThreatIncreaseMin = BalanceData.GetInt(FileName, "ThreatIncreaseMin");
        public static readonly int ThreatIncreaseMax = BalanceData.GetInt(FileName, "ThreatIncreaseMax");

        /// <summary>討伐クエスト達成1件あたりの脅威度減少量。→ BAL: 治安</summary>
        public static readonly int ThreatDecreaseMin = BalanceData.GetInt(FileName, "ThreatDecreaseMin");
        public static readonly int ThreatDecreaseMax = BalanceData.GetInt(FileName, "ThreatDecreaseMax");

        /// <summary>この値を超えると月次助成金が50%カットされる（→ SubsidyBalance）。</summary>
        public static readonly int SubsidyCutThreatThreshold = BalanceData.GetInt(FileName, "SubsidyCutThreatThreshold");

        /// <summary>この値に到達すると治安崩壊＝即時敗北（猶予なし。→ 03 §8.3）。</summary>
        public static readonly int SecurityCollapseThreshold = BalanceData.GetInt(FileName, "SecurityCollapseThreshold");

        /// <summary>破産：所持金マイナスがこの週数連続で解消されないと敗北（→ 03 §8.3）。</summary>
        public static readonly int BankruptcyConsecutiveWeeksThreshold = BalanceData.GetInt(FileName, "BankruptcyConsecutiveWeeksThreshold");
    }
}
