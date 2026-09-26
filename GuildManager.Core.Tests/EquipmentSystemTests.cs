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
            // 大剣(GreatSword)は重戦士・騎士専用。魔導士は装備できない。
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
            // 7職業化に伴い、列挙型の全職業を対象にする（新しい職業を追加しても自動で検証対象になる）。
            foreach (var job in System.Enum.GetValues<JobClass>())
            {
                var adventurer = new Adventurer { JobClass = job };
                var state = new GameState { Gold = 10000 };
                var system = new EquipmentSystem();

                bool result = system.TryPurchaseAndEquip(state, adventurer, ItemCatalog.LeatherArmorId);

                Assert.True(result, $"{job}は革鎧を装備できるはず");
            }
        }

        // ---------------- 7職業化：新3職の装備制限（→ ItemCatalog.AllowedJobs） ----------------

        [Theory]
        // 大剣：重戦士・騎士のみ
        [InlineData(ItemCatalog.GreatSwordId, JobClass.Warrior, true)]
        [InlineData(ItemCatalog.GreatSwordId, JobClass.Knight, true)]
        [InlineData(ItemCatalog.GreatSwordId, JobClass.Ranger, false)]
        [InlineData(ItemCatalog.GreatSwordId, JobClass.Thief, false)]
        [InlineData(ItemCatalog.GreatSwordId, JobClass.Mage, false)]
        [InlineData(ItemCatalog.GreatSwordId, JobClass.Cleric, false)]
        [InlineData(ItemCatalog.GreatSwordId, JobClass.Scholar, false)]
        // 魔導士の杖：魔導士・学者のみ
        [InlineData(ItemCatalog.MageStaffId, JobClass.Mage, true)]
        [InlineData(ItemCatalog.MageStaffId, JobClass.Scholar, true)]
        [InlineData(ItemCatalog.MageStaffId, JobClass.Warrior, false)]
        [InlineData(ItemCatalog.MageStaffId, JobClass.Knight, false)]
        [InlineData(ItemCatalog.MageStaffId, JobClass.Ranger, false)]
        [InlineData(ItemCatalog.MageStaffId, JobClass.Thief, false)]
        [InlineData(ItemCatalog.MageStaffId, JobClass.Cleric, false)]
        // 重装鎧：重戦士・騎士・神官のみ
        [InlineData(ItemCatalog.HeavyArmorId, JobClass.Warrior, true)]
        [InlineData(ItemCatalog.HeavyArmorId, JobClass.Knight, true)]
        [InlineData(ItemCatalog.HeavyArmorId, JobClass.Cleric, true)]
        [InlineData(ItemCatalog.HeavyArmorId, JobClass.Ranger, false)]
        [InlineData(ItemCatalog.HeavyArmorId, JobClass.Thief, false)]
        [InlineData(ItemCatalog.HeavyArmorId, JobClass.Mage, false)]
        [InlineData(ItemCatalog.HeavyArmorId, JobClass.Scholar, false)]
        // ローブ：魔導士・神官・学者のみ
        [InlineData(ItemCatalog.RobeId, JobClass.Mage, true)]
        [InlineData(ItemCatalog.RobeId, JobClass.Cleric, true)]
        [InlineData(ItemCatalog.RobeId, JobClass.Scholar, true)]
        [InlineData(ItemCatalog.RobeId, JobClass.Warrior, false)]
        [InlineData(ItemCatalog.RobeId, JobClass.Knight, false)]
        [InlineData(ItemCatalog.RobeId, JobClass.Ranger, false)]
        [InlineData(ItemCatalog.RobeId, JobClass.Thief, false)]
        public void IsAllowedFor_FollowsSevenJobEquipmentRules(string itemId, JobClass job, bool expected)
        {
            var item = ItemCatalog.FindById(itemId)!;

            Assert.Equal(expected, item.IsAllowedFor(job));
        }

        [Theory]
        [InlineData(ItemCatalog.IronSwordId)]
        [InlineData(ItemCatalog.LeatherArmorId)]
        [InlineData(ItemCatalog.PowerRingId)]
        [InlineData(ItemCatalog.LifeAmuletId)]
        [InlineData(ItemCatalog.QuickBroochId)]
        [InlineData(ItemCatalog.GuardCharmId)]
        public void IsAllowedFor_UnrestrictedItems_AreEquippableByAllSevenJobs(string itemId)
        {
            // 鉄の剣・革鎧・アクセサリー各種は職業制限なし（新3職も装備可）。
            var item = ItemCatalog.FindById(itemId)!;

            foreach (var job in System.Enum.GetValues<JobClass>())
                Assert.True(item.IsAllowedFor(job), $"{job}は{item.Name}を装備できるはず");
        }

        [Theory]
        [InlineData(JobClass.Knight, ItemCatalog.GreatSwordId)]
        [InlineData(JobClass.Knight, ItemCatalog.HeavyArmorId)]
        [InlineData(JobClass.Scholar, ItemCatalog.MageStaffId)]
        [InlineData(JobClass.Scholar, ItemCatalog.RobeId)]
        public void TryPurchaseAndEquip_NewJobClasses_CanEquipTheirDesignatedGear(JobClass job, string itemId)
        {
            // 判定（IsAllowedFor）だけでなく、実際の購入・装備フローでも新3職が装備できること。
            var adventurer = new Adventurer { JobClass = job };
            var state = new GameState { Gold = 10000 };

            bool result = new EquipmentSystem().TryPurchaseAndEquip(state, adventurer, itemId);

            Assert.True(result, $"{job}は{itemId}を装備できるはず");
        }

        [Fact]
        public void TryPurchaseAndEquip_Thief_CannotEquipHeavyArmor()
        {
            // 盗賊は前衛だが重装鎧は装備不可（俊敏さを活かす軽装職）。
            var thief = new Adventurer { JobClass = JobClass.Thief };
            var state = new GameState { Gold = 10000 };

            bool result = new EquipmentSystem().TryPurchaseAndEquip(state, thief, ItemCatalog.HeavyArmorId);

            Assert.False(result);
            Assert.Null(thief.EquippedArmorId);
            Assert.Equal(10000, state.Gold);
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

            // 2026年9月改訂：解除はギルド保管庫へ戻す形になったため、GameStateを受け取る
            // TryUnequipへ置き換わった（→ EquipmentSystem・Systems/EquipmentSystemTests）。
            Assert.True(system.TryUnequip(state, adventurer, EquipmentSlot.Weapon));

            Assert.Null(adventurer.EquippedWeaponId);
            Assert.Equal(ItemCatalog.LeatherArmorId, adventurer.EquippedArmorId); // 他スロットは影響なし
            Assert.Equal(ItemCatalog.IronSwordId, Assert.Single(state.Armory).ItemId); // 外した武器は保管庫へ
        }

        // ---------------- 能力値・最大HPへの効果反映（→ Adventurer.GetEquipmentStatBonus・GetEquipmentHpBonus） ----------------

        [Fact]
        public void StatBonuses_SumAcrossWeaponAndAccessories()
        {
            // §0.37：個人CPは撤廃。武器・アクセサリーの強さは能力値補正として実効ステータスに合算される。
            var adventurer = new Adventurer { JobClass = JobClass.Warrior };
            var state = new GameState { Gold = 10000 };
            var system = new EquipmentSystem();
            system.TryPurchaseAndEquip(state, adventurer, ItemCatalog.IronSwordId);    // STR+2・VIT+1
            system.TryPurchaseAndEquip(state, adventurer, ItemCatalog.PowerRingId);    // STR+5
            system.TryPurchaseAndEquip(state, adventurer, ItemCatalog.QuickBroochId);  // AGI+5
            system.TryPurchaseAndEquip(state, adventurer, ItemCatalog.LeatherArmorId); // 最大HP+15・AGI+2

            Assert.Equal(7, adventurer.GetEquipmentStatBonus("STR"));
            Assert.Equal(7, adventurer.GetEquipmentStatBonus("AGI"));
            Assert.Equal(1, adventurer.GetEquipmentStatBonus("VIT"));
            Assert.Equal(15, adventurer.GetEquipmentHpBonus()); // 最大HP加算は防具だけ
        }

        [Fact]
        public void MaxHP_IncludesEquippedArmorBonus()
        {
            var adventurer = new Adventurer { VIT = 20 };
            int maxHpBefore = adventurer.MaxHP; // 20*2+50=90

            var state = new GameState { Gold = 10000 };
            new EquipmentSystem().TryPurchaseAndEquip(state, adventurer, ItemCatalog.LeatherArmorId); // HP+15（革鎧のAGI+2はHPに影響しない）

            Assert.Equal(90, maxHpBefore);
            Assert.Equal(105, adventurer.MaxHP);
        }

        [Fact]
        public void MaxHP_WeaponAddsNoHp_ButItsVitBonusDoes()
        {
            var adventurer = new Adventurer { VIT = 20, JobClass = JobClass.Warrior };
            var state = new GameState { Gold = 10000 };
            new EquipmentSystem().TryPurchaseAndEquip(state, adventurer, ItemCatalog.IronSwordId); // 最大HP加算なし・VIT+1

            Assert.Equal(92, adventurer.MaxHP); // (20+1)*2+50。武器は最大HPを直接足さず、VIT補正だけが乗る
        }

        [Fact]
        public void EquipmentBonuses_ZeroByDefault_WhenNothingEquipped()
        {
            var adventurer = new Adventurer();

            Assert.Equal(0, adventurer.GetEquipmentHpBonus());
            Assert.Equal(0, adventurer.GetEquipmentStatBonus("STR"));
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
