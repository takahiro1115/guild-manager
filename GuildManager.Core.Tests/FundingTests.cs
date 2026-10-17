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

        // ---------------- 資金の画面（§0.115） ----------------

        [Fact]
        public void Outlook_SumsAMonthOfWagesUpkeepAndLoans_AgainstTheSideJob()
        {
            var state = State(gold: 1000);
            state.Adventurers.AddRange(SampleData.CreateStarterAdventurers());
            state.MasterMood = 50;
            state.Adventurers[2].LoanUntilWeek = 30; // 派遣中の週給は先方持ち
            Assert.True(FundingSystem.Borrow(state, LoanLender.Merchant, Amount(0)));

            var o = FundingSystem.Outlook(state);
            int wages = (state.Adventurers[0].WeeklyWage + state.Adventurers[1].WeeklyWage) * GameCalendar.WeeksPerMonth;
            Assert.Equal(wages, o.Wages);
            Assert.Equal((int)System.Math.Round(EconomyBalance.SideJobBaseAmount * MasterMoodSystem.GetSideJobMultiplier(MasterMoodSystem.GetTier(50))), o.SideJob);
            Assert.Equal(GuildFinanceSystem.MonthlyUpkeep(state), o.Upkeep);
            Assert.Equal(FundingSystem.MonthlyLoanPayments(state), o.LoanPayments);
            Assert.True(o.LoanPayments > 0);
            Assert.Equal(o.SideJob - o.Wages - o.Stipends - o.Upkeep - o.LoanPayments, o.Net);
            Assert.Equal(0, o.Severance);

            if (o.Net < 0)
                Assert.Equal(state.Gold / -o.Net, FundingSystem.MonthsOfRunway(state, o));
            Assert.Null(FundingSystem.MonthsOfRunway(state, o with { SideJob = 100_000 }));
        }

        [Fact]
        public void Outlook_CountsSeveranceOfThoseRetiringAtThisYearEnd()
        {
            var state = State();
            state.Adventurers.AddRange(SampleData.CreateStarterAdventurers());
            state.Adventurers[0].Age = AgingSystem.RetirementAge - 1; // 年度末に満期
            state.Adventurers[1].Age = AgingSystem.RetirementAge - 2; // まだ
            Assert.Equal(AgingSystem.CalculateSeverancePay(state.Adventurers[0]), FundingSystem.Outlook(state).Severance);
        }

        [Fact]
        public void DeadlineClashes_AcceptedCommissionInTheSameMonth_AndYearEndSeverance()
        {
            var state = State(week: 10);
            var contract = new SponsorContract { SponsorName = "王都の商会", DeadlineWeek = 26, Signed = true }; // 1年目 秋1の月（25〜28週）の第2週
            state.SponsorContracts.Add(contract);
            Assert.Equal(17, FundingSystem.WeeksLeft(state, contract));
            Assert.Empty(FundingSystem.DeadlineClashes(state, contract));

            state.Commissions.Add(new GuildCommission { ClientId = "Merchants", Type = CommissionType.Deliver, DeadlineWeek = 28, Accepted = false });
            Assert.Empty(FundingSystem.DeadlineClashes(state, contract)); // 受けていない依頼は数えない
            state.Commissions[0].Accepted = true;
            Assert.Single(FundingSystem.DeadlineClashes(state, contract)); // 同じ月（25〜28週）
            state.Commissions[0].DeadlineWeek = 29;
            Assert.Empty(FundingSystem.DeadlineClashes(state, contract)); // 次の月

            state.Adventurers.AddRange(SampleData.CreateStarterAdventurers());
            state.Adventurers[0].Age = AgingSystem.RetirementAge - 1;
            contract.DeadlineWeek = GameCalendar.WeeksPerYear - 1; // 年度末の月
            Assert.Contains(FundingSystem.DeadlineClashes(state, contract), t => t.Contains("退職金"));
        }

        /// <summary>
        /// §0.115：初めて話が届いた月にスポンサーの場面（§0.151 から月報の前）。手引きは話が無くなる（結ぶ・断る・流れる）と済む。
        /// §0.140：スポンサーの話はイザベラが持ってくる（帳簿の場面を見たあとの、最初の話の月）。
        /// </summary>
        [Fact]
        public void SponsorScene_OnTheFirstOfferAfterTheLedger_GuideDoneWhenNoOfferIsLeft()
        {
            var state = State(week: FundingBalance.SponsorFirstWeek - 1);
            Assert.DoesNotContain("s02_sponsor", StorySystem.DueScenes(state, StoryTiming.BeforeReport).Select(s => s.SceneId));
            state.WeekNumber = FundingBalance.SponsorFirstWeek;
            FundingSystem.ProcessNewWeek(state, new SeededRng(1));
            Assert.DoesNotContain("s02_sponsor", StorySystem.DueScenes(state, StoryTiming.BeforeReport).Select(s => s.SceneId)); // 帳簿の前は出さない
            StorySystem.MarkSeen(state, "s02_ledger");
            Assert.DoesNotContain("s02_sponsor", StorySystem.DueScenes(state, StoryTiming.BeforeReport).Select(s => s.SceneId)); // 帳簿と同じ月報では、次の月報まで待つ
            state.WeekNumber++;
            // §0.151：話の載った月報より前に出す（月報のあとではない）
            Assert.DoesNotContain("s02_sponsor", StorySystem.DueScenes(state, StoryTiming.AfterReport).Select(s => s.SceneId));
            Assert.Contains("s02_sponsor", StorySystem.DueScenes(state, StoryTiming.BeforeReport).Select(s => s.SceneId));
            StorySystem.MarkSeen(state, "s02_sponsor");

            Assert.False(GuideSystem.Groups(state).Single(g => g.SceneId == "s02_sponsor").AllDone);
            Assert.True(FundingSystem.Decline(state, FundingSystem.Offers(state)[0]));
            Assert.False(GuideSystem.Groups(state).Single(g => g.SceneId == "s02_sponsor").AllDone);
            Assert.True(FundingSystem.Decline(state, FundingSystem.Offers(state)[0]));
            Assert.True(GuideSystem.Groups(state).Single(g => g.SceneId == "s02_sponsor").AllDone);
        }

        [Fact]
        public void MonthlyNotes_PointSponsorToTheFundingView()
        {
            var state = State(week: FundingBalance.SponsorFirstWeek);
            FundingSystem.ProcessNewWeek(state, new SeededRng(1));
            var report = MonthlyReport.Build(state, new System.Collections.Generic.List<AutoSkipWeek>(), state.Gold);
            Assert.Contains(report.Notes, n => n.Text.StartsWith("スポンサー契約の話が届いている") && n.Target == MonthlyNoteTarget.Funding);

            Assert.True(FundingSystem.Sign(state, FundingSystem.Offers(state)[0]));
            report = MonthlyReport.Build(state, new System.Collections.Generic.List<AutoSkipWeek>(), state.Gold);
            Assert.Contains(report.Notes, n => n.Text.Contains("スポンサー契約") && n.Text.Contains("あと") && n.Target == MonthlyNoteTarget.Funding);
        }
    }
}
