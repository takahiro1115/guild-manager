using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

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
        private readonly GuildProgressionSystem _guildProgressionSystem;
        private readonly DungeonExpeditionSystem _dungeonExpeditionSystem;

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
            RecruitmentSystem recruitmentSystem,
            // 省略可能：進行管理（→ コアシステム刷新仕様「4. 進行管理」）は、既に注入されている
            // RecruitmentSystem（昇格時の新人補充に使う）からそのまま組み立てられるため、
            // 既存の呼び出し側を変更せずに接続できるよう既定値を持たせている。
            GuildProgressionSystem? guildProgressionSystem = null,
            // 省略可能：大迷宮への出撃の週次解決（→ DungeonExpeditionSystem）。進行管理と同じく、
            // 既存の呼び出し側を変更せずに済むよう既定値を持たせている（出撃が無ければ何も起きない）。
            DungeonExpeditionSystem? dungeonExpeditionSystem = null)
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
            _guildProgressionSystem = guildProgressionSystem ?? new GuildProgressionSystem(recruitmentSystem);
            _dungeonExpeditionSystem = dungeonExpeditionSystem ?? new DungeonExpeditionSystem(
                new ScoutingResolver(new SeededRng(DefaultScoutingSeed)),
                new DungeonResolver(new SeededRng(DefaultDungeonSeed)),
                satisfactionSystem,
                new CompatibilitySystem(new SeededRng(DefaultCompatibilitySeed)));
        }

        // 大迷宮システムを省略した場合の既定シード（固定シードで再現性を保つ。→ MainDashboardの方針と同じ）。
        private const int DefaultScoutingSeed = 1453;
        private const int DefaultDungeonSeed = 1588;
        private const int DefaultCompatibilitySeed = 2526;

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

                // ランク昇格試験による判定は2026年9月、大迷宮への一本化改訂で無効化した。
                // ランク昇格・出撃枠拡張は DungeonExpeditionSystem.ApplyFieldProgression
                // （森10F/20Fボス撃破）のみを唯一のトリガーとする（→ 03 §0.4・§4.5.1）。
                // GuildProgressionSystem.ApplyPromotionIfExamCleared自体は削除していない
                // （既存セーブの PromotionExamPassed 等のフィールドはそのまま読める。
                // 実運用では昇格試験クエスト自体がもう生成されないため、この呼び出しを
                // 復活させても quest.IsBoss が真になることはなく、常にnullを返す）。

                // 複数週クエストの帰還（→ 03 §1.3自動スキップ停止条件4）。1週クエストの
                // その場解決（＝出発と同じ週に決着）はここでいう「帰還」には含めない。
                if (resolution.Quest.DurationWeeks > 1)
                    result.Flags.MultiWeekQuestReturned = true;
            }
            // 大迷宮への出撃（調査任務・ボス討伐）の解決（→ DungeonExpeditionSystem）。
            // 出撃は常に1週拘束のため、出撃操作をした週の決算で必ず決着する。
            result.DungeonMissionResolutions.AddRange(_dungeonExpeditionSystem.ProcessWeeklyMissions(state));
            foreach (var resolution in result.DungeonMissionResolutions)
            {
                if (resolution.DungeonResult != null && resolution.DungeonResult.ForceRetiredAdventurerIds.Count > 0)
                    anyFallenOrNewOldWound = true;
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

            // 脅威度が75%の閾値を今週新たに跨いだか（→ 03 §1.3自動スキップ停止条件6）。
            // 既に閾値を超えたまま変化がない週では発火させない。
            // 治安崩壊（旧100%閾値）は敗北条件から撤廃済みのため、ここでの判定対象からも外した
            // （→ DefeatSystem・経営破綻への一本化改訂）。
            result.Flags.ThreatThresholdNewlyCrossed =
                threatBefore < SecurityBalance.SubsidyCutThreatThreshold && state.ThreatLevel >= SecurityBalance.SubsidyCutThreatThreshold;

            // 敗北条件判定（→ 03 §8.3）：週次決算の最後に1回だけ行う。
            // 破産（所持金マイナス4週連続、猶予あり）のみが唯一の敗北条件（→ DefeatSystem）。
            result.NewDefeatReason = _defeatSystem.ProcessWeeklySettlement(state);
            result.Flags.DefeatOccurred = result.NewDefeatReason != null;

            state.WeekNumber++;

            // 新春採用試験（2年目以降の新年第1週のみ）。ゲームオーバー後は発生させない。
            result.Flags.RecruitmentTrialOccurred =
                state.DefeatReason == null && _recruitmentSystem.IsRecruitmentWeek(state.WeekNumber);

            // ランク昇格試験の提示（旧コアシステム刷新仕様 Phase 2）は2026年9月、
            // 大迷宮への一本化改訂で無効化した。旧昇格試験クエスト（「ゴブリンリーダー討伐」等）
            // はもう AvailableQuests へ追加されない＝週報の「📜 ギルド本部から昇格試験の通達が
            // 届いた」通知も出なくなる（→ 03 §0.4）。

            // 進行の節目（試験の提示・突破）はどちらもプレイヤーの判断を要するため自動スキップを止める。
            result.Flags.GuildProgressionEventOccurred =
                result.OfferedPromotionExam != null || result.PromotionExamResult != null;

            return result;
        }
    }
}
