using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>契約交渉に応じたあとの満足度と間（§0.134）のテスト。</summary>
    public class NegotiationCooldownTests
    {
        private static (GameState State, Adventurer A) Setup()
        {
            var a = new Adventurer { Name = "不満の子", JobClass = JobClass.Warrior, WeeklyWage = 100, Satisfaction = 5 };
            var state = new GameState { Gold = 100000 };
            state.Adventurers.Add(a);
            return (state, a);
        }

        [Fact]
        public void Answering_RaisesSatisfaction_AndPausesTheNextNegotiation()
        {
            var (state, a) = Setup();
            var sys = new SatisfactionSystem();
            sys.ProcessWeeklyNegotiation(state);
            Assert.True(a.NeedsNegotiation);

            sys.RaiseWage(a, SatisfactionBalance.MinRaiseMultiplier);
            Assert.Equal(5 + SatisfactionBalance.NegotiationSatisfactionGain, a.Satisfaction);
            Assert.Equal(SatisfactionBalance.NegotiationCooldownWeeks, a.NegotiationCooldownWeeks);

            a.Satisfaction = 0; // 不満のもとが残っていても
            for (int i = 0; i < SatisfactionBalance.NegotiationCooldownWeeks; i++)
            {
                sys.ProcessWeeklyNegotiation(state);
                Assert.False(a.NeedsNegotiation); // 間のあいだは交渉にならない（週給がふくらまない）
            }
            sys.ProcessWeeklyNegotiation(state);
            Assert.True(a.NeedsNegotiation);
            Assert.Equal((int)System.Math.Round(100 * SatisfactionBalance.MinRaiseMultiplier), a.WeeklyWage);
        }

        [Fact]
        public void ABonusOutsideANegotiation_DoesNotGiveTheGain()
        {
            var (state, a) = Setup();
            a.Satisfaction = 50;
            new SatisfactionSystem().PayBonus(state, a);
            Assert.Equal(50, a.Satisfaction);
            Assert.Equal(0, a.NegotiationCooldownWeeks);
        }
    }
}
