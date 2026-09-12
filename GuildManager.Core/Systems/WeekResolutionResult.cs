using System;
using System.Collections.Generic;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 1回の遠征解決（QuestResolver.Resolve）の結果まとめ。
    /// UI側（Godot）はこれを読んで週報ログに表示する想定。
    /// </summary>
    public class WeekResolutionResult
    {
        public EncounterResult Encounter { get; set; }
        public CombatOutcome Outcome { get; set; }
        public double Ratio { get; set; }
        public bool QuestAchieved { get; set; }
        public int RewardGold { get; set; }

        /// <summary>冒険者IDごとの、今回の遠征で失ったHP量。</summary>
        public Dictionary<Guid, int> HpLostByAdventurer { get; set; } = new();

        /// <summary>
        /// 今回の遠征でダウンした（現在HPが0になった）冒険者ID。
        /// フェーズ3の致死判定対象の絞り込みに使う（→ 03 §4.3）。生存・古傷・戦死のいずれの
        /// 結果になった者も含む（戦死者はさらに FallenAdventurerIds にも含まれる）。
        /// </summary>
        public HashSet<Guid> DownedAdventurerIds { get; set; } = new();

        /// <summary>
        /// 今回の遠征で戦死した冒険者ID（→ 03 §4.3・§4.3.1）。
        /// QuestDispatchSystem側でこれを見て、GameState.Adventurers から
        /// GameState.FallenAdventurers への移動と、仲間ロストの満足度ペナルティ（§5.1）を行う。
        /// </summary>
        public HashSet<Guid> FallenAdventurerIds { get; set; } = new();
    }
}
