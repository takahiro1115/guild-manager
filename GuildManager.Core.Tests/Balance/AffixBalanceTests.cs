using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using Xunit;

namespace GuildManager.Core.Tests.Balance
{
    /// <summary>
    /// ランダムアフィックスのバランス値（→ AffixBalance、affixes.csv・relic.csv の Affix*、03 §4.7・§0.39）のテスト。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~AffixBalance`
    /// </summary>
    public class AffixBalanceTests
    {
        private static readonly string[] Header =
            { "Id", "Type", "Name", "TargetStat", "MinValue", "MaxValue", "Tier", "AllowedSlots", "Weight" };

        private static string[] Row(string id = "X", string type = "Prefix", string name = "剛力の", string target = "STR",
            string min = "1", string max = "2", string tier = "1", string slots = "All", string weight = "100") =>
            new[] { id, type, name, target, min, max, tier, slots, weight };

        private class FixedRng : IRng
        {
            private readonly int _value;
            public FixedRng(int value) => _value = value;
            public int NextInt(int min, int max) => Math.Clamp(_value, min, max);
        }

        // ---------------- 実データの読み込み ----------------

        [Fact]
        public void AffixesCsv_LoadsAllDefinitions_WithUniqueIds()
        {
            var all = AffixBalance.All;

            Assert.Equal(48, all.Count); // 接頭辞・接尾辞 × Tier1〜3 × 8種（7大能力値＋HP）
            Assert.Equal(all.Count, all.Select(d => d.Id).Distinct().Count());
            Assert.Equal(24, all.Count(d => d.Type == AffixType.Prefix));
            Assert.Equal(24, all.Count(d => d.Type == AffixType.Suffix));

            // 各位置・各Tierで、7大能力値＋HPの8種がそろっている
            foreach (var type in Enum.GetValues<AffixType>())
            for (int tier = 1; tier <= 3; tier++)
            {
                var targets = all.Where(d => d.Type == type && d.Tier == tier).Select(d => d.TargetStat).OrderBy(s => s);
                Assert.Equal(new[] { "AGI", "DEX", "HP", "INT", "LDR", "MND", "STR", "VIT" }, targets);
            }
        }

        [Theory]
        [InlineData("PrefixMightT1", AffixType.Prefix, "剛力の", "STR", 1, 2, 1)]
        [InlineData("PrefixLifeT1", AffixType.Prefix, "生命の", "HP", 5, 10, 1)]
        [InlineData("PrefixLifeT2", AffixType.Prefix, "大生命の", "HP", 15, 25, 2)]
        [InlineData("PrefixMightT3", AffixType.Prefix, "破壊の", "STR", 5, 6, 3)]
        [InlineData("PrefixLifeT3", AffixType.Prefix, "不滅の", "HP", 30, 45, 3)]
        [InlineData("SuffixGiantT1", AffixType.Suffix, "［巨躯］", "HP", 5, 10, 1)]
        [InlineData("SuffixTigerT2", AffixType.Suffix, "［猛虎］", "STR", 3, 4, 2)]
        [InlineData("SuffixTitanT3", AffixType.Suffix, "［巨神］", "HP", 30, 45, 3)]
        public void AffixesCsv_ParsesRowValues(string id, AffixType type, string name, string target, int min, int max, int tier)
        {
            var def = AffixBalance.FindById(id);

            Assert.NotNull(def);
            Assert.Equal(type, def!.Type);
            Assert.Equal(name, def.Name);
            Assert.Equal(target, def.TargetStat);
            Assert.Equal(min, def.MinValue);
            Assert.Equal(max, def.MaxValue);
            Assert.Equal(tier, def.Tier);
            Assert.Equal(4, def.AllowedSlots.Count); // 初期データは全枠（All）
            Assert.True(def.Weight > 0);
        }

        [Fact]
        public void FindById_ReturnsNull_ForUnknownOrNullId()
        {
            Assert.Null(AffixBalance.FindById("NoSuchAffix"));
            Assert.Null(AffixBalance.FindById(null));
        }

        [Theory]
        [InlineData(ItemRarity.Common, 20, 0, 1, 1)]
        [InlineData(ItemRarity.Rare, 100, 30, 1, 2)]
        [InlineData(ItemRarity.Epic, 100, 100, 2, 3)]
        [InlineData(ItemRarity.Legendary, 100, 100, 3, 3)]
        public void RarityRules_MatchSpec(ItemRarity rarity, int prefixRate, int suffixRate, int minTier, int maxTier)
        {
            var rule = AffixBalance.GetRule(rarity);

            Assert.Equal(prefixRate, rule.PrefixRate);
            Assert.Equal(suffixRate, rule.SuffixRate);
            Assert.Equal(minTier, rule.MinTier);
            Assert.Equal(maxTier, rule.MaxTier);
        }

