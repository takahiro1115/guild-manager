using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 週次決算処理のオーケストレーション。仕様書 03 §1.3（自動スキップ）参照（v1.10改訂で新設）。
    ///
    /// 「大迷宮の出撃の解決→マスターの機嫌→経済（週給・内職売上）→訓練→回復→成長→満足度→加齢→施設→敗北判定」という
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
        private readonly MasterMoodSystem _masterMoodSystem;
        private readonly EconomySystem _economySystem;
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
            MasterMoodSystem masterMoodSystem,
            EconomySystem economySystem,
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
            _masterMoodSystem = masterMoodSystem;
            _economySystem = economySystem;
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

            // マスターの機嫌（→ 03 §8.1・§8.1.1）：大迷宮での成果で上げ、成果ゼロなら退屈減衰。
            // 内職売上の倍率は「決算時点の機嫌」で決まるため、内職売上より先に済ませる。
            result.MoodReport = _masterMoodSystem.ProcessWeeklyMood(state, result.DungeonMissionResolutions);

            // 出撃の有無にかかわらず、時間は必ず進む。
            _economySystem.ApplyWeeklyWages(state);

            // アルベールの市販薬・内職売上（4週に1回。→ 03 §8.1：機嫌に応じた倍率。旧・月次助成金）。
            result.SideJobIncome = _economySystem.ProcessWeeklySideJobIncome(state);

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

            int moodBeforeAging = state.MasterMood;
            _agingSystem.ProcessWeeklyAging(state); // → 03 §3：加齢・8年稼働モデル
            // 満期引退で退職金を払いきれなかった場合の機嫌低下（→ AgingSystem.Retire）も週報の内訳に載せる。
            if (state.MasterMood != moodBeforeAging)
                result.MoodReport.Entries.Add(new MasterMoodEntry("退職金の不足", state.MasterMood - moodBeforeAging, state.MasterMood - moodBeforeAging));
            result.MoodReport.MoodAfter = state.MasterMood;

            result.CompletedFacility = _facilitySystem.ProcessWeeklyConstruction(state); // → 03 §6.1：施設Lv投資
            result.Flags.FacilityConstructionCompleted = result.CompletedFacility != null;

            // 最終討伐クエストの解禁（→ 03 §8.2。最終フィールドの100Fボス撃破で立つ）。
            // 名声・ギルド格付けは2026年9月に廃止（→ マスターの機嫌、MasterMoodSystem）。
            result.Flags.FinalQuestNewlyUnlocked = !wasFinalQuestUnlocked && state.FinalQuestUnlocked;

            // 敗北条件判定（→ 03 §8.3）：週次決算の最後に1回だけ行う。
            // 副官解雇（マスターの機嫌0、即時）と破産（所持金マイナス4週連続、猶予あり）（→ DefeatSystem）。
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
