using System.Linq;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 2026年10月・§0.70のテスト：月を1ターンにする（次の月へ・月の途中で止まる条件・月報・訓練の月の約束）。
    /// 実行方法: `dotnet test GuildManager.Core.Tests --filter FullyQualifiedName~MonthlyTurn`
    /// </summary>
    public class MonthlyTurnTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private static DungeonExpeditionSystem Expedition() => new(
            new ScoutingResolver(new AlwaysMinRng()), new DungeonResolver(new AlwaysMinRng()), new SatisfactionSystem(),
            new CompatibilitySystem(new AlwaysMinRng()), new DungeonTraversalResolver(new AlwaysMinRng()), new GatheringResolver(new AlwaysMinRng()));

        private static AutoSkipService Service(DungeonExpeditionSystem expedition)
        {
            var week = new WeekProcessingSystem(new MasterMoodSystem(), new EconomySystem(), new TrainingSystem(), new InjuryRecoverySystem(),
                new RestRecoverySystem(), new GrowthSystem(new AlwaysMinRng()), new SatisfactionSystem(), new AgingSystem(new AlwaysMinRng()),
                new FacilitySystem(), new DefeatSystem(), new RecruitmentSystem(new AlwaysMinRng()), expedition);
            return new AutoSkipService(week, new SquadOrderSystem(expedition));
        }

        private static Adventurer Make(string name, int stat)
        {
            var a = new Adventurer
            {
                Name = name, JobClass = JobClass.Warrior,
                STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat,
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        /// <summary>森（10Fボス1体）と、採取を続ける2人部隊。2年目の夏から始める（新春の採用試験や依頼の到着と重ならないように）。</summary>
        private static (GameState State, SavedParty Saved) Setup(SquadOrder order = SquadOrder.Gather)
        {
            var boss = new FloorBoss { Name = "森の主", Floor = 10, MaxHp = 100, CurrentHp = 100 };
            var forest = new DungeonField { Id = "forest", Name = "森", Order = 1, IsUnlocked = true, ReachedFloor = 10, Bosses = { boss } };
            var a = Make("アリス", 80);
            var b = Make("セリア", 80);
            var saved = new SavedParty { Name = "第一部隊", MemberIds = { a.Id, b.Id }, Order = order, OrderFieldId = "forest" };
            var state = new GameState
            {
                Gold = 10000, MasterMood = 60, WeekNumber = GameCalendar.WeeksPerYear + 13 + 1, // 2年目 夏 1の月 第2週
                Adventurers = { a, b }, DungeonFields = { forest }, SavedParties = { saved },
            };
            return (state, saved);
        }

        [Fact]
        public void AdvanceMonth_RunsToTheEndOfTheCurrentMonth()
        {
            var (state, _) = Setup();
            var service = Service(Expedition());
            Assert.Equal(2, GameCalendar.WeekOfMonth(state.WeekNumber));

            var weeks = service.AdvanceMonth(state);

            Assert.Equal(3, weeks.Count); // 第2〜4週
            Assert.True(GameCalendar.IsFirstWeekOfMonth(state.WeekNumber));
            Assert.All(weeks, w => Assert.NotEmpty(w.Orders)); // 毎週、方針どおりに動いている

            var next = service.AdvanceMonth(state);
            Assert.Equal(GameCalendar.WeeksPerMonth, next.Count); // まるごと1か月
        }

        [Fact]
        public void ShouldStopMonth_StopsOnlyForRealDecisions()
        {
            Assert.True(new WeekResult { DeathOrPermanentInjuryOccurred = true }.ShouldStopMonth);
            Assert.True(new WeekResult { FieldUnlocked = true }.ShouldStopMonth);
            Assert.True(new WeekResult { SoulFusionBirthOccurred = true }.ShouldStopMonth);
            Assert.True(new WeekResult { RecruitmentTrialOccurred = true }.ShouldStopMonth);
            Assert.True(new WeekResult { CommissionDeadlineNear = true }.ShouldStopMonth);
            Assert.True(new WeekResult { GameCleared = true }.ShouldStopMonth);
            Assert.True(new WeekResult { DefeatOccurred = true }.ShouldStopMonth);
            // 重傷・ボス撃破・施設の完成では止めない（月報で見せる）
            Assert.False(new WeekResult { SevereInjuryOccurred = true }.ShouldStopMonth);
            Assert.False(new WeekResult { BossDefeated = true }.ShouldStopMonth);
            Assert.False(new WeekResult { FacilityConstructionCompleted = true }.ShouldStopMonth);
            Assert.False(new WeekResult { AnomalyAnnounced = true }.ShouldStopMonth); // 異変は次の月から。月報で知らせる
        }

        [Fact]
        public void MonthlyReport_SumsGrowthGoldAndMood_AndNotesIdleParties()
        {
            var (state, saved) = Setup();
            var idle = Make("ノエル", 40);
            state.Adventurers.Add(idle);
            state.SavedParties.Add(new SavedParty { Name = "第二部隊", MemberIds = { idle.Id } }); // 方針なし
            int gold = state.Gold;

            var weeks = Service(Expedition()).AdvanceMonth(state);
            var report = MonthlyReport.Build(state, weeks, gold);

            Assert.True(report.MonthCompleted);
            Assert.Equal(gold, report.GoldBefore);
            Assert.Equal(state.Gold, report.GoldAfter);
            Assert.Equal(state.MasterMood, report.MoodAfter);
            Assert.Contains(report.Notes, n => n.Text.Contains("第二部隊") && n.Text.Contains("方針が無い") && n.Target == MonthlyNoteTarget.Dungeon);
            Assert.Equal(weeks[0].Settlement.Flags.Week, report.FirstWeek);
            Assert.Empty(report.StopReasons);
        }

        [Fact]
        public void MonthlyReport_NotesOpenTrainingSlots_AtTheStartOfAMonth()
        {
            var (state, _) = Setup();
            state.Facilities.Single(f => f.Type == FacilityType.Church).CurrentLevel = 1;
            state.Adventurers.Add(Make("ノエル", 40)); // 出撃していない冒険者

            var weeks = Service(Expedition()).AdvanceMonth(state); // 月の終わりまで → 次は月のはじめ
            var report = MonthlyReport.Build(state, weeks, state.Gold);

            Assert.Contains(report.Notes, n => n.Target == MonthlyNoteTarget.Facility && n.Text.Contains("訓練施設に空き"));
        }

        [Fact]
        public void StopReasons_ExplainMidMonthStops()
        {
            var reasons = MonthlyReport.StopReasonsOf(new WeekResult { FieldUnlocked = true, CommissionDeadlineNear = true });
            Assert.Equal(new[] { "新しいフィールドが開いた", "依頼の期限が近い" }, reasons);
        }

        [Fact]
        public void Record_KeepsTheLatestTwelveMonths_AndSurvivesSave()
        {
            var state = new GameState();
            for (int i = 1; i <= 15; i++)
                MonthlyReport.Record(state, new MonthlyReport
                {
                    FirstWeek = i * 4 - 3, LastWeek = i * 4, MonthCompleted = true,
                    Growth = { new MonthlyGrowth { Name = "リナ", Gains = { ["AGI"] = i } } },
                    Notes = { new MonthlyNote { Text = "注意", Target = MonthlyNoteTarget.Facility } },
                });

            Assert.Equal(MonthlyReport.KeptReports, state.MonthlyReports.Count);
            Assert.Equal(4 * 4, state.MonthlyReports[0].LastWeek); // 1〜3か月目は捨てた

            var json = System.Text.Json.JsonSerializer.Serialize(state.ToSaveData());
            var restored = GameState.FromSaveData(System.Text.Json.JsonSerializer.Deserialize<SaveData>(json)!);
            Assert.Equal(12, restored.MonthlyReports.Count);
            Assert.Equal(15, restored.MonthlyReports[^1].Growth.Single().Gains["AGI"]);
            Assert.Equal(MonthlyNoteTarget.Facility, restored.MonthlyReports[^1].Notes.Single().Target);
        }

        [Fact]
        public void Training_CanBeChangedOnlyAtTheStartOfAMonth()
        {
            var (state, _) = Setup();
            state.Facilities.Single(f => f.Type == FacilityType.WarriorHall).CurrentLevel = 1;
            var a = state.Adventurers[0];
            var training = new TrainingSystem();

            Assert.False(TrainingSystem.CanChangeAssignments(state)); // 月の第2週
            Assert.False(training.TryAssignForMonth(state, a.Id, FacilityType.WarriorHall));

            state.WeekNumber = GameCalendar.WeeksPerYear + 13 + 4; // 次の月の第1週
            Assert.True(TrainingSystem.CanChangeAssignments(state));
            Assert.True(training.TryAssignForMonth(state, a.Id, FacilityType.WarriorHall));

            state.WeekNumber++; // 月の途中：外せない
            Assert.False(training.TryUnassignForMonth(state, a.Id));
            Assert.True(TrainingSystem.IsTraining(state, a.Id));
        }

        [Fact]
        public void Trainees_StayHome_AndTheRestOfThePartySortsOut()
        {
            var (state, saved) = Setup();
            var a = state.Adventurers[0];
            var b = state.Adventurers[1];
            state.TrainingAssignments[a.Id] = FacilityType.WarriorHall;

            var party = PartyFormationSystem.BuildDispatchParty(state, saved.MemberIds);
            Assert.DoesNotContain(a, party.Members);
            Assert.Contains(b, party.Members);
            a.CurrentHP = 1; // 訓練中の隊員のHPは、部隊の待機の判定に入れない
            Assert.Null(SquadOrderSystem.GetWaitReason(state, saved));

            state.TrainingAssignments[b.Id] = FacilityType.WarriorHall;
            Assert.Contains("全員が訓練中", SquadOrderSystem.GetWaitReason(state, saved));
        }
    }
}
