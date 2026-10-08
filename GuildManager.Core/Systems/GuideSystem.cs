using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>ギルドの手引きのやること1つ（台詞の【手引き】の行）。</summary>
    public sealed class GuideItem
    {
        public string Text { get; init; } = "";
        public bool Done { get; init; }
    }

    /// <summary>ギルドの手引きの1まとまり（場面1つ分：やること・説明・画面へのボタン）。</summary>
    public sealed class GuideGroup
    {
        public string SceneId { get; init; } = "";
        public List<GuideItem> Items { get; init; } = new();
        /// <summary>【手引き（説明）】の行。</summary>
        public List<string> Notes { get; init; } = new();
        /// <summary>【画面へ】の文言（無ければ null）。</summary>
        public string? Jump { get; init; }
        public bool AllDone => Items.All(i => i.Done);
    }

    /// <summary>物語で教えるまで隠す画面（§0.87）。</summary>
    public enum GuideTab
    {
        Warehouse,
        Shop,
        Research,
        Tournament,
        Facility,
    }

    /// <summary>
    /// ギルドの手引き（2026年10月・§0.87、チュートリアル③-2）：見た場面の【手引き】の行を「やること」として並べ、条件を満たすと自動でチェックを付ける。
    /// 場面の全部が済んだら、その月のうちはチェック済みで残し、次の月から消す。まだ教えていない画面（タブ）は、教える場面を見るまで隠す。
    /// やることの条件は、場面のIdと【手引き】の行の順番ごとにここに持つ（台詞ファイルの行と数が揃っているかはテストで確かめる）。
    /// </summary>
    public static class GuideSystem
    {
        /// <summary>場面ごとの、【手引き】の行の順に並べた「済んだか」の条件。</summary>
        private static readonly Dictionary<string, Func<GameState, bool>[]> Conditions = new()
        {
            ["s01_prologue"] = new Func<GameState, bool>[] { s => s.Adventurers.Count >= 5 || s.WeekNumber > 1 },
            ["s01_guild"] = new Func<GameState, bool>[] { s => s.SavedParties.Any(p => p.MemberIds.Count > 0) },
            ["s01_order"] = new Func<GameState, bool>[] { s => s.SavedParties.Any(p => p.MemberIds.Count > 0 && p.Order != SquadOrder.None) },
            ["s01_month"] = new Func<GameState, bool>[] { s => MonthPassedSince(s, "s01_month") },
            ["s01_first_report"] = new Func<GameState, bool>[] { s => s.Adventurers.Any(a => a.IdleActivity == IdleActivity.SelfTraining) || MonthPassedSince(s, "s01_first_report") },
            ["s01_forest10"] = new Func<GameState, bool>[]
            {
                s => s.Armory.Concat(AllEquipped(s)).Any(i => i.Rarity != null),
                s => AllEquipped(s).Any(i => i.Rarity != null),
                s => s.DungeonFields.FirstOrDefault(f => f.Id == IsabellaBalance.VisitFieldId)?.Bosses.Any(b => b.Floor == 20 && b.EverDefeated) == true,
            },
            ["s01_question"] = new Func<GameState, bool>[] { s => s.CompletedResearchIds.Count > 0 },
            ["s01_hunter"] = new Func<GameState, bool>[]
            {
                s => StorySystem.HunterBoss(s) is not { } b || b.EverDefeated || b.IntelRate >= 0.25,
                s => StorySystem.HunterBoss(s) is not { } b || b.EverDefeated,
            },
            ["s02_visit"] = new Func<GameState, bool>[]
            {
                s => StorySystem.FirstExchange(s) != null
                    || (IsabellaSystem.PendingExchange(s) is { } ev && s.TournamentEntries.Count(e => e.EventId == ev.Id) >= IsabellaSystem.ExchangeDisciplines.Length),
            },
            ["s02_advice"] = new Func<GameState, bool>[]
            {
                s => StorySystem.AdviceFacility(s) is not FacilityType t || s.GetFacilityLevel(t) >= 1,
                s => s.TrainingAssignments.Count > 0,
            },
            ["s02_marguerite"] = new Func<GameState, bool>[]
            {
                s => (IsabellaSystem.GuestTrainer(s) is { } g && s.AssignedTrainers.Values.Contains(g.Id)) || s.StoryCounters.GetValueOrDefault(StorySystem.GuestLeftKey) > 0,
            },
            ["s02_tournaments"] = new Func<GameState, bool>[] { s => s.TournamentEvents.Any(e => e.Kind != TournamentKind.Exchange && e.Result?.Placings.Count > 0) },
            ["s02_royal"] = new Func<GameState, bool>[] { s => s.Commissions.Any(c => c.Accepted) || s.CommissionCompletions.Values.Any(n => n > 0) },
        };

        /// <summary>そのタブを開く場面（この場面を見たら現れる）。</summary>
        private static readonly Dictionary<GuideTab, string> TabScenes = new()
        {
            [GuideTab.Warehouse] = "s01_forest10",
            [GuideTab.Shop] = "s01_forest10",
            [GuideTab.Research] = "s01_question",
            [GuideTab.Tournament] = "s02_visit",
            [GuideTab.Facility] = "s02_advice",
        };

        private static string DoneKey(string sceneId) => $"guideDone:{sceneId}";

        /// <summary>手引きをすべて済んだことにする（§0.86より前のセーブ：チュートリアルを見たことにしたゲームでは手引きを出さない）。</summary>
        public static void MarkAllDone(GameState state)
        {
            foreach (var id in Conditions.Keys)
                state.StoryCounters[DoneKey(id)] = 0;
        }

        /// <summary>条件を持つ場面のId（台詞ファイルの【手引き】の行と数が揃っているかのテストに使う）。</summary>
        public static IReadOnlyDictionary<string, int> ConditionCounts => Conditions.ToDictionary(kv => kv.Key, kv => kv.Value.Length);

        /// <summary>そのタブ（画面）が見えるか：教える場面を見たら現れる。</summary>
        public static bool IsTabOpen(GameState state, GuideTab tab) => !state.TutorialEnabled || StorySystem.Seen(state, TabScenes[tab]); // 手ほどき「なし」（§0.88）は初めから全部

        /// <summary>
        /// 今の手引き（見た場面の順）。全部済んだ場面は、済んだ月のうちだけ残す。
        /// 全部済んだと分かった週を記録するので、画面の再描画・週の決算のたびに呼んでよい。
        /// </summary>
        public static List<GuideGroup> Groups(GameState state)
        {
            var groups = new List<GuideGroup>();
            if (!state.TutorialEnabled)
                return groups; // 手ほどき「なし」（§0.88）は手引きを出さない
            foreach (var id in StorySystem.SceneIds.Where(id => StorySystem.Seen(state, id) && Conditions.ContainsKey(id)))
            {
                var lines = StoryBalance.Get(id).Pages.SelectMany(p => p).ToList();
                var guides = lines.Where(l => l.Kind == StoryLineKind.Guide).ToList();
                var conditions = Conditions[id];
                var group = new GuideGroup
                {
                    SceneId = id,
                    Items = guides.Select((l, i) => new GuideItem { Text = StorySystem.Fill(state, l.Text), Done = i < conditions.Length && conditions[i](state) }).ToList(),
                    Notes = lines.Where(l => l.Kind == StoryLineKind.GuideNote).Select(l => StorySystem.Fill(state, l.Text)).ToList(),
                    Jump = lines.FirstOrDefault(l => l.Kind == StoryLineKind.Jump)?.Text,
                };
                if (group.Items.Count == 0) continue;

                if (group.AllDone && !state.StoryCounters.ContainsKey(DoneKey(id)))
                    state.StoryCounters[DoneKey(id)] = state.WeekNumber;
                if (state.StoryCounters.TryGetValue(DoneKey(id), out int doneWeek) && IsabellaSystem.MonthStart(state.WeekNumber) > doneWeek)
                    continue; // 済んだ月が過ぎたら消す（§0.86より前のセーブは初めから済んだことにしてある）
                groups.Add(group);
            }
            return groups;
        }

        /// <summary>まだ済んでいないやることの数（ヘッダーのボタンに出す）。</summary>
        public static int OpenCount(GameState state) => Groups(state).Sum(g => g.Items.Count(i => !i.Done));

        private static IEnumerable<EquipmentItem> AllEquipped(GameState state) =>
            state.Adventurers.SelectMany(a => Adventurer.AllSlots.Select(a.GetEquipped)).OfType<EquipmentItem>();

        private static bool MonthPassedSince(GameState state, string sceneId) =>
            state.StorySeenWeeks.TryGetValue(sceneId, out int seen) && IsabellaSystem.MonthStart(state.WeekNumber) > seen;
    }
}
