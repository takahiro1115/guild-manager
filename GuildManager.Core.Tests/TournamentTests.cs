using System;
using System.Collections.Generic;
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
    /// <summary>大会（§0.82）：暦・出場・試合・ご褒美・招待と、施設のご褒美による開放のテスト。</summary>
    public class TournamentTests
    {
        private static Adventurer Make(string name, int stat, int joinedYear = 0)
        {
            var a = new Adventurer
            {
                Name = name, JobClass = JobClass.Warrior, JoinedYear = joinedYear,
                STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat,
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        /// <summary>1年目 春1の月 第1週（新人戦の月のはじめ）。</summary>
        private static GameState NewGame(params Adventurer[] adventurers)
        {
            var state = new GameState { WeekNumber = 1, Gold = 1000, DungeonFields = SampleData.CreateDefaultFields() };
            state.Adventurers.AddRange(adventurers);
            TournamentSystem.EnsureSchedule(state);
            return state;
        }

        private static TournamentEvent Rookie(GameState state) => state.TournamentEvents.Single(e => e.Kind == TournamentKind.Rookie && e.Year == 1);

        // ---------------- 暦 ----------------

        [Fact]
        public void Schedule_HasClassicsRookieFinal_AndLocalRoyalCount()
        {
            var state = NewGame();
            var year1 = TournamentSystem.EventsOfYear(state, 1);

            Assert.Single(year1, e => e.Kind == TournamentKind.Rookie && e.Month == 1 && e.Week == 4);
            Assert.Equal(4, year1.Count(e => e.Kind == TournamentKind.Classic));
            Assert.Single(year1, e => e.Kind == TournamentKind.Final && e.Month == 12);
            Assert.Equal(TournamentSystem.LocalCount(state, 1), year1.Count(e => e.Kind is TournamentKind.Local or TournamentKind.Royal));
            Assert.Equal(TournamentBalance.LocalBaseCount, TournamentSystem.LocalCount(state, 1));
            Assert.DoesNotContain(year1, e => e.Name.Contains("{"));
        }

        [Fact]
        public void Schedule_KeepsG3BeforeG2BeforeG1_ForEachDiscipline()
        {
            var state = NewGame();
            state.TournamentPlacingsTotal = 40; // 数を増やす
            state.WeekNumber = GameCalendar.WeeksPerYear * 4 + 1; // 5年目
            TournamentSystem.EnsureSchedule(state);
            var year = TournamentSystem.EventsOfYear(state, 5);

            Assert.Equal(TournamentBalance.LocalMax, year.Count(e => e.Kind is TournamentKind.Local or TournamentKind.Royal));
            foreach (var d in new[] { TournamentDiscipline.Sword, TournamentDiscipline.Magic, TournamentDiscipline.Skill })
            {
                int g1 = year.Single(e => e.Kind == TournamentKind.Classic && e.Discipline == d).Month;
                var locals = year.Where(e => e.Kind == TournamentKind.Local && e.Discipline == d).ToList();
                var royals = year.Where(e => e.Kind == TournamentKind.Royal && e.Discipline == d).ToList();
                Assert.NotEmpty(locals);
                Assert.All(royals, r => Assert.True(r.Month < g1 && r.Month >= locals.Max(l => l.Month)));
                Assert.All(locals, l => Assert.True(l.Month >= 2 && l.Month < g1));
            }
        }

        [Fact]
        public void EnsureSchedule_IsIdempotent()
        {
            var state = NewGame();
            int count = state.TournamentEvents.Count;
            TournamentSystem.EnsureSchedule(state);
            Assert.Equal(count, state.TournamentEvents.Count);
        }

        // ---------------- 出場 ----------------

        [Fact]
        public void Rookie_OnlyThisYearsJoiners()
        {
            var rookie = Make("新人", 30, joinedYear: 1);
            var veteran = Make("古参", 60);
            var state = NewGame(rookie, veteran);
            var ev = Rookie(state);

            Assert.Null(TournamentSystem.EntryBlockReason(state, ev, rookie));
            Assert.NotNull(TournamentSystem.EntryBlockReason(state, ev, veteran));
        }

        [Fact]
        public void Entry_OnePerMonth_TwoPerEvent_MonthStartOnly_AndLeavesTraining()
        {
            var a = Make("A", 30, 1);
            var b = Make("B", 30, 1);
            var c = Make("C", 30, 1);
            var state = NewGame(a, b, c);
            var ev = Rookie(state);
            state.TrainingAssignments[a.Id] = FacilityType.DrillHall;

            Assert.True(TournamentSystem.TryEnter(state, ev, a, TournamentPrep.Rest));
            Assert.False(state.TrainingAssignments.ContainsKey(a.Id)); // 大会の月は訓練所に入らない
            Assert.False(TournamentSystem.TryEnter(state, ev, a, TournamentPrep.Rest)); // 1人1か月1大会
            Assert.True(TournamentSystem.TryEnter(state, ev, b, TournamentPrep.Push));
            Assert.False(TournamentSystem.TryEnter(state, ev, c, TournamentPrep.Rest)); // 1ギルド2人まで
            Assert.True(TournamentSystem.IsEntered(state, a.Id));

            state.WeekNumber = 2;
            Assert.NotNull(TournamentSystem.EntryBlockReason(state, ev, c)); // 月のはじめだけ
        }

        [Fact]
        public void Entrants_DoNotSortie()
        {
            var a = Make("A", 30, 1);
            var b = Make("B", 30, 1);
            var state = NewGame(a, b);
            TournamentSystem.TryEnter(state, Rookie(state), a, TournamentPrep.Rest);

            var party = PartyFormationSystem.BuildDispatchParty(state, new[] { a.Id, b.Id });
            Assert.DoesNotContain(a, party.Members);
            Assert.Contains(b, party.Members);
            Assert.Equal(WeekActivity.Tournament, IdleActivitySystem.GetWeekActivity(state, a));
        }

        [Fact]
        public void Qualification_G2NeedsG3Top4_G1NeedsG2Top4_FinalNeedsWins()
        {
            var a = Make("A", 60);
            var royal = new TournamentEvent { Kind = TournamentKind.Royal, Discipline = TournamentDiscipline.Sword, Grade = TournamentGrade.G2, Year = 2 };
            var classic = new TournamentEvent { Kind = TournamentKind.Classic, Discipline = TournamentDiscipline.Sword, Grade = TournamentGrade.G1, Year = 2 };
            var final = new TournamentEvent { Kind = TournamentKind.Final, Discipline = TournamentDiscipline.Best, Grade = TournamentGrade.G1, Year = 2 };

            Assert.NotNull(TournamentSystem.QualificationBlock(royal, a));
            a.TournamentRecords.Add(new TournamentRecord { Year = 1, Grade = TournamentGrade.G3, Discipline = TournamentDiscipline.Sword, Placing = 4 });
            Assert.Null(TournamentSystem.QualificationBlock(royal, a));
            Assert.NotNull(TournamentSystem.QualificationBlock(classic, a));
            a.TournamentRecords.Add(new TournamentRecord { Year = 2, Grade = TournamentGrade.G2, Discipline = TournamentDiscipline.Sword, Placing = 2 });
            Assert.Null(TournamentSystem.QualificationBlock(classic, a));
            Assert.NotNull(TournamentSystem.QualificationBlock(final, a));
            a.TournamentRecords.Add(new TournamentRecord { Year = 2, Grade = TournamentGrade.G1, Discipline = TournamentDiscipline.Sword, Placing = 1 });
            Assert.Null(TournamentSystem.QualificationBlock(final, a));
        }

        // ---------------- 試合とご褒美 ----------------

        [Fact]
        public void Resolve_StrongEntrantWins_GetsPrizeRecordAndSpendsHp()
        {
            var star = Make("星", 100, 1);
            var state = NewGame(star);
            var ev = Rookie(state);
            TournamentSystem.TryEnter(state, ev, star, TournamentPrep.Rest);
            int gold = state.Gold;
            int hp = star.CurrentHP;

            new TournamentSystem(new SeededRng(5)).Resolve(state, ev);

            Assert.NotNull(ev.Result);
            Assert.Equal(7, ev.Result!.Matches.Count); // 8人の勝ち抜き
            var placing = Assert.Single(ev.Result.Placings);
            Assert.Equal(1, placing.Placing);
            Assert.True(ev.Result.WinnerIsOurs);
            var def = TournamentBalance.Find("rookie")!;
            Assert.Equal(gold + def.Prize1, state.Gold);
            Assert.Equal(def.Prize1, state.TournamentPrizeTotal);
            Assert.Equal(1, state.TournamentPlacingsTotal);
            var record = Assert.Single(star.TournamentRecords);
            Assert.Equal(1, record.Placing);
            Assert.Equal(TournamentKind.Rookie, record.Kind);
            Assert.Equal(hp - 3 * (int)Math.Round(star.MaxHP * TournamentBalance.HpCostPerMatch), star.CurrentHP); // 3試合
        }

        [Fact]
        public void Resolve_WeakEntrantLosesEarly_NoPrize()
        {
            var weak = Make("弱", 1, 1);
            var state = NewGame(weak);
            var ev = Rookie(state);
            TournamentSystem.TryEnter(state, ev, weak, TournamentPrep.Rest);
            int gold = state.Gold;

            new TournamentSystem(new SeededRng(5)).Resolve(state, ev);

            Assert.Equal(8, ev.Result!.Placings.Single().Placing);
            Assert.Equal(gold, state.Gold);
            Assert.Equal(0, state.TournamentPlacingsTotal);
        }

        [Fact]
        public void TwoEntrants_AreInDifferentHalves()
        {
            var a = Make("A", 30, 1);
            var b = Make("B", 30, 1);
            var state = NewGame(a, b);
            var ev = Rookie(state);
            TournamentSystem.TryEnter(state, ev, a, TournamentPrep.Rest);
            TournamentSystem.TryEnter(state, ev, b, TournamentPrep.Rest);

            new TournamentSystem(new SeededRng(1)).Resolve(state, ev);

            Assert.DoesNotContain(ev.Result!.Matches.Where(m => m.Round == 1), m => m.OursA && m.OursB);
        }

        [Fact]
        public void HpFactor_LowersStrength()
        {
            var a = Make("A", 60);
            double full = TournamentSystem.MatchStrength(a, TournamentDiscipline.Sword);
            a.CurrentHP = a.MaxHP / 2;
            Assert.True(TournamentSystem.MatchStrength(a, TournamentDiscipline.Sword) < full);
            Assert.Equal(TournamentBalance.HpFactorBase, TournamentSystem.HpFactor(0), 6);
        }

        [Fact]
        public void Push_SpendsHpAndRollsGrowth_RestDoublesRecovery()
        {
            var pusher = Make("追", 30, 1);
            pusher.PA_STR = pusher.PA_VIT = pusher.PA_INT = pusher.PA_MND = pusher.PA_DEX = pusher.PA_AGI = 90;
            var rester = Make("休", 30, 1);
            var state = NewGame(pusher, rester);
            var ev = Rookie(state);
            TournamentSystem.TryEnter(state, ev, pusher, TournamentPrep.Push);
            TournamentSystem.TryEnter(state, ev, rester, TournamentPrep.Rest);
            int hp = pusher.CurrentHP;

            var growth = new GrowthSystem(new SeededRng(1)).ProcessTournamentPush(state);

            Assert.Equal(hp - TournamentBalance.PushHpCost, pusher.CurrentHP);
            Assert.All(growth, g => Assert.Same(pusher, g.Adventurer));

            rester.CurrentHP = rester.MaxHP / 2;
            int before = rester.CurrentHP;
            new RestRecoverySystem().ProcessWeeklyRest(state, new HashSet<Guid>());
            int withTournament = rester.CurrentHP - before;
            state.TournamentEntries.Clear();
            rester.CurrentHP = before;
            new RestRecoverySystem().ProcessWeeklyRest(state, new HashSet<Guid>());
            Assert.True(withTournament > rester.CurrentHP - before);
        }

        // ---------------- 施設のご褒美 ----------------

        [Fact]
        public void RookiePlacing_UnlocksBestDisciplineTrainingFacility()
        {
            var state = NewGame();
            var mage = Make("魔", 10, 1);
            mage.INT = 60; mage.MND = 50;
            state.Adventurers.Add(mage);
            mage.TournamentRecords.Add(new TournamentRecord { Year = 1, Kind = TournamentKind.Rookie, Grade = TournamentGrade.G3, Discipline = TournamentSystem.BestDiscipline(mage), Placing = 4 });

            var notices = FacilityUnlockSystem.Evaluate(state);

            Assert.Equal(TournamentDiscipline.Magic, TournamentSystem.BestDiscipline(mage));
            Assert.Equal(1, FacilityUnlockSystem.GetUnlockedLevel(state, FacilityType.Academy));
            Assert.Equal(0, FacilityUnlockSystem.GetUnlockedLevel(state, FacilityType.DrillHall));
            Assert.Contains(notices, n => n.Facility == FacilityType.Academy && n.Style == "Albert" && n.Line.Length > 0);
            Assert.Equal(1, FacilityUnlockSystem.GetUnlockedLevel(state, FacilityType.RecruitmentOffice)); // 新人戦の入賞
        }

        [Theory]
        [InlineData(TournamentGrade.G3, 1, 2)]
        [InlineData(TournamentGrade.G2, 1, 3)]
        [InlineData(TournamentGrade.G1, 4, 4)]
        [InlineData(TournamentGrade.G1, 1, 5)]
        [InlineData(TournamentGrade.Special, 1, 3)]
        public void TrainingFacility_LevelsFollowGradeAndPlacing(TournamentGrade grade, int placing, int expected)
        {
            var state = NewGame();
            var a = Make("剣", 60);
            state.Adventurers.Add(a);
            a.TournamentRecords.Add(new TournamentRecord { Year = 1, Kind = TournamentKind.Local, Grade = grade, Discipline = TournamentDiscipline.Sword, Placing = placing });

            FacilityUnlockSystem.Evaluate(state);

            Assert.Equal(expected, FacilityUnlockSystem.GetUnlockedLevel(state, FacilityType.DrillHall));
        }

        [Fact]
        public void EstimateWinChance_GrowsWithStrength_AndLabels()
        {
            var ev = new TournamentEvent { DefinitionId = "local_sword", Kind = TournamentKind.Local, Discipline = TournamentDiscipline.Sword, Grade = TournamentGrade.G3, Year = 1 };
            var (min, max) = TournamentSystem.OpponentRange(ev, TournamentDiscipline.Sword);
            double mid = (min + max) / 2;

            Assert.Equal(0.125, TournamentSystem.EstimateWinChance(ev, TournamentDiscipline.Sword, mid), 6); // 五分の相手に3連勝
            Assert.True(TournamentSystem.EstimateWinChance(ev, TournamentDiscipline.Sword, mid * 1.5) > 0.4);
            Assert.Equal(0, TournamentSystem.EstimateWinChance(ev, TournamentDiscipline.Sword, 0));
            Assert.Equal("本命", TournamentSystem.WinChanceLabel(0.5));
            Assert.Equal("対抗", TournamentSystem.WinChanceLabel(0.2));
            Assert.Equal("穴", TournamentSystem.WinChanceLabel(0.05));
            Assert.Equal("厳しい", TournamentSystem.WinChanceLabel(0.01));
        }

        [Fact]
        public void DescribeUnlock_SaysBuildForNewAndLevel()
        {
            Assert.Equal("学問所を建てられるようになった", FacilityUnlockSystem.DescribeUnlock(new FacilityUnlockNotice { Facility = FacilityType.Academy, Level = 1 }));
            Assert.Equal("学問所をLv2まで建てられるようになった", FacilityUnlockSystem.DescribeUnlock(new FacilityUnlockNotice { Facility = FacilityType.Academy, Level = 2 }));
        }

        [Fact]
        public void TrainingFacility_Lv5_AlsoByTwoG1RunnerUps()
        {
            var state = NewGame();
            var a = Make("剣", 60);
            state.Adventurers.Add(a);
            a.TournamentRecords.Add(new TournamentRecord { Year = 3, Kind = TournamentKind.Classic, Grade = TournamentGrade.G1, Discipline = TournamentDiscipline.Sword, Placing = 2 });
            FacilityUnlockSystem.Evaluate(state);
            Assert.Equal(4, FacilityUnlockSystem.GetUnlockedLevel(state, FacilityType.DrillHall));

            a.TournamentRecords.Add(new TournamentRecord { Year = 4, Kind = TournamentKind.Classic, Grade = TournamentGrade.G1, Discipline = TournamentDiscipline.Sword, Placing = 2 });
            FacilityUnlockSystem.Evaluate(state);
            Assert.Equal(5, FacilityUnlockSystem.GetUnlockedLevel(state, FacilityType.DrillHall));
        }

        [Theory]
        [InlineData(new[] { 4 }, 1)]
        [InlineData(new[] { 2 }, 2)]
        [InlineData(new[] { 1 }, 3)]
        [InlineData(new[] { 1, 1 }, 4)]
        [InlineData(new[] { 1, 1, 1 }, 5)]
        public void RecruitmentOffice_FollowsRookiePlacingsAndWins(int[] placings, int expected)
        {
            var state = NewGame();
            var a = Make("新", 60);
            state.Adventurers.Add(a);
            for (int i = 0; i < placings.Length; i++)
                a.TournamentRecords.Add(new TournamentRecord { Year = i + 1, Kind = TournamentKind.Rookie, Grade = TournamentGrade.G3, Discipline = TournamentDiscipline.Sword, Placing = placings[i] });

            FacilityUnlockSystem.Evaluate(state);

            Assert.Equal(expected, FacilityUnlockSystem.GetUnlockedLevel(state, FacilityType.RecruitmentOffice));
        }

        [Fact]
        public void RoyalStyle_ForG1Win()
        {
            var state = NewGame();
            var a = Make("剣", 60);
            state.Adventurers.Add(a);
            a.TournamentRecords.Add(new TournamentRecord { Year = 3, Name = "王国剣闘祭", Kind = TournamentKind.Classic, Grade = TournamentGrade.G1, Discipline = TournamentDiscipline.Sword, Placing = 1 });

            var notice = FacilityUnlockSystem.Evaluate(state).Single(n => n.Facility == FacilityType.DrillHall);

            Assert.Equal(5, notice.Level);
            Assert.Equal("Royal", notice.Style);
            Assert.Contains("王国剣闘祭", notice.Line);
        }

        [Fact]
        public void Dormitory_And_Tavern_FollowTotals()
        {
            var state = NewGame();
            state.TournamentPlacingsTotal = TournamentBalance.DormPlacings[1];
            state.TournamentPrizeTotal = TournamentBalance.TavernPrize[0];

            FacilityUnlockSystem.Evaluate(state);

            Assert.Equal(3, FacilityUnlockSystem.GetUnlockedLevel(state, FacilityType.Dormitory));
            Assert.Equal(2, FacilityUnlockSystem.GetUnlockedLevel(state, FacilityType.Tavern));
        }

        [Fact]
        public void WarRoom_FollowsPartyRecords()
        {
            var state = NewGame();
            var a = Make("隊", 60);
            state.Adventurers.Add(a);
            a.TournamentRecords.Add(new TournamentRecord { Year = 2, DefinitionId = "classic_party", Discipline = TournamentDiscipline.Party, Grade = TournamentGrade.G1, Placing = 1 });
            a.TournamentRecords.Add(new TournamentRecord { Year = 3, DefinitionId = "classic_party", Discipline = TournamentDiscipline.Party, Grade = TournamentGrade.G1, Placing = 1 });

            FacilityUnlockSystem.Evaluate(state);

            Assert.Equal(5, FacilityUnlockSystem.GetUnlockedLevel(state, FacilityType.WarRoom));
        }

        [Fact]
        public void UnlockedLevels_NeverGoDown_AndDescribeNext()
        {
            var state = NewGame();
            state.FacilityUnlockedLevels[FacilityType.SkillHall] = 3;
            FacilityUnlockSystem.Evaluate(state);
            Assert.Equal(3, FacilityUnlockSystem.GetUnlockedLevel(state, FacilityType.SkillHall));
            Assert.Contains("G1でベスト4", FacilityUnlockSystem.DescribeNext(state, FacilityType.SkillHall));
            Assert.Contains("ベスト4", FacilityUnlockSystem.DescribeNext(state, FacilityType.DrillHall));
        }

        [Fact]
        public void Rescue_OffersTrainingFacilityInYear2_WhenNoneOpened()
        {
            var state = NewGame();
            state.WeekNumber = GameCalendar.WeeksPerYear + 1;
            FacilityUnlockSystem.CheckRescue(state);
            Assert.True(state.PendingTrainingFacilityChoice);

            var notice = FacilityUnlockSystem.ChooseRescueFacility(state, FacilityType.SkillHall);

            Assert.NotNull(notice);
            Assert.Equal(1, FacilityUnlockSystem.GetUnlockedLevel(state, FacilityType.SkillHall));
            Assert.False(state.PendingTrainingFacilityChoice);
        }

        // ---------------- 招待 ----------------

        [Fact]
        public void Invitations_FieldUnlock_G1Win_Placings_EachOnce()
        {
            var a = Make("A", 60);
            var state = NewGame(a);
            var field = state.DungeonFields[1];
            var g1 = new TournamentEvent { Grade = TournamentGrade.G1, Year = 1, Result = new TournamentResult { WinnerIsOurs = true, Placings = { new TournamentPlacing { AdventurerId = a.Id, Placing = 1 } } } };
            state.TournamentPlacingsTotal = 10;

            var invites = TournamentSystem.CheckInvitations(state, new[] { field }, new[] { g1 });

            Assert.Equal(3, invites.Count);
            Assert.Contains(invites, e => e.DefinitionId == "invite_lord" && e.Name.Contains(field.Name) && e.SpecialReward == "unique");
            Assert.Contains(invites, e => e.DefinitionId == "invite_royal" && e.InvitedAdventurerId == a.Id);
            Assert.Contains(invites, e => e.DefinitionId == "invite_margrave" && e.SpecialReward == "halfcost");
            Assert.All(invites, e => Assert.Equal(1 + TournamentBalance.InviteLeadMonths, e.Month));
            Assert.Empty(TournamentSystem.CheckInvitations(state, new[] { field }, new[] { g1 })); // 二度は出さない
        }

        // ---------------- 週次決算・セーブ ----------------

        [Fact]
        public void WeekProcessing_ResolvesRookieInWeek4_AndUnlocksFacility()
        {
            var star = Make("星", 100, 1);
            var state = NewGame(star);
            TournamentSystem.TryEnter(state, Rookie(state), star, TournamentPrep.Rest);
            var week = new WeekProcessingSystem(new MasterMoodSystem(), new EconomySystem(), new TrainingSystem(), new InjuryRecoverySystem(),
                new RestRecoverySystem(), new GrowthSystem(new SeededRng(1)), new SatisfactionSystem(), new AgingSystem(new SeededRng(1)),
                new FacilitySystem(), new DefeatSystem(), new RecruitmentSystem(new SeededRng(1)), tournamentSystem: new TournamentSystem(new SeededRng(3)));

            WeeklySettlementResult? last = null;
            for (int i = 0; i < 4; i++)
                last = week.ProcessWeek(state);

            var resolved = Assert.Single(last!.TournamentsResolved);
            Assert.Equal(TournamentKind.Rookie, resolved.Kind);
            Assert.NotEmpty(last.FacilityUnlocks);
            Assert.Contains(new[] { FacilityType.DrillHall, FacilityType.Academy, FacilityType.SkillHall }, t => FacilityUnlockSystem.GetUnlockedLevel(state, t) >= 1); // 新人戦の入賞で最初の訓練所が開く
            Assert.False(TournamentSystem.IsEntered(state, star.Id)); // 月が変わると出場は終わる
        }

        [Fact]
        public void Save_RoundTripsScheduleRecordsAndUnlocks()
        {
            var a = Make("A", 60, 1);
            var state = NewGame(a);
            TournamentSystem.TryEnter(state, Rookie(state), a, TournamentPrep.Push);
            a.TournamentRecords.Add(new TournamentRecord { Year = 1, Name = "新人戦", Placing = 1 });
            state.FacilityUnlockedLevels[FacilityType.DrillHall] = 2;
            state.TournamentInviteKeys.Add("margrave");
            state.TournamentPrizeTotal = 300;

            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(state.ToSaveData()))!);

            Assert.Equal(state.TournamentEvents.Count, restored.TournamentEvents.Count);
            Assert.Equal(TournamentPrep.Push, restored.TournamentEntries.Single().Prep);
            Assert.Equal(2, FacilityUnlockSystem.GetUnlockedLevel(restored, FacilityType.DrillHall));
            Assert.Contains("margrave", restored.TournamentInviteKeys);
            Assert.Equal(300, restored.TournamentPrizeTotal);
            Assert.Equal(1, restored.Adventurers.Single().JoinedYear);
            Assert.Single(restored.Adventurers.Single().TournamentRecords);
        }
    }
}
