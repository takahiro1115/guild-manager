using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 顧問制度（教官・参謀・スカウト）。仕様書 03 §7 参照。
    ///
    /// 「得意ステータスは何か」を判定する一致判定ロジックは持たない。各役職が参照する
    /// 特定ステータス(群)の生涯ピーク値（Adventurer.PeakXXX）を見て、連続比例のボーナスを
    /// 算出するだけである（→ AdvisorBalance）。
    ///
    /// 割り当て対象は GameState.RetiredAdventurers（引退済み・顧問候補）に限る。
    /// 各スロット（教官は施設ごとに1名、参謀は1名、スカウトは1名）は独立しており、
    /// 同じ人物を複数の役職に重複して割り当てることを妨げない（仕様に明記が無いため）。
    /// </summary>
    public class AdvisorSystem
    {
        // ---------------- 割り当て管理 ----------------

        /// <summary>
        /// 教官を配置する。候補が引退済みでない、または訓練施設以外を指定した場合は失敗する。
        /// 施設ごとに1名まで（既に配置済みなら上書きで交代）。
        /// </summary>
        public bool TryAssignTrainer(GameState state, FacilityType facility, Guid candidateId)
        {
            if (!FacilityBalance.IsTrainingFacility(facility)) return false;
            if (!IsRetiredCandidate(state, candidateId)) return false;

            state.AssignedTrainers[facility] = candidateId;
            return true;
        }

        /// <summary>指定した施設の教官配置を解除する。</summary>
        public void UnassignTrainer(GameState state, FacilityType facility) =>
            state.AssignedTrainers.Remove(facility);

        /// <summary>参謀を配置する（作戦資料室に1名まで。既に配置済みなら上書きで交代）。</summary>
        public bool TryAssignAdvisor(GameState state, Guid candidateId)
        {
            if (!IsRetiredCandidate(state, candidateId)) return false;

            state.AssignedAdvisor = candidateId;
            return true;
        }

        public void UnassignAdvisor(GameState state) => state.AssignedAdvisor = null;

        /// <summary>スカウト顧問を任命する（施設に紐づかない役職スロット。1名まで。既に任命済みなら上書きで交代）。</summary>
        public bool TryAssignScoutMaster(GameState state, Guid candidateId)
        {
            if (!IsRetiredCandidate(state, candidateId)) return false;

            state.AssignedScoutMaster = candidateId;
            return true;
        }

        public void UnassignScoutMaster(GameState state) => state.AssignedScoutMaster = null;

        private static bool IsRetiredCandidate(GameState state, Guid candidateId) =>
            state.RetiredAdventurers.Any(a => a.Id == candidateId);

        // ---------------- ボーナス算出（連続比例。一致判定なし） ----------------

        /// <summary>
        /// 教官ボーナス：配置先施設の対象ステータス（1つまたは2つの平均）の生涯ピーク値に比例。
        /// 成長ロールの確率倍率（GrowthBalance.TrainingFacilityMultiplier）に加算する値として返す。
        /// </summary>
        public static double GetTrainerBonus(Adventurer trainer, FacilityType facility)
        {
            var targetStats = FacilityBalance.GetTrainingTargetStats(facility);
            double peakAverage = targetStats.Average(stat => AdventurerStatAccessor.GetPeak(trainer, stat));
            return peakAverage * AdvisorBalance.TrainerBonusCoefficient;
        }

        /// <summary>
        /// 参謀ボーナス：生涯ピーク7能力平均に比例。PartyScout（§4.1）とSurvivalThreshold
        /// （§4.3）の両方に同じ値を加算する想定（→ QuestResolver.Resolveの引数として渡す）。
        /// </summary>
        public static double GetAdvisorBonus(Adventurer advisor)
        {
            double peakAverage = AdventurerStatAccessor.AllStatNames.Average(stat => AdventurerStatAccessor.GetPeak(advisor, stat));
            return peakAverage * AdvisorBalance.AdvisorBonusCoefficient;
        }

        /// <summary>
        /// スカウトボーナス：生涯ピークのLDR・DEX平均に比例。新春採用試験の有望新人応募率に
        /// 加算する値として返す（→ RecruitmentSystem.GenerateCandidatesの引数として渡す）。
        /// </summary>
        public static double GetScoutMasterBonus(Adventurer scoutMaster)
        {
            double peakAverage = (AdventurerStatAccessor.GetPeak(scoutMaster, "LDR") + AdventurerStatAccessor.GetPeak(scoutMaster, "DEX")) / 2.0;
            return peakAverage * AdvisorBalance.ScoutMasterBonusCoefficient;
        }
    }
}
