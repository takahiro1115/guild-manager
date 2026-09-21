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
            var state = new GameState { WeekNumber = weekNumber, Gold = 1000, GuildRank = GuildRank.G };
            var system = new SubsidySystem();

            var amount = system.ProcessWeeklySubsidy(state);

            Assert.Equal(SubsidyBalance.GetBaseAmount(GuildRank.G), amount);
            Assert.Equal(1000 + SubsidyBalance.GetBaseAmount(GuildRank.G), state.Gold);
        }

        [Fact]
        public void ProcessWeeklySubsidy_UsesHigherAmount_ForHigherGuildRank()
        {
            var lowRankState = new GameState { WeekNumber = 4, GuildRank = GuildRank.G };
            var highRankState = new GameState { WeekNumber = 4, GuildRank = GuildRank.S };
            var system = new SubsidySystem();

            var lowAmount = system.ProcessWeeklySubsidy(lowRankState);
            var highAmount = system.ProcessWeeklySubsidy(highRankState);

            Assert.True(highAmount > lowAmount);
        }

        [Theory]
        [InlineData(GuildRank.G)]
        [InlineData(GuildRank.F)]
        [InlineData(GuildRank.E)]
        [InlineData(GuildRank.D)]
        [InlineData(GuildRank.C)]
        [InlineData(GuildRank.B)]
        [InlineData(GuildRank.A)]
        [InlineData(GuildRank.S)]
        public void ProcessWeeklySubsidy_PaysFullRankAmount_WithoutAnyCut(GuildRank rank)
        {
            // 脅威度システムの撤去（2026年9月）により「脅威度75%超で50%カット」は廃止された。
            // 助成金はギルド格付けのみで決まり、常に満額支給される（→ SubsidySystem）。
            var state = new GameState { WeekNumber = 4, Gold = 0, GuildRank = rank };
            var system = new SubsidySystem();

            var amount = system.ProcessWeeklySubsidy(state);

            int expected = SubsidyBalance.GetBaseAmount(rank);
            Assert.Equal(expected, amount);
            Assert.Equal(expected, state.Gold);
        }
    }
}
