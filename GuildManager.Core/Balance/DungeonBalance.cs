using System.Collections.Generic;
using System.Linq;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 階層ボス討伐のバランス値（→ Systems.DungeonResolver）。
    /// 値は docs/04_バランス表/dungeon.csv から読み込む（→ 03 §10.1、フォールバックなし）。
    ///
    /// 設計の骨子：「未調査での突撃は壊滅する」。未対策のギミック1件ごとに被ダメージ倍率が
    /// 積み上がり、即死級を未対策で踏むとHPを全損する。逆に対策が揃っていれば、
    /// 損害は通常の討伐クエスト並みに収まる。
    /// </summary>
    public static class DungeonBalance
    {
        private const string FileName = "dungeon.csv";

        /// <summary>ボス討伐の要求火力の基礎値（→ Systems.DungeonResolver.RequiredPower。§0.47）。</summary>
        public static readonly double PartyPowerRequirementBase = BalanceData.GetDouble(FileName, "PartyPowerRequirementBase");

        /// <summary>ボス討伐の要求火力の階層あたりの増分。</summary>
        public static readonly double PartyPowerRequirementPerFloor = BalanceData.GetDouble(FileName, "PartyPowerRequirementPerFloor");

        // ---- フィールド倍率（§0.47） ----
        // 5つのフィールド（ダンジョン）で難しさに差をつけるための倍率。討伐火力・調査（護衛・隠密・解析）・
        // 走破の要求値すべてに掛ける（→ ScaleRequirement）。2つ目以降の値は調整保留のため仮に1.0。

        private static readonly double[] FieldRequirementMultipliers =
            Enumerable.Range(1, 5)
                .Select(order => BalanceData.GetDouble(FileName, $"FieldRequirementMultiplier_{order}"))
                .ToArray();

        /// <summary>フィールド倍率（Order 1〜5）。範囲外は近い端へ丸める。</summary>
        public static double GetFieldRequirementMultiplier(int fieldOrder) =>
            FieldRequirementMultipliers[System.Math.Clamp(fieldOrder, 1, FieldRequirementMultipliers.Length) - 1];

        /// <summary>
        /// 大迷宮の要求値の共通形＝(基礎値＋階層×階層あたりの増分)×フィールド倍率（§0.47）。
        /// 旧式は「階層×係数」だけで、能力値がPA上限（100）で頭打ちになる冒険者側とは違って深い階層ほど
        /// 際限なく伸び、100Fにはどんな部隊も届かなかった。基礎値で浅い階層の手応えを残しつつ、傾きを緩めた。
        /// </summary>
        public static double ScaleRequirement(double @base, double perFloor, int floor, int fieldOrder) =>
            (@base + floor * perFloor) * GetFieldRequirementMultiplier(fieldOrder);

        /// <summary>完全解析（→ IntelTier.Complete）到達時の与ダメージ補正。</summary>
        public static readonly double FullIntelDamageBonus = BalanceData.GetDouble(FileName, "FullIntelDamageBonus");

        /// <summary>未対策ギミック1件（危険度1あたり）につき被ダメージへ加算される倍率。</summary>
        public static readonly double UncounteredDamageMultiplierPerDangerLevel =
            BalanceData.GetDouble(FileName, "UncounteredDamageMultiplierPerDangerLevel");

        public static readonly int BaseHpLossPctMin = BalanceData.GetInt(FileName, "BaseHpLossPctMin");
        public static readonly int BaseHpLossPctMax = BalanceData.GetInt(FileName, "BaseHpLossPctMax");

        public static readonly int RetreatHpLossPctMin = BalanceData.GetInt(FileName, "RetreatHpLossPctMin");
        public static readonly int RetreatHpLossPctMax = BalanceData.GetInt(FileName, "RetreatHpLossPctMax");

        /// <summary>即死級ギミックを未対策で踏んだ場合のHP消費率（対策の有無が生死を分ける）。</summary>
        public static readonly int InstantKillUncounteredHpLossPct = BalanceData.GetInt(FileName, "InstantKillUncounteredHpLossPct");

        // ---- 階層ボスの配置間隔・難易度スケーリング（→ Data.SampleData.CreateFieldBosses） ----

        /// <summary>階層ボスの配置間隔（10階ごと）。→ Models.DungeonField.BossInterval が参照する。</summary>
        public static readonly int BossIntervalFloors = BalanceData.GetInt(FileName, "BossIntervalFloors");

        /// <summary>10Fボス（森・段1）の基準HP。段が進むごとに BossHpFloorMultiplier を乗算する。</summary>
        public static readonly double BossBaseHp = BalanceData.GetDouble(FileName, "BossBaseHp");

        /// <summary>段（10階進む、またはフィールド1段深くなる）ごとのHP乗算係数。</summary>
        public static readonly double BossHpFloorMultiplier = BalanceData.GetDouble(FileName, "BossHpFloorMultiplier");

        /// <summary>10Fボス（森・段1）の基準報酬ゴールド。段が進むごとに BossRewardGoldMultiplier を乗算する。</summary>
        public static readonly double BossBaseRewardGold = BalanceData.GetDouble(FileName, "BossBaseRewardGold");

        /// <summary>段（10階進む、またはフィールド1段深くなる）ごとの報奨金乗算係数。</summary>
        public static readonly double BossRewardGoldMultiplier = BalanceData.GetDouble(FileName, "BossRewardGoldMultiplier");

        // ---- 道中進軍の未踏破損耗（2026年9月新設、→ Systems.DungeonTraversalResolver） ----
        // 今回の進軍にフィールドの最高到達階層より深い階層が含まれていれば、進軍ランク別の消費率
        // （dungeon_traversal.csv HpLossPct*）ではなく、この重い消費率を基礎にする。

        public static readonly int UnexploredHpLossPctMin = BalanceData.GetInt(FileName, "UnexploredHpLossPctMin");
        public static readonly int UnexploredHpLossPctMax = BalanceData.GetInt(FileName, "UnexploredHpLossPctMax");

        // ---- ボス討伐の部隊火力（→ Systems.DungeonPowerCalculator） ----
        // 旧通常クエストの「討伐」種別の能力重み（quest_type_weights.csv）から、同じ値のまま移設した
        // （旧クエスト撤去、2026年9月）。

        /// <summary>各員の実効ステータスへの重み（ステータス名→重み）。7能力すべてを持つ。</summary>
        public static readonly IReadOnlyList<(string Stat, double Weight)> BossPowerWeights =
            new[] { "STR", "AGI", "VIT", "MND", "DEX", "LDR", "INT" }
                .Select(stat => (stat, BalanceData.GetDouble(FileName, $"BossPowerWeight_{stat}")))
                .ToArray();

        // ---- 累積功績（→ Adventurer.TotalContributionScore。退職金の上乗せ原資） ----
        // 旧通常クエストの解決時に加算していたものを、大迷宮の活動（進軍・ボス撃破・採取）へ再配線した。

        /// <summary>道中進軍で1階層進むごとの功績。</summary>
        public static readonly int ContributionPerTraversedFloor = BalanceData.GetInt(FileName, "ContributionPerTraversedFloor");

        /// <summary>階層ボス撃破時の功績＝ボスの階層×この値。</summary>
        public static readonly int ContributionPerBossFloor = BalanceData.GetInt(FileName, "ContributionPerBossFloor");

        /// <summary>採取任務で素材を獲得した時の功績。</summary>
        public static readonly int ContributionPerGathering = BalanceData.GetInt(FileName, "ContributionPerGathering");

        // ---- 出撃成長（→ GrowthSystem.ApplyExpeditionGrowth。成長トリガー経路1の大迷宮版） ----
        // 旧通常クエストの撤去で止まっていた「出撃経験による成長」を、大迷宮の任務へ再配線した。

        /// <summary>道中進軍（潜行）1週ごとの成長ロール試行回数。</summary>
        public static readonly int GrowthRollsTraversal = BalanceData.GetInt(FileName, "GrowthRolls_Traversal");

        /// <summary>階層ボス撃破時の成長ロール試行回数。</summary>
        public static readonly int GrowthRollsBossVictory = BalanceData.GetInt(FileName, "GrowthRolls_BossVictory");

        /// <summary>迷宮調査からの帰還時の成長ロール試行回数。</summary>
        public static readonly int GrowthRollsSurvey = BalanceData.GetInt(FileName, "GrowthRolls_Survey");

        /// <summary>採取任務からの帰還時の成長ロール試行回数。</summary>
        public static readonly int GrowthRollsGathering = BalanceData.GetInt(FileName, "GrowthRolls_Gathering");

        /// <summary>成長ロール1回あたりの成功確率（%）。</summary>
        public static readonly int GrowthBaseChancePercent = BalanceData.GetInt(FileName, "GrowthBaseChancePercent");
    }
}
