using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests.Systems
{
    /// <summary>
    /// ギルド保管庫（→ GameState.Armory）と冒険者の装備枠のあいだの着脱
    /// （→ EquipmentSystem.TryEquip / TryUnequip、03 §4.2.2）の単体テスト。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~Equipment`
    ///
    /// カタログからの即時購入（TryPurchaseAndEquip）側の検証は、従来からある
    /// GuildManager.Core.Tests.EquipmentSystemTests が担当している。
    /// </summary>
    public class EquipmentSystemTests
    {
        private static Adventurer MakeAdventurer(JobClass job = JobClass.Warrior, int vit = 40)
        {
            var a = new Adventurer
            {
                Name = job.ToString(), Age = 20, JobClass = job,
                STR = 40, AGI = 40, VIT = vit, MND = 40, DEX = 40, LDR = 40, INT = 40,
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        /// <summary>保管庫に1点だけ在庫がある状態を作る。</summary>
        private static (GameState State, Adventurer Adventurer, EquipmentItem Item) MakeStateWith(
            Item catalogItem, JobClass job = JobClass.Warrior)
        {
            var adventurer = MakeAdventurer(job);
            var item = EquipmentItem.FromCatalog(catalogItem, acquiredAtWeek: 5, acquiredFrom: "鑑定");
            var state = new GameState { Adventurers = { adventurer }, Armory = { item } };
            return (state, adventurer, item);
        }

        // ---------------- 装備（指示書指定テスト） ----------------

        [Fact]
        public void Equip_MovesItem_FromArmory_ToAdventurer()
        {
            var (state, adventurer, item) = MakeStateWith(ItemCatalog.IronSword);

            Assert.True(new EquipmentSystem().TryEquip(state, adventurer, EquipmentSlot.Weapon, item));

            // 保管庫から抜け、冒険者のスロットに同一個体が入る（個体は消えも増えもしない）。
            Assert.Empty(state.Armory);
            Assert.Same(item, adventurer.EquippedWeapon);
            Assert.Same(item, adventurer.GetEquipped(EquipmentSlot.Weapon));
            Assert.Equal(ItemCatalog.IronSwordId, adventurer.EquippedWeaponId);
        }

        [Fact]
        public void Equip_SwapsOldEquipment_BackToArmory()
        {
            var (state, adventurer, oldSword) = MakeStateWith(ItemCatalog.IronSword);
            var newSword = EquipmentItem.FromCatalog(ItemCatalog.GreatSword);
            state.Armory.Add(newSword);
            var system = new EquipmentSystem();

            Assert.True(system.TryEquip(state, adventurer, EquipmentSlot.Weapon, oldSword));
            Assert.True(system.TryEquip(state, adventurer, EquipmentSlot.Weapon, newSword));

            Assert.Same(newSword, adventurer.EquippedWeapon);
            Assert.Same(oldSword, Assert.Single(state.Armory)); // 旧装備が保管庫へ戻る
        }

        [Fact]
        public void Equip_Fails_WhenJobRestricted()
        {
            // 大剣は重戦士・騎士専用（→ ItemCatalog.GreatSword.AllowedJobs）。魔導士には着せられない。
            var (state, mage, greatSword) = MakeStateWith(ItemCatalog.GreatSword, JobClass.Mage);

            Assert.False(new EquipmentSystem().TryEquip(state, mage, EquipmentSlot.Weapon, greatSword));

            Assert.Null(mage.EquippedWeapon);
            Assert.Same(greatSword, Assert.Single(state.Armory)); // 在庫は動かない
        }

        [Fact]
        public void Equip_Fails_WhenAdventurerDispatched()
        {
            var (state, adventurer, item) = MakeStateWith(ItemCatalog.IronSword);
            adventurer.IsDispatched = true;

            Assert.False(EquipmentSystem.CanChangeEquipment(adventurer));
            Assert.False(new EquipmentSystem().TryEquip(state, adventurer, EquipmentSlot.Weapon, item));

            Assert.Null(adventurer.EquippedWeapon);
            Assert.Single(state.Armory);
        }

        [Fact]
        public void Equip_Fails_WhenSlotDoesNotMatch()
        {
            // 武器の個体を防具枠へ入れようとしても通らない。
            var (state, adventurer, sword) = MakeStateWith(ItemCatalog.IronSword);

            Assert.False(new EquipmentSystem().TryEquip(state, adventurer, EquipmentSlot.Armor, sword));

            Assert.Null(adventurer.EquippedArmor);
            Assert.Single(state.Armory);
        }

        [Fact]
        public void Equip_Fails_WhenItemIsNotInArmory()
        {
            // 保管庫に無い個体（既に他の冒険者が装備中など）は装備できない。
            var adventurer = MakeAdventurer();
            var state = new GameState { Adventurers = { adventurer } };
            var orphan = EquipmentItem.FromCatalog(ItemCatalog.IronSword);

            Assert.False(new EquipmentSystem().TryEquip(state, adventurer, EquipmentSlot.Weapon, orphan));

            Assert.Null(adventurer.EquippedWeapon);
            Assert.Empty(state.Armory);
        }

        [Fact]
        public void Equip_Fails_ForUnknownCatalogId()
        {
            // カタログから引けない個体（カタログから消えた武具の旧データ）は着せない。
            var adventurer = MakeAdventurer();
            var unknown = new EquipmentItem { ItemId = "NoSuchItem", Name = "謎の武具" };
            var state = new GameState { Adventurers = { adventurer }, Armory = { unknown } };

            Assert.False(new EquipmentSystem().TryEquip(state, adventurer, EquipmentSlot.Weapon, unknown));

            Assert.Null(adventurer.EquippedWeapon);
            Assert.Single(state.Armory);
        }

        [Fact]
        public void Equip_AllFourSlots_AreIndependent()
        {
            var adventurer = MakeAdventurer();
            var weapon = EquipmentItem.FromCatalog(ItemCatalog.IronSword);
            var armor = EquipmentItem.FromCatalog(ItemCatalog.LeatherArmor);
            var accessory1 = EquipmentItem.FromCatalog(ItemCatalog.PowerRing);
            var accessory2 = EquipmentItem.FromCatalog(ItemCatalog.QuickBrooch);
            var state = new GameState { Adventurers = { adventurer }, Armory = { weapon, armor, accessory1, accessory2 } };
            var system = new EquipmentSystem();

            Assert.True(system.TryEquip(state, adventurer, EquipmentSlot.Weapon, weapon));
            Assert.True(system.TryEquip(state, adventurer, EquipmentSlot.Armor, armor));
            Assert.True(system.TryEquip(state, adventurer, EquipmentSlot.Accessory1, accessory1));
            Assert.True(system.TryEquip(state, adventurer, EquipmentSlot.Accessory2, accessory2));

            Assert.Empty(state.Armory);
            Assert.Same(weapon, adventurer.EquippedWeapon);
            Assert.Same(armor, adventurer.EquippedArmor);
            Assert.Same(accessory1, adventurer.EquippedAccessory1);
            Assert.Same(accessory2, adventurer.EquippedAccessory2);
        }

        // ---------------- 解除（指示書指定テスト） ----------------

        [Fact]
        public void Unequip_ReturnsItem_ToArmory()
        {
            var (state, adventurer, item) = MakeStateWith(ItemCatalog.IronSword);
            var system = new EquipmentSystem();
            Assert.True(system.TryEquip(state, adventurer, EquipmentSlot.Weapon, item));

            Assert.True(system.TryUnequip(state, adventurer, EquipmentSlot.Weapon));

            Assert.Null(adventurer.EquippedWeapon);
            Assert.Same(item, Assert.Single(state.Armory));
        }

        [Fact]
        public void Unequip_DoesNothing_ForEmptySlot()
        {
            var adventurer = MakeAdventurer();
            var state = new GameState { Adventurers = { adventurer } };

            Assert.False(new EquipmentSystem().TryUnequip(state, adventurer, EquipmentSlot.Weapon));

            Assert.Empty(state.Armory);
        }

        [Fact]
        public void Unequip_Fails_WhenAdventurerDispatched()
        {
            var (state, adventurer, item) = MakeStateWith(ItemCatalog.IronSword);
            var system = new EquipmentSystem();
            Assert.True(system.TryEquip(state, adventurer, EquipmentSlot.Weapon, item));
            adventurer.IsDispatched = true;

            Assert.False(system.TryUnequip(state, adventurer, EquipmentSlot.Weapon));

            Assert.Same(item, adventurer.EquippedWeapon); // 出撃中の部隊の戦力は変えられない
            Assert.Empty(state.Armory);
        }

        // ---------------- 最大HP・個人CPへの反映（指示書指定テスト） ----------------

        [Fact]
        public void Equip_Updates_MaxHp_AndStats()
        {
            var (state, adventurer, armor) = MakeStateWith(ItemCatalog.LeatherArmor);
            var sword = EquipmentItem.FromCatalog(ItemCatalog.IronSword);
            state.Armory.Add(sword);
            var system = new EquipmentSystem();

            int baseMaxHp = adventurer.MaxHP;
            Assert.Equal(0, adventurer.GetEquipmentHpBonus());
            Assert.Equal(0, adventurer.GetEquipmentStatBonus("STR"));

            // 防具（最大HP加算）と武器（能力値補正）をそれぞれ装備する。
            Assert.True(system.TryEquip(state, adventurer, EquipmentSlot.Armor, armor));
            Assert.True(system.TryEquip(state, adventurer, EquipmentSlot.Weapon, sword));

            // 鉄の剣のVIT補正も最大HPに乗る（→ Adventurer.GetEffectiveStat）。
            int swordVitHp = (int)((adventurer.VIT + ItemCatalog.IronSword.GetStatBonus("VIT")) * CombatBalance.MaxHpVitCoefficient)
                - (int)(adventurer.VIT * CombatBalance.MaxHpVitCoefficient);
            Assert.Equal(baseMaxHp + ItemCatalog.LeatherArmor.MaxHpBonus + swordVitHp, adventurer.MaxHP);
            Assert.Equal(ItemCatalog.LeatherArmor.MaxHpBonus, adventurer.GetEquipmentHpBonus());
            Assert.Equal(ItemCatalog.IronSword.GetStatBonus("STR"), adventurer.GetEquipmentStatBonus("STR"));

            // 外せば元の値に戻る（武器も外す）。
            Assert.True(system.TryUnequip(state, adventurer, EquipmentSlot.Weapon));
            Assert.True(system.TryUnequip(state, adventurer, EquipmentSlot.Armor));
            Assert.Equal(baseMaxHp, adventurer.MaxHP);
            Assert.Equal(0, adventurer.GetEquipmentHpBonus());
        }

        [Fact]
        public void Equip_KeepsCurrentHp_WhenMaxHpRises()
        {
            var (state, adventurer, armor) = MakeStateWith(ItemCatalog.LeatherArmor);
            adventurer.CurrentHP = 30;

            Assert.True(new EquipmentSystem().TryEquip(state, adventurer, EquipmentSlot.Armor, armor));

            // 装備で最大HPが増えても現在HPは据え置き（装備で全回復してしまわない）。
            Assert.Equal(30, adventurer.CurrentHP);
        }

        [Fact]
        public void Unequip_ClampsCurrentHp_WhenMaxHpDrops()
        {
            var (state, adventurer, armor) = MakeStateWith(ItemCatalog.LeatherArmor);
            var system = new EquipmentSystem();
            Assert.True(system.TryEquip(state, adventurer, EquipmentSlot.Armor, armor));
            adventurer.CurrentHP = adventurer.MaxHP; // 装備込みの満タン

            Assert.True(system.TryUnequip(state, adventurer, EquipmentSlot.Armor));

            // 最大HPが下がった分、現在HPも丸められる（現在HP＞最大HPの不整合を残さない）。
            Assert.Equal(adventurer.MaxHP, adventurer.CurrentHP);
            Assert.True(adventurer.CurrentHP > 0);
        }

        // ---------------- UI向けの候補抽出 ----------------

        [Fact]
        public void GetEquippableFromArmory_FiltersBySlotAndJob()
        {
            var mage = MakeAdventurer(JobClass.Mage);
            var staff = EquipmentItem.FromCatalog(ItemCatalog.MageStaff);      // 魔法職専用・武器
            var greatSword = EquipmentItem.FromCatalog(ItemCatalog.GreatSword); // 重戦士・騎士専用・武器
            var robe = EquipmentItem.FromCatalog(ItemCatalog.Robe);            // 後衛職・防具
            var state = new GameState { Adventurers = { mage }, Armory = { staff, greatSword, robe } };

            var weapons = EquipmentSystem.GetEquippableFromArmory(state, mage, EquipmentSlot.Weapon);
            var armors = EquipmentSystem.GetEquippableFromArmory(state, mage, EquipmentSlot.Armor);

            Assert.Same(staff, Assert.Single(weapons)); // 大剣は職業制限で除外される
            Assert.Same(robe, Assert.Single(armors));
            Assert.Empty(EquipmentSystem.GetEquippableFromArmory(state, mage, EquipmentSlot.Accessory1));
        }

        [Fact]
        public void GetEquippableFromArmory_ExcludesItemsAlreadyEquipped()
        {
            // 「保管庫にある＝誰も装備していない」という不変条件の確認。
            var (state, adventurer, sword) = MakeStateWith(ItemCatalog.IronSword);
            Assert.Single(EquipmentSystem.GetEquippableFromArmory(state, adventurer, EquipmentSlot.Weapon));

            Assert.True(new EquipmentSystem().TryEquip(state, adventurer, EquipmentSlot.Weapon, sword));

            Assert.Empty(EquipmentSystem.GetEquippableFromArmory(state, adventurer, EquipmentSlot.Weapon));
        }

        [Fact]
        public void Equip_TransfersItem_BetweenAdventurers_ViaArmory()
        {
            // 付け替えは「外す→保管庫→別の冒険者へ装備」の2手で完結し、個体は1つのまま。
            var first = MakeAdventurer();
            var second = MakeAdventurer();
            var sword = EquipmentItem.FromCatalog(ItemCatalog.IronSword);
            var state = new GameState { Adventurers = { first, second }, Armory = { sword } };
            var system = new EquipmentSystem();

            Assert.True(system.TryEquip(state, first, EquipmentSlot.Weapon, sword));
            Assert.False(system.TryEquip(state, second, EquipmentSlot.Weapon, sword)); // 保管庫に無いので不可
            Assert.True(system.TryUnequip(state, first, EquipmentSlot.Weapon));
            Assert.True(system.TryEquip(state, second, EquipmentSlot.Weapon, sword));

            Assert.Null(first.EquippedWeapon);
            Assert.Same(sword, second.EquippedWeapon);
            Assert.Empty(state.Armory);
        }

        // ---------------- 離脱時の一括回収（指示書指定テスト） ----------------

        [Fact]
        public void UnequipAllToArmory_RemovesAllEquipments_AndAddsToArmory()
        {
            var adventurer = MakeAdventurer();
            var weapon = EquipmentItem.FromCatalog(ItemCatalog.IronSword);
            var armor = EquipmentItem.FromCatalog(ItemCatalog.LeatherArmor);
            var accessory1 = EquipmentItem.FromCatalog(ItemCatalog.PowerRing);
            var accessory2 = EquipmentItem.FromCatalog(ItemCatalog.QuickBrooch);
            var state = new GameState
            {
                WeekNumber = 40, Adventurers = { adventurer },
                Armory = { weapon, armor, accessory1, accessory2 },
            };
            var system = new EquipmentSystem();
            Assert.True(system.TryEquip(state, adventurer, EquipmentSlot.Weapon, weapon));
            Assert.True(system.TryEquip(state, adventurer, EquipmentSlot.Armor, armor));
            Assert.True(system.TryEquip(state, adventurer, EquipmentSlot.Accessory1, accessory1));
            Assert.True(system.TryEquip(state, adventurer, EquipmentSlot.Accessory2, accessory2));
            Assert.Empty(state.Armory);

            var recovered = EquipmentSystem.UnequipAllToArmory(state, adventurer);

            // 4枠すべてが空になり、4個体すべてが保管庫へ入る。
            foreach (var slot in Adventurer.AllSlots)
                Assert.Null(adventurer.GetEquipped(slot));
            Assert.Equal(4, recovered.Count);
            Assert.Equal(4, state.Armory.Count);
            foreach (var item in new[] { weapon, armor, accessory1, accessory2 })
            {
                Assert.Contains(item, state.Armory);
                Assert.Contains(item, recovered);
            }

            // 回収時の週と入手経路（既定は「○○から返還」）が記録される。
            Assert.All(recovered, e => Assert.Equal(40, e.AcquiredAtWeek));
            Assert.All(recovered, e => Assert.Equal($"{adventurer.Name}から返還", e.AcquiredFrom));
        }

        [Fact]
        public void UnequipAllToArmory_ReturnsEmpty_WhenNothingEquipped()
        {
            var adventurer = MakeAdventurer();
            var state = new GameState { Adventurers = { adventurer } };

            Assert.Empty(EquipmentSystem.UnequipAllToArmory(state, adventurer));
            Assert.Empty(state.Armory);
        }

        [Fact]
        public void UnequipAllToArmory_CollectsOnlyOccupiedSlots()
        {
            var (state, adventurer, armor) = MakeStateWith(ItemCatalog.LeatherArmor);
            Assert.True(new EquipmentSystem().TryEquip(state, adventurer, EquipmentSlot.Armor, armor));

            var recovered = EquipmentSystem.UnequipAllToArmory(state, adventurer, "テスト回収");

            Assert.Same(armor, Assert.Single(recovered));
            Assert.Equal("テスト回収", armor.AcquiredFrom);
        }

        [Fact]
        public void UnequipAllToArmory_WorksForDispatchedAdventurer()
        {
            // 決戦で強制除籍される冒険者は定義上まだ出撃中。ここで弾いてしまうと
            // 肝心の経路で装備を取りこぼすため、一括回収は出撃中ガードを持たない。
            var (state, adventurer, sword) = MakeStateWith(ItemCatalog.IronSword);
            Assert.True(new EquipmentSystem().TryEquip(state, adventurer, EquipmentSlot.Weapon, sword));
            adventurer.IsDispatched = true;

            Assert.Same(sword, Assert.Single(EquipmentSystem.UnequipAllToArmory(state, adventurer)));
            Assert.Null(adventurer.EquippedWeapon);
        }

        [Fact]
        public void UnequipAllToArmory_ClampsCurrentHp()
        {
            var (state, adventurer, armor) = MakeStateWith(ItemCatalog.LeatherArmor);
            Assert.True(new EquipmentSystem().TryEquip(state, adventurer, EquipmentSlot.Armor, armor));
            adventurer.CurrentHP = adventurer.MaxHP; // 装備込みの満タン

            EquipmentSystem.UnequipAllToArmory(state, adventurer);

            Assert.Equal(adventurer.MaxHP, adventurer.CurrentHP);
        }

        // ---------------- 売却（→ 03 §4.8、指示書指定テスト） ----------------

        [Fact]
        public void GetSellPrice_CalculatesCorrectly()
        {
            // カタログ品（無銘、Rarity=null）は定価の50%（端数切り捨て）。
            var ironSword = EquipmentItem.FromCatalog(ItemCatalog.IronSword);
            Assert.Null(ironSword.Rarity);
            Assert.Equal(ItemCatalog.IronSword.Price / 2, EquipmentSystem.GetSellPrice(ironSword));

            var heavyArmor = EquipmentItem.FromCatalog(ItemCatalog.HeavyArmor);
            Assert.Equal(ItemCatalog.HeavyArmor.Price / 2, EquipmentSystem.GetSellPrice(heavyArmor));

            // 鑑定で出土した個体は希少度ごとの基準額（定価とは無関係）。
            foreach (var rarity in System.Enum.GetValues<ItemRarity>())
            {
                var relic = EquipmentItem.FromCatalog(ItemCatalog.IronSword, rarity: rarity);
                Assert.Equal(RelicBalance.GetSellPrice(rarity), EquipmentSystem.GetSellPrice(relic));
            }

            // 希少度が上がるほど高く売れる。
            Assert.True(RelicBalance.GetSellPrice(ItemRarity.Legendary) > RelicBalance.GetSellPrice(ItemRarity.Epic));
            Assert.True(RelicBalance.GetSellPrice(ItemRarity.Epic) > RelicBalance.GetSellPrice(ItemRarity.Rare));
            Assert.True(RelicBalance.GetSellPrice(ItemRarity.Rare) > RelicBalance.GetSellPrice(ItemRarity.Common));

            // カタログから引けない個体は0（売っても1Gにならない）。
            Assert.Equal(0, EquipmentSystem.GetSellPrice(new EquipmentItem { ItemId = "NoSuchItem", Name = "謎" }));
        }

        [Fact]
        public void SellEquipments_RemovesItemsFromArmory_AndAddsGold()
        {
            var sword = EquipmentItem.FromCatalog(ItemCatalog.IronSword);
            var armor = EquipmentItem.FromCatalog(ItemCatalog.LeatherArmor);
            var relic = EquipmentItem.FromCatalog(ItemCatalog.GreatSword, rarity: ItemRarity.Epic);
            var keep = EquipmentItem.FromCatalog(ItemCatalog.PowerRing);
            var state = new GameState { Gold = 1000, Armory = { sword, armor, relic, keep } };

            int expected = EquipmentSystem.GetSellPrice(sword)
                + EquipmentSystem.GetSellPrice(armor)
                + EquipmentSystem.GetSellPrice(relic);

            Assert.True(EquipmentSystem.TrySellEquipments(
                state, new[] { sword.Id.ToString(), armor.Id.ToString(), relic.Id.ToString() }, out int gold));

            Assert.Equal(expected, gold);
            Assert.Equal(1000 + expected, state.Gold);
            Assert.Same(keep, Assert.Single(state.Armory)); // 指定しなかった在庫は残る
        }

        [Fact]
        public void SellEquipments_Fails_WhenItemIsEquipped()
        {
            // 装備中の個体は保管庫から抜けているため、個体Idを指定しても売れない。
            var (state, adventurer, sword) = MakeStateWith(ItemCatalog.IronSword);
            var spare = EquipmentItem.FromCatalog(ItemCatalog.LeatherArmor);
            state.Armory.Add(spare);
            state.Gold = 1000;
            Assert.True(new EquipmentSystem().TryEquip(state, adventurer, EquipmentSlot.Weapon, sword));

            Assert.False(EquipmentSystem.TrySellEquipments(
                state, new[] { sword.Id.ToString(), spare.Id.ToString() }, out int gold));

            // 全か無か：同時に指定した在庫の武具も売れていない。
            Assert.Equal(0, gold);
            Assert.Equal(1000, state.Gold);
            Assert.Same(sword, adventurer.EquippedWeapon);
            Assert.Same(spare, Assert.Single(state.Armory));
        }

        [Fact]
        public void SellEquipments_Fails_ForUnknownOrDuplicatedIds()
        {
            var sword = EquipmentItem.FromCatalog(ItemCatalog.IronSword);
            var state = new GameState { Gold = 1000, Armory = { sword } };

            // 空の指定。
            Assert.False(EquipmentSystem.TrySellEquipments(state, System.Array.Empty<string>(), out _));

            // 保管庫に無い個体Id。
            Assert.False(EquipmentSystem.TrySellEquipments(state, new[] { System.Guid.NewGuid().ToString() }, out _));

            // 同じ個体Idの重複指定（1個しか無い物を2個売ろうとする）。
            Assert.False(EquipmentSystem.TrySellEquipments(
                state, new[] { sword.Id.ToString(), sword.Id.ToString() }, out _));

            Assert.Single(state.Armory);
            Assert.Equal(1000, state.Gold);
        }

        [Fact]
        public void SellEquipments_SellsAllStockOfOneCatalogItem()
        {
            // 「鉄の剣 ×3 を全売却」に相当する経路（→ UI: InventoryPanel の保管庫タブ）。
            var swords = Enumerable.Range(0, 3).Select(_ => EquipmentItem.FromCatalog(ItemCatalog.IronSword)).ToList();
            var state = new GameState { Gold = 0 };
            state.Armory.AddRange(swords);

            Assert.True(EquipmentSystem.TrySellEquipments(
                state, swords.Select(e => e.Id.ToString()), out int gold));

            Assert.Equal(EquipmentSystem.GetSellPrice(swords[0]) * 3, gold);
            Assert.Empty(state.Armory);
        }

        [Fact]
        public void GroupArmoryForSale_SeparatesCatalogItemsFromRelics()
        {
            // 同じカタログIdでも、カタログ品と鑑定品は売却額の系統が違うため別グループになる。
            var plain1 = EquipmentItem.FromCatalog(ItemCatalog.IronSword);
            var plain2 = EquipmentItem.FromCatalog(ItemCatalog.IronSword);
            var epic = EquipmentItem.FromCatalog(ItemCatalog.IronSword, rarity: ItemRarity.Epic);
            var state = new GameState { Armory = { plain1, plain2, epic } };

            var groups = EquipmentSystem.GroupArmoryForSale(state);

            Assert.Equal(2, groups.Count);
            Assert.Equal(2, groups.Single(g => g.Key.Rarity == null).Count());
            Assert.Single(groups.Single(g => g.Key.Rarity == ItemRarity.Epic));
        }

        // ---------------- 購入経路との整合（個体を破棄しない） ----------------

        [Fact]
        public void TryPurchaseAndEquip_SendsDisplacedEquipment_ToArmory()
        {
            // 2026年9月改訂：購入で押し出された旧装備も保管庫へ戻る（以前は消滅していた）。
            var adventurer = MakeAdventurer();
            var state = new GameState { Gold = 10000, Adventurers = { adventurer } };
            var system = new EquipmentSystem();

            Assert.True(system.TryPurchaseAndEquip(state, adventurer, ItemCatalog.IronSwordId));
            Assert.Empty(state.Armory);

            Assert.True(system.TryPurchaseAndEquip(state, adventurer, ItemCatalog.GreatSwordId));

            Assert.Equal(ItemCatalog.GreatSwordId, adventurer.EquippedWeaponId);
            Assert.Equal(ItemCatalog.IronSwordId, Assert.Single(state.Armory).ItemId);
        }

        [Fact]
        public void TryPurchaseAndEquip_Fails_WhenAdventurerDispatched()
        {
            var adventurer = MakeAdventurer();
            adventurer.IsDispatched = true;
            var state = new GameState { Gold = 10000, Adventurers = { adventurer } };

            Assert.False(new EquipmentSystem().TryPurchaseAndEquip(state, adventurer, ItemCatalog.IronSwordId));

            Assert.Equal(10000, state.Gold);
            Assert.Null(adventurer.EquippedWeapon);
        }

        [Fact]
        public void PurchasedEquipment_RecordsAcquisitionMetadata()
        {
            var adventurer = MakeAdventurer();
            var state = new GameState { Gold = 10000, WeekNumber = 17, Adventurers = { adventurer } };

            Assert.True(new EquipmentSystem().TryPurchaseAndEquip(state, adventurer, ItemCatalog.IronSwordId));

            var equipped = adventurer.EquippedWeapon!;
            Assert.Equal(17, equipped.AcquiredAtWeek);
            Assert.Equal("カタログから購入", equipped.AcquiredFrom);
            Assert.Equal("鉄の剣", equipped.Name);
        }
    }
}
