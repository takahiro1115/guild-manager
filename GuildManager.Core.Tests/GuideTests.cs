using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>ギルドの手引きとタブを隠す（§0.87）のテスト。</summary>
    public class GuideTests
    {
        private static GameState NewGame()
        {
            var state = new GameState { WeekNumber = 1, Gold = 1000, MasterMood = 50, DungeonFields = SampleData.CreateDefaultFields() };
            state.Adventurers.AddRange(SampleData.CreateStarterAdventurers());
            return state;
        }

        private static FloorBoss Forest(GameState state, int floor) =>
            state.DungeonFields.First(f => f.Id == "forest").Bosses.First(b => b.Floor == floor);

        [Fact]
        public void EveryGuideLine_HasACondition()
        {
            foreach (var id in StorySystem.SceneIds)
            {
                int guides = StoryBalance.Get(id).Pages.SelectMany(p => p).Count(l => l.Kind == StoryLineKind.Guide);
                int conditions = GuideSystem.ConditionCounts.GetValueOrDefault(id);
                Assert.True(guides == conditions, $"{id}：【手引き】{guides}行に対して条件{conditions}個");
            }
        }

        [Fact]
        public void Prologue_ItemsCheckOff_AsThePlayerActs()
        {
            var state = NewGame();
            Assert.Empty(GuideSystem.Groups(state)); // まだ何も見ていない
            StorySystem.MarkSeen(state, "s01_prologue");
            var g = Assert.Single(GuideSystem.Groups(state));
            Assert.False(g.AllDone);
            Assert.Equal(1, GuideSystem.OpenCount(state));

            state.Adventurers.AddRange(SampleData.CreateStarterAdventurers().Take(2));
            StorySystem.MarkSeen(state, "s01_guild");
            var groups = GuideSystem.Groups(state);
            Assert.True(groups[0].AllDone);
            Assert.False(groups[1].AllDone);
            Assert.Equal("部隊・冒険者", groups[1].Jump);

            var party = new SavedParty { Name = "第1部隊", MemberIds = { state.Adventurers[0].Id } };
            state.SavedParties.Add(party);
            Assert.Equal(0, GuideSystem.OpenCount(state));
        }

        [Fact]
        public void DoneGroups_StayThisMonth_ThenDisappear()
        {
            var state = NewGame();
            StorySystem.MarkSeen(state, "s01_question");
            state.CompletedResearchIds.Add("x");
            state.WeekNumber = 2;
            var g = Assert.Single(GuideSystem.Groups(state));
            Assert.True(g.AllDone);
            Assert.NotEmpty(g.Notes);
            state.WeekNumber = 4; // 同じ月
            Assert.Single(GuideSystem.Groups(state));
            state.WeekNumber = 5; // 次の月
            Assert.Empty(GuideSystem.Groups(state));
        }

        [Fact]
        public void Forest10_AppraiseEquipAndRescue()
        {
            var state = NewGame();
            StorySystem.MarkSeen(state, "s01_forest10");
            var items = GuideSystem.Groups(state).Single().Items;
            Assert.All(items, i => Assert.False(i.Done));

            var relic = EquipmentItem.FromCatalog(ItemCatalog.FindById("IronSword")!);
            relic.Rarity = ItemRarity.Rare;
            state.Armory.Add(relic);
            Assert.Equal(new[] { true, false, false }, GuideSystem.Groups(state).Single().Items.Select(i => i.Done));
            state.Armory.Remove(relic);
            state.Adventurers[0].EquippedWeapon = relic;
            Forest(state, 20).IsDefeated = true;
            Assert.True(GuideSystem.Groups(state).Single().AllDone);
        }

        [Fact]
        public void Hunter_RemembersTheBoss_AfterItIsDefeated()
        {
            var state = NewGame();
            var boss = Forest(state, 40);
            state.StoryCounters[$"retreat:{boss.Id}"] = 2;
            StorySystem.MarkSeen(state, "s01_hunter");
            var g = GuideSystem.Groups(state).Single();
            Assert.Contains("40Fの主を解析", g.Items[0].Text);
            boss.IntelRate = 0.3;
            Assert.Equal(new[] { true, false }, GuideSystem.Groups(state).Single().Items.Select(i => i.Done));
            boss.IsDefeated = true;
            var done = GuideSystem.Groups(state).Single();
            Assert.True(done.AllDone);
            Assert.Contains("40Fの主を解析", done.Items[0].Text); // 撃破のあとも同じボスを指す
        }

        [Fact]
        public void Advice_RemembersTheFacility_EvenAfterMoreOpen()
        {
            var state = NewGame();
            state.FacilityUnlockedLevels[FacilityType.SkillHall] = 1;
            StorySystem.MarkSeen(state, "s02_advice");
            state.FacilityUnlockedLevels[FacilityType.DrillHall] = 1; // あとで鍛錬所も開いた
            var g = GuideSystem.Groups(state).Single();
            Assert.Equal("技巧所を建てる", g.Items[0].Text);
            Assert.False(g.Items[0].Done);
            var hall = state.Facilities.FirstOrDefault(x => x.Type == FacilityType.SkillHall);
            if (hall == null) state.Facilities.Add(new Facility { Type = FacilityType.SkillHall, CurrentLevel = 1 }); else hall.CurrentLevel = 1;
            Assert.True(GuideSystem.Groups(state).Single().Items[0].Done);
        }

        [Fact]
        public void Tabs_HiddenUntilTaught()
        {
            var state = NewGame();
            foreach (var tab in System.Enum.GetValues<GuideTab>())
                Assert.False(GuideSystem.IsTabOpen(state, tab));
            StorySystem.MarkSeen(state, "s01_forest10");
            Assert.True(GuideSystem.IsTabOpen(state, GuideTab.Warehouse));
            Assert.True(GuideSystem.IsTabOpen(state, GuideTab.Shop));
            Assert.False(GuideSystem.IsTabOpen(state, GuideTab.Research));
            StorySystem.MarkSeen(state, "s01_question");
            StorySystem.MarkSeen(state, "s02_visit");
            Assert.True(GuideSystem.IsTabOpen(state, GuideTab.Research));
            Assert.True(GuideSystem.IsTabOpen(state, GuideTab.Tournament));
            Assert.False(GuideSystem.IsTabOpen(state, GuideTab.Facility));
            StorySystem.MarkSeen(state, "s02_advice");
            Assert.True(GuideSystem.IsTabOpen(state, GuideTab.Facility));
        }

        [Fact]
        public void NoTutorial_SkipsTeachingScenes_KeepsHighlights_AllTabsNoGuide()
        {
            var state = NewGame();
            StorySystem.DisableTutorial(state);
            Assert.False(state.TutorialEnabled);
            foreach (var tab in System.Enum.GetValues<GuideTab>())
                Assert.True(GuideSystem.IsTabOpen(state, tab));

            // 序章とギルドへは残り、方針・月を進めるは出ない
            Assert.Equal(new[] { "s01_prologue" }, StorySystem.DueScenes(state, StoryTiming.Interactive).Select(s => s.SceneId));
            StorySystem.MarkSeen(state, "s01_prologue");
            state.Adventurers.AddRange(SampleData.CreateStarterAdventurers().Take(2));
            StorySystem.MarkSeen(state, "s01_guild");
            state.SavedParties.Add(new SavedParty { Name = "第1部隊", MemberIds = { state.Adventurers[0].Id }, Order = SquadOrder.Dive });
            Assert.Empty(StorySystem.DueScenes(state, StoryTiming.Interactive));
            Assert.Empty(GuideSystem.Groups(state));

            // ルミナは出て、最初の月報・最初の質問は出ない
            state.MonthlyReports.Add(new MonthlyReport());
            Forest(state, 20).IsDefeated = true;
            Assert.Contains("s01_lumina", StorySystem.DueScenes(state, StoryTiming.BeforeReport).Select(s => s.SceneId));
            StorySystem.MarkSeen(state, "s01_lumina");
            state.WeekNumber = 9;
            Assert.Equal(new[] { "s01_elder" }, StorySystem.DueScenes(state, StoryTiming.AfterReport).Select(s => s.SceneId));

            // セーブに残る
            var restored = GameState.FromSaveData(System.Text.Json.JsonSerializer.Deserialize<SaveData>(System.Text.Json.JsonSerializer.Serialize(state.ToSaveData()))!);
            Assert.False(restored.TutorialEnabled);
            var old = NewGame().ToSaveData();
            old.TutorialEnabled = null;
            Assert.True(GameState.FromSaveData(old).TutorialEnabled);
        }

        [Fact]
        public void OldSave_AllTabsOpen_NoGuide()
        {
            var state = NewGame();
            StorySystem.MarkTutorialSeen(state);
            foreach (var tab in System.Enum.GetValues<GuideTab>())
                Assert.True(GuideSystem.IsTabOpen(state, tab));
            Assert.Empty(GuideSystem.Groups(state)); // 手引きも出さない
        }
    }
}
