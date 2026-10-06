using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 教官深化 Step 3（→ 03 §7.1）のテスト：施設ごとの重点伝授特性の選択と、教官の在任週数による伝授確率の上乗せ、
    /// 任命の交代・解除で記録が消えること、セーブの往復と旧セーブの読み込み。
    /// 実行方法: `dotnet test GuildManager.Core.Tests --filter FullyQualifiedName~TrainerFocusAndTenure`
    /// </summary>
    public class TrainerFocusAndTenureTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private class FixedRng : IRng
        {
            private readonly int _value;
            public FixedRng(int value) => _value = value;
            public int NextInt(int min, int max) => Math.Clamp(_value, min, max);
        }

        private const FacilityType Hall = FacilityType.DrillHall;

        /// <summary>戦士訓練所（Lv1）に教官（trainerTraits を持つ）と生徒1名を配置した状態。</summary>
        private static (GameState State, Adventurer Trainer, Adventurer Student) Setup(params string[] trainerTraits)
        {
            var state = new GameState();
            foreach (var f in state.Facilities)
                if (f.Type == Hall || f.Type == FacilityType.Academy) f.CurrentLevel = 1;

            var trainer = new Adventurer { Name = "クラウディア", IsRetired = true, Age = 26 };
            foreach (var id in trainerTraits) Assert.True(trainer.TryAddTrait(id));
            state.RetiredAdventurers.Add(trainer);
            Assert.True(new AdvisorSystem().TryAssignTrainer(state, Hall, trainer.Id));

            var student = new Adventurer { Name = "リナ", Age = 20 };
            state.Adventurers.Add(student);
            state.TrainingAssignments[student.Id] = Hall;
            return (state, trainer, student);
        }

        // ==================== 在任ボーナス ====================

        [Fact]
        public void CsvValues_AreLoaded()
        {
            Assert.Equal(12, TrainingBalance.TrainerTenureBonusStepWeeks);
            Assert.Equal(0.025, TrainingBalance.TrainerTenureBonusPerStep, precision: 6);
            Assert.Equal(0.10, TrainingBalance.TrainerTenureBonusMax, precision: 6);
        }

        [Theory]
        [InlineData(0, 0.0)]
        [InlineData(11, 0.0)]
        [InlineData(12, 0.025)]
        [InlineData(23, 0.025)]
        [InlineData(24, 0.05)]
        [InlineData(36, 0.075)]
        [InlineData(48, 0.10)]
        [InlineData(500, 0.10)] // 上限で頭打ち
        [InlineData(-3, 0.0)]
        public void TenureBonus_RisesEverySeason_UpToTenPercent(int weeks, double expected)
        {
            Assert.Equal(expected, TrainingSystem.GetTenureBonus(weeks), precision: 6);
        }

        [Fact]
        public void InheritanceChance_AddsTenureOnTopOfMentor()
        {
            var (_, trainer, _) = Setup(TraitCatalog.MentorId);
            Assert.Equal(0.20, TrainingSystem.GetInheritanceChance(trainer, 0), precision: 6);
            Assert.Equal(0.30, TrainingSystem.GetInheritanceChance(trainer, 48), precision: 6);
        }

        [Theory]
        [InlineData(10, true)]  // 在任24週：基礎5%＋在任5%＝10%：閾値10
        [InlineData(11, false)]
        public void WeeklyRoll_UsesTenureBonus(int roll, bool expectInherit)
        {
            var (state, _, student) = Setup(TraitCatalog.BraveId);
            state.TrainerTenureWeeks[Hall] = 24;

            var events = new TrainingSystem(new FixedRng(roll)).ProcessWeeklyTraitTransmission(state);

            Assert.Equal(expectInherit ? 1 : 0, events.Count);
            Assert.Equal(expectInherit, student.HasTrait(TraitCatalog.BraveId));
        }

        [Fact]
        public void Tenure_CountsUpEachWeek_OnlyForAssignedTrainers()
        {
            var (state, _, _) = Setup(TraitCatalog.BraveId);
            var system = new TrainingSystem();

            system.ProcessWeeklyTrainerTenure(state);
            system.ProcessWeeklyTrainerTenure(state);

            Assert.Equal(2, TrainingSystem.GetTrainerTenureWeeks(state, Hall));
            Assert.Equal(0, TrainingSystem.GetTrainerTenureWeeks(state, FacilityType.Academy));
            Assert.False(state.TrainerTenureWeeks.ContainsKey(FacilityType.Academy));
        }

        [Fact]
        public void Tenure_CountsEvenWithoutStudents()
        {
            var (state, _, student) = Setup(TraitCatalog.BraveId);
            state.TrainingAssignments.Remove(student.Id);

            new TrainingSystem().ProcessWeeklyTrainerTenure(state);

            Assert.Equal(1, TrainingSystem.GetTrainerTenureWeeks(state, Hall));
        }

        private static WeekProcessingSystem BuildWeek()
        {
            var satisfaction = new SatisfactionSystem();
            var compatibility = new CompatibilitySystem(new AlwaysMinRng());
            var expedition = new DungeonExpeditionSystem(
                new ScoutingResolver(new AlwaysMinRng()),
                new DungeonResolver(new AlwaysMinRng()),
                satisfaction,
                compatibility,
                new DungeonTraversalResolver(new AlwaysMinRng()),
                new GatheringResolver(new AlwaysMinRng()));

            return new WeekProcessingSystem(
                masterMoodSystem: new MasterMoodSystem(),
                economySystem: new EconomySystem(),
                trainingSystem: new TrainingSystem(),
                injuryRecoverySystem: new InjuryRecoverySystem(),
                restRecoverySystem: new RestRecoverySystem(),
                growthSystem: new GrowthSystem(new AlwaysMinRng()),
                satisfactionSystem: satisfaction,
                agingSystem: new AgingSystem(new AlwaysMinRng()),
                facilitySystem: new FacilitySystem(),
                defeatSystem: new DefeatSystem(),
                recruitmentSystem: new RecruitmentSystem(new AlwaysMinRng()),
                dungeonExpeditionSystem: expedition);
        }

        [Fact]
        public void WeekProcessing_CountsTenure_AfterTheRoll()
        {
            // 任命した週の決算のロールは在任0週で行い、その後に1週を数える。
            var (state, _, _) = Setup(TraitCatalog.BraveId);

            BuildWeek().ProcessWeek(state);

            Assert.Equal(1, TrainingSystem.GetTrainerTenureWeeks(state, Hall));
        }

        // ==================== 任命の交代・解除 ====================

        [Fact]
        public void Reassigning_SamePersonToSameFacility_KeepsRecords()
        {
            var (state, trainer, _) = Setup(TraitCatalog.BraveId, TraitCatalog.AttentiveId);
            state.TrainerTenureWeeks[Hall] = 30;
            Assert.True(TrainingSystem.SetTrainerFocusTrait(state, Hall, TraitCatalog.AttentiveId));

            Assert.True(new AdvisorSystem().TryAssignTrainer(state, Hall, trainer.Id));

            Assert.Equal(30, TrainingSystem.GetTrainerTenureWeeks(state, Hall));
            Assert.Equal(TraitCatalog.AttentiveId, TrainingSystem.GetTrainerFocusTrait(state, Hall));
        }

        [Fact]
        public void Replacing_Trainer_ResetsRecords()
        {
            var (state, _, _) = Setup(TraitCatalog.BraveId);
            state.TrainerTenureWeeks[Hall] = 30;
            Assert.True(TrainingSystem.SetTrainerFocusTrait(state, Hall, TraitCatalog.BraveId));

            var successor = new Adventurer { Name = "後任", IsRetired = true };
            successor.TryAddTrait(TraitCatalog.BraveId);
            state.RetiredAdventurers.Add(successor);
            Assert.True(new AdvisorSystem().TryAssignTrainer(state, Hall, successor.Id));

            Assert.Equal(0, TrainingSystem.GetTrainerTenureWeeks(state, Hall));
            Assert.Null(TrainingSystem.GetTrainerFocusTrait(state, Hall));
        }

        [Fact]
        public void Unassigning_Trainer_ResetsRecords()
        {
            var (state, _, _) = Setup(TraitCatalog.BraveId);
            state.TrainerTenureWeeks[Hall] = 30;
            Assert.True(TrainingSystem.SetTrainerFocusTrait(state, Hall, TraitCatalog.BraveId));

            new AdvisorSystem().UnassignTrainer(state, Hall);

            Assert.Empty(state.TrainerTenureWeeks);
            Assert.Empty(state.TrainerFocusTraits);
        }

        [Fact]
        public void MovingTrainer_ToAnotherFacility_StartsOver()
        {
            var (state, trainer, _) = Setup(TraitCatalog.BraveId);
            state.TrainerTenureWeeks[Hall] = 30;
            Assert.True(TrainingSystem.SetTrainerFocusTrait(state, Hall, TraitCatalog.BraveId));

            Assert.True(new AdvisorSystem().TryAssignTrainer(state, FacilityType.Academy, trainer.Id));

            Assert.Empty(state.TrainerTenureWeeks);
            Assert.Empty(state.TrainerFocusTraits);
            Assert.Equal(0, TrainingSystem.GetTrainerTenureWeeks(state, FacilityType.Academy));
        }

        // ==================== 重点伝授特性 ====================

        [Fact]
        public void FocusTrait_IsTaughtFirst()
        {
            var (state, _, student) = Setup(TraitCatalog.BraveId, TraitCatalog.AttentiveId, TraitCatalog.NightVisionId);
            Assert.True(TrainingSystem.SetTrainerFocusTrait(state, Hall, TraitCatalog.NightVisionId));

            var e = Assert.Single(new TrainingSystem(new AlwaysMinRng()).ProcessWeeklyTraitTransmission(state));

            Assert.Equal(TraitCatalog.NightVisionId, e.TraitId);
            Assert.False(student.HasTrait(TraitCatalog.BraveId));
        }

        [Fact]
        public void FocusTrait_AlreadyOwned_FallsBackToSlotOrder()
        {
            var (state, _, student) = Setup(TraitCatalog.BraveId, TraitCatalog.AttentiveId, TraitCatalog.NightVisionId);
            Assert.True(TrainingSystem.SetTrainerFocusTrait(state, Hall, TraitCatalog.NightVisionId));
            student.TryAddTrait(TraitCatalog.NightVisionId);

            var e = Assert.Single(new TrainingSystem(new AlwaysMinRng()).ProcessWeeklyTraitTransmission(state));

            Assert.Equal(TraitCatalog.BraveId, e.TraitId);
        }

        [Fact]
        public void SetFocusTrait_RejectsTraitsTheTrainerCannotTeach()
        {
            var (state, _, _) = Setup(TraitCatalog.BraveId, TraitCatalog.OldWoundId, TraitCatalog.BeautifulId);

            Assert.False(TrainingSystem.SetTrainerFocusTrait(state, Hall, TraitCatalog.AttentiveId)); // 教官が持っていない
            Assert.False(TrainingSystem.SetTrainerFocusTrait(state, Hall, TraitCatalog.OldWoundId));  // 障害特性
            Assert.False(TrainingSystem.SetTrainerFocusTrait(state, Hall, TraitCatalog.BeautifulId)); // 伝授対象外
            Assert.Empty(state.TrainerFocusTraits);
        }

        [Fact]
        public void SetFocusTrait_FailsWithoutTrainer()
        {
            var state = new GameState();
            Assert.False(TrainingSystem.SetTrainerFocusTrait(state, Hall, TraitCatalog.BraveId));
            Assert.False(TrainingSystem.SetTrainerFocusTrait(state, Hall, null));
        }

        [Fact]
        public void SetFocusTrait_Null_ReturnsToAuto()
        {
            var (state, _, _) = Setup(TraitCatalog.BraveId);
            Assert.True(TrainingSystem.SetTrainerFocusTrait(state, Hall, TraitCatalog.BraveId));

            Assert.True(TrainingSystem.SetTrainerFocusTrait(state, Hall, null));

            Assert.Null(TrainingSystem.GetTrainerFocusTrait(state, Hall));
        }

        [Fact]
        public void StaleFocusTrait_IsIgnored()
        {
            // 記録だけ残っていて教官が伝授できない特性（セーブの書き換え等）は自動扱い。
            var (state, _, _) = Setup(TraitCatalog.BraveId);
            state.TrainerFocusTraits[Hall] = TraitCatalog.AttentiveId;

            Assert.Null(TrainingSystem.GetTrainerFocusTrait(state, Hall));
            var e = Assert.Single(new TrainingSystem(new AlwaysMinRng()).ProcessWeeklyTraitTransmission(state));
            Assert.Equal(TraitCatalog.BraveId, e.TraitId);
        }

        // ==================== セーブ ====================

        private static GameState RoundTrip(GameState state) =>
            GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(state.ToSaveData()))!);

        [Fact]
        public void Save_RoundTrips_TenureAndFocus()
        {
            var (state, _, _) = Setup(TraitCatalog.BraveId, TraitCatalog.AttentiveId);
            state.TrainerTenureWeeks[Hall] = 17;
            Assert.True(TrainingSystem.SetTrainerFocusTrait(state, Hall, TraitCatalog.AttentiveId));

            var restored = RoundTrip(state);

            Assert.Equal(17, TrainingSystem.GetTrainerTenureWeeks(restored, Hall));
            Assert.Equal(TraitCatalog.AttentiveId, TrainingSystem.GetTrainerFocusTrait(restored, Hall));
        }

        [Fact]
        public void OldSave_WithoutRecords_LoadsAsZeroTenureAndAuto()
        {
            var (state, _, _) = Setup(TraitCatalog.BraveId);
            state.TrainerTenureWeeks[Hall] = 5;
            // 旧セーブの JSON には項目自体が無い。
            var node = JsonNode.Parse(JsonSerializer.Serialize(state.ToSaveData()))!.AsObject();
            Assert.True(node.Remove("TrainerTenureWeeks"));
            Assert.True(node.Remove("TrainerFocusTraits"));
            var json = node.ToJsonString();
            Assert.DoesNotContain("TrainerTenureWeeks", json);

            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!);

            Assert.Equal(0, TrainingSystem.GetTrainerTenureWeeks(restored, Hall));
            Assert.Null(TrainingSystem.GetTrainerFocusTrait(restored, Hall));
            Assert.Empty(restored.TrainerTenureWeeks);
            Assert.Empty(restored.TrainerFocusTraits);
        }

        [Fact]
        public void Load_DropsRecords_ForFacilitiesWithoutTrainer()
        {
            var (state, _, _) = Setup(TraitCatalog.BraveId);
            var data = state.ToSaveData();
            data.TrainerTenureWeeks[FacilityType.Academy.ToString()] = 9;
            data.TrainerFocusTraits[FacilityType.Academy.ToString()] = TraitCatalog.BraveId;

            var restored = GameState.FromSaveData(data);

            Assert.False(restored.TrainerTenureWeeks.ContainsKey(FacilityType.Academy));
            Assert.False(restored.TrainerFocusTraits.ContainsKey(FacilityType.Academy));
        }
    }
}
