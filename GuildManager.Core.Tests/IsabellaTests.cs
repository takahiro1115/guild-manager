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
    /// <summary>イザベラの来訪・交流戦・派遣の教官・大会と依頼の開放（§0.84）のテスト。</summary>
    public class IsabellaTests
    {
        /// <summary>乱数が常に最小＝試合の判定は必ず勝つ（0 &lt; 勝率）。</summary>
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        /// <summary>乱数が常に最大＝試合の判定は勝率がほぼ1でなければ負ける。</summary>
        private class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        private static Adventurer Make(string name, int str, int intel, int dex)
        {
            var a = new Adventurer
            {
                Name = name, JobClass = JobClass.Warrior,
                STR = str, VIT = str, AGI = (str + dex) / 2, INT = intel, MND = intel, LDR = intel, DEX = dex,
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        /// <summary>1年目 春1の月 第1週。剣・魔・技の得意な子が1人ずつと、控えが1人。</summary>
        private static GameState NewGame()
        {
            var state = new GameState { WeekNumber = 1, Gold = 1000, MasterMood = 50, DungeonFields = SampleData.CreateDefaultFields() };
            state.Adventurers.AddRange(new[] { Make("剣の子", 60, 20, 30), Make("魔の子", 20, 60, 25), Make("技の子", 30, 20, 60), Make("控え", 30, 30, 30) });
            return state;
        }

        private static void DefeatForest40(GameState state) =>
            state.DungeonFields.First(f => f.Id == "forest").Bosses.First(b => b.Floor == 40).IsDefeated = true;

        /// <summary>来訪して、最初の交流戦の月のはじめ（2の月 第1週＝通算5週）に進めた状態。</summary>
        private static (GameState State, TournamentEvent Exchange) Visited()
        {
            var state = NewGame();
            DefeatForest40(state);
            Assert.True(IsabellaSystem.CheckVisit(state));
            state.WeekNumber = 5;
            IsabellaSystem.AutoFillFirstExchange(state);
            return (state, IsabellaSystem.PendingExchange(state)!);
        }

        private static void Build(GameState state, FacilityType type, int level = 1)
        {
            var facility = state.Facilities.FirstOrDefault(f => f.Type == type);
            if (facility == null) state.Facilities.Add(new Facility { Type = type, CurrentLevel = level });
            else facility.CurrentLevel = level;
        }

        // ---------------- 来訪の前 ----------------

        [Fact]
        public void BeforeVisit_NoTournaments_NoInvites_NoCommissions()
        {
            var state = NewGame();
            TournamentSystem.EnsureSchedule(state);
            Assert.Empty(state.TournamentEvents);
            Assert.Empty(TournamentSystem.CheckInvitations(state, state.DungeonFields.Take(1), Array.Empty<TournamentEvent>()));
            Assert.False(IsabellaSystem.CheckVisit(state));
            Assert.False(CommissionSystem.IsOfferWeek(state, 13));
            Assert.Contains("イザベラ", FacilityUnlockSystem.DescribeNext(state, FacilityType.DrillHall));
        }

        // ---------------- 来訪 ----------------

        [Fact]
        public void Visit_WhenForest40Defeated_SchedulesFirstExchangeNextMonth()
        {
            var state = NewGame();
            DefeatForest40(state);
            Assert.True(IsabellaSystem.CheckVisit(state));
            Assert.False(IsabellaSystem.CheckVisit(state)); // 二度は来ない

            Assert.Equal(1, state.IsabellaVisitWeek);
            var ev = Assert.Single(state.TournamentEvents);
            Assert.Equal(TournamentKind.Exchange, ev.Kind);
            Assert.Equal((1, 2, IsabellaBalance.ExchangeWeekOfMonth), (ev.Year, ev.Month, ev.Week));
            Assert.Equal(TournamentSystem.DisciplineStrength(state.Adventurers[0], TournamentDiscipline.Sword), state.ExchangeAnchor[TournamentDiscipline.Sword], 6);
            Assert.False(IsabellaSystem.TournamentsOpen(state));
            TournamentSystem.EnsureSchedule(state);
            Assert.Single(state.TournamentEvents); // 交流戦の前は大会を置かない
        }

        [Fact]
        public void FirstExchange_AutoFillsThreeDistinctEntrants_ByBestRatio()
        {
            var (state, ev) = Visited();
            var entries = state.TournamentEntries.Where(e => e.EventId == ev.Id).ToList();
            Assert.Equal(3, entries.Count);
            Assert.Equal(3, entries.Select(e => e.AdventurerId).Distinct().Count());
            Assert.Equal("剣の子", state.Adventurers.First(a => a.Id == IsabellaSystem.SlotEntry(state, ev, TournamentDiscipline.Sword)!.AdventurerId).Name);
            Assert.Equal("魔の子", state.Adventurers.First(a => a.Id == IsabellaSystem.SlotEntry(state, ev, TournamentDiscipline.Magic)!.AdventurerId).Name);
            Assert.Equal("技の子", state.Adventurers.First(a => a.Id == IsabellaSystem.SlotEntry(state, ev, TournamentDiscipline.Skill)!.AdventurerId).Name);
            Assert.All(entries, e => Assert.True(TournamentSystem.IsEntered(state, e.AdventurerId!.Value))); // 出撃・訓練から外れる
        }

        [Fact]
        public void TryEnter_SwapsSlot_AndMovesAdventurerBetweenSlots()
        {
            var (state, ev) = Visited();
            var reserve = state.Adventurers.Single(a => a.Name == "控え");
            Assert.True(IsabellaSystem.TryEnter(state, ev, reserve, TournamentDiscipline.Sword, TournamentPrep.Push));
            Assert.Equal(reserve.Id, IsabellaSystem.SlotEntry(state, ev, TournamentDiscipline.Sword)!.AdventurerId);
            Assert.True(IsabellaSystem.TryEnter(state, ev, reserve, TournamentDiscipline.Magic, TournamentPrep.Rest));
            Assert.Null(IsabellaSystem.SlotEntry(state, ev, TournamentDiscipline.Sword));
            Assert.Equal(reserve.Id, IsabellaSystem.SlotEntry(state, ev, TournamentDiscipline.Magic)!.AdventurerId);

            state.WeekNumber = 6; // 月の途中は変えられない
            Assert.NotNull(IsabellaSystem.EntryBlockReason(state, ev, reserve));
        }

        // ---------------- 相手の強さ ----------------

        [Fact]
        public void FirstMatch_WinChance_IsAboutAThird()
        {
            var (state, _) = Visited();
            var chances = IsabellaSystem.ExchangeDisciplines
                .Select(d => IsabellaSystem.BoutWinChance(state.ExchangeAnchor[d], IsabellaSystem.OpponentStrength(state, d))).ToList();
            double win = IsabellaSystem.MatchWinChance(chances);
            Assert.InRange(win, 0.30, 0.40);
        }

        [Fact]
        public void Opponent_GrowsPerMatchAndPerYear()
        {
            var (state, _) = Visited();
            state.WeekNumber = state.IsabellaVisitWeek!.Value;
            double baseline = IsabellaSystem.OpponentStrength(state, TournamentDiscipline.Magic);
            Assert.Equal(state.ExchangeAnchor[TournamentDiscipline.Magic] * IsabellaBalance.ExchangeFactor(TournamentDiscipline.Magic), baseline, 6);
            state.ExchangeMatchesPlayed = 2;
            state.WeekNumber += GameCalendar.WeeksPerYear;
            Assert.Equal(baseline * (1 + 2 * IsabellaBalance.ExchangeGrowthPerMatch) * (1 + IsabellaBalance.ExchangeGrowthPerYear),
                IsabellaSystem.OpponentStrength(state, TournamentDiscipline.Magic), 6);
        }

        // ---------------- 最初の交流戦 ----------------

        [Fact]
        public void FirstExchange_Lost_OpensTrainingFacility_AndTournamentsFromNextMonth()
        {
            var (state, ev) = Visited();
            state.WeekNumber = 8; // 2の月 第4週
            var outcome = Assert.Single(new IsabellaSystem(new AlwaysMaxRng()).ResolveWeek(state));

            Assert.False(outcome.Won);
            Assert.True(outcome.First);
            Assert.Equal(3, outcome.Bouts.Count);
            Assert.NotNull(ev.Result);
            Assert.False(state.GuestTrainerPending); // 負けたら教官は来ない
            Assert.Equal(1, state.ExchangeMatchesPlayed);
            Assert.Equal(9, state.TournamentCalendarFromWeek);
            Assert.NotNull(outcome.OpenedFacility);
            Assert.Equal("Isabella", outcome.OpenedFacility!.Style);
            Assert.Equal(1, FacilityUnlockSystem.GetUnlockedLevel(state, outcome.OpenedFacility.Facility));
            Assert.Equal(1, FacilityUnlockSystem.TrainingFacilities.Count(t => FacilityUnlockSystem.GetUnlockedLevel(state, t) == 1));
            Assert.DoesNotContain("{", outcome.OpenedFacility.Line);

            // 次の月から大会の暦（その年の、それより前の大会は置かない）
            state.WeekNumber = 9;
            TournamentSystem.EnsureSchedule(state);
            var year1 = TournamentSystem.EventsOfYear(state, 1).Where(e => e.Kind != TournamentKind.Exchange).ToList();
            Assert.NotEmpty(year1);
            Assert.All(year1, e => Assert.True(GameCalendar.WeekNumberOf(e.Year, e.Month, e.Week) >= 9));
            Assert.DoesNotContain(year1, e => e.Kind == TournamentKind.Rookie); // 新人戦（春1の月）は翌年から
            Assert.Contains(year1, e => e.Kind == TournamentKind.Final);
        }

        [Fact]
        public void FirstExchange_OpensTheBestRatioDiscipline()
        {
            var (state, _) = Visited();
            // 来訪のあとに魔の子だけ大きく伸びた → 魔が相手に対して最も善戦する（相手の基準は来訪したときのまま）
            state.Adventurers.Single(a => a.Name == "魔の子").INT = 90;
            state.WeekNumber = 8;
            var outcome = new IsabellaSystem(new AlwaysMaxRng()).ResolveWeek(state).Single();
            Assert.Equal(FacilityType.Academy, outcome.OpenedFacility!.Facility);
        }

        [Fact]
        public void FirstWin_SendsGuestTrainer_LaterWinsPayPrizeAndMood()
        {
            var (state, _) = Visited();
            state.WeekNumber = 8;
            var isabella = new IsabellaSystem(new AlwaysMinRng());
            var first = isabella.ResolveWeek(state).Single();
            Assert.True(first.Won && first.FirstWin);
            Assert.Equal(0, first.Prize);
            Assert.True(state.GuestTrainerPending);
            Assert.Equal(1000, state.Gold);

            // 申し込み：季節に1回・月のはじめだけ
            state.WeekNumber = 9; // 3の月 第1週（同じ春）
            Assert.Contains("季節に1回", IsabellaSystem.ApplyBlockReason(state));
            state.WeekNumber = 13; // 夏1の月 第1週
            Assert.Null(IsabellaSystem.ApplyBlockReason(state));
            var rematch = IsabellaSystem.TryApply(state)!;
            Assert.Equal((1, 4, IsabellaBalance.ExchangeWeekOfMonth), (rematch.Year, rematch.Month, rematch.Week));
            Assert.Contains("まだ終わっていない", IsabellaSystem.ApplyBlockReason(state));
            foreach (var (d, name) in new[] { (TournamentDiscipline.Sword, "剣の子"), (TournamentDiscipline.Magic, "魔の子"), (TournamentDiscipline.Skill, "技の子") })
                Assert.True(IsabellaSystem.TryEnter(state, rematch, state.Adventurers.Single(a => a.Name == name), d, TournamentPrep.Rest));

            state.WeekNumber = 16;
            var second = isabella.ResolveWeek(state).Single();
            Assert.True(second.Won);
            Assert.False(second.FirstWin);
            Assert.False(second.First);
            Assert.Equal(IsabellaBalance.ExchangePrize, second.Prize);
            Assert.Equal(1000 + IsabellaBalance.ExchangePrize, state.Gold);
            Assert.Equal(50 + IsabellaBalance.ExchangeMood, state.MasterMood);
            Assert.Equal(2, state.ExchangeWins);
        }

        [Fact]
        public void EmptySlot_IsForfeit()
        {
            var (state, ev) = Visited();
            state.TournamentEntries.RemoveAll(e => e.EventId == ev.Id && e.Discipline != TournamentDiscipline.Sword);
            state.WeekNumber = 8;
            var outcome = new IsabellaSystem(new AlwaysMinRng()).ResolveWeek(state).Single();
            Assert.Equal(1, outcome.Wins);
            Assert.False(outcome.Won);
            Assert.Equal(2, outcome.Bouts.Count(b => b.AdventurerId == null));
        }

        [Fact]
        public void CancelApplication_OnlyForRematches()
        {
            var (state, ev) = Visited();
            Assert.False(IsabellaSystem.CancelApplication(state, ev)); // 最初の交流戦は取り下げられない
            state.WeekNumber = 8;
            new IsabellaSystem(new AlwaysMaxRng()).ResolveWeek(state);
            state.WeekNumber = 13;
            var rematch = IsabellaSystem.TryApply(state)!;
            Assert.True(IsabellaSystem.CancelApplication(state, rematch));
            Assert.Null(IsabellaSystem.PendingExchange(state));
        }

        // ---------------- 派遣の教官 ----------------

        [Fact]
        public void GuestTrainer_WaitsForFacility_ThenStays24Weeks_AndIsNotAGuildRetiree()
        {
            var state = NewGame();
            state.GuestTrainerPending = true;
            Assert.Null(IsabellaSystem.ProcessGuestTrainer(state).Arrived); // 訓練所が無いので待つ
            Assert.True(state.GuestTrainerPending);

            Build(state, FacilityType.SkillHall);
            state.WeekNumber = 10;
            var change = IsabellaSystem.ProcessGuestTrainer(state);
            var guest = change.Arrived!;
            Assert.NotNull(guest);
            Assert.Equal(IsabellaBalance.GuestTrainerName, guest.Name);
            Assert.True(guest.IsGuest && guest.IsRetired);
            Assert.Equal(FacilityType.SkillHall, change.AssignedTo);
            Assert.Equal(guest.Id, state.AssignedTrainers[FacilityType.SkillHall]);
            Assert.Equal(10 + IsabellaBalance.GuestTrainerWeeks, state.GuestTrainerUntilWeek);
            Assert.Contains(IsabellaBalance.GuestTrainerTraitId, guest.TraitIds);
            int expectedStr = (int)Math.Round(state.Adventurers.OrderByDescending(a => a.STR + a.AGI + a.VIT + a.MND + a.DEX + a.LDR + a.INT)
                .Take(IsabellaBalance.GuestTrainerTopCount).Average(a => a.STR));
            Assert.Equal(expectedStr, guest.STR);

            // ギルドの元冒険者ではない：最初の引退者に数えず、参謀・スカウトにもなれない、秘薬の親にもならない
            Assert.Empty(state.GuildRetirees);
            Assert.False(FacilitySystem.IsAvailable(state, FacilityType.WarRoom));
            Build(state, FacilityType.WarRoom);
            Assert.False(new AdvisorSystem().TryAssignAdvisor(state, guest.Id));
            Assert.DoesNotContain(guest, SoulFusionSystem.GuildMembers(state));

            state.WeekNumber = 10 + IsabellaBalance.GuestTrainerWeeks - 1;
            Assert.Null(IsabellaSystem.ProcessGuestTrainer(state).Left);
            state.WeekNumber++;
            Assert.Same(guest, IsabellaSystem.ProcessGuestTrainer(state).Left);
            Assert.DoesNotContain(guest, state.RetiredAdventurers);
            Assert.False(state.AssignedTrainers.ContainsKey(FacilityType.SkillHall));
            Assert.Null(state.GuestTrainerUntilWeek);
        }

        // ---------------- 依頼の開放 ----------------

        [Fact]
        public void Commissions_OpenNextSeason_AfterFirstPlacing()
        {
            var state = NewGame();
            state.WeekNumber = 20; // 夏2の月
            Assert.False(IsabellaSystem.CheckCommissionUnlock(state));
            state.TournamentPlacingsTotal = 1;
            Assert.True(IsabellaSystem.CheckCommissionUnlock(state));
            Assert.False(IsabellaSystem.CheckCommissionUnlock(state));
            Assert.Equal(25, state.CommissionsFromWeek); // 秋のはじめ
            Assert.False(CommissionSystem.IsOfferWeek(state, 13));
            Assert.True(CommissionSystem.IsOfferWeek(state, 25));
        }

        // ---------------- 週の決算 ----------------

        [Fact]
        public void WeekProcessing_VisitThenExchange_ThenCalendar()
        {
            var state = NewGame();
            DefeatForest40(state);
            var week = new WeekProcessingSystem(new MasterMoodSystem(), new EconomySystem(), new TrainingSystem(), new InjuryRecoverySystem(),
                new RestRecoverySystem(), new GrowthSystem(new SeededRng(1)), new SatisfactionSystem(), new AgingSystem(new SeededRng(1)),
                new FacilitySystem(), new DefeatSystem(), new RecruitmentSystem(new SeededRng(1)), isabellaSystem: new IsabellaSystem(new AlwaysMaxRng()));

            var visit = week.ProcessWeek(state); // 第1週の決算で来訪 → 第2週へ
            Assert.True(visit.IsabellaVisited);
            while (state.WeekNumber < 5)
                week.ProcessWeek(state);
            Assert.Equal(3, state.TournamentEntries.Count); // 2の月のはじめに自動で入っている

            WeeklySettlementResult? exchangeWeek = null;
            while (state.WeekNumber <= 8)
            {
                var r = week.ProcessWeek(state);
                if (r.ExchangeMatches.Count > 0) exchangeWeek = r;
            }
            Assert.NotNull(exchangeWeek);
            Assert.Contains(exchangeWeek!.FacilityUnlocks, n => n.Style == "Isabella");
            Assert.Equal(9, state.WeekNumber);
            Assert.Contains(state.TournamentEvents, e => e.Kind == TournamentKind.Final); // 決算のあとの新しい月で暦が置かれている
        }

        [Fact]
        public void MonthlyReport_ListsVisitExchangeGuestAndCommissions()
        {
            var (state, ev) = Visited();
            state.WeekNumber = 8;
            var outcome = new IsabellaSystem(new AlwaysMinRng()).ResolveWeek(state).Single();
            var settlement = new WeeklySettlementResult { IsabellaVisited = true, CommissionsUnlocked = true };
            settlement.ExchangeMatches.Add(outcome);
            settlement.GuestTrainer = new GuestTrainerChange { Arrived = IsabellaSystem.CreateGuestTrainer(state), AssignedTo = FacilityType.DrillHall };
            state.CommissionsFromWeek = 13;

            var report = MonthlyReport.Build(state, new[] { new AutoSkipWeek(new(), settlement) }, state.Gold);
            var lines = report.Highlights.Select(l => l.Text).ToList();

            Assert.Contains(lines, l => l.Contains("イザベラが来訪"));
            Assert.Contains(lines, l => l.Contains("交流戦に勝った（3勝0敗") && l.Contains("マルグリット"));
            Assert.Contains(lines, l => l.Contains("来月から王都の大会"));
            Assert.Contains(lines, l => l.Contains("派遣の教官マルグリットが着任") && l.Contains("鍛錬所"));
            Assert.Contains(lines, l => l.Contains("依頼が届く"));
        }

        [Fact]
        public void FacilityNotices_UseIsabellaInsteadOfAdjutant()
        {
            var state = NewGame();
            state.TournamentPlacingsTotal = TournamentBalance.DormPlacings[0];
            var notice = FacilityUnlockSystem.Evaluate(state).Single(n => n.Facility == FacilityType.Dormitory);
            Assert.Equal("Isabella", notice.Style);
            Assert.Contains("（イザベラ）", notice.Line);
            Assert.DoesNotContain(TournamentBalance.UnlockLines, l => l.Style == "Adjutant");
        }

        // ---------------- セーブ ----------------

        [Fact]
        public void Save_RoundTripsIsabellaState()
        {
            var (state, _) = Visited();
            state.ExchangeMatchesPlayed = 2;
            state.ExchangeWins = 1;
            state.TournamentCalendarFromWeek = 9;
            state.CommissionsFromWeek = 25;
            state.GuestTrainerUntilWeek = 40;
            state.RetiredAdventurers.Add(IsabellaSystem.CreateGuestTrainer(state));

            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(state.ToSaveData()))!);

            Assert.Equal(1, restored.IsabellaVisitWeek);
            Assert.Equal(state.ExchangeAnchor[TournamentDiscipline.Skill], restored.ExchangeAnchor[TournamentDiscipline.Skill], 6);
            Assert.Equal((2, 1, 9, 25, 40), (restored.ExchangeMatchesPlayed, restored.ExchangeWins, restored.TournamentCalendarFromWeek, restored.CommissionsFromWeek, restored.GuestTrainerUntilWeek));
            Assert.True(IsabellaSystem.GuestTrainer(restored)!.IsGuest);
            Assert.Equal(3, restored.TournamentEntries.Count(e => e.Discipline != null));
        }

        [Fact]
        public void NewGameSave_StaysClosed_OldSave_OpensTournamentsAndCommissions()
        {
            var fresh = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(NewGame().ToSaveData()))!);
            Assert.Null(fresh.IsabellaVisitWeek);
            Assert.Null(fresh.TournamentCalendarFromWeek);
            Assert.Null(fresh.CommissionsFromWeek);

            // §0.84より前のセーブ（StoryRulesVersion が無い）
            var data = NewGame().ToSaveData();
            data.StoryRulesVersion = 0;
            var old = GameState.FromSaveData(data);
            Assert.True(IsabellaSystem.HasVisited(old));
            Assert.True(IsabellaSystem.TournamentsOpen(old));
            Assert.Equal(GameState.LegacyFirstOfferWeek, old.CommissionsFromWeek);
            Assert.Null(IsabellaSystem.ApplyBlockReason(old)); // 交流戦も申し込める（相手の基準は今のギルドで決まる）
            Assert.True(IsabellaSystem.OpponentStrength(old, TournamentDiscipline.Sword) > 0);
        }
    }
}
