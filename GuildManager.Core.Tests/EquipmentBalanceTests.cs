using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// EquipmentBalance（→ docs/04_バランス表/equipment.csv、項目58フォローアップ）のテスト。
    /// CSV由来の値が旧・直書き値と一致すること、およびItemCatalog側のItemへ正しく
    /// 反映されていることを確認する。
    /// </summary>
    public class EquipmentBalanceTests
    {
        [Fact]
        public void AllCatalogItems_MatchFormerHardcodedValues()
        {
            Assert.Equal(200, ItemCatalog.IronSword.Price);
            Assert.Equal(10, ItemCatalog.IronSword.EffectValue);

            Assert.Equal(500, ItemCatalog.GreatSword.Price);
            Assert.Equal(20, ItemCatalog.GreatSword.EffectValue);

            Assert.Equal(400, ItemCatalog.MageStaff.Price);
            Assert.Equal(15, ItemCatalog.MageStaff.EffectValue);

            Assert.Equal(200, ItemCatalog.LeatherArmor.Price);
            Assert.Equal(20, ItemCatalog.LeatherArmor.EffectValue);

            Assert.Equal(600, ItemCatalog.HeavyArmor.Price);
            Assert.Equal(40, ItemCatalog.HeavyArmor.EffectValue);

            Assert.Equal(250, ItemCatalog.Robe.Price);
            Assert.Equal(15, ItemCatalog.Robe.EffectValue);

            Assert.Equal(300, ItemCatalog.PowerRing.Price);
            Assert.Equal(8, ItemCatalog.PowerRing.EffectValue);

            Assert.Equal(300, ItemCatalog.LifeAmulet.Price);
            Assert.Equal(15, ItemCatalog.LifeAmulet.EffectValue);

            Assert.Equal(300, ItemCatalog.QuickBrooch.Price);
            Assert.Equal(8, ItemCatalog.QuickBrooch.EffectValue);

            Assert.Equal(300, ItemCatalog.GuardCharm.Price);
            Assert.Equal(15, ItemCatalog.GuardCharm.EffectValue);
        }

        [Fact]
        public void EquipmentBalanceFields_MatchItemCatalog()
        {
            // EquipmentBalanceの各フィールドがItemCatalogの対応するItemへ正しく
            // 配線されていること（キー名の取り違え防止）。
            Assert.Equal(EquipmentBalance.IronSwordPrice, ItemCatalog.IronSword.Price);
            Assert.Equal(EquipmentBalance.IronSwordEffectValue, ItemCatalog.IronSword.EffectValue);
            Assert.Equal(EquipmentBalance.GreatSwordPrice, ItemCatalog.GreatSword.Price);
            Assert.Equal(EquipmentBalance.GreatSwordEffectValue, ItemCatalog.GreatSword.EffectValue);
            Assert.Equal(EquipmentBalance.MageStaffPrice, ItemCatalog.MageStaff.Price);
            Assert.Equal(EquipmentBalance.MageStaffEffectValue, ItemCatalog.MageStaff.EffectValue);
            Assert.Equal(EquipmentBalance.LeatherArmorPrice, ItemCatalog.LeatherArmor.Price);
            Assert.Equal(EquipmentBalance.LeatherArmorEffectValue, ItemCatalog.LeatherArmor.EffectValue);
            Assert.Equal(EquipmentBalance.HeavyArmorPrice, ItemCatalog.HeavyArmor.Price);
            Assert.Equal(EquipmentBalance.HeavyArmorEffectValue, ItemCatalog.HeavyArmor.EffectValue);
            Assert.Equal(EquipmentBalance.RobePrice, ItemCatalog.Robe.Price);
            Assert.Equal(EquipmentBalance.RobeEffectValue, ItemCatalog.Robe.EffectValue);
            Assert.Equal(EquipmentBalance.PowerRingPrice, ItemCatalog.PowerRing.Price);
            Assert.Equal(EquipmentBalance.PowerRingEffectValue, ItemCatalog.PowerRing.EffectValue);
            Assert.Equal(EquipmentBalance.LifeAmuletPrice, ItemCatalog.LifeAmulet.Price);
            Assert.Equal(EquipmentBalance.LifeAmuletEffectValue, ItemCatalog.LifeAmulet.EffectValue);
            Assert.Equal(EquipmentBalance.QuickBroochPrice, ItemCatalog.QuickBrooch.Price);
            Assert.Equal(EquipmentBalance.QuickBroochEffectValue, ItemCatalog.QuickBrooch.EffectValue);
            Assert.Equal(EquipmentBalance.GuardCharmPrice, ItemCatalog.GuardCharm.Price);
            Assert.Equal(EquipmentBalance.GuardCharmEffectValue, ItemCatalog.GuardCharm.EffectValue);
        }
    }
}
