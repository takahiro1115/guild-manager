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
    /// <summary>戦績・称号・殿堂・引退式と観測日誌（大会と育成の栄光 段2、→ HonorSystem）のテスト。</summary>
    public class HonorSystemTests
    {
        private static Adventurer Make(string name, int stat = 50, int age = 18)
        {
            var a = new Adventurer
            {
                Name = name, JobClass = JobClass.Warrior, Age = age,
                STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat,
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static GameState NewGame(params Adventurer[] adventurers)
        {
            var state = new GameState { WeekNumber = 1, Gold = 100000, DungeonFields = SampleData.CreateDefaultFields(), TournamentCalendarFromWeek = 1 }; // 大会は開いている（§0.84）
            state.Adventurers.AddRange(adventurers);
            return state;
        }

        private static TournamentRecord Win(int year, string defId, string name, TournamentKind kind, TournamentDiscipline d, TournamentGrade grade = TournamentGrade.G1, int week = 0, int placing = 1) =>
            new() { Year = year, Week = week, DefinitionId = defId, Name = name, Kind = kind, Grade = grade, Discipline = d, Placing = placing };

        private static TournamentRecord SwordG1(int year, int week = 0) => Win(year, "classic_sword", "王国剣闘祭", TournamentKind.Classic, TournamentDiscipline.Sword, week: week);
        private static TournamentRecord MagicG1(int year) => Win(year, "classic_magic", "王国魔導祭", TournamentKind.Classic, TournamentDiscipline.Magic);
        private static TournamentRecord SkillG1(int year) => Win(year, "classic_skill", "王国射技祭", TournamentKind.Classic, TournamentDiscipline.Skill);

        private static List<string> Names(GameState state, Adventurer a) => HonorSystem.GetTitles(state, a).Select(t => t.Name).ToList();

        private static WeekProcessingSystem Week() =>
            new(new MasterMoodSystem(), new EconomySystem(), new TrainingSystem(), new InjuryRecoverySystem(),
                new RestRecoverySystem(), new GrowthSystem(new SeededRng(1)), new SatisfactionSystem(), new AgingSystem(new SeededRng(1)),
                new FacilitySystem(), new DefeatSystem(), new RecruitmentSystem(new SeededRng(1)), tournamentSystem: new TournamentSystem(new SeededRng(3)));

        // ---------------- バランス表 ----------------

        [Fact]
        public void Balance_LoadsTitlesAndLines()
        {
            Assert.Equal(15, HonorBalance.Titles.Count);
            Assert.Equal(TitleRank.Legend, HonorBalance.Title("final_win").Rank);
            Assert.Equal(10, HonorBalance.Title("boss_mvp").Param);
            foreach (var key in new[] { "Legend", "Name", "Honor", "Plain", "Short", "HallOfFame" })
                Assert.NotEmpty(HonorBalance.RetirementLines[key]);
            Assert.Equal(1.1, HonorBalance.HallOfFameAdvisorMultiplier, 6);
        }

        // ---------------- 年表（週の決算） ----------------

        [Fact]
        public void ProcessWeek_WritesJoinedOnce_WithJoinEstimate_AndPeak()
        {
            var a = Make("A");
            var state = NewGame(a);

            HonorSystem.ProcessWeek(state, Array.Empty<TournamentEvent>(), Array.Empty<SoulFusionCulture>());
            a.STR = 70;
            state.WeekNumber++;
            HonorSystem.ProcessWeek(state, Array.Empty<TournamentEvent>(), Array.Empty<SoulFusionCulture>());
            a.STR = 60; // 下がってもピークは残る

            var joined = Assert.Single(a.Journal, e => e.Kind == JournalKind.Joined);
            Assert.Equal(1, joined.Week);
            Assert.False(string.IsNullOrEmpty(a.JoinEstimate));
            Assert.Contains(a.JoinEstimate, joined.Text);
            Assert.Equal(70, a.PeakStats["STR"]);
            Assert.Equal(50, a.PeakStats["INT"]);
        }

        [Fact]
        public void ProcessWeek_TracksSevereInjuryAndRecovery()
        {
            var a = Make("A");
            var state = NewGame(a);
            a.Injury = InjurySeverity.Severe;
            HonorSystem.ProcessWeek(state, Array.Empty<TournamentEvent>(), Array.Empty<SoulFusionCulture>());
            state.WeekNumber++;
            HonorSystem.ProcessWeek(state, Array.Empty<TournamentEvent>(), Array.Empty<SoulFusionCulture>()); // 治るまで重ねて書かない
            a.Injury = InjurySeverity.None;
            state.WeekNumber++;
            HonorSystem.ProcessWeek(state, Array.Empty<TournamentEvent>(), Array.Empty<SoulFusionCulture>());

            Assert.Single(a.Journal, e => e.Kind == JournalKind.SevereInjury);
            Assert.Equal(3, Assert.Single(a.Journal, e => e.Kind == JournalKind.Recovered).Week);
        }

        [Fact]
        public void ProcessWeek_CountsWeeksUnderTrainer()
        {
            var trainee = Make("弟子");
            var trainer = Make("師匠", 90, 26);
            var state = NewGame(trainee);
            state.RetiredAdventurers.Add(trainer);
            state.TrainingAssignments[trainee.Id] = FacilityType.DrillHall;
            state.AssignedTrainers[FacilityType.DrillHall] = trainer.Id;

            for (int i = 0; i < HonorBalance.MentorWeeks; i++)
                HonorSystem.ProcessWeek(state, Array.Empty<TournamentEvent>(), Array.Empty<SoulFusionCulture>());

            Assert.Equal(HonorBalance.MentorWeeks, trainee.MentorWeeks[trainer.Id]);
            Assert.Contains(HonorSystem.Mentor, HonorSystem.GetRelations(state, trainee).Single(r => r.OtherId == trainer.Id).Tags);
            Assert.Contains(HonorSystem.Mentor, HonorSystem.GetRelations(state, trainer).Single(r => r.OtherId == trainee.Id).Tags);
        }

        [Fact]
        public void ProcessWeek_WritesTournamentFirstsAndG1Win()
        {
            var a = Make("A");
            var state = NewGame(a);
            HonorSystem.ProcessWeek(state, Array.Empty<TournamentEvent>(), Array.Empty<SoulFusionCulture>());

            void Play(TournamentRecord r)
            {
                state.WeekNumber++;
                r.Week = state.WeekNumber;
                a.TournamentRecords.Add(r);
                var ev = new TournamentEvent { DefinitionId = r.DefinitionId, Name = r.Name, Year = r.Year };
                HonorSystem.ProcessWeek(state, new[] { ev }, Array.Empty<SoulFusionCulture>());
            }

            Play(Win(1, "local_sword", "西部剣技大会", TournamentKind.Local, TournamentDiscipline.Sword, TournamentGrade.G3, placing: 8)); // ベスト8は書かない
            Play(Win(1, "local_sword", "東部剣技大会", TournamentKind.Local, TournamentDiscipline.Sword, TournamentGrade.G3, placing: 4));
            Play(Win(1, "royal_sword", "王都剣技会", TournamentKind.Royal, TournamentDiscipline.Sword, TournamentGrade.G2));
            Play(Win(1, "royal_sword", "王都剣技会", TournamentKind.Royal, TournamentDiscipline.Sword, TournamentGrade.G2)); // 2つ目の勝ち鞍は書かない
            Play(SwordG1(1));

            Assert.Single(a.Journal, e => e.Kind == JournalKind.FirstPlacing && e.Text.Contains("東部剣技大会"));
            Assert.Single(a.Journal, e => e.Kind == JournalKind.FirstWin);
            Assert.Single(a.Journal, e => e.Kind == JournalKind.BigWin && e.Text.Contains("王国剣闘祭"));
        }

        [Fact]
        public void ProcessWeek_NotifiesNewTitleOnce()
        {
            var a = Make("A");
            var state = NewGame(a);
            a.TournamentRecords.Add(Win(1, "rookie", "新人戦", TournamentKind.Rookie, TournamentDiscipline.Best, TournamentGrade.G3));

            var first = HonorSystem.ProcessWeek(state, Array.Empty<TournamentEvent>(), Array.Empty<SoulFusionCulture>());
            var second = HonorSystem.ProcessWeek(state, Array.Empty<TournamentEvent>(), Array.Empty<SoulFusionCulture>());

            Assert.Single(first, n => n.Kind == JournalKind.Title && n.Text.Contains("新人王"));
            Assert.DoesNotContain(second, n => n.Kind == JournalKind.Title);
            Assert.Equal(new[] { "rookie_king" }, a.EarnedTitleIds);
            Assert.Single(a.Journal, e => e.Kind == JournalKind.Title);
        }

        [Fact]
        public void ProcessWeek_DaughterBorn_AndMotherDaughterSquadOnce()
        {
            var mother = Make("母", 60, 24);
            var daughter = Make("娘");
            daughter.ParentIds.Add(mother.Id);
            var state = NewGame(mother, daughter);
            var culture = new SoulFusionCulture { ParentAId = mother.Id, ParentBId = mother.Id, Child = daughter };
            var party = new Party();
            party.TryAdd(mother);
            party.TryAdd(daughter);
            state.ActiveDungeonMissions.Add(new ActiveDungeonMission { Party = party, Field = state.DungeonFields[0] });

            var notices = HonorSystem.ProcessWeek(state, Array.Empty<TournamentEvent>(), new[] { culture });
            state.WeekNumber++;
            var again = HonorSystem.ProcessWeek(state, Array.Empty<TournamentEvent>(), Array.Empty<SoulFusionCulture>());

            Assert.Single(mother.Journal, e => e.Kind == JournalKind.DaughterBorn && e.RelatedId == daughter.Id);
            Assert.Single(daughter.Journal, e => e.Kind == JournalKind.MotherDaughterSquad && e.RelatedId == mother.Id);
            Assert.Single(mother.Journal, e => e.Kind == JournalKind.MotherDaughterSquad && e.RelatedId == daughter.Id);
            Assert.Single(notices, n => n.Kind == JournalKind.MotherDaughterSquad);
            Assert.DoesNotContain(again, n => n.Kind == JournalKind.MotherDaughterSquad);
            Assert.Contains("娘として生まれ", Assert.Single(daughter.Journal, e => e.Kind == JournalKind.Joined).Text);
        }

        // ---------------- 称号 ----------------

        [Fact]
        public void Titles_None_ForFreshAdventurer()
        {
            var a = Make("A");
            Assert.Empty(HonorSystem.GetTitles(NewGame(a), a));
            Assert.Equal("", HonorSystem.DisplayTitle(NewGame(a), a));
        }

        [Fact]
        public void Titles_G1Winner_UsesLastG1Name()
        {
            var a = Make("A");
            var state = NewGame(a);
            a.TournamentRecords.Add(SwordG1(2));
            a.TournamentRecords.Add(MagicG1(3));

            Assert.Contains("王国魔導祭の覇者", Names(state, a));
            Assert.Contains("双璧の覇者", Names(state, a));
            Assert.DoesNotContain("三冠", Names(state, a));
        }

        [Fact]
        public void Titles_TripleCrown_ReplacesDoubleCrown()
        {
            var a = Make("A");
            var state = NewGame(a);
            a.TournamentRecords.AddRange(new[] { SwordG1(2), MagicG1(3), SkillG1(4) });

            Assert.Contains("三冠", Names(state, a));
            Assert.DoesNotContain("双璧の覇者", Names(state, a));
            Assert.Equal("三冠", HonorSystem.DisplayTitle(state, a));
        }

        [Fact]
        public void Titles_Streaks_TwoPeatThenThreePeat()
        {
            var a = Make("A");
            var state = NewGame(a);
            a.TournamentRecords.AddRange(new[] { SwordG1(2), SwordG1(3) });
            Assert.Contains("王国剣闘祭二連覇", Names(state, a));

            a.TournamentRecords.Add(SwordG1(4));
            Assert.Contains("王国剣闘祭三連覇", Names(state, a));
            Assert.DoesNotContain("王国剣闘祭二連覇", Names(state, a));
        }

        [Fact]
        public void Titles_Streak_NeedsConsecutiveYears()
        {
            var a = Make("A");
            var state = NewGame(a);
            a.TournamentRecords.AddRange(new[] { SwordG1(2), SwordG1(4) });
            Assert.DoesNotContain(Names(state, a), n => n.Contains("連覇"));
        }

        [Fact]
        public void Titles_TwoGenerations_ForMotherAndDaughter()
        {
            var mother = Make("母");
            var daughter = Make("娘");
            daughter.ParentIds.Add(mother.Id);
            var state = NewGame(daughter);
            state.RetiredAdventurers.Add(mother);
            mother.TournamentRecords.Add(SwordG1(2));
            Assert.DoesNotContain("二代制覇", Names(state, daughter));

            daughter.TournamentRecords.Add(SwordG1(9));
            Assert.Contains("二代制覇", Names(state, daughter));
            Assert.Contains("二代制覇", Names(state, mother));
        }

        [Fact]
        public void Titles_FinalWin_IsLegend_AndHallOfFameReason()
        {
            var a = Make("A");
            var state = NewGame(a);
            a.TournamentRecords.Add(Win(3, "final", "王都最強決定戦", TournamentKind.Final, TournamentDiscipline.Best));

            Assert.Equal("王都最強", HonorSystem.DisplayTitle(state, a));
            Assert.Equal("王都最強決定戦で優勝", HonorSystem.HallOfFameReason(state, a));
        }

        [Fact]
        public void Titles_PartyWin_And_RookieKing()
        {
            var a = Make("A");
            var state = NewGame(a);
            a.TournamentRecords.Add(Win(2, "classic_party", "迷宮踏破杯", TournamentKind.Classic, TournamentDiscipline.Party));
            a.TournamentRecords.Add(Win(1, "rookie", "新人戦", TournamentKind.Rookie, TournamentDiscipline.Best, TournamentGrade.G3));

            Assert.Contains("迷宮の踏破者", Names(state, a));
            Assert.Contains("新人王", Names(state, a));
            Assert.DoesNotContain(Names(state, a), n => n.EndsWith("の覇者")); // 部隊戦のG1は「{G1}の覇者」にしない
        }

        [Fact]
        public void Titles_Indomitable_WithinWeeksAfterRecovery()
        {
            var a = Make("A");
            var state = NewGame(a);
            a.Journal.Add(new JournalEntry { Week = 100, Kind = JournalKind.Recovered });
            a.TournamentRecords.Add(SwordG1(4, week: 100 + HonorBalance.Title("indomitable").Param + 1));
            Assert.DoesNotContain("不屈", Names(state, a));

            a.TournamentRecords.Add(SwordG1(3, week: 120));
            Assert.Contains("不屈", Names(state, a));
        }

        [Fact]
        public void Titles_BossCounts_MvpVeteranAbyss()
        {
            var a = Make("A");
            var state = NewGame(a);
            for (int i = 0; i < HonorBalance.Title("veteran").Param; i++)
                a.BossKills.Add(new BossKillRecord { BossId = Guid.NewGuid(), FieldId = "forest", Floor = 10, IsMvp = i < HonorBalance.Title("boss_mvp").Param });
            Assert.Contains("主討ち", Names(state, a));
            Assert.Contains("古強者", Names(state, a));
            Assert.DoesNotContain("深淵を討つ者", Names(state, a));

            a.BossKills.Add(new BossKillRecord { BossId = Guid.NewGuid(), FieldId = HonorSystem.AbyssFieldId, Floor = DungeonField.MaxFloor });
            Assert.Contains("深淵を討つ者", Names(state, a));
        }

        [Fact]
        public void Titles_LateBloomer_LowEstimateHighPeak()
        {
            var a = Make("A");
            var state = NewGame(a);
            a.JoinEstimate = "C?";
            foreach (var s in new[] { "STR", "AGI", "VIT", "MND", "DEX", "LDR", "INT" })
                a.PeakStats[s] = (int)PotentialEstimateBalance.RankA;
            Assert.Contains("叩き上げ", Names(state, a));

            a.JoinEstimate = "B";
            Assert.DoesNotContain("叩き上げ", Names(state, a));
        }

        [Fact]
        public void Titles_PlacingRegular()
        {
            var a = Make("A");
            var state = NewGame(a);
            for (int i = 0; i < HonorBalance.Title("placing_regular").Param; i++)
                a.TournamentRecords.Add(Win(1, "local_sword", "剣技大会", TournamentKind.Local, TournamentDiscipline.Sword, TournamentGrade.G3, placing: 4));
            Assert.Contains("入賞常連", Names(state, a));
        }

        [Fact]
        public void DisplayTitle_HighestRank_ThenNewest()
        {
            var a = Make("A");
            var state = NewGame(a);
            a.TournamentRecords.Add(Win(1, "rookie", "新人戦", TournamentKind.Rookie, TournamentDiscipline.Best, TournamentGrade.G3));
            a.TournamentRecords.Add(Win(2, "classic_party", "迷宮踏破杯", TournamentKind.Classic, TournamentDiscipline.Party));
            Assert.Equal("迷宮の踏破者", HonorSystem.DisplayTitle(state, a)); // 名＞誉

            // 同じ格（名）なら新しく得たほう
            a.EarnedTitleIds.AddRange(new[] { "rookie_king", "party_win" });
            a.BossKills.Add(new BossKillRecord { FieldId = HonorSystem.AbyssFieldId, Floor = DungeonField.MaxFloor });
            Assert.Equal("深淵を討つ者", HonorSystem.DisplayTitle(state, a)); // まだ知らせていない＝いちばん新しい
            a.EarnedTitleIds.Add("abyss_slayer");
            Assert.Equal("深淵を討つ者", HonorSystem.DisplayTitle(state, a));
            a.EarnedTitleIds.Remove("party_win");
            a.EarnedTitleIds.Add("party_win");
            Assert.Equal("迷宮の踏破者", HonorSystem.DisplayTitle(state, a));
        }

        // ---------------- 殿堂 ----------------

        [Fact]
        public void HallOfFame_TwoG1Wins_JudgedAtRetirement()
        {
            var a = Make("A", 60, 25);
            var state = NewGame(a);
            state.WeekNumber = GameCalendar.WeeksPerYear; // 年度末
            a.TournamentRecords.AddRange(new[] { SwordG1(1) });
            Assert.Null(HonorSystem.HallOfFameReason(state, a));
            a.TournamentRecords.Add(MagicG1(1));
            Assert.Equal("G1を2勝", HonorSystem.HallOfFameReason(state, a));
            Assert.Null(a.HallOfFameYear); // 現役中は入らない

            new AgingSystem(new SeededRng(1)).ProcessWeeklyAging(state); // 26歳で満期引退

            Assert.True(a.IsRetired);
            Assert.Equal(1, a.HallOfFameYear);
            Assert.Equal("G1を2勝", a.HallOfFameReason);
            Assert.Contains(a.Journal, e => e.Kind == JournalKind.Retired);
            Assert.Contains(a.Journal, e => e.Kind == JournalKind.HallOfFame);
            Assert.Equal(new[] { a }, HonorSystem.HallOfFame(state));
        }

        [Fact]
        public void HallOfFame_LegendTitleAlsoQualifies_NoTitleDoesNot()
        {
            var a = Make("A", 60, 25);
            var b = Make("B", 60, 25);
            var state = NewGame(a, b);
            a.TournamentRecords.AddRange(new[] { SwordG1(1), SwordG1(2), SwordG1(3) }); // 三連覇（G1は3勝なので、G1の数で先に入る）
            b.BossKills.Add(new BossKillRecord { FieldId = "forest", Floor = 10 });
            var aging = new AgingSystem(new SeededRng(1));
            aging.RetireVoluntarily(state, a);
            aging.RetireVoluntarily(state, b);

            Assert.NotNull(a.HallOfFameYear);
            Assert.Null(b.HallOfFameYear);
            Assert.Equal("伝説の称号「二代制覇」", HallReasonForLegendOnly());
        }

        /// <summary>G1の勝ちが1つでも伝説の称号（二代制覇）があれば殿堂の資格がある。</summary>
        private static string? HallReasonForLegendOnly()
        {
            var mother = Make("母");
            var daughter = Make("娘");
            daughter.ParentIds.Add(mother.Id);
            var state = NewGame(daughter, mother);
            mother.TournamentRecords.Add(SwordG1(1));
            daughter.TournamentRecords.Add(SwordG1(9));
            return HonorSystem.HallOfFameReason(state, daughter);
        }

        [Fact]
        public void HallOfFame_AdvisorBonusTimes1_1()
        {
            var trainer = Make("師匠", 80, 26);
            double normal = AdvisorSystem.GetTrainerBonus(trainer, FacilityType.DrillHall);
            var state = NewGame();
            state.RetiredAdventurers.Add(trainer);
            state.AssignedAdvisor = trainer.Id;
            double survey = AdvisorSystem.GetAdvisorSurveyIntelBonus(state);
            double scout = AdvisorSystem.GetScoutMasterBonus(trainer);

            trainer.HallOfFameYear = 5;

            Assert.Equal(normal * HonorBalance.HallOfFameAdvisorMultiplier, AdvisorSystem.GetTrainerBonus(trainer, FacilityType.DrillHall), 9);
            Assert.Equal(survey * HonorBalance.HallOfFameAdvisorMultiplier, AdvisorSystem.GetAdvisorSurveyIntelBonus(state), 9);
            Assert.Equal(scout * HonorBalance.HallOfFameAdvisorMultiplier, AdvisorSystem.GetScoutMasterBonus(trainer), 9);
        }

        // ---------------- 関係タグ ----------------

        [Fact]
        public void Relations_TagsAndOrder()
        {
            var a = Make("A");
            var buddy = Make("戦友");
            var pair = Make("相棒");
            var stranger = Make("他人");
            var mother = Make("母");
            a.ParentIds.Add(mother.Id);
            var state = NewGame(a, buddy, pair, stranger, mother);
            state.Compatibility[CompatibilitySystem.NormalizeKey(a.Id, buddy.Id)] = HonorBalance.BuddyCompatibility;
            state.Compatibility[CompatibilitySystem.NormalizeKey(a.Id, pair.Id)] = HonorBalance.PerfectPairCompatibility;
            state.Compatibility[CompatibilitySystem.NormalizeKey(a.Id, stranger.Id)] = 0;
            for (int i = 0; i < HonorBalance.BuddyBossKills; i++)
            {
                var id = Guid.NewGuid();
                a.BossKills.Add(new BossKillRecord { BossId = id });
                buddy.BossKills.Add(new BossKillRecord { BossId = id });
            }

            var relations = HonorSystem.GetRelations(state, a, max: 10);

            Assert.Equal(new[] { HonorSystem.Buddy }, relations.Single(r => r.OtherId == buddy.Id).Tags);
            Assert.Equal(new[] { HonorSystem.PerfectPair }, relations.Single(r => r.OtherId == pair.Id).Tags);
            Assert.Equal(new[] { HonorSystem.MotherDaughter }, relations.Single(r => r.OtherId == mother.Id).Tags);
            Assert.DoesNotContain(relations, r => r.OtherId == stranger.Id);
            Assert.Equal(pair.Id, relations[0].OtherId); // タグの数が同じなら相性の高い順
            Assert.Equal(HonorBalance.RelationsShown, HonorSystem.GetRelations(state, a).Count);
        }

        [Fact]
        public void Relations_BuddyNeedsSharedKills()
        {
            var a = Make("A");
            var b = Make("B");
            var state = NewGame(a, b);
            state.Compatibility[CompatibilitySystem.NormalizeKey(a.Id, b.Id)] = HonorBalance.BuddyCompatibility;
            Assert.Empty(HonorSystem.GetRelations(state, a).Single().Tags);
        }

        // ---------------- 引退式 ----------------

        [Fact]
        public void Ceremony_FullVersion_WithWinsTitleHighlightsAndLine()
        {
            var a = Make("A", 60, 25);
            var state = NewGame(a);
            HonorSystem.ProcessWeek(state, Array.Empty<TournamentEvent>(), Array.Empty<SoulFusionCulture>());
            a.TournamentRecords.AddRange(new[] { SwordG1(2), MagicG1(3) });
            for (int i = 0; i < 12; i++)
                a.Journal.Add(new JournalEntry { Week = 10 + i, Kind = JournalKind.BossMvp, Text = $"主役{i}" });
            a.ActiveWeeks = GameCalendar.WeeksPerYear * 8;
            state.WeekNumber = 30;
            new AgingSystem(new SeededRng(1)).RetireVoluntarily(state, a);

            var c = HonorSystem.BuildCeremony(state, a);

            Assert.False(c.IsShort);
            Assert.True(c.HallOfFame);
            Assert.Equal(8, c.YearsActive);
            Assert.Equal(2, c.Wins.Count);
            Assert.StartsWith("2年目 王国剣闘祭", c.Wins[0]);
            Assert.Equal(HonorBalance.CeremonyHighlights, c.Highlights.Count);
            Assert.Contains(c.Highlights, e => e.Kind == JournalKind.HallOfFame);
            Assert.Contains(c.Highlights, e => e.Kind == JournalKind.Joined);
            Assert.True(c.Highlights.Select(e => e.Week).SequenceEqual(c.Highlights.Select(e => e.Week).OrderBy(w => w))); // 週の順
            Assert.Contains(c.AlbertLine, HonorBalance.RetirementLines["HallOfFame"].Select(l => l.Replace("{name}", "A").Replace("{title}", c.Title)));
            Assert.Equal(7, c.PeakStats.Count);
            Assert.False(string.IsNullOrEmpty(c.TrueRank));
        }

        [Fact]
        public void Ceremony_AlwaysHasJoinedAndRetired_AndCapsTitleLines()
        {
            var a = Make("A", 60, 25);
            var state = NewGame(a);
            HonorSystem.ProcessWeek(state, Array.Empty<TournamentEvent>(), Array.Empty<SoulFusionCulture>());
            for (int i = 0; i < 6; i++)
                a.Journal.Add(new JournalEntry { Week = 10 + i, Kind = JournalKind.Title, Text = $"二つ名{i}" });
            for (int i = 0; i < 3; i++)
                a.Journal.Add(new JournalEntry { Week = 20 + i, Kind = JournalKind.BigWin, Text = $"G1{i}" });
            state.WeekNumber = 40;
            new AgingSystem(new SeededRng(1)).RetireVoluntarily(state, a);

            var c = HonorSystem.BuildCeremony(state, a);

            Assert.Equal(2 + 3 + HonorBalance.CeremonyTitleLines, c.Highlights.Count); // 加入・引退＋G1×3＋二つ名は2行まで
            Assert.Equal(JournalKind.Joined, c.Highlights[0].Kind);
            Assert.Contains(c.Highlights, e => e.Kind == JournalKind.Retired);
            Assert.Equal(new[] { "二つ名5", "二つ名4" }.OrderBy(t => t), c.Highlights.Where(e => e.Kind == JournalKind.Title).Select(e => e.Text).OrderBy(t => t));
        }

        [Fact]
        public void Ceremony_ShortVersion_ForFewRecords()
        {
            var a = Make("A", 40, 25);
            var state = NewGame(a);
            new AgingSystem(new SeededRng(1)).RetireVoluntarily(state, a);

            var c = HonorSystem.BuildCeremony(state, a);

            Assert.True(c.IsShort);
            Assert.False(c.HallOfFame);
            Assert.Equal("", c.Title);
            Assert.Contains(c.AlbertLine, HonorBalance.RetirementLines["Short"].Select(l => l.Replace("{name}", "A")));
        }

        // ---------------- 週の決算とセーブ ----------------

        [Fact]
        public void WeekProcessing_YearEndRetirement_ListsRetiree_AndNotifiesHallOfFame()
        {
            var a = Make("A", 60, 25);
            var state = NewGame(a);
            a.TournamentRecords.AddRange(new[] { SwordG1(1), MagicG1(1) });
            a.EarnedTitleIds.AddRange(HonorSystem.GetTitles(state, a).Select(t => t.Key));
            state.WeekNumber = GameCalendar.WeeksPerYear;
            TournamentSystem.EnsureSchedule(state);

            var result = Week().ProcessWeek(state);

            Assert.Equal(new[] { a.Id }, result.Retirees);
            Assert.Contains(result.HonorNotices, n => n.Kind == JournalKind.HallOfFame && n.AdventurerId == a.Id);
        }

        [Fact]
        public void Save_RoundTripsHonorRecords()
        {
            var a = Make("A");
            var state = NewGame(a);
            a.BossKills.Add(new BossKillRecord { BossId = Guid.NewGuid(), BossName = "主", Floor = 10, IsMvp = true });
            a.PeakStats["STR"] = 77;
            a.JoinEstimate = "B?";
            a.Journal.Add(new JournalEntry { Week = 3, Kind = JournalKind.BigWin, Text = "勝った", RelatedId = Guid.NewGuid() });
            a.EarnedTitleIds.Add("rookie_king");
            a.MentorWeeks[Guid.NewGuid()] = 9;
            a.HallOfFameYear = 4;
            a.HallOfFameReason = "G1を2勝";
            a.TournamentRecords.Add(SwordG1(2, week: 90));

            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(state.ToSaveData()))!).Adventurers.Single();

            Assert.True(restored.BossKills.Single().IsMvp);
            Assert.Equal(77, restored.PeakStats["STR"]);
            Assert.Equal("B?", restored.JoinEstimate);
            Assert.Equal(JournalKind.BigWin, restored.Journal.Single().Kind);
            Assert.Equal(new[] { "rookie_king" }, restored.EarnedTitleIds);
            Assert.Equal(9, restored.MentorWeeks.Values.Single());
            Assert.Equal(4, restored.HallOfFameYear);
            Assert.Equal("G1を2勝", restored.HallOfFameReason);
            Assert.Equal(90, restored.TournamentRecords.Single().Week);
        }

        [Fact]
        public void Load_OldSaveWithoutHonorFields_FillsEmpty()
        {
            var state = NewGame(Make("A"));
            var json = JsonSerializer.Serialize(state.ToSaveData());
            // 段2より前のセーブ：戦績の項目が null で書かれていても読める
            foreach (var key in new[] { "BossKills", "PeakStats", "Journal", "EarnedTitleIds", "MentorWeeks" })
                json = System.Text.RegularExpressions.Regex.Replace(json, $"\"{key}\":(\\[\\]|\\{{\\}})", $"\"{key}\":null");
            json = json.Replace("\"JoinEstimate\":\"\"", "\"JoinEstimate\":null").Replace("\"HallOfFameReason\":\"\"", "\"HallOfFameReason\":null");
            Assert.Contains("\"Journal\":null", json);

            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!).Adventurers.Single();

            Assert.Empty(restored.BossKills);
            Assert.Empty(restored.PeakStats);
            Assert.Empty(restored.Journal);
            Assert.Empty(restored.EarnedTitleIds);
            Assert.Empty(restored.MentorWeeks);
            Assert.Equal("", restored.JoinEstimate);
            Assert.Equal("", restored.HallOfFameReason);
            Assert.Null(restored.HallOfFameYear);
        }
    }
}
