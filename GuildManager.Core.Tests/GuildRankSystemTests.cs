using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// ギルド格付けの降格・ヒステリシス・名声自然減衰（仕様書 03 §8.1・§8.1.1）のテスト。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class GuildRankSystemTests
    {
        // ---------------- 名声の加減算 ----------------

        [Fact]
        public void ApplyQuestResult_AddsReputation_OnAchievement()
        {
            var state = new GameState();
            var system = new GuildRankSystem();

            system.ApplyQuestResult(state, questAchieved: true);

            Assert.Equal(GuildRankBalance.ReputationGainOnAchievement, state.Reputation);
        }

        [Fact]
        public void ApplyQuestResult_SubtractsReputation_OnFailure()
        {
            var state = new GameState { Reputation = 100 };
            var system = new GuildRankSystem();

            system.ApplyQuestResult(state, questAchieved: false);

            Assert.Equal(100 - GuildRankBalance.ReputationLossOnFailure, state.Reputation);
        }

        [Fact]
        public void ApplyQuestResult_ClampsAtZero_OnRepeatedFailure()
        {
            var state = new GameState { Reputation = 5 };
            var system = new GuildRankSystem();

            system.ApplyQuestResult(state, questAchieved: false);

            Assert.Equal(0, state.Reputation);
        }

        // ---------------- 昇格・降格（ヒステリシス） ----------------

        [Fact]
        public void ProcessWeeklySettlement_Promotes_WhenReputationReachesNextRankThreshold()
        {
            var state = new GameState { Reputation = GuildRankBalance.GetThreshold(GuildRank.F).PromoteAt };
            var system = new GuildRankSystem();

            var change = system.ProcessWeeklySettlement(state, achievedRankAppropriateQuestThisWeek: true);

            Assert.NotNull(change);
            Assert.True(change!.IsPromotion);
            Assert.Equal(GuildRank.F, state.GuildRank);
        }

        [Fact]
        public void ProcessWeeklySettlement_PromotesMultipleRanks_WhenReputationJumpsFar()
        {
            // GからSまで一気に届くほどの名声。1週で複数ランク上がることを許容する設計。
            var state = new GameState { Reputation = GuildRankBalance.GetThreshold(GuildRank.S).PromoteAt };
            var system = new GuildRankSystem();

            system.ProcessWeeklySettlement(state, achievedRankAppropriateQuestThisWeek: true);

            Assert.Equal(GuildRank.S, state.GuildRank);
        }

        [Fact]
        public void ProcessWeeklySettlement_DoesNotChangeRank_WhenReputationStaysBetweenPromoteAndDemote()
        {
            // Fの降格ラインと、次(E)の昇格ラインの間の値。どちらの閾値も割らない。
            var promoteAtE = GuildRankBalance.GetThreshold(GuildRank.E).PromoteAt;
            var demoteAtF = GuildRankBalance.GetThreshold(GuildRank.F).DemoteAt;
            var state = new GameState { GuildRank = GuildRank.F, Reputation = (promoteAtE + demoteAtF) / 2 };
            var system = new GuildRankSystem();

            var change = system.ProcessWeeklySettlement(state, achievedRankAppropriateQuestThisWeek: true);

            Assert.Null(change);
            Assert.Equal(GuildRank.F, state.GuildRank);
        }

        [Fact]
        public void ProcessWeeklySettlement_Demotes_WhenReputationFallsBelowCurrentRankDemoteLine()
        {
            var state = new GameState
            {
                GuildRank = GuildRank.C,
                Reputation = GuildRankBalance.GetThreshold(GuildRank.C).DemoteAt - 1,
            };
            var system = new GuildRankSystem();

            var change = system.ProcessWeeklySettlement(state, achievedRankAppropriateQuestThisWeek: true);

            Assert.NotNull(change);
            Assert.False(change!.IsPromotion);
            Assert.Equal(GuildRank.D, state.GuildRank);
        }

        [Fact]
        public void ProcessWeeklySettlement_DoesNotDemoteBelowG()
        {
            var state = new GameState { GuildRank = GuildRank.G, Reputation = 0 };
            var system = new GuildRankSystem();

            var change = system.ProcessWeeklySettlement(state, achievedRankAppropriateQuestThisWeek: true);

            Assert.Null(change);
            Assert.Equal(GuildRank.G, state.GuildRank);
        }

        // ---------------- 名声自然減衰 ----------------

        [Fact]
        public void ProcessWeeklySettlement_ResetsCounter_WhenRankAppropriateQuestAchieved()
        {
            var state = new GameState { WeeksSinceLastRankAppropriateQuest = 3 };
            var system = new GuildRankSystem();

            system.ProcessWeeklySettlement(state, achievedRankAppropriateQuestThisWeek: true);

            Assert.Equal(0, state.WeeksSinceLastRankAppropriateQuest);
        }

        [Fact]
        public void ProcessWeeklySettlement_IncrementsCounter_WhenNotAchieved()
        {
            var state = new GameState { WeeksSinceLastRankAppropriateQuest = 0 };
            var system = new GuildRankSystem();

            system.ProcessWeeklySettlement(state, achievedRankAppropriateQuestThisWeek: false);

            Assert.Equal(1, state.WeeksSinceLastRankAppropriateQuest);
        }

        [Fact]
        public void ProcessWeeklySettlement_DoesNotDecayReputation_BeforeThresholdWeeksReached()
        {
            var state = new GameState { Reputation = 100, WeeksSinceLastRankAppropriateQuest = GuildRankBalance.WeeksWithoutAppropriateQuestThreshold - 2 };
            var system = new GuildRankSystem();

            system.ProcessWeeklySettlement(state, achievedRankAppropriateQuestThisWeek: false);

            Assert.Equal(100, state.Reputation);
        }

        [Fact]
        public void ProcessWeeklySettlement_DecaysReputation_OnceThresholdWeeksReached()
        {
            var state = new GameState { Reputation = 100, WeeksSinceLastRankAppropriateQuest = GuildRankBalance.WeeksWithoutAppropriateQuestThreshold - 1 };
            var system = new GuildRankSystem();

            system.ProcessWeeklySettlement(state, achievedRankAppropriateQuestThisWeek: false);

            Assert.Equal(100 - GuildRankBalance.ReputationDecayPerWeek, state.Reputation);
        }

        [Fact]
        public void ProcessWeeklySettlement_ContinuesDecayingEveryWeek_WhileStillUnachieved()
        {
            var state = new GameState { Reputation = 100, WeeksSinceLastRankAppropriateQuest = GuildRankBalance.WeeksWithoutAppropriateQuestThreshold };
            var system = new GuildRankSystem();

            system.ProcessWeeklySettlement(state, achievedRankAppropriateQuestThisWeek: false);

            Assert.Equal(100 - GuildRankBalance.ReputationDecayPerWeek, state.Reputation);
        }

        [Fact]
        public void ProcessWeeklySettlement_DecayClampsAtZero()
        {
            var state = new GameState { Reputation = 2, WeeksSinceLastRankAppropriateQuest = GuildRankBalance.WeeksWithoutAppropriateQuestThreshold };
            var system = new GuildRankSystem();

            system.ProcessWeeklySettlement(state, achievedRankAppropriateQuestThisWeek: false);

            Assert.Equal(0, state.Reputation);
        }

        // ---------------- Aランク到達フラグ（→ 03 §8.2、v1.10改訂） ----------------

        [Fact]
        public void ProcessWeeklySettlement_SetsFinalQuestUnlocked_WhenNewlyReachingRankA()
        {
            var state = new GameState { GuildRank = GuildRank.B, Reputation = GuildRankBalance.GetThreshold(GuildRank.A).PromoteAt };
            var system = new GuildRankSystem();

            system.ProcessWeeklySettlement(state, achievedRankAppropriateQuestThisWeek: true);

            Assert.Equal(GuildRank.A, state.GuildRank);
            Assert.True(state.FinalQuestUnlocked);
        }

        [Fact]
        public void ProcessWeeklySettlement_DoesNotSetFinalQuestUnlocked_WhenBelowRankA()
        {
            var state = new GameState { GuildRank = GuildRank.C, Reputation = GuildRankBalance.GetThreshold(GuildRank.B).PromoteAt };
            var system = new GuildRankSystem();

            system.ProcessWeeklySettlement(state, achievedRankAppropriateQuestThisWeek: true);

            Assert.Equal(GuildRank.B, state.GuildRank);
            Assert.False(state.FinalQuestUnlocked);
        }

        [Fact]
        public void ProcessWeeklySettlement_KeepsFinalQuestUnlocked_AfterDemotingBelowRankA()
        {
            // 一度Aランクに到達しフラグが立った後、降格してAランクを割り込んでも
            // フラグは取り消さない（→ 03 §8.2「一度依頼が来た、という既成事実は残る」）。
            var state = new GameState { GuildRank = GuildRank.A, FinalQuestUnlocked = true, Reputation = 0 };
            var system = new GuildRankSystem();

            system.ProcessWeeklySettlement(state, achievedRankAppropriateQuestThisWeek: false);

            Assert.True(state.GuildRank < GuildRank.A); // 名声0まで降格したはず
            Assert.True(state.FinalQuestUnlocked); // フラグは取り消されない
        }

        [Fact]
        public void ProcessWeeklySettlement_ReachingRankAAgain_DoesNotReprocess_WhenAlreadyUnlocked()
        {
            // 再度A未満からA以上に昇格しても、既にフラグが立っていれば何もしない
            // （＝例外や不整合が起きず、trueのまま安定していることを確認する）。
            var state = new GameState { GuildRank = GuildRank.B, FinalQuestUnlocked = true, Reputation = GuildRankBalance.GetThreshold(GuildRank.A).PromoteAt };
            var system = new GuildRankSystem();

            system.ProcessWeeklySettlement(state, achievedRankAppropriateQuestThisWeek: true);

            Assert.Equal(GuildRank.A, state.GuildRank);
            Assert.True(state.FinalQuestUnlocked);
        }

        [Fact]
        public void ProcessWeeklySettlement_SetsFinalQuestUnlocked_WhenReachingRankSDirectly()
        {
            // A到達を経由せずS到達（一気に複数ランク昇格）した場合でも、A以上である以上フラグは立つ。
            var state = new GameState { GuildRank = GuildRank.G, Reputation = GuildRankBalance.GetThreshold(GuildRank.S).PromoteAt };
            var system = new GuildRankSystem();

            system.ProcessWeeklySettlement(state, achievedRankAppropriateQuestThisWeek: true);

            Assert.Equal(GuildRank.S, state.GuildRank);
            Assert.True(state.FinalQuestUnlocked);
        }

        // ---------------- ランク⇔クエストランク変換 ----------------

        [Theory]
        [InlineData(GuildRank.G, QuestRank.E)]
        [InlineData(GuildRank.F, QuestRank.E)]
        [InlineData(GuildRank.E, QuestRank.E)]
        [InlineData(GuildRank.D, QuestRank.D)]
        [InlineData(GuildRank.S, QuestRank.S)]
        public void ToQuestRankFloor_MapsGuildRankToComparableQuestRank(GuildRank guildRank, QuestRank expected)
        {
            Assert.Equal(expected, GuildRankBalance.ToQuestRankFloor(guildRank));
        }
    }
}
