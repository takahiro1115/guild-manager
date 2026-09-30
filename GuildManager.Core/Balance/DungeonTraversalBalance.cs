namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 大迷宮「道中進軍」（→ Systems.DungeonTraversalResolver）のバランス値。
    /// 値は docs/04_バランス表/dungeon_traversal.csv から読み込む（→ 03 §10.1、フォールバックなし）。
    ///
    /// 道中進軍は「一度倒したボス階層は素通りし、次の未撃破ボス階層まで部隊の走破力に応じて
    /// 進軍する」という調査任務の分岐（→ ScoutingBalance側のボス解析判定とは別枠）。
    /// 1歩の重さはその階層の要求値で決まる（§0.49）。走破力が不足していても1週に必ず1階層は進むため、
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

        /// <summary>
        /// 道中進軍の要求値＝階層×この値×フィールド倍率（→ DungeonBalance.GetFieldRequirementMultiplier）。1歩ごとに、
        /// 踏み出す階層の値で消費を決める（§0.49）。他の要求値と違って基礎値は置かない：毎回1Fから潜り直すため、
        /// 浅い階を速く抜けられる形を残す（§0.47）。
        /// </summary>
        public static readonly double RequirementPerFloor = BalanceData.GetDouble(FileName, "RequirementPerFloor");

        /// <summary>
        /// 比率1.0の深さで1週に進める階層数（§0.49）。1歩の消費＝その階層の要求値÷（走破力×この値）÷区間の走破倍率で、
        /// 1週の予算は1（→ DungeonTraversalResolver.StepCost）。進軍ランクの逆引き
        /// （max(1, floor(Ratio×この値)) → DungeonTraversalResolver.CalculateBaseFloors）にも使う。
        /// </summary>
        public static readonly double FloorsPerRatio = BalanceData.GetDouble(FileName, "FloorsPerRatio");

        /// <summary>
        /// 進軍ランクの段＝max(1, floor(先頭の階層での比率×この値))（1＝苦戦〜6以上＝神速、→ DungeonTraversalResolver.ClassifyRatio）。
        /// 進む速さ（FloorsPerRatio）とは切り離してあり、FloorsPerRatio を変えても既踏の損耗率は変わらない（§0.49）。
        /// </summary>
        public static readonly double RankRatioScale = BalanceData.GetDouble(FileName, "RankRatioScale");

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
