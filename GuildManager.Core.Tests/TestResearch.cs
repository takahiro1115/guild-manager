using GuildManager.Core.Models;
using GuildManager.Core.Systems;

namespace GuildManager.Core.Tests
{
    /// <summary>テスト用：研究を始めて、完了するまで週を進める（研究の期間、§0.114）。始められなければ false。</summary>
    internal static class TestResearch
    {
        public static bool Complete(GameState state, ResearchDefinition research)
        {
            if (!ResearchSystem.StartResearch(state, research))
                return false;
            while (ResearchSystem.ProcessWeek(state) == null) { }
            return true;
        }
    }
}
