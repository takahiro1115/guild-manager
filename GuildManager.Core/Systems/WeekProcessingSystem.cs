using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 週次決算処理のオーケストレーション。仕様書 03 §1.3（自動スキップ）参照（v1.10改訂で新設）。
    ///
    /// これまでGodot側（MainDashboard.OnNextWeekPressed）に直接書かれていた「派遣の解決→
    /// 治安・格付け→受注可能クエストの補充→経済→訓練→回復→成長→満足度→加齢→施設→
    /// 敗北判定」という一連の週次決算処理を、GuildManager.Core側の1つのメソッドに集約した
    /// （→ 05技術メモ「GuildManager.CoreはGodotに依存しない」方針に沿う）。
    ///
    /// パーティーの派遣（どのパーティーでどのクエストに出撃するか）は含まない。これは
    /// プレイヤーの意思決定であり、自動スキップ中は一切行わない（→ 03 §1.3「新しいクエストを
    /// 自動受注させない」）ため、呼び出し側（UI）が派遣操作を済ませてから本メソッドを呼ぶ想定。
    /// 既に派遣済みの複数週クエストの解決自体は、本メソッドの中でそのまま行われる。
    /// </summary>
    public class WeekProcessingSystem
    {
        private readonly QuestDispatchSystem _questDispatchSystem;
        private readonly GuildRankSystem _guildRankSystem;
        private readonly SecuritySystem _securitySystem;
        private readonly QuestBoardSystem _questBoardSystem;
        private readonly EconomySystem _economySystem;
        private readonly SubsidySystem _subsidySystem;
        private readonly TrainingSystem _trainingSystem;
        private readonly InjuryRecoverySystem _injuryRecoverySystem;
        private readonly RestRecoverySystem _restRecoverySystem;
        private readonly GrowthSystem _growthSystem;
        private readonly SatisfactionSystem _satisfactionSystem;
        private readonly AgingSystem _agingSystem;
        private readonly FacilitySystem _facilitySystem;
        private readonly DefeatSystem _defeatSystem;
        private readonly RecruitmentSystem _recruitmentSystem;

        public WeekProcessingSystem(
            QuestDispatchSystem questDispatchSystem,
            GuildRankSystem guildRankSystem,
            SecuritySystem securitySystem,
            QuestBoardSystem questBoardSystem,
            EconomySystem economySystem,
            SubsidySystem subsidySystem,
            TrainingSystem trainingSystem,
            InjuryRecoverySystem injuryRecoverySystem,
            RestRecoverySystem restRecoverySystem,
            GrowthSystem growthSystem,
            SatisfactionSystem satisfactionSystem,
            AgingSystem agingSystem,
            FacilitySystem facilitySystem,
            DefeatSystem defeatSystem,
            RecruitmentSystem recruitmentSystem)
        {
            _questDispatchSystem = questDispatchSystem;
            _guildRankSystem = guildRankSystem;
            _securitySystem = securitySystem;
            _questBoardSystem = questBoardSystem;
            _economySystem = economySystem;
            _subsidySystem = subsidySystem;
            _trainingSystem = trainingSystem;
            _injuryRecoverySystem = injuryRecoverySystem;
            _restRecoverySystem = restRecoverySystem;
            _growthSystem = growthSystem;
            _satisfactionSystem = satisfactionSystem;
            _agingSystem = agingSystem;
            _facilitySystem = facilitySystem;
            _defeatSystem = defeatSystem;
            _recruitmentSystem = recruitmentSystem;
        }

        /// <summary>
        /// 1週分の決算処理を実行し、週番号を1つ進める。パーティーの派遣操作（受注クエストを
        /// 選ぶこと）は本メソッドの対象外＝呼び出し側が本メソッドを呼ぶ前に済ませておく。
        /// </summary>
        public WeeklySettlementResult ProcessWeek(GameState state)
        {
            var result = new WeeklySettlementResult();
            int thisWeek = state.WeekNumber;
            result.Flags.Week = thisWeek;

            int threatBefore = state.ThreatLevel;
            bool wasFinalQuestUnlocked = state.FinalQuestUnlocked;
            var neededNegotiationBefore = state.Adventurers.Where(a => a.NeedsNegotiation).Select(a => a.Id).ToHashSet();
            // 致死判定の「古傷」を新規に負ったかどうかは、判定前の保有状況とのスナップショット
            // 比較で検出する（QuestResolver.Resolve自体は「今回新たに付与したか」を返さないため）。
            var hadOldWoundBefore = state.ActiveDispatches
                .SelectMany(d => d.Party.Members)
                .Where(m => m.HasTrait(TraitCatalog.OldWoundId))
                .Select(m => m.Id)
                .ToHashSet();

            // 派遣中（今週出発した分も含む）の冒険者は、HP自然回復・訓練場成長の対象から外す（→ 03 §4.0.1）。
            var dispatchedIds = state.Adventurers.Where(a => a.IsDispatched).Select(a => a.Id).ToHashSet();

            // 満了した派遣（1週クエストは今週のうちに満了する）を解決する。
            result.DispatchResolutions.AddRange(_questDispatchSystem.ProcessWeeklyDispatches(state));
            bool achievedRankAppropriateQuestThisWeek = false;
            bool anyFallenOrNewOldWound = false;
            foreach (var resolution in result.DispatchResolutions)
            {
                // ギルド格付け（→ 03 §8.1）：名声は解決の都度加減算する。
                _guildRankSystem.ApplyQuestResult(state, resolution.Result.QuestAchieved);
                if (resolution.Result.QuestAchieved &&
                    resolution.Quest.Rank >= GuildRankBalance.ToQuestRankFloor(state.GuildRank))
                {
                    achievedRankAppropriateQuestThisWeek = true;
                }

                // 治安・脅威度（→ 03 §4.4）：対象は討伐クエストのみ（達成で減少・失敗で上昇）。
                int threatDelta = _securitySystem.ApplyQuestResolution(state, resolution.Quest, resolution.Result.QuestAchieved);
                result.ResolvedQuestThreatDeltas.Add((resolution.Quest, threatDelta));

                if (resolution.Result.FallenAdventurerIds.Count > 0)
                    anyFallenOrNewOldWound = true;
                if (resolution.Party.Members.Any(m => !hadOldWoundBefore.Contains(m.Id) && m.HasTrait(TraitCatalog.OldWoundId)))
                    anyFallenOrNewOldWound = true;

                // 複数週クエストの帰還（→ 03 §1.3自動スキップ停止条件4）。1週クエストの
                // その場解決（＝出発と同じ週に決着）はここでいう「帰還」には含めない。
                if (resolution.Quest.DurationWeeks > 1)
                    result.Flags.MultiWeekQuestReturned = true;
            }
            result.Flags.DeathOrPermanentInjuryOccurred = anyFallenOrNewOldWound;

            // 受注可能クエスト一覧の週次管理（→ 03 §4.0・§4.4）：期限切れ（放置）の除去と補充。
            // 放置された討伐クエストは脅威度上昇の対象になる。
            var expiredQuests = _questBoardSystem.ProcessWeeklyBoard(state);
            foreach (var expiredQuest in expiredQuests)
            {
                int abandonedThreatDelta = _securitySystem.ApplyAbandonedQuest(state, expiredQuest);
                result.AbandonedQuestThreatDeltas.Add((expiredQuest, abandonedThreatDelta));
            }
            // 討伐クエストの期限切れ「1週前」判定（→ 03 §1.3自動スキップ停止条件8）。
            // ProcessWeeklyBoardでDeadlineWeeksを減算した後の状態を見るため、この位置で呼ぶ。
            result.QuestsExpiringNextWeek.AddRange(QuestBoardSystem.GetQuestsExpiringNextWeek(state));
            result.Flags.SubjugationQuestExpiringNextWeek = result.QuestsExpiringNextWeek.Count > 0;

            // 出撃の有無にかかわらず、時間は必ず進む。
            _economySystem.ApplyWeeklyWages(state);

            // 月次助成金（4週に1回。→ 03 §8.1・§4.4：脅威度75%超で50%カット）。
            result.SubsidyAmount = _subsidySystem.ProcessWeeklySubsidy(state);

            _trainingSystem.ProcessWeeklyTraining(state, dispatchedIds); // → 03 §3.1〜3.4・§3.5改：訓練場の週次費用・HP微減
            result.TraitTransmissionEvents.AddRange(_trainingSystem.ProcessWeeklyTraitTransmission(state)); // → 特性伝授刷新仕様：教官からの週次伝授ロール
            _injuryRecoverySystem.ProcessWeeklyRecovery(state);
            _restRecoverySystem.ProcessWeeklyRest(state, dispatchedIds); // → 03 §3.5改：静養・HP自然回復（訓練場配置中は対象外）
            result.TrainingGrowthEvents.AddRange(_growthSystem.ProcessTrainingGrowth(state, dispatchedIds)); // → 03 §3.1〜3.4：成長トリガー経路2

            _satisfactionSystem.ProcessWeeklySatisfaction(state, dispatchedIds); // → 03 §5.1：満足度変動
            result.NegotiationTerminated.AddRange(_satisfactionSystem.ProcessWeeklyNegotiation(state)); // → 03 §5.2：契約交渉・退団
            // 満足度警告（契約交渉）が今週「新たに」発生したか（→ 03 §1.3自動スキップ停止条件2）。
            // 既に警告中で対応待ちのまま変化がないだけの週では発火させない。
            result.Flags.SatisfactionWarningOccurred =
                state.Adventurers.Any(a => a.NeedsNegotiation && !neededNegotiationBefore.Contains(a.Id));

            _agingSystem.ProcessWeeklyAging(state); // → 03 §3：加齢・衰微モデル

            result.CompletedFacility = _facilitySystem.ProcessWeeklyConstruction(state); // → 03 §6.1：施設Lv投資
            result.Flags.FacilityConstructionCompleted = result.CompletedFacility != null;

            // ギルド格付け（→ 03 §8.1・§8.1.1）：名声自然減衰の判定と昇格・降格判定・Aランク到達
            // フラグ（→ GuildRankSystem.UpdateRank）は週次決算で1回だけ行う。
            result.RankChange = _guildRankSystem.ProcessWeeklySettlement(state, achievedRankAppropriateQuestThisWeek);
            result.Flags.FinalQuestNewlyUnlocked = !wasFinalQuestUnlocked && state.FinalQuestUnlocked;

            // 脅威度が75%・100%の閾値を今週新たに跨いだか（→ 03 §1.3自動スキップ停止条件6）。
            // 既に閾値を超えたまま変化がない週では発火させない。
            result.Flags.ThreatThresholdNewlyCrossed =
                (threatBefore < SecurityBalance.SubsidyCutThreatThreshold && state.ThreatLevel >= SecurityBalance.SubsidyCutThreatThreshold) ||
                (threatBefore < SecurityBalance.SecurityCollapseThreshold && state.ThreatLevel >= SecurityBalance.SecurityCollapseThreshold);

            // 敗北条件判定（→ 03 §8.3）：週次決算の最後に1回だけ行う。
            // 破産（所持金マイナス4週連続、猶予あり）／治安崩壊（脅威度100%到達、猶予なし即時敗北）。
            result.NewDefeatReason = _defeatSystem.ProcessWeeklySettlement(state);
            result.Flags.DefeatOccurred = result.NewDefeatReason != null;

            state.WeekNumber++;

            // 新春採用試験（2年目以降の新年第1週のみ）。ゲームオーバー後は発生させない。
            result.Flags.RecruitmentTrialOccurred =
                state.DefeatReason == null && _recruitmentSystem.IsRecruitmentWeek(state.WeekNumber);

            return result;
        }
    }
}
