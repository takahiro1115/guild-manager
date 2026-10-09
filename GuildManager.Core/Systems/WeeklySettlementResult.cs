using System.Collections.Generic;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 1週分の週次決算処理（WeekProcessingSystem.ProcessWeek）の詳細な結果。
    /// 仕様書 03 §1.3（自動スキップ）参照。
    ///
    /// 設計メモ（項目56）：指示書の`WeekResult`はブール値フラグのみを持つ設計だったが、
    /// それだけでは手動「次週へ進める」の詳細な週報ログ（誰が成長したか・どのクエストが
    /// 解決したか等）を再現できない。かといって毎週の処理を「手動フロー用」「自動スキップ用」
    /// の2箇所に重複実装すると、挙動が食い違うリスクがある。そのため、詳細情報を保持する
    /// この`WeeklySettlementResult`を新設し、`Flags`（=WeekResult、自動スキップの停止判定用）
    /// と詳細データの両方を1回の処理で同時に生成する設計にした。手動フロー・自動スキップの
    /// どちらも`WeekProcessingSystem.ProcessWeek`という単一の実装を経由する
    /// （→ AutoSkipServiceは`Flags`だけを取り出して`List&lt;WeekResult&gt;`として返す）。
    /// </summary>
    public class WeeklySettlementResult
    {
        /// <summary>自動スキップの停止判定に使うフラグ一式。</summary>
        public WeekResult Flags { get; } = new();

        /// <summary>
        /// 大迷宮への出撃（調査任務・ボス討伐）の解決結果一覧（→ DungeonExpeditionSystem.ProcessWeeklyMissions）。
        /// </summary>
        public List<DungeonMissionResolution> DungeonMissionResolutions { get; } = new();

        /// <summary>訓練場配置による成長イベント一覧（→ GrowthSystem.ProcessTrainingGrowth）。</summary>
        public List<GrowthEvent> TrainingGrowthEvents { get; } = new();

        /// <summary>
        /// 教官からの特性伝授が成立した一覧（→ TrainingSystem.ProcessWeeklyTraitTransmission、
        /// 特性伝授・スロット上限刷新仕様）。
        /// </summary>
        public List<TraitTransmissionEvent> TraitTransmissionEvents { get; } = new();

        /// <summary>任務の外で後天的に付いた特性（燃え尽き、→ SatisfactionSystem.ProcessWeeklyBurnout、03 §0.56）。任務中のものは DungeonMissionResolutions 側。</summary>
        public List<TraitGrantEvent> TraitGrantEvents { get; } = new();

        /// <summary>契約交渉の猶予切れで契約解除された冒険者一覧（→ SatisfactionSystem.ProcessWeeklyNegotiation）。</summary>
        public List<Adventurer> NegotiationTerminated { get; } = new();

        /// <summary>アルベールの市販薬・内職売上（4週に1回のみ値が入る。→ EconomySystem.ProcessWeeklySideJobIncome）。</summary>
        public SideJobIncome? SideJobIncome { get; set; }

        /// <summary>今週のマスターの機嫌の変動（→ MasterMoodSystem.ProcessWeeklyMood）。</summary>
        public MasterMoodReport MoodReport { get; set; } = new();

        /// <summary>今週の依頼の達成・失敗（→ CommissionSystem.ProcessSettlement、§0.64）。</summary>
        public CommissionSettlement Commissions { get; set; } = new();

        /// <summary>決算後の新しい週に届いた依頼・予告された異変・期限の近い依頼（→ CommissionSystem.ProcessNewWeek、§0.64）。</summary>
        public CommissionArrivals Arrivals { get; set; } = new();

        /// <summary>今週、研究を手伝った冒険者（→ IdleActivitySystem.ProcessWeek、03 §8.1・§0.73）。対象0名なら空。</summary>
        public List<IdleHelpEntry> IdleHelpEntries { get; } = new();

        /// <summary>今週、自主練をした人数（→ IdleActivitySystem、§0.73）。</summary>
        public int SelfTrainerCount { get; set; }

        /// <summary>今週の自主練による成長（→ GrowthSystem.ProcessSelfTraining、§0.73）。</summary>
        public List<GrowthEvent> SelfTrainingGrowthEvents { get; } = new();

        /// <summary>今週、培養槽から誕生した子（→ SoulFusionSystem.ProcessWeeklyCultures、03 §5.4・§0.58）。子はロースターに加わっている。</summary>
        public List<SoulFusionCulture> SoulFusionBirths { get; } = new();

        /// <summary>今週完成した施設（無ければnull）。</summary>
        public Facility? CompletedFacility { get; set; }

        /// <summary>今週、大会などのご褒美で開いた施設（演出の型と台詞つき。→ FacilityUnlockSystem、§0.82。§0.79の上限Lvを置き換え）。</summary>
        public List<FacilityUnlockNotice> FacilityUnlocks { get; } = new();

        /// <summary>今週行った大会（結果つき。→ TournamentSystem.ResolveWeek、§0.82）。</summary>
        public List<TournamentEvent> TournamentsResolved { get; } = new();

        /// <summary>今週（年の最後の週）確定した年末のギルドの順位表（→ GuildStandingSystem、§0.94）。ほかの週は null。</summary>
        public GuildStanding? YearStanding { get; set; }

        /// <summary>今週届いた招待大会の予告（→ TournamentSystem.CheckInvitations、§0.82）。</summary>
        public List<TournamentEvent> TournamentInvitations { get; } = new();

        /// <summary>決算のあとの新しい週に、記憶から蘇った主（クリアのあと、§0.90）。</summary>
        public List<FloorBoss> RevivedBosses { get; } = new();

        /// <summary>今週、依頼の派遣から帰ってきた冒険者（成長と特性つき。→ CommissionSystem.ProcessLoanReturns、§0.85）。</summary>
        public List<LoanReturn> LoanReturns { get; } = new();

        /// <summary>今週、イザベラが来訪した（森の40Fのボスを初めて倒した。次の月に最初の交流戦、§0.84）。</summary>
        public bool IsabellaVisited { get; set; }

        /// <summary>今週行った交流戦（→ IsabellaSystem.ResolveWeek、§0.84）。</summary>
        public List<ExchangeOutcome> ExchangeMatches { get; } = new();

        /// <summary>今週の派遣の教官の出入り（→ IsabellaSystem.ProcessGuestTrainer、§0.84）。</summary>
        public GuestTrainerChange GuestTrainer { get; set; } = new();

        /// <summary>今週、初めて入賞して依頼が届くようになった（次の季節のはじめから、§0.84）。</summary>
        public bool CommissionsUnlocked { get; set; }

        /// <summary>今週の栄誉の知らせ（新しい二つ名・殿堂入り・母娘の出撃。→ HonorSystem.ProcessWeek、大会と育成の栄光 段2）。</summary>
        public List<HonorNotice> HonorNotices { get; } = new();

        /// <summary>今週、満期で引退した冒険者のId（引退式の小窓を出す。→ HonorSystem.BuildCeremony）。</summary>
        public List<System.Guid> Retirees { get; } = new();

        /// <summary>今週、最初の引退者が出て、作戦資料室・冒険者支援室が建てられるようになったか（§0.78・§0.79）。</summary>
        public bool AdvisorFacilitiesOpened { get; set; }

        /// <summary>
        /// 現役の冒険者ごとの今週の過ごし方（出撃・訓練・静養・研究の手伝い・自主練。決算のはじめ、HPが動く前に決める。
        /// → IdleActivitySystem.GetWeekActivity）。月報の「冒険者ごと」に使う（§0.81）。
        /// </summary>
        public Dictionary<System.Guid, WeekActivity> Activities { get; } = new();

        /// <summary>今週新たに確定した敗北理由（無ければnull）。</summary>
        public DefeatReason? NewDefeatReason { get; set; }
    }
}
