using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 武具の7大能力値補正（→ Item.StatBonuses、Adventurer.GetEffectiveStat、03 §4.2.2）のテスト。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~EquipmentStat`
    /// </summary>
    public class EquipmentStatTests
    {
        private static Adventurer MakeAdventurer(JobClass job) => new()
        {
            JobClass = job,
            STR = 40, VIT = 40, AGI = 40, DEX = 40, INT = 40, MND = 40, LDR = 40,
        };

        [Fact]
        public void Equipping_WeaponAndArmor_RaisesEffectiveStats_ByTheirBonuses()
        {
            var ranger = MakeAdventurer(JobClass.Ranger);
            var state = new GameState { Gold = 10000 };
            var system = new EquipmentSystem();

            Assert.True(system.TryPurchaseAndEquip(state, ranger, ItemCatalog.DaggerId));       // DEX+5, AGI+2
            Assert.True(system.TryPurchaseAndEquip(state, ranger, ItemCatalog.ScholarCoatId));  // INT+3, DEX+2

            Assert.Equal(40 + 5 + 2, ranger.GetEffectiveStat("DEX"));
            Assert.Equal(40 + 2, ranger.GetEffectiveStat("AGI"));
            Assert.Equal(40 + 3, ranger.GetEffectiveStat("INT"));
            Assert.Equal(40, ranger.GetEffectiveStat("STR")); // 補正の無い能力値は変わらない
            Assert.Equal(7, ranger.GetEquipmentStatBonus("DEX"));
        }

        [Fact]
        public void Unequipping_RestoresBaseStats()
        {
            var warrior = MakeAdventurer(JobClass.Warrior);
            var state = new GameState { Gold = 10000 };
            var system = new EquipmentSystem();
            system.TryPurchaseAndEquip(state, warrior, ItemCatalog.GreatSwordId); // STR+6, VIT+2
            Assert.Equal(46, warrior.GetEffectiveStat("STR"));

            Assert.True(system.TryUnequip(state, warrior, EquipmentSlot.Weapon));

            Assert.Equal(40, warrior.GetEffectiveStat("STR"));
            Assert.Equal(40, warrior.GetEffectiveStat("VIT"));
            Assert.Equal(0, warrior.GetEquipmentStatBonus("STR"));
        }

        [Fact]
        public void MaxHP_IncludesArmorHpBonus_AndVitBonus()
        {
            var knight = MakeAdventurer(JobClass.Knight);
            int before = knight.MaxHP;

            knight.SetEquippedId(EquipmentSlot.Armor, ItemCatalog.PlateArmorId); // HP+50, VIT+10

            int vitHp = (int)(50 * CombatBalance.MaxHpVitCoefficient) - (int)(40 * CombatBalance.MaxHpVitCoefficient);
            Assert.Equal(before + 50 + vitHp, knight.MaxHP);
        }

        [Fact]
        public void TraitReduction_AppliesToBaseOnly_NotToEquipmentBonus()
        {
            // 古傷（STR/VIT/AGI/DEX -15%）を負っていても、装備の固定補正は目減りしない。
            var warrior = MakeAdventurer(JobClass.Warrior);
            warrior.STR = 100;
            warrior.TraitIds.Add(TraitCatalog.OldWoundId);
            warrior.SetEquippedId(EquipmentSlot.Weapon, ItemCatalog.GreatSwordId); // STR+6

            Assert.Equal(85.0 + 6, warrior.GetEffectiveStat("STR"));
        }

        [Theory]
        [InlineData(ItemCatalog.DaggerId, JobClass.Warrior)]
        [InlineData(ItemCatalog.HuntingBowId, JobClass.Mage)]
        [InlineData(ItemCatalog.SpearId, JobClass.Cleric)]
        [InlineData(ItemCatalog.MaceId, JobClass.Thief)]
        [InlineData(ItemCatalog.WarhammerId, JobClass.Ranger)]
        [InlineData(ItemCatalog.GrimoireId, JobClass.Knight)]
        [InlineData(ItemCatalog.ScholarCoatId, JobClass.Warrior)]
        [InlineData(ItemCatalog.ChainmailId, JobClass.Mage)]
        [InlineData(ItemCatalog.PlateArmorId, JobClass.Cleric)]
        public void NewItems_RejectDisallowedJobs(string itemId, JobClass job)
        {
            var adventurer = MakeAdventurer(job);
            var state = new GameState { Gold = 10000 };

            Assert.False(new EquipmentSystem().TryPurchaseAndEquip(state, adventurer, itemId));
            Assert.Equal(10000, state.Gold); // 失敗時は代金を取らない
            Assert.Equal(40, adventurer.GetEffectiveStat("STR"));
        }

        [Theory]
        [InlineData(ItemCatalog.DaggerId, JobClass.Scholar)]
        [InlineData(ItemCatalog.HuntingBowId, JobClass.Thief)]
        [InlineData(ItemCatalog.SpearId, JobClass.Ranger)]
        [InlineData(ItemCatalog.MaceId, JobClass.Knight)]
        [InlineData(ItemCatalog.WarhammerId, JobClass.Cleric)]
        [InlineData(ItemCatalog.GrimoireId, JobClass.Cleric)]
        [InlineData(ItemCatalog.ScholarCoatId, JobClass.Mage)]
        [InlineData(ItemCatalog.ChainmailId, JobClass.Ranger)]
        [InlineData(ItemCatalog.PlateArmorId, JobClass.Knight)]
        public void NewItems_AcceptAllowedJobs(string itemId, JobClass job)
        {
            var adventurer = MakeAdventurer(job);
            var state = new GameState { Gold = 10000 };

            Assert.True(new EquipmentSystem().TryPurchaseAndEquip(state, adventurer, itemId));
        }
    }
}
