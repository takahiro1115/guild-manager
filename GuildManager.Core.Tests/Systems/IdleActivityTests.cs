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
    /// 待機中の過ごし方（→ IdleActivitySystem、03 §8.1・§0.73。旧・待機お手伝い）のテスト：
    /// 研究を手伝う（研究の手伝い＋機嫌、研究費の割引）・自主練（HPを使って少し伸びる）・静養の条件。
    /// 実行方法: `dotnet test GuildManager.Core.Tests --filter FullyQualifiedName~IdleActivity`
    /// </summary>
    public class IdleActivityTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        private static readonly IReadOnlySet<Guid> NoneDispatched = new HashSet<Guid>();

        private static Adventurer Healthy(string name, IdleActivity activity = IdleActivity.Help)
        {
            var a = new Adventurer
            {
                Name = name, JobClass = JobClass.Warrior, VIT = 30, IdleActivity = activity,
                PA_STR = 99, PA_AGI = 99, PA_VIT = 99, PA_MND = 99, PA_DEX = 99, PA_LDR = 99, PA_INT = 99,
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static GameState StateWith(params Adventurer[] adventurers)
        {
            var state = new GameState { Gold = 1000, MasterMood = 50 };
            state.Adventurers.AddRange(adventurers);
            return state;
        }

        private static WeekProcessingSystem Week(IRng rng) => new(
            new MasterMoodSystem(), new EconomySystem(), new TrainingSystem(), new InjuryRecoverySystem(),
            new RestRecoverySystem(), new GrowthSystem(rng), new SatisfactionSystem(),
            new AgingSystem(new AlwaysMinRng()), new FacilitySystem(), new DefeatSystem(),
            new RecruitmentSystem(new AlwaysMinRng()));

        [Fact]
        public void CsvValues_AreLoaded()
        {
            Assert.Equal(10, MasterMoodBalance.IdleAdventurerHelpResearchCredit);
            Assert.Equal(1, MasterMoodBalance.IdleAdventurerHelpMood);
            Assert.Equal(0.7, TrainingBalance.IdleActivityHpRatio, precision: 6);
            Assert.Equal(0.5, TrainingBalance.SelfTrainingGrowthMultiplier, precision: 6);
            Assert.Equal(5, TrainingBalance.SelfTrainingHpCost);
            Assert.Equal(600, TrainingBalance.ResearchCreditMax);
            Assert.Equal(60, TrainingBalance.ResearchCreditPerWeek);
            Assert.Equal(0.5, TrainingBalance.ResearchMaxShortenRate, precision: 6);
        }

        [Fact]
        public void NewAdventurer_HelpsByDefault()
        {
            var a = new Adventurer();
            Assert.Equal(IdleActivity.Help, a.IdleActivity);
            Assert.Null(a.SelfTrainingStat);
        }

        // ==================== 研究を手伝う ====================

        [Fact]
        public void Helper_AddsResearchCreditAndMood_NotGold()
        {
            var state = StateWith(Healthy("リナ"));
            var report = new MasterMoodReport { MoodBefore = 50 };

            var week = IdleActivitySystem.ProcessWeek(state, NoneDispatched, report);

            Assert.Equal(1000, state.Gold); // 旧・待機お手伝いの +15G は無い
            Assert.Equal(10, state.ResearchCredit);
            Assert.Equal(51, state.MasterMood);
            var entry = Assert.Single(week.HelpEntries);
            Assert.Equal("リナ", entry.Name);
            Assert.Equal(10, entry.Credit);
            Assert.Equal(1, entry.MoodApplied);
            Assert.Contains(report.Entries, e => e.Reason.StartsWith("研究の手伝い") && e.Applied == 1);
            Assert.Equal(51, report.MoodAfter);
            Assert.Empty(week.SelfTrainerIds);
        }

        [Fact]
        public void TwoHelpers_Accumulate()
        {
            var state = StateWith(Healthy("リナ"), Healthy("フィオナ"));

            var week = IdleActivitySystem.ProcessWeek(state, NoneDispatched, new MasterMoodReport());

            Assert.Equal(20, state.ResearchCredit);
            Assert.Equal(52, state.MasterMood);
            Assert.Equal(2, week.HelpEntries.Count);
        }

        [Fact]
        public void ResearchCredit_StopsAtMax()
        {
            var state = StateWith(Healthy("A"));
            state.ResearchCredit = TrainingBalance.ResearchCreditMax - 3;

            var week = IdleActivitySystem.ProcessWeek(state, NoneDispatched, new MasterMoodReport());

            Assert.Equal(TrainingBalance.ResearchCreditMax, state.ResearchCredit);
            Assert.Equal(3, Assert.Single(week.HelpEntries).Credit); // 実際に貯まった分だけ
        }

        [Fact]
        public void Mood_IsClampedAt100()
        {
            var state = StateWith(Healthy("A"), Healthy("B"));
            state.MasterMood = 99;
            var report = new MasterMoodReport();

            var week = IdleActivitySystem.ProcessWeek(state, NoneDispatched, report);

            Assert.Equal(100, state.MasterMood);
            Assert.Equal(20, state.ResearchCredit); // 研究の手伝いは機嫌の上限で止まらない
            Assert.Equal(new[] { 1, 0 }, week.HelpEntries.Select(e => e.MoodApplied));
            var moodEntry = Assert.Single(report.Entries);
            Assert.Equal(2, moodEntry.Delta);
            Assert.Equal(1, moodEntry.Applied);
        }

        // ==================== 条件（それ以外は静養） ====================

        [Fact]
        public void DispatchedAndTrainingAdventurers_AreExcluded()
        {
            var away = Healthy("出撃中");
            away.IsDispatched = true;
            var returned = Healthy("今週帰還"); // 解決で IsDispatched は下りたが、決算開始時点では出撃中だった
            var trainee = Healthy("訓練中", IdleActivity.SelfTraining);
            var state = StateWith(away, returned, trainee, Healthy("待機"));
            state.TrainingAssignments[trainee.Id] = FacilityType.DrillHall;

            var week = IdleActivitySystem.ProcessWeek(state, new HashSet<Guid> { away.Id, returned.Id }, new MasterMoodReport());

            Assert.Equal("待機", Assert.Single(week.HelpEntries).Name);
            Assert.Empty(week.SelfTrainerIds);
            Assert.Equal(WeekActivity.Dispatched, IdleActivitySystem.GetWeekActivity(state, away));
            Assert.Equal(WeekActivity.Training, IdleActivitySystem.GetWeekActivity(state, trainee));
        }

        [Fact]
        public void HpAtOrBelow70Percent_Rests_Above70Percent_Acts()
        {
            var low = Healthy("ちょうど7割");
            low.CurrentHP = (int)Math.Floor(low.MaxHP * 0.7);
            var high = Healthy("7割超", IdleActivity.SelfTraining);
            high.CurrentHP = (int)Math.Floor(high.MaxHP * 0.7) + 1;
            var state = StateWith(low, high);

            var week = IdleActivitySystem.ProcessWeek(state, NoneDispatched, new MasterMoodReport());

            Assert.Empty(week.HelpEntries);
            Assert.Equal(new[] { high.Id }, week.SelfTrainerIds);
            Assert.Equal(WeekActivity.Resting, IdleActivitySystem.GetWeekActivity(state, low));
            Assert.Equal("HP70%以下", IdleActivitySystem.RestReason(low));
        }

        [Theory]
        [InlineData(InjurySeverity.Light, 1)]
        [InlineData(InjurySeverity.Severe, 3)]
        [InlineData(InjurySeverity.None, 1)] // 負傷は治っても残り週数が残っていれば静養
        public void InjuredAdventurer_Rests(InjurySeverity severity, int weeksRemaining)
        {
            var injured = Healthy("負傷");
            injured.Injury = severity;
            injured.InjuryWeeksRemaining = weeksRemaining;
            var state = StateWith(injured);

            var week = IdleActivitySystem.ProcessWeek(state, NoneDispatched, new MasterMoodReport());

            Assert.Empty(week.HelpEntries);
            Assert.Equal(0, state.ResearchCredit);
            Assert.Equal("負傷", IdleActivitySystem.RestReason(injured));
        }

        [Fact]
        public void PoisonedAdventurer_Rests()
        {
            var poisoned = Healthy("毒", IdleActivity.SelfTraining);
            poisoned.PoisonWeeksRemaining = 2;
            poisoned.PoisonStatPenalty = 0.1;
            var state = StateWith(poisoned);

            var week = IdleActivitySystem.ProcessWeek(state, NoneDispatched, new MasterMoodReport());

            Assert.Empty(week.SelfTrainerIds);
            Assert.Equal("毒状態", IdleActivitySystem.RestReason(poisoned));
        }

        // ==================== 自主練 ====================

        [Fact]
        public void SelfTrainer_SpendsHp_GainsNoCreditOrMood()
        {
            var trainee = Healthy("自主練", IdleActivity.SelfTraining);
            int hp = trainee.CurrentHP;
            var state = StateWith(trainee);
            var report = new MasterMoodReport();

            var week = IdleActivitySystem.ProcessWeek(state, NoneDispatched, report);

            Assert.Equal(new[] { trainee.Id }, week.SelfTrainerIds);
            Assert.Equal(hp - TrainingBalance.SelfTrainingHpCost, trainee.CurrentHP);
            Assert.Equal(0, state.ResearchCredit);
            Assert.Equal(50, state.MasterMood);
            Assert.Empty(report.Entries);
        }

        [Fact]
        public void SelfTraining_GrowsTheChosenStat()
        {
            var trainee = Healthy("自主練", IdleActivity.SelfTraining);
            trainee.SelfTrainingStat = "LDR";
            trainee.LDR = 20;
            var state = StateWith(trainee);

            var events = new GrowthSystem(new AlwaysMinRng()).ProcessSelfTraining(state, new HashSet<Guid> { trainee.Id });

            var e = Assert.Single(events);
            Assert.Equal("LDR", e.Stat);
            Assert.Equal(20 + GrowthBalance.MinGrowthAmount, trainee.LDR);
        }

        [Fact]
        public void SelfTraining_WithoutChoice_FollowsJobGrowthWeights()
        {
            var trainee = Healthy("自主練", IdleActivity.SelfTraining);
            var state = StateWith(trainee);

            var events = new GrowthSystem(new AlwaysMinRng()).ProcessSelfTraining(state, new HashSet<Guid> { trainee.Id });

            Assert.True(GrowthBalance.GetJobStatWeight(JobClass.Warrior, Assert.Single(events).Stat) > 0);
        }

        [Fact]
        public void SelfTraining_RollsAtHalfTheBaseChance()
        {
            // 乱数が最大（100）なら、どの年齢帯でも基礎確率×0.5 は100%に届かず伸びない
            var trainee = Healthy("自主練", IdleActivity.SelfTraining);
            var state = StateWith(trainee);

            var events = new GrowthSystem(new AlwaysMaxRng()).ProcessSelfTraining(state, new HashSet<Guid> { trainee.Id });

            Assert.Empty(events);
        }

        [Fact]
        public void ProcessWeek_BothRecoverHp_SelfTrainerRecoversLessByTheCost()
        {
            var helper = Healthy("手伝い");
            helper.CurrentHP = (int)(helper.MaxHP * 0.8);
            var trainee = Healthy("自主練", IdleActivity.SelfTraining);
            trainee.CurrentHP = (int)(trainee.MaxHP * 0.8);
            int helperHp = helper.CurrentHP;
            var state = StateWith(helper, trainee);

            var result = Week(new AlwaysMinRng()).ProcessWeek(state);

            // どちらも静養の回復は受ける（自主練は HP 条件の少し上に張り付かないよう、回復が消費の分だけ遅くなるだけ）
            Assert.True(helper.CurrentHP > helperHp);
            Assert.Equal(helper.CurrentHP - TrainingBalance.SelfTrainingHpCost, trainee.CurrentHP);
            Assert.Equal("手伝い", Assert.Single(result.IdleHelpEntries).Name);
            Assert.Equal(1, result.SelfTrainerCount);
            Assert.NotEmpty(result.SelfTrainingGrowthEvents);
            Assert.Contains(result.MoodReport.Entries, e => e.Reason.StartsWith("研究の手伝い"));
        }

        [Fact]
        public void ProcessWeek_NoEntries_WhenNobodyQualifies()
        {
            var hurt = Healthy("負傷");
            hurt.CurrentHP = 1;
            var state = StateWith(hurt);

            var result = Week(new AlwaysMinRng()).ProcessWeek(state);

            Assert.Empty(result.IdleHelpEntries);
            Assert.Equal(0, result.SelfTrainerCount);
            Assert.DoesNotContain(result.MoodReport.Entries, e => e.Reason.StartsWith("研究の手伝い"));
        }

        // ==================== 研究の期間と研究の手伝い（§0.114） ====================

        /// <summary>研究を始められる状態（研究費と素材を持ち、前提は済ませる）。</summary>
        private static GameState ReadyFor(ResearchDefinition research, int credit = 0)
        {
            var state = new GameState { Gold = research.RequiredGold, ResearchCredit = credit };
            foreach (var (materialId, count) in research.RequiredMaterials)
                state.AddMaterial(materialId, count);
            if (research.PrerequisiteId != null)
                state.CompletedResearchIds.Add(research.PrerequisiteId);
            return state;
        }

        [Fact]
        public void StartResearch_PaysFullCost_AndCompletesAfterItsWeeks()
        {
            var research = ResearchBalance.Find(ResearchIds.HerbPoultice)!;
            var state = ReadyFor(research);

            Assert.True(ResearchSystem.StartResearch(state, research));
            Assert.Equal(0, state.Gold); // 割り引きは無い
            Assert.True(ResearchSystem.IsInProgress(state, research.Id));
            Assert.False(state.IsResearchCompleted(research.Id));

            for (int w = 1; w < research.Weeks; w++)
                Assert.Null(ResearchSystem.ProcessWeek(state));
            Assert.Same(research, ResearchSystem.ProcessWeek(state));
            Assert.True(state.IsResearchCompleted(research.Id));
            Assert.Null(state.ResearchInProgress);
            Assert.Null(ResearchSystem.ProcessWeek(state)); // 研究していない週は何もしない
        }

        [Fact]
        public void Credit_AdvancesOneExtraWeek_UpToHalfTheWeeks()
        {
            var research = ResearchBalance.Find(ResearchIds.CaveOintment)!; // 8週 → 縮められるのは4週まで
            int max = ResearchSystem.MaxShortenWeeks(research);
            Assert.Equal(research.Weeks / 2, max);
            var state = ReadyFor(research, credit: 10_000);
            Assert.True(ResearchSystem.StartResearch(state, research));

            int weeks = 0;
            while (ResearchSystem.ProcessWeek(state) == null) weeks++;
            weeks++;
            Assert.Equal(research.Weeks - max, weeks);
            Assert.Equal(10_000 - max * TrainingBalance.ResearchCreditPerWeek, state.ResearchCredit);
        }

        [Fact]
        public void Credit_BelowOneWeek_IsKept()
        {
            var research = ResearchBalance.Find(ResearchIds.HerbPoultice)!;
            var state = ReadyFor(research, credit: TrainingBalance.ResearchCreditPerWeek - 1);
            Assert.True(ResearchSystem.StartResearch(state, research));
            Assert.Null(ResearchSystem.ProcessWeek(state));
            Assert.Equal(research.Weeks - 1, state.ResearchInProgress!.WeeksRemaining);
            Assert.Equal(TrainingBalance.ResearchCreditPerWeek - 1, state.ResearchCredit);
        }

        [Fact]
        public void OnlyOneResearch_AtATime_CancelDoesNotRefund()
        {
            var herb = ResearchBalance.Find(ResearchIds.HerbPoultice)!;
            var bag = ResearchBalance.Find(ResearchIds.GatheringBag)!;
            var state = ReadyFor(herb);
            state.Gold += bag.RequiredGold;
            foreach (var (materialId, count) in bag.RequiredMaterials)
                state.AddMaterial(materialId, count);

            Assert.True(ResearchSystem.StartResearch(state, herb));
            Assert.False(ResearchSystem.CanStartResearch(state, bag));
            Assert.False(ResearchSystem.StartResearch(state, bag));

            int gold = state.Gold;
            Assert.True(ResearchSystem.CancelResearch(state));
            Assert.Null(state.ResearchInProgress);
            Assert.Equal(gold, state.Gold);
            Assert.False(state.IsResearchCompleted(herb.Id));
            Assert.True(ResearchSystem.StartResearch(state, bag));
            Assert.False(ResearchSystem.CancelResearch(new GameState()));
        }

        // ==================== セーブ ====================

        [Fact]
        public void Save_RoundTrips_IdleActivity_SelfTrainingStat_AndResearchCredit()
        {
            var a = Healthy("リナ", IdleActivity.SelfTraining);
            a.SelfTrainingStat = "MND";
            var state = StateWith(a);
            state.ResearchCredit = 123;

            var json = System.Text.Json.JsonSerializer.Serialize(state.ToSaveData());
            var restored = GameState.FromSaveData(System.Text.Json.JsonSerializer.Deserialize<SaveData>(json)!);

            Assert.Equal(123, restored.ResearchCredit);
            var r = Assert.Single(restored.Adventurers);
            Assert.Equal(IdleActivity.SelfTraining, r.IdleActivity);
            Assert.Equal("MND", r.SelfTrainingStat);
        }

        [Fact]
        public void OldSave_WithoutKeys_LoadsDefaults_AndBrokenValuesAreReset()
        {
            var state = StateWith(Healthy("リナ"));
            var node = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(state.ToSaveData()))!;
            node.AsObject().Remove("ResearchCredit");
            var adv = node["ActiveAdventurers"]![0]!.AsObject();
            adv.Remove("IdleActivity");
            adv["SelfTrainingStat"] = "XYZ";

            var restored = GameState.FromSaveData(System.Text.Json.JsonSerializer.Deserialize<SaveData>(node.ToJsonString())!);

            Assert.Equal(0, restored.ResearchCredit);
            Assert.Equal(IdleActivity.Help, restored.Adventurers[0].IdleActivity);
            Assert.Null(restored.Adventurers[0].SelfTrainingStat);

            adv["IdleActivity"] = 7;
            node["ResearchCredit"] = -50;
            restored = GameState.FromSaveData(System.Text.Json.JsonSerializer.Deserialize<SaveData>(node.ToJsonString())!);
            Assert.Equal(0, restored.ResearchCredit);
            Assert.Equal(IdleActivity.Help, restored.Adventurers[0].IdleActivity);
        }

        [Fact]
        public void Save_RoundTrips_ResearchInProgress_OldSaveHasNone_UnknownOrDoneIsDropped()
        {
            var research = ResearchBalance.Find(ResearchIds.HerbPoultice)!;
            var state = ReadyFor(research, credit: 100);
            Assert.True(ResearchSystem.StartResearch(state, research));
            ResearchSystem.ProcessWeek(state); // 1週進めて手伝いで1週縮める

            var node = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(state.ToSaveData()))!;
            var restored = GameState.FromSaveData(System.Text.Json.JsonSerializer.Deserialize<SaveData>(node.ToJsonString())!);
            Assert.Equal(research.Id, restored.ResearchInProgress!.ResearchId);
            Assert.Equal(state.ResearchInProgress!.WeeksRemaining, restored.ResearchInProgress.WeeksRemaining);
            Assert.Equal(1, restored.ResearchInProgress.WeeksShortened);

            node.AsObject().Remove("ResearchInProgress"); // §0.114 より前のセーブ
            Assert.Null(GameState.FromSaveData(System.Text.Json.JsonSerializer.Deserialize<SaveData>(node.ToJsonString())!).ResearchInProgress);

            node["ResearchInProgress"] = new System.Text.Json.Nodes.JsonObject { ["ResearchId"] = "res_gone", ["WeeksRemaining"] = 3 };
            Assert.Null(GameState.FromSaveData(System.Text.Json.JsonSerializer.Deserialize<SaveData>(node.ToJsonString())!).ResearchInProgress);

            node["ResearchInProgress"] = new System.Text.Json.Nodes.JsonObject { ["ResearchId"] = research.Id, ["WeeksRemaining"] = 0 };
            Assert.Equal(1, GameState.FromSaveData(System.Text.Json.JsonSerializer.Deserialize<SaveData>(node.ToJsonString())!).ResearchInProgress!.WeeksRemaining); // 壊れた残り週は1に
        }

        // ==================== 月報 ====================

        [Fact]
        public void MonthlyReport_ShowsResearchCreditAndSelfTrainingGrowth()
        {
            var helper = Healthy("手伝い");
            var trainee = Healthy("自主練", IdleActivity.SelfTraining);
            trainee.SelfTrainingStat = "DEX";
            var state = StateWith(helper, trainee);
            var system = Week(new AlwaysMinRng());

            var weeks = new List<AutoSkipWeek> { new(new List<SquadOrderEvent>(), system.ProcessWeek(state)) };
            var report = MonthlyReport.Build(state, weeks, 1000);

            Assert.Equal(10, report.ResearchCreditGained);
            Assert.Equal(10, report.ResearchCreditAfter);
            var growth = Assert.Single(report.SelfTrainingGrowth);
            Assert.Equal("自主練", growth.Name);
            Assert.True(growth.Gains["DEX"] > 0);
            Assert.Contains(report.Growth, g => g.Name == "自主練"); // 成長の一覧にも含まれる
        }

        /// <summary>§0.120：森の20Fを倒していて現役がそろった週の決算で3つ目の部隊が開き、月報の出来事に出る。</summary>
        [Fact]
        public void WeeklySettlement_OpensTheThirdSquad_WhenEnoughActive()
        {
            var adventurers = Enumerable.Range(0, ProgressionBalance.Slot3MinActive).Select(i => Healthy($"子{i}")).ToArray();
            var state = StateWith(adventurers);
            state.Gold = 100_000;
            state.UnlockedSquadSlots = 2;
            state.DungeonFields = GuildManager.Core.Data.SampleData.CreateDefaultFields();
            state.DungeonFields.First(f => f.Order == 1).Bosses.Single(b => b.Floor == 20).IsDefeated = true;

            var week = new AutoSkipWeek(new List<SquadOrderEvent>(), Week(new AlwaysMinRng()).ProcessWeek(state));
            Assert.Equal(3, week.Settlement.SquadSlotsExpandedTo);
            Assert.Equal(3, state.UnlockedSquadSlots);
            Assert.Contains(MonthlyReport.Build(state, new List<AutoSkipWeek> { week }, 100_000).Highlights, l => l.Text.Contains("部隊が3つになった"));
        }

        /// <summary>§0.114：週の決算で研究が進み、その週の研究の手伝いも使って縮み、完了は月報の出来事に出る。</summary>
        [Fact]
        public void WeeklySettlement_AdvancesResearch_WithThisWeeksHelp_AndReportsCompletion()
        {
            var research = ResearchBalance.Find(ResearchIds.HerbPoultice)!; // 4週
            var helpers = Enumerable.Range(0, 6).Select(i => Healthy($"手伝い{i}")).ToArray(); // 週に60貯まる
            var state = StateWith(helpers);
            state.Gold = research.RequiredGold + 100_000;
            foreach (var (materialId, count) in research.RequiredMaterials)
                state.AddMaterial(materialId, count);
            Assert.True(ResearchSystem.StartResearch(state, research));
            var system = Week(new AlwaysMinRng());

            var weeks = new List<AutoSkipWeek>();
            for (int w = 0; w < 2; w++)
                weeks.Add(new AutoSkipWeek(new List<SquadOrderEvent>(), system.ProcessWeek(state)));

            Assert.Null(weeks[0].Settlement.CompletedResearch);
            Assert.Same(research, weeks[1].Settlement.CompletedResearch); // 4週の研究が手伝いで2週に
            Assert.True(weeks[1].Settlement.Flags.ResearchCompleted);
            Assert.True(state.IsResearchCompleted(research.Id));
            var report = MonthlyReport.Build(state, weeks, 100_000);
            Assert.Contains(report.Highlights, l => l.Text == $"研究「{research.Name}」が完了した");
        }
    }
}
