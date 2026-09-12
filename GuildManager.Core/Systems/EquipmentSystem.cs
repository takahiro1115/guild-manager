using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 装備の購入・着脱管理。仕様書 03 §4.2.2 参照。
    ///
    /// - 装備枠は武器・防具・アクセサリー1・アクセサリー2の4枠（→ Adventurer.Equipped*Id）。
    /// - 入手経路は即時購入のみ（→ TryPurchaseAndEquip。製作・素材・工房はpost-MVP、→ §11）。
    /// - 職業制限に反する装備は不可（→ Item.IsAllowedFor）。
    /// - 個人CP・最大HPへの効果反映は、Adventurer.GetEquipmentBonus経由で
    ///   QuestResolver.PersonalCp・Adventurer.MaxHPからそれぞれ参照される
    ///   （このクラス自体はスロットの状態管理のみを担当する）。
    /// </summary>
    public class EquipmentSystem
    {
        /// <summary>
        /// アイテムを購入し、即座に該当スロットへ装備する。資金不足・職業制限違反・
        /// 未知のアイテムIdのいずれかに該当する場合は何もせず false を返す。
        /// 既に同じスロットに別のアイテムを装備していた場合は上書きする（返金はしない）。
        /// </summary>
        public bool TryPurchaseAndEquip(GameState state, Adventurer adventurer, string itemId)
        {
            var item = ItemCatalog.FindById(itemId);
            if (item == null) return false;
            if (!item.IsAllowedFor(adventurer.JobClass)) return false;
            if (state.Gold < item.Price) return false;

            state.Gold -= item.Price;
            adventurer.SetEquippedId(item.Slot, item.Id);
            return true;
        }

        /// <summary>
        /// 既に購入済み（別の冒険者から外した等）のアイテムを、購入処理を挟まず装備する。
        /// 職業制限に反する場合は失敗する。現状は購入と同時装備のみが入手経路のため、
        /// このメソッドは主にテスト・将来の付け替えUI向けに用意してある。
        /// </summary>
        public bool TryEquip(Adventurer adventurer, string itemId)
        {
            var item = ItemCatalog.FindById(itemId);
            if (item == null) return false;
            if (!item.IsAllowedFor(adventurer.JobClass)) return false;

            adventurer.SetEquippedId(item.Slot, item.Id);
            return true;
        }

        /// <summary>指定したスロットの装備を外す（返金はしない）。</summary>
        public void Unequip(Adventurer adventurer, EquipmentSlot slot) => adventurer.SetEquippedId(slot, null);
    }
}
