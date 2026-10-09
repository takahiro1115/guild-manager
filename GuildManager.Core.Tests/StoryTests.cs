using System;
using System.Linq;
using System.Text.Json;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>物語の場面（§0.86）：台詞ファイルの読み込みと、場面を出す条件のテスト。</summary>
    public class StoryTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        private static GameState NewGame()
        {
            var state = new GameState { WeekNumber = 1, Gold = 1000, MasterMood = 50, DungeonFields = SampleData.CreateDefaultFields() };
            state.Adventurers.AddRange(SampleData.CreateStarterAdventurers());
            return state;
        }

        private static string[] Ids(GameState state, StoryTiming timing) => StorySystem.DueScenes(state, timing).Select(s => s.SceneId).ToArray();

        /// <summary>出した場面を見たことにする（画面と同じ）。</summary>
        private static string[] Show(GameState state, StoryTiming timing)
        {
            var due = StorySystem.DueScenes(state, timing);
            foreach (var s in due) StorySystem.MarkSeen(state, s.SceneId);
            return due.Select(s => s.SceneId).ToArray();
        }

        private static FloorBoss Forest(GameState state, int floor) =>
            state.DungeonFields.First(f => f.Id == "forest").Bosses.First(b => b.Floor == floor);

        // ---------------- 台詞ファイル ----------------

        [Fact]
        public void Parse_LinesPagesAndVariants()
        {
            var scenes = StoryBalance.Parse(new[]
            {
                "; 注記",
                "## s_test　見出し",
                "〔背景：ギルドの広間〕",
                "〔扉が開いた〕",
                "アルベール（笑顔）「ふふ」",
                "▼",
                "？？？「――今の悲鳴は、ここね」",
                "【画面へ】部隊・冒険者",
                "【手引き】□ 部隊を組む",
                "【手引き（説明）】説明の行",
                "## s_var",
                "@variants",
                "A「一」",
                "B「二」",
            });

            Assert.Equal(2, scenes.Count);
            var s = scenes[0];
            Assert.Equal(("s_test", "見出し"), (s.Id, s.Title));
            Assert.Equal(2, s.Pages.Count);
            Assert.Equal(new StoryLine(StoryLineKind.Background, "ギルドの広間"), s.Pages[0][0]);
            Assert.Equal(new StoryLine(StoryLineKind.Narration, "扉が開いた"), s.Pages[0][1]);
            Assert.Equal(new StoryLine(StoryLineKind.Speech, "ふふ", "アルベール", "笑顔"), s.Pages[0][2]);
            Assert.Equal("？？？", s.Pages[1][0].Speaker);
            Assert.Equal(new StoryLine(StoryLineKind.Jump, "部隊・冒険者"), s.Pages[1][1]);
            Assert.Equal(new StoryLine(StoryLineKind.Guide, "部隊を組む"), s.Pages[1][2]);
            Assert.Equal(StoryLineKind.GuideNote, s.Pages[1][3].Kind);
            Assert.True(scenes[1].Variants);
            Assert.Throws<BalanceDataException>(() => StoryBalance.Parse(new[] { "## x", "読めない行" }));
            Assert.Throws<BalanceDataException>(() => StoryBalance.Parse(new[] { "台詞「場面の前」" }));
        }

        [Fact]
        public void StoryFiles_LoadAndHaveEveryScene_WithoutLeftoverMarks()
        {
            Assert.All(StorySystem.SceneIds, id => Assert.True(StoryBalance.Scenes.ContainsKey(id), id));
            Assert.Equal(StorySystem.SceneIds.Append(StorySystem.NarrationSceneId).OrderBy(x => x), StoryBalance.Scenes.Keys.OrderBy(x => x)); // 使わない場面も残さない
            foreach (var scene in StoryBalance.Scenes.Values)
                foreach (var line in scene.Pages.SelectMany(p => p))
                {
                    Assert.DoesNotContain("書き手メモ", line.Text);
                    Assert.DoesNotContain("きっかけ", line.Text);
                }
            Assert.Contains(StoryBalance.Get("s01_prologue").Pages.SelectMany(p => p), l => l.Speaker == "アルベール");
        }

        // ---------------- 序章 ----------------

        [Fact]
        public void Prologue_ThenGuild_Order_Month_FollowPlayerActions()
        {
            var state = NewGame();
            Assert.Equal(new[] { "s01_prologue" }, Show(state, StoryTiming.Interactive));
            Assert.Empty(Show(state, StoryTiming.Interactive)); // まだ2人を迎えていない

            state.Adventurers.AddRange(SampleData.CreateStarterAdventurers().Take(2));
            Assert.Equal(new[] { "s01_guild" }, Show(state, StoryTiming.Interactive));

            var party = new SavedParty { Name = "第1部隊" };
            state.SavedParties.Add(party);
            Assert.Empty(Show(state, StoryTiming.Interactive));
            party.MemberIds.Add(state.Adventurers[0].Id);
            Assert.Equal(new[] { "s01_order" }, Show(state, StoryTiming.Interactive));
            party.Order = SquadOrder.Dive;
            Assert.Equal(new[] { "s01_month" }, Show(state, StoryTiming.Interactive));
            Assert.Empty(Show(state, StoryTiming.Interactive));
        }

        [Fact]
        public void FirstReport_AfterTheReport()
        {
            var state = NewGame();
            Assert.DoesNotContain("s01_first_report", Ids(state, StoryTiming.AfterReport));
            state.MonthlyReports.Add(new MonthlyReport());
            Assert.Contains("s01_first_report", Ids(state, StoryTiming.AfterReport));
            Assert.DoesNotContain("s01_first_report", Ids(state, StoryTiming.BeforeReport));
        }

        // ---------------- 森 ----------------

        [Fact]
        public void Forest10_Lumina_ThenElderAndQuestionNextMonth()
        {
            var state = NewGame();
            state.WeekNumber = 9;
            Forest(state, 10).IsDefeated = true;
            Forest(state, 20).IsDefeated = true;
            Assert.Equal(new[] { "s01_forest10", "s01_lumina" }, Show(state, StoryTiming.BeforeReport));
            Assert.DoesNotContain("s01_elder", Ids(state, StoryTiming.AfterReport)); // 同じ月はまだ

            state.WeekNumber = 13;
            var after = Show(state, StoryTiming.AfterReport);
            Assert.Equal(new[] { "s01_elder", "s01_question" }, after.Where(id => id.StartsWith("s01_e") || id.StartsWith("s01_q")).ToArray());
        }

        [Fact]
        public void Hunter_AfterTwoRetreatsAtSameBoss_FillsFloor()
        {
            var state = NewGame();
            var boss = Forest(state, 40);
            var field = state.DungeonFields.First(f => f.Id == "forest");
            for (int i = 0; i < 2; i++)
            {
                var result = new WeeklySettlementResult();
                result.DungeonMissionResolutions.Add(new DungeonMissionResolution(new Party(), boss, field, DungeonMissionType.Scouting)
                    { ArrivedAtBossDoor = true, DoorRetreatReason = "勝ち目が薄い" });
                Assert.DoesNotContain("s01_hunter", Ids(state, StoryTiming.BeforeReport));
                StorySystem.ProcessWeek(state, result);
            }
            var hunter = Assert.Single(StorySystem.DueScenes(state, StoryTiming.BeforeReport), s => s.SceneId == "s01_hunter");
            var texts = hunter.Pages.SelectMany(p => p).Select(l => l.Text).ToList();
            Assert.Contains(texts, t => t.Contains("また40階の主"));
            Assert.DoesNotContain(texts, t => t.Contains("{"));

            boss.IsDefeated = true; // 倒したら出さない
            Assert.DoesNotContain("s01_hunter", Ids(state, StoryTiming.BeforeReport));
        }

        // ---------------- イザベラ ----------------

        [Fact]
        public void Isabella_Visit_Disciplines_Lost_Advice_Tournaments()
        {
            var state = NewGame();
            Forest(state, 40).IsDefeated = true;
            IsabellaSystem.CheckVisit(state);
            state.WeekNumber = 5; // 交流戦の月のはじめ
            IsabellaSystem.AutoFillFirstExchange(state);
            Assert.Contains("s02_visit", Show(state, StoryTiming.BeforeReport));
            Assert.Contains("s02_disciplines", Show(state, StoryTiming.AfterReport));

            state.WeekNumber = 8;
            new IsabellaSystem(new AlwaysMaxRng()).ResolveWeek(state); // 負ける
            state.WeekNumber = 9;
            var shown = Show(state, StoryTiming.BeforeReport);
            Assert.Equal(new[] { "s02_lost", "s02_advice", "s02_tournaments" }, shown.Where(id => id.StartsWith("s02")).ToArray());
        }

        [Fact]
        public void Advice_FillsOpenedTrainingFacility()
        {
            var state = NewGame();
            state.FacilityUnlockedLevels[FacilityType.Academy] = 1;
            state.StorySeenWeeks["s02_lost"] = 1;
            var advice = Assert.Single(StorySystem.DueScenes(state, StoryTiming.BeforeReport), s => s.SceneId == "s02_advice");
            var texts = advice.Pages.SelectMany(p => p).Select(l => l.Text).ToList();
            Assert.Contains(texts, t => t.Contains("魔の子に見込み") && t.Contains("学問所から建てなさいな"));
            Assert.DoesNotContain(texts, t => t.Contains("{"));
        }

        [Fact]
        public void Won_ThenMarguerite_ThenLeaves()
        {
            var state = NewGame();
            Forest(state, 40).IsDefeated = true;
            IsabellaSystem.CheckVisit(state);
            state.WeekNumber = 5;
            IsabellaSystem.AutoFillFirstExchange(state);
            state.WeekNumber = 8;
            new IsabellaSystem(new AlwaysMinRng()).ResolveWeek(state); // 勝つ
            state.WeekNumber = 9;
            var shown = Show(state, StoryTiming.BeforeReport).Where(id => id.StartsWith("s02")).ToArray();
            Assert.Equal(new[] { "s02_visit", "s02_won", "s02_advice", "s02_marguerite", "s02_tournaments" }, shown);

            var result = new WeeklySettlementResult { GuestTrainer = new GuestTrainerChange { Left = new Adventurer { Name = "マルグリット" } } };
            StorySystem.ProcessWeek(state, result);
            Assert.Contains("s02_marguerite_leaves", Ids(state, StoryTiming.BeforeReport));
        }

        [Fact]
        public void RematchLines_RotateVariants_OncePerExchange()
        {
            var state = NewGame();
            state.TournamentCalendarFromWeek = 1;
            state.IsabellaVisitWeek = 1;
            state.ExchangeMatchesPlayed = 2;
            state.TournamentEvents.Add(new TournamentEvent { Kind = TournamentKind.Exchange, Year = 1, Month = 2, Result = new TournamentResult { WinnerIsOurs = false } });
            state.TournamentEvents.Add(new TournamentEvent { Kind = TournamentKind.Exchange, Year = 1, Month = 4, Result = new TournamentResult { WinnerIsOurs = true } });
            StorySystem.MarkTutorialSeen(state);
            state.StoryCounters.Remove("rematchCommented"); // 2回目の一言はまだ

            var first = Assert.Single(StorySystem.DueScenes(state, StoryTiming.BeforeReport));
            Assert.Equal("s02_rematch_won", first.SceneId);
            var line1 = Assert.Single(first.Pages.SelectMany(p => p), l => l.Kind == StoryLineKind.Speech);
            StorySystem.MarkSeen(state, first.SceneId);
            Assert.Empty(StorySystem.DueScenes(state, StoryTiming.BeforeReport)); // 同じ交流戦では一度だけ

            state.ExchangeMatchesPlayed = 3;
            state.TournamentEvents.Add(new TournamentEvent { Kind = TournamentKind.Exchange, Year = 1, Month = 7, Result = new TournamentResult { WinnerIsOurs = true } });
            var second = Assert.Single(StorySystem.DueScenes(state, StoryTiming.BeforeReport));
            Assert.NotEqual(line1.Text, second.Pages.SelectMany(p => p).Single(l => l.Kind == StoryLineKind.Speech).Text); // 次の一言
        }

        [Fact]
        public void Invite_AtSeasonStart_WhenTwoSeasonsSinceLastExchange()
        {
            var state = NewGame();
            state.TournamentCalendarFromWeek = 1;
            state.IsabellaVisitWeek = 1;
            state.ExchangeMatchesPlayed = 1;
            state.TournamentEvents.Add(new TournamentEvent { Kind = TournamentKind.Exchange, Year = 1, Month = 2, Result = new TournamentResult() }); // 春
            StorySystem.MarkTutorialSeen(state);

            state.WeekNumber = 13; // 夏のはじめ：1季節しか空いていない
            Assert.DoesNotContain("s02_invite", Ids(state, StoryTiming.AfterReport));
            state.WeekNumber = 25; // 秋のはじめ
            Assert.Contains("s02_invite", Show(state, StoryTiming.AfterReport));
            Assert.DoesNotContain("s02_invite", Ids(state, StoryTiming.AfterReport)); // この季節はもう言った
            state.WeekNumber = 26; // 季節のはじめでない
            Assert.DoesNotContain("s02_invite", Ids(state, StoryTiming.AfterReport));
        }

        [Fact]
        public void Royal_AfterFirstPlacing()
        {
            var state = NewGame();
            Assert.DoesNotContain("s02_royal", Ids(state, StoryTiming.BeforeReport));
            state.TournamentPlacingsTotal = 1;
            IsabellaSystem.CheckCommissionUnlock(state);
            Assert.Contains("s02_royal", Ids(state, StoryTiming.BeforeReport));
        }

        // ---------------- 台詞03・04（§0.89） ----------------

        private static void Defeat(GameState state, string fieldId, int floor) =>
            state.DungeonFields.First(f => f.Id == fieldId).Bosses.First(b => b.Floor == floor).IsDefeated = true;

        private static string[] Texts(StoryShowing s) => s.Pages.SelectMany(p => p).Select(l => l.Text).ToArray();

        [Fact]
        public void Omens_WaitForThePreviousOne()
        {
            var state = NewGame();
            StorySystem.MarkSeen(state, "s01_lumina");
            Defeat(state, "cave", 50);
            Assert.DoesNotContain("s04_omen2", Ids(state, StoryTiming.BeforeReport)); // 森の前兆がまだ
            Defeat(state, "forest", 50);
            Assert.Equal(new[] { "s04_omen1", "s04_omen2" }, Show(state, StoryTiming.BeforeReport).Where(id => id.StartsWith("s04")).ToArray());
        }

        [Fact]
        public void Truth_ByCanyon_LetterThenTruth_LaterNameCall()
        {
            var state = NewGame();
            foreach (var id in new[] { "s01_lumina", "s04_omen1", "s04_omen2", "s04_omen3", "s04_omen4", "s04_trail1", "s04_trail2", "s04_trail3" })
                StorySystem.MarkSeen(state, id);
            Defeat(state, "canyon", 100);
            var due = StorySystem.DueScenes(state, StoryTiming.BeforeReport).Where(s => s.SceneId.StartsWith("s04")).ToList();
            Assert.Equal(new[] { "s04_trail4", "s04_truth" }, due.Select(s => s.SceneId));
            Assert.Contains(Texts(due[0]), t => t == "マスター。これ、わたしの名前なんですね"); // 真相の前の台詞
            Assert.DoesNotContain(Texts(due[0]), t => t.Contains("お母さんたちの手紙"));
            foreach (var s in due) StorySystem.MarkSeen(state, s.SceneId);

            Defeat(state, "abyss", 90);
            Assert.Equal(new[] { "s04_namecall" }, Show(state, StoryTiming.BeforeReport).Where(id => id.StartsWith("s04")).ToArray());
            state.WeekNumber += GameCalendar.WeeksPerMonth;
            Assert.Contains("s04_home", Ids(state, StoryTiming.AfterReport));
        }

        [Fact]
        public void Truth_ByAbyss90_NameCallThenTruth_LetterLaterWithOtherLine()
        {
            var state = NewGame();
            foreach (var id in new[] { "s01_lumina", "s04_omen1", "s04_omen2", "s04_omen3", "s04_omen4", "s04_trail1", "s04_trail2", "s04_trail3" })
                StorySystem.MarkSeen(state, id);
            Defeat(state, "abyss", 90);
            Assert.Equal(new[] { "s04_namecall", "s04_truth" }, Show(state, StoryTiming.BeforeReport).Where(id => id.StartsWith("s04")).ToArray());
            Defeat(state, "canyon", 100);
            var letter = Assert.Single(StorySystem.DueScenes(state, StoryTiming.BeforeReport), s => s.SceneId == "s04_trail4");
            Assert.Contains(Texts(letter), t => t == "……本当に、わたしのお母さんたちの手紙なんですね");
            Assert.DoesNotContain(Texts(letter), t => t.Contains("わたしの名前なんですね"));
        }

        [Fact]
        public void Academy_AllThreeAtAbyss50_WhenFewExchangeWins()
        {
            var state = NewGame();
            state.IsabellaVisitWeek = 1;
            state.ExchangeWins = 2;
            Assert.Equal(new[] { "s03_academy2" }, Show(state, StoryTiming.BeforeReport).Where(id => id.StartsWith("s03_academy")).ToArray());
            Defeat(state, "abyss", 50);
            Assert.Equal(new[] { "s03_academy4", "s03_academy6" }, Show(state, StoryTiming.BeforeReport).Where(id => id.StartsWith("s03_academy")).ToArray());
        }

        [Fact]
        public void FirstRetiree_Name_AndHallOfFameLineOnlyWhenHallOfFame()
        {
            var state = NewGame();
            var a = state.Adventurers[0];
            state.Adventurers.Remove(a);
            a.IsRetired = true;
            a.RetiredAtWeek = 48;
            state.RetiredAdventurers.Add(a);
            var retire = Assert.Single(StorySystem.DueScenes(state, StoryTiming.AfterReport), s => s.SceneId == "s03_retire");
            Assert.Contains(Texts(retire), t => t.Contains($"{a.Name}さん、行っちゃいましたね"));
            Assert.DoesNotContain(Texts(retire), t => t.Contains("殿堂"));

            a.HallOfFameYear = 1;
            retire = StorySystem.DueScenes(state, StoryTiming.AfterReport).Single(s => s.SceneId == "s03_retire");
            Assert.Contains(Texts(retire), t => t == "……それに、あの子の名前は殿堂に残るわ");
            Assert.Contains("s03_hall", StorySystem.DueScenes(state, StoryTiming.AfterReport).Select(s => s.SceneId));
        }

        [Fact]
        public void Daughter_FillsMothersAndName()
        {
            var state = NewGame();
            var (ma, mb) = (state.Adventurers[0], state.Adventurers[1]);
            var girl = new Adventurer { Name = "ミラ", JoinedYear = 2, ParentIds = { ma.Id, mb.Id } };
            state.Adventurers.Add(girl);
            var scene = Assert.Single(StorySystem.DueScenes(state, StoryTiming.BeforeReport), s => s.SceneId == "s03_daughter");
            Assert.Contains(Texts(scene), t => t.Contains($"{ma.Name}さんと{mb.Name}さんの娘"));
            Assert.Contains(Texts(scene), t => t.Contains("ミラの顔を見に"));
        }

        [Fact]
        public void Ending_ScenesAfterClear_HeartDropsLinesWithoutRecords_Narration()
        {
            var state = NewGame();
            Assert.Empty(StorySystem.DueScenes(state, StoryTiming.Ending));
            state.IsGameCleared = true;
            var due = StorySystem.DueScenes(state, StoryTiming.Ending);
            Assert.Equal(new[] { "s04_heart", "s04_banquet", "s04_sortie" }, due.Select(s => s.SceneId));
            Assert.DoesNotContain(Texts(due[0]), t => t.Contains("{") || t.Contains("生まれた日のこと")); // 娘も殿堂もいない
            Assert.Contains(Texts(due[0]), t => t == "ここです");

            foreach (var s in due) StorySystem.MarkSeen(state, s.SceneId);
            Assert.Empty(StorySystem.DueScenes(state, StoryTiming.Ending));
            Assert.Equal(3, StorySystem.EndingScenes(state).Count); // 見直すときは見た場面も出す

            var narration = StorySystem.Narration(state);
            Assert.Equal("失われた理想郷", narration[0].Text);
            Assert.Contains(narration, l => l.Kind == StoryLineKind.Speech && l.Speaker == "アルベール");
        }

        // ---------------- セーブ ----------------

        [Fact]
        public void Save_RoundTripsSeenAndCounters_OldSaveSkipsTutorial()
        {
            var state = NewGame();
            StorySystem.MarkSeen(state, "s01_prologue");
            state.StoryCounters["retreat:x"] = 2;
            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(state.ToSaveData()))!);
            Assert.Equal(1, restored.StorySeenWeeks["s01_prologue"]);
            Assert.Equal(2, restored.StoryCounters["retreat:x"]);
            Assert.False(StorySystem.Seen(restored, "s01_guild"));

            // §0.86より前のセーブ（キーが無い）：チュートリアルは見たことにする
            var data = NewGame().ToSaveData();
            data.StorySeenWeeks = null;
            data.StoryCounters = null;
            var old = GameState.FromSaveData(data);
            Assert.True(StorySystem.Seen(old, "s01_prologue"));
            Assert.True(StorySystem.Seen(old, "s02_royal"));
            Assert.Empty(StorySystem.DueScenes(old, StoryTiming.Interactive));
            Assert.Empty(StorySystem.DueScenes(old, StoryTiming.BeforeReport));
        }
    }
}
