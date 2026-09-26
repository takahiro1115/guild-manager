using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// 装備アイテムの静的カタログ。仕様書 03 §4.2.2 参照。
    /// 入手経路は即時購入のみ（→ EconomySystem・EquipmentSystem）。製作・素材・工房を伴う
    /// 入手経路はpost-MVP（→ §11）。Price・最大HP加算（MaxHpBonus）・能力値補正（StatBonuses）は
    /// docs/04_バランス表/equipment.csv 由来（→ EquipmentBalance）。ここに書くのは構造
    /// （Id・名前・スロット・効果種別・職業制限・見た目・重装区分）だけで、数値は持たない。
    ///
    /// 2026年9月改訂（武具の7大能力値補正）：武器6種・防具3種を追加し、全武器・防具に
    /// 能力値補正を持たせた。大剣のIdは旧セーブ互換のため "GreatSword" のまま据え置く。
    /// </summary>
    public static class ItemCatalog
    {
        /// <summary>構造だけを書いたItemへ、equipment.csvの数値を流し込む。CSVに行が無ければ起動失敗（→ EquipmentBalance.Get）。</summary>
        private static Item Define(Item item)
        {
            var stats = EquipmentBalance.Get(item.Id);
            item.Price = stats.Price;
            item.MaxHpBonus = stats.HpBonus;
            item.StatBonuses = stats.StatBonuses;
            return item;
        }

        // ---- 武器（Weapon）：能力値補正のみ（§0.37で個人CP加算を撤廃） ----
        public const string IronSwordId = "IronSword";
        public const string DaggerId = "Dagger";
        public const string HuntingBowId = "HuntingBow";
        public const string SpearId = "Spear";
        public const string GreatSwordId = "GreatSword";
        public const string MaceId = "Mace";
        public const string WarhammerId = "Warhammer";
        public const string MageStaffId = "MageStaff";
        public const string GrimoireId = "Grimoire";

        public static readonly Item IronSword = Define(new Item
        {
            Id = IronSwordId, Name = "鉄の剣", Slot = EquipmentSlot.Weapon,
            VisualPartId = "weapon_iron_sword",
        });

        public static readonly Item Dagger = Define(new Item
        {
            Id = DaggerId, Name = "短剣", Slot = EquipmentSlot.Weapon,
            AllowedJobs = new List<JobClass> { JobClass.Thief, JobClass.Ranger, JobClass.Scholar }, // 器用さを活かす軽武器
            VisualPartId = "weapon_dagger",
        });

        public static readonly Item HuntingBow = Define(new Item
        {
            Id = HuntingBowId, Name = "弓", Slot = EquipmentSlot.Weapon,
            AllowedJobs = new List<JobClass> { JobClass.Ranger, JobClass.Thief }, // 射手：斥候・盗賊
            VisualPartId = "weapon_bow",
        });

        public static readonly Item Spear = Define(new Item
        {
            Id = SpearId, Name = "長槍", Slot = EquipmentSlot.Weapon,
            AllowedJobs = new List<JobClass> { JobClass.Warrior, JobClass.Knight, JobClass.Ranger },
            VisualPartId = "weapon_spear",
        });

        public static readonly Item GreatSword = Define(new Item
        {
            Id = GreatSwordId, Name = "大剣", Slot = EquipmentSlot.Weapon,
            AllowedJobs = new List<JobClass> { JobClass.Warrior, JobClass.Knight }, // 重量武器：重戦士・騎士
            VisualPartId = "weapon_greatsword",
        });

        public static readonly Item Mace = Define(new Item
        {
            Id = MaceId, Name = "メイス", Slot = EquipmentSlot.Weapon,
            AllowedJobs = new List<JobClass> { JobClass.Cleric, JobClass.Knight, JobClass.Warrior },
            VisualPartId = "weapon_mace",
        });

        public static readonly Item Warhammer = Define(new Item
        {
            Id = WarhammerId, Name = "戦槌", Slot = EquipmentSlot.Weapon,
            AllowedJobs = new List<JobClass> { JobClass.Warrior, JobClass.Knight, JobClass.Cleric },
            VisualPartId = "weapon_warhammer",
        });

        public static readonly Item MageStaff = Define(new Item
        {
            Id = MageStaffId, Name = "魔導士の杖", Slot = EquipmentSlot.Weapon,
            AllowedJobs = new List<JobClass> { JobClass.Mage, JobClass.Scholar }, // 魔法職：魔導士・学者
            VisualPartId = "weapon_staff",
        });

        public static readonly Item Grimoire = Define(new Item
        {
            Id = GrimoireId, Name = "魔導書", Slot = EquipmentSlot.Weapon,
            AllowedJobs = new List<JobClass> { JobClass.Mage, JobClass.Scholar, JobClass.Cleric },
            VisualPartId = "weapon_grimoire",
        });

        // ---- 防具（Armor）：最大HPへの固定加算＋能力値補正 ----
        public const string LeatherArmorId = "LeatherArmor";
        public const string ScholarCoatId = "ScholarCoat";
        public const string RobeId = "Robe";
        public const string ChainmailId = "Chainmail";
        public const string HeavyArmorId = "HeavyArmor";
        public const string PlateArmorId = "PlateArmor";

        public static readonly Item LeatherArmor = Define(new Item
        {
            Id = LeatherArmorId, Name = "革鎧", Slot = EquipmentSlot.Armor,
            VisualPartId = "armor_leather",
        });

        public static readonly Item ScholarCoat = Define(new Item
        {
            Id = ScholarCoatId, Name = "学術コート", Slot = EquipmentSlot.Armor,
            AllowedJobs = new List<JobClass> { JobClass.Scholar, JobClass.Ranger, JobClass.Thief, JobClass.Mage },
            VisualPartId = "armor_scholar_coat",
        });

        public static readonly Item Robe = Define(new Item
        {
            Id = RobeId, Name = "ローブ", Slot = EquipmentSlot.Armor,
            AllowedJobs = new List<JobClass> { JobClass.Mage, JobClass.Cleric, JobClass.Scholar }, // 後衛職：魔導士・神官・学者
            VisualPartId = "armor_robe",
        });

        public static readonly Item Chainmail = Define(new Item
        {
            Id = ChainmailId, Name = "鎖帷子", Slot = EquipmentSlot.Armor,
            AllowedJobs = new List<JobClass> { JobClass.Warrior, JobClass.Knight, JobClass.Ranger, JobClass.Cleric },
            // 中装：重装ペナルティ（→ ScoutingResolver.CountHeavyMembers）の対象外。
            VisualPartId = "armor_chainmail",
        });

        public static readonly Item HeavyArmor = Define(new Item
        {
            Id = HeavyArmorId, Name = "重装鎧", Slot = EquipmentSlot.Armor,
            AllowedJobs = new List<JobClass> { JobClass.Warrior, JobClass.Knight, JobClass.Cleric }, // 重装可：重戦士・騎士・神官
            IsHeavyArmor = true,
            VisualPartId = "armor_heavy",
        });

        public static readonly Item PlateArmor = Define(new Item
        {
            Id = PlateArmorId, Name = "全身板金鎧", Slot = EquipmentSlot.Armor,
            AllowedJobs = new List<JobClass> { JobClass.Warrior, JobClass.Knight },
            IsHeavyArmor = true,
            VisualPartId = "armor_plate",
        });

        // ---- アクセサリー1（Accessory1）：最大HP加算または能力値補正、見た目に反映される ----
        public const string PowerRingId = "PowerRing";
        public const string LifeAmuletId = "LifeAmulet";

        public static readonly Item PowerRing = Define(new Item
        {
            Id = PowerRingId, Name = "力の指輪", Slot = EquipmentSlot.Accessory1,
            VisualPartId = "accessory_power_ring",
        });

        public static readonly Item LifeAmulet = Define(new Item
        {
            Id = LifeAmuletId, Name = "生命のお守り", Slot = EquipmentSlot.Accessory1,
            VisualPartId = "accessory_life_amulet",
        });

        // ---- アクセサリー2（Accessory2）：最大HP加算または能力値補正、見た目には反映されない ----
        public const string QuickBroochId = "QuickBrooch";
        public const string GuardCharmId = "GuardCharm";

        public static readonly Item QuickBrooch = Define(new Item
        {
            Id = QuickBroochId, Name = "俊敏のブローチ", Slot = EquipmentSlot.Accessory2,
            // アクセサリー2は立ち絵側の対応枠が無いためVisualPartIdを設定しない（→ 03 §4.2.2・§11）。
        });

        public static readonly Item GuardCharm = Define(new Item
        {
            Id = GuardCharmId, Name = "守りのお守り", Slot = EquipmentSlot.Accessory2,
        });

        private static readonly Item[] All =
        {
            IronSword, Dagger, HuntingBow, Spear, GreatSword, Mace, Warhammer, MageStaff, Grimoire,
            LeatherArmor, ScholarCoat, Robe, Chainmail, HeavyArmor, PlateArmor,
            PowerRing, LifeAmulet,
            QuickBrooch, GuardCharm,
        };

        /// <summary>カタログの全アイテム（カタログ順）。</summary>
        public static IReadOnlyList<Item> GetAll() => All;

        public static Item? FindById(string? id) => id == null ? null : All.FirstOrDefault(i => i.Id == id);

        /// <summary>指定したスロットに装備可能な全アイテム（カタログ順）。購入UIの一覧表示に使う。</summary>
        public static IEnumerable<Item> GetBySlot(EquipmentSlot slot) => All.Where(i => i.Slot == slot);
    }
}
