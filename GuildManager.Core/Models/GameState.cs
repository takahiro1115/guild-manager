using System;
using System.Collections.Generic;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// ゲーム全体の状態。将来的にJSONへシリアライズしてセーブする前提（→ 05 技術メモ §4）。
    /// そのため参照の持ち方はシンプルに保つ（循環参照を避ける）。
    /// </summary>
    public class GameState
    {
        public int WeekNumber { get; set; } = 1;

        /// <summary>初期資金。→ BAL: 経済/初期資金</summary>
        public int Gold { get; set; } = 3000;

        public List<Adventurer> Adventurers { get; set; } = new();

        /// <summary>
        /// 40歳強制引退した冒険者の一覧（仕様書 03 §3.7つづき）。現役ロースター
        /// （Adventurers）からは除外しつつ、データとしては破棄しない。
        /// §7顧問制度が未実装の間は「引退済み・顧問候補」として保持するのみで、
        /// 教官・スカウト・参謀としての実際の効果は付与しない。§7実装時にここから再任用する想定。
        /// </summary>
        public List<Adventurer> RetiredAdventurers { get; set; } = new();

        public List<Quest> AvailableQuests { get; set; } = new();

        /// <summary>クエストIDをキーに、そのクエストへ派遣中のパーティを保持する。</summary>
        public Dictionary<Guid, Party> DispatchedParties { get; set; } = new();

        /// <summary>
        /// 訓練場に配置されている冒険者ID（→ 03 §3.1〜3.4「成長トリガー・経路2」）。
        /// 施設Lv投資（§6）自体は未実装のため、枠数上限・Lv別補正を持たない最小限のフック。
        /// TODO(→ 03 §6): 訓練場の枠数上限・Lv別成長補正を実装する際、ここに制約を追加する。
        /// </summary>
        public HashSet<Guid> TrainingAssignments { get; set; } = new();
    }
}
