using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests.Systems
{
    /// <summary>
    /// 鑑定時のランダムアフィックス付与（→ AppraisalSystem.RollAffixes、03 §4.7・§0.39）のテスト。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~AppraisalAffix`
    ///
    /// 希少度ごとの付与率・Tier範囲は relic.csv（→ AffixBalance.GetRule）。統計的な性質（銅は接尾辞が付かない、
    /// 金・虹は2枠確定、Tierが範囲内）は SeededRng で多数ロールして確かめ、名前・値の組み立ては固定乱数で決め打ちする。
    /// </summary>
    public class AppraisalAffixTests
    {
        private const int Samples = 600;

        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        /// <summary>用意した値を順に返し（範囲にクランプ）、使い切ったら下限を返す乱数。</summary>
        private class SequenceRng : IRng
        {
            private readonly Queue<int> _values;
            public SequenceRng(params int[] values) => _values = new Queue<int>(values);
            public int NextInt(int min, int max) => _values.Count > 0 ? Math.Clamp(_values.Dequeue(), min, max) : min;
        }

        private static List<EquipmentItem> RollMany(ItemRarity rarity, Item item, int seed = 12345)
        {
            var system = new AppraisalSystem(new SeededRng(seed));
            var result = new List<EquipmentItem>();
            for (int i = 0; i < Samples; i++)
            {
                var equipment = EquipmentItem.FromCatalog(item, rarity: rarity);
                system.RollAffixes(equipment, item.Slot, rarity);
                result.Add(equipment);
            }
            return result;
        }

        /// <summary>付いたアフィックスが位置・Tier範囲・値の範囲を守り、補正の合計が値と一致すること。</summary>
        private static void AssertConsistent(EquipmentItem e, int minTier, int maxTier)
        {
            var expectedStats = new Dictionary<string, int>();
            int expectedHp = 0;
            foreach (var (id, value, type) in new[] { (e.PrefixId, e.PrefixValue, AffixType.Prefix), (e.SuffixId, e.SuffixValue, AffixType.Suffix) })
            {
                if (id == null) continue;
                var def = AffixBalance.FindById(id)!;
                Assert.Equal(type, def.Type);
                Assert.InRange(def.Tier, minTier, maxTier);
                Assert.InRange(value, def.MinValue, def.MaxValue);
                if (def.IsHpBonus) expectedHp += value;
                else expectedStats[def.TargetStat] = expectedStats.GetValueOrDefault(def.TargetStat) + value;
            }
            Assert.Equal(expectedHp, e.AffixHpBonus);
            Assert.Equal(expectedStats.OrderBy(p => p.Key), e.AffixStatBonuses.OrderBy(p => p.Key));
        }

        // ---------------- 希少度ごとの付与ルール ----------------

        [Fact]
        public void Common_GivesNoAffix_OrOnlyTier1Prefix()
        {
            var items = RollMany(ItemRarity.Common, ItemCatalog.IronSword);

            Assert.All(items, e => Assert.Null(e.SuffixId));
            Assert.All(items, e => AssertConsistent(e, 1, 1));

            // 付与率20%：600回で大きく外れない（おおよそ120件）
            int withPrefix = items.Count(e => e.PrefixId != null);
            Assert.InRange(withPrefix, Samples * 10 / 100, Samples * 30 / 100);
            Assert.Contains(items, e => !e.HasAffix);
        }

        [Fact]
        public void Rare_AlwaysHasPrefix_SometimesSuffix_UpToTier2()
        {
            var items = RollMany(ItemRarity.Rare, ItemCatalog.Robe);

            Assert.All(items, e => Assert.NotNull(e.PrefixId));
            Assert.All(items, e => AssertConsistent(e, 1, 2));

            int withSuffix = items.Count(e => e.SuffixId != null);
            Assert.InRange(withSuffix, Samples * 20 / 100, Samples * 40 / 100); // 付与率30%
            Assert.Contains(items, e => AffixBalance.FindById(e.PrefixId)!.Tier == 2);
        }

        [Fact]
        public void Epic_AlwaysHasBothAffixes_WithinTier2To3()
        {
            var items = RollMany(ItemRarity.Epic, ItemCatalog.HeavyArmor);

            Assert.All(items, e => Assert.NotNull(e.PrefixId));
            Assert.All(items, e => Assert.NotNull(e.SuffixId));
            Assert.All(items, e => AssertConsistent(e, 2, 3));
            Assert.Contains(items, e => AffixBalance.FindById(e.PrefixId)!.Tier == 3);
        }

        [Fact]
        public void Legendary_AlwaysHasBothAffixes_OfTier3Only()
        {
            var items = RollMany(ItemRarity.Legendary, ItemCatalog.LifeAmulet);

            Assert.All(items, e => Assert.NotNull(e.PrefixId));
            Assert.All(items, e => Assert.NotNull(e.SuffixId));
            Assert.All(items, e => AssertConsistent(e, 3, 3));
        }

        [Fact]
        public void Common_PrefixRoll_AtRateBoundary()
        {
            var system = new AppraisalSystem(new SequenceRng(20));
            var hit = EquipmentItem.FromCatalog(ItemCatalog.IronSword, rarity: ItemRarity.Common);
            system.RollAffixes(hit, EquipmentSlot.Weapon, ItemRarity.Common);
            Assert.NotNull(hit.PrefixId); // 20 ≤ 20% → 付与

            var miss = EquipmentItem.FromCatalog(ItemCatalog.IronSword, rarity: ItemRarity.Common);
            new AppraisalSystem(new SequenceRng(21)).RollAffixes(miss, EquipmentSlot.Weapon, ItemRarity.Common);
            Assert.False(miss.HasAffix);
        }

        // ---------------- 鑑定（Appraise）経由の決め打ち ----------------

        [Fact]
        public void Appraise_Epic_WithMinRolls_BuildsNameValuesAndEffects()
        {
            var relic = new UnidentifiedItem
            {
                Name = "？？？ テスト用の封印箱", Rarity = ItemRarity.Epic, OriginFieldId = "forest", OriginFloor = 30,
                AppraisalCost = RelicBalance.GetAppraisalCost(ItemRarity.Epic),
            };
            var state = new GameState { Gold = 10000, UnidentifiedItems = { relic } };

            // 下限固定：種別ロール1＝武具、プール先頭＝重装鎧、接頭辞・接尾辞とも付与判定1で当選し、
            // 重み抽選1＝Tier2〜3の母集団の先頭（怪力の／［猛虎］）、値は下限の3。
            var result = new AppraisalSystem(new AlwaysMinRng()).Appraise(state, relic.Id);

            var equipment = Assert.Single(state.Armory);
            Assert.Equal(ItemCatalog.HeavyArmorId, equipment.ItemId);
            Assert.Equal("PrefixMightT2", equipment.PrefixId);
            Assert.Equal("SuffixTigerT2", equipment.SuffixId);
            Assert.Equal(3, equipment.PrefixValue);
            Assert.Equal(3, equipment.SuffixValue);
            Assert.Equal(6, equipment.GetAffixStatBonus("STR")); // 同じ能力値は合算
            Assert.Equal(0, equipment.AffixHpBonus);

            Assert.Equal("怪力の重装鎧［猛虎］", equipment.DisplayName);
            Assert.Equal("怪力の重装鎧［猛虎］", result!.ItemName);
            Assert.Equal("怪力の重装鎧［猛虎］（武具）", AppraisalSystem.BuildResultSummary(result));
            Assert.Equal($"{ItemCatalog.HeavyArmor.DescribeEffects()} [怪力: STR+3] [猛虎: STR+3]", equipment.DescribeEffects());
            Assert.Equal("重装鎧", equipment.Name); // Name はカタログ名のスナップショットのまま
        }

        [Fact]
        public void Appraise_Legendary_WithMaxValueRolls_UsesUpperBound()
        {
            var equipment = EquipmentItem.FromCatalog(ItemCatalog.GreatSword, rarity: ItemRarity.Legendary);
            var candidates = AffixBalance.GetCandidates(AffixType.Prefix, EquipmentSlot.Weapon, 3, 3);
            int totalWeight = candidates.Sum(d => d.Weight);
            var last = candidates[^1];

            // 接頭辞：判定1 → 重み抽選は末尾（重み合計）→ 値は上限。接尾辞：判定1 → 先頭 → 上限。
            new AppraisalSystem(new SequenceRng(1, totalWeight, 999, 1, 1, 999))
                .RollAffixes(equipment, EquipmentSlot.Weapon, ItemRarity.Legendary);

            Assert.Equal(last.Id, equipment.PrefixId);
            Assert.Equal(last.MaxValue, equipment.PrefixValue);
            var firstSuffix = AffixBalance.GetCandidates(AffixType.Suffix, EquipmentSlot.Weapon, 3, 3)[0];
            Assert.Equal(firstSuffix.Id, equipment.SuffixId);
            Assert.Equal(firstSuffix.MaxValue, equipment.SuffixValue);
        }

        [Fact]
        public void Appraise_NonEquipmentResults_AreUnaffected()
        {
            // 換金になった遺物では保管庫に何も増えない（アフィックスの抽選は武具のときだけ）。
            var relic = new UnidentifiedItem
            {
                Name = "？？？", Rarity = ItemRarity.Legendary, OriginFieldId = "forest", OriginFloor = 1,
                AppraisalCost = RelicBalance.GetAppraisalCost(ItemRarity.Legendary),
            };
            var state = new GameState { Gold = 10000, UnidentifiedItems = { relic } };

            var result = new AppraisalSystem(new SequenceRng(100)).Appraise(state, relic.Id);

            Assert.Equal(AppraisalResultType.Gold, result!.Type);
            Assert.Empty(state.Armory);
        }
    }
}
