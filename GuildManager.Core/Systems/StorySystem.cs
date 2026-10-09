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
        private sealed record Rule(string Id, StoryTiming Timing, Func<GameState, bool> IsDue, bool Repeats = false);

        /// <summary>場面の並び（同じ時機に重なったらこの順に出す）。</summary>
        private static readonly Rule[] Rules =
        {
            // ---- 台詞01 序章とチュートリアル ----
            new("s01_prologue", StoryTiming.Interactive, s => s.WeekNumber == 1),
            new("s01_guild", StoryTiming.Interactive, s => Seen(s, "s01_prologue") && (s.WeekNumber > 1 || s.Adventurers.Count >= 5)),
            new("s01_order", StoryTiming.Interactive, s => Seen(s, "s01_guild") && s.SavedParties.Any(p => p.MemberIds.Count > 0)),
            new("s01_month", StoryTiming.Interactive, s => Seen(s, "s01_order") && s.SavedParties.Any(p => p.MemberIds.Count > 0 && p.Order != SquadOrder.None)),
            new("s01_first_report", StoryTiming.AfterReport, s => s.MonthlyReports.Count > 0),
            new("s01_forest10", StoryTiming.BeforeReport, s => ForestBossDefeated(s, 10)),
            new("s01_lumina", StoryTiming.BeforeReport, s => ForestBossDefeated(s, 20)),
            new("s01_elder", StoryTiming.AfterReport, s => SeenBefore(s, "s01_lumina", s.WeekNumber)),
            new("s01_question", StoryTiming.AfterReport, s => Seen(s, "s01_elder")),
            new("s01_hunter", StoryTiming.BeforeReport, s => RetreatedBoss(s) != null),
            // ---- 台詞02 イザベラの来訪と交流戦 ----
            new("s02_visit", StoryTiming.BeforeReport, s => IsabellaSystem.HasVisited(s)),
            new("s02_disciplines", StoryTiming.AfterReport, s => Seen(s, "s02_visit") && IsabellaSystem.ExchangeThisMonth(s) is { Result: null }),
            new("s02_lost", StoryTiming.BeforeReport, s => FirstExchange(s) is { Result.WinnerIsOurs: false }),
            new("s02_won", StoryTiming.BeforeReport, s => FirstExchange(s) is { Result.WinnerIsOurs: true }),
            new("s02_advice", StoryTiming.BeforeReport, s => Seen(s, "s02_lost") || Seen(s, "s02_won")),
            new("s02_rematch_won", StoryTiming.BeforeReport, s => NewRematch(s) == true, Repeats: true), // 再戦で初めて勝ったときは、一言のあとに教官の派遣
            new("s02_rematch_lost", StoryTiming.BeforeReport, s => NewRematch(s) == false, Repeats: true),
            new("s02_marguerite", StoryTiming.BeforeReport, s => Seen(s, "s02_advice") && s.ExchangeWins >= 1),
            new("s02_tournaments", StoryTiming.BeforeReport, s => Seen(s, "s02_advice") && s.TournamentCalendarFromWeek is int from && s.WeekNumber >= from),
            new("s02_marguerite_leaves", StoryTiming.BeforeReport, s => Seen(s, "s02_marguerite") && s.StoryCounters.GetValueOrDefault(GuestLeftKey) > 0),
            new("s02_royal", StoryTiming.BeforeReport, s => IsabellaSystem.CommissionsOpen(s) && s.TournamentPlacingsTotal > 0),
            new("s02_invite", StoryTiming.AfterReport, InviteDue, Repeats: true),
            // ---- 台詞03 中盤（§0.89） ----
            new("s03_abyss", StoryTiming.BeforeReport, s => FieldUnlocked(s, "abyss")),
            new("s03_wish", StoryTiming.AfterReport, s => SeenBefore(s, "s03_abyss", s.WeekNumber)),
            new("s03_academy2", StoryTiming.BeforeReport, s => IsabellaSystem.HasVisited(s) && (s.ExchangeWins >= 2 || BossDefeated(s, "abyss", 50))),
            new("s03_academy4", StoryTiming.BeforeReport, s => Seen(s, "s03_academy2") && (s.ExchangeWins >= 4 || BossDefeated(s, "abyss", 50))),
            new("s03_academy6", StoryTiming.BeforeReport, s => Seen(s, "s03_academy4") && (s.ExchangeWins >= 6 || BossDefeated(s, "abyss", 50))),
            new("s03_daughter", StoryTiming.BeforeReport, s => FirstDaughter(s) != null),
            new("s03_g1", StoryTiming.BeforeReport, s => FirstG1Win(s) != null),
            new("s03_fallen", StoryTiming.BeforeReport, s => s.FallenAdventurers.Count > 0),
            new("s03_bow", StoryTiming.BeforeReport, s => Seen(s, "s01_lumina") && s.ExchangeMatchesPlayed >= 3),
            new("s03_sword", StoryTiming.BeforeReport, s => Seen(s, "s03_bow") && s.ExchangeMatchesPlayed >= 5),
            new("s03_cecilia", StoryTiming.BeforeReport, s => CeciliaBeaten(s) != null),
            new("s03_retire", StoryTiming.AfterReport, s => FirstRetiree(s) != null),
            new("s03_hall", StoryTiming.AfterReport, s => FirstHallOfFame(s) != null),
            new("s03_sharp", StoryTiming.AfterReport, s => s.StorySeenWeeks.TryGetValue("s01_lumina", out int w) && s.WeekNumber - w >= 5 * GameCalendar.WeeksPerYear),
            // ---- 台詞04 前兆・足跡・真相・エンディング（§0.89）。前兆も足跡も森→洞窟→廃墟→峡谷の順に、前の場面を待つ ----
            new("s04_omen1", StoryTiming.BeforeReport, s => Seen(s, "s01_lumina") && BossDefeated(s, "forest", 50)),
            new("s04_omen2", StoryTiming.BeforeReport, s => Seen(s, "s04_omen1") && BossDefeated(s, "cave", 50)),
            new("s04_omen3", StoryTiming.BeforeReport, s => Seen(s, "s04_omen2") && BossDefeated(s, "ruins", 50)),
            new("s04_omen4", StoryTiming.BeforeReport, s => Seen(s, "s04_omen3") && BossDefeated(s, "canyon", 50)),
            new("s04_trail1", StoryTiming.BeforeReport, s => Seen(s, "s04_omen4") && BossDefeated(s, "forest", 100)),
            new("s04_trail2", StoryTiming.BeforeReport, s => Seen(s, "s04_trail1") && BossDefeated(s, "cave", 100)),
            new("s04_trail3", StoryTiming.BeforeReport, s => Seen(s, "s04_trail2") && BossDefeated(s, "ruins", 100)),
            new("s04_trail4", StoryTiming.BeforeReport, s => Seen(s, "s04_trail3") && BossDefeated(s, "canyon", 100)),
            // 真相は峡谷の制覇（足跡4）か深淵の90Fの早いほう。深淵が先なら「名を呼ぶ声」→真相の順（足跡4はあとで{真相のあと}の台詞で出る）
            new("s04_namecall", StoryTiming.BeforeReport, s => Seen(s, "s01_lumina") && BossDefeated(s, "abyss", 90)),
            new("s04_truth", StoryTiming.BeforeReport, s => Seen(s, "s04_trail4") || Seen(s, "s04_namecall")),
            new("s04_home", StoryTiming.AfterReport, s => SeenBefore(s, "s04_truth", s.WeekNumber)),
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
            "s02_disciplines", "s02_advice", "s02_tournaments",
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
        /// </summary>
        public static List<StoryShowing> DueScenes(GameState state, StoryTiming timing)
        {
            var due = new List<StoryShowing>();
            var assumed = new List<string>();
            try
            {
                foreach (var rule in Rules.Where(r => r.Timing == timing))
                {
                    if ((!rule.Repeats && Seen(state, rule.Id)) || !rule.IsDue(state))
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
        /// 小窓の1ページの舞台（§0.91）。背景はそのページまでの最後の〔背景：…〕（場面をまたいでは引き継がない）。
        /// 立ち絵はそのページで話す人のうち立ち絵がある人を、出てきた順に最大2人（表情はそのページで最初の台詞のもの）。
        /// 左右は場面の中で立ち絵のある人が初めて話した順に左・右・左…と決め、同じページで重なったら2人目を反対側へ寄せる。
        /// </summary>
        public static StoryStage StageOf(StoryShowing showing, int pageIndex)
        {
            string background = showing.Pages.Take(pageIndex + 1).SelectMany(p => p)
                .LastOrDefault(l => l.Kind == StoryLineKind.Background)?.Text ?? "";

            var sides = new Dictionary<string, bool>(); // 話者 → 左なら true
            foreach (var line in showing.Pages.SelectMany(p => p))
                if (line.Kind == StoryLineKind.Speech && !sides.ContainsKey(line.Speaker) && StoryImageBalance.PortraitFile(line.Speaker) != null)
                    sides[line.Speaker] = sides.Count % 2 == 0;

            var onPage = showing.Pages[pageIndex]
                .Where(l => l.Kind == StoryLineKind.Speech && sides.ContainsKey(l.Speaker))
                .GroupBy(l => l.Speaker).Select(g => g.First()).Take(2)
                .Select(l => (Left: sides[l.Speaker], Portrait: new StoryPortrait(l.Speaker, StoryImageBalance.PortraitFile(l.Speaker, l.Expression)!)))
                .ToList();
            StoryPortrait? left = null, right = null;
            for (int i = 0; i < onPage.Count; i++)
            {
                bool toLeft = i == 0 ? onPage[0].Left : !onPage[0].Left;
                if (toLeft) left = onPage[i].Portrait; else right = onPage[i].Portrait;
            }
            return new StoryStage(background, background.Length > 0 ? StoryImageBalance.BackgroundFile(background) : null, left, right);
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

        /// <summary>週の決算のあと：場面のきっかけを数える（扉前の撤退・派遣の教官が帰った）。WeekProcessingSystem が呼ぶ。</summary>
        public static void ProcessWeek(GameState state, WeeklySettlementResult result)
        {
            foreach (var r in result.DungeonMissionResolutions.Where(r => r.ArrivedAtBossDoor && r.DoorRetreatReason != null && r.Boss != null))
                state.StoryCounters[RetreatKey(r.Boss!.Id)] = state.StoryCounters.GetValueOrDefault(RetreatKey(r.Boss.Id)) + 1;
            if (result.GuestTrainer.Left != null)
                state.StoryCounters[GuestLeftKey] = state.StoryCounters.GetValueOrDefault(GuestLeftKey) + 1;
        }

        // ==================== 条件 ====================

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

        /// <summary>最初の引退者（引退した週の順）。</summary>
        public static Adventurer? FirstRetiree(GameState state) =>
            state.GuildRetirees.OrderBy(a => a.RetiredAtWeek ?? int.MaxValue).FirstOrDefault();

        /// <summary>最初に殿堂入りした者。</summary>
        public static Adventurer? FirstHallOfFame(GameState state) =>
            state.GuildRetirees.Where(a => a.HallOfFameYear != null).OrderBy(a => a.RetiredAtWeek ?? int.MaxValue).FirstOrDefault();

        /// <summary>魂魄融和で最初に生まれた娘（加わった年の順、同じ年なら名簿の順）。</summary>
        public static Adventurer? FirstDaughter(GameState state) =>
            Everyone(state).Where(a => a.ParentIds.Count >= 2).OrderBy(a => a.JoinedYear).FirstOrDefault();

        /// <summary>最初のG1の優勝（交流戦は除く）。</summary>
        private static TournamentEvent? FirstG1Win(GameState state) =>
            state.TournamentEvents.Where(e => e.Kind != TournamentKind.Exchange && e.Grade == TournamentGrade.G1 && e.Result?.WinnerIsOurs == true)
                .OrderBy(e => e.Year).ThenBy(e => e.Month).ThenBy(e => e.Week).FirstOrDefault();

        /// <summary>交流戦の魔（セシリア）に初めて勝った者の名前。無ければ null。</summary>
        private static string? CeciliaBeaten(GameState state) =>
            state.TournamentEvents.Where(e => e.Kind == TournamentKind.Exchange && e.Result != null)
                .OrderBy(e => e.Year).ThenBy(e => e.Month)
                .Select(e => e.Result!.Matches.FirstOrDefault(m => m.Round == 2 && m.AWon && m.NameB == IsabellaBalance.Opponent(TournamentDiscipline.Magic)))
                .FirstOrDefault(m => m != null)?.NameA;

        /// <summary>扉前で2回以上撤退した、まだ倒していないボス（いちばん浅いもの）。無ければ null。</summary>
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
                var speeches = pages.SelectMany(p => p).Where(l => l.Kind == StoryLineKind.Speech).ToList();
                if (speeches.Count > 0)
                {
                    var pick = speeches[state.StoryCounters.GetValueOrDefault(SeenCountKey(sceneId)) % speeches.Count];
                    pages = new List<List<StoryLine>> { pages.SelectMany(p => p).Where(l => l.Kind != StoryLineKind.Speech || ReferenceEquals(l, pick)).ToList() };
                }
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
                _ => null,
            };
            if (name != null) values["{名前}"] = name;
            if (FirstG1Win(state) is { } g1) values["{大会}"] = g1.Name;
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
            if (FirstHallOfFame(state) is { } hall) values["{殿堂の名前}"] = hall.Name;
            if (FirstRetiree(state) is { } retiree)
            {
                values["{初めての引退者の名前}"] = retiree.Name;
                if (retiree.HallOfFameYear != null) values["{殿堂入りなら}"] = "";
            }
            values[Seen(state, "s04_truth") ? "{真相のあと}" : "{真相の前}"] = "";
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
