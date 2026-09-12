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
    /// ポストは「教官(施設ごとに4枠)・参謀(作戦資料室に1枠)・スカウト(採用本部相当に1枠)」の
    /// 計6枠あるが、参謀（参謀本部）・スカウト（採用本部）も教官の各施設と同様に
    /// 「1つの担当（ポスト）」として扱う（ユーザー指示）。1人の顧問候補が同時に
    /// 就けるポストは1つのみ：いずれかのポストに新たに任命すると、その候補が
    /// 就いていた他の全ポスト（教官・参謀・スカウトを問わず）から自動的に外れる。
    /// </summary>
    public class AdvisorSystem
    {
        // ---------------- 割り当て管理 ----------------

        /// <summary>
        /// 教官を配置する。候補が引退済みでない、または訓練施設以外を指定した場合は失敗する。
        /// 1人の顧問候補が就けるポストは1つのみのため、この候補が既に教官・参謀・スカウトの
        /// いずれかのポストに就いていれば、そちらを解除してから新しい施設へ付け替える。
        /// 各施設スロット自体も1名まで（既に別の人物が配置済みなら上書きで交代）。
        /// </summary>
        public bool TryAssignTrainer(GameState state, FacilityType facility, Guid candidateId)
        {
            if (!FacilityBalance.IsTrainingFacility(facility)) return false;
            if (!IsRetiredCandidate(state, candidateId)) return false;

            UnassignFromAllPosts(state, candidateId);
            state.AssignedTrainers[facility] = candidateId;
            return true;
        }

        /// <summary>指定した施設の教官配置を解除する。</summary>
        public void UnassignTrainer(GameState state, FacilityType facility) =>
            state.AssignedTrainers.Remove(facility);

        /// <summary>
        /// 参謀を配置する（作戦資料室＝参謀本部に1名まで。既に配置済みなら上書きで交代）。
        /// 1人の顧問候補が就けるポストは1つのみのため、教官・スカウトの他のポストからは外れる。
        /// </summary>
        public bool TryAssignAdvisor(GameState state, Guid candidateId)
        {
            if (!IsRetiredCandidate(state, candidateId)) return false;

            UnassignFromAllPosts(state, candidateId);
            state.AssignedAdvisor = candidateId;
            return true;
        }

        public void UnassignAdvisor(GameState state) => state.AssignedAdvisor = null;

        /// <summary>
        /// スカウト顧問を任命する（採用本部相当のポストに1名まで。既に任命済みなら上書きで交代）。
        /// 1人の顧問候補が就けるポストは1つのみのため、教官・参謀の他のポストからは外れる。
        /// </summary>
        public bool TryAssignScoutMaster(GameState state, Guid candidateId)
        {
            if (!IsRetiredCandidate(state, candidateId)) return false;

            UnassignFromAllPosts(state, candidateId);
            state.AssignedScoutMaster = candidateId;
            return true;
        }

        public void UnassignScoutMaster(GameState state) => state.AssignedScoutMaster = null;

        private static bool IsRetiredCandidate(GameState state, Guid candidateId) =>
            state.RetiredAdventurers.Any(a => a.Id == candidateId);

        /// <summary>
        /// 指定した候補者を、教官（全施設）・参謀・スカウトの全ポストから外す（重複兼任防止）。
        /// いずれかのポストへ新たに任命する直前に必ず呼ぶ。
        /// </summary>
        private static void UnassignFromAllPosts(GameState state, Guid candidateId)
        {
            foreach (var facility in state.AssignedTrainers
                         .Where(kv => kv.Value == candidateId)
                         .Select(kv => kv.Key)
                         .ToList())
            {
                state.AssignedTrainers.Remove(facility);
            }

            if (state.AssignedAdvisor == candidateId) state.AssignedAdvisor = null;
            if (state.AssignedScoutMaster == candidateId) state.AssignedScoutMaster = null;
        }

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
