using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 成長トリガー（GrowthSystem）で実際にステータスが伸びた1件の記録。
    /// UI側（Godot）はこれを見て週報ログに表示する想定（→ ユーザー要望：能力上昇の報告）。
    /// </summary>
    public class GrowthEvent
    {
        public Adventurer Adventurer { get; }
        public string Stat { get; }
        public int Before { get; }
        public int After { get; }

        public GrowthEvent(Adventurer adventurer, string stat, int before, int after)
        {
            Adventurer = adventurer;
            Stat = stat;
            Before = before;
            After = after;
        }
    }
}
