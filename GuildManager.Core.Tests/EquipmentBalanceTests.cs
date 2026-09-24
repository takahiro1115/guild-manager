using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// EquipmentBalance（→ docs/04_バランス表/equipment.csv、テーブル形式）のテスト。
    /// CSV由来の値（2026年9月、武具の7大能力値補正の仮値）がItemCatalog側のItemへ
    /// 正しく反映されていることを確認する。
    /// </summary>
    public class EquipmentBalanceTests
    {
        [Theory]
        //           Id                          Price CP/HP STR VIT AGI DEX INT MND LDR
        [InlineData(ItemCatalog.IronSwordId,     100,  8,  2,  1,  0,  0,  0,  0,  0)]
        [InlineData(ItemCatalog.DaggerId,         80,  6,  0,  0,  2,  5,  0,  0,  0)]
        [InlineData(ItemCatalog.HuntingBowId,    140,  9,  0,  0,  4,  2,  0,  0,  0)]
        [InlineData(ItemCatalog.SpearId,         180, 11,  3,  3,  0,  0,  0,  0,  0)]
        [InlineData(ItemCatalog.GreatSwordId,    200, 14,  6,  2,  0,  0,  0,  0,  0)]
        [InlineData(ItemCatalog.MaceId,          150,  7,  2,  0,  0,  0,  0,  5,  0)]
        [InlineData(ItemCatalog.WarhammerId,     210, 12,  4,  0,  0,  0,  0,  3,  0)]
        [InlineData(ItemCatalog.MageStaffId,     160,  9,  0,  0,  0,  0,  6,  0,  0)]
        [InlineData(ItemCatalog.GrimoireId,      220, 11,  0,  0,  0,  0,  4,  4,  0)]
        [InlineData(ItemCatalog.LeatherArmorId,   80, 15,  0,  0,  2,  0,  0,  0,  0)]
        [InlineData(ItemCatalog.ScholarCoatId,   130, 15,  0,  0,  0,  2,  3,  0,  0)]
        [InlineData(ItemCatalog.RobeId,          100, 10,  0,  0,  0,  0,  3,  3,  0)]
        [InlineData(ItemCatalog.ChainmailId,     160, 25,  0,  3,  0,  0,  0,  0,  0)]
        [InlineData(ItemCatalog.HeavyArmorId,    200, 35,  1,  6,  0,  0,  0,  0,  0)]
        [InlineData(ItemCatalog.PlateArmorId,    300, 50,  2, 10,  0,  0,  0,  0,  0)]
        [InlineData(ItemCatalog.PowerRingId,     300,  8,  0,  0,  0,  0,  0,  0,  0)]
        [InlineData(ItemCatalog.LifeAmuletId,    300, 15,  0,  0,  0,  0,  0,  0,  0)]
        [InlineData(ItemCatalog.QuickBroochId,   300,  8,  0,  0,  0,  0,  0,  0,  0)]
        [InlineData(ItemCatalog.GuardCharmId,    300, 15,  0,  0,  0,  0,  0,  0,  0)]
        public void CatalogItems_MatchCsvValues(string id, int price, int effect,
            int str, int vit, int agi, int dex, int intel, int mnd, int ldr)
        {
            var item = ItemCatalog.FindById(id)!;

            Assert.Equal(price, item.Price);
            Assert.Equal(effect, item.EffectValue);
            Assert.Equal(str, item.GetStatBonus("STR"));
            Assert.Equal(vit, item.GetStatBonus("VIT"));
            Assert.Equal(agi, item.GetStatBonus("AGI"));
            Assert.Equal(dex, item.GetStatBonus("DEX"));
            Assert.Equal(intel, item.GetStatBonus("INT"));
            Assert.Equal(mnd, item.GetStatBonus("MND"));
            Assert.Equal(ldr, item.GetStatBonus("LDR"));
        }

        [Fact]
        public void EveryCatalogItem_IsWiredToItsOwnCsvRow()
        {
            // ItemCatalogの各Itemが、自分のIdの行の値を持っていること（行の取り違え防止）。
            foreach (var item in ItemCatalog.GetAll())
            {
                var stats = EquipmentBalance.Get(item.Id);
                Assert.Equal(stats.Price, item.Price);
                Assert.Equal(stats.EffectValue, item.EffectValue);
                Assert.Same(stats.StatBonuses, item.StatBonuses);
            }
        }

        [Fact]
        public void Get_UnknownId_Throws()
        {
            Assert.Throws<BalanceDataException>(() => EquipmentBalance.Get("__NoSuchItem__"));
        }

        [Fact]
        public void OnlyHeavyArmorAndPlateArmor_AreHeavy()
        {
            var heavy = ItemCatalog.GetAll().Where(i => i.IsHeavyArmor).Select(i => i.Id).OrderBy(i => i);
            Assert.Equal(new[] { ItemCatalog.HeavyArmorId, ItemCatalog.PlateArmorId }.OrderBy(i => i), heavy);
        }
    }
}
