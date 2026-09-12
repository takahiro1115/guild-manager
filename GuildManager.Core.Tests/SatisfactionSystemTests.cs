using System;
using System.Collections.Generic;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 満足度変動・契約交渉フロー（仕様書 03 §5.1・§5.2）のテスト。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class SatisfactionSystemTests
    {
        private static readonly IReadOnlySet<Guid> NoDispatch = new HashSet<Guid>();

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members)
                party.TryAdd(m);
            return party;
        }

        // ---------------- 出場機会ペナルティ（§5.1） ----------------

        [Fact]
        public void ProcessWeeklySatisfaction_ResetsStreak_WhenDispatched()
        {
            var adventurer = new Adventurer { Age = 24, WeeksSinceLastDeployment = 5, WeeklyWage = 1000 };
            var state = new GameState { Adventurers = { adventurer } };
            var system = new SatisfactionSystem();

            system.ProcessWeeklySatisfaction(state, new HashSet<Guid> { adventurer.Id });

            Assert.Equal(0, adventurer.WeeksSinceLastDeployment);
        }

        [Fact]
        public void ProcessWeeklySatisfaction_AppliesNoDeploymentPenalty_AtFourthConsecutiveWeek()
        {
            // 22〜27歳が対象。週給は適正値以上にして賃金ペナルティを混入させない。
            var adventurer = new Adventurer
            {
                Age = 24,
                Satisfaction = 70,
                WeeksSinceLastDeployment = 3, // 今回で4週目に到達
                PA_STR = 100, PA_AGI = 100, PA_VIT = 100, PA_MND = 100, PA_DEX = 100, PA_LDR = 100,
                WeeklyWage = 1000,
            };
            var state = new GameState { Adventurers = { adventurer } };
            var system = new SatisfactionSystem();

            system.ProcessWeeklySatisfaction(state, NoDispatch);

            // -5(出場機会) +1(自然回復) = -4
            Assert.Equal(66, adventurer.Satisfaction);
            Assert.Equal(4, adventurer.WeeksSinceLastDeployment);
        }

        [Fact]
        public void ProcessWeeklySatisfaction_NoDeploymentPenalty_DoesNotApply_BeforeFourthWeek()
        {
            var adventurer = new Adventurer
            {
                Age = 24, Satisfaction = 70, WeeksSinceLastDeployment = 2, // 今回で3週目
                PA_STR = 100, PA_AGI = 100, PA_VIT = 100, PA_MND = 100, PA_DEX = 100, PA_LDR = 100,
                WeeklyWage = 1000,
            };
            var state = new GameState { Adventurers = { adventurer } };
            var system = new SatisfactionSystem();

            system.ProcessWeeklySatisfaction(state, NoDispatch);

            Assert.Equal(71, adventurer.Satisfaction); // 自然回復+1のみ
        }

        [Theory]
        [InlineData(21)] // 成長期
        [InlineData(28)] // 円熟期
        public void ProcessWeeklySatisfaction_NoDeploymentPenalty_OnlyAppliesToPrimeAgeBand(int age)
        {
            var adventurer = new Adventurer
            {
                Age = age, Satisfaction = 70, WeeksSinceLastDeployment = 10,
                PA_STR = 100, PA_AGI = 100, PA_VIT = 100, PA_MND = 100, PA_DEX = 100, PA_LDR = 100,
                WeeklyWage = 1000,
            };
            var state = new GameState { Adventurers = { adventurer } };
            var system = new SatisfactionSystem();

            system.ProcessWeeklySatisfaction(state, NoDispatch);

            Assert.Equal(71, adventurer.Satisfaction); // 22〜27歳以外はペナルティ対象外。自然回復+1のみ
        }

        // ---------------- 賃金妥当性ペナルティ（§5.1） ----------------

        [Fact]
        public void ProcessWeeklySatisfaction_AppliesUnderpaidPenalty()
        {
            // TotalPA=100 → 適正週給=60。80%ライン=48。週給10なら未満。
            var adventurer = new Adventurer
            {
                Age = 30, Satisfaction = 70, WeeklyWage = 10,
                PA_STR = 100, PA_AGI = 100, PA_VIT = 100, PA_MND = 100, PA_DEX = 100, PA_LDR = 100,
            };
            var state = new GameState { Adventurers = { adventurer } };
            var system = new SatisfactionSystem();

            system.ProcessWeeklySatisfaction(state, new HashSet<Guid> { adventurer.Id }); // 出撃扱いにして出場機会は除外

            // -8(賃金) +1(自然回復) = -7
            Assert.Equal(63, adventurer.Satisfaction);
        }

        [Fact]
        public void ProcessWeeklySatisfaction_NoUnderpaidPenalty_WhenWageIsAdequate()
        {
            var adventurer = new Adventurer
            {
                Age = 30, Satisfaction = 70, WeeklyWage = 1000,
                PA_STR = 100, PA_AGI = 100, PA_VIT = 100, PA_MND = 100, PA_DEX = 100, PA_LDR = 100,
            };
            var state = new GameState { Adventurers = { adventurer } };
            var system = new SatisfactionSystem();

            system.ProcessWeeklySatisfaction(state, new HashSet<Guid> { adventurer.Id });

            Assert.Equal(71, adventurer.Satisfaction); // 自然回復+1のみ
        }

        [Fact]
        public void ProcessWeeklySatisfaction_ClampsAtMax()
        {
            var adventurer = new Adventurer { Age = 30, Satisfaction = 100, WeeklyWage = 1000 };
            var state = new GameState { Adventurers = { adventurer } };
            var system = new SatisfactionSystem();

            system.ProcessWeeklySatisfaction(state, new HashSet<Guid> { adventurer.Id });

            Assert.Equal(100, adventurer.Satisfaction);
        }

        [Fact]
        public void ProcessWeeklySatisfaction_ClampsAtMin()
        {
            var adventurer = new Adventurer { Age = 24, Satisfaction = 2, WeeksSinceLastDeployment = 10, WeeklyWage = 0 };
            var state = new GameState { Adventurers = { adventurer } };
            var system = new SatisfactionSystem();

            system.ProcessWeeklySatisfaction(state, NoDispatch);

            Assert.Equal(0, adventurer.Satisfaction);
        }

        // ---------------- 勝利・功績ボーナス（§5.1） ----------------

        [Fact]
        public void ApplyQuestAchievementBonus_GrantsBonus_ForBRankAchieved()
        {
            var a = new Adventurer { Satisfaction = 50 };
            var b = new Adventurer { Satisfaction = 50 };
            var party = PartyOf(a, b);
            var system = new SatisfactionSystem();

            system.ApplyQuestAchievementBonus(party, new Quest { Rank = QuestRank.B }, questAchieved: true);

            Assert.Equal(60, a.Satisfaction);
            Assert.Equal(60, b.Satisfaction);
        }

        [Fact]
        public void ApplyQuestAchievementBonus_GrantsBonus_ForSRankAchieved()
        {
            var a = new Adventurer { Satisfaction = 50 };
            var party = PartyOf(a);
            var system = new SatisfactionSystem();

            system.ApplyQuestAchievementBonus(party, new Quest { Rank = QuestRank.S }, questAchieved: true);

            Assert.Equal(60, a.Satisfaction);
        }

        [Fact]
        public void ApplyQuestAchievementBonus_NoBonus_ForBelowBRank()
        {
            var a = new Adventurer { Satisfaction = 50 };
            var party = PartyOf(a);
            var system = new SatisfactionSystem();

            system.ApplyQuestAchievementBonus(party, new Quest { Rank = QuestRank.C }, questAchieved: true);

            Assert.Equal(50, a.Satisfaction);
        }

        [Fact]
        public void ApplyQuestAchievementBonus_NoBonus_WhenNotAchieved()
        {
            var a = new Adventurer { Satisfaction = 50 };
            var party = PartyOf(a);
            var system = new SatisfactionSystem();

            system.ApplyQuestAchievementBonus(party, new Quest { Rank = QuestRank.S }, questAchieved: false);

            Assert.Equal(50, a.Satisfaction);
        }

        // ---------------- 仲間ロストの余波（§5.1・§4.3接続前提） ----------------

        [Fact]
        public void ApplyPartyLossPenalty_AppliesToSurvivingMembersOnly()
        {
            var lost = new Adventurer { Satisfaction = 50 };
            var survivor1 = new Adventurer { Satisfaction = 50 };
            var survivor2 = new Adventurer { Satisfaction = 50 };
            var party = PartyOf(lost, survivor1, survivor2);
            var system = new SatisfactionSystem();

            system.ApplyPartyLossPenalty(party, lost.Id);

            Assert.Equal(50, lost.Satisfaction); // ロスト本人は対象外（既にロストしているため）
            Assert.Equal(20, survivor1.Satisfaction);
            Assert.Equal(20, survivor2.Satisfaction);
        }

        // ---------------- 人間関係：相性「険悪」ペナルティ（§5.1・§5.3.1、v1.5からの保留を解消） ----------------

        [Fact]
        public void ProcessWeeklySatisfaction_AppliesHostilePairPenalty_ForDispatchedHostilePair()
        {
            var a = new Adventurer { Age = 30, Satisfaction = 70, WeeklyWage = 1000 };
            var b = new Adventurer { Age = 30, Satisfaction = 70, WeeklyWage = 1000 };
            var party = PartyOf(a, b);
            var state = new GameState { Adventurers = { a, b } };
            state.Compatibility[CompatibilitySystem.NormalizeKey(a.Id, b.Id)] = CompatibilityBalance.HostileThreshold - 1;
            state.ActiveDispatches.Add(new ActiveDispatch { Party = party, WeeksRemaining = 2 });
            var system = new SatisfactionSystem();

            system.ProcessWeeklySatisfaction(state, new HashSet<Guid> { a.Id, b.Id });

            // -10(険悪ペナルティ) +1(自然回復) = -9
            Assert.Equal(61, a.Satisfaction);
            Assert.Equal(61, b.Satisfaction);
        }

        [Fact]
        public void ProcessWeeklySatisfaction_NoHostilePairPenalty_WhenCompatibilityAtOrAboveThreshold()
        {
            var a = new Adventurer { Age = 30, Satisfaction = 70, WeeklyWage = 1000 };
            var b = new Adventurer { Age = 30, Satisfaction = 70, WeeklyWage = 1000 };
            var party = PartyOf(a, b);
            var state = new GameState { Adventurers = { a, b } };
            state.Compatibility[CompatibilitySystem.NormalizeKey(a.Id, b.Id)] = CompatibilityBalance.HostileThreshold;
            state.ActiveDispatches.Add(new ActiveDispatch { Party = party, WeeksRemaining = 2 });
            var system = new SatisfactionSystem();

            system.ProcessWeeklySatisfaction(state, new HashSet<Guid> { a.Id, b.Id });

            Assert.Equal(71, a.Satisfaction); // 自然回復+1のみ
            Assert.Equal(71, b.Satisfaction);
        }

        [Fact]
        public void ProcessWeeklySatisfaction_StacksHostilePairPenalty_ForMultipleSimultaneousHostilePairs()
        {
            // cが a・b両方と険悪な3人パーティ：cは2件分のペナルティを受ける。
            var a = new Adventurer { Age = 30, Satisfaction = 70, WeeklyWage = 1000 };
            var b = new Adventurer { Age = 30, Satisfaction = 70, WeeklyWage = 1000 };
            var c = new Adventurer { Age = 30, Satisfaction = 70, WeeklyWage = 1000 };
            var party = PartyOf(a, b, c);
            var state = new GameState { Adventurers = { a, b, c } };
            state.Compatibility[CompatibilitySystem.NormalizeKey(a.Id, c.Id)] = CompatibilityBalance.HostileThreshold - 1;
            state.Compatibility[CompatibilitySystem.NormalizeKey(b.Id, c.Id)] = CompatibilityBalance.HostileThreshold - 1;
            state.ActiveDispatches.Add(new ActiveDispatch { Party = party, WeeksRemaining = 2 });
            var system = new SatisfactionSystem();

            system.ProcessWeeklySatisfaction(state, new HashSet<Guid> { a.Id, b.Id, c.Id });

            // a・b：-10(険悪1件) +1(自然回復) = -9
            Assert.Equal(61, a.Satisfaction);
            Assert.Equal(61, b.Satisfaction);
            // c：-20(険悪2件) +1(自然回復) = -19
            Assert.Equal(51, c.Satisfaction);
        }

        [Fact]
        public void ProcessWeeklySatisfaction_NoHostilePairPenalty_WhenNotCurrentlyDispatchedTogether()
        {
            var a = new Adventurer { Age = 30, Satisfaction = 70, WeeklyWage = 1000 };
            var b = new Adventurer { Age = 30, Satisfaction = 70, WeeklyWage = 1000 };
            var state = new GameState { Adventurers = { a, b } };
            state.Compatibility[CompatibilitySystem.NormalizeKey(a.Id, b.Id)] = CompatibilityBalance.HostileThreshold - 1;
            // ActiveDispatchesに登録しない＝現在同パーティで出撃中ではない
            var system = new SatisfactionSystem();

            system.ProcessWeeklySatisfaction(state, new HashSet<Guid> { a.Id, b.Id });

            Assert.Equal(71, a.Satisfaction); // 自然回復+1のみ
            Assert.Equal(71, b.Satisfaction);
        }

        // ---------------- 契約交渉フロー（§5.2） ----------------

        [Fact]
        public void ProcessWeeklyNegotiation_RaisesWarningFlag_WhenSatisfactionDropsBelowTwenty()
        {
            var adventurer = new Adventurer { Satisfaction = 19 };
            var state = new GameState { Adventurers = { adventurer } };
            var system = new SatisfactionSystem();

            var terminated = system.ProcessWeeklyNegotiation(state);

            Assert.True(adventurer.NeedsNegotiation);
            Assert.Equal(0, adventurer.NegotiationWeeksElapsed);
            Assert.Empty(terminated);
            Assert.Contains(adventurer, state.Adventurers);
        }

        [Fact]
        public void ProcessWeeklyNegotiation_DoesNotWarn_WhenSatisfactionIsTwentyOrAbove()
        {
            var adventurer = new Adventurer { Satisfaction = 20 };
            var state = new GameState { Adventurers = { adventurer } };
            var system = new SatisfactionSystem();

            system.ProcessWeeklyNegotiation(state);

            Assert.False(adventurer.NeedsNegotiation);
        }

        [Fact]
        public void ProcessWeeklyNegotiation_TerminatesContract_AfterGraceWeeksExpire()
        {
            var adventurer = new Adventurer { Satisfaction = 10 };
            var state = new GameState { Adventurers = { adventurer } };
            var system = new SatisfactionSystem();

            var week1 = system.ProcessWeeklyNegotiation(state); // 警告発生（経過0）
            var week2 = system.ProcessWeeklyNegotiation(state); // 経過1
            var week3 = system.ProcessWeeklyNegotiation(state); // 経過2
            var week4 = system.ProcessWeeklyNegotiation(state); // 猶予(2週)超過→契約解除

            Assert.Empty(week1);
            Assert.Empty(week2);
            Assert.Empty(week3);
            Assert.Contains(adventurer, week4);
            Assert.DoesNotContain(adventurer, state.Adventurers);
        }

        [Fact]
        public void ProcessWeeklyNegotiation_Termination_FreesTrainingSlot()
        {
            var adventurer = new Adventurer { Satisfaction = 10 };
            var state = new GameState { Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id, FacilityType.WarriorHall);
            var system = new SatisfactionSystem();

            for (int i = 0; i < 4; i++)
                system.ProcessWeeklyNegotiation(state);

            Assert.DoesNotContain(adventurer.Id, state.TrainingAssignments.Keys);
        }

        [Fact]
        public void ProcessWeeklyNegotiation_DoesNotTerminate_OtherAdventurers()
        {
            var troubled = new Adventurer { Satisfaction = 10 };
            var happy = new Adventurer { Satisfaction = 80 };
            var state = new GameState { Adventurers = { troubled, happy } };
            var system = new SatisfactionSystem();

            for (int i = 0; i < 4; i++)
                system.ProcessWeeklyNegotiation(state);

            Assert.DoesNotContain(troubled, state.Adventurers);
            Assert.Contains(happy, state.Adventurers);
        }

        // ---------------- 昇給・ボーナス対応（§5.2） ----------------

        [Fact]
        public void RaiseWage_AppliesMultiplierAndResolvesWarning()
        {
            var adventurer = new Adventurer { WeeklyWage = 100, NeedsNegotiation = true, NegotiationWeeksElapsed = 1 };
            var system = new SatisfactionSystem();

            system.RaiseWage(adventurer, 1.5);

            Assert.Equal(150, adventurer.WeeklyWage);
            Assert.False(adventurer.NeedsNegotiation);
            Assert.Equal(0, adventurer.NegotiationWeeksElapsed);
        }

        [Theory]
        [InlineData(1.0, 150)]  // 下限1.5未満はクランプ（100*1.5=150）
        [InlineData(3.0, 200)]  // 上限2.0超過はクランプ（100*2.0=200）
        public void RaiseWage_ClampsMultiplierToSpecRange(double multiplier, int expectedWage)
        {
            var adventurer = new Adventurer { WeeklyWage = 100 };
            var system = new SatisfactionSystem();

            system.RaiseWage(adventurer, multiplier);

            Assert.Equal(expectedWage, adventurer.WeeklyWage);
        }

        [Fact]
        public void PayBonus_DeductsGoldAndResolvesWarning()
        {
            var adventurer = new Adventurer { WeeklyWage = 100, NeedsNegotiation = true, NegotiationWeeksElapsed = 1 };
            var state = new GameState { Gold = 5000, Adventurers = { adventurer } };
            var system = new SatisfactionSystem();

            system.PayBonus(state, adventurer);

            Assert.Equal(5000 - 100 * SatisfactionBalance.BonusWeeksEquivalent, state.Gold);
            Assert.False(adventurer.NeedsNegotiation);
            Assert.Equal(0, adventurer.NegotiationWeeksElapsed);
        }
    }
}
