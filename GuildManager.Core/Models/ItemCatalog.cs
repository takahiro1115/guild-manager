using System.Collections.Generic;
using System.Linq;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// 装備アイテムの静的カタログ。仕様書 03 §4.2.2 参照。
    /// 入手経路は即時購入のみ（→ EconomySystem・EquipmentSystem）。製作・素材・工房を伴う
    /// 入手経路はpost-MVP（→ §11）。値はすべて仮値（→ BAL: 装備）。
    /// </summary>
    public static class ItemCatalog
    {
        // ---- 武器（Weapon）：個人CPへの固定加算 ----
        public const string IronSwordId = "IronSword";
        public const string GreatSwordId = "GreatSword";
        public const string MageStaffId = "MageStaff";

        public static readonly Item IronSword = new Item
        {
            Id = IronSwordId, Name = "鉄の剣", Slot = EquipmentSlot.Weapon,
            EffectType = EquipmentEffectType.PersonalCpBonus, EffectValue = 10, Price = 200,
            VisualPartId = "weapon_iron_sword",
        };

        public static readonly Item GreatSword = new Item
        {
            Id = GreatSwordId, Name = "大剣", Slot = EquipmentSlot.Weapon,
            EffectType = EquipmentEffectType.PersonalCpBonus, EffectValue = 20, Price = 500,
            AllowedJobs = new List<JobClass> { JobClass.Warrior }, // 重量武器：戦士専用
            VisualPartId = "weapon_greatsword",
        };

        public static readonly Item MageStaff = new Item
        {
            Id = MageStaffId, Name = "魔導士の杖", Slot = EquipmentSlot.Weapon,
            EffectType = EquipmentEffectType.PersonalCpBonus, EffectValue = 15, Price = 400,
            AllowedJobs = new List<JobClass> { JobClass.Mage },
            VisualPartId = "weapon_staff",
        };

        // ---- 防具（Armor）：最大HPへの固定加算 ----
        public const string LeatherArmorId = "LeatherArmor";
        public const string HeavyArmorId = "HeavyArmor";
        public const string RobeId = "Robe";

        public static readonly Item LeatherArmor = new Item
        {
            Id = LeatherArmorId, Name = "革鎧", Slot = EquipmentSlot.Armor,
            EffectType = EquipmentEffectType.MaxHpBonus, EffectValue = 20, Price = 200,
            VisualPartId = "armor_leather",
        };

        public static readonly Item HeavyArmor = new Item
        {
            Id = HeavyArmorId, Name = "重装鎧", Slot = EquipmentSlot.Armor,
            EffectType = EquipmentEffectType.MaxHpBonus, EffectValue = 40, Price = 600,
            AllowedJobs = new List<JobClass> { JobClass.Warrior, JobClass.Cleric }, // 魔導士・斥候は不可
            VisualPartId = "armor_heavy",
        };

        public static readonly Item Robe = new Item
        {
            Id = RobeId, Name = "ローブ", Slot = EquipmentSlot.Armor,
            EffectType = EquipmentEffectType.MaxHpBonus, EffectValue = 15, Price = 250,
            AllowedJobs = new List<JobClass> { JobClass.Mage, JobClass.Cleric },
            VisualPartId = "armor_robe",
        };

        // ---- アクセサリー1（Accessory1）：CP/HPいずれか、見た目に反映される ----
        public const string PowerRingId = "PowerRing";
        public const string LifeAmuletId = "LifeAmulet";

        public static readonly Item PowerRing = new Item
        {
            Id = PowerRingId, Name = "力の指輪", Slot = EquipmentSlot.Accessory1,
            EffectType = EquipmentEffectType.PersonalCpBonus, EffectValue = 8, Price = 300,
            VisualPartId = "accessory_power_ring",
        };

        public static readonly Item LifeAmulet = new Item
        {
            Id = LifeAmuletId, Name = "生命のお守り", Slot = EquipmentSlot.Accessory1,
            EffectType = EquipmentEffectType.MaxHpBonus, EffectValue = 15, Price = 300,
            VisualPartId = "accessory_life_amulet",
        };

        // ---- アクセサリー2（Accessory2）：CP/HPいずれか、見た目には反映されない ----
        public const string QuickBroochId = "QuickBrooch";
        public const string GuardCharmId = "GuardCharm";

        public static readonly Item QuickBrooch = new Item
        {
            Id = QuickBroochId, Name = "俊敏のブローチ", Slot = EquipmentSlot.Accessory2,
            EffectType = EquipmentEffectType.PersonalCpBonus, EffectValue = 8, Price = 300,
            // アクセサリー2は立ち絵側の対応枠が無いためVisualPartIdを設定しない（→ 03 §4.2.2・§11）。
        };

        public static readonly Item GuardCharm = new Item
        {
            Id = GuardCharmId, Name = "守りのお守り", Slot = EquipmentSlot.Accessory2,
            EffectType = EquipmentEffectType.MaxHpBonus, EffectValue = 15, Price = 300,
        };

        private static readonly Item[] All =
        {
            IronSword, GreatSword, MageStaff,
            LeatherArmor, HeavyArmor, Robe,
            PowerRing, LifeAmulet,
            QuickBrooch, GuardCharm,
        };

        public static Item? FindById(string? id) => id == null ? null : All.FirstOrDefault(i => i.Id == id);

        /// <summary>指定したスロットに装備可能な全アイテム（カタログ順）。購入UIの一覧表示に使う。</summary>
        public static IEnumerable<Item> GetBySlot(EquipmentSlot slot) => All.Where(i => i.Slot == slot);
    }
}
