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

        /// <summary>契約交渉の猶予切れで契約解除された冒険者一覧（→ SatisfactionSystem.ProcessWeeklyNegotiation）。</summary>
        public List<Adventurer> NegotiationTerminated { get; } = new();

        /// <summary>アルベールの市販薬・内職売上（4週に1回のみ値が入る。→ EconomySystem.ProcessWeeklySideJobIncome）。</summary>
        public SideJobIncome? SideJobIncome { get; set; }

        /// <summary>今週のマスターの機嫌の変動（→ MasterMoodSystem.ProcessWeeklyMood）。</summary>
        public MasterMoodReport MoodReport { get; set; } = new();

        /// <summary>今週完成した施設（無ければnull）。</summary>
        public Facility? CompletedFacility { get; set; }

        /// <summary>今週新たに確定した敗北理由（無ければnull）。</summary>
        public DefeatReason? NewDefeatReason { get; set; }
    }
}
