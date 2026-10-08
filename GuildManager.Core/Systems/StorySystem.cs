using System;
using System.Collections.Generic;
using System.Linq;
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
        };

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

        /// <summary>場面を出したことを記録する（見た週・回数。交流戦の一言は、どの交流戦まで言ったかも）。</summary>
        public static void MarkSeen(GameState state, string sceneId)
        {
            state.StorySeenWeeks[sceneId] = state.WeekNumber;
            state.StoryCounters[SeenCountKey(sceneId)] = state.StoryCounters.GetValueOrDefault(SeenCountKey(sceneId)) + 1;
            if (sceneId is "s02_rematch_won" or "s02_rematch_lost")
                state.StoryCounters[RematchKey] = state.ExchangeMatchesPlayed;
            // 手引き（§0.87）で、あとから同じ対象を指せるように覚えておく（撃破や次の訓練所の開放で変わるため）
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
        public static string Fill(GameState state, string text) =>
            Placeholders(state).Aggregate(text, (t, kv) => t.Replace(kv.Key, kv.Value));

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

        private static bool ForestBossDefeated(GameState state, int floor) =>
            state.DungeonFields.FirstOrDefault(f => f.Id == IsabellaBalance.VisitFieldId)?.Bosses.Any(b => b.Floor == floor && b.IsDefeated) == true;

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
            var pages = scene.Pages.Select(p => p.Select(l => l with { Text = Fill(state, l.Text) }).ToList()).ToList();

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

        private static Dictionary<string, string> Placeholders(GameState state)
        {
            var values = new Dictionary<string, string>();
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
