using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 序盤のギルド進行管理（→ コアシステム刷新仕様「4. 進行管理（ランク昇格と第2パーティ開放）」）。
    ///
    /// 狙い：プレイ開始40〜60分（4〜5回目の出撃）で「第2部隊枠の開放」という明確な達成感に
    /// 到達させ、低リスク採取を延々と繰り返す作業感（グラインド）から早期に脱出させる。
    ///
    /// 進行：
    ///  - Phase 1（Foundation）：同時出撃枠1。採取・巡回で資金と経験を貯める。
    ///  - Phase 2（ExamOffered）：累計出撃回数と資金が基準に達すると、受注可能一覧へ
    ///    ランクE昇格試験（ボス討伐）が出現する。
    ///  - Phase 3（ExamInProgress）：昇格試験へ出撃中。UI側はログをステップ再生する。
    ///  - Phase 4（Expanded）：突破でランクE・同時出撃枠2・昇格報奨金・新人4名の補充。
    ///
    /// 既存のギルド格付け（→ GuildRankSystem、名声ベースの昇格・降格）とは**別経路**である。
    /// 昇格試験は「名声が溜まるのを待たずに、プレイヤーの実力で早期にEへ上がる」ための
    /// ショートカットとして機能する。名声側のロジックには手を入れず、試験突破時に
    /// ランクを直接引き上げる（既にE以上なら据え置き＝降格させない）。
    ///
    /// **2026年9月、大迷宮一本化改訂により、実運用では無効化した。**
    /// `WeekProcessingSystem` からはこのクラスの `TryOfferPromotionExam`・
    /// `ApplyPromotionIfExamCleared` をもう呼ばない。ランク昇格・出撃枠拡張は
    /// `DungeonExpeditionSystem.ApplyFieldProgression`（森10F/20Fボス撃破）のみを
    /// 唯一のトリガーとする。このクラス自体は削除していない：既存セーブの
    /// `PromotionExamOffered`/`PromotionExamPassed` フィールドをそのまま読めるようにするため、
    /// またクラス単体のロジック（下記メソッド群）は引き続きテストで検証されている。
    /// </summary>
    public class GuildProgressionSystem
    {
        private readonly RecruitmentSystem _recruitmentSystem;

        public GuildProgressionSystem(RecruitmentSystem recruitmentSystem)
        {
            _recruitmentSystem = recruitmentSystem;
        }

        /// <summary>現在の進行フェーズ（GameStateの条件値から導出する）。</summary>
        public GuildProgressionPhase GetPhase(GameState state)
        {
            if (state.PromotionExamPassed)
                return GuildProgressionPhase.Expanded;

            if (state.ActiveDispatches.Any(d => d.Quest.IsBoss))
                return GuildProgressionPhase.ExamInProgress;

            if (state.PromotionExamOffered)
                return GuildProgressionPhase.ExamOffered;

            return GuildProgressionPhase.Foundation;
        }

        /// <summary>
        /// 昇格試験を提示できる状態か（→ Phase 2の発生条件：累計出撃回数と資金）。
        /// 既に提示済み・突破済みの場合はfalse（同じ試験を何度も出さない）。
        /// </summary>
        public bool IsPromotionExamAvailable(GameState state)
        {
            if (state.PromotionExamPassed || state.PromotionExamOffered)
                return false;

            return state.TotalDispatchCount >= ProgressionBalance.PromotionExamMinDispatchCount
                && state.Gold >= ProgressionBalance.PromotionExamMinGold;
        }

        /// <summary>
        /// 条件を満たしていれば、昇格試験クエストを受注可能一覧へ追加する。
        /// 追加したクエストを返す（条件未達・提示済みならnull）。週次決算から毎週呼ぶ想定
        /// （→ WeekProcessingSystem）。
        ///
        /// 提示は一度きり：期限切れで一覧から消えた場合も再提示しない
        /// （何度でも再挑戦できると「昇格試験」という節目の重みが失われるため。
        /// 詰み防止の観点では、名声による通常のランク昇格経路が別途残っている）。
        /// </summary>
        public Quest? TryOfferPromotionExam(GameState state)
        {
            if (!IsPromotionExamAvailable(state))
                return null;

            var quest = CreatePromotionExamQuest();
            state.AvailableQuests.Add(quest);
            state.PromotionExamOffered = true;
            return quest;
        }

        /// <summary>
        /// 昇格試験クエストの実体（→ ProgressionBalance。値はすべてprogression.csv由来）。
        /// 拘束1週（Scale=Small）にしてあるのは、決戦の結果を出撃したその週に見せて
        /// テンポを保つため（→ 仕様「非同期・自動化のテンポ」）。
        /// </summary>
        public static Quest CreatePromotionExamQuest() => new Quest
        {
            Name = ProgressionBalance.PromotionExamQuestName,
            QuestType = QuestType.Subjugation,
            Rank = QuestRank.E,
            IsBoss = true,
            Difficulty = ProgressionBalance.PromotionExamDifficulty,
            ScoutRequirement = ProgressionBalance.PromotionExamScoutRequirement,
            RewardGold = ProgressionBalance.PromotionExamRewardQuestGold,
            DeadlineWeeks = ProgressionBalance.PromotionExamDeadlineWeeks,
            RecommendedMembers = ProgressionBalance.PromotionExamRecommendedMembers,
            Scale = QuestScale.Small,
        };

        /// <summary>
        /// 昇格試験の結果を反映する（→ Phase 4）。突破していれば、
        /// ランクE昇格・同時出撃枠の拡張・昇格報奨金・新人の補充をまとめて行う。
        ///
        /// 対象外（ボス以外のクエスト・失敗・突破済み）の場合は何もせずnullを返す。
        /// クエスト解決のたびに呼んでよい（→ WeekProcessingSystem）。
        /// </summary>
        public PromotionExamResult? ApplyPromotionIfExamCleared(GameState state, Quest quest, bool questAchieved)
        {
            if (!quest.IsBoss || !questAchieved || state.PromotionExamPassed)
                return null;

            state.PromotionExamPassed = true;

            // 既にE以上まで名声で上がっている場合は据え置く（試験突破で降格させない）。
            if (state.GuildRank < GuildRank.E)
                state.GuildRank = GuildRank.E;

            // 名声（→ GuildRankSystem）との整合：ランクは本来「名声から導出される」値であり、
            // 週次決算の降格判定（名声がDemoteAtを下回るとランクを下げる）が毎週走る。
            // そのためランクだけを書き換えると、昇格したその週の決算で名声不足と判定され、
            // 即座にFへ引き戻されてしまう（実装中にテストで検出した）。
            // 昇格試験の突破は「Eランク相当の実績を挙げた」ことの証明なので、名声そのものを
            // Eの昇格ラインまで引き上げ、単一の真実（名声→ランク）を保ったまま昇格を成立させる。
            // 以後も名声を維持できなければ通常どおり降格しうるが、開放された同時出撃枠
            // （UnlockedSquadSlots）はランクとは独立しているため取り消されない。
            state.Reputation = Math.Max(state.Reputation, GuildRankBalance.GetThreshold(GuildRank.E).PromoteAt);

            // 枠も同様に、既により多く開放されていれば減らさない。
            state.UnlockedSquadSlots = Math.Max(state.UnlockedSquadSlots, ProgressionBalance.SquadSlotsAfterPromotionExam);

            state.Gold += ProgressionBalance.PromotionExamRewardGold;

            // 第2部隊を編成できるよう、酒場へ新人を補充する（採用するかはプレイヤーの判断）。
            var offers = _recruitmentSystem.GenerateCandidates(
                state, candidateCount: ProgressionBalance.PromotionExamNewHireCount);

            return new PromotionExamResult
            {
                NewRank = state.GuildRank,
                UnlockedSquadSlots = state.UnlockedSquadSlots,
                RewardGold = ProgressionBalance.PromotionExamRewardGold,
                NewHireOffers = offers,
            };
        }
    }
}
