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
        public List<Quest> AvailableQuests { get; set; } = new();

        /// <summary>クエストIDをキーに、そのクエストへ派遣中のパーティを保持する。</summary>
        public Dictionary<Guid, Party> DispatchedParties { get; set; } = new();
    }
}
