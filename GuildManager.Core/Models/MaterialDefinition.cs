namespace GuildManager.Core.Models
{
    /// <summary>
    /// 素材1件分の定義（→ 第3の任務「探索（採取）」）。値は
    /// docs/04_バランス表/materials.csv から読み込む（→ Balance.MaterialBalance）。
    ///
    /// MinFloor/MaxFloorはこの素材が見つかる階層帯の目安。採取任務での実際の抽選対象は
    /// フィールドの到達階層（DungeonField.ReachedFloor）がMinFloor以上かどうかだけで判定する
    /// （→ Systems.GatheringResolver）。MaxFloorはCSV上の記録用メタ情報であり、
    /// 到達階層がこれを超えても抽選対象から外れることはない（深く進んでも入手経路を
    /// 失わせないための設計判断）。
    /// </summary>
    public class MaterialDefinition
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public string FieldId { get; init; } = "";
        public int MinFloor { get; init; }
        public int MaxFloor { get; init; }

        /// <summary>この素材が選ばれた際の基準獲得個数（→ Systems.GatheringResolver.Resolve）。</summary>
        public int BaseYield { get; init; }

        /// <summary>
        /// 1個あたりの売却額（→ Systems.EconomySystem.TrySellMaterial、03 §4.8）。深いフィールドの
        /// 素材ほど高く設定する。研究レシピ（→ Balance.ResearchBalance）で使う素材を売り払うか
        /// 貯めておくかのトレードオフを作るための値。
        /// </summary>
        public int SellPrice { get; init; }
    }

    /// <summary>
    /// GatheringResolver・ResearchBalanceの各所が参照する素材Idの定数。マジックストリングを
    /// 1箇所へ集約する（→ Models.ResearchIdsと同じ考え方）。対応する詳細（表示名・獲得条件）は
    /// materials.csv側で管理する。
    /// </summary>
    public static class MaterialIds
    {
        public const string ForestHerb = "mat_forest_herb";
        public const string ForestWood = "mat_forest_wood";
        public const string ForestSpore = "mat_forest_spore";

        // ---- 洞窟・廃墟・峡谷・深淵フィールド分（2026年9月新設） ----
        public const string CaveMoss = "mat_cave_moss";
        public const string CaveOre = "mat_cave_ore";
        public const string RuinsScrap = "mat_ruins_scrap";
        public const string RuinsRune = "mat_ruins_rune";
        public const string CanyonAsh = "mat_canyon_ash";
        public const string CanyonGem = "mat_canyon_gem";
        public const string AbyssDust = "mat_abyss_dust";
        public const string AbyssCrystal = "mat_abyss_crystal";
    }
}
