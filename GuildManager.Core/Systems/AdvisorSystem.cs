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
    /// 特定ステータス(群)の引退時の実効値（Adventurer.STR等）を見て、連続比例のボーナスを
    /// 算出するだけである（→ AdvisorBalance）。生涯ピーク値は加齢衰微の廃止（v2.0）に伴い撤廃した
    /// （引退後に実効値が下がる経路が無いため、実効値そのものが現役時代の到達値を表す）。
    ///
    /// 割り当て対象は GameState.RetiredAdventurers（引退済み・顧問候補）に限る。
    /// ポストは「教官(施設ごとに4枠)・参謀(作戦資料室に1枠)・スカウト(冒険者支援室に1枠)」の
    /// 計6枠あるが、参謀（参謀本部＝作戦資料室）・スカウト（冒険者支援室）も教官の各施設と
    /// 同様に「1つの担当（ポスト）」として扱う（ユーザー指示）。1人の顧問候補が同時に
    /// 就けるポストは1つのみ：いずれかのポストに新たに任命すると、その候補が
    /// 就いていた他の全ポスト（教官・参謀・スカウトを問わず）から自動的に外れる。
    ///
    /// v1.4改訂：各ポストは対応する施設がLv0（未建設）の間は配置できない
    /// （教官は各訓練施設、参謀は作戦資料室、スカウトは冒険者支援室のLvをそれぞれ参照する）。
    /// </summary>
    public class AdvisorSystem
    {
        // ---------------- 割り当て管理 ----------------

        /// <summary>
        /// 教官を配置する。候補が引退済みでない、訓練施設以外を指定した場合、または
        /// 対象施設がLv0（未建設）の場合は失敗する（v1.4改訂：施設Lv0ガード）。
        /// 1人の顧問候補が就けるポストは1つのみのため、この候補が既に教官・参謀・スカウトの
        /// いずれかのポストに就いていれば、そちらを解除してから新しい施設へ付け替える。
        /// 各施設スロット自体も1名まで（既に別の人物が配置済みなら上書きで交代）。
        /// </summary>
        public bool TryAssignTrainer(GameState state, FacilityType facility, Guid candidateId)
        {
            if (!FacilityBalance.IsTrainingFacility(facility)) return false;
            if (state.GetFacilityLevel(facility) < 1) return false; // Lv0（未建設）は配置不可（→ 03 §6）
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
        /// 作戦資料室がLv0（未建設）の場合は失敗する（v1.4改訂：施設Lv0ガード）。
        /// 1人の顧問候補が就けるポストは1つのみのため、教官・スカウトの他のポストからは外れる。
        /// </summary>
        public bool TryAssignAdvisor(GameState state, Guid candidateId)
        {
            if (state.GetFacilityLevel(FacilityType.WarRoom) < 1) return false; // Lv0（未建設）は配置不可（→ 03 §6・§7.2）
            if (!IsRetiredCandidate(state, candidateId)) return false;

            UnassignFromAllPosts(state, candidateId);
            state.AssignedAdvisor = candidateId;
            return true;
        }

        public void UnassignAdvisor(GameState state) => state.AssignedAdvisor = null;

        /// <summary>
        /// スカウト顧問を任命する（冒険者支援室に1名まで。既に任命済みなら上書きで交代）。
        /// 冒険者支援室がLv0（未建設）の場合は失敗する（v1.4改訂：施設Lv0ガード。新設）。
        /// 1人の顧問候補が就けるポストは1つのみのため、教官・参謀の他のポストからは外れる。
        /// </summary>
        public bool TryAssignScoutMaster(GameState state, Guid candidateId)
        {
            if (state.GetFacilityLevel(FacilityType.RecruitmentOffice) < 1) return false; // Lv0（未建設）は配置不可（→ 03 §6・§7.3）
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
        /// 教官ボーナス：配置先施設の対象ステータス（1つまたは2つの平均）の実効値に比例。
        /// 成長ロールの確率倍率（GrowthBalance.TrainingFacilityMultiplier）に加算する値として返す。
        /// </summary>
        public static double GetTrainerBonus(Adventurer trainer, FacilityType facility)
        {
            var targetStats = FacilityBalance.GetTrainingTargetStats(facility);
            double average = targetStats.Average(stat => AdventurerStatAccessor.GetStat(trainer, stat));
            return average * AdvisorBalance.TrainerBonusCoefficient;
        }

        /// <summary>
        /// 参謀ボーナス：7能力（STR・VIT・AGI・DEX・INT・MND・LDR）の実効値平均に比例。
        /// 旧通常クエストのPartyScout（§4.1）とSurvivalThreshold（§4.3）へ加算していた値。
        /// 旧クエストの撤去（2026年9月）以降は加算先が無い（大迷宮向けは GetAdvisorSurveyIntelBonus・
        /// GetAdvisorTraversalPowerBonus）。
        /// </summary>
        public static double GetAdvisorBonus(Adventurer advisor) =>
            SevenStatAverage(advisor) * AdvisorBalance.AdvisorBonusCoefficient;

        // ---------------- 参謀の大迷宮支援（2026年9月、→ 03 §7.2） ----------------

        /// <summary>任命中の参謀（作戦資料室）。未任命・該当者なしならnull。</summary>
        public static Adventurer? GetAssignedAdvisor(GameState state) =>
            state.AssignedAdvisor is Guid id ? state.RetiredAdventurers.FirstOrDefault(a => a.Id == id) : null;

        /// <summary>
        /// 迷宮調査（ボス解析）の解析率上昇量への加算率（小数。0.10＝+10%）。
        /// 参謀の7能力実効値平均×Advisor_SurveyIntelBonusCoeff。未任命なら0。
        /// ScoutingResolver が研究ボーナス（IntelRateBonus）と合算して (1＋合計) 倍に使う。
        /// </summary>
        public static double GetAdvisorSurveyIntelBonus(GameState state)
        {
            var advisor = GetAssignedAdvisor(state);
            return advisor == null ? 0 : SevenStatAverage(advisor) * AdvisorBalance.SurveyIntelBonusCoefficient;
        }

        /// <summary>
        /// 道中潜行の走破力スコアへの加算値。参謀の7能力実効値平均×Advisor_TraversalPowerBonusCoeff。
        /// 未任命なら0。DungeonTraversalResolver.CalculateTraversalScore が研究ボーナスと並べて加算する。
        /// </summary>
        public static double GetAdvisorTraversalPowerBonus(GameState state)
        {
            var advisor = GetAssignedAdvisor(state);
            return advisor == null ? 0 : SevenStatAverage(advisor) * AdvisorBalance.TraversalPowerBonusCoefficient;
        }

        private static double SevenStatAverage(Adventurer advisor) =>
            AdventurerStatAccessor.AllStatNames.Average(stat => AdventurerStatAccessor.GetStat(advisor, stat));

        /// <summary>
        /// スカウトボーナス：LDR・DEXの実効値平均に比例。新春採用試験の有望新人応募率に
        /// 加算する値として返す（→ RecruitmentSystem.GenerateCandidatesの引数として渡す）。
        /// </summary>
        public static double GetScoutMasterBonus(Adventurer scoutMaster)
        {
            double average = (AdventurerStatAccessor.GetStat(scoutMaster, "LDR") + AdventurerStatAccessor.GetStat(scoutMaster, "DEX")) / 2.0;
            return average * AdvisorBalance.ScoutMasterBonusCoefficient;
        }
    }
}
