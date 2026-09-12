namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 治安・脅威度（ThreatLevel）関連の暫定バランス値。仕様書 03 §4.4・§8.3 参照。
    /// 現状はすべて仮値の定数（→ BAL: 治安）。
    /// </summary>
    public static class SecurityBalance
    {
        /// <summary>脅威度は0〜100でクランプする。</summary>
        public const int MinThreatLevel = 0;
        public const int MaxThreatLevel = 100;

        /// <summary>初期脅威度。→ BAL: 治安/初期値</summary>
        public const int InitialThreatLevel = 0;

        /// <summary>討伐クエストの放置・失敗1件あたりの脅威度上昇量。→ BAL: 治安</summary>
        public const int ThreatIncreaseMin = 2;
        public const int ThreatIncreaseMax = 5;

        /// <summary>討伐クエスト達成1件あたりの脅威度減少量。→ BAL: 治安</summary>
        public const int ThreatDecreaseMin = 10;
        public const int ThreatDecreaseMax = 15;

        /// <summary>この値を超えると月次助成金が50%カットされる（→ SubsidyBalance）。</summary>
        public const int SubsidyCutThreatThreshold = 75;

        /// <summary>この値に到達すると治安崩壊＝即時敗北（猶予なし。→ 03 §8.3）。</summary>
        public const int SecurityCollapseThreshold = 100;

        /// <summary>破産：所持金マイナスがこの週数連続で解消されないと敗北（→ 03 §8.3）。</summary>
        public const int BankruptcyConsecutiveWeeksThreshold = 4;
    }
}
