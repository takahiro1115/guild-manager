using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 2026年10月・§0.61のテスト（クリアへのバランス 第2段）：店の上位装備（倒したボスの数で入荷）、
    /// 深い階層の鑑定品のアフィックスの倍率、能力を伸ばす霊薬。
    /// 実行方法: `dotnet test GuildManager.Core.Tests --filter FullyQualifiedName~GearAndElixir`
    /// </summary>
    public class GearAndElixirTests
    {
        private class SequenceRng : IRng
        {
            private readonly Queue<int> _values;
            private int _last;
            public SequenceRng(params int[] values) { _values = new Queue<int>(values); _last = values.LastOrDefault(); }
            public int NextInt(int min, int max)
            {
                if (_values.Count > 0) _last = _values.Dequeue();
                return Math.Clamp(_last, min, max);
            }
        }

        /// <summary>森の先頭から n 体のボスを倒した状態。</summary>
        private static GameState WithDefeatedBosses(int n)
        {
            var state = new GameState { Gold = 1_000_000, DungeonFields = SampleData.CreateDefaultFields() };
            foreach (var boss in state.DungeonFields.SelectMany(f => f.Bosses).Take(n))
                boss.IsDefeated = true;
            return state;
        }

        // ---------------- 店の上位装備 ----------------

        [Fact]
        public void TieredGear_IsDefined_SevenPerTier()
        {
            Assert.Equal(new[] { 15, 25, 35 }, ProgressionBalance.ShopTierUnlockBosses);
            foreach (int tier in new[] { 1, 2, 3 })
            {
                var items = ItemCatalog.GetAll().Where(i => i.ShopTier == tier).ToList();
                Assert.Equal(7, items.Count);
                Assert.Equal(3, items.Count(i => i.Slot == EquipmentSlot.Weapon));
                Assert.Equal(2, items.Count(i => i.Slot == EquipmentSlot.Armor));
                Assert.Equal(2, items.Count(i => i.Slot == EquipmentSlot.Accessory1)); // 指輪と耳飾り。装飾品は1と2の区別が無い（§0.67）
                Assert.All(items, i => Assert.False(i.IsHeavyArmor));
            }
            Assert.Equal(0, ItemCatalog.IronSword.ShopTier);
            Assert.Equal("ミスリルの剣", ItemCatalog.FindById("MithrilSword")!.Name);
            Assert.Equal("星糸の法衣", ItemCatalog.FindById("StarIronVestment")!.Name);
            // 段が上がるほど強く高い
            Assert.True(ItemCatalog.FindById("StarIronSword")!.GetStatBonus("STR") > ItemCatalog.FindById("OrichalcumSword")!.GetStatBonus("STR"));
            Assert.True(ItemCatalog.FindById("OrichalcumSword")!.GetStatBonus("STR") > ItemCatalog.FindById("MithrilSword")!.GetStatBonus("STR"));
            Assert.True(ItemCatalog.FindById("StarIronSword")!.Price > ItemCatalog.FindById("OrichalcumSword")!.Price);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(14, 0)]
        [InlineData(15, 1)]
        [InlineData(24, 1)]
        [InlineData(25, 2)]
        [InlineData(35, 3)]
        [InlineData(50, 3)]
        public void UnlockedShopTier_FollowsDefeatedBossCount(int defeated, int expectedTier)
        {
            var state = WithDefeatedBosses(defeated);
            Assert.Equal(defeated, EquipmentSystem.CountDefeatedBosses(state));
            Assert.Equal(expectedTier, EquipmentSystem.GetUnlockedShopTier(state));
        }

        [Fact]
        public void LockedTier_CannotBeBought_UntilUnlocked()
        {
            var state = WithDefeatedBosses(14);
            var knight = new Adventurer { JobClass = JobClass.Knight };
            state.Adventurers.Add(knight);

            Assert.True(EquipmentSystem.IsInShop(state, ItemCatalog.IronSword));
            Assert.False(EquipmentSystem.IsInShop(state, ItemCatalog.FindById("MithrilSword")!));
            Assert.False(new EquipmentSystem().TryPurchaseAndEquip(state, knight, "MithrilSword"));
            Assert.Null(knight.EquippedWeapon);

            state.DungeonFields.SelectMany(f => f.Bosses).First(b => !b.IsDefeated).IsDefeated = true; // 15体目
            int gold = state.Gold;
            Assert.True(new EquipmentSystem().TryPurchaseAndEquip(state, knight, "MithrilSword"));
            Assert.Equal("MithrilSword", knight.EquippedWeaponId);
            Assert.Equal(gold - ItemCatalog.FindById("MithrilSword")!.Price, state.Gold);
            Assert.False(new EquipmentSystem().TryPurchaseAndEquip(state, knight, "OrichalcumSword")); // 段2はまだ
        }

        // ---------------- 深い階層の鑑定品 ----------------

        [Theory]
        [InlineData(1, 1.0)]
        [InlineData(20, 1.0)]
        [InlineData(60, 1.5)]
        [InlineData(100, 2.0)]
        [InlineData(150, 2.0)]
        public void AffixDepthMultiplier_GrowsWithFloor(int floor, double expected)
        {
            Assert.Equal(expected, AffixBalance.DepthMultiplier(floor), precision: 6);
        }

        [Fact]
        public void DeepRelic_AffixValuesAreScaled_WithSameDraws()
        {
            // 虹：接頭辞・接尾辞とも必ず付く。乱数はどちらも同じ列で引くので、値だけが倍率の分だけ変わる。
            var shallow = EquipmentItem.FromCatalog(ItemCatalog.IronSword, rarity: ItemRarity.Legendary);
            var deep = EquipmentItem.FromCatalog(ItemCatalog.IronSword, rarity: ItemRarity.Legendary);
            new AppraisalSystem(new SequenceRng(1, 1, 5, 1, 1, 5)).RollAffixes(shallow, EquipmentSlot.Weapon, ItemRarity.Legendary, 10);
            new AppraisalSystem(new SequenceRng(1, 1, 5, 1, 1, 5)).RollAffixes(deep, EquipmentSlot.Weapon, ItemRarity.Legendary, 100);

            Assert.Equal(shallow.PrefixId, deep.PrefixId);
            Assert.Equal(shallow.SuffixId, deep.SuffixId);
            Assert.Equal(shallow.PrefixValue * 2, deep.PrefixValue);
            Assert.Equal(shallow.SuffixValue * 2, deep.SuffixValue);
        }

        // ---------------- 霊薬 ----------------

        private static (GameState State, Adventurer A) ElixirState()
        {
            var a = new Adventurer { Name = "アリス", STR = 40, VIT = 50, PA_STR = 60, PA_VIT = 52, AGI = 30, PA_AGI = 80 };
            var state = new GameState { Gold = 100_000, Adventurers = { a } };
            state.CompletedResearchIds.Add(ResearchIds.ElixirBrewing);
            foreach (var recipe in ElixirBalance.Recipes)
                foreach (var (id, n) in recipe.RequiredMaterials)
                    state.AddMaterial(id, n * 10);
            return (state, a);
        }

        [Fact]
        public void ElixirCsv_IsLoaded()
        {
            Assert.Equal(3, ElixirBalance.MaxPerAdventurer);
            Assert.Equal(5, ElixirBalance.Recipes.Count);
            var might = ElixirBalance.Find("elixir_might")!;
            Assert.Equal(new[] { "STR", "VIT" }, might.TargetStats);
            Assert.Equal(5, might.PaBonus);
            Assert.Equal(3, might.StatBonus);
            Assert.Equal(7, ElixirBalance.Find("elixir_awakening")!.TargetStats.Count);
            Assert.All(ElixirBalance.Recipes.SelectMany(r => r.RequiredMaterials.Keys), id => Assert.NotNull(MaterialBalance.Find(id)));
            Assert.Equal(ResearchEffectType.ElixirUnlock, ResearchBalance.Find(ResearchIds.ElixirBrewing)!.EffectType);
        }

        [Fact]
        public void Elixir_RaisesPaAndStat_AndCosts()
        {
            var (state, a) = ElixirState();
            var recipe = ElixirBalance.Find("elixir_might")!;
            int gold = state.Gold;
            int ore = state.Materials["mat_cave_ore"];

            Assert.True(ElixirSystem.TryGive(state, a, "elixir_might"));

            Assert.Equal(65, a.PA_STR);
            Assert.Equal(43, a.STR);
            Assert.Equal(57, a.PA_VIT);
            Assert.Equal(53, a.VIT);
            Assert.Equal(80, a.PA_AGI); // 対象外は変わらない
            Assert.Equal(1, a.ElixirsTaken);
            Assert.Equal(gold - recipe.RequiredGold, state.Gold);
            Assert.Equal(ore - recipe.RequiredMaterials["mat_cave_ore"], state.Materials["mat_cave_ore"]);
        }

        [Fact]
        public void Elixir_RespectsPaCaps()
        {
            var (state, a) = ElixirState();
            a.PA_STR = 98; a.STR = 98;    // 100で止まる。実効値もPAを超えない
            a.PA_VIT = 110; a.VIT = 90;   // 限界突破済みは120まで
            Assert.True(ElixirSystem.TryGive(state, a, "elixir_might"));
            Assert.Equal(100, a.PA_STR);
            Assert.Equal(100, a.STR);
            Assert.Equal(115, a.PA_VIT);
            Assert.Equal(93, a.VIT);
        }

        [Fact]
        public void Elixir_Checks()
        {
            var (state, a) = ElixirState();
            Assert.Equal(ElixirCheck.UnknownElixir, ElixirSystem.Check(state, a, "no_such"));

            a.IsDispatched = true;
            Assert.Equal(ElixirCheck.Dispatched, ElixirSystem.Check(state, a, "elixir_gale"));
            a.IsDispatched = false;

            for (int i = 0; i < ElixirBalance.MaxPerAdventurer; i++)
                Assert.True(ElixirSystem.TryGive(state, a, "elixir_gale"));
            Assert.Equal(ElixirCheck.LimitReached, ElixirSystem.Check(state, a, "elixir_gale"));
            Assert.False(ElixirSystem.TryGive(state, a, "elixir_gale"));

            var b = new Adventurer { Name = "セリア" };
            Assert.Equal(ElixirCheck.NotInRoster, ElixirSystem.Check(state, b, "elixir_gale")); // ロースター外
            state.Adventurers.Add(b);
            state.Gold = 0;
            Assert.Equal(ElixirCheck.NotEnoughGold, ElixirSystem.Check(state, b, "elixir_gale"));
            state.Gold = 100_000;
            state.Materials.Clear();
            Assert.Equal(ElixirCheck.NotEnoughMaterials, ElixirSystem.Check(state, b, "elixir_gale"));

            state.CompletedResearchIds.Clear();
            Assert.Equal(ElixirCheck.NotUnlocked, ElixirSystem.Check(state, b, "elixir_gale"));
        }

        [Fact]
        public void ElixirsTaken_SurvivesSave_AndDefaultsToZero()
        {
            var a = new Adventurer { Name = "アリス", ElixirsTaken = 2 };
            var state = new GameState { Adventurers = { a } };
            var json = JsonSerializer.Serialize(state.ToSaveData());
            Assert.Equal(2, GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!).Adventurers[0].ElixirsTaken);

            var oldJson = json.Replace(",\"ElixirsTaken\":2", "");
            Assert.DoesNotContain("ElixirsTaken", oldJson);
            Assert.Equal(0, GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(oldJson)!).Adventurers[0].ElixirsTaken);
        }
    }
}
