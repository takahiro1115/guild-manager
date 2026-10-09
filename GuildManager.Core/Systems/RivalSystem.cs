using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 王都のライバルギルド（2026年10月・§0.93、大会と育成の栄光 段3-1、→ RivalBalance）。
    /// イザベラの来訪（大会が開くとき）に3つのギルドの名簿を作り、年のはじめに年を取らせ、26歳の子を引退させて18歳の新人で埋める。
    /// ライバルは得意な部門の、自分の強さが幅に入る大会に出る（TournamentSystem.Resolve が RivalEntrants を呼ぶ）。
    /// 乱数は年とギルドから決まる種を使う（セーブとロードで結果が変わらない）。
    /// </summary>
    public static class RivalSystem
    {
        /// <summary>白百合の杖（イザベラのギルド）の Id。</summary>
        public const string WhiteLilyId = "whitelily";

        /// <summary>
        /// 週の処理：来訪済みで名簿が無ければ作り、年が変わっていれば入れ替える。何度呼んでもよい。
        /// </summary>
        public static void ProcessWeek(GameState state)
        {
            if (state.IsabellaVisitWeek == null)
                return;
            int year = GameCalendar.YearOf(state.WeekNumber);
            if (state.RivalYear == 0 || state.RivalGuilds.Count == 0)
            {
                CreateRosters(state, year);
                return;
            }
            while (state.RivalYear < year)
                AdvanceYear(state, state.RivalYear + 1);
        }

        /// <summary>名簿を作る（看板の子＋得意な部門3人・ほかの部門1人ずつ・残りはどれか。年齢は18〜25）。</summary>
        public static void CreateRosters(GameState state, int year)
        {
            state.RivalGuilds.Clear();
            var names = UsedNames(state);
            foreach (var (def, index) in RivalBalance.Guilds.Select((d, i) => (d, i)))
            {
                var rng = new SeededRng(unchecked(year * 104729 + index * 7919 + 17));
                var guild = new RivalGuild { Id = def.Id, Name = def.Name };
                var slots = RosterDisciplines(def.Specialty, rng);
                foreach (var fixedMember in RivalBalance.FixedMembers.Where(m => m.GuildId == def.Id))
                {
                    guild.Members.Add(new RivalMember
                    {
                        Name = fixedMember.Name, Epithet = fixedMember.Epithet, Age = fixedMember.Age, Peak = fixedMember.Peak,
                        Discipline = fixedMember.Discipline, JoinedYear = year - Math.Max(0, fixedMember.Age - RivalBalance.RookieAge),
                    });
                    names.Add(fixedMember.Name);
                    slots.Remove(fixedMember.Discipline); // 看板の子が部門の枠を1つ使う
                }
                foreach (var discipline in slots.Take(Math.Max(0, RivalBalance.RosterSize - guild.Members.Count)))
                    guild.Members.Add(NewMember(rng, names, discipline, rng.NextInt(RivalBalance.StartAgeMin, RivalBalance.StartAgeMax), year));
                state.RivalGuilds.Add(guild);
            }
            state.RivalYear = year;
        }

        /// <summary>名簿の部門の並び：得意な部門×SpecialtyCount、ほかの部門を1人ずつ、残りは抽選（rng が null なら得意な部門）。</summary>
        private static List<TournamentDiscipline> RosterDisciplines(TournamentDiscipline specialty, IRng? rng)
        {
            var all = new[] { TournamentDiscipline.Sword, TournamentDiscipline.Magic, TournamentDiscipline.Skill };
            var list = Enumerable.Repeat(specialty, RivalBalance.SpecialtyCount).ToList();
            list.AddRange(all.Where(d => d != specialty));
            while (list.Count < RivalBalance.RosterSize)
                list.Add(rng == null ? specialty : all[rng.NextInt(0, all.Length - 1)]);
            return list;
        }

        /// <summary>
        /// 年の入れ替え：引退の年齢の子を引退させ、残りは1つ年を取り、空いた分を新人で埋める（自分のギルドと同じく、26歳の年度末で引退）。
        /// </summary>
        public static void AdvanceYear(GameState state, int year)
        {
            var names = UsedNames(state);
            foreach (var (guild, index) in state.RivalGuilds.Select((g, i) => (g, i)))
            {
                var rng = new SeededRng(unchecked(year * 104729 + index * 7919 + 31));
                foreach (var m in guild.Members.Where(m => m.Age >= RivalBalance.RetirementAge).ToList())
                {
                    m.RetiredYear = year - 1;
                    guild.Members.Remove(m);
                    state.RetiredRivals.Add(m);
                    state.RetiredRivalGuilds[m.Id] = guild.Id;
                }
                foreach (var m in guild.Members)
                    m.Age++;
                var specialty = RivalBalance.Guilds.FirstOrDefault(d => d.Id == guild.Id)?.Specialty ?? TournamentDiscipline.Sword;
                while (guild.Members.Count < RivalBalance.RosterSize)
                {
                    // 足りない部門（得意な部門3・ほかの部門1）を先に埋め、満ちていれば得意な部門
                    var want = RosterDisciplines(specialty, null).GroupBy(d => d).Select(g => (g.Key, Need: g.Count() - guild.Members.Count(m => m.Discipline == g.Key)))
                        .Where(x => x.Need > 0).Select(x => x.Key).DefaultIfEmpty(specialty).First();
                    guild.Members.Add(NewMember(rng, names, want, RivalBalance.RookieAge, year));
                }
            }
            state.RivalYear = year;
        }

        /// <summary>新しい子（入った年は年齢から逆算：18歳ならその年、20歳なら2年前）。</summary>
        private static RivalMember NewMember(IRng rng, HashSet<string> names, TournamentDiscipline discipline, int age, int year)
        {
            string name = NameGenerator.GenerateUniqueFirstName(NameGenerator.RollCulture(rng), names, rng);
            names.Add(name);
            double peak = RivalBalance.PeakMin + (RivalBalance.PeakMax - RivalBalance.PeakMin) * rng.NextInt(0, 1000) / 1000.0;
            return new RivalMember { Name = name, Age = age, Peak = Math.Round(peak), Discipline = discipline, JoinedYear = year - Math.Max(0, age - RivalBalance.RookieAge) };
        }

        private static HashSet<string> UsedNames(GameState state) =>
            new(state.Adventurers.Select(a => a.Name)
                .Concat(state.RivalGuilds.SelectMany(g => g.Members).Select(m => m.Name))
                .Concat(state.RetiredRivals.Select(m => m.Name))
                .Concat(RivalBalance.Guilds.Select(g => g.Master)) // マスターと同じ名前の子を作らない
                .Concat(RivalBalance.FixedMembers.Select(m => m.Name)));

        /// <summary>強さ（大会の相手の強さと同じ尺度。部門の倍率と王都の水準の伸びは掛けない）＝全盛×年齢の伸び。</summary>
        public static double Power(RivalMember m) => m.Peak * RivalBalance.AgeCurve(m.Age);

        /// <summary>強さの目安の言葉（その強さで出られるいちばん上の格）。</summary>
        public static string PowerLabel(RivalMember m)
        {
            double p = Power(m);
            double g1 = TournamentBalance.Find("classic_sword")?.MinStrength ?? 110;
            double g2 = TournamentBalance.Find("royal_sword")?.MinStrength ?? 85;
            return p >= g1 ? "G1級" : p >= g2 ? "G2級" : "G3級";
        }

        /// <summary>表示の名前（二つ名があれば「氷華」セシリア）。</summary>
        public static string DisplayName(RivalMember m) => m.Epithet.Length > 0 ? $"「{m.Epithet}」{m.Name}" : m.Name;

        /// <summary>ライバルの子の今のギルド（引退していれば元のギルド）。見つからなければ null。</summary>
        public static RivalGuild? GuildOf(GameState state, RivalMember m) =>
            state.RivalGuilds.FirstOrDefault(g => g.Members.Contains(m))
            ?? (state.RetiredRivalGuilds.TryGetValue(m.Id, out var id) ? state.RivalGuilds.FirstOrDefault(g => g.Id == id) : null);

        /// <summary>
        /// その大会に出るライバル（§0.93）。得意な部門が大会の部門と同じ（得意・抽選の大会は誰でも）で、強さが大会の幅に入る子。
        /// 大祭（G1）・王都最強決定戦・王族の御前試合は上限なし。新人戦はその年に入った新人だけ。迷宮踏破杯・交流戦には出ない。
        /// 1ギルドから EntrantsPerGuild 人まで、強い順に。全体で slots 人まで（強い順）。
        /// </summary>
        public static List<(RivalMember Member, RivalGuild Guild)> Entrants(GameState state, TournamentEvent ev, int slots)
        {
            var result = new List<(RivalMember, RivalGuild)>();
            var def = TournamentBalance.Find(ev.DefinitionId);
            if (def == null || slots <= 0 || ev.Kind == TournamentKind.Exchange || ev.Discipline == TournamentDiscipline.Party)
                return result;
            bool uncapped = ev.Grade == TournamentGrade.G1 || ev.Kind == TournamentKind.Final || ev.DefinitionId == "invite_royal";
            foreach (var guild in state.RivalGuilds)
            {
                var eligible = guild.Members.Where(m =>
                {
                    if (ev.Discipline is not (TournamentDiscipline.Best or TournamentDiscipline.Random) && m.Discipline != ev.Discipline)
                        return false;
                    if (ev.Kind == TournamentKind.Rookie)
                        return m.JoinedYear == ev.Year && m.Age == RivalBalance.RookieAge;
                    double p = Power(m);
                    return p >= def.MinStrength && (uncapped || p <= def.MaxStrength);
                });
                result.AddRange(eligible.OrderByDescending(Power).Take(TournamentBalance.EntrantsPerGuild).Select(m => (m, guild)));
            }
            return result.OrderByDescending(x => Power(x.Item1)).Take(slots).ToList();
        }

        /// <summary>試合の強さ＝強さ×部門の倍率×王都の水準の伸び（名前の無い相手と同じ掛け方）。</summary>
        public static double MatchStrength(RivalMember m, int year) =>
            Power(m) * TournamentBalance.DisciplineFactor(m.Discipline) * (1 + TournamentBalance.YearlyGrowth * Math.Max(0, year - 1));

        /// <summary>大会の結果をライバルの戦績に残す（ベスト4以上）。G1を初めて勝ったら二つ名を付ける。</summary>
        public static void Record(GameState state, RivalMember m, TournamentEvent ev, int placing, int week)
        {
            if (placing > RivalBalance.RecordMinPlacing)
                return;
            m.Records.Add(new TournamentRecord
            {
                Year = ev.Year, Week = week, DefinitionId = ev.DefinitionId, Name = ev.Name, Kind = ev.Kind, Grade = ev.Grade,
                Discipline = m.Discipline, Placing = placing,
            });
            if (placing == 1 && ev.Grade == TournamentGrade.G1 && m.Epithet.Length == 0)
            {
                var used = new HashSet<string>(state.RivalGuilds.SelectMany(g => g.Members).Concat(state.RetiredRivals).Select(x => x.Epithet));
                m.Epithet = RivalBalance.Epithets(m.Discipline).FirstOrDefault(e => !used.Contains(e)) ?? "";
            }
        }

        /// <summary>
        /// 交流戦の相手（§0.93）：白百合の杖のその部門の看板の子（rival_members.csv）が現役ならその子、引退していればその部門でいちばん強い現役の子。
        /// 名簿が無ければ null。
        /// </summary>
        public static RivalMember? WhiteLilyAce(GameState state, TournamentDiscipline d)
        {
            var members = state.RivalGuilds.FirstOrDefault(g => g.Id == WhiteLilyId)?.Members.Where(m => m.Discipline == d).ToList();
            if (members == null) return null;
            var faces = RivalBalance.FixedMembers.Where(f => f.GuildId == WhiteLilyId && f.Discipline == d).Select(f => f.Name).ToHashSet();
            return members.FirstOrDefault(m => faces.Contains(m.Name)) ?? members.OrderByDescending(Power).FirstOrDefault();
        }

        /// <summary>その名前のライバルが引退していて、同じ名前の現役がいない（物語の台詞を出さないのに使う）。</summary>
        public static bool IsRetiredRival(GameState state, string name) =>
            state.RetiredRivals.Any(m => m.Name == name) && !state.RivalGuilds.SelectMany(g => g.Members).Any(m => m.Name == name);
    }
}
