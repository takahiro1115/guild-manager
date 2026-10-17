using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>出す場面1つ（台詞の {階}・{部門}・{施設} を埋め、@variants は1行に絞ったもの）。</summary>
    public sealed class StoryShowing
    {
        public string SceneId { get; init; } = "";
        public List<List<StoryLine>> Pages { get; init; } = new();
    }

    /// <summary>
    /// 物語の場面（2026年10月・§0.86、→ 物語帳「台詞 01・02」、docs/04_バランス表/story/*.txt）。
    /// 場面ごとに「いつ出すか（StoryTiming）」と「出す条件」をここに持ち、台詞そのものは台詞ファイルから読む。
    /// 画面は時機ごとに DueScenes を呼び、出した場面を MarkSeen で記録する（一度きりの場面は二度と出さない）。
    /// 条件はゲームの状態（ボスの撃破・来訪・交流戦・大会の入賞など）と、見た場面・数えたきっかけ（GameState.StoryCounters）で決める。
    /// </summary>
    public static class StorySystem
    {
        private sealed record Rule(string Id, StoryTiming Timing, Func<GameState, bool> IsDue, bool Repeats = false, StoryView? OnView = null);

        /// <summary>場面の並び（同じ時機に重なったらこの順に出す）。</summary>
        private static readonly Rule[] Rules =
        {
            // ---- 台詞01 序章とチュートリアル ----
            new("s01_prologue", StoryTiming.Interactive, s => s.WeekNumber == 1),
            new("s01_guild", StoryTiming.Interactive, s => Seen(s, "s01_prologue") && (s.WeekNumber > 1 || s.Adventurers.Count >= 5)),
            new("s01_order", StoryTiming.Interactive, s => Seen(s, "s01_guild") && s.SavedParties.Any(p => p.MemberIds.Count > 0), OnView: StoryView.Dungeon), // 部隊を組んで、初めて大迷宮の画面を開いたとき（§0.108）
            new("s01_month", StoryTiming.Interactive, s => Seen(s, "s01_order") && s.SavedParties.Any(p => p.MemberIds.Count > 0 && p.Order != SquadOrder.None)),
            new("s01_first_report", StoryTiming.AfterReport, s => s.MonthlyReports.Count > 0),
            new("s01_forest10", StoryTiming.AfterReport, s => ForestBossDefeated(s, 10)),
            new("s01_squad2", StoryTiming.AfterReport, s => s.UnlockedSquadSlots >= 2), // 同時に出せる部隊が2つに（森の10F、§0.113）
            new("s01_lumina", StoryTiming.AfterReport, s => ForestBossDefeated(s, 20)),
            new("s01_squad3", StoryTiming.AfterReport, s => s.UnlockedSquadSlots >= 3), // 3つに（森の20F＋現役7人以上、§0.113・§0.120）
            new("s01_elder", StoryTiming.AfterReport, s => SeenBefore(s, "s01_lumina", s.WeekNumber)),
            new("s01_question", StoryTiming.AfterReport, s => Seen(s, "s01_elder")),
            new("s01_research", StoryTiming.Interactive, s => Seen(s, "s01_question"), OnView: StoryView.Research), // 初めて研究室の画面を開いたとき（§0.114）
            new("s01_hunter", StoryTiming.AfterReport, s => Seen(s, "s01_lumina") && RetreatedBoss(s) != null), // ルミナを救出したあと（§0.140。救出前に2回引き返しても出さない）
            new("s01_bailout", StoryTiming.AfterReport, s => s.AlbertBailoutWeek != null), // アルベールのへそくり（資金の画面が開く前の赤字、§0.140）
            new("s01_newyear", StoryTiming.AfterReport, s => GameCalendar.YearOf(s.WeekNumber) >= 2), // 初めての新春の採用試験と契約更改（2年目のはじめ、§0.140）
            new("s01_negotiation", StoryTiming.AfterReport, s => s.Adventurers.Any(a => !a.IsRetired && a.NeedsNegotiation)), // 初めての契約交渉（§0.140）
            // ---- 台詞02 イザベラの来訪と交流戦 ----
            new("s02_visit", StoryTiming.AfterReport, s => IsabellaSystem.HasVisited(s)),
            new("s02_disciplines", StoryTiming.AfterReport, s => Seen(s, "s02_visit") && IsabellaSystem.ExchangeThisMonth(s) is { Result: null }),
            new("s02_lost", StoryTiming.AfterReport, s => FirstExchange(s) is { Result.WinnerIsOurs: false }),
            new("s02_won", StoryTiming.AfterReport, s => FirstExchange(s) is { Result.WinnerIsOurs: true }),
            new("s02_advice", StoryTiming.AfterReport, s => Seen(s, "s02_lost") || Seen(s, "s02_won")),
            new("s02_rematch_won", StoryTiming.AfterReport, s => NewRematch(s) == true, Repeats: true), // 再戦で初めて勝ったときは、一言のあとに教官の派遣
            new("s02_rematch_lost", StoryTiming.AfterReport, s => NewRematch(s) == false, Repeats: true),
            new("s02_marguerite", StoryTiming.AfterReport, s => Seen(s, "s02_advice") && s.ExchangeWins >= 1),
            new("s02_tournaments", StoryTiming.AfterReport, s => Seen(s, "s02_advice") && s.TournamentCalendarFromWeek is int from && s.WeekNumber >= from),
            new("s02_ledger", StoryTiming.AfterReport, s => s.FundingOpenedWeek > 1 && s.TutorialEnabled), // 帳簿：資金の画面が開いた（王都の大会の次の月、または早めの来訪の月。§0.140）
            new("s02_sponsor", StoryTiming.AfterReport, s => Seen(s, "s02_ledger") && (FundingSystem.Offers(s).Count > 0 || FundingSystem.ActiveContract(s) != null)), // 初めてのスポンサーの話（イザベラが持ってくる、§0.140）
            new("s02_marguerite_leaves", StoryTiming.AfterReport, s => Seen(s, "s02_marguerite") && s.StoryCounters.GetValueOrDefault(GuestLeftKey) > 0),
            new("s02_royal", StoryTiming.AfterReport, s => IsabellaSystem.CommissionsOpen(s) && s.TournamentPlacingsTotal > 0),
            new("s02_invite", StoryTiming.AfterReport, InviteDue, Repeats: true),
            // ---- 台詞03 中盤（§0.89） ----
            new("s03_abyss", StoryTiming.AfterReport, s => FieldUnlocked(s, "abyss")),
            new("s03_wish", StoryTiming.AfterReport, s => SeenBefore(s, "s03_abyss", s.WeekNumber)),
            new("s03_academy2", StoryTiming.AfterReport, s => IsabellaSystem.HasVisited(s) && (s.ExchangeWins >= 2 || BossDefeated(s, "abyss", 50))),
            new("s03_academy4", StoryTiming.AfterReport, s => Seen(s, "s03_academy2") && (s.ExchangeWins >= 4 || BossDefeated(s, "abyss", 50))),
            new("s03_academy6", StoryTiming.AfterReport, s => Seen(s, "s03_academy4") && (s.ExchangeWins >= 6 || BossDefeated(s, "abyss", 50))),
            new("s03_culture", StoryTiming.AfterReport, s => s.SoulFusionCultures.Count > 0 && !Seen(s, "s03_daughter")), // 初めて秘薬を処方した月（§0.118）
            new("s03_daughter", StoryTiming.AfterReport, s => FirstCulture(s) is { } c && SoulFusionBalance.CultureWeeks - c.WeeksRemaining >= GameCalendar.WeeksPerMonth || FirstDaughter(s) != null), // 初めて処方した翌月、生まれた赤子を見せる（§0.123）
            new("s03_g1", StoryTiming.AfterReport, s => FirstG1Win(s) != null),
            new("s03_fallen", StoryTiming.AfterReport, s => s.FallenAdventurers.Count > 0),
            new("s03_bow", StoryTiming.AfterReport, s => Seen(s, "s01_lumina") && s.ExchangeMatchesPlayed >= 3),
            new("s03_sword", StoryTiming.AfterReport, s => Seen(s, "s03_bow") && s.ExchangeMatchesPlayed >= 5),
            new("s03_cecilia", StoryTiming.AfterReport, s => CeciliaBeaten(s) != null),
            new("s03_retire", StoryTiming.AfterReport, s => FirstRetiree(s) != null), // 引退者が役職につかずにギルドを去った月（教官・顧問で残る子では出さない、§0.122）
            new("s03_hall", StoryTiming.AfterReport, s => FirstHallOfFame(s) != null),
            new("s03_yeartop", StoryTiming.AfterReport, s => FirstYearTop(s) != null), // 年末のギルドの順位で初めて1位（§0.145）
            new("s03_sharp", StoryTiming.AfterReport, s => s.StorySeenWeeks.TryGetValue("s01_lumina", out int w) && s.WeekNumber - w >= 5 * GameCalendar.WeeksPerYear),
            // ---- 台詞05 迷宮の主の顔見せ（各フィールドの30F、§0.140。物語帳「台詞 05」） ----
            new("s05_face1", StoryTiming.AfterReport, s => Seen(s, "s01_lumina") && BossDefeated(s, "forest", 30)),
            new("s05_face2", StoryTiming.AfterReport, s => Seen(s, "s01_lumina") && BossDefeated(s, "cave", 30)),
            new("s05_face3", StoryTiming.AfterReport, s => Seen(s, "s01_lumina") && BossDefeated(s, "ruins", 30)),
            new("s05_face4", StoryTiming.AfterReport, s => Seen(s, "s01_lumina") && BossDefeated(s, "canyon", 30)),
            new("s05_face5", StoryTiming.AfterReport, s => Seen(s, "s01_lumina") && BossDefeated(s, "abyss", 30)),
            // 届く声（顔見せから2季節あと、まだその迷宮の70Fに届いていないとき。ネフェリスは前兆が2つ出た次の季節。§0.142）
            new("s05_voice1", StoryTiming.AfterReport, s => SeasonsSince(s, "s05_face1") >= 2 && !BossDefeated(s, "forest", 70)),
            new("s05_voice2", StoryTiming.AfterReport, s => SeasonsSince(s, "s05_face2") >= 2 && !BossDefeated(s, "cave", 70)
                && new[] { "s05_face1", "s05_face3", "s05_face4", "s05_face5" }.Any(id => Seen(s, id))),
            new("s05_voice3", StoryTiming.AfterReport, s => SeasonsSince(s, "s05_face3") >= 2 && !BossDefeated(s, "ruins", 70)),
            new("s05_voice4", StoryTiming.AfterReport, s => !Seen(s, "s04_truth") && SecondOmenWeek(s) is int omen
                && s.WeekNumber / GameCalendar.WeeksPerSeason > (omen - 1) / GameCalendar.WeeksPerSeason),
            // ヴェスペラとの決闘（森の60Fで挑めるようになった・70Fの前哨戦で退かせた・100Fの決着。§0.142）
            new("s05_duel1", StoryTiming.AfterReport, s => Seen(s, "s01_lumina") && BossDefeated(s, "forest", 60)),
            // 作戦会議（初めて戦った月、退かせられなかったとき。攻め方のヒントはここで初めて話す。初戦で退かせたら決闘 2 の最後に一言。§0.143）
            // 招待と同じ月には出さない（招待の前に戦ってしまった月は、招待だけ。会議は次に戦った月）
            new("s05_meet1", StoryTiming.AfterReport, s => MeetingDue(s, RulerSystem.VesperaId)),
            new("s05_duel2", StoryTiming.AfterReport, s => BossDefeated(s, "forest", 70)),
            new("s05_duel3", StoryTiming.AfterReport, s => BossDefeated(s, "forest", 100)),
            // メルジーネ・オルテンシア・イグニア・ネフェリスとの決闘（60Fで挑めるようになった・初戦のあとの作戦会議・70Fの前哨戦で退かせた・100Fの決着。§0.144）
            new("s05_duel4", StoryTiming.AfterReport, s => Seen(s, "s01_lumina") && BossDefeated(s, "cave", 60)),
            new("s05_meet2", StoryTiming.AfterReport, s => MeetingDue(s, RulerSystem.MelusineId)),
            new("s05_duel5", StoryTiming.AfterReport, s => BossDefeated(s, "cave", 70)),
            new("s05_duel6", StoryTiming.AfterReport, s => BossDefeated(s, "cave", 100)),
            new("s05_duel7", StoryTiming.AfterReport, s => Seen(s, "s01_lumina") && BossDefeated(s, "ruins", 60)),
            new("s05_meet3", StoryTiming.AfterReport, s => MeetingDue(s, RulerSystem.OrtensiaId)),
            new("s05_duel8", StoryTiming.AfterReport, s => BossDefeated(s, "ruins", 70)),
            new("s05_duel9", StoryTiming.AfterReport, s => BossDefeated(s, "ruins", 100)),
            new("s05_duel10", StoryTiming.AfterReport, s => Seen(s, "s01_lumina") && BossDefeated(s, "canyon", 60)),
            new("s05_meet4", StoryTiming.AfterReport, s => MeetingDue(s, RulerSystem.IgniaId)),
            new("s05_duel11", StoryTiming.AfterReport, s => BossDefeated(s, "canyon", 70)),
            new("s05_duel12", StoryTiming.AfterReport, s => BossDefeated(s, "canyon", 100)),
            new("s05_duel13", StoryTiming.AfterReport, s => Seen(s, "s01_lumina") && BossDefeated(s, "abyss", 60)),
            new("s05_meet5", StoryTiming.AfterReport, s => MeetingDue(s, RulerSystem.NepherisId)),
            new("s05_duel14", StoryTiming.AfterReport, s => BossDefeated(s, "abyss", 70)),
            // ---- 台詞04 前兆・足跡・真相・エンディング（§0.89）。前兆も足跡も森→洞窟→廃墟→峡谷の順に、前の場面を待つ ----
            new("s04_omen1", StoryTiming.AfterReport, s => Seen(s, "s01_lumina") && BossDefeated(s, "forest", 50)),
            new("s04_omen2", StoryTiming.AfterReport, s => Seen(s, "s04_omen1") && BossDefeated(s, "cave", 50)),
            new("s04_omen3", StoryTiming.AfterReport, s => Seen(s, "s04_omen2") && BossDefeated(s, "ruins", 50)),
            new("s04_omen4", StoryTiming.AfterReport, s => Seen(s, "s04_omen3") && BossDefeated(s, "canyon", 50)),
            new("s04_trail1", StoryTiming.AfterReport, s => (Seen(s, "s04_omen4") || Seen(s, "s05_duel3")) && BossDefeated(s, "forest", 100)), // ヴェスペラとの決着のすぐあと（§0.142）
            new("s04_trail2", StoryTiming.AfterReport, s => (Seen(s, "s04_trail1") || Seen(s, "s05_duel6")) && BossDefeated(s, "cave", 100)), // 決着のすぐあと（§0.144）
            new("s04_trail3", StoryTiming.AfterReport, s => (Seen(s, "s04_trail2") || Seen(s, "s05_duel9")) && BossDefeated(s, "ruins", 100)),
            new("s04_trail4", StoryTiming.AfterReport, s => (Seen(s, "s04_trail3") || Seen(s, "s05_duel12")) && BossDefeated(s, "canyon", 100)),
            // 真相は峡谷の制覇（足跡4）か深淵の90Fの早いほう。深淵が先なら「名を呼ぶ声」→真相の順（足跡4はあとで{真相のあと}の台詞で出る）
            new("s04_namecall", StoryTiming.AfterReport, s => Seen(s, "s01_lumina") && BossDefeated(s, "abyss", 90)),
            new("s04_truth", StoryTiming.AfterReport, s => Seen(s, "s04_trail4") || Seen(s, "s04_namecall")),
            new("s04_home", StoryTiming.AfterReport, s => SeenBefore(s, "s04_truth", s.WeekNumber)),
            new("s05_duel15", StoryTiming.Ending, s => s.IsGameCleared), // ネフェリスとの決着は、都の心臓の前（§0.144）
            new("s04_heart", StoryTiming.Ending, s => s.IsGameCleared),
            new("s04_banquet", StoryTiming.Ending, s => s.IsGameCleared),
            new("s04_sortie", StoryTiming.Ending, s => s.IsGameCleared),
        };

        /// <summary>エンディングの窓に出す締めの語り（会話の小窓ではない。→ Narration）。</summary>
        public const string NarrationSceneId = "s04_narration";

        /// <summary>エンディングの窓の締めの語り（台詞ファイルの s04_narration。最初の〔…〕が見出し）。</summary>
        public static List<StoryLine> Narration(GameState state) =>
            StoryBalance.Get(NarrationSceneId).Pages.SelectMany(p => p).Select(l => l with { Text = Fill(state, l.Text) }).ToList();

        /// <summary>エンディングの場面（見直すとき用：見たかどうかにかかわらず、都の心臓・祝宴・初出撃を返す）。</summary>
        public static List<StoryShowing> EndingScenes(GameState state) =>
            Rules.Where(r => r.Timing == StoryTiming.Ending).Select(r => Build(state, r.Id)).ToList();

        /// <summary>
        /// 操作を教えるだけの場面（§0.88）。「物語と手ほどき なし」で始めたゲームでは出さない（物語の山場だけ残す。ユーザー判断）。
        /// </summary>
        public static readonly IReadOnlyCollection<string> TeachingSceneIds = new HashSet<string>
        {
            "s01_order", "s01_month", "s01_first_report", "s01_question", "s01_hunter",
            "s02_disciplines", "s02_advice", "s02_tournaments", "s01_squad2", "s01_squad3", "s01_research", "s02_sponsor", "s03_culture",
            "s01_newyear", "s01_negotiation", "s02_ledger",
        };

        /// <summary>
        /// 「物語と手ほどき なし」で始める（§0.88。新しいゲームの初めに呼ぶ）：操作を教える場面を見たことにして飛ばし、
        /// 手引きを出さず、すべての画面のタブを見せる。ゲームの決まり（大会・依頼の開き方）は変えない。
        /// </summary>
        public static void DisableTutorial(GameState state)
        {
            state.TutorialEnabled = false;
            foreach (var id in TeachingSceneIds)
                state.StorySeenWeeks.TryAdd(id, 0);
            GuideSystem.MarkAllDone(state);
        }

        public const string GuestLeftKey = "guestLeft";
        private const string RematchKey = "rematchCommented";
        private static string RetreatKey(Guid bossId) => $"retreat:{bossId}";
        private static string SeenCountKey(string id) => $"seen:{id}";

        /// <summary>コードが使う場面のId（台詞ファイルに揃っているかのテストに使う）。</summary>
        public static IEnumerable<string> SceneIds => Rules.Select(r => r.Id);

        /// <summary>
        /// 今の時機に出す場面（並びの順。台詞を埋めて返す）。前の場面を見たことが条件の場面（来訪→剣・魔・技、交流戦→助言など）も、
        /// 同じ時機に続けて出せるよう、並びの前の場面を見たことにして順に判定する（記録はしない。出した場面は画面が MarkSeen する）。
        /// currentView は今開いている画面（大迷宮・研究室の画面で見せる場面は、ほかの画面のあいだは待つ。画面の無いシミュレーター・テストは null＝どの場面も出す）。
        /// </summary>
        public static List<StoryShowing> DueScenes(GameState state, StoryTiming timing, StoryView? currentView = null)
        {
            var due = new List<StoryShowing>();
            var assumed = new List<string>();
            try
            {
                foreach (var rule in Rules.Where(r => r.Timing == timing))
                {
                    if ((!rule.Repeats && Seen(state, rule.Id)) || (rule.OnView is { } view && currentView is { } now && view != now) || !rule.IsDue(state))
                        continue;
                    due.Add(Build(state, rule.Id));
                    if (state.StorySeenWeeks.TryAdd(rule.Id, state.WeekNumber))
                        assumed.Add(rule.Id);
                }
            }
            finally
            {
                foreach (var id in assumed)
                    state.StorySeenWeeks.Remove(id);
            }
            return due;
        }

        /// <summary>物語の記録の章（台詞ファイルの番号＝場面のIdの頭 s01〜s04 → 画面に出す章の名前、§0.92）。</summary>
        public static readonly IReadOnlyList<(string Prefix, string Title)> RecordChapters = new[]
        {
            ("s01", "一　出会い"),
            ("s02", "二　白百合の杖"),
            ("s03", "三　ギルドの日々"),
            ("s04", "四　失われた理想郷"),
            ("s05", "五　迷宮の主"),
        };

        /// <summary>
        /// 物語の記録（§0.92）：見た場面を章ごとに、見た順（同じ週なら場面の並び順）に並べる。毎回の一言（交流戦の一言・再戦の申し込み）は載せない。
        /// 見た週が0の場面は「物語と手ほどき なし」で見たことにした場面（時期を出さない）。
        /// </summary>
        public static List<StoryRecordChapter> Record(GameState state)
        {
            var order = Rules.Select((r, i) => (r, i)).ToDictionary(x => x.r.Id, x => x.i);
            var chapters = new List<StoryRecordChapter>();
            foreach (var (prefix, title) in RecordChapters)
            {
                var entries = Rules
                    .Where(r => !r.Repeats && r.Id.StartsWith(prefix + "_") && state.StorySeenWeeks.ContainsKey(r.Id))
                    .OrderBy(r => state.StorySeenWeeks[r.Id]).ThenBy(r => order[r.Id])
                    .Select(r => new StoryRecordEntry(r.Id, RecordTitle(StoryBalance.Get(r.Id).Title), state.StorySeenWeeks[r.Id]))
                    .ToList();
                if (entries.Count > 0)
                    chapters.Add(new StoryRecordChapter(title, entries));
            }
            return chapters;
        }

        /// <summary>
        /// 記録に出す場面の名前：台詞ファイルの見出しから、頭の番号（「3　」「3a　」「6-2　」）と、きっかけの書き添え（最後の（…）から後ろ）を除く。
        /// 「前兆 1」「エンディング 2」のような言葉つきの番号は残す。
        /// </summary>
        public static string RecordTitle(string heading)
        {
            string title = Regex.Replace(heading, "（[^（）]*）(→.*)?$", "").Trim();
            title = Regex.Replace(title, @"^[0-9]+[a-z]?(-[0-9]+)?[　 ]+", "");
            title = Regex.Replace(title, "^（[^（）]*）", "").Trim();
            return title.Length > 0 ? title : heading;
        }

        /// <summary>記録から見返す場面（台詞を今の記録で埋める。見たことの記録や数は変えない）。</summary>
        public static StoryShowing Replay(GameState state, string sceneId) => Build(state, sceneId);

        /// <summary>
        /// 小窓のコマ（§0.107、物語帳の舞台と同じ見せ方）：台詞と地の文を1行ずつ、ページ（▼）をまたいで順に並べる。
        /// 背景はその行までの最後の〔背景：…〕（場面をまたいでは引き継がない）。立ち絵はその行で話す人の1枚だけ（表情つきなら表情の絵、無ければ通常）。
        /// 画面へ・手引きの行はコマにしない（画面が最後に添える）。
        /// </summary>
        public static IReadOnlyList<StoryStep> StepsOf(StoryShowing showing)
        {
            var steps = new List<StoryStep>();
            string background = "";
            foreach (var line in showing.Pages.SelectMany(p => p))
            {
                switch (line.Kind)
                {
                    case StoryLineKind.Background:
                        background = line.Text;
                        break;
                    case StoryLineKind.Speech or StoryLineKind.Narration:
                        string? portrait = line.Kind == StoryLineKind.Speech ? StoryImageBalance.PortraitFile(line.Speaker, line.Expression) : null;
                        steps.Add(new StoryStep(line, background, background.Length > 0 ? StoryImageBalance.BackgroundFile(background) : null, portrait));
                        break;
                }
            }
            return steps;
        }

        /// <summary>場面を出したことを記録する（見た週・回数。交流戦の一言は、どの交流戦まで言ったかも）。</summary>
        public static void MarkSeen(GameState state, string sceneId)
        {
            state.StorySeenWeeks[sceneId] = state.WeekNumber;
            state.StoryCounters[SeenCountKey(sceneId)] = state.StoryCounters.GetValueOrDefault(SeenCountKey(sceneId)) + 1;
            if (sceneId is "s02_rematch_won" or "s02_rematch_lost")
                state.StoryCounters[RematchKey] = state.ExchangeMatchesPlayed;
            // 手引き（§0.87）で、あとから同じ対象を指せるように覚えておく（撃破や次の訓練所の開放で変わるため）
            if (sceneId == "s04_sortie")
                PostGameSystem.EnsureLumina(state); // 初出撃のあと、ルミナが名簿に加わる（§0.90）
            if (sceneId == "s01_hunter" && RetreatedBoss(state) is { } boss)
            {
                state.StoryCounters[HunterFieldKey] = boss.FieldOrder;
                state.StoryCounters[HunterFloorKey] = boss.Floor;
            }
            if (sceneId == "s02_advice" && FacilityUnlockSystem.TrainingFacilities.Where(t => FacilityUnlockSystem.GetUnlockedLevel(state, t) >= 1).ToList() is { Count: > 0 } opened)
                state.StoryCounters[AdviceFacilityKey] = (int)opened[0];
        }

        public const string HunterFieldKey = "hunterField";
        public const string HunterFloorKey = "hunterFloor";
        public const string AdviceFacilityKey = "adviceFacility";

        /// <summary>獲物の癖の場面で指したボス（見たあとは覚えたもの、見る前は今の撤退の多いボス）。</summary>
        public static FloorBoss? HunterBoss(GameState state) =>
            state.StoryCounters.TryGetValue(HunterFieldKey, out int field) && state.StoryCounters.TryGetValue(HunterFloorKey, out int floor)
                ? state.DungeonFields.SelectMany(f => f.Bosses).FirstOrDefault(b => b.FieldOrder == field && b.Floor == floor)
                : RetreatedBoss(state);

        /// <summary>最初の助言で勧めた訓練所（見たあとは覚えたもの、見る前は開いている訓練所）。</summary>
        public static FacilityType? AdviceFacility(GameState state) =>
            state.StoryCounters.TryGetValue(AdviceFacilityKey, out int type) ? (FacilityType)type
            : FacilityUnlockSystem.TrainingFacilities.Where(t => FacilityUnlockSystem.GetUnlockedLevel(state, t) >= 1).Select(t => (FacilityType?)t).FirstOrDefault();

        /// <summary>台詞・手引きの {階}・{部門}・{施設} を埋める。</summary>
        public static string Fill(GameState state, string text, string? sceneId = null) =>
            Placeholders(state, sceneId).Aggregate(text, (t, kv) => t.Replace(kv.Key, kv.Value));

        public static bool Seen(GameState state, string sceneId) => state.StorySeenWeeks.ContainsKey(sceneId);

        /// <summary>§0.86より前に始めたゲーム：チュートリアルの一度きりの場面をすべて見たことにし、交流戦の一言も今までの分は言ったことにする。</summary>
        public static void MarkTutorialSeen(GameState state)
        {
            foreach (var rule in Rules.Where(r => !r.Repeats))
                state.StorySeenWeeks.TryAdd(rule.Id, state.WeekNumber);
            state.StoryCounters[RematchKey] = state.ExchangeMatchesPlayed;
            GuideSystem.MarkAllDone(state); // 手引きも出さない（§0.87）
        }

        /// <summary>
        /// §0.140より前のセーブ：序盤の流れで足した場面のうち、もう過ぎたもの（2年目に入っていれば新春・初めての契約交渉、30Fを倒したフィールドの迷宮の主の顔見せ）を
        /// 見たことにする（読み込んだ月に、いくつもの場面が一度に出ないように）。
        /// </summary>
        public static void MarkEarlyFlowSeen(GameState state)
        {
            if (GameCalendar.YearOf(state.WeekNumber) >= 2)
            {
                state.StorySeenWeeks.TryAdd("s01_newyear", state.WeekNumber);
                state.StorySeenWeeks.TryAdd("s01_negotiation", state.WeekNumber);
            }
            foreach (var (id, field) in new[] { ("s05_face1", "forest"), ("s05_face2", "cave"), ("s05_face3", "ruins"), ("s05_face4", "canyon"), ("s05_face5", "abyss") })
                if (BossDefeated(state, field, 30))
                    state.StorySeenWeeks.TryAdd(id, state.WeekNumber);
        }

        /// <summary>
        /// §0.142より前のセーブ：迷宮の主の場面のうち、もう過ぎたもの（倒した階の決闘、前兆が2つ出ていればネフェリスの夢）を見たことにする。
        /// 届く声 1〜3 は顔見せから2季節あとに、その70Fにまだ届いていなければ出る（もう届いていれば出ない）ので、そのままにする。
        /// </summary>
        public static void MarkRulerScenesSeen(GameState state)
        {
            foreach (var (id, floor) in new[] { ("s05_duel1", 60), ("s05_duel2", 70), ("s05_duel3", 100) })
                if (BossDefeated(state, "forest", floor))
                    state.StorySeenWeeks.TryAdd(id, state.WeekNumber);
            if (SecondOmenWeek(state) != null)
                state.StorySeenWeeks.TryAdd("s05_voice4", state.WeekNumber);
        }

        /// <summary>
        /// §0.143より前のセーブ：もう迷宮の主と戦ったことがあれば、その作戦会議を見たことにする
        /// （前の決闘 1 で攻め方を聞いているので、読み込んだ月に会議が出たり、決闘 2 に初戦の一言が足されたりしないように）。
        /// </summary>
        public static void MarkStrategyMeetingsSeen(GameState state)
        {
            foreach (var r in RulerScenes)
                if (DuelCount(state, r.RulerId) >= 1)
                    state.StorySeenWeeks.TryAdd(r.Meeting, state.WeekNumber);
        }

        /// <summary>
        /// §0.144より前のセーブ：メルジーネ・オルテンシア・イグニア・ネフェリスとの決闘の場面のうち、もう倒した階のもの（60F・70F・100F）を見たことにする
        /// （読み込んだ月に、過ぎた場面が一度に出ないように。ネフェリスの決着はクリアのときだけ出るので、クリア済みなら見たことにする）。
        /// </summary>
        public static void MarkLaterRulerScenesSeen(GameState state)
        {
            foreach (var r in RulerScenes.Where(r => r.RulerId != RulerSystem.VesperaId))
                foreach (var (id, floor) in new[] { (r.Invite, 60), (r.Retreat, 70), (r.Final, 100) })
                    if (BossDefeated(state, r.FieldId, floor))
                        state.StorySeenWeeks.TryAdd(id, state.WeekNumber);
        }

        /// <summary>迷宮の主ごとの場面のId（招待・作戦会議・退く・決着）。</summary>
        private static readonly (string RulerId, string FieldId, string Invite, string Meeting, string Retreat, string Final)[] RulerScenes =
        {
            (RulerSystem.VesperaId, RulerSystem.ForestId, "s05_duel1", "s05_meet1", "s05_duel2", "s05_duel3"),
            (RulerSystem.MelusineId, RulerSystem.CaveId, "s05_duel4", "s05_meet2", "s05_duel5", "s05_duel6"),
            (RulerSystem.OrtensiaId, RulerSystem.RuinsId, "s05_duel7", "s05_meet3", "s05_duel8", "s05_duel9"),
            (RulerSystem.IgniaId, RulerSystem.CanyonId, "s05_duel10", "s05_meet4", "s05_duel11", "s05_duel12"),
            (RulerSystem.NepherisId, RulerSystem.AbyssId, "s05_duel13", "s05_meet5", "s05_duel14", "s05_duel15"),
        };

        /// <summary>
        /// 作戦会議を出すか：招待を前の週までに見ていて、その迷宮の主と1回以上戦い、70Fをまだ倒していない（初戦で退かせたら出さない）。
        /// 招待と同じ月に戦っていたら、その月は招待だけで、会議は次に戦った月（§0.143）。
        /// </summary>
        private static bool MeetingDue(GameState state, string rulerId)
        {
            var r = RulerScenes.First(x => x.RulerId == rulerId);
            return SeenBefore(state, r.Invite, state.WeekNumber) && DuelCount(state, rulerId) >= 1 && !BossDefeated(state, r.FieldId, 70);
        }

        /// <summary>年末のギルドの順位で初めて1位になった年の順位表（無ければ null）。</summary>
        private static GuildStanding? FirstYearTop(GameState state) =>
            state.GuildStandings.OrderBy(st => st.Year).FirstOrDefault(st => st.Rows.Any(r => r.Ours && r.Rank == 1));

        /// <summary>§0.145より前のセーブ：もう年末の順位で1位になった年があれば、初めての年間1位の場面を見たことにする。</summary>
        public static void MarkYearTopSeen(GameState state)
        {
            if (FirstYearTop(state) != null)
                state.StorySeenWeeks.TryAdd("s03_yeartop", state.WeekNumber);
        }

        /// <summary>その迷宮の主と戦った回数（まだ現れていなければ0）。</summary>
        private static int DuelCount(GameState state, string rulerId) =>
            state.Rulers.FirstOrDefault(r => r.RulerId == rulerId)?.DuelCount ?? 0;

        /// <summary>
        /// 週の決算のあと：場面のきっかけを数える（扉前の撤退・決戦の負け・派遣の教官が帰った）。WeekProcessingSystem が呼ぶ。
        /// 決戦の負けも「勝てずに引き返した」1回に数える（§0.98）。
        /// </summary>
        public static void ProcessWeek(GameState state, WeeklySettlementResult result)
        {
            foreach (var r in result.DungeonMissionResolutions.Where(r => r.Boss != null && IsStalledAtBoss(r)))
                state.StoryCounters[RetreatKey(r.Boss!.Id)] = state.StoryCounters.GetValueOrDefault(RetreatKey(r.Boss.Id)) + 1;
            if (result.GuestTrainer.Left != null)
                state.StoryCounters[GuestLeftKey] = state.StoryCounters.GetValueOrDefault(GuestLeftKey) + 1;
        }

        /// <summary>扉前で撤退したか、決戦で負けたか（§0.98）。</summary>
        private static bool IsStalledAtBoss(DungeonMissionResolution r) =>
            (r.ArrivedAtBossDoor && r.DoorRetreatReason != null) || r.DungeonResult?.Outcome == DungeonOutcome.Retreat;

        // ==================== 条件 ====================

        /// <summary>その場面を見てから何季節たったか（見ていなければ −1）。</summary>
        private static int SeasonsSince(GameState state, string sceneId) =>
            state.StorySeenWeeks.TryGetValue(sceneId, out int seen) ? (state.WeekNumber - seen) / GameCalendar.WeeksPerSeason : -1;

        /// <summary>前兆（台詞 04）の2つ目を見た週（2つ見ていなければ null）。</summary>
        private static int? SecondOmenWeek(GameState state)
        {
            var weeks = new[] { "s04_omen1", "s04_omen2", "s04_omen3", "s04_omen4" }
                .Where(id => state.StorySeenWeeks.ContainsKey(id)).Select(id => state.StorySeenWeeks[id]).OrderBy(w => w).ToList();
            return weeks.Count >= 2 ? weeks[1] : null;
        }

        private static bool SeenBefore(GameState state, string sceneId, int week) =>
            state.StorySeenWeeks.TryGetValue(sceneId, out int seen) && seen < week;

        private static bool ForestBossDefeated(GameState state, int floor) => BossDefeated(state, IsabellaBalance.VisitFieldId, floor);

        private static bool BossDefeated(GameState state, string fieldId, int floor) =>
            state.DungeonFields.FirstOrDefault(f => f.Id == fieldId)?.Bosses.Any(b => b.Floor == floor && b.EverDefeated) == true; // 蘇った主も（§0.90）

        private static bool FieldUnlocked(GameState state, string fieldId) =>
            state.DungeonFields.FirstOrDefault(f => f.Id == fieldId)?.IsUnlocked == true;

        // ---- 記録（台詞の {名前} などを埋める。§0.89） ----

        private static IEnumerable<Adventurer> Everyone(GameState state) =>
            state.Adventurers.Concat(state.GuildRetirees).Concat(state.FallenAdventurers);

        /// <summary>
        /// 最初にギルドを去った引退者（§0.122：初めての引退式〈s03_retire〉とエンディングの「初めての引退者」）。
        /// 教官・顧問として残っている子は数えない（役職を外れて、のちに去ったら数える）。去った週の順、同じ週なら引退した週の順。
        /// </summary>
        public static Adventurer? FirstRetiree(GameState state) =>
            state.GuildRetirees.Where(a => a.LeftGuild).OrderBy(a => a.LeftGuildAtWeek ?? int.MaxValue).ThenBy(a => a.RetiredAtWeek ?? int.MaxValue).FirstOrDefault();

        /// <summary>最初に殿堂入りした者。</summary>
        public static Adventurer? FirstHallOfFame(GameState state) =>
            state.GuildRetirees.Where(a => a.HallOfFameYear != null).OrderBy(a => a.RetiredAtWeek ?? int.MaxValue).FirstOrDefault();

        /// <summary>魂魄融和で最初に生まれた娘（加わった年の順、同じ年なら名簿の順）。</summary>
        public static Adventurer? FirstDaughter(GameState state) =>
            Everyone(state).Where(a => a.ParentIds.Count >= 2).OrderBy(a => a.JoinedYear).FirstOrDefault();

        /// <summary>いちばん古い培養（培養槽の娘、§0.118）。無ければ null。</summary>
        public static SoulFusionCulture? FirstCulture(GameState state) =>
            state.SoulFusionCultures.OrderBy(c => c.WeeksRemaining).FirstOrDefault();

        /// <summary>初めての娘（s03_daughter）に立ち会う子の印の名前（物語帳の行末の {○○が立ち会うなら}、§0.123）。</summary>
        private static readonly Dictionary<string, string> WitnessNames = new() { ["fiona"] = "フィオナ", ["rina"] = "リナ", ["claudia"] = "クラウディア" };

        /// <summary>
        /// 初めての娘（s03_daughter）に立ち会う子（§0.123）：初期の3人のうち、その娘の母でない子。2人以上ならフィオナ→リナ→クラウディアの順。
        /// ギルドを去った子・除籍された子は立ち会わない（誰もいなければ null で、立ち会いの行を飛ばす）。
        /// </summary>
        public static Adventurer? DaughterWitness(GameState state)
        {
            var mothers = FirstDaughter(state) is { } daughter ? daughter.ParentIds.ToHashSet()
                : FirstCulture(state) is { } culture ? new HashSet<Guid> { culture.ParentAId, culture.ParentBId } : new HashSet<Guid>();
            var inGuild = state.Adventurers.Concat(state.GuildRetirees.Where(r => !r.LeftGuild)).ToList();
            return WitnessNames.Keys.Select(key => inGuild.FirstOrDefault(a => a.PortraitId == key)).FirstOrDefault(a => a != null && !mothers.Contains(a.Id));
        }

        /// <summary>最初のG1の優勝（交流戦は除く）。</summary>
        private static TournamentEvent? FirstG1Win(GameState state) =>
            state.TournamentEvents.Where(e => e.Kind != TournamentKind.Exchange && e.Grade == TournamentGrade.G1 && e.Result?.WinnerIsOurs == true)
                .OrderBy(e => e.Year).ThenBy(e => e.Month).ThenBy(e => e.Week).FirstOrDefault();

        /// <summary>
        /// 大会（交流戦は除く）でのギルドの最高の入賞の言い方（「優勝」「準優勝」「ベスト4」）。入賞が無ければ null。
        /// s02_royal（初めての入賞）の {入賞} に使う（§0.112：優勝してもベスト4と言わないように）。
        /// </summary>
        public static string? BestPlacingLabel(GameState state)
        {
            int best = state.TournamentEvents.Where(e => e.Kind != TournamentKind.Exchange && e.Result != null)
                .SelectMany(e => e.Result!.Placings).Select(p => p.Placing).Where(p => p >= 1 && p <= 4)
                .DefaultIfEmpty(0).Min();
            return best switch { 1 => "優勝", 2 => "準優勝", 4 => "ベスト4", _ => null };
        }

        /// <summary>交流戦の魔（セシリア）に初めて勝った者の名前。無ければ null。</summary>
        private static string? CeciliaBeaten(GameState state) =>
            state.TournamentEvents.Where(e => e.Kind == TournamentKind.Exchange && e.Result != null)
                .OrderBy(e => e.Year).ThenBy(e => e.Month)
                .Select(e => e.Result!.Matches.FirstOrDefault(m => m.Round == 2 && m.AWon && m.NameB == IsabellaBalance.Opponent(TournamentDiscipline.Magic)))
                .FirstOrDefault(m => m != null)?.NameA;

        /// <summary>扉前の撤退と決戦の負けが合わせて2回以上の、まだ倒していないボス（いちばん浅いもの）。無ければ null。</summary>
        public static FloorBoss? RetreatedBoss(GameState state) =>
            state.DungeonFields.SelectMany(f => f.Bosses)
                .Where(b => !b.IsDefeated && state.StoryCounters.GetValueOrDefault(RetreatKey(b.Id)) >= 2)
                .OrderBy(b => b.FieldOrder).ThenBy(b => b.Floor).FirstOrDefault();

        /// <summary>最初の交流戦（行ったもの）。</summary>
        internal static TournamentEvent? FirstExchange(GameState state) =>
            state.TournamentEvents.Where(e => e.Kind == TournamentKind.Exchange && e.Result != null)
                .OrderBy(e => e.Year).ThenBy(e => e.Month).FirstOrDefault();

        /// <summary>まだ一言を言っていない2回目以降の交流戦があれば、その勝ち負け（最後の交流戦）。無ければ null。</summary>
        private static bool? NewRematch(GameState state)
        {
            if (state.ExchangeMatchesPlayed < 2 || state.StoryCounters.GetValueOrDefault(RematchKey) >= state.ExchangeMatchesPlayed)
                return null;
            var last = state.TournamentEvents.Where(e => e.Kind == TournamentKind.Exchange && e.Result != null)
                .OrderBy(e => e.Year).ThenBy(e => e.Month).LastOrDefault();
            return last?.Result?.WinnerIsOurs;
        }

        /// <summary>再戦の申し込み：季節のはじめに申し込めて、前回の交流戦から2季節以上空いていて、この季節にまだ言っていない。</summary>
        private static bool InviteDue(GameState state)
        {
            if (GameCalendar.WeekOfSeason(state.WeekNumber) != 1 || IsabellaSystem.ApplyBlockReason(state) != null)
                return false;
            int season = SeasonIndex(state.WeekNumber);
            var last = state.TournamentEvents.Where(e => e.Kind == TournamentKind.Exchange)
                .Select(e => SeasonIndex(GameCalendar.WeekNumberOf(e.Year, e.Month, 1))).DefaultIfEmpty(int.MinValue / 2).Max();
            bool saidThisSeason = state.StorySeenWeeks.TryGetValue("s02_invite", out int seen) && SeasonIndex(seen) == season;
            return season - last >= 2 && !saidThisSeason;
        }

        private static int SeasonIndex(int week) => (week - 1) / GameCalendar.WeeksPerSeason;

        // ==================== 台詞を埋める ====================

        private static StoryShowing Build(GameState state, string sceneId)
        {
            var scene = StoryBalance.Get(sceneId);
            // 埋められない {…} が残った行（記録が無い・出す条件の印が合わない）は出さない（§0.89）。空になったページも詰める。
            var pages = scene.Pages
                .Select(p => p.Select(l => l with { Text = Fill(state, l.Text, sceneId) }).Where(l => !l.Text.Contains('{')).ToList())
                .Where(p => p.Count > 0).ToList();
            // 背景の指定だけが残ったページは次のページの頭へ寄せる（§0.91。背景だけの空のページを出さない）
            for (int i = pages.Count - 2; i >= 0; i--)
                if (pages[i].All(l => l.Kind == StoryLineKind.Background))
                {
                    pages[i + 1].InsertRange(0, pages[i]);
                    pages.RemoveAt(i);
                }

            if (scene.Variants)
            {
                // 台詞の行のうち1つだけ（見た回数の順に回す）。それ以外の行（手引きなど）はそのまま。
                // 引退したライバルの子（白百合の杖の看板など）の行は出さない（§0.93）
                var speeches = pages.SelectMany(p => p).Where(l => l.Kind == StoryLineKind.Speech && !RivalSystem.IsRetiredRival(state, l.Speaker)).ToList();
                var pick = speeches.Count > 0 ? speeches[state.StoryCounters.GetValueOrDefault(SeenCountKey(sceneId)) % speeches.Count] : null;
                pages = new List<List<StoryLine>> { pages.SelectMany(p => p).Where(l => l.Kind != StoryLineKind.Speech || ReferenceEquals(l, pick)).ToList() };
            }
            return new StoryShowing { SceneId = sceneId, Pages = pages };
        }

        private static Dictionary<string, string> Placeholders(GameState state, string? sceneId = null)
        {
            var values = new Dictionary<string, string>();
            // 台詞03・04（§0.89）：場面ごとの {名前}、ギルドの記録（初めての娘・殿堂・初めての引退者）、出す行を分ける印
            string? name = sceneId switch
            {
                "s03_retire" => FirstRetiree(state)?.Name,
                "s03_hall" => FirstHallOfFame(state)?.Name,
                "s03_fallen" => state.FallenAdventurers.FirstOrDefault()?.Name,
                "s03_g1" => FirstG1Win(state)?.Result?.WinnerName,
                "s03_cecilia" => CeciliaBeaten(state),
                "s01_negotiation" => state.Adventurers.FirstOrDefault(a => !a.IsRetired && a.NeedsNegotiation)?.Name,
                _ => null,
            };
            if (name != null) values["{名前}"] = name;
            if (FirstG1Win(state) is { } g1) values["{大会}"] = g1.Name;
            values["{入賞}"] = BestPlacingLabel(state) ?? "入賞";
            if (FirstDaughter(state) is { } daughter)
            {
                values["{娘}"] = values["{初めての娘の名前}"] = daughter.Name;
                var mothers = daughter.ParentIds.Select(state.FindAdventurer).ToList();
                if (mothers.Count >= 2 && mothers[0] != null && mothers[1] != null)
                {
                    values["{母A}"] = mothers[0]!.Name;
                    values["{母B}"] = mothers[1]!.Name;
                }
            }
            else if (FirstCulture(state) is { } culture && state.FindAdventurer(culture.ParentAId) is { } motherA && state.FindAdventurer(culture.ParentBId) is { } motherB)
            {
                // 培養槽の娘（§0.118）：まだ名簿にいないので、いちばん古い培養の母と子の名前（§0.123）
                values["{娘}"] = culture.Child.Name;
                values["{母A}"] = motherA.Name;
                values["{母B}"] = motherB.Name;
            }
            if (DaughterWitness(state) is { } witness) values[$"{{{WitnessNames[witness.PortraitId!]}が立ち会うなら}}"] = ""; // 初めての娘に立ち会う子（§0.123）
            if (FirstHallOfFame(state) is { } hall) values["{殿堂の名前}"] = hall.Name;
            if (FirstRetiree(state) is { } retiree)
            {
                values["{初めての引退者の名前}"] = retiree.Name;
                if (retiree.HallOfFameYear != null) values["{殿堂入りなら}"] = "";
            }
            values[Seen(state, "s04_truth") ? "{真相のあと}" : "{真相の前}"] = "";
            // 序盤の流れ（§0.140）：へそくりの額・ルミナの有無・早めの来訪かどうか・契約交渉の期限
            values["{金額}"] = FundingBalance.AlbertBailoutGold.ToString();
            if (Seen(state, "s01_lumina")) values["{ルミナがいるなら}"] = "";
            values[state.IsabellaEarlyVisit ? "{早めの来訪なら}" : "{森の来訪なら}"] = "";
            values["{週}"] = SatisfactionBalance.NegotiationGraceWeeks.ToString();
            if (IsabellaSystem.HasVisited(state)) values["{来訪のあとなら}"] = "";
            // 初めての年間1位（§0.145）：手紙をくれるライバルギルドが王都にいるか
            bool crimson = state.RivalGuilds.Any(g => g.Id == "crimson"), silvermoon = state.RivalGuilds.Any(g => g.Id == "silvermoon");
            if (crimson) values["{紅蓮の牙がいるなら}"] = "";
            if (silvermoon) values["{銀月の弓がいるなら}"] = "";
            if (crimson || silvermoon) values["{手紙が来たなら}"] = "";
            // 迷宮の主の場面（§0.142）：決闘の最後に勝った代表、ネフェリスの夢の出し分け
            values["{代表}"] = state.Rulers.FirstOrDefault(r => r.RulerId == RulerSystem.VesperaId)?.LastVictorName ?? "代表";
            values[Seen(state, "s04_omen4") ? "{前兆4のあと}" : "{前兆4の前}"] = "";
            // 退く場面で、その迷宮の主の作戦会議を見ていない＝初戦で退かせた（§0.143・§0.144）
            if (sceneId != null && RulerScenes.FirstOrDefault(r => r.Retreat == sceneId) is { Meeting: { } meeting } && !Seen(state, meeting))
                values["{初戦で退かせたなら}"] = "";
            values[Seen(state, "s04_omen2") ? "{前兆2のあと}" : "{前兆2の前}"] = ""; // ルミナが水妖の歌を聞き取ったあとか（作戦会議 2）
            if (HunterBoss(state) is { } boss)
                values["{階}"] = boss.Floor.ToString();
            if (AdviceFacility(state) is FacilityType training)
            {
                values["{施設}"] = FacilityUnlockSystem.FacilityName(training);
                values["{部門}"] = TournamentSystem.DisciplineLabel(FacilityUnlockSystem.DisciplineOf(training));
            }
            return values;
        }
    }
}
