using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
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
    /// - 最大HP・能力値への効果反映は、Adventurer.GetEquipmentHpBonus・GetEffectiveStat経由で
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
        /// 冒険者の全装備枠を解除し、すべての個体をギルド保管庫へ回収する（2026年9月新設、
        /// → §4.2.2「離脱時の自動回収」）。回収した個体の一覧を返す（週報ログ・引退メッセージでの
        /// 表示用。何も装備していなければ空）。
        ///
        /// 扱いは**ギルド備品の返還**であって「形見」という独立した枠組みではない（2026年9月整理）：
        /// 回収された個体は他の在庫と区別されず、誰でも装備でき、売却もできる。前所有者を偲ぶ
        /// 特別な装備（真の遺産化）は、将来の「銘入り装備」で扱う（→ 06タスクリスト）。
        /// ここで記録するのは入手経路の文字列（誰から返ってきたか）だけに留める。
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
        /// 書き込む。保管庫一覧（→ UI: InventoryPanel）で「第40週 ○○から返還」と辿れるようにするため。
        /// </summary>
        /// <param name="acquiredFrom">
        /// 保管庫の在庫に記録する入手経路。省略時は「○○から返還」。呼び出し側は離脱の種別に合わせた
        /// 文言（「○○（引退）から返還」「○○（除籍）から返還」等）を渡す。
        /// </param>
        public static IReadOnlyList<EquipmentItem> UnequipAllToArmory(
            GameState state, Adventurer adventurer, string? acquiredFrom = null)
        {
            var recovered = new List<EquipmentItem>();
            string provenance = acquiredFrom ?? $"{adventurer.Name}から返還";

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

        // ==================== 売却（→ 03 §4.8） ====================

        /// <summary>
        /// 保管庫の武具1点あたりの売却額（→ 03 §4.8）。2系統ある：
        ///  - **カタログ品（無銘、`EquipmentItem.Rarity` が null）**：カタログ定価の50%（端数切り捨て、
        ///    → BAL: equipment.csv の Price 列）。買い直せる物なので目減りする。
        ///  - **鑑定で出土した個体（Rarity あり）**：希少度ごとの基準額（→ BAL: relic.csv の SellPrice*）。
        ///    定価とは無関係に希少度だけで決まる（「掘り出し物」としての価値）。
        ///
        /// カタログから引けない個体（カタログから消えた武具の旧データ）は0を返す＝売っても1Gにならない。
        /// 武具の解体（素材への還元）は実装しない（→ 03 §4.8。世界観上、武具は打ち直せない）。
        /// </summary>
        public static int GetSellPrice(EquipmentItem item)
        {
            if (item.Rarity.HasValue)
                return RelicBalance.GetSellPrice(item.Rarity.Value);

            var definition = item.GetDefinition();
            return definition == null ? 0 : definition.Price / 2;
        }

        /// <summary>
        /// 保管庫の武具をまとめて売却する（→ 03 §4.8）。itemIds は個体Id（→ EquipmentItem.Id）の
        /// 文字列表現で受け取る（UI のリスト行がGuidを文字列で持つため）。
        ///
        /// 以下のいずれかに該当する場合は**保管庫・所持金を一切動かさず** false を返す
        /// （一部だけ売れて残りが失敗する、という半端な状態を作らないための全か無か判定）：
        ///  - itemIds が空
        ///  - 同じ個体Idが重複している
        ///  - 保管庫に無い個体Idが含まれている（＝誰かが装備中、または存在しない）
        ///
        /// 「冒険者が装備中の個体は売れない」は、**保管庫に在るかどうか**の判定で自然に担保される
        /// （装備中の個体は保管庫から抜けている、という不変条件。→ 本クラス冒頭）。念のため
        /// 現役・引退・除籍の全ロースターの装備枠も突き合わせて二重に弾く。
        /// </summary>
        public static bool TrySellEquipments(GameState state, IEnumerable<string> itemIds, out int totalGold)
        {
            totalGold = 0;

            var requested = itemIds.ToList();
            if (requested.Count == 0 || requested.Distinct().Count() != requested.Count)
                return false;

            var byId = state.Armory.ToDictionary(e => e.Id.ToString());
            var targets = new List<EquipmentItem>();

            foreach (var id in requested)
            {
                if (!byId.TryGetValue(id, out var item))
                    return false; // 保管庫に無い＝装備中か存在しない
                targets.Add(item);
            }

            if (targets.Any(item => IsEquippedBySomeone(state, item)))
                return false;

            foreach (var item in targets)
            {
                state.Armory.Remove(item);
                totalGold += GetSellPrice(item);
            }

            state.Gold += totalGold;
            return true;
        }

        /// <summary>
        /// 指定した個体が誰かの装備枠に入っているか（現役・引退済み・除籍者の全員を見る）。
        /// 通常は「保管庫に在る」だけで十分だが、セーブデータの破損等で個体が二重に現れた場合に
        /// 売却で装備が消えるのを防ぐための二重チェック（→ TrySellEquipments）。
        /// </summary>
        private static bool IsEquippedBySomeone(GameState state, EquipmentItem item) =>
            state.Adventurers
                .Concat(state.RetiredAdventurers)
                .Concat(state.FallenAdventurers)
                .Any(a => Adventurer.AllSlots.Any(slot => ReferenceEquals(a.GetEquipped(slot), item)
                    || a.GetEquipped(slot)?.Id == item.Id));

        /// <summary>
        /// 保管庫の武具のうち、同じカタログId・同じ希少度の個体をまとめた在庫単位（→ UI: InventoryPanel）。
        /// 単価が群の中で一様になるよう希少度も鍵に含める（カタログ品と鑑定品では売却額の系統が違う）。
        /// </summary>
        public static IReadOnlyList<IGrouping<(string ItemId, ItemRarity? Rarity), EquipmentItem>> GroupArmoryForSale(
            GameState state) =>
            state.Armory
                .GroupBy(e => (e.ItemId, e.Rarity))
                .OrderBy(g => g.Key.ItemId, StringComparer.Ordinal)
                .ThenBy(g => g.Key.Rarity ?? ItemRarity.Common)
                .ToList();

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
