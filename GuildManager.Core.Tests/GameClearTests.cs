using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 2026年10月・§0.59のテスト：クリア（深淵100Fのボス撃破）と、エンディングに出すギルドの記録（GuildChronicle）。
    /// 実行方法: `dotnet test GuildManager.Core.Tests --filter FullyQualifiedName~GameClear`
    /// </summary>
    public class GameClearTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private static Adventurer Make(string name, int stat = 200)
        {
            var a = new Adventurer
            {
                Name = name, JobClass = JobClass.Warrior,
                STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat,
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        [Fact]
        public void DefeatingAbyss100F_ThroughExpedition_ClearsAndRecordsParty()
        {
            var boss = new FloorBoss { Name = "深淵の主", Floor = 100, MaxHp = 1, CurrentHp = 1 };
            var forest = new DungeonField { Id = "forest", Name = "森", Order = 1, IsUnlocked = true };
            var abyss = new DungeonField { Id = "abyss", Name = "深淵", Order = 5, IsUnlocked = true, Bosses = { boss } };
            var a = Make("アリス");
            var b = Make("セリア");
            var state = new GameState { WeekNumber = 720, Adventurers = { a, b }, DungeonFields = { forest, abyss } };
            var party = new Party();
            party.TryAdd(a);
            party.TryAdd(b);

            var system = new DungeonExpeditionSystem(
                new ScoutingResolver(new AlwaysMinRng()), new DungeonResolver(new AlwaysMinRng()), new SatisfactionSystem(),
                new CompatibilitySystem(new AlwaysMinRng()), new DungeonTraversalResolver(new AlwaysMinRng()), new GatheringResolver(new AlwaysMinRng()));
            Assert.True(system.TryDispatch(state, party, boss, DungeonMissionType.BossAssault));
            var resolution = Assert.Single(system.ProcessWeeklyMissions(state));

            Assert.Equal(DungeonOutcome.Victory, resolution.DungeonResult!.Outcome);
            Assert.True(state.IsGameCleared);
            Assert.Equal(720, state.ClearedAtWeek);
            Assert.Equal(new[] { a.Id, b.Id }, state.ClearingMemberIds);
        }

        [Fact]
        public void Chronicle_SummarizesTheGuild()
        {
            var forestBosses = new List<FloorBoss>
            {
                new() { Floor = 10, IsDefeated = true }, new() { Floor = 20, IsDefeated = true },
            };
            var abyssBosses = new List<FloorBoss>
            {
                new() { Floor = 10, IsDefeated = true }, new() { Floor = 100, IsDefeated = true }, new() { Floor = 90, IsDefeated = false },
            };
            var founderA = Make("初代A");
            var founderB = Make("初代B");
            var daughter = Make("二代目");
            daughter.ParentIds = new List<Guid> { founderA.Id, founderB.Id };
            var granddaughter = Make("三代目");
            granddaughter.ParentIds = new List<Guid> { daughter.Id, Guid.NewGuid() }; // 記録に無い親は第1世代扱い
            granddaughter.TotalContributionScore = 900;
            founderA.TotalContributionScore = 500;
            var expelled = Make("除籍者");

            var state = new GameState
            {
                WeekNumber = 800,
                ClearedAtWeek = 15 * GameCalendar.WeeksPerYear - 5, // 15年目
                IsGameCleared = true,
                TotalDispatchCount = 321,
                Adventurers = { daughter, granddaughter },
                RetiredAdventurers = { founderA, founderB },
                FallenAdventurers = { expelled },
                ClearingMemberIds = new List<Guid> { granddaughter.Id, Guid.NewGuid() },
                DungeonFields =
                {
                    new DungeonField { Id = "f", Order = 1, Bosses = forestBosses },
                    new DungeonField { Id = "a", Order = 5, Bosses = abyssBosses },
                },
            };

            var c = GuildChronicle.Build(state);

            Assert.Equal(15 * GameCalendar.WeeksPerYear - 5, c.ClearedAtWeek);
            Assert.Equal(15, c.Years);
            Assert.Equal(5, c.TotalMembers);
            Assert.Equal(2, c.ActiveCount);
            Assert.Equal(2, c.RetiredCount);
            Assert.Equal(1, c.ExpelledCount);
            Assert.Equal(2, c.DaughterCount);
            Assert.Equal(3, c.MaxGeneration);
            Assert.Equal(4, c.BossesDefeated);
            Assert.Equal(5, c.TotalBosses);
            Assert.Equal(1, c.FieldsConquered);
            Assert.Equal(2, c.TotalFields);
            Assert.Equal(321, c.TotalDispatchCount);
            Assert.Equal(new[] { "三代目" }, c.ClearingMemberNames); // 記録に無い者は出さない
            Assert.Equal("三代目", c.TopContributorName);
            Assert.Equal(900, c.TopContributorScore);
        }

        [Fact]
        public void Chronicle_BeforeClear_UsesCurrentWeek_AndEmptyGuildIsSafe()
        {
            var c = GuildChronicle.Build(new GameState { WeekNumber = 50 });
            Assert.Equal(50, c.ClearedAtWeek);
            Assert.Equal(2, c.Years);
            Assert.Equal(0, c.TotalMembers);
            Assert.Equal(0, c.MaxGeneration);
            Assert.Null(c.TopContributorName);
        }

        [Fact]
        public void Generation_StopsOnCorruptedCycle()
        {
            var a = Make("A");
            var b = Make("B");
            a.ParentIds = new List<Guid> { b.Id };
            b.ParentIds = new List<Guid> { a.Id };
            var state = new GameState { Adventurers = { a, b } };
            Assert.InRange(GuildChronicle.GenerationOf(state, a), 1, 3);
        }
    }
}
