using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 装備システム（4枠・職業制限・即時購入）のテスト（仕様書 03 §4.2.2）。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class EquipmentSystemTests
    {
        // ---------------- 購入・装備（TryPurchaseAndEquip） ----------------

        [Fact]
        public void TryPurchaseAndEquip_Succeeds_ForAllowedJobAndSufficientGold()
        {
            var adventurer = new Adventurer { JobClass = JobClass.Warrior };
            var state = new GameState { Gold = 1000 };
            var system = new EquipmentSystem();

            bool result = system.TryPurchaseAndEquip(state, adventurer, ItemCatalog.IronSwordId);

            Assert.True(result);
            Assert.Equal(ItemCatalog.IronSwordId, adventurer.EquippedWeaponId);
            Assert.Equal(1000 - ItemCatalog.IronSword.Price, state.Gold);
        }

        [Fact]
        public void TryPurchaseAndEquip_Fails_WhenGoldInsufficient()
        {
            var adventurer = new Adventurer { JobClass = JobClass.Warrior };
            var state = new GameState { Gold = 0 };
            var system = new EquipmentSystem();

            bool result = system.TryPurchaseAndEquip(state, adventurer, ItemCatalog.IronSwordId);

            Assert.False(result);
            Assert.Null(adventurer.EquippedWeaponId);
            Assert.Equal(0, state.Gold); // 変化しない
        }

        [Fact]
        public void TryPurchaseAndEquip_Fails_ForUnknownItemId()
        {
            var adventurer = new Adventurer { JobClass = JobClass.Warrior };
            var state = new GameState { Gold = 10000 };
            var system = new EquipmentSystem();

            bool result = system.TryPurchaseAndEquip(state, adventurer, "NoSuchItem");

            Assert.False(result);
            Assert.Equal(10000, state.Gold);
        }

        [Fact]
        public void TryPurchaseAndEquip_Overwrites_ExistingEquipmentInSameSlot_WithoutRefund()
        {
            var adventurer = new Adventurer { JobClass = JobClass.Warrior };
            var state = new GameState { Gold = 10000 };
            var system = new EquipmentSystem();
            system.TryPurchaseAndEquip(state, adventurer, ItemCatalog.IronSwordId);
            int goldAfterFirst = state.Gold;

            bool result = system.TryPurchaseAndEquip(state, adventurer, ItemCatalog.GreatSwordId);

            Assert.True(result);
            Assert.Equal(ItemCatalog.GreatSwordId, adventurer.EquippedWeaponId); // 上書きされる
            Assert.Equal(goldAfterFirst - ItemCatalog.GreatSword.Price, state.Gold); // 返金はしない
        }

        // ---------------- 職業制限 ----------------

        [Fact]
        public void TryPurchaseAndEquip_Fails_WhenJobClassNotAllowed()
        {
            // 大剣(GreatSword)は戦士専用。魔導士は装備できない。
            var mage = new Adventurer { JobClass = JobClass.Mage };
            var state = new GameState { Gold = 10000 };
            var system = new EquipmentSystem();

            bool result = system.TryPurchaseAndEquip(state, mage, ItemCatalog.GreatSwordId);

            Assert.False(result);
            Assert.Null(mage.EquippedWeaponId);
            Assert.Equal(10000, state.Gold); // 装備できなければ課金もされない
        }

        [Fact]
        public void TryPurchaseAndEquip_Fails_WhenMageEquipsHeavyArmor()
        {
            var mage = new Adventurer { JobClass = JobClass.Mage };
            var state = new GameState { Gold = 10000 };
            var system = new EquipmentSystem();

            bool result = system.TryPurchaseAndEquip(state, mage, ItemCatalog.HeavyArmorId);

            Assert.False(result);
            Assert.Null(mage.EquippedArmorId);
        }

        [Fact]
        public void TryPurchaseAndEquip_Succeeds_ForItemWithNoJobRestriction()
        {
            // 職業制限の無いアイテム（AllowedJobsが空）は全職業で装備可能。
            foreach (var job in new[] { JobClass.Warrior, JobClass.Ranger, JobClass.Mage, JobClass.Cleric })
            {
                var adventurer = new Adventurer { JobClass = job };
                var state = new GameState { Gold = 10000 };
                var system = new EquipmentSystem();

                bool result = system.TryPurchaseAndEquip(state, adventurer, ItemCatalog.LeatherArmorId);

                Assert.True(result, $"{job}は革鎧を装備できるはず");
            }
        }

        // ---------------- 4枠の独立性 ----------------

        [Fact]
        public void TryPurchaseAndEquip_AllFourSlots_AreIndependent()
        {
            var adventurer = new Adventurer { JobClass = JobClass.Warrior };
            var state = new GameState { Gold = 10000 };
            var system = new EquipmentSystem();

            system.TryPurchaseAndEquip(state, adventurer, ItemCatalog.IronSwordId);
            system.TryPurchaseAndEquip(state, adventurer, ItemCatalog.LeatherArmorId);
            system.TryPurchaseAndEquip(state, adventurer, ItemCatalog.PowerRingId);
            system.TryPurchaseAndEquip(state, adventurer, ItemCatalog.QuickBroochId);

            Assert.Equal(ItemCatalog.IronSwordId, adventurer.EquippedWeaponId);
            Assert.Equal(ItemCatalog.LeatherArmorId, adventurer.EquippedArmorId);
            Assert.Equal(ItemCatalog.PowerRingId, adventurer.EquippedAccessory1Id);
            Assert.Equal(ItemCatalog.QuickBroochId, adventurer.EquippedAccessory2Id);
        }

        // ---------------- 解除（Unequip） ----------------

        [Fact]
        public void Unequip_ClearsTheSpecifiedSlotOnly()
        {
            var adventurer = new Adventurer { JobClass = JobClass.Warrior };
            var state = new GameState { Gold = 10000 };
            var system = new EquipmentSystem();
            system.TryPurchaseAndEquip(state, adventurer, ItemCatalog.IronSwordId);
            system.TryPurchaseAndEquip(state, adventurer, ItemCatalog.LeatherArmorId);

            system.Unequip(adventurer, EquipmentSlot.Weapon);

            Assert.Null(adventurer.EquippedWeaponId);
            Assert.Equal(ItemCatalog.LeatherArmorId, adventurer.EquippedArmorId); // 他スロットは影響なし
        }

        // ---------------- 個人CP・最大HPへの効果反映（→ Adventurer.GetEquipmentBonus） ----------------

        [Fact]
        public void GetEquipmentBonus_SumsPersonalCpBonus_AcrossWeaponAndAccessories()
        {
            var adventurer = new Adventurer { JobClass = JobClass.Warrior };
            var state = new GameState { Gold = 10000 };
            var system = new EquipmentSystem();
            system.TryPurchaseAndEquip(state, adventurer, ItemCatalog.IronSwordId); // CP+10
            system.TryPurchaseAndEquip(state, adventurer, ItemCatalog.PowerRingId); // CP+8
            system.TryPurchaseAndEquip(state, adventurer, ItemCatalog.QuickBroochId); // CP+8
            system.TryPurchaseAndEquip(state, adventurer, ItemCatalog.LeatherArmorId); // HP側なのでCPには寄与しない

            Assert.Equal(26, adventurer.GetEquipmentBonus(EquipmentEffectType.PersonalCpBonus));
        }

        [Fact]
        public void MaxHP_IncludesEquippedArmorBonus()
        {
            var adventurer = new Adventurer { VIT = 20 };
            int maxHpBefore = adventurer.MaxHP; // 20*2+50=90

            var state = new GameState { Gold = 10000 };
            new EquipmentSystem().TryPurchaseAndEquip(state, adventurer, ItemCatalog.LeatherArmorId); // HP+20

            Assert.Equal(90, maxHpBefore);
            Assert.Equal(110, adventurer.MaxHP);
        }

        [Fact]
        public void MaxHP_UnaffectedByEquippedWeapon_OnlyArmorAndHpAccessoriesCount()
        {
            var adventurer = new Adventurer { VIT = 20, JobClass = JobClass.Warrior };
            var state = new GameState { Gold = 10000 };
            new EquipmentSystem().TryPurchaseAndEquip(state, adventurer, ItemCatalog.IronSwordId); // CP側

            Assert.Equal(90, adventurer.MaxHP); // 20*2+50、武器のCP加算はHPに影響しない
        }

        [Fact]
        public void GetEquipmentBonus_ZeroByDefault_WhenNothingEquipped()
        {
            var adventurer = new Adventurer();

            Assert.Equal(0, adventurer.GetEquipmentBonus(EquipmentEffectType.PersonalCpBonus));
            Assert.Equal(0, adventurer.GetEquipmentBonus(EquipmentEffectType.MaxHpBonus));
        }

        // ---------------- ItemCatalog ----------------

        [Fact]
        public void ItemCatalog_FindById_ReturnsNull_ForUnknownId()
        {
            Assert.Null(ItemCatalog.FindById("NoSuchItem"));
            Assert.Null(ItemCatalog.FindById(null));
        }

        [Fact]
        public void ItemCatalog_GetBySlot_ReturnsOnlyMatchingSlotItems()
        {
            var weapons = ItemCatalog.GetBySlot(EquipmentSlot.Weapon);
            Assert.All(weapons, i => Assert.Equal(EquipmentSlot.Weapon, i.Slot));
            Assert.Contains(weapons, i => i.Id == ItemCatalog.IronSwordId);
        }

        [Fact]
        public void ItemCatalog_Accessory2Items_HaveNoVisualPartId()
        {
            // アクセサリー2は立ち絵側の対応枠が無いため見た目に反映されない（→ 03 §4.2.2）。
            foreach (var item in ItemCatalog.GetBySlot(EquipmentSlot.Accessory2))
                Assert.Null(item.VisualPartId);
        }
    }
}
