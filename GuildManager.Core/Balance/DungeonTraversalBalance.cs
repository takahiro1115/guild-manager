namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 大迷宮「道中進軍」（→ Systems.DungeonTraversalResolver）のバランス値。
    /// 値は docs/04_バランス表/dungeon_traversal.csv から読み込む（→ 03 §10.1、フォールバックなし）。
    ///
    /// 道中進軍は「一度倒したボス階層は素通りし、次の未撃破ボス階層まで部隊の走破力に応じて
    /// 一気に進軍する」という調査任務の分岐（→ ScoutingBalance側のボス解析判定とは別枠）。
    /// 走破力が不足していても必ず1階層は進む（→ FloorsAdvancedStruggling）ため、
    /// 完全な足止めにはならない設計。
    /// </summary>
    public static class DungeonTraversalBalance
    {
        private const string FileName = "dungeon_traversal.csv";

        public static readonly double StatCoefficient = BalanceData.GetDouble(FileName, "StatCoefficient");

        /// <summary>部隊長LDR×この係数を走破力へ加算する（指揮による効率化）。</summary>
        public static readonly double LeaderCoefficient = BalanceData.GetDouble(FileName, "LeaderCoefficient");

        /// <summary>道中進軍の要求値＝現在到達階層（ReachedFloor）×この値。</summary>
        public static readonly double RequirementPerFloor = BalanceData.GetDouble(FileName, "RequirementPerFloor");

        // ---- 進軍ランクの閾値（Ratio） ----
        public static readonly double RatioThresholdLightning = BalanceData.GetDouble(FileName, "RatioThreshold_Lightning");
        public static readonly double RatioThresholdSwift = BalanceData.GetDouble(FileName, "RatioThreshold_Swift");
        public static readonly double RatioThresholdNormal = BalanceData.GetDouble(FileName, "RatioThreshold_Normal");

        // ---- 進軍ランクごとの進軍階層数 ----
        public static readonly int FloorsAdvancedLightning = BalanceData.GetInt(FileName, "FloorsAdvanced_Lightning");
        public static readonly int FloorsAdvancedSwift = BalanceData.GetInt(FileName, "FloorsAdvanced_Swift");
        public static readonly int FloorsAdvancedNormal = BalanceData.GetInt(FileName, "FloorsAdvanced_Normal");
        public static readonly int FloorsAdvancedStruggling = BalanceData.GetInt(FileName, "FloorsAdvanced_Struggling");

        // ---- 進軍ランクごとのHP消費率 ----
        public static readonly int HpLossPctMinLightning = BalanceData.GetInt(FileName, "HpLossPctMin_Lightning");
        public static readonly int HpLossPctMaxLightning = BalanceData.GetInt(FileName, "HpLossPctMax_Lightning");
        public static readonly int HpLossPctMinSwift = BalanceData.GetInt(FileName, "HpLossPctMin_Swift");
        public static readonly int HpLossPctMaxSwift = BalanceData.GetInt(FileName, "HpLossPctMax_Swift");
        public static readonly int HpLossPctMinNormal = BalanceData.GetInt(FileName, "HpLossPctMin_Normal");
        public static readonly int HpLossPctMaxNormal = BalanceData.GetInt(FileName, "HpLossPctMax_Normal");
        public static readonly int HpLossPctMinStruggling = BalanceData.GetInt(FileName, "HpLossPctMin_Struggling");
        public static readonly int HpLossPctMaxStruggling = BalanceData.GetInt(FileName, "HpLossPctMax_Struggling");

        // ---- 調査度連動の走破加速（2026年9月新設、→ 毎回1Fリセット・複数週潜行型） ----

        /// <summary>走破倍率＝1.0＋区間担当ボスの解析率×この値（未調査1.0倍〜完全解析3.0倍）。</summary>
        public static readonly double IntelSpeedBonusPerIntel = BalanceData.GetDouble(FileName, "IntelSpeedBonusPerIntel");

        /// <summary>区間担当ボスが完全解析済みの階層を進む際の被ダメージ倍率。</summary>
        public static readonly double FullIntelDamageMultiplier = BalanceData.GetDouble(FileName, "FullIntelDamageMultiplier");

        // ---- 道中の拾得物（帰還時にギルドへ格納） ----

        /// <summary>1階層進むごとに拾うゴールド。</summary>
        public static readonly int LootGoldPerFloor = BalanceData.GetInt(FileName, "LootGoldPerFloor");

        /// <summary>この階層数を進むごとに素材を1個拾う。</summary>
        public static readonly int LootFloorsPerMaterial = BalanceData.GetInt(FileName, "LootFloorsPerMaterial");
    }
}
