using System.Collections.Generic;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// 4スロットのパーティ編成。仕様書 03 §9（中央ペイン）参照。
    /// MVPでは前衛/後衛の区別なし（→ docs/06_タスクリスト.md Phase 3 で追加予定）。
    /// </summary>
    public class Party
    {
        public const int MaxSlots = 4;

        /// <summary>
        /// 携行アイテム（消耗品）ポーチの最大枠数。「パーティ携行アイテム」刷新仕様参照。
        /// 使い切りで、クエスト解決時に一括消費される（→ QuestResolver.Resolve）。
        /// </summary>
        public const int MaxConsumableSlots = 2;

        private readonly List<Adventurer> _members = new();

        public IReadOnlyList<Adventurer> Members => _members;

        public bool IsFull => _members.Count == MaxSlots;
        public bool IsEmpty => _members.Count == 0;

        /// <summary>空きがあれば追加する。満杯や重複追加ならfalseを返す。</summary>
        public bool TryAdd(Adventurer adventurer)
        {
            if (_members.Count >= MaxSlots) return false;
            if (_members.Contains(adventurer)) return false;

            _members.Add(adventurer);
            return true;
        }

        public bool Remove(Adventurer adventurer) => _members.Remove(adventurer);

        /// <summary>携行中の消耗品（→ ConsumableCatalog）のId一覧。最大2件。重複は持てない。</summary>
        public List<string> ConsumableItemIds { get; set; } = new();

        /// <summary>
        /// 携行アイテムをポーチに入れる。満杯（2枠）や重複追加ならfalseを返す
        /// （TryAdd・Adventurer.TryAddTraitと同じ「Try」系のパターン）。
        /// </summary>
        public bool TryAddConsumable(string itemId)
        {
            if (ConsumableItemIds.Count >= MaxConsumableSlots) return false;
            if (ConsumableItemIds.Contains(itemId)) return false;

            ConsumableItemIds.Add(itemId);
            return true;
        }

        public bool RemoveConsumable(string itemId) => ConsumableItemIds.Remove(itemId);
    }
}
