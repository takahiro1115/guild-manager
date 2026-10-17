using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>迷宮の主の場面（§0.142・§0.143、→ story_05.txt）：届く声 1〜4・ヴェスペラとの決闘 1〜3と作戦会議、足跡 1 は決着のすぐあと。</summary>
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

        [Fact]
        public void Duel1_GivesNoTactics_TheMeetingAfterTheFirstDuelDoes()
        {
            // 攻め方のヒントは、はじめは話さず、初めて戦った月の作戦会議で話す（§0.143）
            var state = NewGame();
            Defeat(state, "forest", 60);
            var invite = Texts(state, "s05_duel1");
            Assert.DoesNotContain(invite, t => t.Contains("MND") || t.Contains("魅入られ"));
            StorySystem.MarkSeen(state, "s05_duel1");
            Assert.DoesNotContain("s05_meet1", Due(state)); // まだ戦っていない

            state.Rulers.Single(r => r.RulerId == RulerSystem.VesperaId).DuelCount = 1;
            Assert.DoesNotContain("s05_meet1", Due(state)); // 招待と同じ月に戦っていても、会議は次の月から
            state.WeekNumber += GameCalendar.WeeksPerMonth;
            var meeting = Texts(state, "s05_meet1");
            Assert.Contains(meeting, t => t.Contains("得物を替えて"));
            Assert.Contains(meeting, t => t.Contains("相棒（相性80以上）"));
            Assert.DoesNotContain(meeting, t => t.Contains("{"));
            StorySystem.MarkSeen(state, "s05_meet1");

            // 会議を見たあとの決闘 2 には、初戦の一言を足さない
            Defeat(state, "forest", 70);
            var retreat = Texts(state, "s05_duel2");
            Assert.DoesNotContain(retreat, t => t.Contains("一度で退かせた"));
            Assert.DoesNotContain(retreat, t => t.Contains("{"));
        }

        [Fact]
        public void FirstDuelRetreat_SkipsTheMeeting_AndDuel2CarriesTheHints()
        {
            var state = NewGame();
            Defeat(state, "forest", 60);
            StorySystem.MarkSeen(state, "s05_duel1");
            state.Rulers.Single(r => r.RulerId == RulerSystem.VesperaId).DuelCount = 1;
            Defeat(state, "forest", 70); // 初戦でいきなり退かせた
            var due = Due(state);
            Assert.DoesNotContain("s05_meet1", due);
            var retreat = Texts(state, "s05_duel2");
            Assert.Contains(retreat, t => t.Contains("一度で退かせた"));
            Assert.Contains(retreat, t => t.Contains("魅了は MND で耐える"));
            Assert.DoesNotContain(retreat, t => t.Contains("{"));
        }

        [Fact]
        public void OldSaves_WhoAlreadyFoughtVespera_SkipTheMeeting()
        {
            var state = NewGame();
            StorySystem.MarkSeen(state, "s01_prologue");
            Defeat(state, "forest", 60);
            StorySystem.MarkSeen(state, "s05_duel1");
            JsonObject Saved(int duels, int version)
            {
                state.Rulers.Single(r => r.RulerId == RulerSystem.VesperaId).DuelCount = duels;
                var node = JsonNode.Parse(JsonSerializer.Serialize(state.ToSaveData()))!.AsObject();
                node["StoryRulesVersion"] = version;
                return node;
            }
            var fought = GameState.FromSaveData(Saved(1, 3).Deserialize<SaveData>()!); // §0.143より前・もう戦った
            Assert.True(StorySystem.Seen(fought, "s05_meet1"));
            Assert.DoesNotContain("s05_meet1", Due(fought));
            var notYet = GameState.FromSaveData(Saved(0, 3).Deserialize<SaveData>()!); // まだ戦っていない
            Assert.False(StorySystem.Seen(notYet, "s05_meet1"));
            var current = GameState.FromSaveData(Saved(1, 4).Deserialize<SaveData>()!); // §0.143のセーブは補わない
            Assert.False(StorySystem.Seen(current, "s05_meet1"));
        }
    }
}
