using System.Linq;
using System.Text.Json;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>エンディングのあと（§0.90）：主が蘇る・異変を穏やかに・ルミナ・50年目の立ち絵のテスト。</summary>
    public class PostGameTests
    {
        private static GameState NewGame()
        {
            var state = new GameState { WeekNumber = 1, Gold = 1000, MasterMood = 50, DungeonFields = SampleData.CreateDefaultFields() };
            state.Adventurers.AddRange(SampleData.CreateStarterAdventurers());
            return state;
        }

        private static FloorBoss Boss(GameState state, string field, int floor) =>
            state.DungeonFields.First(f => f.Id == field).Bosses.First(b => b.Floor == floor);

        private static GameState Cleared(int clearedAt = 500)
        {
            var state = NewGame();
            state.WeekNumber = clearedAt;
            state.IsGameCleared = true;
            state.ClearedAtWeek = clearedAt;
            return state;
        }

        [Fact]
        public void CsvValues_AreLoaded()
        {
            Assert.Equal(48, PostGameBalance.RevivalWeeks);
            Assert.Equal(new[] { Season.Spring, Season.Autumn }, PostGameBalance.PostClearAnomalySeasons);
            Assert.Equal(JobClass.Ranger, PostGameBalance.LuminaJob);
            Assert.Equal(50, PostGameBalance.LuminaGrownYear);
        }

        [Fact]
        public void NoRevival_BeforeClear()
        {
            var state = NewGame();
            var boss = Boss(state, "forest", 10);
            boss.IsDefeated = true;
            state.WeekNumber = 1000;
            Assert.Empty(PostGameSystem.ProcessWeek(state));
            Assert.True(boss.IsDefeated);
        }

        [Fact]
        public void Bosses_ReviveOneYearAfterDefeat_OrAfterClear()
        {
            var state = Cleared(500);
            var old = Boss(state, "forest", 10);
            old.IsDefeated = true; // クリアの前（記録の無い旧セーブ）に倒した
            var recent = Boss(state, "cave", 10);
            recent.IsDefeated = true;
            recent.LastDefeatedWeek = 520;
            recent.CurrentHp = 0;

            state.WeekNumber = 500 + PostGameBalance.RevivalWeeks - 1;
            Assert.Empty(PostGameSystem.ProcessWeek(state));
            state.WeekNumber = 500 + PostGameBalance.RevivalWeeks;
            Assert.Equal(new[] { old }, PostGameSystem.ProcessWeek(state));
            Assert.False(old.IsDefeated);
            Assert.True(old.EverDefeated); // 回数の記録の無い旧セーブの主も、蘇ったあと「一度倒した」に数える

            state.WeekNumber = 520 + PostGameBalance.RevivalWeeks;
            Assert.Equal(new[] { recent }, PostGameSystem.ProcessWeek(state));
            Assert.Equal(recent.MaxHp, recent.CurrentHp);
        }

        [Fact]
        public void Defeat_RecordsCountAndWeek_RevivedBossesStillCount()
        {
            var state = Cleared(500);
            var boss = Boss(state, "forest", 10);
            int before = EquipmentSystem.CountDefeatedBosses(state);
            boss.IsDefeated = true;
            DungeonExpeditionSystem.ApplyFieldProgression(state, boss);
            Assert.Equal((1, 500), (boss.DefeatCount, boss.LastDefeatedWeek!.Value));
            Assert.Equal(before + 1, EquipmentSystem.CountDefeatedBosses(state));

            state.WeekNumber = 500 + PostGameBalance.RevivalWeeks;
            PostGameSystem.ProcessWeek(state);
            Assert.False(boss.IsDefeated);
            Assert.True(boss.EverDefeated);
            Assert.Equal(before + 1, EquipmentSystem.CountDefeatedBosses(state)); // 蘇っても減らない
        }

        [Fact]
        public void Anomalies_OnlySpringAndAutumn_AfterClear()
        {
            var state = NewGame();
            Assert.True(PostGameSystem.AnomalySeasonAllowed(state, 16)); // 夏・クリアの前
            state.IsGameCleared = true;
            Assert.True(PostGameSystem.AnomalySeasonAllowed(state, 4)); // 春
            Assert.False(PostGameSystem.AnomalySeasonAllowed(state, 16)); // 夏
            Assert.True(PostGameSystem.AnomalySeasonAllowed(state, 28)); // 秋
            Assert.False(PostGameSystem.AnomalySeasonAllowed(state, 40)); // 冬
        }

        [Fact]
        public void Lumina_JoinsAfterSortie_WithGuildBestStats()
        {
            var state = Cleared(500);
            state.Adventurers[0].STR = 88;
            state.Adventurers[1].DEX = 77;
            Assert.False(PostGameSystem.EnsureLumina(state)); // 初出撃をまだ見ていない
            state.StorySeenWeeks["s01_lumina"] = 9;
            StorySystem.MarkSeen(state, "s04_sortie"); // 見たら加わる

            var lumina = PostGameSystem.Lumina(state)!;
            Assert.NotNull(lumina);
            Assert.False(PostGameSystem.EnsureLumina(state)); // 二人目は来ない
            Assert.Equal(("ルミナ", JobClass.Ranger, 0, 100), (lumina.Name, lumina.JobClass, lumina.WeeklyWage, lumina.Satisfaction));
            Assert.Equal(88, lumina.STR);
            Assert.Equal(77, lumina.DEX);
            Assert.Equal(100, lumina.PA_AGI);
            Assert.Equal(14 + (500 - 9) / 48, lumina.Age);
            Assert.Contains("NightVision", lumina.TraitIds);
            Assert.Equal("lumina", lumina.PortraitId);
            Assert.True(lumina.NeverRetires);
        }

        [Fact]
        public void Lumina_NeverRetires_NoNegotiation_NotAParent()
        {
            var state = Cleared(500);
            StorySystem.MarkSeen(state, "s04_sortie");
            var lumina = PostGameSystem.Lumina(state)!;
            lumina.Age = AgingSystem.RetirementAge + 3;
            state.WeekNumber = GameCalendar.WeeksPerYear * 11; // 年度末
            new AgingSystem(new SeededRng(1)).ProcessWeeklyAging(state);
            Assert.Contains(lumina, state.Adventurers);
            Assert.False(lumina.IsRetired);
            Assert.Empty(new AgingSystem(new SeededRng(1)).RetireVoluntarily(state, lumina));

            lumina.Satisfaction = 0;
            new SatisfactionSystem().ProcessWeeklyNegotiation(state);
            Assert.False(lumina.NeedsNegotiation);
            Assert.DoesNotContain(lumina, SoulFusionSystem.GuildMembers(state));
        }

        [Fact]
        public void Lumina_GrownPortrait_FromYear50()
        {
            var state = Cleared(500);
            StorySystem.MarkSeen(state, "s04_sortie");
            state.WeekNumber = GameCalendar.WeeksPerYear * 49; // 49年目の終わり
            PostGameSystem.ProcessWeek(state);
            Assert.Equal("lumina", PostGameSystem.Lumina(state)!.PortraitId);
            state.WeekNumber = GameCalendar.WeeksPerYear * 49 + 1; // 50年目
            PostGameSystem.ProcessWeek(state);
            Assert.Equal("lumina_grown", PostGameSystem.Lumina(state)!.PortraitId);
        }

        [Fact]
        public void OldClearedSave_LuminaJoinsOnNextWeek()
        {
            var state = Cleared(500);
            var data = state.ToSaveData();
            data.StorySeenWeeks = null; // §0.86より前のセーブ（物語は見たことにする）
            var loaded = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(data))!);
            Assert.Null(PostGameSystem.Lumina(loaded));
            PostGameSystem.ProcessWeek(loaded);
            Assert.NotNull(PostGameSystem.Lumina(loaded));

            var round = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(loaded.ToSaveData()))!);
            Assert.Equal(PostGameSystem.LuminaId, PostGameSystem.Lumina(round)!.StoryCharacterId);
        }
    }
}
