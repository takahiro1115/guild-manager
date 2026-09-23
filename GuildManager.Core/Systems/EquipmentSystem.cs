using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 装備の購入・着脱管理。仕様書 03 §4.2.2 参照。
    ///
    /// - 装備枠は武器・防具・アクセサリー1・アクセサリー2の4枠（→ Adventurer.EquippedWeapon 等）。
    /// - 入手経路は2つ：カタログからの即時購入（→ TryPurchaseAndEquip）と、
    ///   ギルド保管庫（→ GameState.Armory）にある現物の装着（→ TryEquip。2026年9月新設。
    ///   遺物の鑑定で武具が出るようになったため、→ §4.7）。
    /// - **不変条件：** 1つの個体（EquipmentItem）が保管庫と冒険者の両方に存在することはない。
    ///   装着すれば保管庫から抜け、外せば保管庫へ戻る（＝個体が消えたり増えたりしない）。
    /// - 職業制限に反する装備は不可（→ Item.IsAllowedFor）。出撃中の冒険者は着脱不可
    ///   （→ Adventurer.IsDispatched。出撃中の部隊の戦力が決算直前に変わるのを防ぐ）。
    /// - 個人CP・最大HPへの効果反映は、Adventurer.GetEquipmentBonus経由で
    ///   DungeonPowerCalculator・Adventurer.MaxHPからそれぞれ参照される（このクラスは
    ///   スロットの状態管理と、最大HPが下がった場合の現在HPのクランプのみを担当する）。
    /// </summary>
    public class EquipmentSystem
    {
        /// <summary>
        /// カタログから購入し、即座に該当スロットへ装備する。資金不足・職業制限違反・
        /// 未知のアイテムId・出撃中のいずれかに該当する場合は何もせず false を返す。
        ///
        /// 既に同じスロットに別の装備がある場合、その個体はギルド保管庫へ戻す（2026年9月改訂。
        /// 以前は上書きで消滅していた。保管庫ができた以降は、個体を黙って破棄しないのが本クラスの
        /// 不変条件）。購入代金の返金はしない（従来どおり）。
        /// </summary>
        public bool TryPurchaseAndEquip(GameState state, Adventurer adventurer, string itemId)
        {
            var item = ItemCatalog.FindById(itemId);
            if (item == null) return false;
            if (adventurer.IsDispatched) return false;
            if (!item.IsAllowedFor(adventurer.JobClass)) return false;
            if (state.Gold < item.Price) return false;

            state.Gold -= item.Price;
            Attach(state, adventurer, item.Slot, EquipmentItem.FromCatalog(item, state.WeekNumber, "カタログから購入"));
            return true;
        }

        /// <summary>
        /// ギルド保管庫（→ GameState.Armory）にある現物を、指定スロットへ装備する（2026年9月新設、→ §4.2.2）。
        /// 以下のいずれかに該当する場合は何もせず false を返す（Try*系の共通パターン）：
        ///  - 冒険者が出撃中（→ Adventurer.IsDispatched）
        ///  - 個体がカタログから引けない（正体不明の武具は着せない）
        ///  - 個体の装備枠が指定した slot と一致しない（武器枠に防具を入れる等）
        ///  - 職業制限違反（→ Item.IsAllowedFor。例：魔導士に大剣）
        ///  - 個体が保管庫に無い（他の冒険者が装備中・既に鑑定前の別物等）
        ///
        /// 成功時は「旧装備を保管庫へ戻す → 新装備を保管庫から抜く → スロットへ差す」の順で入れ替え、
        /// 最大HPが下がった場合は現在HPをクランプする（上がった場合は現在HPを据え置く）。
        /// </summary>
        public bool TryEquip(GameState state, Adventurer adventurer, EquipmentSlot slot, EquipmentItem item)
        {
            if (!CanChangeEquipment(adventurer))
                return false;

            var definition = item.GetDefinition();
            if (definition == null) return false;
            if (definition.Slot != slot) return false;
            if (!definition.IsAllowedFor(adventurer.JobClass)) return false;
            if (!state.Armory.Contains(item)) return false;

            Attach(state, adventurer, slot, item);
            return true;
        }

        /// <summary>
        /// 指定スロットの装備を外し、ギルド保管庫へ戻す（→ §4.2.2）。
        /// 出撃中、またはスロットが空の場合は何もせず false を返す。
        /// </summary>
        public bool TryUnequip(GameState state, Adventurer adventurer, EquipmentSlot slot)
        {
            if (!CanChangeEquipment(adventurer))
                return false;

            var current = adventurer.GetEquipped(slot);
            if (current == null)
                return false;

            adventurer.SetEquipped(slot, null);
            state.Armory.Add(current);
            ClampCurrentHp(adventurer);
            return true;
        }

        /// <summary>
        /// 冒険者の全装備枠を解除し、すべての個体をギルド保管庫へ回収する（2026年9月新設、→ §4.2.2「形見装備」）。
        /// 回収した個体の一覧を返す（週報ログ・引退メッセージでの表示用。何も装備していなければ空）。
        ///
        /// 用途は**ギルドからの離脱**：満期引退・早期引退（→ AgingSystem.Retire）、決戦での強制除籍
        /// （→ DungeonExpeditionSystem.ApplyForcedRetirements）、契約解除・退団
        /// （→ SatisfactionSystem.Terminate）。離脱者が装備したまま記録リストへ移ると、
        /// その個体は二度と手が届かない場所へ行く（＝実質的な消失）ため、離脱の直前に必ず通す。
        ///
        /// TryEquip/TryUnequip と違い**出撃中ガードを持たない**：決戦で強制除籍される冒険者は
        /// 定義上まだ出撃中であり、そこで弾いてしまうと肝心の経路で装備を取りこぼす。
        ///
        /// 回収した個体には入手経路（→ EquipmentItem.AcquiredFrom）として、誰から返ってきたのかを
        /// 書き込む。保管庫一覧（→ UI: InventoryPanel）で「第40週 ○○の形見」と辿れるようにするため。
        /// </summary>
        /// <param name="acquiredFrom">
        /// 保管庫の在庫に記録する入手経路。省略時は「○○の形見」。引退・退団のように本人が健在な
        /// 経路では、呼び出し側が実態に合う文言（「○○（引退）から返還」等）を渡す。
        /// </param>
        public static IReadOnlyList<EquipmentItem> UnequipAllToArmory(
            GameState state, Adventurer adventurer, string? acquiredFrom = null)
        {
            var recovered = new List<EquipmentItem>();
            string provenance = acquiredFrom ?? $"{adventurer.Name}の形見";

            foreach (var slot in Adventurer.AllSlots)
            {
                var equipped = adventurer.GetEquipped(slot);
                if (equipped == null)
                    continue;

                adventurer.SetEquipped(slot, null);
                equipped.AcquiredAtWeek = state.WeekNumber;
                equipped.AcquiredFrom = provenance;
                state.Armory.Add(equipped);
                recovered.Add(equipped);
            }

            // 装備が外れた分だけ最大HPが下がるため、既存の着脱と同じ整合性処理を通す
            // （離脱者のHPは以後参照されないが、記録として矛盾した値を残さない）。
            if (recovered.Count > 0)
                ClampCurrentHp(adventurer);

            return recovered;
        }

        /// <summary>
        /// 装備を変更できる状態か（→ TryEquip・TryUnequip の共通ガード）。
        /// 出撃中（IsDispatched）は不可。UI側（AdventurerPanel）も同じ条件でボタンを
        /// Disabledにしているが、可否の判定そのものはCore層で自己完結させる方針
        /// （→ DungeonExpeditionSystem.TryDispatchのフィールド未開放チェックと同じ考え方）。
        /// </summary>
        public static bool CanChangeEquipment(Adventurer adventurer) => !adventurer.IsDispatched;

        /// <summary>
        /// 指定した冒険者・スロットに対して、保管庫から装備できる個体の一覧（→ UI: EquipmentPopup）。
        /// スロットが一致し、かつ職業制限を満たすものだけを返す。
        /// </summary>
        public static IReadOnlyList<EquipmentItem> GetEquippableFromArmory(
            GameState state, Adventurer adventurer, EquipmentSlot slot) =>
            state.Armory
                .Where(e => e.GetSlot() == slot && e.IsAllowedFor(adventurer.JobClass))
                .ToList();

        /// <summary>
        /// スロットへの差し替え本体：旧装備を保管庫へ戻し、新装備を保管庫から抜いてスロットへ差す。
        /// 購入経路（新規個体・保管庫に無い）でも、保管庫経路（在庫の現物）でも同じ手順で通せるよう、
        /// Remove は「在れば抜く」（戻り値を見ない）扱いにしてある。
        /// </summary>
        private static void Attach(GameState state, Adventurer adventurer, EquipmentSlot slot, EquipmentItem item)
        {
            var previous = adventurer.GetEquipped(slot);
            if (previous != null)
                state.Armory.Add(previous);

            state.Armory.Remove(item);
            adventurer.SetEquipped(slot, item);
            ClampCurrentHp(adventurer);
        }

        /// <summary>
        /// 装備変更で最大HPが下がった場合に現在HPを丸める（→ Adventurer.MaxHP は装備補正を含む
        /// 計算プロパティのため、外した瞬間に「現在HP＞最大HP」が起こり得る）。
        /// 最大HPが上がった場合は現在HPを据え置く（装備で全回復してしまわないようにする）。
        /// </summary>
        private static void ClampCurrentHp(Adventurer adventurer)
        {
            if (adventurer.CurrentHP > adventurer.MaxHP)
                adventurer.CurrentHP = adventurer.MaxHP;
        }
    }
}
