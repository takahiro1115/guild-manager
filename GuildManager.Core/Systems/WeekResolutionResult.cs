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
    }
}
