using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>お金の流れ（段4d、§0.103）：累進の改築費・維持費・取り壊し・手当・慰労会・契約更改・満足度の慣れ・機嫌の張り合いのテスト。</summary>
    public class FinanceTests
    {
        private static Adventurer Make(string name, int satisfaction = 50, int wage = 100, int contribution = 0, Drive main = Drive.Gather)
        {
            var a = new Adventurer
            {
                Name = name, JobClass = JobClass.Warrior, Satisfaction = satisfaction, WeeklyWage = wage, TotalContributionScore = contribution,
                STR = 50, AGI = 50, VIT = 50, MND = 50, DEX = 50, LDR = 50, INT = 50,
                Personality = new Personality { Main = main },
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static GameState State(params Adventurer[] adventurers)
        {
            var state = new GameState { WeekNumber = 5, Gold = 100000, DungeonFields = SampleData.CreateDefaultFields() };
            state.Adventurers.AddRange(adventurers);
            return state;
        }

        private static void SetLevel(GameState state, FacilityType type, int level)
        {
            var f = state.Facilities.FirstOrDefault(x => x.Type == type);
            if (f == null) { f = new Facility { Type = type }; state.Facilities.Add(f); }
            f.CurrentLevel = level;
        }

        // ---------------- 改築費・維持費・取り壊し ----------------

        [Fact]
        public void UpgradeCost_IsProgressive()
        {
            var costs = Enumerable.Range(0, 5).Select(lv => FacilityBalance.GetUpgradeCost(FacilityType.Tavern, lv)).ToList();
            Assert.Equal(costs.OrderBy(c => c), costs);
            Assert.True(costs[4] >= costs[0] * 10);
        }

        [Fact]
        public void Upkeep_IsPaidAtTheEndOfTheMonthOnly()
        {
            var state = State();
            SetLevel(state, FacilityType.Tavern, 2);
            SetLevel(state, FacilityType.Infirmary, 5);
            int expected = FacilityBalance.GetUpkeep(2) + FacilityBalance.GetUpkeep(5) + FacilityBalance.GetUpkeep(state.GetFacilityLevel(FacilityType.Dormitory));
            Assert.Equal(expected, GuildFinanceSystem.MonthlyUpkeep(state));

            Assert.Equal(0, GuildFinanceSystem.PayUpkeepIfDue(state, 3));
            Assert.Equal(expected, GuildFinanceSystem.PayUpkeepIfDue(state, 4));
            Assert.Equal(100000 - expected, state.Gold);
            Assert.Equal(0, FacilityBalance.GetUpkeep(0));
        }

        [Fact]
        public void AllFacilitiesAtLevelFive_CostAFortune()
        {
            var state = State();
            foreach (var t in Enum.GetValues<FacilityType>()) SetLevel(state, t, 5);
            Assert.True(GuildFinanceSystem.MonthlyUpkeep(state) >= 3000);
        }

        [Fact]
        public void Demolish_LowersALevel_AndClearsSpecialtyAndTrainees()
        {
            var trainee = Make("訓練生");
            var state = State(trainee);
            SetLevel(state, FacilityType.DrillHall, 4);
            state.Facilities.First(f => f.Type == FacilityType.DrillHall).Specialty = FacilitySpecialty.Rivalry;
            var system = new FacilitySystem();

            Assert.True(system.TryDemolish(state, FacilityType.DrillHall));
            Assert.Equal(3, state.GetFacilityLevel(FacilityType.DrillHall));
            Assert.Equal(FacilitySpecialty.None, state.GetFacilitySpecialty(FacilityType.DrillHall));

            SetLevel(state, FacilityType.DrillHall, 1);
            state.TrainingAssignments[trainee.Id] = FacilityType.DrillHall;
            Assert.True(system.TryDemolish(state, FacilityType.DrillHall));
            Assert.False(state.TrainingAssignments.ContainsKey(trainee.Id));
            Assert.NotNull(FacilitySystem.DemolishBlockReason(state, FacilityType.DrillHall)); // Lv0は下げられない
        }

        [Fact]
        public void Demolish_KeepsTheDormitoryBigEnough()
        {
            var state = State(Enumerable.Range(0, FacilityBalance.GetDormitoryCapacity(1) + 1).Select(i => Make($"子{i}")).ToArray());
            SetLevel(state, FacilityType.Dormitory, 2);

            Assert.NotNull(FacilitySystem.DemolishBlockReason(state, FacilityType.Dormitory));
        }

        // ---------------- 手当 ----------------

        [Fact]
        public void Stipends_ArePaidForAppointedRetirees_ButNotGuests()
        {
            var strong = Make("強い引退者");
            strong.IsRetired = true;
            strong.STR = strong.AGI = strong.VIT = strong.MND = strong.DEX = strong.LDR = strong.INT = 90;
            var guest = Make("派遣の教官");
            guest.IsRetired = true;
            guest.IsGuest = true;
            var state = State();
            state.RetiredAdventurers.AddRange(new[] { strong, guest });
            state.AssignedTrainers[FacilityType.DrillHall] = strong.Id;
            state.AssignedTrainers[FacilityType.Academy] = guest.Id;

            Assert.Equal(EconomyBalance.StipendMax, GuildFinanceSystem.Stipend(strong));
            Assert.Equal(EconomyBalance.StipendMax, GuildFinanceSystem.PayStipends(state));
            Assert.Equal(100000 - EconomyBalance.StipendMax, state.Gold);
        }

        // ---------------- 慰労会 ----------------

        [Fact]
        public void Feast_RaisesTheSquad_OncePerSeason()
        {
            var a = Make("A", 40);
            var b = Make("B", 40);
            var state = State(a, b);
            var party = new SavedParty { Name = "主力", MemberIds = { a.Id, b.Id } };
            state.SavedParties.Add(party);
            int cost = GuildFinanceSystem.FeastCost(state, party);
            int gain = GuildFinanceSystem.FeastSatisfaction(state);

            Assert.True(GuildFinanceSystem.HoldFeast(state, party));
            Assert.Equal(40 + gain, a.Satisfaction);
            Assert.Equal(100000 - cost, state.Gold);
            Assert.NotNull(GuildFinanceSystem.FeastBlockReason(state, party));
            Assert.False(GuildFinanceSystem.HoldFeast(state, party));

            state.WeekNumber += GameCalendar.WeeksPerSeason; // 次の季節
            Assert.Null(GuildFinanceSystem.FeastBlockReason(state, party));
        }

        // ---------------- 契約更改 ----------------

        [Fact]
        public void ContractReview_OpensAtTheStartOfYearTwo()
        {
            var state = State(Make("A"));
            Assert.False(GuildFinanceSystem.OpenContractReviewIfDue(state, 1)); // 1年目のはじめは無し
            Assert.False(GuildFinanceSystem.OpenContractReviewIfDue(state, 50));
            Assert.True(GuildFinanceSystem.OpenContractReviewIfDue(state, GameCalendar.WeeksPerYear + 1));
            Assert.Equal(2, state.ContractReviewPendingYear);
            Assert.True(new WeekResult { ContractReviewDue = true }.ShouldStopMonth);
        }

        [Fact]
        public void ContractReview_MeritRaisesTheTopThird_AndOpensAGap()
        {
            var ace = Make("エース", 50, 100, contribution: 300);
            var mid = Make("中堅", 50, 100, contribution: 100);
            var greedy = Make("稼ぎ屋", 50, 100, contribution: 10, main: Drive.Money);
            var state = State(ace, mid, greedy);
            state.SavedParties.Add(new SavedParty { Name = "主力", MemberIds = { ace.Id, mid.Id } });
            state.Compatibility[CompatibilitySystem.NormalizeKey(ace.Id, mid.Id)] = 60;
            state.ContractReviewPendingYear = 2;

            Assert.True(GuildFinanceSystem.ApplyContractReview(state, ContractPolicy.Merit));

            Assert.Equal(100 + (int)Math.Round(100 * EconomyBalance.ContractMeritRaise), ace.WeeklyWage);
            Assert.Equal(100, mid.WeeklyWage);
            Assert.Equal(50 + EconomyBalance.ContractMeritTopSatisfaction, ace.Satisfaction);
            Assert.Equal(50 + EconomyBalance.ContractMeritOthersSatisfaction, mid.Satisfaction);
            Assert.Equal(50 + EconomyBalance.ContractMeritOthersSatisfaction * 2, greedy.Satisfaction); // お金が好きな子は2倍
            Assert.Equal(60 - EconomyBalance.ContractGapCompatLoss, CompatibilitySystem.GetCompatibility(state, ace.Id, mid.Id));
            Assert.Equal(300, ace.ContributionAtYearStart); // この年の功績を数え直す
            Assert.Null(state.ContractReviewPendingYear);
            Assert.Equal(2, state.ContractReviewDoneYear);
        }

        [Fact]
        public void ContractReview_EvenAndFreeze()
        {
            var a = Make("A", 50, 100);
            var state = State(a);
            state.ContractReviewPendingYear = 2;
            GuildFinanceSystem.ApplyContractReview(state, ContractPolicy.Even);
            Assert.Equal(100 + (int)Math.Round(100 * EconomyBalance.ContractEvenRaise), a.WeeklyWage);
            Assert.Equal(50 + EconomyBalance.ContractEvenSatisfaction, a.Satisfaction);

            var b = Make("B", 50, 100);
            var state2 = State(b);
            state2.ContractReviewPendingYear = 3;
            GuildFinanceSystem.ApplyContractReview(state2, ContractPolicy.Freeze);
            Assert.Equal(100, b.WeeklyWage);
            Assert.Equal(50 + EconomyBalance.ContractFreezeSatisfaction, b.Satisfaction);
            Assert.False(GuildFinanceSystem.ApplyContractReview(state2, ContractPolicy.Even)); // もう返事待ちが無い
        }

        // ---------------- 満足度の慣れ・機嫌の張り合い ----------------

        [Fact]
        public void Satisfaction_TavernHealsOnlyTheUnhappy_AndContentChildrenDrift()
        {
            var unhappy = Make("不満", 50);
            var content = Make("満ち足りた", 90);
            var state = State(unhappy, content);
            SetLevel(state, FacilityType.Tavern, 3);
            int recovery = FacilityBalance.GetTavernSatisfactionRecovery(3, FacilitySpecialty.None);
            foreach (var a in state.Adventurers) a.WeeklyWage = 10000; // 賃金の不満を混ぜない

            new SatisfactionSystem().ProcessWeeklySatisfaction(state, new HashSet<Guid>());

            Assert.Equal(50 + recovery, unhappy.Satisfaction);
            Assert.Equal(90 + SatisfactionBalance.ContentDrift, content.Satisfaction);
        }

        [Fact]
        public void Mood_SlipsWhenCheerfulWithoutNewAchievements()
        {
            var state = State();
            state.MasterMood = 90;

            var report = new MasterMoodSystem().ProcessWeeklyMood(state, new List<DungeonMissionResolution>());

            Assert.Contains(report.Entries, e => e.Reason.Contains("上機嫌の慣れ"));
        }

        [Fact]
        public void FinanceFields_RoundTripInTheSave()
        {
            var a = Make("A");
            a.ContributionAtYearStart = 77;
            var state = State(a);
            state.LastFeastSeason = 9;
            state.ContractReviewDoneYear = 4;
            state.ContractReviewPendingYear = 5;

            var back = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(state.ToSaveData()))!);

            Assert.Equal((9, 4, (int?)5, 77), (back.LastFeastSeason!.Value, back.ContractReviewDoneYear, back.ContractReviewPendingYear, back.Adventurers.Single().ContributionAtYearStart));
        }
    }
}
