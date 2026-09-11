using System.Collections.Generic;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 満了した複数週クエスト1件の解決結果。仕様書 03 §4.0.1 参照。
    /// UI側（Godot）はこれを見て週報ログに表示する想定。
    /// </summary>
    public class DispatchResolution
    {
        public Party Party { get; }
        public Quest Quest { get; }
        public WeekResolutionResult Result { get; }
        public List<GrowthEvent> GrowthEvents { get; }

        public DispatchResolution(Party party, Quest quest, WeekResolutionResult result, List<GrowthEvent> growthEvents)
        {
            Party = party;
            Quest = quest;
            Result = result;
            GrowthEvents = growthEvents;
        }
    }
}
