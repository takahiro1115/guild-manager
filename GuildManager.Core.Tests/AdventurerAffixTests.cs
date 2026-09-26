using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// アフィックス付き武具の実効値・最大HPへの反映（→ Adventurer.GetEquipmentStatBonus・GetEquipmentHpBonus、
    /// 03 §4.2.2・§0.39）と、個体の表示名・効果説明（→ EquipmentItem.DisplayName・DescribeEffects）のテスト。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~AdventurerAffix`
    /// </summary>
    public class AdventurerAffixTests
    {
        private static Adventurer MakeWarrior() => new()
        {
            JobClass = JobClass.Warrior,
            STR = 40, VIT = 40, AGI = 40, DEX = 40, INT = 40, MND = 40, LDR = 40,
        };

        /// <summary>怪力の鉄の剣［巨像］（STR+3・最大HP+20）。</summary>
        private static EquipmentItem MakeAffixedSword()
        {
            var sword = EquipmentItem.FromCatalog(ItemCatalog.IronSword, rarity: ItemRarity.Epic);
            sword.ApplyAffix(AffixBalance.FindById("PrefixMightT2")!, 3);
            sword.ApplyAffix(AffixBalance.FindById("SuffixColossusT2")!, 20);
            return sword;
        }

        [Fact]
        public void EquippingAffixedWeapon_AddsAffixToEffectiveStat_AndMaxHp()
        {
            var warrior = MakeWarrior();
            double strBase = warrior.GetEffectiveStat("STR");
            int hpBase = warrior.MaxHP;

            // 比較用：同じカタログの無銘品を着けたときの値
            var plain = EquipmentItem.FromCatalog(ItemCatalog.IronSword);
            var plainState = new GameState { Armory = { plain } };
            var plainWarrior = MakeWarrior();
            Assert.True(new EquipmentSystem().TryEquip(plainState, plainWarrior, EquipmentSlot.Weapon, plain));

            var sword = MakeAffixedSword();
            var state = new GameState { Armory = { sword } };
            Assert.True(new EquipmentSystem().TryEquip(state, warrior, EquipmentSlot.Weapon, sword));

            Assert.Equal(plainWarrior.GetEffectiveStat("STR") + 3, warrior.GetEffectiveStat("STR"));
            Assert.Equal(strBase + ItemCatalog.IronSword.GetStatBonus("STR") + 3, warrior.GetEffectiveStat("STR"));
            Assert.Equal(ItemCatalog.IronSword.GetStatBonus("STR") + 3, warrior.GetEquipmentStatBonus("STR"));
            Assert.Equal(plainWarrior.GetEquipmentHpBonus() + 20, warrior.GetEquipmentHpBonus());

            // 最大HP＝VIT由来＋装備のHP加算。剣のVIT補正は無銘品と同じなので、差はアフィックスの+20だけ
            Assert.Equal(plainWarrior.MaxHP + 20, warrior.MaxHP);
            Assert.True(warrior.MaxHP > hpBase);

            // 補正の無い能力値は無銘品と変わらない
            Assert.Equal(plainWarrior.GetEffectiveStat("INT"), warrior.GetEffectiveStat("INT"));
        }

        [Fact]
        public void UnequippingAffixedWeapon_RestoresBaseValues()
        {
            var warrior = MakeWarrior();
            double strBase = warrior.GetEffectiveStat("STR");
            int hpBase = warrior.MaxHP;
            var sword = MakeAffixedSword();
            var state = new GameState { Armory = { sword } };
            var system = new EquipmentSystem();

            Assert.True(system.TryEquip(state, warrior, EquipmentSlot.Weapon, sword));
            Assert.True(system.TryUnequip(state, warrior, EquipmentSlot.Weapon));

            Assert.Equal(strBase, warrior.GetEffectiveStat("STR"));
            Assert.Equal(hpBase, warrior.MaxHP);
            Assert.Equal(0, warrior.GetEquipmentHpBonus());
            Assert.Contains(sword, state.Armory);
            Assert.Equal(3, sword.GetAffixStatBonus("STR")); // 個体の補正は外しても失われない
        }

        [Fact]
        public void AffixesOnSeveralSlots_AreSummed()
        {
            var warrior = MakeWarrior();
            var sword = MakeAffixedSword();                                   // STR+3・HP+20
            var armor = EquipmentItem.FromCatalog(ItemCatalog.LeatherArmor, rarity: ItemRarity.Rare);
            armor.ApplyAffix(AffixBalance.FindById("PrefixMightT1")!, 2);     // STR+2
            armor.ApplyAffix(AffixBalance.FindById("SuffixGiantT1")!, 7);     // HP+7
            var state = new GameState { Armory = { sword, armor } };
            var system = new EquipmentSystem();

            Assert.True(system.TryEquip(state, warrior, EquipmentSlot.Weapon, sword));
            Assert.True(system.TryEquip(state, warrior, EquipmentSlot.Armor, armor));

            int catalogStr = ItemCatalog.IronSword.GetStatBonus("STR") + ItemCatalog.LeatherArmor.GetStatBonus("STR");
            Assert.Equal(catalogStr + 3 + 2, warrior.GetEquipmentStatBonus("STR"));
            Assert.Equal(ItemCatalog.LeatherArmor.MaxHpBonus + 20 + 7, warrior.GetEquipmentHpBonus());
        }

        [Fact]
        public void SamePositionTwice_Throws()
        {
            var sword = MakeAffixedSword();
            Assert.Throws<System.InvalidOperationException>(() => sword.ApplyAffix(AffixBalance.FindById("PrefixMightT1")!, 1));
        }

        // ---------------- 表示名・効果説明 ----------------

        [Fact]
        public void DisplayName_ComposesPrefixCatalogNameAndSuffix()
        {
            var both = MakeAffixedSword();
            Assert.Equal("怪力の鉄の剣［巨像］", both.DisplayName);
            Assert.True(both.HasAffix);

            var prefixOnly = EquipmentItem.FromCatalog(ItemCatalog.IronSword, rarity: ItemRarity.Common);
            prefixOnly.ApplyAffix(AffixBalance.FindById("PrefixMightT1")!, 1);
            Assert.Equal("剛力の鉄の剣", prefixOnly.DisplayName);

            var suffixOnly = EquipmentItem.FromCatalog(ItemCatalog.IronSword, rarity: ItemRarity.Rare);
            suffixOnly.ApplyAffix(AffixBalance.FindById("SuffixGiantT1")!, 5);
            Assert.Equal("鉄の剣［巨躯］", suffixOnly.DisplayName);

            var plain = EquipmentItem.FromCatalog(ItemCatalog.IronSword);
            Assert.Equal("鉄の剣", plain.DisplayName);
            Assert.False(plain.HasAffix);
        }

        [Fact]
        public void DescribeEffects_AppendsAffixBonusesToCatalogEffects()
        {
            var sword = MakeAffixedSword();
            Assert.Equal("STR+2・VIT+1 [怪力: STR+3] [巨像: 最大HP+20]", sword.DescribeEffects());

            var plain = EquipmentItem.FromCatalog(ItemCatalog.IronSword);
            Assert.Equal(ItemCatalog.IronSword.DescribeEffects(), plain.DescribeEffects());
        }
    }
}
