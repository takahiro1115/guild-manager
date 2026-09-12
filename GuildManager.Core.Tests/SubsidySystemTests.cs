using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 月次助成金（Subsidy）のテスト（仕様書 03 §8.1・§4.4）。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class SubsidySystemTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(5)]
        public void ProcessWeeklySubsidy_DoesNothing_OnNonMonthBoundaryWeeks(int weekNumber)
        {
            var state = new GameState { WeekNumber = weekNumber, Gold = 1000 };
            var system = new SubsidySystem();

            var amount = system.ProcessWeeklySubsidy(state);

            Assert.Null(amount);
            Assert.Equal(1000, state.Gold);
        }

        [Theory]
        [InlineData(4)]
        [InlineData(8)]
        [InlineData(48)]
        public void ProcessWeeklySubsidy_GrantsSubsidy_OnMonthBoundaryWeeks(int weekNumber)
        {
            var state = new GameState { WeekNumber = weekNumber, Gold = 1000, GuildRank = GuildRank.G, ThreatLevel = 0 };
            var system = new SubsidySystem();

            var amount = system.ProcessWeeklySubsidy(state);

            Assert.Equal(SubsidyBalance.GetBaseAmount(GuildRank.G), amount);
            Assert.Equal(1000 + SubsidyBalance.GetBaseAmount(GuildRank.G), state.Gold);
        }

        [Fact]
        public void ProcessWeeklySubsidy_UsesHigherAmount_ForHigherGuildRank()
        {
            var lowRankState = new GameState { WeekNumber = 4, GuildRank = GuildRank.G, ThreatLevel = 0 };
            var highRankState = new GameState { WeekNumber = 4, GuildRank = GuildRank.S, ThreatLevel = 0 };
            var system = new SubsidySystem();

            var lowAmount = system.ProcessWeeklySubsidy(lowRankState);
            var highAmount = system.ProcessWeeklySubsidy(highRankState);

            Assert.True(highAmount > lowAmount);
        }

        [Fact]
        public void ProcessWeeklySubsidy_CutsAmountInHalf_WhenThreatExceedsThreshold()
        {
            var state = new GameState
            {
                WeekNumber = 4,
                Gold = 0,
                GuildRank = GuildRank.C,
                ThreatLevel = SecurityBalance.SubsidyCutThreatThreshold + 1,
            };
            var system = new SubsidySystem();

            var amount = system.ProcessWeeklySubsidy(state);

            int expected = (int)(SubsidyBalance.GetBaseAmount(GuildRank.C) * SubsidyBalance.ThreatCutMultiplier);
            Assert.Equal(expected, amount);
            Assert.Equal(expected, state.Gold);
        }

        [Fact]
        public void ProcessWeeklySubsidy_DoesNotCut_WhenThreatAtThresholdExactly()
        {
            // 「75%超」でカットなので、ちょうど75はカット対象外。
            var state = new GameState
            {
                WeekNumber = 4,
                GuildRank = GuildRank.C,
                ThreatLevel = SecurityBalance.SubsidyCutThreatThreshold,
            };
            var system = new SubsidySystem();

            var amount = system.ProcessWeeklySubsidy(state);

            Assert.Equal(SubsidyBalance.GetBaseAmount(GuildRank.C), amount);
        }
    }
}
