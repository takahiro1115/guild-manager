using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>迷宮の主の場面（§0.142、→ story_05.txt）：届く声 1〜4・ヴェスペラとの決闘 1〜3、足跡 1 は決着のすぐあと。</summary>
    public class RulerSceneTests
    {
        private static GameState NewGame(int week = 100)
        {
            var state = new GameState { WeekNumber = week, Gold = 1000, DungeonFields = SampleData.CreateDefaultFields() };
            state.Adventurers.AddRange(SampleData.CreateStarterAdventurers());
            RulerSystem.EnsureRulers(state);
            StorySystem.MarkSeen(state, "s01_lumina");
            return state;
        }

        private static void Defeat(GameState state, string fieldId, int floor)
        {
            var field = state.DungeonFields.First(f => f.Id == fieldId);
            field.IsUnlocked = true;
            foreach (var b in field.Bosses.Where(b => b.Floor <= floor)) b.IsDefeated = true;
        }

        private static string[] Due(GameState state) => StorySystem.DueScenes(state, StoryTiming.AfterReport).Select(s => s.SceneId).ToArray();

        private static string[] Texts(GameState state, string id) =>
            StorySystem.DueScenes(state, StoryTiming.AfterReport).Single(s => s.SceneId == id).Pages.SelectMany(p => p).Select(l => l.Text).ToArray();

        [Fact]
        public void Voice1_TwoSeasonsAfterTheFaceReveal_OnlyBeforeTheOutpost()
        {
            var state = NewGame();
            Defeat(state, "forest", 30);
            state.StorySeenWeeks["s05_face1"] = 100;
            state.WeekNumber = 100 + GameCalendar.WeeksPerSeason * 2 - 1;
            Assert.DoesNotContain("s05_voice1", Due(state));
            state.WeekNumber = 100 + GameCalendar.WeeksPerSeason * 2;
            Assert.Contains("s05_voice1", Due(state));
            Defeat(state, "forest", 70); // もう前哨戦で退かせたあとは出さない
            Assert.DoesNotContain("s05_voice1", Due(state));
        }

        [Fact]
        public void Voice4_TheSeasonAfterTheSecondOmen_WithTheLineBeforeOmen4()
        {
            var state = NewGame(week: 200);
            state.StorySeenWeeks["s04_omen1"] = 150;
            state.StorySeenWeeks["s04_omen2"] = 197; // 200週と同じ季節（193〜204週）
            Assert.DoesNotContain("s05_voice4", Due(state));
            state.WeekNumber = 205; // 次の季節のはじめ
            var texts = Texts(state, "s05_voice4");
            Assert.Contains(texts, t => t.Contains("わたしの親のこと"));
            Assert.DoesNotContain(texts, t => t.Contains("お母さんたち"));
            Assert.DoesNotContain(texts, t => t.Contains("{"));
        }

        [Fact]
        public void Duels_AtForest60_70_100_WithTheVictorsName_AndTrail1RightAfterTheEnd()
        {
            var state = NewGame();
            Defeat(state, "forest", 50);
            Assert.DoesNotContain("s05_duel1", Due(state));
            Defeat(state, "forest", 60);
            Assert.Contains("s05_duel1", Due(state));
            StorySystem.MarkSeen(state, "s05_duel1");

            state.Rulers.Single(r => r.RulerId == RulerSystem.VesperaId).LastVictorName = "クラウディア";
            Defeat(state, "forest", 70);
            Assert.Contains(Texts(state, "s05_duel2"), t => t.Contains("クラウディアの一撃"));
            StorySystem.MarkSeen(state, "s05_duel2");

            Defeat(state, "forest", 100);
            var due = Due(state).ToList();
            Assert.Contains("s05_duel3", due);
            Assert.Contains("s04_trail1", due); // 前兆4を待たずに、決着のすぐあと
            Assert.True(due.IndexOf("s05_duel3") < due.IndexOf("s04_trail1"));
        }

        [Fact]
        public void LastVictorName_IsSaved_AndOldSavesMarkPassedDuelsSeen()
        {
            var state = NewGame();
            StorySystem.MarkSeen(state, "s01_prologue");
            state.Rulers.Single(r => r.RulerId == RulerSystem.VesperaId).LastVictorName = "リナ";
            Defeat(state, "forest", 70);
            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(state.ToSaveData()))!);
            Assert.Equal("リナ", restored.Rulers.Single(r => r.RulerId == RulerSystem.VesperaId).LastVictorName);

            var node = JsonNode.Parse(JsonSerializer.Serialize(state.ToSaveData()))!.AsObject();
            node["StoryRulesVersion"] = 2; // §0.142より前
            var old = GameState.FromSaveData(node.Deserialize<SaveData>()!);
            Assert.True(StorySystem.Seen(old, "s05_duel1"));
            Assert.True(StorySystem.Seen(old, "s05_duel2"));
            Assert.False(StorySystem.Seen(old, "s05_duel3"));
            Assert.DoesNotContain("s05_duel2", Due(old));
        }
    }
}
