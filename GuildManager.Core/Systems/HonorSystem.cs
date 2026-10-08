using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 戦績・称号・殿堂・引退式と観測日誌（2026年10月・大会と育成の栄光 段2、→ docs/検討中_大会と育成の栄光.md §4.1）。
    ///  - 記録：ボス撃破（→ RecordBossKill）、能力のピーク・加入時の見立て・重傷と復帰・教官のもとでの週数（→ ProcessWeek）、年表（Adventurer.Journal）。
    ///  - 称号：記録から毎回計算する（→ GetTitles。セーブには持たない）。新しく得た称号は1回だけ知らせる（Adventurer.EarnedTitleIds）。
    ///    表に出すのはいちばん格の高い1つ（同じ格なら新しく得たほう、→ DisplayTitle）。
    ///  - 殿堂：引退のときに判定する（→ OnRetired）。殿堂入りした者を教官・顧問にすると効果×HallOfFameAdvisorMultiplier（→ AdvisorMultiplier）。
    ///  - 関係タグ：戦友・名コンビ・母娘・師弟（表示だけ、→ GetRelations）。
    ///  - 引退式：1人1枚（→ BuildCeremony）。
    /// 乱数は使わない（引退式の台詞の選び方も冒険者の Id で決まる）。
    /// </summary>
    public static class HonorSystem
    {
        public const string Buddy = "戦友";
        public const string PerfectPair = "名コンビ";
        public const string MotherDaughter = "母娘";
        public const string Mentor = "師弟";

        // ==================== 週の決算 ====================

        /// <summary>
        /// 週の決算の終わり（週番号が進む前）に1回呼ぶ。加入・能力のピーク・重傷と復帰・教官のもとでの週数を記録し、
        /// 今週の大会・誕生・母娘の出撃を年表に書き、新しい称号と殿堂入りを知らせる。知らせ（月報の主な出来事）を返す。
        /// </summary>
        public static List<HonorNotice> ProcessWeek(GameState state, IEnumerable<TournamentEvent> tournamentsResolved, IEnumerable<SoulFusionCulture> births)
        {
            var notices = new List<HonorNotice>();
            var active = state.Adventurers.Where(a => !a.IsRetired).ToList();

            foreach (var a in active)
            {
                EnsureJoined(state, a);
                UpdatePeak(a);
                TrackInjury(state, a);
                TrackMentor(state, a);
            }

            foreach (var ev in tournamentsResolved)
                foreach (var a in active)
                    foreach (var r in a.TournamentRecords.Where(r => r.Year == ev.Year && r.DefinitionId == ev.DefinitionId && r.Week == state.WeekNumber && r.Name == ev.Name))
                        RecordTournament(state, a, r);

            foreach (var birth in births)
                foreach (var parentId in new[] { birth.ParentAId, birth.ParentBId }.Distinct())
                    if (state.FindAdventurer(parentId) is { } mother)
                        Write(state, mother, JournalKind.DaughterBorn, $"娘の{birth.Child.Name}が生まれた", birth.Child.Id);

            foreach (var mission in state.ActiveDungeonMissions)
            {
                var members = mission.Party.Members;
                foreach (var daughter in members)
                    foreach (var mother in members.Where(m => daughter.ParentIds.Contains(m.Id)))
                    {
                        if (daughter.Journal.Any(e => e.Kind == JournalKind.MotherDaughterSquad && e.RelatedId == mother.Id))
                            continue;
                        Write(state, daughter, JournalKind.MotherDaughterSquad, $"母の{mother.Name}と同じ部隊で迷宮へ向かった", mother.Id);
                        Write(state, mother, JournalKind.MotherDaughterSquad, $"娘の{daughter.Name}と同じ部隊で迷宮へ向かった", daughter.Id);
                        notices.Add(new HonorNotice { AdventurerId = daughter.Id, Kind = JournalKind.MotherDaughterSquad, Text = $"{mother.Name}と娘の{daughter.Name}が、同じ部隊で迷宮へ向かった" });
                    }
            }

            // 称号：母が引退していても、娘の優勝で「二代制覇」が付くので、引退者も見る
            foreach (var a in active.Concat(state.RetiredAdventurers))
                notices.AddRange(NotifyNewTitles(state, a));

            // 殿堂入り（今週の引退で判定した分。早期引退は週の合間に行うので、次の決算で知らせる）
            foreach (var a in state.RetiredAdventurers.Where(a => a.Journal.Any(e => e.Kind == JournalKind.HallOfFame && e.Week == state.WeekNumber)))
                notices.Add(new HonorNotice { AdventurerId = a.Id, Kind = JournalKind.HallOfFame, Text = $"{a.Name}が殿堂入りした（{a.HallOfFameReason}）" });

            return notices;
        }

        /// <summary>加入の記録（年表が空の者だけ）：加入時の見立てを残す。</summary>
        public static void EnsureJoined(GameState state, Adventurer a)
        {
            if (a.Journal.Count > 0) return;
            if (string.IsNullOrEmpty(a.JoinEstimate))
                a.JoinEstimate = PotentialEstimateSystem.TotalRankLabel(state, a);
            var mothers = a.ParentIds.Select(id => state.FindAdventurer(id)?.Name).OfType<string>().ToList();
            string text = a.ParentIds.Count > 0
                ? $"魂魄融和の秘薬で{(mothers.Count > 0 ? string.Join("と", mothers) + "の" : "")}娘として生まれ、ギルドに加わった（副官の見立て {a.JoinEstimate}）"
                : $"{a.Age}歳でギルドに加わった（副官の見立て {a.JoinEstimate}）";
            Write(state, a, JournalKind.Joined, text);
        }

        private static void UpdatePeak(Adventurer a)
        {
            foreach (var stat in AdventurerStatAccessor.AllStatNames)
            {
                int now = AdventurerStatAccessor.GetStat(a, stat);
                if (!a.PeakStats.TryGetValue(stat, out int peak) || now > peak)
                    a.PeakStats[stat] = now;
            }
        }

        private static void TrackInjury(GameState state, Adventurer a)
        {
            var last = a.Journal.LastOrDefault(e => e.Kind is JournalKind.SevereInjury or JournalKind.Recovered);
            bool open = last?.Kind == JournalKind.SevereInjury;
            if (a.Injury == InjurySeverity.Severe && !open)
                Write(state, a, JournalKind.SevereInjury, "重傷を負った");
            else if (a.Injury != InjurySeverity.Severe && open)
                Write(state, a, JournalKind.Recovered, "重傷から復帰した");
        }

        private static void TrackMentor(GameState state, Adventurer a)
        {
            if (state.TrainingAssignments.TryGetValue(a.Id, out var facility)
                && state.AssignedTrainers.TryGetValue(facility, out var trainer) && trainer is Guid trainerId)
                a.MentorWeeks[trainerId] = a.MentorWeeks.GetValueOrDefault(trainerId) + 1;
        }

        /// <summary>大会の記録を年表に書く：G1の優勝、初めての勝ち鞍、初めての入賞（ベスト8は書かない）。</summary>
        private static void RecordTournament(GameState state, Adventurer a, TournamentRecord r)
        {
            if (r.Placing > 4) return;
            string where = $"{r.Name}（{TournamentSystem.GradeLabel(r.Grade)}）";
            if (r.Placing == 1 && r.Grade == TournamentGrade.G1)
                Write(state, a, JournalKind.BigWin, $"{where}で優勝した");
            else if (r.Placing == 1 && a.TournamentRecords.Count(x => x.Placing == 1) == 1)
                Write(state, a, JournalKind.FirstWin, $"{where}で初めての勝ち鞍を挙げた");
            else if (a.TournamentRecords.Count(x => x.Placing <= 4) == 1)
                Write(state, a, JournalKind.FirstPlacing, $"{where}で初めて入賞した（{TournamentSystem.PlacingLabel(r.Placing)}）");
        }

        private static List<HonorNotice> NotifyNewTitles(GameState state, Adventurer a)
        {
            var notices = new List<HonorNotice>();
            foreach (var t in GetTitles(state, a).Where(t => !a.EarnedTitleIds.Contains(t.Key)))
            {
                a.EarnedTitleIds.Add(t.Key);
                Write(state, a, JournalKind.Title, $"二つ名「{t.Name}」で呼ばれるようになった");
                notices.Add(new HonorNotice { AdventurerId = a.Id, Kind = JournalKind.Title, Text = $"{a.Name}が二つ名「{t.Name}」を得た" });
            }
            return notices;
        }

        private static void Write(GameState state, Adventurer a, JournalKind kind, string text, Guid? related = null) =>
            a.Journal.Add(new JournalEntry { Week = state.WeekNumber, Kind = kind, Text = text, RelatedId = related });

        // ==================== ボス撃破 ====================

        /// <summary>
        /// ボス撃破を、撃破した部隊の生還者（現役ロースターに残っている者）に記録する（→ DungeonExpeditionSystem.EngageBoss）。
        /// 主役＝生還者のうち討伐火力（DungeonPowerCalculator.MemberPower）が最も高い者。主役は年表にも書く。
        /// </summary>
        public static void RecordBossKill(GameState state, Party party, FloorBoss boss, DungeonField field)
        {
            var survivors = party.Members.Where(m => state.Adventurers.Contains(m)).ToList();
            if (survivors.Count == 0) return;
            var mvp = survivors.OrderByDescending(m => DungeonPowerCalculator.MemberPower(m, boss)).First();
            foreach (var m in survivors)
            {
                m.BossKills.Add(new BossKillRecord
                {
                    Week = state.WeekNumber, BossId = boss.Id, FieldId = field.Id, FieldName = field.Name,
                    Floor = boss.Floor, BossName = boss.Name, IsMvp = m == mvp,
                });
            }
            Write(state, mvp, JournalKind.BossMvp, $"{field.Name} 第{boss.Floor}層「{boss.Name}」を倒した部隊の主役になった");
        }

        // ==================== 称号 ====================

        /// <summary>得ている称号の一覧（記録から計算する。順は titles.csv の順）。</summary>
        public static List<EarnedTitle> GetTitles(GameState state, Adventurer a)
        {
            var titles = new List<EarnedTitle>();
            void Add(string id, string? g1Name = null, string? keySuffix = null)
            {
                var def = HonorBalance.Title(id);
                titles.Add(new EarnedTitle
                {
                    Key = keySuffix == null ? id : $"{id}:{keySuffix}",
                    Name = def.Name.Replace("{G1}", g1Name ?? ""),
                    Rank = def.Rank,
                });
            }

            var wins = a.TournamentRecords.Where(r => r.Placing == 1).ToList();
            var bigWins = wins.Where(r => r.Kind is TournamentKind.Classic or TournamentKind.Final).ToList();
            var solo = new[] { TournamentDiscipline.Sword, TournamentDiscipline.Magic, TournamentDiscipline.Skill };
            int soloClassics = wins.Where(r => r.Kind == TournamentKind.Classic && solo.Contains(r.Discipline)).Select(r => r.Discipline).Distinct().Count();

            if (wins.Any(r => r.Kind == TournamentKind.Final))
                Add("final_win");
            if (soloClassics >= solo.Length)
                Add("triple_crown");

            // 連覇：同じ定例のG1を続けた年数（大会ごとに、いちばん上の称号だけ）
            var streaks = new List<(string Id, string Name, int Run)>();
            foreach (var g in bigWins.GroupBy(r => r.DefinitionId))
                streaks.Add((g.Key, g.Last().Name, LongestRun(g.Select(r => r.Year))));
            foreach (var s in streaks.Where(s => s.Run >= HonorBalance.Title("three_peat").Param))
                Add("three_peat", s.Name, s.Id);

            if (bigWins.Any(w => Relatives(state, a).Any(rel => rel.TournamentRecords.Any(r => r.Placing == 1 && r.DefinitionId == w.DefinitionId))))
                Add("two_generations");
            foreach (var s in streaks.Where(s => s.Run >= HonorBalance.Title("two_peat").Param && s.Run < HonorBalance.Title("three_peat").Param))
                Add("two_peat", s.Name, s.Id);
            if (soloClassics >= HonorBalance.Title("double_crown").Param && soloClassics < solo.Length)
                Add("double_crown");
            if (wins.Any(r => r.Kind == TournamentKind.Classic && r.Discipline == TournamentDiscipline.Party))
                Add("party_win");

            var recoveries = a.Journal.Where(e => e.Kind == JournalKind.Recovered).Select(e => e.Week).ToList();
            int indomitableWeeks = HonorBalance.Title("indomitable").Param;
            if (wins.Any(w => w.Grade == TournamentGrade.G1 && w.Week > 0 && recoveries.Any(rw => rw <= w.Week && w.Week - rw <= indomitableWeeks)))
                Add("indomitable");
            if (a.BossKills.Any(k => k.FieldId == AbyssFieldId && k.Floor == DungeonField.MaxFloor))
                Add("abyss_slayer");

            var lastSoloBigWin = bigWins.Where(r => r.Discipline != TournamentDiscipline.Party).OrderBy(r => r.Year).ThenBy(r => r.Week).LastOrDefault();
            if (lastSoloBigWin != null)
                Add("g1_winner", lastSoloBigWin.Name);
            if (a.BossKills.Count(k => k.IsMvp) >= HonorBalance.Title("boss_mvp").Param)
                Add("boss_mvp");
            if (wins.Any(r => r.Kind == TournamentKind.Rookie))
                Add("rookie_king");
            if (a.JoinEstimate.Length > 0 && (a.JoinEstimate[0] == 'C' || a.JoinEstimate[0] == 'D')
                && a.PeakStats.Count > 0 && PotentialEstimateSystem.Rank(a.PeakStats.Values.Average()) is "A" or "S")
                Add("late_bloomer");
            if (a.BossKills.Count >= HonorBalance.Title("veteran").Param)
                Add("veteran");
            if (a.TournamentRecords.Count(r => r.Placing <= 4) >= HonorBalance.Title("placing_regular").Param)
                Add("placing_regular");

            return titles;
        }

        /// <summary>表に出す二つ名：いちばん格の高い1つ（同じ格なら新しく得たほう）。無ければ空。</summary>
        public static string DisplayTitle(GameState state, Adventurer a) => TopTitle(state, a)?.Name ?? "";

        private static EarnedTitle? TopTitle(GameState state, Adventurer a) =>
            GetTitles(state, a)
                .OrderByDescending(t => t.Rank)
                .ThenByDescending(t => a.EarnedTitleIds.IndexOf(t.Key) is int i && i >= 0 ? i : int.MaxValue)
                .FirstOrDefault();

        /// <summary>深淵のフィールドの Id（→ SampleData.CreateDefaultFields）。</summary>
        public const string AbyssFieldId = "abyss";

        private static int LongestRun(IEnumerable<int> years)
        {
            var sorted = years.Distinct().OrderBy(y => y).ToList();
            int best = 0, run = 0;
            for (int i = 0; i < sorted.Count; i++)
            {
                run = i > 0 && sorted[i] == sorted[i - 1] + 1 ? run + 1 : 1;
                best = Math.Max(best, run);
            }
            return best;
        }

        /// <summary>母と娘（魂魄融和の親子）。現役・引退者・除籍者から探す。</summary>
        private static IEnumerable<Adventurer> Relatives(GameState state, Adventurer a) =>
            Everyone(state).Where(o => o.Id != a.Id && (a.ParentIds.Contains(o.Id) || o.ParentIds.Contains(a.Id)));

        private static IEnumerable<Adventurer> Everyone(GameState state) =>
            state.Adventurers.Concat(state.RetiredAdventurers).Concat(state.FallenAdventurers);

        // ==================== 殿堂 ====================

        /// <summary>殿堂入りの理由（資格が無ければ null）。王都最強決定戦の優勝・G1の優勝の数・伝説の称号の順に見る。</summary>
        public static string? HallOfFameReason(GameState state, Adventurer a)
        {
            if (a.TournamentRecords.Any(r => r.Placing == 1 && r.Kind == TournamentKind.Final))
                return "王都最強決定戦で優勝";
            int g1Wins = a.TournamentRecords.Count(r => r.Placing == 1 && r.Grade == TournamentGrade.G1);
            if (g1Wins >= HonorBalance.HallOfFameG1Wins)
                return $"G1を{g1Wins}勝";
            var legend = GetTitles(state, a).FirstOrDefault(t => t.Rank == TitleRank.Legend);
            return legend != null ? $"伝説の称号「{legend.Name}」" : null;
        }

        /// <summary>
        /// 引退のとき（→ AgingSystem.Retire。満期・早期の両方）：年表に引退を書き、殿堂入りを判定する。
        /// </summary>
        public static void OnRetired(GameState state, Adventurer a)
        {
            EnsureJoined(state, a);
            UpdatePeak(a);
            NotifyNewTitles(state, a);
            Write(state, a, JournalKind.Retired, $"{a.Age}歳で引退した（在籍{YearsActive(a)}年）");
            if (a.HallOfFameYear == null && HallOfFameReason(state, a) is { } reason)
            {
                a.HallOfFameYear = GameCalendar.YearOf(state.WeekNumber);
                a.HallOfFameReason = reason;
                Write(state, a, JournalKind.HallOfFame, $"殿堂入りした（{reason}）");
            }
        }

        /// <summary>殿堂入りした者（殿堂入りの年の順）。</summary>
        public static List<Adventurer> HallOfFame(GameState state) =>
            Everyone(state).Where(a => a.HallOfFameYear != null).OrderBy(a => a.HallOfFameYear).ThenBy(a => a.RetiredAtWeek ?? 0).ToList();

        /// <summary>教官・参謀・スカウト顧問の効果に掛ける倍率（殿堂入りした者は HallOfFameAdvisorMultiplier、ほかは1）。</summary>
        public static double AdvisorMultiplier(Adventurer advisor) =>
            advisor.HallOfFameYear != null ? HonorBalance.HallOfFameAdvisorMultiplier : 1.0;

        /// <summary>在籍した年数（稼働週数を年に丸める。最低1）。</summary>
        public static int YearsActive(Adventurer a) =>
            Math.Max(1, (int)Math.Round(a.ActiveWeeks / (double)GameCalendar.WeeksPerYear, MidpointRounding.AwayFromZero));

        // ==================== 戦績の頁 ====================

        /// <summary>能力の名前（画面の並び順）。</summary>
        public static IReadOnlyList<string> StatNames => AdventurerStatAccessor.AllStatNames;

        /// <summary>今の能力（素の値。能力名→値）。</summary>
        public static Dictionary<string, int> CurrentStats(Adventurer a) =>
            AdventurerStatAccessor.AllStatNames.ToDictionary(s => s, s => AdventurerStatAccessor.GetStat(a, s));

        /// <summary>格ごとの優勝と入賞（ベスト4以上）の数（G1・G2〈特別を含む〉・G3の順）。</summary>
        public static List<(string Grade, int Wins, int Placings)> GradeCounts(Adventurer a)
        {
            var list = new List<(string, int, int)>();
            foreach (var (label, grades) in new[] { ("G1", new[] { TournamentGrade.G1 }), ("G2", new[] { TournamentGrade.G2, TournamentGrade.Special }), ("G3", new[] { TournamentGrade.G3 }) })
            {
                var records = a.TournamentRecords.Where(r => grades.Contains(r.Grade)).ToList();
                list.Add((label, records.Count(r => r.Placing == 1), records.Count(r => r.Placing <= 4)));
            }
            return list;
        }

        /// <summary>いちばん深い撃破（フィールドの攻略順→階層の順。無ければ null）。</summary>
        public static BossKillRecord? DeepestKill(GameState state, Adventurer a) =>
            a.BossKills
                .OrderBy(k => state.DungeonFields.FirstOrDefault(f => f.Id == k.FieldId)?.Order ?? 0)
                .ThenBy(k => k.Floor)
                .LastOrDefault();

        // ==================== 関係タグ ====================

        /// <summary>関係の深い相手（タグの多い順・相性の高い順に RelationsShown 人まで）。相性が0以下でタグも無い相手は出さない。</summary>
        public static List<RelationInfo> GetRelations(GameState state, Adventurer a, int? max = null)
        {
            var kills = a.BossKills.Select(k => k.BossId).ToHashSet();
            var list = new List<RelationInfo>();
            foreach (var o in Everyone(state).Where(o => o.Id != a.Id))
            {
                int compat = CompatibilitySystem.GetCompatibility(state, a.Id, o.Id);
                var tags = new List<string>();
                if (a.ParentIds.Contains(o.Id) || o.ParentIds.Contains(a.Id))
                    tags.Add(MotherDaughter);
                if (compat >= HonorBalance.PerfectPairCompatibility)
                    tags.Add(PerfectPair);
                if (compat >= HonorBalance.BuddyCompatibility && o.BossKills.Count(k => kills.Contains(k.BossId)) >= HonorBalance.BuddyBossKills)
                    tags.Add(Buddy);
                if (a.MentorWeeks.GetValueOrDefault(o.Id) >= HonorBalance.MentorWeeks || o.MentorWeeks.GetValueOrDefault(a.Id) >= HonorBalance.MentorWeeks)
                    tags.Add(Mentor);
                if (tags.Count == 0 && compat <= 0) continue;
                list.Add(new RelationInfo { OtherId = o.Id, OtherName = o.Name, Compatibility = compat, Tags = tags });
            }
            return list.OrderByDescending(r => r.Tags.Count).ThenByDescending(r => r.Compatibility)
                .Take(max ?? HonorBalance.RelationsShown).ToList();
        }

        // ==================== 引退式 ====================

        /// <summary>年表の出来事の重み（引退式に載せる出来事を選ぶ）。</summary>
        public static int Weight(JournalKind kind) => kind switch
        {
            JournalKind.HallOfFame => 9,
            JournalKind.BigWin => 8,
            JournalKind.Title => 7,
            JournalKind.FirstWin => 6,
            JournalKind.DaughterBorn => 6,
            JournalKind.Joined => 5,
            JournalKind.Retired => 5,
            JournalKind.MotherDaughterSquad => 5,
            JournalKind.BossMvp => 4,
            JournalKind.FirstPlacing => 4,
            _ => 3,
        };

        /// <summary>引退式の1枚を作る（状態は変えない）。引退した者にも、現役の者にも使える。</summary>
        public static RetirementCeremony BuildCeremony(GameState state, Adventurer a)
        {
            var top = TopTitle(state, a);
            var wins = a.TournamentRecords.Where(r => r.Placing == 1).OrderBy(r => r.Year).ThenBy(r => r.Week)
                .Select(r => $"{r.Year}年目 {r.Name}（{TournamentSystem.GradeLabel(r.Grade)}）").ToList();
            bool isShort = top == null && wins.Count == 0 && a.Journal.Count <= HonorBalance.ShortCeremonyMaxEntries;
            // 加入と引退は必ず載せ、残りを重みの大きい順に選ぶ。二つ名の行は新しいものから CeremonyTitleLines 行まで（ほかの出来事を押し出さない）
            var indexed = a.Journal.Select((e, i) => (e, i)).ToList();
            var fixedLines = indexed.Where(x => x.e.Kind is JournalKind.Joined or JournalKind.Retired).ToList();
            var titleLines = indexed.Where(x => x.e.Kind == JournalKind.Title).OrderByDescending(x => x.i).Take(HonorBalance.CeremonyTitleLines);
            var others = indexed.Where(x => x.e.Kind is not (JournalKind.Joined or JournalKind.Retired or JournalKind.Title)).Concat(titleLines)
                .OrderByDescending(x => Weight(x.e.Kind)).ThenBy(x => x.i)
                .Take(Math.Max(0, HonorBalance.CeremonyHighlights - fixedLines.Count));
            var highlights = fixedLines.Concat(others).OrderBy(x => x.i).Select(x => x.e).ToList();
            bool hall = a.HallOfFameYear != null;
            string key = hall ? "HallOfFame" : isShort ? "Short" : top?.Rank switch
            {
                TitleRank.Legend => "Legend",
                TitleRank.Name => "Name",
                TitleRank.Honor => "Honor",
                _ => "Plain",
            };
            var lines = HonorBalance.RetirementLines.TryGetValue(key, out var l) ? l : Array.Empty<string>();
            string line = lines.Count == 0 ? "" : lines[(int)((uint)a.Id.GetHashCode() % (uint)lines.Count)]
                .Replace("{name}", a.Name).Replace("{title}", top?.Name ?? "");
            double truePa = AdventurerStatAccessor.AllStatNames.Average(s => AdventurerStatAccessor.GetPa(a, s));

            return new RetirementCeremony
            {
                AdventurerId = a.Id,
                Name = a.Name,
                PortraitId = a.PortraitId,
                Title = top?.Name ?? "",
                YearsActive = YearsActive(a),
                JoinEstimate = a.JoinEstimate,
                TrueRank = PotentialEstimateSystem.Rank(truePa),
                PeakStats = AdventurerStatAccessor.AllStatNames.ToDictionary(s => s, s => a.PeakStats.TryGetValue(s, out int v) ? v : AdventurerStatAccessor.GetStat(a, s)),
                Highlights = highlights,
                Wins = wins,
                HallOfFame = hall,
                AlbertLine = line,
                IsShort = isShort,
            };
        }
    }
}
