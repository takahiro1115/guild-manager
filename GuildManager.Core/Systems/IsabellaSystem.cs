using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>交流戦の1試合（剣・魔・技のどれか）。</summary>
    public sealed class ExchangeBout
    {
        public TournamentDiscipline Discipline { get; set; }
        /// <summary>自分のギルドの出場者（出なかったら null＝不戦敗）。</summary>
        public Guid? AdventurerId { get; set; }
        public string OurName { get; set; } = "";
        public string OpponentName { get; set; } = "";
        public double OurStrength { get; set; }
        public double OpponentStrength { get; set; }
        public bool Won { get; set; }
    }

    /// <summary>交流戦1回の結果（週の決算から月報・画面へ渡す）。</summary>
    public sealed class ExchangeOutcome
    {
        public TournamentEvent Event { get; set; } = new();
        public List<ExchangeBout> Bouts { get; } = new();
        public bool Won { get; set; }
        /// <summary>最初の交流戦（来訪のあとの1回目）。このあと最初の訓練所が開き、大会の暦が置かれる。</summary>
        public bool First { get; set; }
        /// <summary>初めて勝った（ご褒美は派遣の教官）。</summary>
        public bool FirstWin { get; set; }
        public int Prize { get; set; }
        public int Mood { get; set; }
        /// <summary>最初の交流戦のあとに開いた訓練所（イザベラの助言）。無ければ null。</summary>
        public FacilityUnlockNotice? OpenedFacility { get; set; }
        public int Wins => Bouts.Count(b => b.Won);
    }

    /// <summary>派遣の教官の出入り（その週に来た・帰った）。</summary>
    public sealed class GuestTrainerChange
    {
        public Adventurer? Arrived { get; set; }
        public FacilityType? AssignedTo { get; set; }
        public Adventurer? Left { get; set; }
    }

    /// <summary>
    /// イザベラ（白百合の杖）の来訪・交流戦・派遣の教官・依頼の開放（2026年10月・§0.84、→ 物語帳「ゲームの決まりが変わる点」）。
    ///  - 来訪：森の40Fのボスを初めて倒した週。最初の交流戦を次の月の第4週に置く（→ CheckVisit）。それまで大会も依頼も無い。
    ///  - 交流戦：剣・魔・技の1対1の3本勝負（2勝で勝ち）。季節に1回、月のはじめに申し込み、その月の第4週に行う（→ TryApply）。
    ///    出場者はその月は出撃も訓練もしない（大会の出場と同じく TournamentEntries に入る）。相手の強さは来訪したときの自分のギルドが基準。
    ///  - 最初の交流戦のあと（勝敗にかかわらず）：いちばん善戦した部門の訓練所が開き、次の月から大会の暦が置かれる。
    ///  - 初勝利：派遣の教官（マルグリット）が、訓練所が建っていれば来て24週いる（→ ProcessGuestTrainer）。2勝目からは賞金と機嫌。
    ///  - 依頼：どこかの大会で初めて入賞（ベスト4以上）したら、次の季節のはじめから届く（→ CheckCommissionUnlock）。
    /// </summary>
    public class IsabellaSystem
    {
        public const string ExchangeDefinitionId = "exchange";
        public const string ExchangeName = "白百合の杖との交流戦";

        /// <summary>交流戦の部門（この順に試合をする）。</summary>
        public static readonly TournamentDiscipline[] ExchangeDisciplines =
            { TournamentDiscipline.Sword, TournamentDiscipline.Magic, TournamentDiscipline.Skill };

        private readonly IRng _rng;

        public IsabellaSystem(IRng rng)
        {
            _rng = rng;
        }

        // ==================== 進み具合 ====================

        public static bool HasVisited(GameState state) => state.IsabellaVisitWeek != null;

        /// <summary>大会が開いているか（最初の交流戦を終えた）。</summary>
        public static bool TournamentsOpen(GameState state) => state.TournamentCalendarFromWeek != null;

        /// <summary>依頼が届くようになっているか（届き始める週が決まっている）。</summary>
        public static bool CommissionsOpen(GameState state) => state.CommissionsFromWeek != null;

        /// <summary>次の月のはじめの週。</summary>
        public static int NextMonthStart(int week) => week - (GameCalendar.WeekOfMonth(week) - 1) + GameCalendar.WeeksPerMonth;

        /// <summary>今の月のはじめの週。</summary>
        public static int MonthStart(int week) => week - (GameCalendar.WeekOfMonth(week) - 1);

        /// <summary>次の季節のはじめの週。</summary>
        public static int NextSeasonStart(int week) => week - (GameCalendar.WeekOfSeason(week) - 1) + GameCalendar.WeeksPerSeason;

        // ==================== 来訪 ====================

        /// <summary>
        /// 森の40Fのボスを倒していて、まだ来訪していなければ来訪する（相手の強さの基準を今のギルドで決め、最初の交流戦を次の月に置く）。
        /// 来訪したら true。週の決算で迷宮の結果のあとに呼ぶ。
        /// </summary>
        public static bool CheckVisit(GameState state)
        {
            if (HasVisited(state)) return false;
            var field = state.DungeonFields.FirstOrDefault(f => f.Id == IsabellaBalance.VisitFieldId);
            if (field == null || !field.Bosses.Any(b => b.Floor == IsabellaBalance.VisitFloor && b.EverDefeated)) return false;
            state.IsabellaVisitWeek = state.WeekNumber;
            state.ExchangeAnchor.Clear();
            EnsureAnchor(state);
            AddExchangeEvent(state, NextMonthStart(state.WeekNumber));
            return true;
        }

        // ==================== 相手の強さ ====================

        /// <summary>相手の強さの基準が無い部門を、今の現役で部門の強さが最も高い子の値で決める（旧セーブはここで初めて決まる）。</summary>
        public static void EnsureAnchor(GameState state)
        {
            foreach (var d in ExchangeDisciplines)
            {
                if (state.ExchangeAnchor.ContainsKey(d)) continue;
                double best = state.Adventurers.Where(a => !a.IsRetired).Select(a => TournamentSystem.DisciplineStrength(a, d)).DefaultIfEmpty(0).Max();
                state.ExchangeAnchor[d] = best > 0 ? best : 60; // 冒険者がいない（テストなど）ときの下限
            }
        }

        /// <summary>今の交流戦の相手の強さ＝基準×部門の倍率×(1＋1回ごとの伸び×行った回数)×(1＋年ごとの伸び×来訪からの年数)。</summary>
        public static double OpponentStrength(GameState state, TournamentDiscipline d)
        {
            EnsureAnchor(state);
            double years = Math.Max(0, state.WeekNumber - (state.IsabellaVisitWeek ?? state.WeekNumber)) / (double)GameCalendar.WeeksPerYear;
            return state.ExchangeAnchor[d] * IsabellaBalance.ExchangeFactor(d)
                * (1 + IsabellaBalance.ExchangeGrowthPerMatch * state.ExchangeMatchesPlayed)
                * (1 + IsabellaBalance.ExchangeGrowthPerYear * years);
        }

        /// <summary>1試合の勝率の目安（運を除く。大会と同じ式）。</summary>
        public static double BoutWinChance(double ours, double opponent)
        {
            if (ours <= 0) return 0;
            if (opponent <= 0) return 1;
            double k = TournamentBalance.WinExponent;
            return Math.Pow(ours, k) / (Math.Pow(ours, k) + Math.Pow(opponent, k));
        }

        /// <summary>3試合のうち2勝以上する見込み（試合ごとの勝率から）。</summary>
        public static double MatchWinChance(IReadOnlyList<double> bouts)
        {
            if (bouts.Count != 3) return 0;
            double a = bouts[0], b = bouts[1], c = bouts[2];
            return a * b + a * c + b * c - 2 * a * b * c;
        }

        // ==================== 予定と申し込み ====================

        /// <summary>まだ行っていない交流戦（無ければ null）。</summary>
        public static TournamentEvent? PendingExchange(GameState state) =>
            state.TournamentEvents.FirstOrDefault(e => e.Kind == TournamentKind.Exchange && e.Result == null);

        /// <summary>今月の交流戦（行ったものも含む。無ければ null）。</summary>
        public static TournamentEvent? ExchangeThisMonth(GameState state) =>
            state.TournamentEvents.FirstOrDefault(e => e.Kind == TournamentKind.Exchange && TournamentSystem.IsThisMonth(state, e));

        private static int SeasonIndex(int year, int monthOfYear) => (year - 1) * GameCalendar.SeasonsPerYear + (monthOfYear - 1) / GameCalendar.MonthsPerSeason;

        /// <summary>交流戦を申し込めない理由（申し込めるなら null）。</summary>
        public static string? ApplyBlockReason(GameState state)
        {
            if (!HasVisited(state)) return "イザベラがまだ来ていない";
            if (!TournamentsOpen(state)) return "最初の交流戦がまだ終わっていない";
            if (PendingExchange(state) != null) return "申し込んだ交流戦がまだ終わっていない";
            if (!TournamentSystem.CanChangeEntries(state)) return "申し込めるのは月のはじめだけ";
            int season = SeasonIndex(GameCalendar.YearOf(state.WeekNumber), GameCalendar.MonthOfYear(state.WeekNumber));
            if (state.TournamentEvents.Any(e => e.Kind == TournamentKind.Exchange && SeasonIndex(e.Year, e.Month) == season))
                return "この季節はもう交流戦をした（季節に1回）";
            return null;
        }

        /// <summary>交流戦を申し込む（今月の第4週に置く）。申し込めなければ null。</summary>
        public static TournamentEvent? TryApply(GameState state) =>
            ApplyBlockReason(state) != null ? null : AddExchangeEvent(state, MonthStart(state.WeekNumber));

        /// <summary>申し込みを取り下げる（月のはじめだけ。来訪のあとの最初の交流戦は取り下げられない）。</summary>
        public static bool CancelApplication(GameState state, TournamentEvent ev)
        {
            if (ev.Kind != TournamentKind.Exchange || ev.Result != null || !TournamentsOpen(state)) return false;
            if (!TournamentSystem.CanChangeEntries(state) || !TournamentSystem.IsThisMonth(state, ev)) return false;
            state.TournamentEntries.RemoveAll(e => e.EventId == ev.Id);
            return state.TournamentEvents.Remove(ev);
        }

        private static TournamentEvent AddExchangeEvent(GameState state, int monthStartWeek)
        {
            var ev = new TournamentEvent
            {
                DefinitionId = ExchangeDefinitionId,
                Name = ExchangeName,
                Kind = TournamentKind.Exchange,
                Discipline = TournamentDiscipline.Best,
                Grade = TournamentGrade.Special,
                Year = GameCalendar.YearOf(monthStartWeek),
                Month = GameCalendar.MonthOfYear(monthStartWeek),
                Week = IsabellaBalance.ExchangeWeekOfMonth,
            };
            state.TournamentEvents.Add(ev);
            return ev;
        }

        // ==================== 出場 ====================

        /// <summary>その部門の出場（無ければ null）。</summary>
        public static TournamentEntry? SlotEntry(GameState state, TournamentEvent ev, TournamentDiscipline d) =>
            state.TournamentEntries.FirstOrDefault(e => e.EventId == ev.Id && e.Discipline == d);

        /// <summary>その冒険者がその部門で交流戦に出られない理由（出られるなら null）。同じ交流戦のほかの部門に出ていれば、入れ替えられる。</summary>
        public static string? EntryBlockReason(GameState state, TournamentEvent ev, Adventurer a)
        {
            if (!TournamentSystem.CanChangeEntries(state)) return "出場を決められるのは月のはじめだけ";
            if (ev.Kind != TournamentKind.Exchange || !TournamentSystem.IsThisMonth(state, ev) || ev.Result != null) return "今月の交流戦ではない";
            if (a.IsRetired) return "引退している";
            if (a.IsDispatched) return "出撃中";
            if (a.IsOnLoan) return "派遣中";
            if (a.Injury != InjurySeverity.None) return "負傷している";
            if (a.IsPoisoned) return "毒状態";
            if (TournamentSystem.EntryOf(state, a.Id) is { } other && other.EventId != ev.Id) return "今月はほかの大会に出る（1人1か月1大会）";
            return null;
        }

        /// <summary>その部門に出す（前にその部門に出ていた子・この子のほかの部門の出場は外す）。訓練所に入っていれば外す。</summary>
        public static bool TryEnter(GameState state, TournamentEvent ev, Adventurer a, TournamentDiscipline d, TournamentPrep prep)
        {
            if (!ExchangeDisciplines.Contains(d) || EntryBlockReason(state, ev, a) != null) return false;
            state.TournamentEntries.RemoveAll(e => e.EventId == ev.Id && (e.Discipline == d || e.AdventurerId == a.Id));
            state.TrainingAssignments.Remove(a.Id);
            state.TournamentEntries.Add(new TournamentEntry { EventId = ev.Id, AdventurerId = a.Id, Prep = prep, Discipline = d });
            return true;
        }

        /// <summary>
        /// 最初の交流戦の月のはじめに、空いている部門へ出られる子を入れておく（勝ちやすさ＝部門の強さ÷相手の強さが高い組から）。
        /// 画面で入れ替えられる。週の決算のあと（新しい週）に呼ぶ。
        /// </summary>
        public static void AutoFillFirstExchange(GameState state)
        {
            if (TournamentsOpen(state) || ExchangeThisMonth(state) is not { Result: null } ev || !TournamentSystem.CanChangeEntries(state)) return;
            while (true)
            {
                var open = ExchangeDisciplines.Where(d => SlotEntry(state, ev, d) == null).ToList();
                var used = state.TournamentEntries.Where(e => e.EventId == ev.Id).Select(e => e.AdventurerId).ToHashSet();
                var best = open.SelectMany(d => state.Adventurers
                        .Where(a => !used.Contains(a.Id) && EntryBlockReason(state, ev, a) == null)
                        .Select(a => (d, a, ratio: TournamentSystem.DisciplineStrength(a, d) / OpponentStrength(state, d))))
                    .OrderByDescending(x => x.ratio).FirstOrDefault();
                if (best.a == null) return;
                TryEnter(state, ev, best.a, best.d, TournamentPrep.Rest);
            }
        }

        // ==================== 試合 ====================

        /// <summary>この週の交流戦を行う。</summary>
        public List<ExchangeOutcome> ResolveWeek(GameState state)
        {
            var due = state.TournamentEvents.Where(e => e.Kind == TournamentKind.Exchange && e.Result == null
                && e.Year == GameCalendar.YearOf(state.WeekNumber) && e.Month == GameCalendar.MonthOfYear(state.WeekNumber)
                && e.Week == GameCalendar.WeekOfMonth(state.WeekNumber)).ToList();
            return due.Select(ev => Resolve(state, ev)).ToList();
        }

        /// <summary>1回の交流戦を行い、ご褒美を与える（テストからも呼ぶ）。</summary>
        public ExchangeOutcome Resolve(GameState state, TournamentEvent ev)
        {
            var outcome = new ExchangeOutcome { Event = ev, First = !TournamentsOpen(state) };
            foreach (var d in ExchangeDisciplines)
            {
                double opp = OpponentStrength(state, d);
                var bout = new ExchangeBout { Discipline = d, OpponentName = IsabellaBalance.Opponent(d), OpponentStrength = opp };
                var entry = SlotEntry(state, ev, d);
                var a = entry?.AdventurerId is Guid id ? state.Adventurers.FirstOrDefault(x => x.Id == id && !x.IsRetired && x.Injury != InjurySeverity.Severe) : null;
                if (a == null)
                {
                    bout.OurName = "（不戦敗）";
                }
                else
                {
                    bout.AdventurerId = a.Id;
                    bout.OurName = a.Name;
                    bout.OurStrength = TournamentSystem.MatchStrength(a, d);
                    double sa = bout.OurStrength * Luck(), sb = opp * Luck();
                    bout.Won = Next01() < BoutWinChance(sa, sb);
                    a.CurrentHP = Math.Max(1, a.CurrentHP - (int)Math.Round(a.MaxHP * TournamentBalance.HpCostPerMatch));
                }
                outcome.Bouts.Add(bout);
            }

            outcome.Won = outcome.Wins >= 2;
            state.ExchangeMatchesPlayed++;
            if (outcome.Won)
            {
                outcome.FirstWin = state.ExchangeWins == 0;
                state.ExchangeWins++;
                if (outcome.FirstWin)
                {
                    state.GuestTrainerPending = true;
                }
                else
                {
                    outcome.Prize = IsabellaBalance.ExchangePrize;
                    state.Gold += outcome.Prize;
                    outcome.Mood = MasterMoodSystem.Adjust(state, IsabellaBalance.ExchangeMood);
                }
            }

            if (outcome.First)
            {
                outcome.OpenedFacility = OpenFirstTrainingFacility(state, outcome.Bouts);
                state.TournamentCalendarFromWeek = NextMonthStart(state.WeekNumber);
            }

            ev.Result = new TournamentResult
            {
                WinnerIsOurs = outcome.Won,
                WinnerName = outcome.Won ? "当ギルド" : "白百合の杖",
                Matches = outcome.Bouts.Select((b, i) => new TournamentMatch
                {
                    Round = i + 1, NameA = b.OurName, NameB = b.OpponentName, OursA = true,
                    StrengthA = b.OurStrength, StrengthB = b.OpponentStrength, AWon = b.Won,
                }).ToList(),
            };
            return outcome;
        }

        /// <summary>最初の交流戦のあとに開く訓練所：いちばん善戦した部門（自分の強さ÷相手の強さが最も高い）の、まだ開いていない訓練所。</summary>
        private static FacilityUnlockNotice? OpenFirstTrainingFacility(GameState state, IReadOnlyList<ExchangeBout> bouts)
        {
            var candidates = bouts.OrderByDescending(b => b.OpponentStrength > 0 ? b.OurStrength / b.OpponentStrength : 0)
                .Select(b => FacilityUnlockSystem.FacilityOf(b.Discipline))
                .Where(t => FacilityUnlockSystem.GetUnlockedLevel(state, t) == 0).ToList();
            if (candidates.Count == 0) return null;
            state.FacilityUnlockedLevels[candidates[0]] = 1;
            return FacilityUnlockSystem.FirstTrainingNotice(candidates[0]);
        }

        private double Next01() => _rng.NextInt(0, 999_999) / 1_000_000.0;

        private double Luck() => TournamentBalance.LuckMin + (TournamentBalance.LuckMax - TournamentBalance.LuckMin) * Next01();

        // ==================== 派遣の教官 ====================

        /// <summary>今いる派遣の教官（いなければ null）。</summary>
        public static Adventurer? GuestTrainer(GameState state) => state.RetiredAdventurers.FirstOrDefault(a => a.IsGuest);

        /// <summary>
        /// 派遣の教官の出入り（週の決算の最後、施設の工事のあとに呼ぶ）。期限の週が来たら帰り（教官のポストも外す）、
        /// 待っていて訓練所が1つ以上建っていれば来る（能力は現役の上位の平均。空いている訓練所の教官に就ける）。
        /// </summary>
        public static GuestTrainerChange ProcessGuestTrainer(GameState state)
        {
            var change = new GuestTrainerChange();
            if (state.GuestTrainerUntilWeek is int until && state.WeekNumber >= until)
            {
                var guest = GuestTrainer(state);
                if (guest != null)
                {
                    var advisor = new AdvisorSystem();
                    foreach (var facility in state.AssignedTrainers.Where(kv => kv.Value == guest.Id).Select(kv => kv.Key).ToList())
                        advisor.UnassignTrainer(state, facility);
                    state.RetiredAdventurers.Remove(guest);
                    change.Left = guest;
                }
                state.GuestTrainerUntilWeek = null;
            }

            var built = FacilityUnlockSystem.TrainingFacilities.Where(t => state.GetFacilityLevel(t) >= 1).ToList();
            if (state.GuestTrainerPending && built.Count > 0)
            {
                var guest = CreateGuestTrainer(state);
                state.RetiredAdventurers.Add(guest);
                state.GuestTrainerPending = false;
                state.GuestTrainerUntilWeek = state.WeekNumber + IsabellaBalance.GuestTrainerWeeks;
                change.Arrived = guest;
                var empty = built.Where(t => !state.AssignedTrainers.TryGetValue(t, out var id) || id == null).ToList();
                if (empty.Count > 0 && new AdvisorSystem().TryAssignTrainer(state, empty[0], guest.Id))
                    change.AssignedTo = empty[0];
            }
            return change;
        }

        /// <summary>派遣の教官を作る：能力は現役の上位 GuestTrainerTopCount 人（能力の合計の順）の能力ごとの平均。</summary>
        public static Adventurer CreateGuestTrainer(GameState state)
        {
            var top = state.Adventurers.Where(a => !a.IsRetired)
                .OrderByDescending(a => AdventurerStatAccessor.AllStatNames.Sum(s => AdventurerStatAccessor.GetStat(a, s)))
                .Take(Math.Max(1, IsabellaBalance.GuestTrainerTopCount)).ToList();
            var guest = new Adventurer
            {
                Name = IsabellaBalance.GuestTrainerName,
                JobClass = JobClass.Warrior,
                Age = IsabellaBalance.GuestTrainerAge,
                IsRetired = true,
                IsGuest = true,
            };
            foreach (var stat in AdventurerStatAccessor.AllStatNames)
            {
                int value = top.Count == 0 ? 40 : (int)Math.Round(top.Average(a => AdventurerStatAccessor.GetStat(a, stat)));
                AdventurerStatAccessor.SetStat(guest, stat, value);
            }
            if (TraitCatalog.FindById(IsabellaBalance.GuestTrainerTraitId) != null)
                guest.TraitIds.Add(IsabellaBalance.GuestTrainerTraitId);
            guest.CurrentHP = guest.MaxHP;
            return guest;
        }

        // ==================== 依頼の開放 ====================

        /// <summary>初めて入賞（ベスト4以上）していて依頼がまだ開いていなければ、次の季節のはじめから届くようにする。開いたら true。</summary>
        public static bool CheckCommissionUnlock(GameState state)
        {
            if (CommissionsOpen(state) || state.TournamentPlacingsTotal <= 0) return false;
            state.CommissionsFromWeek = NextSeasonStart(state.WeekNumber);
            return true;
        }
    }
}
