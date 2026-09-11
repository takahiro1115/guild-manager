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
        /// 現状どのSystemもまだ参照していないが、§4.3の本来の致死判定（不可逆障害・戦死）
        /// を実装する際にダウン者の絞り込みとして使う想定で残してある。
        /// </summary>
        public HashSet<Guid> DownedAdventurerIds { get; set; } = new();
    }
}
