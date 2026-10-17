using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>出来事の報告（§0.141、→ EventReportSystem・event_lines.csv）：月の知らせを人物に話させる。</summary>
    public class EventReportTests
    {
        private static GameState NewGame(bool lumina = true)
        {
            var state = new GameState { WeekNumber = 9, Gold = 1000, MasterMood = 60, DungeonFields = SampleData.CreateDefaultFields() };
            state.Adventurers.AddRange(SampleData.CreateStarterAdventurers());
            state.Adventurers.Add(new Adventurer { Name = "新人", Age = 18, Personality = new Personality { Temperament = -1 } });
            if (lumina) StorySystem.MarkSeen(state, "s01_lumina");
            return state;
        }

        private static List<EventReport> Build(GameState state, WeeklySettlementResult settlement) =>
            EventReportSystem.Build(state, new[] { new AutoSkipWeek(new List<SquadOrderEvent>(), settlement) });

        private static Adventurer Named(GameState state, string name) => state.Adventurers.First(a => a.Name == name);

        [Fact]
        public void Lines_LoadAndEveryRowHasText()
        {
            var keys = EventReportBalance.Keys.ToList();
            Assert.NotEmpty(keys);
            foreach (var (kind, speaker) in keys)
                Assert.All(EventReportBalance.Get(kind, speaker), l => Assert.False(string.IsNullOrWhiteSpace(l.Text), $"{kind}/{speaker}"));
            // 当人が話す知らせには、中庸の台詞が必ずある（気質の台詞が無いときの代わり）
            foreach (var kind in new[] { "Mentor", "Insight", "Treat", "Pumped", "LoanReturn", "Fret", "Poach", "Charge", "Flee", "Bond", "Guard", "Quarrel", "Refuse", "TournamentWin", "ExchangeWon", "ExchangeLost" })
                Assert.NotEmpty(EventReportBalance.Get(kind, "temper:normal"));
        }

        [Fact]
        public void Bond_IsToldByTheAdventurer_WithThePartnerFilledIn()
        {
            var state = NewGame();
            var fiona = Named(state, "フィオナ");
            var rina = Named(state, "リナ");
            var settlement = new WeeklySettlementResult();
            settlement.Incidents.Add(new IncidentEvent(IncidentKind.Bond, "🌸", true, fiona.Id, rina.Id));
            var report = Assert.Single(Build(state, settlement));
            Assert.Equal("Bond", report.Kind);
            Assert.Equal(fiona.Id, report.SubjectId);
            Assert.Contains("リナさん", report.Line); // フィオナの専用の台詞
            Assert.DoesNotContain("{", report.Line);
            var lines = EventReportSystem.Lines(report);
            Assert.All(lines.Where(l => l.Kind == StoryLineKind.Speech), l => Assert.Equal("フィオナ", l.Speaker));
        }

        [Fact]
        public void PersonalityKnown_IsToldOnceEverythingIsSure_WithTheMainDrive()
        {
            // §0.151：志向（主・副・苦手）と気質がすべて確かになった週に、当人が打ち明ける
            var state = NewGame();
            var rookie = Named(state, "新人");
            rookie.Personality!.Main = Drive.Gather;
            int sure = PersonalityBalance.RevealSure;
            rookie.Personality.Exposure["Gather"] = sure;
            var settlement = new WeeklySettlementResult();
            settlement.PersonalityDiscoveries.Add(new PersonalityDiscovery(rookie, "Gather", PersonalityKnowledge.Sure));
            Assert.Empty(Build(state, settlement)); // 気質はまだ確かでない

            rookie.Personality.Exposure[PersonalitySystem.TemperamentKey] = sure;
            settlement.PersonalityDiscoveries.Add(new PersonalityDiscovery(rookie, PersonalitySystem.TemperamentKey, PersonalityKnowledge.Sure));
            var report = Assert.Single(Build(state, settlement));
            Assert.Equal("PersonalityKnown", report.Kind);
            Assert.Equal(rookie.Id, report.SubjectId);
            Assert.Contains("素材を集めて歩くこと", report.Line); // 主志向の言い回し
            Assert.Contains("無理をしない", report.Line); // 慎重の台詞
            Assert.DoesNotContain("{", report.Line);
        }

        [Fact]
        public void MasterMood_TierChange_IsToldByAlbert_OncePerSeasonPerDirection()
        {
            // §0.151：機嫌の段階が前の月報から変わったら、アルベールが話す。同じ向きは季節に1回まで、危機はいつも
            var state = NewGame();
            state.WeekNumber = 5;
            state.MasterMood = 60; // 平常
            Assert.Empty(Build(state, new WeeklySettlementResult())); // 初めて比べる月は覚えるだけ

            state.MasterMood = 85;
            var up = Assert.Single(Build(state, new WeeklySettlementResult()));
            Assert.Equal("MoodUp:Cheerful", up.Kind);
            Assert.Contains(EventReportSystem.Lines(up), l => l.Speaker == "アルベール");

            state.MasterMood = 70;
            Assert.Equal("MoodDown:Normal", Assert.Single(Build(state, new WeeklySettlementResult())).Kind);
            state.MasterMood = 85;
            Assert.Empty(Build(state, new WeeklySettlementResult())); // 同じ季節にもう一度上がっても言わない
            state.MasterMood = 70;
            Assert.Empty(Build(state, new WeeklySettlementResult()));
            state.MasterMood = 10;
            var crisis = Assert.Single(Build(state, new WeeklySettlementResult())); // 危機はいつも
            Assert.Equal("MoodDown:Crisis", crisis.Kind);
            Assert.NotEmpty(crisis.Jump);

            state.WeekNumber += GameCalendar.WeeksPerSeason; // 次の季節
            state.MasterMood = 85;
            Assert.Equal("MoodUp:Cheerful", Assert.Single(Build(state, new WeeklySettlementResult())).Kind);
        }

        [Fact]
        public void Notices_NewOnes_AreToldByLumina_OncePerAppearance()
        {
            // §0.151：判断が要る注意は、前の月報に無かったものだけルミナが行き先つきで知らせる（2人の不満は2件）
            var state = NewGame();
            var fiona = Named(state, "フィオナ");
            var rina = Named(state, "リナ");
            MonthlyNote Discontent(Adventurer a) => new() { Text = "不満", Key = $"Discontent:{a.Id}", Values = { ["{名前}"] = a.Name, ["{直し方}"] = "休ませてあげてください" } };
            var notes = new List<MonthlyNote> { Discontent(fiona), Discontent(rina), new() { Text = "毒", Key = "" } };
            var reports = EventReportSystem.Build(state, new List<AutoSkipWeek>(), notes);
            Assert.Equal(2, reports.Count(r => r.Kind == "Notice:Discontent"));
            Assert.All(reports, r => Assert.NotEmpty(r.Jump));
            Assert.Contains(reports, r => r.Line.Contains("フィオナさんの元気") && r.Line.Contains("休ませて"));

            state.MonthlyReports.Add(new MonthlyReport { MonthCompleted = true, Notes = { Discontent(fiona) } });
            var next = EventReportSystem.Build(state, new List<AutoSkipWeek>(), notes);
            var only = Assert.Single(next); // フィオナは先月も出ていたので話さない
            Assert.Contains("リナさん", only.Line);
        }

        [Fact]
        public void TournamentPlace_RunnerUpAnyGrade_Best4OnlyAtG1()
        {
            // §0.151：準優勝はどの格でも、ベスト4は G1・特別だけ当人が話す
            var state = NewGame();
            var rina = Named(state, "リナ");
            var fiona = Named(state, "フィオナ");
            var settlement = new WeeklySettlementResult();
            settlement.TournamentsResolved.Add(new TournamentEvent { Name = "森の弓比べ", Kind = TournamentKind.Local, Grade = TournamentGrade.G3,
                Result = new TournamentResult { Placings = { new TournamentPlacing { AdventurerId = rina.Id, Placing = 2 }, new TournamentPlacing { AdventurerId = fiona.Id, Placing = 4 } } } });
            var report = Assert.Single(Build(state, settlement));
            Assert.Equal("TournamentPlace", report.Kind);
            Assert.Contains("森の弓比べ、準優勝", report.Line); // リナの専用の台詞

            settlement.TournamentsResolved[0].Grade = TournamentGrade.G1;
            Assert.Equal(2, Build(state, settlement).Count(r => r.Kind == "TournamentPlace"));
        }

        [Fact]
        public void Temperament_PicksTheLines_AndFallsBackToNormal()
        {
            var state = NewGame();
            var rookie = Named(state, "新人"); // 慎重
            var settlement = new WeeklySettlementResult();
            settlement.Incidents.Add(new IncidentEvent(IncidentKind.Charge, "⚔", false, rookie.Id)); // 突っ込むのに慎重の台詞は無い → 中庸
            var report = Assert.Single(Build(state, settlement));
            Assert.Contains("体が勝手に動いちゃって", report.Line);
            Assert.Equal("大迷宮（扉前の構えを見直す）", report.Jump);
        }

        [Fact]
        public void LuminaLines_AreDroppedBeforeTheRescue_AndSponsorUsesAlbert()
        {
            var state = NewGame(lumina: false);
            var settlement = new WeeklySettlementResult();
            settlement.Sponsors.Completed.Add(new SponsorContract { SponsorName = "辺境伯家", Bonus = 900 });
            settlement.Incidents.Add(new IncidentEvent(IncidentKind.Gloom, "😞", false, Named(state, "リナ").Id)); // ルミナだけが話す知らせ → 出さない
            var report = Assert.Single(Build(state, settlement));
            Assert.Equal("SponsorDone", report.Kind);
            Assert.Contains("お金の話は、あなたに任せるわ", report.Line);
            Assert.Contains("900G", report.Line);

            var withLumina = NewGame();
            var again = new WeeklySettlementResult();
            again.Sponsors.Completed.Add(new SponsorContract { SponsorName = "辺境伯家", Bonus = 900 });
            again.Incidents.Add(new IncidentEvent(IncidentKind.Gloom, "😞", false, Named(withLumina, "リナ").Id));
            var lines = Build(withLumina, again);
            Assert.Contains(lines, r => r.Kind == "SponsorDone" && r.Line.Contains("ルミナ「"));
            Assert.Contains(lines, r => r.Kind == "Gloom" && r.Line.Contains("リナさんが、部屋から出てきません"));
        }

        [Fact]
        public void Research_UsesTheSpecialLinesForTheElixir_AndTheGeneralOnesOtherwise()
        {
            var state = NewGame();
            var soul = ResearchBalance.GetAll().First(r => r.Id == "res_soul_fusion");
            var settlement = new WeeklySettlementResult { CompletedResearch = soul };
            var report = Assert.Single(Build(state, settlement));
            Assert.Equal("ResearchDone:res_soul_fusion", report.Kind);

            var other = ResearchBalance.GetAll().First(r => r.Id == "res_herb_poultice");
            var general = Assert.Single(Build(state, new WeeklySettlementResult { CompletedResearch = other }));
            Assert.Equal("ResearchDone", general.Kind);
            Assert.Contains(other.Name, general.Line);
            Assert.Equal("研究室（次の研究を選ぶ）", general.Jump);
        }

        [Fact]
        public void Intro_OnlyWhenTheSceneComesThisMonth()
        {
            var state = NewGame();
            var settlement = new WeeklySettlementResult();
            settlement.SquadSlotsExpandedTo = 3; // 3つ目の部隊（場面「三つ目の部隊」がこの月に出る）
            state.DungeonFields.First(f => f.Id == "forest").Bosses.First(b => b.Floor == 20).IsDefeated = true;
            state.UnlockedSquadSlots = 3;
            var intro = Assert.Single(Build(state, settlement));
            Assert.Equal("Intro:Slot3", intro.Kind);

            StorySystem.MarkSeen(state, "s01_squad3"); // 場面を見たあと（手ほどき「なし」など）は出さない
            Assert.Empty(Build(state, settlement));
        }

        [Fact]
        public void Bond_IsToldOnlyTheFirstTimeForThePair()
        {
            // 休日の絆は、その2人の初めてのときだけ話す。2回目からは月報の行だけ（§0.148）
            var state = NewGame();
            var fiona = Named(state, "フィオナ");
            var rina = Named(state, "リナ");
            var settlement = new WeeklySettlementResult();
            settlement.Incidents.Add(new IncidentEvent(IncidentKind.Bond, "🌸", true, fiona.Id, rina.Id));
            Assert.Equal("Bond", Assert.Single(Build(state, settlement)).Kind);
            Assert.Empty(Build(state, settlement));
            var reversed = new WeeklySettlementResult();
            reversed.Incidents.Add(new IncidentEvent(IncidentKind.Bond, "🌸", true, rina.Id, fiona.Id)); // 並びが逆でも同じ組
            Assert.Empty(Build(state, reversed));
        }

        [Fact]
        public void FirstYearTop_SceneWithRivalLetters_AndTheIntroReplacesTheRankLine()
        {
            // 年末の順位で初めて1位：導入の一言のあとに場面（イザベラ）。手紙の行はそのギルドが王都にいるときだけ（§0.145）
            var state = NewGame();
            var standing = new GuildStanding { Year = 3, Rows = { new GuildStandingRow { Ours = true, Rank = 1, Name = "わがギルド" }, new GuildStandingRow { GuildId = "whitelily", Rank = 2 } } };
            state.GuildStandings.Add(standing);
            StorySystem.MarkSeen(state, "s03_valeria"); // ヴァレリアには会った（シルヴィアにはまだ）
            var settlement = new WeeklySettlementResult { YearStanding = standing };
            Assert.Equal("Intro:YearTop", Assert.Single(Build(state, settlement)).Kind);

            var texts = StorySystem.DueScenes(state, StoryTiming.AfterReport).Single(s => s.SceneId == "s03_yeartop").Pages.SelectMany(p => p).Select(l => l.Text).ToList();
            Assert.Contains(texts, t => t.Contains("ヴァレリアさん"));
            Assert.DoesNotContain(texts, t => t.Contains("シルヴィアさん"));
            Assert.Contains(texts, t => t.Contains("らしいわね"));
            Assert.DoesNotContain(texts, t => t.Contains("{"));

            StorySystem.MarkSeen(state, "s03_yeartop"); // 2回目からは、ふつうの順位の一言
            Assert.Equal("YearRank", Assert.Single(Build(state, settlement)).Kind);
        }

        [Fact]
        public void Order_DecisionsFirst_ThenAdventurers_ThenLetters_AndSameEventOncePerMonth()
        {
            var state = NewGame();
            var rina = Named(state, "リナ");
            var settlement = new WeeklySettlementResult();
            settlement.Sponsors.Completed.Add(new SponsorContract { SponsorName = "商人", Bonus = 100 });
            settlement.Incidents.Add(new IncidentEvent(IncidentKind.Treat, "🍰", true, rina.Id));
            settlement.Incidents.Add(new IncidentEvent(IncidentKind.Boycott, "💤", false, rina.Id));
            settlement.Incidents.Add(new IncidentEvent(IncidentKind.Boycott, "💤", false, rina.Id)); // 同じ月にもう一度
            var kinds = Build(state, settlement).Select(r => r.Kind).ToList();
            Assert.Equal(new[] { "Boycott", "Treat", "SponsorDone" }, kinds);
        }

        [Fact]
        public void Insight_WithoutRoomToGrow_AddsTheLine()
        {
            var state = NewGame();
            var settlement = new WeeklySettlementResult();
            settlement.Incidents.Add(new IncidentEvent(IncidentKind.Insight, "💡", true, Named(state, "新人").Id));
            var report = Assert.Single(Build(state, settlement));
            Assert.EndsWith("「……でも、体がついてこないみたい」", report.Line);
        }

        [Fact]
        public void Lines_SplitsNamedSpeakersAndNarration()
        {
            var report = new EventReport { SubjectName = "リナ", Line = "〔扉が開く〕｜アルベール「ふふ」｜ルミナ「はい」｜「ただいま」" };
            var lines = EventReportSystem.Lines(report);
            Assert.Equal(StoryLineKind.Narration, lines[0].Kind);
            Assert.Equal(("アルベール", "ふふ"), (lines[1].Speaker, lines[1].Text));
            Assert.Equal(("ルミナ", "はい"), (lines[2].Speaker, lines[2].Text));
            Assert.Equal(("リナ", "ただいま"), (lines[3].Speaker, lines[3].Text));
        }

        [Fact]
        public void MonthlyReport_KeepsTheEventReports_AndTheSettingIsSaved()
        {
            var state = NewGame();
            var settlement = new WeeklySettlementResult();
            settlement.Incidents.Add(new IncidentEvent(IncidentKind.Pumped, "🔥 リナが張り切っている", true, Named(state, "リナ").Id));
            var report = MonthlyReport.Build(state, new[] { new AutoSkipWeek(new List<SquadOrderEvent>(), settlement) }, state.Gold);
            Assert.Contains(report.Highlights, h => h.Text.Contains("張り切っている")); // 月報の行も残す
            Assert.Single(report.EventReports);
            MonthlyReport.Record(state, report);
            state.HideEventReports = true;

            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(state.ToSaveData()))!);
            Assert.True(restored.HideEventReports);
            Assert.Equal("Pumped", restored.MonthlyReports.Last().EventReports.Single().Kind);
        }
    }
}
