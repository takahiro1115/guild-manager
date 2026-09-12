using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 顧問制度（教官・参謀・スカウト）のテスト（仕様書 03 §7）。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class AdvisorSystemTests
    {
        // ---------------- 割り当て管理 ----------------

        [Fact]
        public void TryAssignTrainer_Succeeds_ForRetiredCandidateAndTrainingFacility()
        {
            var candidate = new Adventurer();
            var state = new GameState { RetiredAdventurers = { candidate } };
            var system = new AdvisorSystem();

            bool result = system.TryAssignTrainer(state, FacilityType.WarriorHall, candidate.Id);

            Assert.True(result);
            Assert.Equal(candidate.Id, state.AssignedTrainers[FacilityType.WarriorHall]);
        }

        [Fact]
        public void TryAssignTrainer_Fails_WhenCandidateIsNotRetired()
        {
            var stillActive = new Adventurer();
            var state = new GameState(); // RetiredAdventurersに含まれない
            var system = new AdvisorSystem();

            bool result = system.TryAssignTrainer(state, FacilityType.WarriorHall, stillActive.Id);

            Assert.False(result);
        }

        [Fact]
        public void TryAssignTrainer_Fails_ForNonTrainingFacility()
        {
            var candidate = new Adventurer();
            var state = new GameState { RetiredAdventurers = { candidate } };
            var system = new AdvisorSystem();

            bool result = system.TryAssignTrainer(state, FacilityType.WarRoom, candidate.Id);

            Assert.False(result);
        }

        [Fact]
        public void TryAssignTrainer_EachFacilitySlotIsIndependent()
        {
            var a = new Adventurer();
            var b = new Adventurer();
            var state = new GameState { RetiredAdventurers = { a, b } };
            var system = new AdvisorSystem();

            system.TryAssignTrainer(state, FacilityType.WarriorHall, a.Id);
            system.TryAssignTrainer(state, FacilityType.Church, b.Id);

            Assert.Equal(a.Id, state.AssignedTrainers[FacilityType.WarriorHall]);
            Assert.Equal(b.Id, state.AssignedTrainers[FacilityType.Church]);
        }

        [Fact]
        public void TryAssignTrainer_RestrictsCandidateToOneFacility_ReassigningReleasesThePrevious()
        {
            // ユーザー指摘：教官が複数施設に同時配置できてしまう不具合の修正確認。
            // 1人の教官は同時に1施設のみ担当できる（TrainingSystem.TryAssignの付け替えと同じ設計）。
            var trainer = new Adventurer();
            var state = new GameState { RetiredAdventurers = { trainer } };
            var system = new AdvisorSystem();
            system.TryAssignTrainer(state, FacilityType.WarriorHall, trainer.Id);

            bool result = system.TryAssignTrainer(state, FacilityType.Church, trainer.Id);

            Assert.True(result);
            Assert.Equal(trainer.Id, state.AssignedTrainers[FacilityType.Church]);
            Assert.False(state.AssignedTrainers.ContainsKey(FacilityType.WarriorHall)); // 元の施設からは解除される
        }

        [Fact]
        public void TryAssignAdvisor_RemovesCandidateFromExistingTrainerPost()
        {
            // ユーザー指摘：教官が参謀・スカウトを兼務できてしまう不具合の修正確認。
            // 参謀（参謀本部）も教官の各施設と同様に「1つの担当」として扱う。
            var candidate = new Adventurer();
            var state = new GameState { RetiredAdventurers = { candidate } };
            var system = new AdvisorSystem();
            system.TryAssignTrainer(state, FacilityType.WarriorHall, candidate.Id);

            bool result = system.TryAssignAdvisor(state, candidate.Id);

            Assert.True(result);
            Assert.Equal(candidate.Id, state.AssignedAdvisor);
            Assert.False(state.AssignedTrainers.ContainsKey(FacilityType.WarriorHall)); // 教官からは外れる
        }

        [Fact]
        public void TryAssignScoutMaster_RemovesCandidateFromExistingAdvisorPost()
        {
            // スカウト（採用本部）も同様に「1つの担当」として扱う。
            var candidate = new Adventurer();
            var state = new GameState { RetiredAdventurers = { candidate } };
            var system = new AdvisorSystem();
            system.TryAssignAdvisor(state, candidate.Id);

            bool result = system.TryAssignScoutMaster(state, candidate.Id);

            Assert.True(result);
            Assert.Equal(candidate.Id, state.AssignedScoutMaster);
            Assert.Null(state.AssignedAdvisor); // 参謀からは外れる
        }

        [Fact]
        public void TryAssignTrainer_RemovesCandidateFromExistingScoutMasterPost()
        {
            var candidate = new Adventurer();
            var state = new GameState { RetiredAdventurers = { candidate } };
            var system = new AdvisorSystem();
            system.TryAssignScoutMaster(state, candidate.Id);

            bool result = system.TryAssignTrainer(state, FacilityType.Church, candidate.Id);

            Assert.True(result);
            Assert.Equal(candidate.Id, state.AssignedTrainers[FacilityType.Church]);
            Assert.Null(state.AssignedScoutMaster); // スカウトからは外れる
        }

        [Fact]
        public void OnePersonCanHoldOnlyOnePost_AcrossAllTrainerAdvisorAndScoutMasterSlots()
        {
            // 教官(4施設)・参謀・スカウトの計6ポストのうち、1人が同時に就けるのは1つだけ。
            var candidate = new Adventurer();
            var state = new GameState { RetiredAdventurers = { candidate } };
            var system = new AdvisorSystem();

            system.TryAssignTrainer(state, FacilityType.WarriorHall, candidate.Id);
            system.TryAssignTrainer(state, FacilityType.Church, candidate.Id);
            system.TryAssignTrainer(state, FacilityType.MageLab, candidate.Id);
            system.TryAssignTrainer(state, FacilityType.ScoutPost, candidate.Id);
            system.TryAssignAdvisor(state, candidate.Id);
            system.TryAssignScoutMaster(state, candidate.Id); // 最後に任命したポストだけが残るはず

            Assert.Empty(state.AssignedTrainers);
            Assert.Null(state.AssignedAdvisor);
            Assert.Equal(candidate.Id, state.AssignedScoutMaster);
        }

        [Fact]
        public void UnassignTrainer_RemovesAssignment()
        {
            var candidate = new Adventurer();
            var state = new GameState { RetiredAdventurers = { candidate } };
            var system = new AdvisorSystem();
            system.TryAssignTrainer(state, FacilityType.WarriorHall, candidate.Id);

            system.UnassignTrainer(state, FacilityType.WarriorHall);

            Assert.False(state.AssignedTrainers.ContainsKey(FacilityType.WarriorHall));
        }

        [Fact]
        public void TryAssignAdvisor_Succeeds_ForRetiredCandidate()
        {
            var candidate = new Adventurer();
            var state = new GameState { RetiredAdventurers = { candidate } };
            var system = new AdvisorSystem();

            bool result = system.TryAssignAdvisor(state, candidate.Id);

            Assert.True(result);
            Assert.Equal(candidate.Id, state.AssignedAdvisor);
        }

        [Fact]
        public void TryAssignAdvisor_Fails_WhenCandidateIsNotRetired()
        {
            var stillActive = new Adventurer();
            var state = new GameState();
            var system = new AdvisorSystem();

            bool result = system.TryAssignAdvisor(state, stillActive.Id);

            Assert.False(result);
            Assert.Null(state.AssignedAdvisor);
        }

        [Fact]
        public void TryAssignScoutMaster_Succeeds_ForRetiredCandidate()
        {
            var candidate = new Adventurer();
            var state = new GameState { RetiredAdventurers = { candidate } };
            var system = new AdvisorSystem();

            bool result = system.TryAssignScoutMaster(state, candidate.Id);

            Assert.True(result);
            Assert.Equal(candidate.Id, state.AssignedScoutMaster);
        }

        // ---------------- ボーナス算出（連続比例。一致判定なし） ----------------

        [Fact]
        public void GetTrainerBonus_IsProportionalToTargetStatsPeakAverage()
        {
            var trainer = new Adventurer { STR = 80, VIT = 60 }; // 戦士訓練所の対象=[STR,VIT]、平均70

            double bonus = AdvisorSystem.GetTrainerBonus(trainer, FacilityType.WarriorHall);

            Assert.Equal(70.0 * AdvisorBalance.TrainerBonusCoefficient, bonus, precision: 6);
        }

        [Fact]
        public void GetTrainerBonus_UsesSingleStat_ForSingleStatFacility()
        {
            var trainer = new Adventurer { MND = 90 }; // 教会の対象=[MND]のみ

            double bonus = AdvisorSystem.GetTrainerBonus(trainer, FacilityType.Church);

            Assert.Equal(90.0 * AdvisorBalance.TrainerBonusCoefficient, bonus, precision: 6);
        }

        [Fact]
        public void GetAdvisorBonus_IsProportionalToSevenStatPeakAverage()
        {
            var advisor = new Adventurer { STR = 70, AGI = 70, VIT = 70, MND = 70, DEX = 70, LDR = 70, INT = 70 };

            double bonus = AdvisorSystem.GetAdvisorBonus(advisor);

            Assert.Equal(70.0 * AdvisorBalance.AdvisorBonusCoefficient, bonus, precision: 6);
        }

        [Fact]
        public void GetScoutMasterBonus_IsProportionalToLdrDexPeakAverage()
        {
            var scoutMaster = new Adventurer { LDR = 80, DEX = 60 }; // 平均70

            double bonus = AdvisorSystem.GetScoutMasterBonus(scoutMaster);

            Assert.Equal(70.0 * AdvisorBalance.ScoutMasterBonusCoefficient, bonus, precision: 6);
        }

        [Fact]
        public void Bonuses_UsePeakValue_NotCurrentDeclinedValue()
        {
            // 顧問効果は現在の実効値ではなく生涯ピーク値を基準にする（→ 03 §7 方針の確認）。
            // PeakLDR/PeakDEXへ明示的に80を記録した状態（＝現役時代に到達したピーク）を再現し、
            // その後、現在値だけが衰微等で下がったと仮定する。
            var advisor = new Adventurer { LDR = 80, DEX = 80 };
            advisor.PeakLDR = 80; // 明示的にピークを記録（GrowthSystemの成長ロールが行うのと同じ操作）
            advisor.PeakDEX = 80;
            advisor.LDR = 20; // 衰微等で現在値のみ下がった想定
            advisor.DEX = 20;

            double bonus = AdvisorSystem.GetScoutMasterBonus(advisor);

            // Peakゲッターは Max(記録済みの値, 現在値) を返すため、現在値が下がってもPeakは80のまま。
            Assert.Equal(80.0 * AdvisorBalance.ScoutMasterBonusCoefficient, bonus, precision: 6);
        }
    }
}
