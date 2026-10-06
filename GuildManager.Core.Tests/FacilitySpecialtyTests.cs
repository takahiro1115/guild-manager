using System.Linq;
using System.Text.Json;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 施設の専門（§0.76、→ 03 §6.2）：Lv3→4の着工で専門を選ぶ・改装・専門ごとの効果・セーブのテスト。
    /// 数値は docs/04_バランス表/facility.csv の値（仮の値を変えたらここも直す）。
    /// </summary>
    public class FacilitySpecialtyTests
    {
        private static GameState StateWith(FacilityType type, int level, FacilitySpecialty specialty = FacilitySpecialty.None, int gold = 100000)
        {
            var state = FacilitySystemTests.DefeatBosses(new GameState { Gold = gold }, 30); // 上限Lv（§0.79）に止められないように
            var facility = state.Facilities.First(f => f.Type == type);
            facility.CurrentLevel = level;
            facility.Specialty = specialty;
            return state;
        }

        private static void Complete(GameState state, FacilitySystem system)
        {
            while (state.UnderConstruction != null)
                system.ProcessWeeklyConstruction(state);
        }

        // ---------------- 着工で専門を選ぶ ----------------

        [Fact]
        public void UpgradeFromLevel3_Fails_WithoutSpecialty()
        {
            var state = StateWith(FacilityType.Infirmary, 3);

            Assert.False(new FacilitySystem().TryStartConstruction(state, FacilityType.Infirmary));
            Assert.Null(state.UnderConstruction);
            Assert.Equal(100000, state.Gold);
        }

        [Fact]
        public void UpgradeFromLevel3_Fails_WithSpecialtyOfAnotherFacility()
        {
            var state = StateWith(FacilityType.Infirmary, 3);

            Assert.False(new FacilitySystem().TryStartConstruction(state, FacilityType.Infirmary, FacilitySpecialty.Elite));
        }

        [Fact]
        public void UpgradeFromLevel3_WithSpecialty_SetsSpecialtyOnCompletion()
        {
            var state = StateWith(FacilityType.DrillHall, 3);
            var system = new FacilitySystem();

            Assert.True(system.TryStartConstruction(state, FacilityType.DrillHall, FacilitySpecialty.Rivalry));
            Assert.Equal(FacilitySpecialty.None, state.GetFacilitySpecialty(FacilityType.DrillHall)); // 工事中はLv3のまま
            Complete(state, system);

            Assert.Equal(4, state.GetFacilityLevel(FacilityType.DrillHall));
            Assert.Equal(FacilitySpecialty.Rivalry, state.GetFacilitySpecialty(FacilityType.DrillHall));
        }

        [Fact]
        public void UpgradeFromLevel4_KeepsSpecialty()
        {
            var state = StateWith(FacilityType.Tavern, 4, FacilitySpecialty.Trade);
            var system = new FacilitySystem();

            Assert.True(system.TryStartConstruction(state, FacilityType.Tavern));
            Complete(state, system);

            Assert.Equal(5, state.GetFacilityLevel(FacilityType.Tavern));
            Assert.Equal(FacilitySpecialty.Trade, state.GetFacilitySpecialty(FacilityType.Tavern));
        }

        [Fact]
        public void Dormitory_HasNoSpecialty_AndUpgradesFromLevel3Freely()
        {
            var state = StateWith(FacilityType.Dormitory, 3);
            var system = new FacilitySystem();

            Assert.False(FacilityBalance.HasSpecialty(FacilityType.Dormitory));
            Assert.True(system.TryStartConstruction(state, FacilityType.Dormitory));
            Complete(state, system);
            Assert.Equal(FacilitySpecialty.None, state.GetFacilitySpecialty(FacilityType.Dormitory));
        }

        [Fact]
        public void GetFacilitySpecialty_FallsBackToFirstOption_AboveLevel3WithoutRecord()
        {
            var state = StateWith(FacilityType.WarRoom, 5);

            Assert.Equal(FacilitySpecialty.Analysis, state.GetFacilitySpecialty(FacilityType.WarRoom));
        }

        [Fact]
        public void GetFacilitySpecialty_IsNone_AtLevel3EvenWithRecord()
        {
            var state = StateWith(FacilityType.WarRoom, 3, FacilitySpecialty.Pathfinding);

            Assert.Equal(FacilitySpecialty.None, state.GetFacilitySpecialty(FacilityType.WarRoom));
        }

        // ---------------- 改装 ----------------

        [Fact]
        public void Remodel_ChangesSpecialty_KeepsLevel_CostsLevelTimes1000_TakesTwoWeeks()
        {
            var state = StateWith(FacilityType.Infirmary, 5, FacilitySpecialty.Sanatorium, gold: 6000);
            var system = new FacilitySystem();

            Assert.True(system.TryStartRemodel(state, FacilityType.Infirmary, FacilitySpecialty.FieldAid));
            Assert.Equal(1000, state.Gold); // 5×1000
            Assert.Equal(2, state.UnderConstruction!.WeeksRemaining);
            Assert.True(state.UnderConstruction.IsRemodel);

            system.ProcessWeeklyConstruction(state);
            Assert.Equal(FacilitySpecialty.Sanatorium, state.GetFacilitySpecialty(FacilityType.Infirmary)); // 改装中は今の専門のまま
            system.ProcessWeeklyConstruction(state);

            Assert.Null(state.UnderConstruction);
            Assert.Equal(5, state.GetFacilityLevel(FacilityType.Infirmary));
            Assert.Equal(FacilitySpecialty.FieldAid, state.GetFacilitySpecialty(FacilityType.Infirmary));
        }

        [Fact]
        public void Remodel_Fails_ForSameSpecialty_BelowLevel4_OrWhileBusy()
        {
            var system = new FacilitySystem();

            Assert.False(system.TryStartRemodel(StateWith(FacilityType.Tavern, 4, FacilitySpecialty.Leisure), FacilityType.Tavern, FacilitySpecialty.Leisure));
            Assert.False(system.TryStartRemodel(StateWith(FacilityType.Tavern, 3), FacilityType.Tavern, FacilitySpecialty.Trade));
            Assert.False(system.TryStartRemodel(StateWith(FacilityType.Dormitory, 5), FacilityType.Dormitory, FacilitySpecialty.Trade));

            var busy = StateWith(FacilityType.Tavern, 4, FacilitySpecialty.Leisure);
            busy.UnderConstruction = new FacilityConstruction { Type = FacilityType.Dormitory, TargetLevel = 2, WeeksRemaining = 1 };
            Assert.False(system.TryStartRemodel(busy, FacilityType.Tavern, FacilitySpecialty.Trade));
        }

        [Fact]
        public void Remodel_Fails_WithoutEnoughGold()
        {
            var state = StateWith(FacilityType.Tavern, 4, FacilitySpecialty.Leisure, gold: 3999);

            Assert.False(new FacilitySystem().TryStartRemodel(state, FacilityType.Tavern, FacilitySpecialty.Trade));
            Assert.Equal(3999, state.Gold);
        }

        // ---------------- 専門ごとの効果 ----------------

        [Theory]
        [InlineData(4, 1, 1.45)]
        [InlineData(5, 1, 1.6)]
        public void Elite_KeepsOneSlot_AndRaisesGrowth(int level, int slots, double multiplier)
        {
            var state = StateWith(FacilityType.Academy, level, FacilitySpecialty.Elite);

            Assert.Equal(slots, new TrainingSystem().GetSlotCapacity(state, FacilityType.Academy));
            Assert.Equal(multiplier, FacilityBalance.GetTrainingGrowthMultiplier(level, FacilitySpecialty.Elite), precision: 6);
        }

        [Theory]
        [InlineData(4, 1.3 * 0.7)]
        [InlineData(5, 1.3 * 0.9)]
        public void Rivalry_HasTwoSlots_AndLowerGrowthThanLevel3(int level, double multiplier)
        {
            var state = StateWith(FacilityType.SkillHall, level, FacilitySpecialty.Rivalry);

            Assert.Equal(2, new TrainingSystem().GetSlotCapacity(state, FacilityType.SkillHall));
            Assert.Equal(multiplier * GrowthBalance.TrainingFacilityMultiplier,
                TrainingSystem.GetFacilityGrowthMultiplier(state, FacilityType.SkillHall), precision: 6);
        }

        [Fact]
        public void Rivalry_AllowsTwoTrainees()
        {
            var state = StateWith(FacilityType.SkillHall, 4, FacilitySpecialty.Rivalry);
            var a = new Adventurer();
            var b = new Adventurer();
            var c = new Adventurer();
            state.Adventurers.AddRange(new[] { a, b, c });
            var system = new TrainingSystem();

            Assert.True(system.TryAssign(state, a.Id, FacilityType.SkillHall));
            Assert.True(system.TryAssign(state, b.Id, FacilityType.SkillHall));
            Assert.False(system.TryAssign(state, c.Id, FacilityType.SkillHall));
        }

        [Fact]
        public void Infirmary_Sanatorium_AddsHpAndInjuryAboveLevel3()
        {
            Assert.Equal(1.0 + 2 * 0.25 + 2 * 0.35, FacilityBalance.GetInfirmaryHpRecoveryMultiplier(5, FacilitySpecialty.Sanatorium), precision: 6);
            Assert.Equal(3 + 2, FacilityBalance.GetInfirmaryInjuryRecoverySpeed(5, FacilitySpecialty.Sanatorium));
            Assert.Equal(0, FacilityBalance.GetFieldAidSurvivalBonus(5, FacilitySpecialty.Sanatorium));
        }

        [Fact]
        public void Infirmary_FieldAid_StopsHpAtLevel3_AndAddsSurvival()
        {
            Assert.Equal(1.0 + 2 * 0.25, FacilityBalance.GetInfirmaryHpRecoveryMultiplier(5, FacilitySpecialty.FieldAid), precision: 6);
            Assert.Equal(3, FacilityBalance.GetInfirmaryInjuryRecoverySpeed(5, FacilitySpecialty.FieldAid));
            Assert.Equal(4, FacilityBalance.GetFieldAidSurvivalBonus(5, FacilitySpecialty.FieldAid), precision: 6);
        }

        [Fact]
        public void Infirmary_BelowLevel4_IsUnchanged()
        {
            Assert.Equal(1.5, FacilityBalance.GetInfirmaryHpRecoveryMultiplier(3, FacilitySpecialty.None), precision: 6);
            Assert.Equal(2, FacilityBalance.GetInfirmaryInjuryRecoverySpeed(2, FacilitySpecialty.None));
        }

        [Fact]
        public void Tavern_Leisure_AndTrade()
        {
            Assert.Equal(3 + 2 * 2, FacilityBalance.GetTavernSatisfactionRecovery(5, FacilitySpecialty.Leisure));
            Assert.Equal(3, FacilityBalance.GetTavernSatisfactionRecovery(5, FacilitySpecialty.Trade));
            Assert.Equal(200, FacilityBalance.GetTradeSideJobBonus(5, FacilitySpecialty.Trade));
            Assert.Equal(0, FacilityBalance.GetTradeSideJobBonus(5, FacilitySpecialty.Leisure));
        }

        [Fact]
        public void Trade_AddsToSideJobIncomeBase()
        {
            var state = StateWith(FacilityType.Tavern, 5, FacilitySpecialty.Trade);
            state.WeekNumber = 4;

            var income = new EconomySystem().ProcessWeeklySideJobIncome(state)!;

            Assert.Equal(200, income.TradeBonus);
            Assert.Equal(EconomyBalance.SideJobBaseAmount + 200, income.BaseGold);
        }

        [Fact]
        public void Appraisal_AddsEye()
        {
            var plain = new GameState();
            var state = StateWith(FacilityType.RecruitmentOffice, 5, FacilitySpecialty.Appraisal);

            Assert.Equal(PotentialEstimateSystem.GetEye(plain) + 0.2, PotentialEstimateSystem.GetEye(state), precision: 6);
        }

        [Fact]
        public void Recruiting_AddsCandidates_ToDefaultCount()
        {
            var state = StateWith(FacilityType.RecruitmentOffice, 5, FacilitySpecialty.Recruiting);
            var recruitment = new RecruitmentSystem(new SeededRng(1), new SeededRng(2));

            Assert.Equal(RecruitmentBalance.CandidateCount + 2, recruitment.GenerateCandidates(state).Count);
            Assert.Equal(3, recruitment.GenerateCandidates(state, candidateCount: 3).Count); // 人数を指定したときはそのまま
        }

        [Fact]
        public void WarRoom_Analysis_AndPathfinding()
        {
            Assert.Equal(0.2, FacilityBalance.GetAnalysisIntelBonus(5, FacilitySpecialty.Analysis), precision: 6);
            Assert.Equal(0, FacilityBalance.GetAnalysisIntelBonus(5, FacilitySpecialty.Pathfinding));
            Assert.Equal(10, FacilityBalance.GetPathfindingTraversalBonus(4, FacilitySpecialty.Pathfinding), precision: 6);
        }

        // ---------------- セーブ ----------------

        private static GameState RoundTrip(GameState state) =>
            GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(state.ToSaveData()))!);

        [Fact]
        public void Save_RoundTrips_SpecialtyAndRemodel()
        {
            var state = StateWith(FacilityType.Infirmary, 5, FacilitySpecialty.FieldAid);
            new FacilitySystem().TryStartRemodel(state, FacilityType.Infirmary, FacilitySpecialty.Sanatorium);

            var restored = RoundTrip(state);

            Assert.Equal(FacilitySpecialty.FieldAid, restored.GetFacilitySpecialty(FacilityType.Infirmary));
            Assert.NotNull(restored.UnderConstruction);
            Assert.True(restored.UnderConstruction!.IsRemodel);
            Assert.Equal(FacilitySpecialty.Sanatorium, restored.UnderConstruction.TargetSpecialty);
            Assert.Equal(5, restored.UnderConstruction.TargetLevel);
        }

        [Fact]
        public void Load_DropsSpecialty_NotOfferedByThatFacility()
        {
            var state = StateWith(FacilityType.Tavern, 4, FacilitySpecialty.Leisure);
            var data = state.ToSaveData();
            data.FacilitySpecialties[FacilityType.Tavern.ToString()] = FacilitySpecialty.Elite.ToString();

            var restored = GameState.FromSaveData(data);

            Assert.Equal(FacilitySpecialty.Leisure, restored.GetFacilitySpecialty(FacilityType.Tavern)); // 記録を捨てて既定（1つ目）
            Assert.Equal(FacilitySpecialty.None, restored.Facilities.First(f => f.Type == FacilityType.Tavern).Specialty);
        }
    }
}
