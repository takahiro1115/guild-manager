using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests.Systems
{
    /// <summary>
    /// 待機お手伝い（→ MasterMoodSystem.ProcessIdleHelp、03 §8.1、2026年9月新設）のテスト。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~IdleAdventurerHelp`
    /// </summary>
    public class IdleAdventurerHelpTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private static readonly IReadOnlySet<Guid> NoneDispatched = new HashSet<Guid>();

        private static Adventurer Healthy(string name)
        {
            var a = new Adventurer { Name = name, VIT = 30 };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static GameState StateWith(params Adventurer[] adventurers)
        {
            var state = new GameState { Gold = 1000, MasterMood = 50 };
            state.Adventurers.AddRange(adventurers);
            return state;
        }

        [Fact]
        public void CsvValues_AreLoaded()
        {
            Assert.Equal(15, MasterMoodBalance.IdleAdventurerHelpGold);
            Assert.Equal(1, MasterMoodBalance.IdleAdventurerHelpMood);
        }

        [Fact]
        public void OneHealthyIdleAdventurer_Adds15GoldAnd1Mood_AndIsRecorded()
        {
            var state = StateWith(Healthy("リナ"));
            var report = new MasterMoodReport { MoodBefore = 50 };

            var entries = MasterMoodSystem.ProcessIdleHelp(state, NoneDispatched, report);

            Assert.Equal(1015, state.Gold);
            Assert.Equal(51, state.MasterMood);
            var entry = Assert.Single(entries);
            Assert.Equal("リナ", entry.Name);
            Assert.Equal(15, entry.Gold);
            Assert.Equal(1, entry.Mood);
            Assert.Equal(1, entry.MoodApplied);
            Assert.Contains(report.Entries, e => e.Reason.StartsWith("待機お手伝い") && e.Applied == 1);
            Assert.Equal(51, report.MoodAfter);
        }

        [Fact]
        public void TwoHealthyIdleAdventurers_AccumulateGoldAndMood()
        {
            var state = StateWith(Healthy("リナ"), Healthy("フィオナ"));

            var entries = MasterMoodSystem.ProcessIdleHelp(state, NoneDispatched, new MasterMoodReport());

            Assert.Equal(1030, state.Gold);
            Assert.Equal(52, state.MasterMood);
            Assert.Equal(2, entries.Count);
        }

        [Fact]
        public void DispatchedAdventurers_AreExcluded()
        {
            var away = Healthy("出撃中");
            away.IsDispatched = true;
            var returned = Healthy("今週帰還"); // 解決で IsDispatched は下りたが、決算開始時点では出撃中だった
            var state = StateWith(away, returned, Healthy("待機"));

            var entries = MasterMoodSystem.ProcessIdleHelp(state, new HashSet<Guid> { away.Id, returned.Id }, new MasterMoodReport());

            Assert.Equal("待機", Assert.Single(entries).Name);
            Assert.Equal(1015, state.Gold);
        }

        [Fact]
        public void AdventurerAssignedToTraining_IsExcluded()
        {
            var trainee = Healthy("訓練中");
            var state = StateWith(trainee, Healthy("待機"));
            state.TrainingAssignments[trainee.Id] = FacilityType.WarriorHall;

            var entries = MasterMoodSystem.ProcessIdleHelp(state, NoneDispatched, new MasterMoodReport());

            Assert.Equal("待機", Assert.Single(entries).Name);
            Assert.Equal(1015, state.Gold);
            Assert.Equal(51, state.MasterMood);
        }

        [Fact]
        public void AdventurerMissingEvenOneHp_IsExcluded()
        {
            var hurt = Healthy("かすり傷");
            hurt.CurrentHP = hurt.MaxHP - 1;
            var state = StateWith(hurt);

            var entries = MasterMoodSystem.ProcessIdleHelp(state, NoneDispatched, new MasterMoodReport());

            Assert.Empty(entries);
            Assert.Equal(1000, state.Gold);
            Assert.Equal(50, state.MasterMood);
        }

        [Theory]
        [InlineData(InjurySeverity.Light, 1)]
        [InlineData(InjurySeverity.Severe, 3)]
        [InlineData(InjurySeverity.None, 1)] // 負傷は治っても残り週数が残っていれば対象外
        public void InjuredAdventurer_IsExcluded(InjurySeverity severity, int weeksRemaining)
        {
            var injured = Healthy("負傷");
            injured.Injury = severity;
            injured.InjuryWeeksRemaining = weeksRemaining;
            var state = StateWith(injured);

            Assert.Empty(MasterMoodSystem.ProcessIdleHelp(state, NoneDispatched, new MasterMoodReport()));
            Assert.Equal(1000, state.Gold);
        }

        [Fact]
        public void Mood_IsClampedAt100()
        {
            var state = StateWith(Healthy("A"), Healthy("B"));
            state.MasterMood = 99;
            var report = new MasterMoodReport();

            var entries = MasterMoodSystem.ProcessIdleHelp(state, NoneDispatched, report);

            Assert.Equal(100, state.MasterMood);
            Assert.Equal(1030, state.Gold); // Goldはクランプされない
            Assert.Equal(new[] { 1, 0 }, entries.Select(e => e.MoodApplied));
            var moodEntry = Assert.Single(report.Entries);
            Assert.Equal(2, moodEntry.Delta);
            Assert.Equal(1, moodEntry.Applied);
        }

        [Fact]
        public void ProcessWeek_RecordsIdleHelp_InSettlementAndMoodReport()
        {
            var state = StateWith(Healthy("リナ"));
            var system = new WeekProcessingSystem(
                masterMoodSystem: new MasterMoodSystem(),
                economySystem: new EconomySystem(),
                trainingSystem: new TrainingSystem(),
                injuryRecoverySystem: new InjuryRecoverySystem(),
                restRecoverySystem: new RestRecoverySystem(),
                growthSystem: new GrowthSystem(new AlwaysMinRng()),
                satisfactionSystem: new SatisfactionSystem(),
                agingSystem: new AgingSystem(new AlwaysMinRng()),
                facilitySystem: new FacilitySystem(),
                defeatSystem: new DefeatSystem(),
                recruitmentSystem: new RecruitmentSystem(new AlwaysMinRng()));

            var result = system.ProcessWeek(state);

            Assert.Equal("リナ", Assert.Single(result.IdleHelpEntries).Name);
            Assert.Contains(result.MoodReport.Entries, e => e.Reason.StartsWith("待機お手伝い"));
        }

        [Fact]
        public void ProcessWeek_NoEntries_WhenNobodyQualifies()
        {
            var hurt = Healthy("負傷");
            hurt.CurrentHP = 1;
            var state = StateWith(hurt);
            var system = new WeekProcessingSystem(
                new MasterMoodSystem(), new EconomySystem(), new TrainingSystem(), new InjuryRecoverySystem(),
                new RestRecoverySystem(), new GrowthSystem(new AlwaysMinRng()), new SatisfactionSystem(),
                new AgingSystem(new AlwaysMinRng()), new FacilitySystem(), new DefeatSystem(),
                new RecruitmentSystem(new AlwaysMinRng()));

            var result = system.ProcessWeek(state);

            Assert.Empty(result.IdleHelpEntries);
            Assert.DoesNotContain(result.MoodReport.Entries, e => e.Reason.StartsWith("待機お手伝い"));
        }
    }
}
