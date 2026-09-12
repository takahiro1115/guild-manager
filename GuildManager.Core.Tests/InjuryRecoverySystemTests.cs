using System;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 負傷回復処理（03 §3.6）が、指定週数で確実に全快することを確認するテスト。
    /// </summary>
    public class InjuryRecoverySystemTests
    {
        private static Adventurer CreateSeverelyInjuredAdventurer(int weeksRemaining)
        {
            var adventurer = new Adventurer { VIT = 20 }; // MaxHP = 20*2+50 = 90
            adventurer.Injury = InjurySeverity.Severe;
            adventurer.InjuryWeeksRemaining = weeksRemaining;
            adventurer.CurrentHP = 1;
            return adventurer;
        }

        [Fact]
        public void ProcessWeeklyRecovery_DecrementsWeeksRemaining_WhileStillInjured()
        {
            var adventurer = CreateSeverelyInjuredAdventurer(3);
            var state = new GameState { Adventurers = { adventurer } };
            var system = new InjuryRecoverySystem();

            system.ProcessWeeklyRecovery(state);

            Assert.Equal(2, adventurer.InjuryWeeksRemaining);
            Assert.Equal(InjurySeverity.Severe, adventurer.Injury);
        }

        [Fact]
        public void ProcessWeeklyRecovery_FullyHealsWhenCountdownReachesZero()
        {
            var adventurer = CreateSeverelyInjuredAdventurer(1);
            var state = new GameState { Adventurers = { adventurer } };
            var system = new InjuryRecoverySystem();

            system.ProcessWeeklyRecovery(state);

            Assert.Equal(InjurySeverity.None, adventurer.Injury);
            Assert.Equal(adventurer.MaxHP, adventurer.CurrentHP);
        }

        [Fact]
        public void ProcessWeeklyRecovery_DoesNothingToUninjuredAdventurer()
        {
            var adventurer = new Adventurer { VIT = 20, CurrentHP = 90 };
            var state = new GameState { Adventurers = { adventurer } };
            var system = new InjuryRecoverySystem();

            system.ProcessWeeklyRecovery(state);

            Assert.Equal(InjurySeverity.None, adventurer.Injury);
            Assert.Equal(90, adventurer.CurrentHP);
        }
    }
}
