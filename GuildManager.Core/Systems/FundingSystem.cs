using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>スポンサー契約の週の決算の結果（月報へ）。</summary>
    public sealed class SponsorSettlement
    {
        /// <summary>ノルマに届いた契約（成功報酬を払った）。</summary>
        public List<SponsorContract> Completed { get; } = new();
        /// <summary>期限までに届かなかった契約（違約金を取られた）。</summary>
        public List<SponsorContract> Failed { get; } = new();
    }

    /// <summary>
    /// 資金の調達（段4e、2026年10月・§0.104。→ docs/検討中_経営と育成.md 第1部 B1・B2・§4.4）。
    ///  - 商人ギルドの融資：いつでも借りられる（1口まで）。手数料が高く、返し終わるまでマスターの機嫌が毎週下がる。
    ///  - イザベラの融資：来訪のあと、年に1回。手数料が安く、機嫌は下がらない。上限は交流戦に勝つほど上がる。
    ///  - 返済：月の最後の週に、維持費と一緒に1回分ずつ払う。払えなければ所持金がマイナスになり、破産の数えに入る。
    ///    一括で返すと、残りの手数料はかからない（残りの元本だけ払う）。
    ///  - スポンサー契約：1年目の秋から、春と秋のはじめに控えめ・強気の話が1件ずつ届く。結べるのは1件まで。
    ///    結ぶと前金が入り、ノルマ（まだ倒していないボス・大会の入賞・依頼の達成の数）に届けば成功報酬と機嫌、
    ///    期限までに届かなければ違約金と機嫌の大きな低下。結ばなかった話は季節の終わりに流れる（罰なし）。
    /// </summary>
    public static class FundingSystem
    {
        // ==================== 融資 ====================

        public static GuildLoan? FindLoan(GameState state, LoanLender lender) => state.Loans.FirstOrDefault(l => l.Lender == lender && l.Remaining > 0);

        /// <summary>商人ギルドから借りられない理由（借りられるなら null）。</summary>
        public static string? MerchantLoanBlockReason(GameState state, int amount)
        {
            if (FindLoan(state, LoanLender.Merchant) != null) return "商人ギルドの借金を返し終わるまで、次は借りられない";
            if (!FundingBalance.MerchantLoanAmounts.Contains(amount)) return "その額は借りられない";
            return null;
        }

        /// <summary>イザベラから借りられる上限（基本＋交流戦の勝ち数×額、頭打ちあり）。</summary>
        public static int IsabellaLoanLimit(GameState state) =>
            Math.Min(FundingBalance.IsabellaLoanMax, FundingBalance.IsabellaLoanBase + state.ExchangeWins * FundingBalance.IsabellaLoanPerExchangeWin);

        /// <summary>イザベラから借りられない理由（借りられるなら null）。</summary>
        public static string? IsabellaLoanBlockReason(GameState state, int amount)
        {
            if (!IsabellaSystem.HasVisited(state)) return "イザベラが来訪してから（森の40Fのボスを倒したあと）";
            if (FindLoan(state, LoanLender.Isabella) != null) return "イザベラへの借金を返し終わるまで、次は借りられない";
            int year = GameCalendar.YearOf(state.WeekNumber);
            if (state.IsabellaLoanWeeks.Count(w => GameCalendar.YearOf(w) == year) >= FundingBalance.IsabellaLoanPerYear) return "イザベラの融資は年に1回まで（来年また頼める）";
            if (amount <= 0 || amount > IsabellaLoanLimit(state) || amount % FundingBalance.IsabellaLoanStep != 0) return $"借りられるのは {IsabellaLoanLimit(state)} G まで";
            return null;
        }

        /// <summary>返す額の合計（借りた額＋手数料）。</summary>
        public static int TotalDue(LoanLender lender, int amount) =>
            amount + (int)Math.Round(amount * (lender == LoanLender.Merchant ? FundingBalance.MerchantLoanFee : FundingBalance.IsabellaLoanFee), MidpointRounding.AwayFromZero);

        public static int Months(LoanLender lender) => lender == LoanLender.Merchant ? FundingBalance.MerchantLoanMonths : FundingBalance.IsabellaLoanMonths;

        /// <summary>月々の返済額（切り上げ。最後の月は残りだけ）。</summary>
        public static int MonthlyPayment(LoanLender lender, int amount) => (TotalDue(lender, amount) + Months(lender) - 1) / Months(lender);

        /// <summary>借りる。借りられなければ false。</summary>
        public static bool Borrow(GameState state, LoanLender lender, int amount)
        {
            string? block = lender == LoanLender.Merchant ? MerchantLoanBlockReason(state, amount) : IsabellaLoanBlockReason(state, amount);
            if (block != null) return false;
            int total = TotalDue(lender, amount);
            state.Loans.Add(new GuildLoan
            {
                Lender = lender,
                Principal = amount,
                TotalDue = total,
                Remaining = total,
                MonthlyPayment = MonthlyPayment(lender, amount),
                TakenWeek = state.WeekNumber,
            });
            if (lender == LoanLender.Isabella) state.IsabellaLoanWeeks.Add(state.WeekNumber);
            state.Gold += amount;
            return true;
        }

        /// <summary>月々の返済額の合計（画面の表示用）。</summary>
        public static int MonthlyLoanPayments(GameState state) => state.Loans.Sum(l => Math.Min(l.MonthlyPayment, l.Remaining));

        /// <summary>月の最後の週なら、借金を1回分ずつ返す（借りたその週は返さない）。返した額を返す。返し終わった借金は外す。</summary>
        public static int PayLoansIfDue(GameState state, int week)
        {
            if (!GameCalendar.IsLastWeekOfMonth(week)) return 0;
            int paid = 0;
            foreach (var loan in state.Loans.Where(l => l.TakenWeek != week).ToList())
            {
                int pay = Math.Min(loan.MonthlyPayment, loan.Remaining);
                loan.Remaining -= pay;
                state.Gold -= pay;
                paid += pay;
            }
            state.Loans.RemoveAll(l => l.Remaining <= 0);
            return paid;
        }

        /// <summary>一括で返すときの額（残りの元本だけ。残りの手数料はかからない）。</summary>
        public static int EarlyRepayment(GuildLoan loan) =>
            loan.TotalDue <= 0 ? loan.Remaining : (int)Math.Ceiling(loan.Remaining * (double)loan.Principal / loan.TotalDue);

        /// <summary>一括で返す。お金が足りなければ false。</summary>
        public static bool RepayEarly(GameState state, GuildLoan loan)
        {
            int amount = EarlyRepayment(loan);
            if (!state.Loans.Contains(loan) || state.Gold < amount) return false;
            state.Gold -= amount;
            state.Loans.Remove(loan);
            return true;
        }

        /// <summary>商人ギルドの借金があれば、マスターの機嫌を下げて内訳に載せる（週の決算で、機嫌の集計のあとに呼ぶ）。</summary>
        public static void ApplyDebtMood(GameState state, MasterMoodReport report)
        {
            if (FindLoan(state, LoanLender.Merchant) == null) return;
            int applied = MasterMoodSystem.Adjust(state, -FundingBalance.MerchantLoanMoodLoss);
            report.Entries.Add(new MasterMoodEntry("商人ギルドへの借金", -FundingBalance.MerchantLoanMoodLoss, applied));
        }

        public static string LenderLabel(LoanLender lender) => lender == LoanLender.Merchant ? "商人ギルド" : "イザベラ";

        // ==================== スポンサー契約 ====================

        /// <summary>ノルマの数え（今の値）。まだ倒していないボスは、これまでに倒したボスの数で数える。</summary>
        public static int Counter(GameState state, SponsorQuota quota) => quota switch
        {
            SponsorQuota.Placings => state.TournamentPlacingsTotal,
            SponsorQuota.Commissions => state.CommissionCompletions.Values.Sum(),
            _ => state.DungeonFields.Sum(f => f.Bosses.Count(b => b.EverDefeated)),
        };

        public static int Progress(GameState state, SponsorContract c) => Math.Max(0, Counter(state, c.Quota) - c.Baseline);

        /// <summary>今選べるノルマの種類（大会・依頼は開いてから）。</summary>
        public static List<SponsorQuota> AvailableQuotas(GameState state)
        {
            var list = new List<SponsorQuota> { SponsorQuota.Bosses };
            if (IsabellaSystem.TournamentsOpen(state)) list.Add(SponsorQuota.Placings);
            if (IsabellaSystem.CommissionsOpen(state)) list.Add(SponsorQuota.Commissions);
            return list;
        }

        /// <summary>話が届く週か（SponsorFirstWeek 以降の、春と秋のはじめ）。</summary>
        public static bool IsOfferWeek(int week) =>
            week >= FundingBalance.SponsorFirstWeek && GameCalendar.WeekOfSeason(week) == 1
            && GameCalendar.SeasonOf(week) is Season.Spring or Season.Autumn;

        public static SponsorContract? ActiveContract(GameState state) => state.SponsorContracts.FirstOrDefault(c => c.Signed);

        public static List<SponsorContract> Offers(GameState state) => state.SponsorContracts.Where(c => !c.Signed).ToList();

        /// <summary>
        /// 新しい週のはじめ（WeekNumber を進めたあと）：話が届く週なら、控えめ・強気の話を届ける。
        /// 結んでいる契約があるとき・攻略の最前線が無い（全フィールド制覇）ときは届かない。届いた話を返す。
        /// </summary>
        public static List<SponsorContract> ProcessNewWeek(GameState state, IRng rng)
        {
            var offered = new List<SponsorContract>();
            int week = state.WeekNumber;
            if (!IsOfferWeek(week) || ActiveContract(state) != null) return offered;
            var frontier = CommissionSystem.GetFrontier(state);
            if (frontier.Count == 0) return offered;
            state.SponsorContracts.RemoveAll(c => !c.Signed);

            int reward = Math.Min(frontier[0].Boss.RewardGold, FundingBalance.SponsorAdvanceBaseCap); // 前金の基準（頭打ちあり）
            var quotas = AvailableQuotas(state);
            var names = FundingBalance.SponsorNames.ToList();
            for (int i = 0; i < FundingBalance.SponsorOffers; i++)
            {
                bool bold = i % 2 == 1;
                var quota = quotas[rng.NextInt(0, quotas.Count - 1)];
                string name = names.Count == 0 ? "スポンサー" : names[rng.NextInt(0, names.Count - 1)];
                names.Remove(name);
                int advance = RoundTo100(reward * (bold ? FundingBalance.SponsorAdvanceBold : FundingBalance.SponsorAdvanceModest));
                var c = new SponsorContract
                {
                    SponsorName = name,
                    Quota = quota,
                    Bold = bold,
                    Target = FundingBalance.GetTarget(quota, bold),
                    Advance = advance,
                    Bonus = RoundTo100(advance * FundingBalance.SponsorBonus),
                    Penalty = RoundTo100(advance * FundingBalance.SponsorPenalty),
                    OfferedWeek = week,
                    DeadlineWeek = week + FundingBalance.SponsorWeeks - 1,
                };
                state.SponsorContracts.Add(c);
                offered.Add(c);
            }
            return offered;
        }

        private static int RoundTo100(double value) => Math.Max(100, (int)Math.Round(value / 100.0, MidpointRounding.AwayFromZero) * 100);

        /// <summary>結べない理由（結べるなら null）。</summary>
        public static string? SignBlockReason(GameState state, SponsorContract c)
        {
            if (c.Signed || !state.SponsorContracts.Contains(c)) return "この話はもう無い";
            if (ActiveContract(state) != null) return "結べるのは1件まで";
            if (state.WeekNumber > c.DeadlineWeek) return "期限が過ぎている";
            return null;
        }

        /// <summary>契約を結ぶ（前金が入り、ほかの話は流れる）。結べなければ false。</summary>
        public static bool Sign(GameState state, SponsorContract c)
        {
            if (SignBlockReason(state, c) != null) return false;
            c.Signed = true;
            c.Baseline = Counter(state, c.Quota);
            state.Gold += c.Advance;
            state.SponsorContracts.RemoveAll(x => !x.Signed);
            return true;
        }

        /// <summary>話を断る（罰なし）。</summary>
        public static bool Decline(GameState state, SponsorContract c) => !c.Signed && state.SponsorContracts.Remove(c);

        /// <summary>
        /// 週の決算（迷宮・大会・依頼の結果のあと）：ノルマに届いた契約は成功報酬と機嫌、期限の週に届かなかった契約は違約金と機嫌。
        /// 結ばなかった話は、届いた季節の最後の週に流れる。機嫌の増減は内訳に載せる。
        /// </summary>
        public static SponsorSettlement ProcessSettlement(GameState state, int week, MasterMoodReport? report = null)
        {
            var result = new SponsorSettlement();
            foreach (var c in state.SponsorContracts.ToList())
            {
                if (!c.Signed)
                {
                    if (week >= c.OfferedWeek + GameCalendar.WeeksPerSeason - 1) state.SponsorContracts.Remove(c);
                    continue;
                }
                if (Progress(state, c) >= c.Target)
                {
                    state.Gold += c.Bonus;
                    int applied = MasterMoodSystem.Adjust(state, FundingBalance.SponsorSuccessMood);
                    report?.Entries.Add(new MasterMoodEntry($"スポンサー契約の達成（{c.SponsorName}）", FundingBalance.SponsorSuccessMood, applied));
                    result.Completed.Add(c);
                    state.SponsorContracts.Remove(c);
                }
                else if (week >= c.DeadlineWeek)
                {
                    state.Gold -= c.Penalty;
                    int applied = MasterMoodSystem.Adjust(state, FundingBalance.SponsorFailureMood);
                    report?.Entries.Add(new MasterMoodEntry($"スポンサー契約の未達（{c.SponsorName}）", FundingBalance.SponsorFailureMood, applied));
                    result.Failed.Add(c);
                    state.SponsorContracts.Remove(c);
                }
            }
            return result;
        }

        // ==================== 資金の画面（§0.115） ====================

        /// <summary>
        /// 1か月の収支の見込み（資金の画面）：いまの機嫌での内職の売上、週給・手当（4週分）、維持費、借金の返済。
        /// 依頼・撃破の報酬や大会の賞金は読めないので入れない。Severance は今年度末に満期を迎える子（いま25歳以上）の退職金の合計。
        /// </summary>
        public sealed record MonthlyOutlook(int SideJob, int Wages, int Stipends, int Upkeep, int LoanPayments, int Severance)
        {
            /// <summary>月の差し引き（内職の売上 − 週給・手当・維持費・返済）。</summary>
            public int Net => SideJob - Wages - Stipends - Upkeep - LoanPayments;
        }

        public static MonthlyOutlook Outlook(GameState state)
        {
            double multiplier = MasterMoodSystem.GetSideJobMultiplier(MasterMoodSystem.GetTier(state.MasterMood));
            int sideJob = (int)Math.Round((EconomyBalance.SideJobBaseAmount + EconomySystem.GetSideJobResearchBonus(state) + EconomySystem.GetSideJobTradeBonus(state)) * multiplier);
            int wages = state.Adventurers.Where(a => !a.IsOnLoan).Sum(a => a.WeeklyWage) * GameCalendar.WeeksPerMonth; // 派遣中は先方持ち（§0.85）
            int stipends = GuildFinanceSystem.WeeklyStipends(state) * GameCalendar.WeeksPerMonth;
            int severance = state.Adventurers.Where(a => !a.IsRetired && !a.NeverRetires && a.Age + 1 >= AgingSystem.RetirementAge)
                .Sum(AgingSystem.CalculateSeverancePay);
            return new MonthlyOutlook(sideJob, wages, stipends, GuildFinanceSystem.MonthlyUpkeep(state), MonthlyLoanPayments(state), severance);
        }

        /// <summary>いまの所持金で、月の差し引きの赤字が何か月もつか（赤字でなければ null）。</summary>
        public static int? MonthsOfRunway(GameState state, MonthlyOutlook outlook) =>
            outlook.Net >= 0 ? null : Math.Max(0, state.Gold) / -outlook.Net;

        /// <summary>スポンサー契約の期限まで残りの週（今週を含む）。</summary>
        public static int WeeksLeft(GameState state, SponsorContract c) => c.DeadlineWeek - state.WeekNumber + 1;

        /// <summary>
        /// スポンサー契約の期限の月に重なる、ほかの「届かなければ損をする」もの（§0.115。引継ぎ書の「敗北の重なり」）：
        /// 受けた依頼の期限、年度末の退職金。重なりが無ければ空。
        /// </summary>
        public static List<string> DeadlineClashes(GameState state, SponsorContract c)
        {
            var clashes = new List<string>();
            int year = GameCalendar.YearOf(c.DeadlineWeek), month = GameCalendar.MonthOfYear(c.DeadlineWeek);
            bool SameMonth(int week) => GameCalendar.YearOf(week) == year && GameCalendar.MonthOfYear(week) == month;
            foreach (var com in state.Commissions.Where(x => x.Accepted && SameMonth(x.DeadlineWeek)))
                clashes.Add($"依頼（{CommissionSystem.ClientName(com)}の{CommissionSystem.TypeLabel(com.Type)}）の期限");
            if (month == GameCalendar.MonthOfYear(GameCalendar.WeeksPerYear) && Outlook(state).Severance is int sev and > 0)
                clashes.Add($"年度末の退職金（約 {sev} G）");
            return clashes;
        }

        public static string QuotaLabel(SponsorContract c) => c.Quota switch
        {
            SponsorQuota.Placings => $"大会で{c.Target}回入賞（ベスト4以上）",
            SponsorQuota.Commissions => $"依頼を{c.Target}件達成",
            _ => $"まだ倒していないボスを{c.Target}体倒す",
        };
    }
}
