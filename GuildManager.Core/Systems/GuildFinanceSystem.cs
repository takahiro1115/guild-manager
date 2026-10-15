using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>契約更改の方針（§0.103）。</summary>
    public enum ContractPolicy
    {
        /// <summary>成果に応じて：前の年の功績が上位3分の1の子だけ上げる（差がつく）。</summary>
        Merit,
        /// <summary>全員を少しずつ上げる。</summary>
        Even,
        /// <summary>据え置く（お金はかからないが、みんな不満）。</summary>
        Freeze,
    }

    /// <summary>
    /// お金の流れ（段4d、2026年10月・§0.103。→ docs/検討中_経営と育成.md 第1部 A1・C1、第2部 §7.4）。
    ///  - 施設の維持費：施設のLvごとの月々の維持費を、月の最後の週に払う（→ facility.csv Upkeep_Lv*）。8施設すべてLv5では維持できないくらい。
    ///    払えなければ所持金がマイナスになり、今の破産の数え（4週）に入る（施設は止めない）。
    ///  - 教官・顧問の手当：任命した引退者ごとに、引退したときの強さに応じた週の手当（週給と一緒に払う）。
    ///  - 酒場の慰労会：お金で部隊の全員の満足度をその場で上げる（季節に1回まで）。
    ///  - 契約更改：年のはじめに、給与をどう上げるかを決める（成果に応じて／全員を少しずつ／据え置く）。答えなければその月の終わりに据え置き。
    /// 乱数は使わない。
    /// </summary>
    public static class GuildFinanceSystem
    {
        // ==================== 施設の維持費 ====================

        /// <summary>今の施設の月々の維持費の合計。</summary>
        public static int MonthlyUpkeep(GameState state) =>
            Enum.GetValues<FacilityType>().Sum(t => FacilityBalance.GetUpkeep(state.GetFacilityLevel(t)));

        /// <summary>月の最後の週なら維持費を払い、払った額を返す（ほかの週は0）。</summary>
        public static int PayUpkeepIfDue(GameState state, int week)
        {
            if (!GameCalendar.IsLastWeekOfMonth(week)) return 0;
            int upkeep = MonthlyUpkeep(state);
            state.Gold -= upkeep;
            return upkeep;
        }

        // ==================== 教官・顧問の手当 ====================

        /// <summary>引退者1人の週の手当＝StipendBase＋(引退したときの7能力の平均−40)×StipendPerStat（上限 StipendMax）。</summary>
        public static int Stipend(Adventurer retiree)
        {
            double avg = AdventurerStatAccessor.AllStatNames.Average(s => (double)AdventurerStatAccessor.GetStat(retiree, s));
            return Math.Clamp((int)Math.Round(EconomyBalance.StipendBase + Math.Max(0, avg - 40) * EconomyBalance.StipendPerStat), EconomyBalance.StipendBase, EconomyBalance.StipendMax);
        }

        /// <summary>任命されている教官・顧問（派遣の教官は先方持ちなので含めない）。</summary>
        public static List<Adventurer> PaidRetirees(GameState state)
        {
            var ids = state.AssignedTrainers.Values.OfType<Guid>().ToList();
            if (state.AssignedAdvisor is Guid advisor) ids.Add(advisor);
            if (state.AssignedScoutMaster is Guid scout) ids.Add(scout);
            ids.AddRange(GuildShopSystem.AllWorkers(state)); // ギルドの店の働き手（§0.134）
            return ids.Distinct().Select(id => state.RetiredAdventurers.FirstOrDefault(r => r.Id == id)).OfType<Adventurer>().Where(r => !r.IsGuest).ToList();
        }

        public static int WeeklyStipends(GameState state) => PaidRetirees(state).Sum(Stipend);

        /// <summary>週の手当を払い、払った額を返す。</summary>
        public static int PayStipends(GameState state)
        {
            int total = WeeklyStipends(state);
            state.Gold -= total;
            return total;
        }

        // ==================== 酒場の慰労会 ====================

        /// <summary>通算の季節（1年目の春＝0）。</summary>
        public static int SeasonIndex(int week) => (GameCalendar.YearOf(week) - 1) * GameCalendar.SeasonsPerYear + (int)GameCalendar.SeasonOf(week);

        private static List<Adventurer> FeastMembers(GameState state, SavedParty party) =>
            party.MemberIds.Select(state.FindAdventurer).OfType<Adventurer>().Where(a => !a.IsRetired && !a.IsOnLoan && !a.IsDispatched).ToList();

        public static int FeastCost(GameState state, SavedParty party) => FeastMembers(state, party).Count * EconomyBalance.FeastCostPerHead;

        /// <summary>慰労会で上がる満足度（基本＋酒場のLv×FeastSatisfactionPerTavernLevel）。</summary>
        public static int FeastSatisfaction(GameState state) =>
            EconomyBalance.FeastSatisfaction + state.GetFacilityLevel(FacilityType.Tavern) * EconomyBalance.FeastSatisfactionPerTavernLevel;

        /// <summary>慰労会を開けない理由（開けるなら null）。</summary>
        public static string? FeastBlockReason(GameState state, SavedParty party)
        {
            if (state.LastFeastSeason == SeasonIndex(state.WeekNumber)) return "慰労会は季節に1回まで（次の季節に開ける）";
            var members = FeastMembers(state, party);
            if (members.Count == 0) return "出られるメンバーがいない（出撃中・派遣中の子は呼べない）";
            if (state.Gold < FeastCost(state, party)) return $"お金が足りない（{FeastCost(state, party)}G）";
            return null;
        }

        /// <summary>慰労会を開く（部隊の全員の満足度が上がる）。開けなければ false。</summary>
        public static bool HoldFeast(GameState state, SavedParty party)
        {
            if (FeastBlockReason(state, party) != null) return false;
            state.Gold -= FeastCost(state, party);
            int gain = FeastSatisfaction(state);
            foreach (var a in FeastMembers(state, party))
            {
                SatisfactionSystem.Adjust(a, gain);
                MoraleSystem.UpdateStage(a);
            }
            state.LastFeastSeason = SeasonIndex(state.WeekNumber);
            return true;
        }

        // ==================== 契約更改 ====================

        /// <summary>年のはじめなら契約更改を求める（返事待ちにする）。求めたら true。</summary>
        public static bool OpenContractReviewIfDue(GameState state, int week)
        {
            int year = GameCalendar.YearOf(week);
            if (GameCalendar.WeekOfYear(week) != 1 || year <= state.ContractReviewDoneYear || state.ContractReviewPendingYear == year) return false;
            state.ContractReviewPendingYear = year;
            return true;
        }

        /// <summary>この年の功績（前の契約更改からの累積功績の増え方）。</summary>
        public static int YearContribution(Adventurer a) => Math.Max(0, a.TotalContributionScore - a.ContributionAtYearStart);

        private static List<Adventurer> Staff(GameState state) => state.Adventurers.Where(a => !a.IsRetired && !a.NeverRetires).ToList();

        /// <summary>「成果に応じて」で上げる子（この年の功績の上位3分の1、切り上げ）。</summary>
        public static List<Adventurer> MeritTop(GameState state)
        {
            var staff = Staff(state);
            int n = (staff.Count + 2) / 3;
            return staff.OrderByDescending(YearContribution).ThenBy(a => a.Name, StringComparer.Ordinal).Take(n).ToList();
        }

        private static bool LovesMoney(Adventurer a) => a.Personality is { } p && (p.Main == Drive.Money || p.Sub == Drive.Money);

        /// <summary>方針ごとに、週給が週あたりいくら増えるかの見込み（画面の表示用）。</summary>
        public static int WageIncrease(GameState state, ContractPolicy policy) => policy switch
        {
            ContractPolicy.Merit => MeritTop(state).Sum(a => (int)Math.Round(a.WeeklyWage * EconomyBalance.ContractMeritRaise)),
            ContractPolicy.Even => Staff(state).Sum(a => (int)Math.Round(a.WeeklyWage * EconomyBalance.ContractEvenRaise)),
            _ => 0,
        };

        /// <summary>契約更改を行う（返事待ちを閉じ、この年の功績を数え直す）。返事待ちが無ければ false。</summary>
        public static bool ApplyContractReview(GameState state, ContractPolicy policy)
        {
            if (state.ContractReviewPendingYear is not int year) return false;
            var staff = Staff(state);
            switch (policy)
            {
                case ContractPolicy.Merit:
                {
                    var top = MeritTop(state).Select(a => a.Id).ToHashSet();
                    foreach (var a in staff)
                    {
                        if (top.Contains(a.Id))
                        {
                            a.WeeklyWage += (int)Math.Round(a.WeeklyWage * EconomyBalance.ContractMeritRaise);
                            SatisfactionSystem.Adjust(a, EconomyBalance.ContractMeritTopSatisfaction);
                            PersonalitySystem.NotifyMoneyJoy(a);
                        }
                        else
                            SatisfactionSystem.Adjust(a, EconomyBalance.ContractMeritOthersSatisfaction * (LovesMoney(a) ? 2 : 1));
                    }
                    // 差がつく：同じ部隊で上げた子と上げなかった子の相性が下がる
                    foreach (var party in state.SavedParties)
                    {
                        var ids = party.MemberIds;
                        foreach (var x in ids.Where(top.Contains))
                            foreach (var y in ids.Where(id => !top.Contains(id)))
                            {
                                var key = CompatibilitySystem.NormalizeKey(x, y);
                                int current = CompatibilitySystem.GetCompatibility(state, x, y);
                                state.Compatibility[key] = Math.Clamp(current - EconomyBalance.ContractGapCompatLoss, CompatibilityBalance.MinValue, CompatibilityBalance.MaxValue);
                            }
                    }
                    break;
                }
                case ContractPolicy.Even:
                    foreach (var a in staff)
                    {
                        a.WeeklyWage += (int)Math.Round(a.WeeklyWage * EconomyBalance.ContractEvenRaise);
                        SatisfactionSystem.Adjust(a, EconomyBalance.ContractEvenSatisfaction + (LovesMoney(a) ? 2 : 0));
                        PersonalitySystem.NotifyMoneyJoy(a);
                    }
                    break;
                default:
                    foreach (var a in staff)
                        SatisfactionSystem.Adjust(a, EconomyBalance.ContractFreezeSatisfaction * (LovesMoney(a) ? 2 : 1));
                    break;
            }
            foreach (var a in staff)
            {
                a.ContributionAtYearStart = a.TotalContributionScore;
                MoraleSystem.UpdateStage(a);
            }
            state.ContractReviewDoneYear = year;
            state.ContractReviewPendingYear = null;
            return true;
        }

        public static string PolicyLabel(ContractPolicy policy) => policy switch
        {
            ContractPolicy.Merit => "成果に応じて上げる",
            ContractPolicy.Even => "全員を少しずつ上げる",
            _ => "据え置く",
        };
    }
}
