using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>出来事の報告1件（月報に入れてセーブにも残す。§0.141）。</summary>
    public sealed class EventReport
    {
        /// <summary>知らせの種類（event_lines.csv の Kind）。</summary>
        public string Kind { get; set; } = "";
        /// <summary>当人（冒険者の出来事・大会の優勝など）。名前のある人物だけが話す知らせは null。</summary>
        public Guid? SubjectId { get; set; }
        public string SubjectName { get; set; } = "";
        /// <summary>当人の顔（→ assets/portraits）。無ければ null。</summary>
        public string? PortraitId { get; set; }
        /// <summary>台詞（「｜」で段に分かれる。〔…〕＝地の文、名前「…」＝名前のある人物、「…」＝当人。差し込みは埋めてある）。</summary>
        public string Line { get; set; } = "";
        /// <summary>行き先の案内（【画面へ】の文。無ければ空）。判断が要る知らせ（★）に付く。</summary>
        public string Jump { get; set; } = "";
    }

    /// <summary>
    /// 出来事の報告（2026年10月・§0.141。ユーザー「月報の緑文字の多くを、イベントとしてキャラクターに話させたい。特にプレイヤーが判断した方がいい場面」）。
    /// 月報を作るときに、その月の知らせ（新しいフィールド・研究・施設・娘・培養槽・手紙・大会・栄誉・冒険者の出来事）を人物に話させる台詞に変える。
    /// 部隊長の報告のあとに続けて見せ、月報の行はそのまま残す。台詞は event_lines.csv（正本は物語帳「台詞 06」）。
    /// - 話す人：アルベール（研究・迷宮・秘薬）・ルミナ（手紙・知らせ。救出の前は出さない）・当人（気質の口調、初期の3人は専用）。
    /// - 同じ月に、同じ知らせの物語の場面も出るなら、場面の見せ場を先に言わない導入の一言（Intro:*）にする。場面が出ない月の導入は出さない。
    /// - 並び：判断が要る知らせ（行き先つき）→ 冒険者の出来事 → 手紙。同じ種類・同じ当人の知らせは月に1回。乱数は使わない（月と知らせの順で台詞を回す）。
    /// </summary>
    public static class EventReportSystem
    {
        private const char SegmentSeparator = '｜';
        private static readonly Regex NamedSpeech = new(@"^(?<name>[^「〔]+)「(?<text>.*)」$");

        /// <summary>名前のある人物の台詞の話し手（ここに無い名前は当人の台詞と同じ扱いにしない）。</summary>
        public static readonly IReadOnlyCollection<string> NamedSpeakers = new HashSet<string> { "アルベール", "ルミナ", "イザベラ", "マルグリット" };

        public static List<EventReport> Build(GameState state, IReadOnlyList<AutoSkipWeek> weeks, IReadOnlyList<MonthlyNote>? notes = null)
        {
            var b = new Builder(state);
            // 判断が要る注意（§0.151）：前の月の月報（月の終わりまで進んだもの）に無かった種類だけ、ルミナが行き先つきで知らせる
            if (notes != null)
            {
                var before = state.MonthlyReports.LastOrDefault(r => r.MonthCompleted)?.Notes.Select(n => n.Key).ToHashSet() ?? new HashSet<string>();
                foreach (var note in notes.Where(n => n.Key.Length > 0 && !before.Contains(n.Key)))
                    b.Notice(note);
            }
            foreach (var w in weeks)
            {
                var s = w.Settlement;
                foreach (var ev in w.Orders.SelectMany(o => o.Incidents ?? new List<IncidentEvent>()))
                    b.Incident(ev);
                foreach (var r in s.DungeonMissionResolutions)
                {
                    if (r.FieldNewlyUnlocked != null) b.Field(r.FieldNewlyUnlocked);
                    if (r.SquadSlotsExpandedTo is int slots) b.Slots(slots);
                    if (r.Incident != null) b.Incident(r.Incident);
                    foreach (var guard in r.DungeonResult?.GuardEvents ?? new List<GuardEvent>())
                        b.Subject("Guard", guard.ProtectedId, new() { ["{相手}"] = guard.GuardianName });
                }
                foreach (var field in s.FieldsUnlockedAtMonthStart) b.Field(field);
                if (s.SquadSlotsExpandedTo is int moreSlots) b.Slots(moreSlots);
                foreach (var ev in s.Incidents) b.Incident(ev);
                if (s.CompletedResearch is { } research)
                {
                    string id = research.Id.StartsWith("res_culture_tank") ? "res_culture_tank" : research.Id;
                    b.Fixed(EventReportBalance.Get($"ResearchDone:{id}", "any").Count > 0 ? $"ResearchDone:{id}" : "ResearchDone", new() { ["{研究}"] = research.Name });
                }
                if (s.CompletedFacility is { } facility)
                    b.Fixed("FacilityBuilt", new() { ["{施設}"] = FacilityUnlockSystem.FacilityName(facility.Type), ["{Lv}"] = facility.CurrentLevel.ToString() });
                foreach (var birth in s.SoulFusionBirths)
                    b.Fixed("DaughterJoined", new() { ["{子}"] = birth.Child.Name, ["{母A}"] = NameOf(state, birth.ParentAId), ["{母B}"] = NameOf(state, birth.ParentBId) }, birth.Child.Id);
                foreach (var n in s.NurtureEvents)
                    b.Fixed(n.Kind == NurtureKind.Entrust ? "NurtureEntrust" : "Nurture",
                        new() { ["{子}"] = n.Culture.Child.Name, ["{育て方}"] = SoulFusionSystem.NurtureLabel(n.Kind), ["{伸び}"] = NurtureGain(n) }, n.Culture.Child.Id);
                foreach (var c in s.Sponsors.Completed)
                    b.Fixed("SponsorDone", new() { ["{スポンサー}"] = c.SponsorName, ["{金額}"] = c.Bonus.ToString() });
                foreach (var done in s.Commissions.Completed)
                    b.Fixed($"CommissionDone:{done.Commission.ClientId}", new() { ["{依頼人}"] = done.ClientName });
                foreach (var back in s.LoanReturns)
                    b.Subject("LoanReturn", back.Adventurer.Id, new() { ["{依頼人}"] = back.ClientName });
                foreach (var ev in s.TournamentsResolved)
                    foreach (var p in (ev.Result?.Placings ?? new List<TournamentPlacing>()).Where(p => p.Placing == 1 && p.AdventurerId != null))
                        b.Subject("TournamentWin", p.AdventurerId!.Value, new() { ["{大会}"] = ev.Name });
                // 優勝に届かなかった入賞（§0.151）：準優勝はどの格でも、ベスト4は G1・特別だけ（毎月の大会で出すぎないように）
                foreach (var ev in s.TournamentsResolved.Where(e => e.Kind != TournamentKind.Exchange))
                    foreach (var p in (ev.Result?.Placings ?? new List<TournamentPlacing>()).Where(p => p.AdventurerId != null
                        && (p.Placing == 2 || (p.Placing == 4 && ev.Grade is TournamentGrade.G1 or TournamentGrade.Special))))
                        b.Subject("TournamentPlace", p.AdventurerId!.Value, new() { ["{大会}"] = ev.Name, ["{順位}"] = TournamentSystem.PlacingLabel(p.Placing) });
                if (s.YearStanding?.Rows.FirstOrDefault(r => r.Ours) is { Rank: <= 2 } standing
                    && !(standing.Rank == 1 && b.Intro("Intro:YearTop", "s03_yeartop"))) // 初めての1位は、場面のイザベラにつなぐ導入の一言（§0.145）
                    b.Fixed("YearRank", new() { ["{順位}"] = standing.Rank.ToString() });
                if (s.IsabellaVisited) b.Intro("Intro:Visit", "s02_visit");
                foreach (var m in s.ExchangeMatches)
                {
                    var deciding = m.Bouts.LastOrDefault(x => x.Won == m.Won && x.AdventurerId != null);
                    if (deciding != null)
                        b.Subject(m.Won ? "ExchangeWon" : "ExchangeLost", deciding.AdventurerId!.Value, new());
                    if (m.First) b.Fixed("Intro:TournamentsOpen", new());
                }
                if (s.GuestTrainer.Arrived != null)
                {
                    if (!b.Intro("Intro:GuestArrive", "s02_marguerite"))
                        b.Fixed("GuestArrive", new());
                }
                if (s.CommissionsUnlocked) b.Intro("Intro:CommissionsOpen", "s02_royal");
                foreach (var invite in s.TournamentInvitations)
                    b.Fixed("Invitation", new()
                    {
                        ["{大会}"] = invite.Name,
                        ["{日付}"] = $"{GameCalendar.FormatMonth(GameCalendar.WeekNumberOf(invite.Year, invite.Month, invite.Week))} 第{invite.Week}週",
                    });
                foreach (var notice in s.HonorNotices)
                    b.Honor(notice);
                // 見立てが済んだ（志向と気質がすべて確かになった週、§0.151）：当人が打ち明ける
                foreach (var found in s.PersonalityDiscoveries.Where(d => d.Level == PersonalityKnowledge.Sure).GroupBy(d => d.Adventurer.Id))
                    if (found.First().Adventurer is { Personality: { } p } a && PersonalitySystem.IsFullyKnown(state, a))
                        b.Subject("PersonalityKnown", a.Id, new() { ["{好きなこと}"] = PersonalitySystem.LikePhrase(p.Main) });
            }
            b.Mood();
            return b.Result();
        }

        /// <summary>前の月報のときのマスターの機嫌の段階（§0.151。無ければ、まだ一度も比べていない）。</summary>
        public const string MoodTierKey = "moodtier";

        /// <summary>報告を小窓の行に分ける（地の文・名前のある人物の台詞・当人の台詞）。当人の台詞の話し手は当人の名前。</summary>
        public static List<StoryLine> Lines(EventReport report)
        {
            var lines = new List<StoryLine>();
            foreach (var raw in report.Line.Split(SegmentSeparator))
            {
                string text = raw.Trim();
                if (text.Length == 0) continue;
                if (text.StartsWith('〔') && text.EndsWith('〕'))
                    lines.Add(new StoryLine(StoryLineKind.Narration, text[1..^1]));
                else if (NamedSpeech.Match(text) is { Success: true } named && NamedSpeakers.Contains(named.Groups["name"].Value))
                    lines.Add(new StoryLine(StoryLineKind.Speech, named.Groups["text"].Value, named.Groups["name"].Value));
                else
                    lines.Add(new StoryLine(StoryLineKind.Speech, text.StartsWith('「') && text.EndsWith('」') ? text[1..^1] : text, report.SubjectName));
            }
            return lines;
        }

        private static string NameOf(GameState state, Guid id) => state.FindAdventurer(id)?.Name ?? "？";

        private static string NurtureGain(NurtureEvent n) => n.Kind switch
        {
            NurtureKind.Mothers => $"母たちとの相性+{SoulFusionBalance.NurtureMotherCompatibility}",
            NurtureKind.World => "見立てが進んだ",
            _ => n.Gains.Count > 0 ? string.Join("・", n.Gains.Select(g => $"{g.Key}+{g.Value}")) : "少し伸びた",
        };

        /// <summary>休日の絆を話した2人の記録のキー（2人の並びによらない）。</summary>
        public static string BondToldKey(Guid a, Guid b) =>
            string.CompareOrdinal(a.ToString(), b.ToString()) < 0 ? $"bondtold:{a}:{b}" : $"bondtold:{b}:{a}";

        private static string IncidentKindKey(IncidentKind kind) => kind switch
        {
            IncidentKind.Quarrel => "Quarrel", IncidentKind.Gloom => "Gloom", IncidentKind.Waste => "Waste", IncidentKind.Fret => "Fret",
            IncidentKind.Poach => "Poach", IncidentKind.Mentor => "Mentor", IncidentKind.Insight => "Insight", IncidentKind.Treat => "Treat",
            IncidentKind.Pumped => "Pumped", IncidentKind.Bond => "Bond", IncidentKind.Brawl => "Brawl", IncidentKind.Boycott => "Boycott",
            IncidentKind.Refuse => "Refuse", IncidentKind.Charge => "Charge", IncidentKind.Flee => "Flee", IncidentKind.PoachLeft => "PoachLeft",
            IncidentKind.Warm => "Warm",
            _ => "",
        };

        /// <summary>報告を集める（並び・重なり・ルミナの有無・台詞の回し方をまとめて扱う）。</summary>
        private sealed class Builder
        {
            private readonly GameState _state;
            private readonly bool _lumina;
            private readonly int _month;
            private readonly HashSet<string> _dueScenes;
            private readonly List<(int Order, EventReport Report)> _reports = new();
            private readonly HashSet<string> _seen = new();
            private readonly Dictionary<string, int> _kindCounts = new();

            public Builder(GameState state)
            {
                _state = state;
                _lumina = StorySystem.Seen(state, "s01_lumina");
                _month = (state.WeekNumber - 1) / GameCalendar.WeeksPerMonth;
                _dueScenes = StorySystem.DueScenes(state, StoryTiming.AfterReport).Select(s => s.SceneId).ToHashSet();
            }

            public List<EventReport> Result() => _reports.OrderBy(r => r.Order).Select(r => r.Report).ToList(); // OrderBy は安定（同じ順位は出来事の順）

            public void Field(DungeonField field)
            {
                if (field.Id == "abyss") Intro("Intro:Abyss", "s03_abyss");
                else Fixed($"FieldOpen:{field.Id}", new() { ["{フィールド}"] = field.Name });
            }

            public void Slots(int slots)
            {
                if (slots == 2) Intro("Intro:Slot2", "s01_squad2");
                else if (slots == 3) Intro("Intro:Slot3", "s01_squad3");
            }

            /// <summary>
            /// マスターの機嫌の段階が前の月報から変わった（§0.151）：アルベールが話す（ルミナがいれば添える）。
            /// 上機嫌の境目の行き来で毎月出ないよう、同じ向きは季節に1回まで（危機に落ちたときはいつも話す）。初めて比べる月（旧セーブ）は覚えるだけ。
            /// </summary>
            public void Mood()
            {
                int tier = (int)MasterMoodSystem.GetTier(_state.MasterMood);
                bool compared = _state.StoryCounters.TryGetValue(MoodTierKey, out int last);
                _state.StoryCounters[MoodTierKey] = tier;
                if (!compared || tier == last) return;
                bool up = tier > last;
                string toldKey = up ? "moodtold:up" : "moodtold:down";
                int season = (_state.WeekNumber - 1) / GameCalendar.WeeksPerSeason + 1; // 0 を「まだ」と分ける
                if (tier != (int)MasterMoodTier.Crisis && _state.StoryCounters.GetValueOrDefault(toldKey) == season) return;
                if (Fixed($"{(up ? "MoodUp" : "MoodDown")}:{(MasterMoodTier)tier}", new()))
                    _state.StoryCounters[toldKey] = season;
            }

            /// <summary>判断が要る注意の知らせ（§0.151。Kind は Notice:種類。種類のあとの「:Id」は台詞を分けない）。初めてのスポンサーの話は場面が話すので出さない。</summary>
            public void Notice(MonthlyNote note)
            {
                string kind = note.Key.Split(':')[0];
                if (kind == "SponsorOffer" && StorySystem.DueScenes(_state, StoryTiming.BeforeReport).Any(s => s.SceneId == "s02_sponsor")) return;
                Fixed($"Notice:{kind}", new Dictionary<string, string>(note.Values), other: null, dedupeKey: note.Key);
            }

            /// <summary>物語の場面と重なる知らせの導入の一言（その場面がこの月に出るときだけ）。出したら true。</summary>
            public bool Intro(string kind, string sceneId) => _dueScenes.Contains(sceneId) && Fixed(kind, new());

            public void Honor(HonorNotice notice)
            {
                var a = _state.FindAdventurer(notice.AdventurerId);
                if (a == null) return;
                switch (notice.Kind)
                {
                    case JournalKind.Title:
                        var m = Regex.Match(notice.Text, "「(?<title>[^」]+)」");
                        if (m.Success) Fixed("Title", new() { ["{名前}"] = a.Name, ["{二つ名}"] = m.Groups["title"].Value }, a.Id);
                        break;
                    case JournalKind.HallOfFame:
                        Fixed("HallOfFame", new() { ["{名前}"] = a.Name, ["{殿堂の理由}"] = a.HallOfFameReason }, a.Id);
                        break;
                    case JournalKind.MotherDaughterSquad:
                        Fixed("MotherDaughter", new() { ["{名前}"] = a.Name }, a.Id);
                        break;
                }
            }

            public void Incident(IncidentEvent ev)
            {
                string kind = IncidentKindKey(ev.Kind);
                if (kind.Length == 0 || ev.Who is not Guid who) return;
                // 休日の絆は、その2人の初めてのときだけ話す（2回目からは月報の行だけ。§0.148）
                // 打ち解けたも同じく、その2人の初めてだけ（§0.151。組が多く、毎月のように起きる）
                if (ev.Kind == IncidentKind.Warm && ev.Other is Guid friend)
                {
                    string key = "warm" + BondToldKey(who, friend);
                    if (_state.StoryCounters.ContainsKey(key)) return;
                    _state.StoryCounters[key] = _state.WeekNumber;
                }
                if (ev.Kind == IncidentKind.Bond && ev.Other is Guid partner)
                {
                    string key = BondToldKey(who, partner);
                    if (_state.StoryCounters.ContainsKey(key)) return;
                    _state.StoryCounters[key] = _state.WeekNumber;
                }
                string other = ev.Other is Guid o ? NameOf(_state, o) : "";
                var values = new Dictionary<string, string> { ["{相手}"] = other, ["{後輩}"] = other };
                switch (ev.Kind)
                {
                    case IncidentKind.Waste: values["{金額}"] = ev.Detail; break;
                    case IncidentKind.Poach or IncidentKind.PoachLeft: values["{ギルド}"] = ev.Detail; break;
                    case IncidentKind.Insight: values["{能力}"] = ev.Detail; break;
                }
                if (ev.Kind is IncidentKind.Gloom or IncidentKind.Waste or IncidentKind.Boycott or IncidentKind.PoachLeft or IncidentKind.Brawl)
                {
                    values["{名前}"] = NameOf(_state, who);
                    Fixed(kind, values, who, ev.Other); // ルミナが知らせる
                }
                else
                    Subject(kind, who, values, ev.Other, ev.Kind == IncidentKind.Insight && ev.Detail.Length == 0);
            }

            /// <summary>名前のある人物が話す知らせ（当人の気質で変わらない）。出したら true。</summary>
            public bool Fixed(string kind, Dictionary<string, string> values, Guid? subjectId = null, Guid? other = null, string? dedupeKey = null)
            {
                var rows = !_lumina && EventReportBalance.Get(kind, "nolumina").Count > 0 ? EventReportBalance.Get(kind, "nolumina") : EventReportBalance.Get(kind, "any");
                var subject = subjectId is Guid id ? _state.FindAdventurer(id) : null;
                return Add(kind, rows, values, subject, subjectId, other, false, dedupeKey);
            }

            /// <summary>当人が話す知らせ（初期の3人は専用の台詞、ほかは気質の台詞。無ければ中庸）。</summary>
            public void Subject(string kind, Guid subjectId, Dictionary<string, string> values, Guid? other = null, bool noGrowth = false)
            {
                var a = _state.FindAdventurer(subjectId);
                if (a == null) return;
                values["{名前}"] = a.Name;
                var rows = SquadReportSystem.CharacterKey(a) is { } key && EventReportBalance.Get(kind, $"char:{key}").Count > 0
                    ? EventReportBalance.Get(kind, $"char:{key}")
                    : EventReportBalance.Get(kind, $"temper:{SquadReportSystem.TemperKey(a)}") is { Count: > 0 } temper ? temper
                    : EventReportBalance.Get(kind, "temper:normal");
                Add(kind, rows, values, a, subjectId, other, noGrowth);
            }

            private bool Add(string kind, IReadOnlyList<EventReportBalance.EventLine> rows, Dictionary<string, string> values,
                Adventurer? subject, Guid? subjectId, Guid? other, bool noGrowth, string? dedupeKey = null)
            {
                if (rows.Count == 0) return false;
                string dedupe = dedupeKey ?? $"{kind}:{subjectId}:{other}";
                if (!_seen.Add(dedupe)) return false; // 同じ種類・同じ当人の知らせは月に1回（ボイコットが毎週続くときなど）
                int n = _kindCounts.GetValueOrDefault(kind);
                _kindCounts[kind] = n + 1;
                var row = rows[(_month + n) % rows.Count];
                var segments = row.Text.Split(SegmentSeparator)
                    .Select(t => values.Aggregate(t, (text, kv) => text.Replace(kv.Key, kv.Value)))
                    .Where(t => _lumina || !t.StartsWith("ルミナ「")) // ルミナを救出する前は、ルミナの台詞を出さない
                    .ToList();
                if (noGrowth) segments.Add("「……でも、体がついてこないみたい」"); // ひらめいたが伸びしろが無かった
                if (!segments.Any(t => !t.StartsWith('〔'))) return false; // 台詞が残らなければ出さない
                var report = new EventReport
                {
                    Kind = kind,
                    SubjectId = subjectId,
                    SubjectName = subject?.Name ?? "",
                    PortraitId = subject?.PortraitId,
                    Line = string.Join(SegmentSeparator, segments),
                    Jump = row.Jump,
                };
                int order = row.Jump.Length > 0 ? 0 : subjectId != null && kind is not ("DaughterJoined" or "Nurture" or "NurtureEntrust" or "Title" or "HallOfFame" or "MotherDaughter") ? 1 : 2;
                _reports.Add((order, report));
                return true;
            }
        }
    }
}
