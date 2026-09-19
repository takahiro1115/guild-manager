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
        /// <summary>
        /// v1.4改訂：教官・参謀・スカウトが紐づく施設（訓練4施設・作戦資料室・冒険者支援室）は
        /// Lv0（未建設）スタートになったため、配置成功を検証するテストでは明示的にLv1へ
        /// 引き上げてから使う（→ 03 §6）。
        /// </summary>
        private static void SetFacilityLevel(GameState state, FacilityType type, int level)
        {
            foreach (var f in state.Facilities)
                if (f.Type == type) { f.CurrentLevel = level; return; }
        }

        private static GameState BuildBuiltState(params Adventurer[] retired)
        {
            var state = new GameState { RetiredAdventurers = { } };
            foreach (var a in retired) state.RetiredAdventurers.Add(a);
            // 教官・参謀・スカウトが紐づく全施設をLv1（建設済み）にしておく（既存挙動を踏襲するテスト用）。
            SetFacilityLevel(state, FacilityType.WarriorHall, 1);
            SetFacilityLevel(state, FacilityType.Church, 1);
            SetFacilityLevel(state, FacilityType.MageLab, 1);
            SetFacilityLevel(state, FacilityType.ScoutPost, 1);
            SetFacilityLevel(state, FacilityType.WarRoom, 1);
            SetFacilityLevel(state, FacilityType.RecruitmentOffice, 1);
            return state;
        }

        // ---------------- 施設Lv0（未建設）ガード（→ 03 §6、v1.4改訂・新設） ----------------

        [Fact]
        public void TryAssignTrainer_Fails_WhenFacilityIsLevelZero()
        {
            var candidate = new Adventurer();
            var state = new GameState { RetiredAdventurers = { candidate } }; // 新規GameStateはLv0スタート
            var system = new AdvisorSystem();

            bool result = system.TryAssignTrainer(state, FacilityType.WarriorHall, candidate.Id);

            Assert.False(result);
            Assert.False(state.AssignedTrainers.ContainsKey(FacilityType.WarriorHall));
        }

        [Fact]
        public void TryAssignAdvisor_Fails_WhenWarRoomIsLevelZero()
        {
            var candidate = new Adventurer();
            var state = new GameState { RetiredAdventurers = { candidate } };
            var system = new AdvisorSystem();

            bool result = system.TryAssignAdvisor(state, candidate.Id);

            Assert.False(result);
            Assert.Null(state.AssignedAdvisor);
        }

        [Fact]
        public void TryAssignScoutMaster_Fails_WhenRecruitmentOfficeIsLevelZero()
        {
            var candidate = new Adventurer();
            var state = new GameState { RetiredAdventurers = { candidate } };
            var system = new AdvisorSystem();

            bool result = system.TryAssignScoutMaster(state, candidate.Id);

            Assert.False(result);
            Assert.Null(state.AssignedScoutMaster);
        }

        [Fact]
        public void TryAssignTrainer_Succeeds_OnceFacilityReachesLevelOne()
        {
            var candidate = new Adventurer();
            var state = new GameState { RetiredAdventurers = { candidate } };
            SetFacilityLevel(state, FacilityType.WarriorHall, 1);
            var system = new AdvisorSystem();

            bool result = system.TryAssignTrainer(state, FacilityType.WarriorHall, candidate.Id);

            Assert.True(result);
            Assert.Equal(candidate.Id, state.AssignedTrainers[FacilityType.WarriorHall]);
        }

        // ---------------- 割り当て管理（以下は該当施設がLv1に建設済みの前提） ----------------

        [Fact]
        public void TryAssignTrainer_Succeeds_ForRetiredCandidateAndTrainingFacility()
        {
            var candidate = new Adventurer();
            var state = BuildBuiltState(candidate);
            var system = new AdvisorSystem();

            bool result = system.TryAssignTrainer(state, FacilityType.WarriorHall, candidate.Id);

            Assert.True(result);
            Assert.Equal(candidate.Id, state.AssignedTrainers[FacilityType.WarriorHall]);
        }

        [Fact]
        public void TryAssignTrainer_Fails_WhenCandidateIsNotRetired()
        {
            var stillActive = new Adventurer();
            var state = BuildBuiltState(); // RetiredAdventurersに含まれない
            var system = new AdvisorSystem();

            bool result = system.TryAssignTrainer(state, FacilityType.WarriorHall, stillActive.Id);

            Assert.False(result);
        }

        [Fact]
        public void TryAssignTrainer_Fails_ForNonTrainingFacility()
        {
            var candidate = new Adventurer();
            var state = BuildBuiltState(candidate);
            var system = new AdvisorSystem();

            bool result = system.TryAssignTrainer(state, FacilityType.WarRoom, candidate.Id);

            Assert.False(result);
        }

        [Fact]
        public void TryAssignTrainer_EachFacilitySlotIsIndependent()
        {
            var a = new Adventurer();
            var b = new Adventurer();
            var state = BuildBuiltState(a, b);
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
            var state = BuildBuiltState(trainer);
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
            var state = BuildBuiltState(candidate);
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
            var state = BuildBuiltState(candidate);
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
            var state = BuildBuiltState(candidate);
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
            var state = BuildBuiltState(candidate);
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
            var state = BuildBuiltState(candidate);
            var system = new AdvisorSystem();
            system.TryAssignTrainer(state, FacilityType.WarriorHall, candidate.Id);

            system.UnassignTrainer(state, FacilityType.WarriorHall);

            Assert.False(state.AssignedTrainers.ContainsKey(FacilityType.WarriorHall));
        }

        [Fact]
        public void TryAssignAdvisor_Succeeds_ForRetiredCandidate()
        {
            var candidate = new Adventurer();
            var state = BuildBuiltState(candidate);
            var system = new AdvisorSystem();

            bool result = system.TryAssignAdvisor(state, candidate.Id);

            Assert.True(result);
            Assert.Equal(candidate.Id, state.AssignedAdvisor);
        }

        [Fact]
        public void TryAssignAdvisor_Fails_WhenCandidateIsNotRetired()
        {
            var stillActive = new Adventurer();
            var state = BuildBuiltState();
            var system = new AdvisorSystem();

            bool result = system.TryAssignAdvisor(state, stillActive.Id);

            Assert.False(result);
            Assert.Null(state.AssignedAdvisor);
        }

        [Fact]
        public void TryAssignScoutMaster_Succeeds_ForRetiredCandidate()
        {
            var candidate = new Adventurer();
            var state = BuildBuiltState(candidate);
            var system = new AdvisorSystem();

            bool result = system.TryAssignScoutMaster(state, candidate.Id);

            Assert.True(result);
            Assert.Equal(candidate.Id, state.AssignedScoutMaster);
        }

        // ---------------- ボーナス算出（連続比例。一致判定なし） ----------------

        // 生涯ピーク値は撤廃済み（v2.0）。いずれのボーナスも引退時の実効ステータス（STR等）を直接参照する。

        [Fact]
        public void GetTrainerBonus_IsProportionalToTargetStatsEffectiveAverage()
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
        public void GetAdvisorBonus_IsProportionalToSevenStatEffectiveAverage()
        {
            // 7能力の実効値がばらついていても、単純平均（=70）を基準にする。
            var advisor = new Adventurer { STR = 100, AGI = 40, VIT = 90, MND = 50, DEX = 70, LDR = 80, INT = 60 };

            double bonus = AdvisorSystem.GetAdvisorBonus(advisor);

            Assert.Equal(70.0 * AdvisorBalance.AdvisorBonusCoefficient, bonus, precision: 6);
        }

        [Fact]
        public void GetScoutMasterBonus_IsProportionalToLdrDexEffectiveAverage()
        {
            var scoutMaster = new Adventurer { LDR = 80, DEX = 60 }; // 平均70

            double bonus = AdvisorSystem.GetScoutMasterBonus(scoutMaster);

            Assert.Equal(70.0 * AdvisorBalance.ScoutMasterBonusCoefficient, bonus, precision: 6);
        }

        [Fact]
        public void Bonuses_FollowCurrentEffectiveStats_Directly()
        {
            // 生涯ピーク値の撤廃（v2.0）以降、顧問効果は引退時の実効ステータスをそのまま参照する。
            // 実効値が変われば、ボーナスも記録値に縛られずにそのまま追従する。
            var advisor = new Adventurer { LDR = 80, DEX = 80 };
            Assert.Equal(80.0 * AdvisorBalance.ScoutMasterBonusCoefficient, AdvisorSystem.GetScoutMasterBonus(advisor), precision: 6);

            advisor.LDR = 20;
            advisor.DEX = 20;

            Assert.Equal(20.0 * AdvisorBalance.ScoutMasterBonusCoefficient, AdvisorSystem.GetScoutMasterBonus(advisor), precision: 6);
        }

        [Fact]
        public void TrainerAndAdvisorBonuses_IgnoreStatsOutsideTheirTargets()
        {
            // 教官は担当施設の対象ステータスだけを見る（戦士訓練所=STR/VIT。INT・LDRがいくら高くても無関係）。
            var trainer = new Adventurer { STR = 50, VIT = 30, INT = 100, LDR = 100 };

            Assert.Equal(40.0 * AdvisorBalance.TrainerBonusCoefficient,
                AdvisorSystem.GetTrainerBonus(trainer, FacilityType.WarriorHall), precision: 6);
            Assert.Equal(100.0 * AdvisorBalance.TrainerBonusCoefficient,
                AdvisorSystem.GetTrainerBonus(trainer, FacilityType.MageLab), precision: 6);
        }
    }
}
