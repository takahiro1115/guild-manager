using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>物語の記録（§0.92）：見た場面の一覧と、見返し。</summary>
    public class StoryRecordTests
    {
        private static GameState NewGame()
        {
            var state = new GameState { WeekNumber = 1, Gold = 1000, MasterMood = 50, DungeonFields = SampleData.CreateDefaultFields() };
            state.Adventurers.AddRange(SampleData.CreateStarterAdventurers());
            return state;
        }

        private static void SeeAt(GameState state, int week, string id)
        {
            state.WeekNumber = week;
            StorySystem.MarkSeen(state, id);
        }

        [Theory]
        [InlineData("序章　出会い（ゲーム開始）", "序章　出会い")]
        [InlineData("3　ルミナ（森の20Fのボスを初めて倒した）", "ルミナ")]
        [InlineData("3a　交流戦の結果：負けた（1勝以下）", "交流戦の結果：負けた")]
        [InlineData("6-2　派遣の教官が帰る（6か月後）", "派遣の教官が帰る")]
        [InlineData("3-1　（2勝目）一番と二番", "一番と二番")]
        [InlineData("前兆 1　光る結晶（翠緑の原生林の50F）", "前兆 1　光る結晶")]
        [InlineData("足跡 4　母たちの手紙（焦熱の峡谷の100F）→ そのまま真相へ", "足跡 4　母たちの手紙")]
        [InlineData("真相　この先は、黙ったまま連れていけない", "真相　この先は、黙ったまま連れていけない")]
        public void 記録の名前は番号ときっかけを除く(string heading, string expected)
        {
            Assert.Equal(expected, StorySystem.RecordTitle(heading));
        }

        [Fact]
        public void どの場面も記録の名前が付く()
        {
            foreach (var id in StorySystem.SceneIds)
            {
                string title = StorySystem.RecordTitle(StoryBalance.Get(id).Title);
                Assert.False(string.IsNullOrWhiteSpace(title), id);
                Assert.DoesNotContain("（", title.Replace("（夢）", ""));
            }
        }

        [Fact]
        public void 見た場面だけを章ごとに見た順で並べ_毎回の一言は載せない()
        {
            var state = NewGame();
            Assert.Empty(StorySystem.Record(state));

            SeeAt(state, 1, "s01_prologue");
            SeeAt(state, 1, "s01_guild");
            SeeAt(state, 30, "s02_visit");
            SeeAt(state, 20, "s01_lumina");
            SeeAt(state, 40, "s02_invite");
            SeeAt(state, 41, "s02_rematch_won");

            var record = StorySystem.Record(state);
            Assert.Equal(new[] { "一　出会い", "二　白百合の杖" }, record.Select(c => c.Title));
            Assert.Equal(new[] { "s01_prologue", "s01_guild", "s01_lumina" }, record[0].Entries.Select(e => e.SceneId));
            Assert.Equal(new[] { "s02_visit" }, record[1].Entries.Select(e => e.SceneId));
            Assert.Equal(20, record[0].Entries[2].SeenWeek);
            Assert.Equal("ルミナ", record[0].Entries[2].Title);
        }

        [Fact]
        public void 物語と手ほどきなしで見たことにした場面は週0で載る()
        {
            var state = NewGame();
            StorySystem.DisableTutorial(state);
            var entries = StorySystem.Record(state).SelectMany(c => c.Entries).ToList();
            Assert.Contains(entries, e => e.SceneId == "s01_order" && e.SeenWeek == 0);
        }

        [Fact]
        public void 見返しても見たことの記録と数は変わらない()
        {
            var state = NewGame();
            SeeAt(state, 1, "s01_prologue");
            state.WeekNumber = 9;
            var weeks = state.StorySeenWeeks.ToDictionary(kv => kv.Key, kv => kv.Value);
            var counters = state.StoryCounters.ToDictionary(kv => kv.Key, kv => kv.Value);

            var showing = StorySystem.Replay(state, "s01_prologue");

            Assert.Equal("s01_prologue", showing.SceneId);
            Assert.NotEmpty(showing.Pages);
            Assert.Equal(weeks, state.StorySeenWeeks);
            Assert.Equal(counters, state.StoryCounters);
        }
    }
}
