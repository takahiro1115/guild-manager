using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 大会（2026年10月・§0.82、→ docs/検討中_大会と育成の栄光.md）：暦・出場・試合・ご褒美・招待。
    ///  - 暦：年のはじめに、定例（新人戦・G1×5）と、進み具合に応じた数の地方大会（G3）・王都大会（G2）を置く（→ EnsureSchedule）。
    ///    月に0〜3つ。同じ部門の G3 → G2 → G1 の順は崩さない。招待大会は条件を満たすと2か月後に置く（→ CheckInvitations）。
    ///  - 出場：月のはじめに、大会ごとに1ギルド2人まで（部隊戦は1部隊）。1人が1か月に出られるのは1つ。
    ///    出場者はその月は出撃しない・訓練所に入らない。過ごし方は休養か追い込み（→ TournamentPrep）。
    ///  - 試合：8人の勝ち抜き。強さ＝部門の強さ×HPの補正×満足度×特性×運。勝率＝a^k÷(a^k＋b^k)。1試合ごとにHPを使う。
    ///  - ご褒美：賞金・機嫌・優勝者の満足度・勝ち鞍（→ Adventurer.TournamentRecords）・入賞と賞金の累計（施設のご褒美、→ FacilityUnlockSystem）。
    /// 暦は state に依存する固定の種で置く（同じゲームなら同じ暦）。試合だけ注入した乱数を使う。
    /// </summary>
    public class TournamentSystem
    {
        private readonly IRng _rng;

        public TournamentSystem(IRng rng)
        {
            _rng = rng;
        }

        // ==================== 部門の強さ ====================

        /// <summary>個人の部門の強さ（装備・特性込みの実効値）。部隊戦・得意・抽選は得意な部門で数える。</summary>
        public static double DisciplineStrength(Adventurer a, TournamentDiscipline discipline) => discipline switch
        {
            TournamentDiscipline.Sword => a.GetEffectiveStat("STR") * 1.0 + a.GetEffectiveStat("VIT") * 0.6 + a.GetEffectiveStat("AGI") * 0.4,
            TournamentDiscipline.Magic => a.GetEffectiveStat("INT") * 1.0 + a.GetEffectiveStat("MND") * 0.8 + a.GetEffectiveStat("LDR") * 0.2,
            TournamentDiscipline.Skill => a.GetEffectiveStat("DEX") * 1.0 + a.GetEffectiveStat("AGI") * 0.8 + a.GetEffectiveStat("STR") * 0.2,
            _ => DisciplineStrength(a, BestDiscipline(a)),
        };

        /// <summary>得意な部門：剣・魔・技のうち、相手の倍率で割った強さ（勝ちやすさ）が最も高いもの。</summary>
        public static TournamentDiscipline BestDiscipline(Adventurer a) =>
            new[] { TournamentDiscipline.Sword, TournamentDiscipline.Magic, TournamentDiscipline.Skill }
                .OrderByDescending(d => DisciplineStrength(a, d) / TournamentBalance.DisciplineFactor(d))
                .First();

        /// <summary>その大会で、この冒険者が戦う部門（得意・抽選の大会は得意な部門）。</summary>
        public static TournamentDiscipline DisciplineFor(TournamentEvent ev, Adventurer a) =>
            ev.Discipline is TournamentDiscipline.Best or TournamentDiscipline.Random ? BestDiscipline(a) : ev.Discipline;

        /// <summary>部門の表示名。</summary>
        public static string DisciplineLabel(TournamentDiscipline d) => d switch
        {
            TournamentDiscipline.Sword => "剣",
            TournamentDiscipline.Magic => "魔",
            TournamentDiscipline.Skill => "技",
            TournamentDiscipline.Party => "部隊戦",
            _ => "得意な部門",
        };

        /// <summary>順位の表示名（1＝優勝、2＝準優勝、4＝ベスト4、8＝ベスト8）。</summary>
        public static string PlacingLabel(int placing) => placing switch
        {
            1 => "優勝",
            2 => "準優勝",
            _ => $"ベスト{placing}",
        };

        /// <summary>格の表示名。</summary>
        public static string GradeLabel(TournamentGrade g) => g == TournamentGrade.Special ? "特別" : g.ToString();

        /// <summary>試合の強さの HP の補正＝HpFactorBase＋(1−HpFactorBase)×HP比率。</summary>
        public static double HpFactor(double hpRatio) =>
            TournamentBalance.HpFactorBase + (1 - TournamentBalance.HpFactorBase) * Math.Clamp(hpRatio, 0, 1);

        /// <summary>個人の試合の強さ（運を除く）：部門の強さ×HP×満足度×豪胆。</summary>
        public static double MatchStrength(Adventurer a, TournamentDiscipline discipline)
        {
            double s = DisciplineStrength(a, discipline) * HpFactor(a.MaxHP > 0 ? (double)a.CurrentHP / a.MaxHP : 0);
            if (a.Satisfaction < TournamentBalance.SatisfactionLowThreshold) s *= TournamentBalance.SatisfactionLowFactor;
            else if (a.Satisfaction >= TournamentBalance.SatisfactionHighThreshold) s *= TournamentBalance.SatisfactionHighFactor;
            if (a.HasTrait(TraitCatalog.BraveId)) s *= TournamentBalance.BraveFactor;
            return s;
        }

        /// <summary>部隊戦の強さ（運を除く）：走破力×平均HP×(1＋相性の平均÷100×上限)。</summary>
        public static double PartyStrength(GameState state, IReadOnlyList<Adventurer> members)
        {
            if (members.Count == 0) return 0;
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            double power = PartyFormationSystem.CalculateMetrics(party, state, atFullHp: true).TraversalPower;
            double hp = members.Sum(m => m.CurrentHP) / (double)Math.Max(1, members.Sum(m => m.MaxHP));
            var pairs = members.SelectMany((a, i) => members.Skip(i + 1).Select(b => CompatibilitySystem.GetCompatibility(state, a.Id, b.Id))).ToList();
            double compat = pairs.Count > 0 ? Math.Max(0, pairs.Average()) / CompatibilityBalance.MaxValue : 0;
            return power * HpFactor(hp) * (1 + compat * TournamentBalance.PartyCompatibilityBonusMax);
        }

        /// <summary>
        /// 勝てそうかの目安（画面の表示用）：相手の幅の中央の強さの者に、優勝までの回数（8人なら3回）続けて勝つ見込み。
        /// 運と、試合ごとのHPの減りは含めない。
        /// </summary>
        public static double EstimateWinChance(TournamentEvent ev, TournamentDiscipline discipline, double strength)
        {
            var (min, max) = OpponentRange(ev, discipline);
            double mid = (min + max) / 2;
            if (strength <= 0 || mid <= 0) return 0;
            double e = TournamentBalance.WinExponent;
            double p = Math.Pow(strength, e) / (Math.Pow(strength, e) + Math.Pow(mid, e));
            int rounds = (int)Math.Round(Math.Log2(Math.Max(2, TournamentBalance.BracketSize)));
            return Math.Pow(p, rounds);
        }

        /// <summary>名前にライバルギルドの名前を添える（§0.93。「セシリア〈白百合の杖〉」。ギルドが空なら名前だけ）。</summary>
        public static string WithGuild(string name, string guild) => guild.Length > 0 ? $"{name}〈{guild}〉" : name;

        /// <summary>勝てそうかの目安の言葉（本命＝4割以上／対抗＝15%以上／穴＝3%以上／厳しい）。</summary>
        public static string WinChanceLabel(double chance) =>
            chance >= 0.4 ? "本命" : chance >= 0.15 ? "対抗" : chance >= 0.03 ? "穴" : "厳しい";

        /// <summary>その大会の相手の強さの幅（部門の倍率・王都の水準の年ごとの上昇込み）。</summary>
        public static (double Min, double Max) OpponentRange(TournamentEvent ev, TournamentDiscipline discipline)
        {
            var def = TournamentBalance.Find(ev.DefinitionId);
            if (def == null) return (0, 0);
            double scale = TournamentBalance.DisciplineFactor(discipline) * (1 + TournamentBalance.YearlyGrowth * Math.Max(0, ev.Year - 1));
            return (def.MinStrength * scale, def.MaxStrength * scale);
        }

        // ==================== 暦 ====================

        /// <summary>今の月か（大会の Year・Month が今の週と同じ）。</summary>
        public static bool IsThisMonth(GameState state, TournamentEvent ev) =>
            ev.Year == GameCalendar.YearOf(state.WeekNumber) && ev.Month == GameCalendar.MonthOfYear(state.WeekNumber);

        /// <summary>今月の大会（週の順）。</summary>
        public static List<TournamentEvent> EventsThisMonth(GameState state) =>
            state.TournamentEvents.Where(e => IsThisMonth(state, e)).OrderBy(e => e.Week).ToList();

        /// <summary>その年の大会（月・週の順）。</summary>
        public static List<TournamentEvent> EventsOfYear(GameState state, int year) =>
            state.TournamentEvents.Where(e => e.Year == year).OrderBy(e => e.Month).ThenBy(e => e.Week).ToList();

        /// <summary>地方・王都の大会の年間の数＝基本＋年数（上限あり）＋入賞の累計÷N（上限あり）。上限 LocalMax。</summary>
        public static int LocalCount(GameState state, int year) =>
            Math.Min(TournamentBalance.LocalMax,
                TournamentBalance.LocalBaseCount
                + Math.Min(TournamentBalance.LocalYearBonusMax, Math.Max(0, year - 1))
                + Math.Min(TournamentBalance.LocalPlacingsBonusMax, state.TournamentPlacingsTotal / Math.Max(1, TournamentBalance.LocalPlacingsPer)));

        /// <summary>
        /// 今の年の暦が無ければ置く（定例と、地方・王都の大会）。前の月までの出場の記録は消す。何度呼んでもよい。
        /// 大会はイザベラとの最初の交流戦のあとに開く（§0.84）：それまでは置かず、開いた年は TournamentCalendarFromWeek より前の大会を置かない。
        /// </summary>
        public static void EnsureSchedule(GameState state)
        {
            int year = GameCalendar.YearOf(state.WeekNumber);
            state.TournamentEntries.RemoveAll(entry =>
            {
                var ev = state.TournamentEvents.FirstOrDefault(e => e.Id == entry.EventId);
                return ev == null || !IsThisMonth(state, ev);
            });
            if (state.TournamentCalendarFromWeek is not int from)
                return;
            if (state.TournamentEvents.Any(e => e.Year == year && e.Kind is not (TournamentKind.Invite or TournamentKind.Exchange)))
                return;
            state.TournamentEvents.AddRange(BuildYear(state, year)
                .Where(e => GameCalendar.WeekNumberOf(e.Year, e.Month, e.Week) >= from));
        }

        /// <summary>出場者が戦う部門（交流戦は受け持った部門、ほかは DisciplineFor）。</summary>
        public static TournamentDiscipline EntryDiscipline(TournamentEvent ev, TournamentEntry entry, Adventurer a) =>
            entry.Discipline ?? DisciplineFor(ev, a);

        private static readonly (TournamentDiscipline Discipline, string LocalId, string RoyalId, string ClassicId)[] Disciplines =
        {
            (TournamentDiscipline.Sword, "local_sword", "royal_sword", "classic_sword"),
            (TournamentDiscipline.Magic, "local_magic", "royal_magic", "classic_magic"),
            (TournamentDiscipline.Skill, "local_skill", "royal_skill", "classic_skill"),
        };

        /// <summary>1年分の暦。地方大会は春〜夏（2の月から、その部門のG1の3か月前まで）、王都大会はその後でG1の前の月まで。</summary>
        private static List<TournamentEvent> BuildYear(GameState state, int year)
        {
            var rng = new SeededRng(unchecked(year * 7919 + (state.SavedParties.FirstOrDefault()?.Id.GetHashCode() ?? 0)));
            var events = new List<TournamentEvent>();
            foreach (var def in TournamentBalance.Definitions.Where(d => d.Kind is TournamentKind.Rookie or TournamentKind.Classic or TournamentKind.Final))
                events.Add(NewEvent(def, year, def.Month, def.Week, def.Discipline, def.Name));

            int total = LocalCount(state, year);
            int start = rng.NextInt(0, Disciplines.Length - 1);
            for (int i = 0; i < Disciplines.Length; i++)
            {
                var d = Disciplines[(start + i) % Disciplines.Length];
                int count = total / Disciplines.Length + (i < total % Disciplines.Length ? 1 : 0);
                if (count <= 0) continue;
                int royalCount = count / 2;
                int localCount = count - royalCount;
                int g1Month = TournamentBalance.Find(d.ClassicId)?.Month ?? 8;
                var localDef = TournamentBalance.Find(d.LocalId)!;
                var royalDef = TournamentBalance.Find(d.RoyalId)!;
                int lastLocalMonth = 0;
                for (int k = 0; k < localCount; k++)
                {
                    int month = rng.NextInt(2, Math.Max(2, g1Month - 3));
                    lastLocalMonth = Math.Max(lastLocalMonth, month);
                    string region = TournamentBalance.LocalRegions.Count > 0 ? TournamentBalance.LocalRegions[rng.NextInt(0, TournamentBalance.LocalRegions.Count - 1)] : "";
                    events.Add(NewEvent(localDef, year, month, rng.NextInt(1, 4), d.Discipline, localDef.Name.Replace("{地方}", region)));
                }
                for (int k = 0; k < royalCount; k++)
                {
                    int month = rng.NextInt(Math.Min(g1Month - 1, Math.Max(4, lastLocalMonth + 1)), g1Month - 1);
                    events.Add(NewEvent(royalDef, year, month, rng.NextInt(1, 4), d.Discipline, royalDef.Name));
                }
            }
            return events;
        }

        private static TournamentEvent NewEvent(TournamentDefinition def, int year, int month, int week, TournamentDiscipline discipline, string name) => new()
        {
            DefinitionId = def.Id,
            Name = name,
            Kind = def.Kind,
            Discipline = discipline,
            Grade = def.Grade,
            Year = year,
            Month = month,
            Week = week,
        };

        // ==================== 招待 ====================

        /// <summary>
        /// 招待大会を置く：新しいフィールドを開いた（領主杯）・G1で優勝した（御前試合、1年1回）・入賞の累計が10回（辺境伯杯、1回）。
        /// 2か月後（InviteLeadMonths）の第3週に置き、置いた大会を返す（知らせに使う）。
        /// </summary>
        public static List<TournamentEvent> CheckInvitations(GameState state, IEnumerable<DungeonField> newlyUnlockedFields, IEnumerable<TournamentEvent> resolvedThisWeek)
        {
            var added = new List<TournamentEvent>();
            if (!IsabellaSystem.TournamentsOpen(state))
                return added; // 大会が開く前（§0.84）は招待も無い
            int year = GameCalendar.YearOf(state.WeekNumber);
            int month = GameCalendar.MonthOfYear(state.WeekNumber);
            int target = (year - 1) * 12 + month - 1 + TournamentBalance.InviteLeadMonths;
            int targetYear = target / 12 + 1, targetMonth = target % 12 + 1;
            var rng = new SeededRng(unchecked(state.WeekNumber * 31 + state.TournamentInviteKeys.Count));

            void Add(string defId, string key, string name, TournamentDiscipline discipline, Guid? invited, string reward)
            {
                var def = TournamentBalance.Find(defId);
                if (def == null || !state.TournamentInviteKeys.Add(key)) return;
                var ev = NewEvent(def, targetYear, targetMonth, def.Week > 0 ? def.Week : 3, discipline, name);
                ev.InvitedAdventurerId = invited;
                ev.SpecialReward = reward;
                state.TournamentEvents.Add(ev);
                added.Add(ev);
            }

            TournamentDiscipline RandomDiscipline() => Disciplines[rng.NextInt(0, Disciplines.Length - 1)].Discipline;

            foreach (var field in newlyUnlockedFields)
                Add("invite_lord", $"lord:{field.Id}", (TournamentBalance.Find("invite_lord")?.Name ?? "{フィールド}の領主杯").Replace("{フィールド}", field.Name), RandomDiscipline(), null, "unique");

            var g1Win = resolvedThisWeek.Where(e => e.Grade == TournamentGrade.G1 && e.Result?.WinnerIsOurs == true)
                .SelectMany(e => e.Result!.Placings.Where(p => p.Placing == 1 && p.AdventurerId != null)).FirstOrDefault();
            if (g1Win != null)
                Add("invite_royal", $"royal:{year}", TournamentBalance.Find("invite_royal")?.Name ?? "王族の御前試合", TournamentDiscipline.Best, g1Win.AdventurerId, "");

            if (state.TournamentPlacingsTotal >= 10)
                Add("invite_margrave", "margrave", TournamentBalance.Find("invite_margrave")?.Name ?? "辺境伯杯", RandomDiscipline(), null, "halfcost");
            return added;
        }

        // ==================== 出場 ====================

        /// <summary>出場を決められるのは月のはじめだけ（訓練の割り振りと同じ、§0.70）。</summary>
        public static bool CanChangeEntries(GameState state) => GameCalendar.IsFirstWeekOfMonth(state.WeekNumber);

        /// <summary>今月、大会に出る（出場者・出場する部隊のメンバー）か。出場者はその月は出撃・訓練をしない。</summary>
        public static bool IsEntered(GameState state, Guid adventurerId) => EntryOf(state, adventurerId) != null;

        /// <summary>今月のこの冒険者の出場（部隊戦なら部隊のメンバーとして）。無ければ null。</summary>
        public static TournamentEntry? EntryOf(GameState state, Guid adventurerId)
        {
            foreach (var entry in state.TournamentEntries)
            {
                var ev = state.TournamentEvents.FirstOrDefault(e => e.Id == entry.EventId);
                if (ev == null || !IsThisMonth(state, ev)) continue;
                if (entry.AdventurerId == adventurerId) return entry;
                if (entry.PartyId is Guid pid && state.SavedParties.FirstOrDefault(p => p.Id == pid)?.MemberIds.Contains(adventurerId) == true) return entry;
            }
            return null;
        }

        /// <summary>その大会のギルドの出場（個人・部隊）。</summary>
        public static List<TournamentEntry> EntriesOf(GameState state, TournamentEvent ev) =>
            state.TournamentEntries.Where(e => e.EventId == ev.Id).ToList();

        /// <summary>その冒険者がその大会に出られない理由（出られるなら null）。</summary>
        public static string? EntryBlockReason(GameState state, TournamentEvent ev, Adventurer a)
        {
            if (!CanChangeEntries(state)) return "出場を決められるのは月のはじめだけ";
            if (!IsThisMonth(state, ev) || ev.Result != null) return "今月の大会ではない";
            if (ev.Discipline == TournamentDiscipline.Party) return "部隊で出る大会";
            if (a.IsRetired) return "引退している";
            if (a.IsDispatched) return "出撃中";
            if (a.IsOnLoan) return "派遣中";
            if (a.Injury != InjurySeverity.None) return "負傷している";
            if (a.IsPoisoned) return "毒状態";
            if (IsEntered(state, a.Id)) return "今月はほかの大会に出る（1人1か月1大会）";
            if (EntriesOf(state, ev).Count >= TournamentBalance.EntrantsPerGuild) return $"この大会にはもう{TournamentBalance.EntrantsPerGuild}人出る";
            return QualificationBlock(ev, a);
        }

        /// <summary>出場の資格（新人戦＝その年に加わった者、G2＝G3でベスト4、G1＝G2でベスト4、最強決定戦＝その年にG1優勝かG2で2勝、招待）。</summary>
        public static string? QualificationBlock(TournamentEvent ev, Adventurer a)
        {
            if (ev.InvitedAdventurerId is Guid invited && invited != a.Id) return "招待された者だけ";
            var discipline = DisciplineFor(ev, a);
            bool Placed(TournamentGrade atLeast) => a.TournamentRecords.Any(r =>
                r.Year >= ev.Year - 1 && r.Discipline == discipline && r.Placing <= 4 && GradeRank(r.Grade) >= GradeRank(atLeast));
            switch (ev.Kind)
            {
                case TournamentKind.Rookie:
                    return a.JoinedYear == ev.Year ? null : "その年に加わった者だけ";
                case TournamentKind.Royal:
                    return Placed(TournamentGrade.G3) ? null : $"{DisciplineLabel(discipline)}のG3でベスト4以上が要る";
                case TournamentKind.Classic:
                    return Placed(TournamentGrade.G2) ? null : $"{DisciplineLabel(discipline)}のG2でベスト4以上が要る";
                case TournamentKind.Final:
                    int g1Wins = a.TournamentRecords.Count(r => r.Year == ev.Year && r.Grade == TournamentGrade.G1 && r.Placing == 1);
                    int g2Wins = a.TournamentRecords.Count(r => r.Year == ev.Year && GradeRank(r.Grade) == GradeRank(TournamentGrade.G2) && r.Placing == 1);
                    return g1Wins > 0 || g2Wins >= 2 ? null : "その年にG1で優勝、またはG2で2勝した者だけ";
                default:
                    return null;
            }
        }

        /// <summary>格の強さの順（G3＜G2＝特別＜G1）。施設のご褒美では特別をG2として数える。</summary>
        public static int GradeRank(TournamentGrade g) => g switch
        {
            TournamentGrade.G3 => 1,
            TournamentGrade.G2 or TournamentGrade.Special => 2,
            _ => 3,
        };

        /// <summary>部隊戦に出られない理由（出られるなら null）。部隊の出撃できるメンバーが2人以上、どこかで30Fのボスを倒していること。</summary>
        public static string? PartyEntryBlockReason(GameState state, TournamentEvent ev, SavedParty party)
        {
            if (!CanChangeEntries(state)) return "出場を決められるのは月のはじめだけ";
            if (ev.Discipline != TournamentDiscipline.Party || !IsThisMonth(state, ev) || ev.Result != null) return "今月の部隊戦ではない";
            if (EntriesOf(state, ev).Count >= 1) return "この大会にはもう1部隊出る";
            int deepest = state.DungeonFields.Select(f => f.Bosses.Where(b => b.EverDefeated).Select(b => b.Floor).DefaultIfEmpty(0).Max()).DefaultIfEmpty(0).Max();
            if (deepest < TournamentBalance.PartyEntryMinFloor) return $"どこかのフィールドで{TournamentBalance.PartyEntryMinFloor}Fのボスを倒していること";
            var members = PartyMembers(state, party);
            if (members.Count < 2) return "出られるメンバーが2人以上要る";
            if (members.Any(m => m.IsDispatched)) return "部隊が出撃中";
            if (members.Any(m => m.IsOnLoan)) return "部隊に派遣中の者がいる";
            if (members.Any(m => IsEntered(state, m.Id))) return "メンバーがほかの大会に出る";
            return null;
        }

        /// <summary>部隊のメンバーのうち、大会に出られる者（現役・重傷でない）。</summary>
        public static List<Adventurer> PartyMembers(GameState state, SavedParty party) =>
            party.MemberIds.Select(id => state.Adventurers.FirstOrDefault(a => a.Id == id))
                .OfType<Adventurer>().Where(a => !a.IsRetired && a.Injury != InjurySeverity.Severe).ToList();

        /// <summary>出場を決める。訓練所に入っていれば外す（大会の月は訓練しない）。出られなければ false。</summary>
        public static bool TryEnter(GameState state, TournamentEvent ev, Adventurer a, TournamentPrep prep)
        {
            if (EntryBlockReason(state, ev, a) != null) return false;
            state.TrainingAssignments.Remove(a.Id);
            state.TournamentEntries.Add(new TournamentEntry { EventId = ev.Id, AdventurerId = a.Id, Prep = prep });
            return true;
        }

        /// <summary>部隊戦に出る。部隊のメンバーは訓練所から外す。</summary>
        public static bool TryEnterParty(GameState state, TournamentEvent ev, SavedParty party, TournamentPrep prep)
        {
            if (PartyEntryBlockReason(state, ev, party) != null) return false;
            foreach (var id in party.MemberIds) state.TrainingAssignments.Remove(id);
            state.TournamentEntries.Add(new TournamentEntry { EventId = ev.Id, PartyId = party.Id, Prep = prep });
            return true;
        }

        /// <summary>出場をやめる（月のはじめだけ）。</summary>
        public static bool Withdraw(GameState state, TournamentEntry entry) =>
            CanChangeEntries(state) && state.TournamentEntries.Remove(entry);

        /// <summary>過ごし方を変える（月のはじめだけ）。</summary>
        public static bool SetPrep(GameState state, TournamentEntry entry, TournamentPrep prep)
        {
            if (!CanChangeEntries(state)) return false;
            entry.Prep = prep;
            return true;
        }

        // ==================== 試合 ====================

        private sealed class Competitor
        {
            public string Name = "";
            public bool Ours;
            public double Strength;
            public TournamentDiscipline Discipline;
            public Adventurer? Adventurer;
            public SavedParty? Party;
            public List<Adventurer> Members = new();
            public int Placing;
            /// <summary>ライバルギルドの子（§0.93）。名前の無い相手・自分のギルドは null。</summary>
            public RivalMember? Rival;
            public string Guild = "";
        }

        /// <summary>
        /// この週の大会を行う（state.WeekNumber の月・週の、まだ結果の無い大会）。結果を大会に書き、ご褒美を与える。行った大会を返す。
        /// </summary>
        public List<TournamentEvent> ResolveWeek(GameState state)
        {
            int year = GameCalendar.YearOf(state.WeekNumber);
            int month = GameCalendar.MonthOfYear(state.WeekNumber);
            int week = GameCalendar.WeekOfMonth(state.WeekNumber);
            var resolved = new List<TournamentEvent>();
            foreach (var ev in state.TournamentEvents.Where(e => e.Year == year && e.Month == month && e.Week == week && e.Result == null
                         && e.Kind != TournamentKind.Exchange).ToList()) // 交流戦は IsabellaSystem が行う（§0.84）
            {
                Resolve(state, ev);
                resolved.Add(ev);
            }
            return resolved;
        }

        /// <summary>1つの大会を行う（テストからも呼ぶ）。</summary>
        public void Resolve(GameState state, TournamentEvent ev)
        {
            var ours = new List<Competitor>();
            foreach (var entry in EntriesOf(state, ev))
            {
                if (entry.AdventurerId is Guid aid && state.Adventurers.FirstOrDefault(a => a.Id == aid && !a.IsRetired) is { } a
                    && a.Injury != InjurySeverity.Severe)
                {
                    var d = DisciplineFor(ev, a);
                    ours.Add(new Competitor { Name = a.Name, Ours = true, Strength = MatchStrength(a, d), Discipline = d, Adventurer = a });
                }
                else if (entry.PartyId is Guid pid && state.SavedParties.FirstOrDefault(p => p.Id == pid) is { } party)
                {
                    var members = PartyMembers(state, party);
                    if (members.Count > 0)
                        ours.Add(new Competitor { Name = party.Name, Ours = true, Strength = PartyStrength(state, members), Discipline = TournamentDiscipline.Party, Party = party, Members = members });
                }
            }

            // 相手：まずライバルギルドの子（§0.93。得意な部門で、強さが大会の幅に入る子）、空いた枠は名前の無い「王都の腕自慢」。
            // 名前の無い相手の部門の倍率は大会の部門（得意の大会は先頭の出場者の部門）。
            var oppDiscipline = ev.Discipline is TournamentDiscipline.Best or TournamentDiscipline.Random
                ? (ours.FirstOrDefault()?.Discipline ?? TournamentDiscipline.Sword)
                : ev.Discipline;
            var (min, max) = OpponentRange(ev, oppDiscipline);
            var names = new HashSet<string>(ours.Select(o => o.Name));
            names.UnionWith(state.RivalGuilds.SelectMany(g => g.Members).Select(m => m.Name)); // 名前の無い相手がライバルと同じ名前にならないように
            var field = new List<Competitor>();
            foreach (var (member, guild) in RivalSystem.Entrants(state, ev, TournamentBalance.BracketSize - ours.Count))
            {
                names.Add(member.Name);
                field.Add(new Competitor { Name = member.Name, Strength = RivalSystem.MatchStrength(member, ev.Year), Discipline = member.Discipline, Rival = member, Guild = guild.Name });
            }
            while (ours.Count + field.Count < TournamentBalance.BracketSize)
            {
                string name = NameGenerator.GenerateUniqueFirstName(_rng.NextInt(0, 1) == 0 ? NameCulture.Western : NameCulture.Eastern, names, _rng);
                names.Add(name);
                field.Add(new Competitor { Name = ev.Discipline == TournamentDiscipline.Party ? $"{name}の一行" : name, Strength = min + (max - min) * Next01(), Discipline = oppDiscipline });
            }

            // 組み合わせ：ギルドの出場者は別の山へ（1回戦で当たらない）。残りは相手を散らす。
            var bracket = new Competitor?[TournamentBalance.BracketSize];
            for (int i = 0; i < ours.Count && i < 2; i++)
                bracket[i * TournamentBalance.BracketSize / 2] = ours[i];
            var rest = field.OrderBy(_ => _rng.NextInt(0, 1_000_000)).ToList();
            for (int i = 0, k = 0; i < bracket.Length; i++)
                if (bracket[i] == null) bracket[i] = rest[k++];

            var result = new TournamentResult();
            var alive = bracket.Select(c => c!).ToList();
            int round = 1;
            while (alive.Count > 1)
            {
                var next = new List<Competitor>();
                for (int i = 0; i < alive.Count; i += 2)
                {
                    var a = alive[i];
                    var b = alive[i + 1];
                    double sa = a.Strength * Luck(), sb = b.Strength * Luck();
                    double pa = Math.Pow(sa, TournamentBalance.WinExponent) / (Math.Pow(sa, TournamentBalance.WinExponent) + Math.Pow(sb, TournamentBalance.WinExponent));
                    bool aWon = Next01() < pa;
                    result.Matches.Add(new TournamentMatch { Round = round, NameA = a.Name, NameB = b.Name, GuildA = a.Guild, GuildB = b.Guild, OursA = a.Ours, OursB = b.Ours, StrengthA = sa, StrengthB = sb, AWon = aWon });
                    foreach (var c in new[] { a, b }.Where(c => c.Ours)) SpendHp(c);
                    var loser = aWon ? b : a;
                    loser.Placing = alive.Count; // 8人の1回戦で負け＝ベスト8、準決勝で負け＝ベスト4、決勝で負け＝2
                    next.Add(aWon ? a : b);
                }
                alive = next;
                round++;
            }
            alive[0].Placing = 1;
            result.WinnerName = alive[0].Name;
            result.WinnerIsOurs = alive[0].Ours;
            result.WinnerGuild = alive[0].Guild;
            foreach (var c in bracket.Where(c => c!.Rival != null))
                RivalSystem.Record(state, c!.Rival!, ev, c.Placing, state.WeekNumber);

            var def = TournamentBalance.Find(ev.DefinitionId);
            foreach (var c in ours)
            {
                int prize = def == null ? 0 : c.Placing switch { 1 => def.Prize1, 2 => def.Prize2, 4 => def.Prize4, _ => 0 };
                int mood = def == null ? 0 : c.Placing switch { 1 => def.Mood1, 2 => def.Mood2, 4 => def.Mood4, _ => 0 };
                state.Gold += prize;
                state.TournamentPrizeTotal += prize;
                if (mood != 0) MasterMoodSystem.Adjust(state, mood);
                if (c.Placing <= 4) state.TournamentPlacingsTotal++;
                var holders = c.Adventurer != null ? new List<Adventurer> { c.Adventurer } : c.Members;
                foreach (var h in holders)
                {
                    h.TournamentRecords.Add(new TournamentRecord
                    {
                        Year = ev.Year, Week = state.WeekNumber, DefinitionId = ev.DefinitionId, Name = ev.Name, Kind = ev.Kind, Grade = ev.Grade,
                        Discipline = c.Discipline, Placing = c.Placing,
                    });
                    if (c.Placing == 1)
                        h.Satisfaction = Math.Min(100, h.Satisfaction + TournamentBalance.WinnerSatisfaction);
                }
                if (c.Placing == 1)
                    GrantSpecialReward(state, ev);
                result.Placings.Add(new TournamentPlacing
                {
                    AdventurerId = c.Adventurer?.Id, PartyId = c.Party?.Id, Name = c.Name, Discipline = c.Discipline,
                    Placing = c.Placing, Prize = prize, Mood = mood,
                });
            }
            ev.Result = result;
        }

        /// <summary>招待大会の優勝のご褒美（固有武具・次の改築費が半額）。</summary>
        private static void GrantSpecialReward(GameState state, TournamentEvent ev)
        {
            if (ev.SpecialReward == "unique")
            {
                var available = UniqueItemSystem.GetAvailableArtifacts(state);
                if (available.Count > 0)
                    UniqueItemSystem.Grant(state, available[Math.Abs(ev.Id.GetHashCode()) % available.Count], ev.Name);
            }
            else if (ev.SpecialReward == "halfcost")
                state.NextUpgradeHalfPrice = true;
        }

        /// <summary>試合1つ分のHPを使う（部隊戦はメンバー全員）。</summary>
        private static void SpendHp(Competitor c)
        {
            var who = c.Adventurer != null ? new List<Adventurer> { c.Adventurer } : c.Members;
            foreach (var a in who)
                a.CurrentHP = Math.Max(1, a.CurrentHP - (int)Math.Round(a.MaxHP * TournamentBalance.HpCostPerMatch));
        }

        private double Next01() => _rng.NextInt(0, 999_999) / 1_000_000.0;

        private double Luck() => TournamentBalance.LuckMin + (TournamentBalance.LuckMax - TournamentBalance.LuckMin) * Next01();
    }
}
