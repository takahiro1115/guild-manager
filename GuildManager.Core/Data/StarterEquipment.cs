using System;
using GuildManager.Core.Models;

namespace GuildManager.Core.Data
{
    /// <summary>
    /// 職業ごとの初期基本装備（武器＋防具）の一元テーブル（→ 03 §4.2.2「初期装備」・§2.4「新春ドラフト」、2026年9月）。
    /// 固定初期メンバー（→ SampleData）と第1週の新春ドラフトで加入する新人（→ RecruitmentSystem.TryDraftHire）が
    /// 同じ表を使う。アイテムIdはItemCatalog側の構造値（職業制限を満たす組み合わせのみ）。
    /// </summary>
    public static class StarterEquipment
    {
        /// <summary>職業の初期装備（武器Id・防具Id）。</summary>
        public static (string WeaponId, string ArmorId) GetLoadout(JobClass job) => job switch
        {
            JobClass.Warrior => (ItemCatalog.IronSwordId, ItemCatalog.LeatherArmorId),
            JobClass.Knight => (ItemCatalog.IronSwordId, ItemCatalog.ChainmailId),
            JobClass.Ranger => (ItemCatalog.DaggerId, ItemCatalog.LeatherArmorId),
            JobClass.Thief => (ItemCatalog.DaggerId, ItemCatalog.LeatherArmorId),
            JobClass.Mage => (ItemCatalog.MageStaffId, ItemCatalog.RobeId),
            JobClass.Cleric => (ItemCatalog.MaceId, ItemCatalog.RobeId),
            JobClass.Scholar => (ItemCatalog.MageStaffId, ItemCatalog.ScholarCoatId),
            _ => throw new ArgumentOutOfRangeException(nameof(job), job, "初期装備が未定義の職業"),
        };

        /// <summary>
        /// 職業の初期装備を新しいカタログ品の個体として着せ、装備補正（防具のHP・VIT補正）込みの
        /// 最大HPでHPを満タンにする。既に着ている装備は上書きされる（加入直後の新人にのみ使う想定）。
        /// </summary>
        public static void Equip(Adventurer adventurer)
        {
            var (weaponId, armorId) = GetLoadout(adventurer.JobClass);
            adventurer.SetEquippedId(EquipmentSlot.Weapon, weaponId);
            adventurer.SetEquippedId(EquipmentSlot.Armor, armorId);
            adventurer.CurrentHP = adventurer.MaxHP;
        }
    }
}
