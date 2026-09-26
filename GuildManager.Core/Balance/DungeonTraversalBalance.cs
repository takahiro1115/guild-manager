namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 大迷宮「道中進軍」（→ Systems.DungeonTraversalResolver）のバランス値。
    /// 値は docs/04_バランス表/dungeon_traversal.csv から読み込む（→ 03 §10.1、フォールバックなし）。
    ///
    /// 道中進軍は「一度倒したボス階層は素通りし、次の未撃破ボス階層まで部隊の走破力に応じて
    /// 一気に進軍する」という調査任務の分岐（→ ScoutingBalance側のボス解析判定とは別枠）。
    /// 走破力が不足していても必ず1階層は進む（→ FloorsPerRatio・max(1, …)）ため、
    /// 完全な足止めにはならない設計。
    /// </summary>
    public static class DungeonTraversalBalance
    {
        private const string FileName = "dungeon_traversal.csv";

        // ---- 走破力の重み（2026年9月改訂、→ 03 §4.5.3） ----
        // 旧モデルは AGI+DEX 合算（StatCoefficient）で、隠密適性（→ ScoutingBalance）とまったく同じ
        // 式を参照しており、UIの「走破力予測」と「隠密適性」が常に同値になっていた。
        // 走破力は「悪路を踏み越える体力（VIT）と、長い潜行に耐える気力（MND）」の指標へ切り分けた。

        /// <summary>各員のVIT（体力・悪路踏破）への重み。</summary>
        public static readonly double WeightVit = BalanceData.GetDouble(FileName, "Traversal_Weight_Vit");

        /// <summary>各員のMND（精神力・気力の持久）への重み。</summary>
        public static readonly double WeightMnd = BalanceData.GetDouble(FileName, "Traversal_Weight_Mnd");

        /// <summary>部隊長LDR×この係数を走破力へ加算する（指揮による道中効率化）。</summary>
        public static readonly double WeightLdr = BalanceData.GetDouble(FileName, "Traversal_Weight_Ldr");

        /// <summary>道中進軍の要求値＝現在到達階層（ReachedFloor）×この値。</summary>
        public static readonly double RequirementPerFloor = BalanceData.GetDouble(FileName, "RequirementPerFloor");

        /// <summary>
        /// 基礎進軍階層数のRatioスケール（2026年9月、リニア進軍モデル）：
        /// 基礎進軍階層数＝max(1, floor(走破力Ratio×この値))。上限なし（→ DungeonTraversalResolver.CalculateBaseFloors）。
        /// 旧来の「Ratio閾値3本（1.0/1.4/1.8）→ 1〜4階層の4段階」（RatioThreshold_*／FloorsAdvanced_*）を置き換えた。
        /// </summary>
        public static readonly double FloorsPerRatio = BalanceData.GetDouble(FileName, "FloorsPerRatio");

        // ---- 進軍ランクごとのHP消費率（疾風・神速は電撃の率で底打ち、→ DungeonTraversalResolver.RankHpLossRange） ----
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

        /// <summary>
        /// 夜目（→ TraitCatalog.NightVision、03 §4.5.3・§5.3.2）：部隊に保有者が1人でもいれば、未踏破階層の
        /// 基礎損耗率（→ DungeonBalance.UnexploredHpLossPct*）をこの率だけ減らす（→ DungeonTraversalResolver.ApplyHpLoss）。
        /// </summary>
        public static readonly double NightVisionUnexploredDamageReductionRate = BalanceData.GetDouble(FileName, "NightVisionUnexploredDamageReductionRate");
    }
}