        [Fact]
        public void EveryRarityAndSlot_HasCandidates_WhereRateIsPositive()
        {
            foreach (var rarity in Enum.GetValues<ItemRarity>())
            foreach (var type in Enum.GetValues<AffixType>())
            foreach (var slot in Enum.GetValues<EquipmentSlot>())
            {
                var rule = AffixBalance.GetRule(rarity);
                if (rule.RateFor(type) == 0) continue;
                var candidates = AffixBalance.GetCandidates(type, slot, rule.MinTier, rule.MaxTier);
                Assert.NotEmpty(candidates);
                Assert.All(candidates, d => Assert.InRange(d.Tier, rule.MinTier, rule.MaxTier));
                Assert.All(candidates, d => Assert.Equal(type, d.Type));
            }
        }

        [Theory]
        [InlineData("PrefixMightT1", "剛力")]
        [InlineData("PrefixSturdyT1", "頑強")]
        [InlineData("PrefixLifeT2", "大生命")]
        [InlineData("SuffixGiantT1", "巨躯")]
        [InlineData("SuffixWorldTreeT3", "世界樹")]
        public void ShortLabel_StripsParticleOrBrackets(string id, string expected)
        {
            Assert.Equal(expected, AffixBalance.FindById(id)!.ShortLabel);
        }

        // ---------------- 重み付き抽選 ----------------

        [Fact]
        public void PickWeighted_SelectsByCumulativeWeight()
        {
            var candidates = AffixBalance.Parse(Header, new List<string[]>
            {
                Row(id: "A", weight: "100"),
                Row(id: "B", weight: "50"),
            });

            Assert.Equal("A", AffixBalance.PickWeighted(candidates, new FixedRng(1))!.Id);
            Assert.Equal("A", AffixBalance.PickWeighted(candidates, new FixedRng(100))!.Id);
            Assert.Equal("B", AffixBalance.PickWeighted(candidates, new FixedRng(101))!.Id);
            Assert.Equal("B", AffixBalance.PickWeighted(candidates, new FixedRng(150))!.Id);
            Assert.Null(AffixBalance.PickWeighted(Array.Empty<AffixDefinition>(), new FixedRng(1)));
        }

        [Fact]
        public void Parse_AllowedSlots_AcceptsAccessoryAndPipeSeparatedList()
        {
            var defs = AffixBalance.Parse(Header, new List<string[]>
            {
                Row(id: "Acc", slots: "Accessory"),
                Row(id: "WA", slots: "Weapon|Armor"),
            });

            Assert.Equal(new[] { EquipmentSlot.Accessory1, EquipmentSlot.Accessory2 }, defs[0].AllowedSlots);
            Assert.Equal(new[] { EquipmentSlot.Weapon, EquipmentSlot.Armor }, defs[1].AllowedSlots);
        }

        // ---------------- フォーマット違反（フォールバックせず例外） ----------------

        public static IEnumerable<object[]> MalformedRows() => new[]
        {
            new object[] { Row(type: "Infix") },
            new object[] { Row(target: "LUK") },
            new object[] { Row(min: "3", max: "2") },
            new object[] { Row(min: "abc") },
            new object[] { Row(tier: "0") },
            new object[] { Row(tier: "4") },
            new object[] { Row(slots: "Shield") },
            new object[] { Row(weight: "0") },
            new object[] { Row(id: "") },
            new object[] { Row(name: "") },
        };

        [Theory]
        [MemberData(nameof(MalformedRows))]
        public void Parse_Throws_OnMalformedRow(string[] row)
        {
            Assert.Throws<BalanceDataException>(() => AffixBalance.Parse(Header, new List<string[]> { row }));
        }

        [Fact]
        public void Parse_Throws_OnDuplicateId()
        {
            Assert.Throws<BalanceDataException>(() =>
                AffixBalance.Parse(Header, new List<string[]> { Row(id: "Dup"), Row(id: "Dup") }));
        }

        [Fact]
        public void Parse_Throws_OnMissingColumn()
        {
            var header = Header.Where(h => h != "Weight").ToArray();
            var row = Row().Take(header.Length).ToArray();

            var ex = Assert.Throws<BalanceDataException>(() => AffixBalance.Parse(header, new List<string[]> { row }));
            Assert.Contains("Weight", ex.Message);
        }

        [Fact]
        public void Parse_Throws_OnEmptyTable()
        {
            Assert.Throws<BalanceDataException>(() => AffixBalance.Parse(Header, new List<string[]>()));
        }
    }
}
