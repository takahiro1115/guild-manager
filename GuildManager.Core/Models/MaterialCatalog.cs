using System.Collections.Generic;

namespace GuildManager.Core.Models
{
    /// <summary>フィールド1件分の素材抽選表の1行（→ MaterialCatalog.GetFieldDrops）。</summary>
    public class MaterialDrop
    {
        public string MaterialId { get; init; } = "";

        /// <summary>この素材の抽選率（%）。同じフィールド内の全行の合計は100になる。</summary>
        public int Percent { get; init; }
    }

    /// <summary>
    /// 素材の静的カタログ（→ Systems.GatheringResolver）。第3の任務「探索（採取）」で
    /// フィールドごとに獲得できる素材のIdと表示名、抽選テーブルを持つ。
    ///
    /// 職業（PlacementRules）や施設と同じく、フィールドと素材の対応関係は構造的な値
    /// （5フィールド固定・各2〜3種）として扱い、CSV化はしない（→ 03 §10.1の方針）。
    /// 抽選率そのものの調整はここを直接編集する。
    /// </summary>
    public static class MaterialCatalog
    {
        // ---- forest（翠緑の原生林） ----
        public const string HerbMoonlightId = "herb_moonlight";
        public const string SporeMutantId = "spore_mutant";

        // ---- cave（嘆きの鍾乳洞） ----
        public const string MossLuminousId = "moss_luminous";
        public const string OreBlackId = "ore_black";

        // ---- ruins（忘却の古代廃墟） ----
        public const string RelicShardId = "relic_shard";
        public const string RuneStoneId = "rune_stone";
        public const string DustAncientId = "dust_ancient";

        // ---- canyon（焦熱の峡谷） ----
        public const string OreCrimsonId = "ore_crimson";
        public const string AshVolcanicId = "ash_volcanic";

        // ---- abyss（深淵の特異点） ----
        public const string CrystalVoidId = "crystal_void";
        public const string EssenceAbyssalId = "essence_abyssal";
        public const string ShardForbiddenId = "shard_forbidden";

        private static readonly Dictionary<string, string> Names = new()
        {
            [HerbMoonlightId] = "月光草",
            [SporeMutantId] = "変異胞子",
            [MossLuminousId] = "発光苔",
            [OreBlackId] = "黒鉱石",
            [RelicShardId] = "遺物の欠片",
            [RuneStoneId] = "ルーン石",
            [DustAncientId] = "太古の粉塵",
            [OreCrimsonId] = "紅玉鉱石",
            [AshVolcanicId] = "火山灰",
            [CrystalVoidId] = "虚空の結晶",
            [EssenceAbyssalId] = "深淵のエッセンス",
            [ShardForbiddenId] = "禁忌の欠片",
        };

        private static readonly Dictionary<string, List<MaterialDrop>> FieldDrops = new()
        {
            ["forest"] = new()
            {
                new MaterialDrop { MaterialId = HerbMoonlightId, Percent = 70 },
                new MaterialDrop { MaterialId = SporeMutantId, Percent = 30 },
            },
            ["cave"] = new()
            {
                new MaterialDrop { MaterialId = MossLuminousId, Percent = 70 },
                new MaterialDrop { MaterialId = OreBlackId, Percent = 30 },
            },
            ["ruins"] = new()
            {
                new MaterialDrop { MaterialId = RelicShardId, Percent = 60 },
                new MaterialDrop { MaterialId = RuneStoneId, Percent = 25 },
                new MaterialDrop { MaterialId = DustAncientId, Percent = 15 },
            },
            ["canyon"] = new()
            {
                new MaterialDrop { MaterialId = OreCrimsonId, Percent = 65 },
                new MaterialDrop { MaterialId = AshVolcanicId, Percent = 35 },
            },
            ["abyss"] = new()
            {
                new MaterialDrop { MaterialId = CrystalVoidId, Percent = 55 },
                new MaterialDrop { MaterialId = EssenceAbyssalId, Percent = 30 },
                new MaterialDrop { MaterialId = ShardForbiddenId, Percent = 15 },
            },
        };

        /// <summary>素材Idの表示名。カタログに無いIdはそのまま表示する（防御的フォールバック）。</summary>
        public static string GetName(string materialId) =>
            Names.TryGetValue(materialId, out var name) ? name : materialId;

        /// <summary>指定フィールドの素材抽選表。未定義のフィールドIdは空リスト（採取任務は成立しない）。</summary>
        public static IReadOnlyList<MaterialDrop> GetFieldDrops(string fieldId) =>
            FieldDrops.TryGetValue(fieldId, out var drops) ? drops : new List<MaterialDrop>();
    }
}
