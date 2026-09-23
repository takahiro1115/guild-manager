using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// マスターの機嫌（→ MasterMoodSystem、03 §8.1・§8.1.1）・内職売上（→ EconomySystem.ProcessWeeklySideJobIncome）・
    /// 副官解雇（→ DefeatSystem）の単体テスト。旧・名声／ギルド格付け／月次助成金の後継（2026年9月）。
    /// </summary>
    public class MasterMoodSystemTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private static readonly DungeonField Field = new() { Id = "forest", Name = "森", Order = 1, IsUnlocked = true };

        private static DungeonMissionResolution Traversal(int unexploredFloors) =>
            new(new Party(), null, Field, 0, new TraversalResult { UnexploredFloorsAdvanced = unexploredFloors });

        private static DungeonMissionResolution Survey(GuardTier tier) =>
            new(new Party(), new FloorBoss { Name = "調査対象", Floor = 10 }, Field, 0, new ScoutingResult { GuardTier = tier }, DungeonMissionType.Survey);

        private static DungeonMissionResolution BossBattle(DungeonOutcome outcome) =>
            new(new Party(), new FloorBoss { Name = "森の主", Floor = 10 }, Field, 0, new DungeonResult { Outcome = outcome });

        private static DungeonMissionResolution Gathering(string materialId, int count) =>
            new(new Party(), Field, new GatheringResult { MaterialId = materialId, MaterialCount = count });

        // ---------------- 成果による上昇と、経過週数のリセット ----------------

        public static TheoryData<string, int> AchievementCases => new()
        {
            { "boss", MasterMoodBalance.BossDefeatMoodGain },
            { "traversal", 3 * MasterMoodBalance.PioneerMoodPerFloor },
            { "survey-abundant", MasterMoodBalance.SurveySuccessMoodGain },
            { "survey-sufficient", MasterMoodBalance.SurveySuccessMoodGain },
            { "gathering", MasterMoodBalance.GatheringMoodGain },
        };

        [Theory]
        [MemberData(nameof(AchievementCases))]
        public void ProcessWeeklyMood_Achievement_RaisesMood_AndResetsActivityCounter(string kind, int expectedGain)
        {
            var resolution = kind switch
            {
                "boss" => BossBattle(DungeonOutcome.Victory),
                "traversal" => Traversal(unexploredFloors: 3),
                "survey-abundant" => Survey(GuardTier.Abundant),
                "survey-sufficient" => Survey(GuardTier.Sufficient),
                _ => Gathering(MaterialIds.ForestHerb, 4),
            };
            var state = new GameState { MasterMood = 50, WeeksSinceLastGuildActivity = 7 };

            var report = new MasterMoodSystem().ProcessWeeklyMood(state, new[] { resolution });

            Assert.Equal(50 + expectedGain, state.MasterMood);
            Assert.Equal(0, state.WeeksSinceLastGuildActivity);
            Assert.False(report.Bored);
            Assert.Equal(expectedGain, report.Delta);
        }

        [Fact]
        public void ProcessWeeklyMood_MultipleAchievements_AreSummed()
        {
            var state = new GameState { MasterMood = 40 };

            var report = new MasterMoodSystem().ProcessWeeklyMood(state, new[]
            {
                BossBattle(DungeonOutcome.Victory), Traversal(2), Gathering(MaterialIds.ForestHerb, 1),
            });

            int expected = 40 + MasterMoodBalance.BossDefeatMoodGain + 2 * MasterMoodBalance.PioneerMoodPerFloor + MasterMoodBalance.GatheringMoodGain;
            Assert.Equal(expected, state.MasterMood);
            Assert.Equal(3, report.Entries.Count);
        }

        // ---------------- 成果ゼロの週：退屈減衰 ----------------

        [Fact]
        public void ProcessWeeklyMood_NoMissions_DecaysByBoredom_AndIncrementsCounter()
        {
            var state = new GameState { MasterMood = 50, WeeksSinceLastGuildActivity = 2 };

            var report = new MasterMoodSystem().ProcessWeeklyMood(state, Array.Empty<DungeonMissionResolution>());

            Assert.Equal(50 - MasterMoodBalance.BoredomMoodDecay, state.MasterMood);
            Assert.Equal(5, MasterMoodBalance.BoredomMoodDecay);
            Assert.Equal(3, state.WeeksSinceLastGuildActivity);
            Assert.True(report.Bored);
        }

        [Fact]
        public void ProcessWeeklyMood_OnlyNonAchievements_CountAsZeroWeek()
        {
            // 既踏階層だけの潜行・ボス戦の撤退・素材ゼロの採取は成果に数えない。
            var state = new GameState { MasterMood = 50 };

            new MasterMoodSystem().ProcessWeeklyMood(state, new[]
            {
                Traversal(0), BossBattle(DungeonOutcome.Retreat), Gathering("", 1),
            });

            Assert.Equal(50 - MasterMoodBalance.BoredomMoodDecay, state.MasterMood);
            Assert.Equal(1, state.WeeksSinceLastGuildActivity);
        }

        [Fact]
        public void ProcessWeeklyMood_RoutedSurvey_LowersMood_AndStillCountsAsZeroWeek()
        {
            // 迷宮調査の潰走（護衛不足）は−3、しかも成果ゼロ扱いなので退屈減衰−5も重なる。
            var state = new GameState { MasterMood = 50 };

            var report = new MasterMoodSystem().ProcessWeeklyMood(state, new[] { Survey(GuardTier.Deficient) });

            Assert.Equal(50 - MasterMoodBalance.SurveyRoutedMoodLoss - MasterMoodBalance.BoredomMoodDecay, state.MasterMood);
            Assert.True(report.Bored);
            Assert.Equal(1, state.WeeksSinceLastGuildActivity);
        }

        [Fact]
        public void ProcessWeeklyMood_MarginalSurvey_CountsAsActivity_WithoutMoodChange()
        {
            var state = new GameState { MasterMood = 50, WeeksSinceLastGuildActivity = 4 };

            new MasterMoodSystem().ProcessWeeklyMood(state, new[] { Survey(GuardTier.Marginal) });

            Assert.Equal(50, state.MasterMood);
            Assert.Equal(0, state.WeeksSinceLastGuildActivity);
        }

        [Fact]
        public void Adjust_ClampsBetweenZeroAndHundred()
        {
            var state = new GameState { MasterMood = 95 };
            Assert.Equal(5, MasterMoodSystem.Adjust(state, 20));
            Assert.Equal(100, state.MasterMood);

            state.MasterMood = 3;
            Assert.Equal(-3, MasterMoodSystem.Adjust(state, -5));
            Assert.Equal(0, state.MasterMood);
        }

        // ---------------- 内職売上（機嫌の段階ごとの倍率） ----------------

        [Theory]
        [InlineData(100, MasterMoodTier.Cheerful, 1.5)]
        [InlineData(60, MasterMoodTier.Normal, 1.0)]
        [InlineData(30, MasterMoodTier.Grumpy, 0.5)]
        [InlineData(10, MasterMoodTier.Crisis, 0.0)]
        public void SideJobIncome_AppliesMoodMultiplier(int mood, MasterMoodTier expectedTier, double expectedMultiplier)
        {
            var state = new GameState { WeekNumber = EconomyBalance.SideJobIntervalWeeks, MasterMood = mood, Gold = 0 };

            var income = new EconomySystem().ProcessWeeklySideJobIncome(state);

            Assert.NotNull(income);
            Assert.Equal(expectedTier, income!.Tier);
            Assert.Equal(expectedMultiplier, income.Multiplier, precision: 6);
            Assert.Equal(EconomyBalance.SideJobBaseAmount, income.BaseGold);
            int expectedGold = (int)Math.Round(EconomyBalance.SideJobBaseAmount * expectedMultiplier);
            Assert.Equal(expectedGold, income.FinalGold);
            Assert.Equal(expectedGold, state.Gold);
        }

        [Fact]
        public void SideJobIncome_IsNull_OnNonIntervalWeek()
        {
            var state = new GameState { WeekNumber = EconomyBalance.SideJobIntervalWeeks + 1, Gold = 0 };

            Assert.Null(new EconomySystem().ProcessWeeklySideJobIncome(state));
            Assert.Equal(0, state.Gold);
        }

        // ---------------- 機嫌の段階ごとのアルベールの一言（2026年9月新設） ----------------

        [Theory]
        [InlineData(100, "「ふふ、いいデータが届いたわ。今夜は気分がいいから調合も捗るわね」")]
        [InlineData(80, "「ふふ、いいデータが届いたわ。今夜は気分がいいから調合も捗るわね」")]
        [InlineData(60, "「順調ね。次も期待しているわよ、副官」")]
        [InlineData(50, "「順調ね。次も期待しているわよ、副官」")]
        [InlineData(30, "「……はあ。退屈ね。薬の注文なんて放っておいて頂戴、気分じゃないの」")]
        [InlineData(10, "「ねえ副官、あなた本当に私の役に立っているのかしら？ 次はないと思いなさい」")]
        [InlineData(1, "「ねえ副官、あなた本当に私の役に立っているのかしら？ 次はないと思いなさい」")]
        public void GetAlbertLine_ReturnsLineForMoodTier(int mood, string expected) =>
            Assert.Equal(expected, MasterMoodSystem.GetAlbertLine(MasterMoodSystem.GetTier(mood)));

        [Fact]
        public void ApplyForcedRetirementFury_LowersTwentyPerMember_ClampedAtZero()
        {
            var state = new GameState { MasterMood = 70 };
            Assert.Equal(-20, MasterMoodSystem.ApplyForcedRetirementFury(state, 1));
            Assert.Equal(50, state.MasterMood);
            Assert.Equal(-40, MasterMoodSystem.ApplyForcedRetirementFury(state, 2));
            Assert.Equal(10, state.MasterMood);
            Assert.Equal(-10, MasterMoodSystem.ApplyForcedRetirementFury(state, 3)); // 下限0
            Assert.Equal(0, state.MasterMood);
            Assert.Equal(0, MasterMoodSystem.ApplyForcedRetirementFury(new GameState { MasterMood = 50 }, 0));
        }

        // ---------------- 副官解雇（機嫌0でゲームオーバー） ----------------

        [Fact]
        public void DefeatSystem_MoodZero_DismissedByMaster()
        {
            var state = new GameState { MasterMood = 0, Gold = 1000 };

            var reason = new DefeatSystem().ProcessWeeklySettlement(state);

            Assert.Equal(DefeatReason.DismissedByMaster, reason);
            Assert.Equal(DefeatReason.DismissedByMaster, state.DefeatReason);
        }

        [Fact]
        public void DefeatSystem_PositiveMood_NoDefeat()
        {
            var state = new GameState { MasterMood = 1, Gold = 1000 };

            Assert.Null(new DefeatSystem().ProcessWeeklySettlement(state));
            Assert.Null(state.DefeatReason);
        }

        // ---------------- 週次決算を通した結合 ----------------

        private static WeekProcessingSystem BuildWeekSystem(DungeonExpeditionSystem? expedition = null) =>
            new(
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
                recruitmentSystem: new RecruitmentSystem(new AlwaysMinRng()),
                dungeonExpeditionSystem: expedition);

        [Fact]
        public void ProcessWeek_IdleWeeks_DecayMood_UntilDismissedByMaster()
        {
            // 成果ゼロの週が続くと毎週−5ずつ減り、0に達した週の決算で副官解雇になる。
            var state = new GameState { MasterMood = 2 * MasterMoodBalance.BoredomMoodDecay, Gold = 100_000 };
            var week = BuildWeekSystem();

            var first = week.ProcessWeek(state);
            Assert.Equal(MasterMoodBalance.BoredomMoodDecay, state.MasterMood);
            Assert.Equal(1, state.WeeksSinceLastGuildActivity);
            Assert.True(first.MoodReport.Bored);
            Assert.Null(first.NewDefeatReason);

            var second = week.ProcessWeek(state);
            Assert.Equal(0, state.MasterMood);
            Assert.Equal(2, state.WeeksSinceLastGuildActivity);
            Assert.Equal(DefeatReason.DismissedByMaster, second.NewDefeatReason);
            Assert.True(second.Flags.DefeatOccurred);
        }

        [Fact]
        public void ProcessWeek_GatheringWeek_RaisesMood_AndResetsCounter_EndToEnd()
        {
            var forest = new DungeonField { Id = "forest", Name = "森", Order = 1, IsUnlocked = true, ReachedFloor = 1 };
            var state = new GameState { DungeonFields = { forest }, MasterMood = 50, WeeksSinceLastGuildActivity = 3 };
            var gatherer = new Adventurer { Name = "採取係", Age = 18, JobClass = JobClass.Ranger, STR = 40, AGI = 40, VIT = 40, MND = 40, DEX = 40, LDR = 40, INT = 40 };
            gatherer.CurrentHP = gatherer.MaxHP;
            state.Adventurers.Add(gatherer);
            var party = new Party();
            party.TryAdd(gatherer);
            var expedition = new DungeonExpeditionSystem(
                new ScoutingResolver(new AlwaysMinRng()), new DungeonResolver(new AlwaysMinRng()),
                new SatisfactionSystem(), new CompatibilitySystem(new AlwaysMinRng()));
            Assert.True(expedition.TryDispatchGathering(state, party, forest));

            var settlement = BuildWeekSystem(expedition).ProcessWeek(state);

            Assert.NotNull(settlement.DungeonMissionResolutions.Single().GatheringResult);
            Assert.Equal(50 + MasterMoodBalance.GatheringMoodGain, state.MasterMood);
            Assert.Equal(0, state.WeeksSinceLastGuildActivity);
            Assert.False(settlement.MoodReport.Bored);
        }

        [Fact]
        public void ProcessWeek_SideJobIncome_UsesMoodAfterThisWeeksChange()
        {
            // 決算時点（今週の機嫌変動の後）の機嫌で倍率が決まる：80（上機嫌）から退屈減衰で75（平常）へ落ちた週は×1.0。
            var state = new GameState { WeekNumber = EconomyBalance.SideJobIntervalWeeks, MasterMood = 80, Gold = 100_000 };

            var settlement = BuildWeekSystem().ProcessWeek(state);

            Assert.NotNull(settlement.SideJobIncome);
            Assert.Equal(MasterMoodTier.Normal, settlement.SideJobIncome!.Tier);
            Assert.Equal(EconomyBalance.SideJobBaseAmount, settlement.SideJobIncome.FinalGold);
        }
    }
}
