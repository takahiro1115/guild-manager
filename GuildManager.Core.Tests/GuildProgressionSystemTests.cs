using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 序盤のギルド進行管理（ランク昇格試験と第2部隊枠の開放）のテスト。
    /// → コアシステム刷新仕様「4. 進行管理（ランク昇格と第2パーティ開放）」。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class GuildProgressionSystemTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private static GuildProgressionSystem BuildSystem() =>
            new GuildProgressionSystem(new RecruitmentSystem(new AlwaysMinRng()));

        /// <summary>昇格試験の提示条件（出撃回数・資金）を満たした状態を作る。</summary>
        private static GameState MakeEligibleState() => new GameState
        {
            TotalDispatchCount = ProgressionBalance.PromotionExamMinDispatchCount,
            Gold = ProgressionBalance.PromotionExamMinGold,
        };

        // ---------------- Phase 1：初期状態 ----------------

        [Fact]
        public void NewGame_StartsWithSingleSquadSlot()
        {
            // Phase 1：同時出撃枠は1（1枠の中で1〜4名の分割は自由）。
            Assert.Equal(ProgressionBalance.InitialSquadSlots, new GameState().UnlockedSquadSlots);
            Assert.Equal(1, new GameState().UnlockedSquadSlots);
        }

        [Fact]
        public void GetPhase_ReturnsFoundation_ForNewGame()
        {
            Assert.Equal(GuildProgressionPhase.Foundation, BuildSystem().GetPhase(new GameState()));
        }

        // ---------------- Phase 2：昇格試験の提示条件 ----------------

        [Fact]
        public void IsPromotionExamAvailable_False_WhenNotEnoughDispatches()
        {
            var state = MakeEligibleState();
            state.TotalDispatchCount = ProgressionBalance.PromotionExamMinDispatchCount - 1;

            Assert.False(BuildSystem().IsPromotionExamAvailable(state));
        }

        [Fact]
        public void IsPromotionExamAvailable_False_WhenNotEnoughGold()
        {
            var state = MakeEligibleState();
            state.Gold = ProgressionBalance.PromotionExamMinGold - 1;

            Assert.False(BuildSystem().IsPromotionExamAvailable(state));
        }

        [Fact]
        public void IsPromotionExamAvailable_True_WhenBothConditionsMet()
        {
            Assert.True(BuildSystem().IsPromotionExamAvailable(MakeEligibleState()));
        }

        [Fact]
        public void TryOfferPromotionExam_AddsBossQuestToAvailableQuests()
        {
            var state = MakeEligibleState();

            var quest = BuildSystem().TryOfferPromotionExam(state);

            Assert.NotNull(quest);
            Assert.True(quest!.IsBoss);
            Assert.Contains(quest, state.AvailableQuests);
            Assert.Equal(ProgressionBalance.PromotionExamQuestName, quest.Name);
            Assert.Equal(QuestType.Subjugation, quest.QuestType);
            Assert.Equal(1, quest.DurationWeeks); // 決戦は出撃したその週に決着する（テンポ優先）
        }

        [Fact]
        public void TryOfferPromotionExam_OffersOnlyOnce()
        {
            var state = MakeEligibleState();
            var system = BuildSystem();
            system.TryOfferPromotionExam(state);

            var second = system.TryOfferPromotionExam(state);

            Assert.Null(second);
            Assert.Single(state.AvailableQuests, q => q.IsBoss);
        }

        [Fact]
        public void GetPhase_ReturnsExamOffered_AfterOffering()
        {
            var state = MakeEligibleState();
            var system = BuildSystem();
            system.TryOfferPromotionExam(state);

            Assert.Equal(GuildProgressionPhase.ExamOffered, system.GetPhase(state));
        }

        [Fact]
        public void GetPhase_ReturnsExamInProgress_WhileBossQuestIsDispatched()
        {
            var state = MakeEligibleState();
            var system = BuildSystem();
            var quest = system.TryOfferPromotionExam(state)!;
            state.ActiveDispatches.Add(new ActiveDispatch { Quest = quest, WeeksRemaining = 1 });

            Assert.Equal(GuildProgressionPhase.ExamInProgress, system.GetPhase(state));
        }

        // ---------------- Phase 4：突破による第2部隊枠の開放 ----------------

        [Fact]
        public void ApplyPromotionIfExamCleared_UnlocksSecondSquadSlotAndPromotesToRankE()
        {
            var state = MakeEligibleState();
            var system = BuildSystem();
            var quest = system.TryOfferPromotionExam(state)!;
            int goldBefore = state.Gold;

            var result = system.ApplyPromotionIfExamCleared(state, quest, questAchieved: true);

            Assert.NotNull(result);
            Assert.Equal(GuildRank.E, state.GuildRank);
            Assert.Equal(ProgressionBalance.SquadSlotsAfterPromotionExam, state.UnlockedSquadSlots);
            Assert.Equal(2, state.UnlockedSquadSlots);
            Assert.Equal(goldBefore + ProgressionBalance.PromotionExamRewardGold, state.Gold);
            Assert.True(state.PromotionExamPassed);
            Assert.Equal(GuildProgressionPhase.Expanded, system.GetPhase(state));
        }

        [Fact]
        public void ApplyPromotionIfExamCleared_OffersNewHires()
        {
            // 第2部隊を編成できるよう、酒場へ新人が補充される（雇うかはプレイヤーの判断）。
            var state = MakeEligibleState();
            var system = BuildSystem();
            var quest = system.TryOfferPromotionExam(state)!;

            var result = system.ApplyPromotionIfExamCleared(state, quest, questAchieved: true);

            Assert.Equal(ProgressionBalance.PromotionExamNewHireCount, result!.NewHireOffers.Count);
            Assert.Equal(4, result.NewHireOffers.Count);
        }

        [Fact]
        public void ApplyPromotionIfExamCleared_ReturnsNull_WhenExamFailed()
        {
            var state = MakeEligibleState();
            var system = BuildSystem();
            var quest = system.TryOfferPromotionExam(state)!;

            var result = system.ApplyPromotionIfExamCleared(state, quest, questAchieved: false);

            Assert.Null(result);
            Assert.False(state.PromotionExamPassed);
            Assert.Equal(1, state.UnlockedSquadSlots); // 枠は拡張されないまま
        }

        [Fact]
        public void ApplyPromotionIfExamCleared_IgnoresNonBossQuests()
        {
            var state = MakeEligibleState();
            var normalQuest = new Quest { QuestType = QuestType.Gathering, IsBoss = false };

            var result = BuildSystem().ApplyPromotionIfExamCleared(state, normalQuest, questAchieved: true);

            Assert.Null(result);
            Assert.False(state.PromotionExamPassed);
        }

        [Fact]
        public void ApplyPromotionIfExamCleared_AppliesOnlyOnce()
        {
            var state = MakeEligibleState();
            var system = BuildSystem();
            var quest = system.TryOfferPromotionExam(state)!;
            system.ApplyPromotionIfExamCleared(state, quest, true);
            int goldAfterFirst = state.Gold;

            var second = system.ApplyPromotionIfExamCleared(state, quest, true);

            Assert.Null(second);
            Assert.Equal(goldAfterFirst, state.Gold); // 報奨金の二重取りが起きない
        }

        [Fact]
        public void PromotedRank_SurvivesTheSameWeeksReputationSettlement()
        {
            // 実装中に検出した統合バグの回帰テスト：
            // ランク（GuildRank）は本来「名声から導出される」値であり、週次決算で毎週
            // 降格判定が走る。昇格試験でランクだけを書き換えていた当初の実装では、
            // 突破したその週の決算で名声不足と判定され、即座にFへ引き戻されていた。
            var state = MakeEligibleState();
            var system = BuildSystem();
            var quest = system.TryOfferPromotionExam(state)!;

            system.ApplyPromotionIfExamCleared(state, quest, questAchieved: true);
            new GuildRankSystem().ProcessWeeklySettlement(state, achievedRankAppropriateQuestThisWeek: true);

            Assert.Equal(GuildRank.E, state.GuildRank);
        }

        [Fact]
        public void SquadSlots_StayExpanded_EvenIfRankLaterDrops()
        {
            // 開放された同時出撃枠はランクとは独立した進行状態であり、
            // 後から名声が落ちてランクが下がっても取り消されない（プレイヤーの到達を巻き戻さない）。
            var state = MakeEligibleState();
            var system = BuildSystem();
            var quest = system.TryOfferPromotionExam(state)!;
            system.ApplyPromotionIfExamCleared(state, quest, questAchieved: true);

            state.Reputation = 0; // 長期の不活動で名声が尽きた想定
            new GuildRankSystem().ProcessWeeklySettlement(state, achievedRankAppropriateQuestThisWeek: false);

            Assert.Equal(2, state.UnlockedSquadSlots);
        }

        [Fact]
        public void ApplyPromotionIfExamCleared_DoesNotDemote_WhenRankIsAlreadyHigher()
        {
            // 名声（GuildRankSystem）で既にE以上まで上がっている場合、試験突破で引き下げない。
            var state = MakeEligibleState();
            state.GuildRank = GuildRank.C;
            var system = BuildSystem();
            var quest = system.TryOfferPromotionExam(state)!;

            system.ApplyPromotionIfExamCleared(state, quest, true);

            Assert.Equal(GuildRank.C, state.GuildRank);
        }

        // ---------------- 同時出撃枠の制限（→ QuestDispatchSystem） ----------------

        [Fact]
        public void CanDispatch_False_WhenAllSquadSlotsAreBusy()
        {
            var state = new GameState(); // 枠1
            state.ActiveDispatches.Add(new ActiveDispatch());

            Assert.False(QuestDispatchSystem.CanDispatch(state));
        }

        [Fact]
        public void CanDispatch_True_AfterSlotsExpandToTwo()
        {
            var state = new GameState { UnlockedSquadSlots = 2 };
            state.ActiveDispatches.Add(new ActiveDispatch());

            Assert.True(QuestDispatchSystem.CanDispatch(state));
        }

        // ---------------- セーブ/ロードでの進行状態の保持 ----------------

        [Fact]
        public void ProgressionState_SurvivesSaveAndLoad()
        {
            var state = MakeEligibleState();
            var system = BuildSystem();
            var quest = system.TryOfferPromotionExam(state)!;
            system.ApplyPromotionIfExamCleared(state, quest, true);

            var restored = GameState.FromSaveData(state.ToSaveData());

            Assert.Equal(2, restored.UnlockedSquadSlots);
            Assert.True(restored.PromotionExamPassed);
            Assert.True(restored.PromotionExamOffered);
            Assert.Equal(state.TotalDispatchCount, restored.TotalDispatchCount);
        }

        [Fact]
        public void LoadingOldSaveWithoutSquadSlots_FallsBackToInitialValue()
        {
            // 本フィールドを持たない旧セーブ（値0）でも、派遣不能な状態にはならない。
            var data = new GameState().ToSaveData();
            data.UnlockedSquadSlots = 0;

            var restored = GameState.FromSaveData(data);

            Assert.Equal(ProgressionBalance.InitialSquadSlots, restored.UnlockedSquadSlots);
        }
    }
}
