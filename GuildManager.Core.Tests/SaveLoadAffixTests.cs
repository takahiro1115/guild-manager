using System.IO;
using System.Linq;
using System.Text.Json;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// アフィックス付き装備のセーブ・ロード（→ SaveData.Armory・Adventurer.EquippedWeapon 等、03 §12・§0.39）のテスト。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~SaveLoadAffix`
    /// </summary>
    public class SaveLoadAffixTests
    {
        private static EquipmentItem MakeAffixedSword()
        {
            var sword = EquipmentItem.FromCatalog(ItemCatalog.IronSword, acquiredAtWeek: 12, acquiredFrom: "森 第12層の遺物を鑑定", rarity: ItemRarity.Epic);
            sword.ApplyAffix(AffixBalance.FindById("PrefixMightT2")!, 4);   // STR+4
            sword.ApplyAffix(AffixBalance.FindById("SuffixColossusT2")!, 22); // 最大HP+22
            return sword;
        }

        private static EquipmentItem MakeAffixedAmulet()
        {
            var amulet = EquipmentItem.FromCatalog(ItemCatalog.LifeAmulet, rarity: ItemRarity.Legendary);
            amulet.ApplyAffix(AffixBalance.FindById("PrefixLightT3")!, 6);   // INT+6
            amulet.ApplyAffix(AffixBalance.FindById("SuffixDivineT3")!, 5);  // INT+5（合算でINT+11）
            return amulet;
        }

        private static void AssertSameAffixes(EquipmentItem expected, EquipmentItem actual)
        {
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.ItemId, actual.ItemId);
            Assert.Equal(expected.Rarity, actual.Rarity);
            Assert.Equal(expected.PrefixId, actual.PrefixId);
            Assert.Equal(expected.PrefixValue, actual.PrefixValue);
            Assert.Equal(expected.SuffixId, actual.SuffixId);
            Assert.Equal(expected.SuffixValue, actual.SuffixValue);
            Assert.Equal(expected.AffixStatBonuses.OrderBy(p => p.Key), actual.AffixStatBonuses.OrderBy(p => p.Key));
            Assert.Equal(expected.AffixHpBonus, actual.AffixHpBonus);
            Assert.Equal(expected.DisplayName, actual.DisplayName);
            Assert.Equal(expected.DescribeEffects(), actual.DescribeEffects());
        }

        [Fact]
        public void RoundTrip_ThroughSaveFile_PreservesAffixesInArmoryAndOnAdventurer()
        {
            var dir = Path.Combine(Path.GetTempPath(), "GuildManagerAffixTest_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var warrior = new Adventurer { Name = "テスト重戦士", JobClass = JobClass.Warrior, STR = 40, VIT = 40, INT = 30 };
                var sword = MakeAffixedSword();
                var amulet = MakeAffixedAmulet();
                var state = new GameState { Adventurers = { warrior }, Armory = { sword, amulet } };
                Assert.True(new EquipmentSystem().TryEquip(state, warrior, EquipmentSlot.Weapon, sword));
                double strBefore = warrior.GetEffectiveStat("STR");
                int maxHpBefore = warrior.MaxHP;

                var service = new SaveLoadService(dir);
                service.Save(state);
                var loaded = service.Load()!;

                // 装備中の個体
                var restoredWarrior = Assert.Single(loaded.Adventurers);
                AssertSameAffixes(sword, restoredWarrior.EquippedWeapon!);
                Assert.Equal("怪力の鉄の剣［巨像］", restoredWarrior.EquippedWeapon!.DisplayName);
                Assert.Equal(strBefore, restoredWarrior.GetEffectiveStat("STR"));
                Assert.Equal(maxHpBefore, restoredWarrior.MaxHP);

                // 保管庫の個体
                var restoredAmulet = Assert.Single(loaded.Armory);
                AssertSameAffixes(amulet, restoredAmulet);
                Assert.Equal("天光の生命のお守り［神威］", restoredAmulet.DisplayName);
                Assert.Equal(11, restoredAmulet.GetAffixStatBonus("INT"));
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        [Fact]
        public void Json_DoesNotWriteDerivedProperties()
        {
            var json = JsonSerializer.Serialize(MakeAffixedSword());

            Assert.Contains("\"PrefixId\":\"PrefixMightT2\"", json);
            Assert.Contains("\"AffixHpBonus\":22", json);
            Assert.DoesNotContain("DisplayName", json);
            Assert.DoesNotContain("HasAffix", json);
        }

        [Fact]
        public void LegacyEquipmentJson_WithoutAffixKeys_LoadsAsNoAffix()
        {
            // §0.39以前の個体：アフィックスのキーを一切持たない
            var json = "{\"Id\":\"8b0b3c7e-6a55-4d8c-9d9e-3a1d6c0d9f10\",\"ItemId\":\"IronSword\",\"Name\":\"鉄の剣\",\"AcquiredAtWeek\":3,\"AcquiredFrom\":\"鑑定\",\"Rarity\":1}";

            var restored = JsonSerializer.Deserialize<EquipmentItem>(json)!;

            Assert.False(restored.HasAffix);
            Assert.Null(restored.PrefixId);
            Assert.Null(restored.SuffixId);
            Assert.NotNull(restored.AffixStatBonuses);
            Assert.Empty(restored.AffixStatBonuses);
            Assert.Equal(0, restored.AffixHpBonus);
            Assert.Equal("鉄の剣", restored.DisplayName);
            Assert.Equal(ItemCatalog.IronSword.DescribeEffects(), restored.DescribeEffects());
        }

        [Fact]
        public void UnknownAffixId_KeepsBakedBonus_AndOmitsNameOnly()
        {
            // affixes.csv から行を消しても、既に掘り出した個体の補正は個体側に焼き付いたまま残る
            var json = "{\"ItemId\":\"IronSword\",\"Name\":\"鉄の剣\",\"PrefixId\":\"RemovedAffix\",\"PrefixValue\":4,\"AffixStatBonuses\":{\"STR\":4}}";

            var restored = JsonSerializer.Deserialize<EquipmentItem>(json)!;

            Assert.True(restored.HasAffix);
            Assert.Equal("鉄の剣", restored.DisplayName);
            Assert.Equal(4, restored.GetAffixStatBonus("STR"));
            Assert.EndsWith("[RemovedAffix: +4]", restored.DescribeEffects());
        }
    }
}
