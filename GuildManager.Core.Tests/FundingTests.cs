using System.Linq;
using System.Text.Json;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>資金の調達（段4e、§0.104）：商人ギルドの融資・イザベラの融資・スポンサー契約のテスト。</summary>
    public class FundingTests
    {
        private static GameState State(int week = 5, int gold = 1000)
        {
            var state = new GameState { WeekNumber = week, Gold = gold, DungeonFields = SampleData.CreateDefaultFields() };
            foreach (var f in state.DungeonFields) f.IsUnlocked = f.Order == 1;
            return state;
        }

        private static int Amount(int i) => FundingBalance.MerchantLoanAmounts[i];

        // ---------------- 融資 ----------------

        [Fact]
        public void MerchantLoan_PaysOutNow_AndIsRepaidMonthlyWithTheFee()
        {
            var state = State(week: 2);
            int amount = Amount(0);
            Assert.True(FundingSystem.Borrow(state, LoanLender.Merchant, amount));
            Assert.Equal(1000 + amount, state.Gold);
            var loan = state.Loans.Single();
            Assert.Equal(FundingSystem.TotalDue(LoanLender.Merchant, amount), loan.TotalDue);
            Assert.True(loan.TotalDue > amount);

            Assert.Equal(0, FundingSystem.PayLoansIfDue(state, 3));
            Assert.Equal(loan.MonthlyPayment, FundingSystem.PayLoansIfDue(state, 4));

            int paid = loan.MonthlyPayment;
            for (int month = 2; month <= FundingBalance.MerchantLoanMonths; month++)
                paid += FundingSystem.PayLoansIfDue(state, month * GameCalendar.WeeksPerMonth);
            Assert.Equal(loan.TotalDue, paid);
            Assert.Empty(state.Loans);
        }

        [Fact]
        public void MerchantLoan_OnlyOneAtATime_AndOnlyListedAmounts()
        {
            var state = State();
            Assert.NotNull(FundingSystem.MerchantLoanBlockReason(state, 1234));
            Assert.True(FundingSystem.Borrow(state, LoanLender.Merchant, Amount(0)));
            Assert.NotNull(FundingSystem.MerchantLoanBlockReason(state, Amount(1)));
            Assert.False(FundingSystem.Borrow(state, LoanLender.Merchant, Amount(1)));
        }

        [Fact]
        public void LoanBorrowedInTheLastWeek_StartsRepayingNextMonth()
        {
            var state = State(week: 4);
            FundingSystem.Borrow(state, LoanLender.Merchant, Amount(0));
            Assert.Equal(0, FundingSystem.PayLoansIfDue(state, 4));
        }

        [Fact]
        public void UnpaidInstallment_DrivesGoldNegative()
        {
            var state = State(week: 1, gold: 0);
            FundingSystem.Borrow(state, LoanLender.Merchant, Amount(0));
            state.Gold = 0;
            FundingSystem.PayLoansIfDue(state, 4);
            Assert.True(state.Gold < 0); // 払えなければ所持金がマイナスになり、破産の数えに入る
        }

        [Fact]
        public void EarlyRepayment_SkipsTheRemainingFee()
        {
            var state = State(week: 1, gold: 100000);
            FundingSystem.Borrow(state, LoanLender.Merchant, Amount(0));
            var loan = state.Loans.Single();
            Assert.Equal(Amount(0), FundingSystem.EarlyRepayment(loan));
            Assert.True(FundingSystem.RepayEarly(state, loan));
            Assert.Empty(state.Loans);
            Assert.Equal(100000, state.Gold);
        }

        [Fact]
        public void MerchantDebt_LowersMoodEveryWeek()
        {
            var state = State();
            state.MasterMood = 50;
            var report = new MasterMoodReport();
            FundingSystem.ApplyDebtMood(state, report);
            Assert.Equal(50, state.MasterMood);

            FundingSystem.Borrow(state, LoanLender.Merchant, Amount(0));
            FundingSystem.ApplyDebtMood(state, report);
            Assert.Equal(50 - FundingBalance.MerchantLoanMoodLoss, state.MasterMood);
            Assert.Contains(report.Entries, e => e.Reason.Contains("借金"));
        }

        [Fact]
        public void IsabellaLoan_AfterTheVisit_OncePerYear_LimitGrowsWithExchangeWins()
        {
            var state = State();
            Assert.NotNull(FundingSystem.IsabellaLoanBlockReason(state, FundingBalance.IsabellaLoanStep));

            state.IsabellaVisitWeek = 3;
            Assert.Equal(FundingBalance.IsabellaLoanBase, FundingSystem.IsabellaLoanLimit(state));
            state.ExchangeWins = 2;
            int limit = FundingSystem.IsabellaLoanLimit(state);
            Assert.Equal(System.Math.Min(FundingBalance.IsabellaLoanMax, FundingBalance.IsabellaLoanBase + 2 * FundingBalance.IsabellaLoanPerExchangeWin), limit);
            Assert.NotNull(FundingSystem.IsabellaLoanBlockReason(state, limit + FundingBalance.IsabellaLoanStep));

            Assert.True(FundingSystem.Borrow(state, LoanLender.Isabella, limit));
            Assert.True(FundingSystem.TotalDue(LoanLender.Isabella, limit) < FundingSystem.TotalDue(LoanLender.Merchant, limit)); // 低利
            var loan = state.Loans.Single();
            Assert.True(FundingSystem.RepayEarly(state, loan));
            Assert.NotNull(FundingSystem.IsabellaLoanBlockReason(state, FundingBalance.IsabellaLoanStep)); // 同じ年は1回まで

            state.WeekNumber += GameCalendar.WeeksPerYear;
            Assert.Null(FundingSystem.IsabellaLoanBlockReason(state, FundingBalance.IsabellaLoanStep));
        }

        // ---------------- スポンサー契約 ----------------

        [Fact]
        public void SponsorOffers_ArriveFromYearOneAutumn_InSpringAndAutumn()
        {
            Assert.False(FundingSystem.IsOfferWeek(1));
            Assert.True(FundingSystem.IsOfferWeek(FundingBalance.SponsorFirstWeek));
            Assert.False(FundingSystem.IsOfferWeek(GameCalendar.WeeksPerYear - GameCalendar.WeeksPerSeason + 1)); // 冬
            Assert.True(FundingSystem.IsOfferWeek(GameCalendar.WeeksPerYear + 1)); // 2年目の春

            var state = State(week: FundingBalance.SponsorFirstWeek);
            var offers = FundingSystem.ProcessNewWeek(state, new SeededRng(1));
            Assert.Equal(FundingBalance.SponsorOffers, offers.Count);
            Assert.Contains(offers, o => o.Bold);
            Assert.All(offers, o => Assert.Equal(SponsorQuota.Bosses, o.Quota)); // 大会・依頼が開く前はボスだけ
            Assert.True(offers.Single(o => o.Bold).Advance > offers.Single(o => !o.Bold).Advance);
        }

        [Fact]
        public void Sponsor_SignPaysTheAdvance_AndReachingTheQuotaPaysTheBonus()
        {
            var state = State(week: FundingBalance.SponsorFirstWeek);
            var offer = FundingSystem.ProcessNewWeek(state, new SeededRng(1)).First(o => !o.Bold);
            int gold = state.Gold;
            Assert.True(FundingSystem.Sign(state, offer));
            Assert.Equal(gold + offer.Advance, state.Gold);
            Assert.Single(state.SponsorContracts); // ほかの話は流れる

            foreach (var boss in state.DungeonFields.First(f => f.Order == 1).Bosses.Where(b => !b.EverDefeated).Take(offer.Target))
                boss.IsDefeated = true;
            state.MasterMood = 50;
            var result = FundingSystem.ProcessSettlement(state, state.WeekNumber);

            Assert.Single(result.Completed);
            Assert.Equal(gold + offer.Advance + offer.Bonus, state.Gold);
            Assert.Equal(50 + FundingBalance.SponsorSuccessMood, state.MasterMood);
            Assert.Empty(state.SponsorContracts);
        }

        [Fact]
        public void Sponsor_MissingTheQuotaCostsThePenaltyAtTheDeadline()
        {
            var state = State(week: FundingBalance.SponsorFirstWeek);
            var offer = FundingSystem.ProcessNewWeek(state, new SeededRng(1)).First(o => o.Bold);
            FundingSystem.Sign(state, offer);
            int gold = state.Gold;
            state.MasterMood = 50;

            Assert.Empty(FundingSystem.ProcessSettlement(state, offer.DeadlineWeek - 1).Failed);
            var result = FundingSystem.ProcessSettlement(state, offer.DeadlineWeek);

            Assert.Single(result.Failed);
            Assert.Equal(gold - offer.Penalty, state.Gold);
            Assert.True(offer.Penalty > offer.Advance);
            Assert.Equal(50 + FundingBalance.SponsorFailureMood, state.MasterMood);
        }

        [Fact]
        public void UnsignedOffers_LapseAtTheEndOfTheSeason_WithoutPenalty()
        {
            var state = State(week: FundingBalance.SponsorFirstWeek);
            var offers = FundingSystem.ProcessNewWeek(state, new SeededRng(1));
            int gold = state.Gold;
            FundingSystem.ProcessSettlement(state, offers[0].OfferedWeek + GameCalendar.WeeksPerSeason - 2);
            Assert.Equal(offers.Count, state.SponsorContracts.Count);
            FundingSystem.ProcessSettlement(state, offers[0].OfferedWeek + GameCalendar.WeeksPerSeason - 1);
            Assert.Empty(state.SponsorContracts);
            Assert.Equal(gold, state.Gold);
        }

        [Fact]
        public void NoNewOffers_WhileAContractIsRunning()
        {
            var state = State(week: FundingBalance.SponsorFirstWeek);
            FundingSystem.Sign(state, FundingSystem.ProcessNewWeek(state, new SeededRng(1))[0]);
            state.WeekNumber = FundingBalance.SponsorFirstWeek + GameCalendar.WeeksPerSeason * 2;
            Assert.Empty(FundingSystem.ProcessNewWeek(state, new SeededRng(1)));
        }

        [Fact]
        public void FundingFields_RoundTripInTheSave()
        {
            var state = State(week: FundingBalance.SponsorFirstWeek);
            FundingSystem.Borrow(state, LoanLender.Merchant, Amount(0));
            state.IsabellaLoanWeeks.Add(7);
            FundingSystem.Sign(state, FundingSystem.ProcessNewWeek(state, new SeededRng(1))[0]);

            var back = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(state.ToSaveData()))!);

            Assert.Equal(state.Loans.Single().Remaining, back.Loans.Single().Remaining);
            Assert.Equal(new[] { 7 }, back.IsabellaLoanWeeks);
            Assert.True(back.SponsorContracts.Single().Signed);

            var legacy = JsonSerializer.Deserialize<SaveData>("{}")!;
            legacy.Loans = null;
            legacy.SponsorContracts = null;
            legacy.IsabellaLoanWeeks = null;
            var fresh = GameState.FromSaveData(legacy);
            Assert.Empty(fresh.Loans);
            Assert.Empty(fresh.SponsorContracts);
        }
    }
}
