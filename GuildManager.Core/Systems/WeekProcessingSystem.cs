using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 週次決算処理のオーケストレーション。仕様書 03 §1.3（自動スキップ）参照（v1.10改訂で新設）。
    ///
    /// 「大迷宮の出撃の解決→経済→訓練→回復→成長→満足度→加齢→施設→格付け→敗北判定」という
    /// 一連の週次決算処理を、GuildManager.Core側の1つのメソッドに集約する
    /// （→ 05技術メモ「GuildManager.CoreはGodotに依存しない」方針に沿う）。
    ///
    /// 出撃操作（どの部隊をどの任務へ送るか）は含まない。これはプレイヤーの意思決定であり、
    /// 自動スキップ中は一切行わないため、呼び出し側（UI）が出撃操作を済ませてから本メソッドを呼ぶ想定。
    /// 既に出撃中の部隊の進行（複数週潜行・扉前待機・決戦）は、本メソッドの中でそのまま行われる。
    ///
    /// 旧通常クエスト（掲示板・受託依頼）の解決・治安（脅威度）の増減・昇格試験は、
    /// 大迷宮への完全一本化（2026年9月）に伴い撤去した（→ 03 §4.0〜§4.4）。
    /// </summary>
    public class WeekProcessingSystem
    {
        private readonly GuildRankSystem _guildRankSystem;
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
        private readonly DungeonExpeditionSystem _dungeonExpeditionSystem;

        public WeekProcessingSystem(
            GuildRankSystem guildRankSystem,
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
            // 省略可能：大迷宮への出撃の週次解決（→ DungeonExpeditionSystem）。省略時は固定シードの
            // 既定構成を組み立てる（出撃が無ければ何も起きない）。
            DungeonExpeditionSystem? dungeonExpeditionSystem = null)
        {
            _guildRankSystem = guildRankSystem;
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
        /// 1週分の決算処理を実行し、週番号を1つ進める。出撃操作は本メソッドの対象外
        /// ＝呼び出し側が本メソッドを呼ぶ前に済ませておく。
        /// </summary>
        public WeeklySettlementResult ProcessWeek(GameState state)
        {
            var result = new WeeklySettlementResult();
            int thisWeek = state.WeekNumber;
            result.Flags.Week = thisWeek;

            bool wasFinalQuestUnlocked = state.FinalQuestUnlocked;
            var neededNegotiationBefore = state.Adventurers.Where(a => a.NeedsNegotiation).Select(a => a.Id).ToHashSet();

            // 出撃中（今週出発した分も含む）の冒険者は、HP自然回復・訓練場成長の対象から外す（→ 03 §4.0.1）。
            var dispatchedIds = state.Adventurers.Where(a => a.IsDispatched).Select(a => a.Id).ToHashSet();

            // 大迷宮への出撃（道中進軍・扉前待機・ボス討伐・採取・迷宮調査）を1週分進める
            // （→ DungeonExpeditionSystem）。潜行は複数週にわたって進み、未撃破ボスの扉前に
            // 着いたら判断待ちで止まる（→ 毎回1Fリセット・複数週潜行型）。扉前到達は自動スキップの停止条件。
            result.DungeonMissionResolutions.AddRange(_dungeonExpeditionSystem.ProcessWeeklyMissions(state));
            foreach (var resolution in result.DungeonMissionResolutions)
            {
                if (resolution.DungeonResult != null && resolution.DungeonResult.ForceRetiredAdventurerIds.Count > 0)
                    result.Flags.DeathOrPermanentInjuryOccurred = true;
                if (resolution.ArrivedAtBossDoor)
                    result.Flags.BossDoorReached = true;
            }

            // 出撃の有無にかかわらず、時間は必ず進む。
            _economySystem.ApplyWeeklyWages(state);

            // 月次助成金（4週に1回。→ 03 §8.1：ギルド格付け連動で満額支給）。
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

            _agingSystem.ProcessWeeklyAging(state); // → 03 §3：加齢・8年稼働モデル

            result.CompletedFacility = _facilitySystem.ProcessWeeklyConstruction(state); // → 03 §6.1：施設Lv投資
            result.Flags.FacilityConstructionCompleted = result.CompletedFacility != null;

            // ギルド格付け（→ 03 §8.1・§8.1.1）：名声自然減衰の判定と昇格・降格判定・Aランク到達
            // フラグ（→ GuildRankSystem.UpdateRank）は週次決算で1回だけ行う。
            // 「現ランク相当のクエスト達成」は旧通常クエストの撤去以降は発生しないため、常にfalseを渡す
            // （＝名声の自然減衰は撤去前と同じく毎週判定される。名声の加算は大迷宮のボス撃破報酬が担う）。
            result.RankChange = _guildRankSystem.ProcessWeeklySettlement(state, achievedRankAppropriateQuestThisWeek: false);
            result.Flags.FinalQuestNewlyUnlocked = !wasFinalQuestUnlocked && state.FinalQuestUnlocked;

            // 敗北条件判定（→ 03 §8.3）：週次決算の最後に1回だけ行う。
            // 破産（所持金マイナス4週連続、猶予あり）のみが唯一の敗北条件（→ DefeatSystem）。
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
