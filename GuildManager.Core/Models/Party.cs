using System.Collections.Generic;
using System.Linq;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// 4スロットのパーティ編成。仕様書 03 §9（中央ペイン）参照。
    /// MVPでは前衛/後衛の区別なし（→ docs/06_タスクリスト.md Phase 3 で追加予定）。
    /// </summary>
    public class Party
    {
        public const int MaxSlots = 4;

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

        /// <summary>パーティの平均疲労度（戦闘比率計算で使用。仕様書 03 §4.2）。</summary>
        public double AverageFatigue =>
            _members.Count == 0 ? 0 : _members.Average(a => a.Fatigue);
    }
}
