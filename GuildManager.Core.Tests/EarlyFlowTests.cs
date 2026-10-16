using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 序盤の流れ（§0.140、→ docs/検討中_序盤の流れ.md）：資金の画面はイザベラの帳簿で開く・それまでの赤字はアルベールのへそくり（1回）→ イザベラの早めの来訪、
    /// スポンサーは帳簿のあとの季節から、序盤の場面（へそくり・新春・初めての契約交渉・帳簿・迷宮の主の顔見せ）のきっかけ、旧セーブの扱い。
    /// </summary>
    public class EarlyFlowTests
    {
        private static GameState NewGame(int week = 5, int gold = 1000)
        {
            var state = new GameState { WeekNumber = week, Gold = gold, MasterMood = 60, DungeonFields = SampleData.CreateDefaultFields() };
            state.Adventurers.AddRange(SampleData.CreateStarterAdventurers());
            FundingSystem.CloseForNewGame(state);
            return state;
        }

        private static void Defeat(GameState state, string fieldId, int floor)
        {
            var field = state.DungeonFields.First(f => f.Id == fieldId);
            field.IsUnlocked = true;
            foreach (var b in field.Bosses.Where(b => b.Floor <= floor)) b.IsDefeated = true;
        }

        private static string[] Due(GameState state) => StorySystem.DueScenes(state, StoryTiming.AfterReport).Select(s => s.SceneId).ToArray();

        private static string[] Texts(GameState state, string sceneId) =>
            StorySystem.DueScenes(state, StoryTiming.AfterReport).Single(s => s.SceneId == sceneId).Pages.SelectMany(p => p).Select(l => l.Text).ToArray();

        // ---------------- 資金の画面 ----------------

        [Fact]
        public void NewGame_FundingIsClosed_NoLoansAndNoSponsors()
        {
            var state = NewGame(week: FundingBalance.SponsorFirstWeek);
            Assert.False(FundingSystem.IsOpen(state));
            Assert.NotNull(FundingSystem.MerchantLoanBlockReason(state, FundingBalance.MerchantLoanAmounts[0]));
            Assert.False(FundingSystem.Borrow(state, LoanLender.Merchant, FundingBalance.MerchantLoanAmounts[0]));
            Assert.Empty(FundingSystem.ProcessNewWeek(state, new SeededRng(1)));
        }

        [Fact]
        public void NoTutorial_FundingIsOpenFromTheStart_SponsorsFromTheFirstAutumn()
        {
            var state = NewGame(week: FundingBalance.SponsorFirstWeek);
            StorySystem.DisableTutorial(state);
            Assert.True(FundingSystem.IsOpen(state));
            Assert.NotEmpty(FundingSystem.ProcessNewWeek(state, new SeededRng(1)));
            Assert.Null(FundingSystem.ProcessEarlyDeficit(state)); // 手当ては要らない（借りられる）
        }

        [Fact]
        public void Ledger_OpensAtTheStartOfTheMonthAfterTheTournamentCalendar()
        {
            var state = NewGame(week: 9);
            state.TournamentCalendarFromWeek = 9; // 3か月目から大会の暦
            for (int w = 9; w <= 12; w++)
            {
                state.WeekNumber = w;
                Assert.False(FundingSystem.ProcessFundingOpen(state));
            }
            state.WeekNumber = 13;
            Assert.True(FundingSystem.ProcessFundingOpen(state));
            Assert.Equal(13, state.FundingOpenedWeek);
            Assert.True(FundingSystem.IsOpen(state));
            Assert.False(FundingSystem.ProcessFundingOpen(state)); // 二度は開かない
        }

        [Fact]
        public void Sponsors_ComeTheSeasonAfterTheLedger()
        {
            var state = NewGame(week: FundingBalance.SponsorFirstWeek);
            state.FundingOpenedWeek = FundingBalance.SponsorFirstWeek; // 帳簿が秋のはじめと同じ週
            Assert.Empty(FundingSystem.ProcessNewWeek(state, new SeededRng(1)));
            state.WeekNumber = FundingBalance.SponsorFirstWeek + GameCalendar.WeeksPerSeason * 2; // 次の春のはじめ
            Assert.NotEmpty(FundingSystem.ProcessNewWeek(state, new SeededRng(1)));
        }

        // ---------------- 赤字の手当て ----------------

        [Fact]
        public void FirstDeficit_AlbertBailsOutOnce_ThenIsabellaComesEarly()
        {
            var state = NewGame(gold: -100);
            var first = FundingSystem.ProcessEarlyDeficit(state);
            Assert.NotNull(first);
            Assert.Equal(FundingBalance.AlbertBailoutGold, first!.BailoutGold);
            Assert.False(first.EarlyVisit);
            Assert.Equal(-100 + FundingBalance.AlbertBailoutGold, state.Gold);
            Assert.Equal(state.WeekNumber, state.AlbertBailoutWeek);
            Assert.False(FundingSystem.IsOpen(state));
            Assert.False(IsabellaSystem.HasVisited(state));

            state.WeekNumber += 3;
            state.Gold = -50;
            var second = FundingSystem.ProcessEarlyDeficit(state);
            Assert.NotNull(second);
            Assert.Equal(0, second!.BailoutGold); // へそくりは一度だけ
            Assert.True(second.EarlyVisit);
            Assert.True(second.FundingOpened);
            Assert.True(state.IsabellaEarlyVisit);
            Assert.Equal(state.WeekNumber, state.IsabellaVisitWeek);
            Assert.True(FundingSystem.IsOpen(state)); // 借金で立て直せる
            Assert.Single(state.TournamentEvents, e => e.Kind == TournamentKind.Exchange); // 交流戦はいつもどおり次の月
            Assert.Null(FundingSystem.ProcessEarlyDeficit(state)); // 開いたあとは手当てしない
        }

        [Fact]
        public void BailoutNotEnough_IsabellaComesTheSameWeek()
        {
            var state = NewGame(gold: -(FundingBalance.AlbertBailoutGold + 200));
            var relief = FundingSystem.ProcessEarlyDeficit(state);
            Assert.Equal(FundingBalance.AlbertBailoutGold, relief!.BailoutGold);
            Assert.True(relief.EarlyVisit);
            Assert.True(FundingSystem.IsOpen(state));
        }

        [Fact]
        public void DeficitAfterTheVisitBeforeTheLedger_OpensFundingWithoutAnotherVisit()
        {
            var state = NewGame();
            Defeat(state, "forest", IsabellaBalance.VisitFloor);
            Assert.True(IsabellaSystem.CheckVisit(state));
            state.AlbertBailoutWeek = 3;
            state.Gold = -10;
            var relief = FundingSystem.ProcessEarlyDeficit(state);
            Assert.False(relief!.EarlyVisit);
            Assert.True(relief.FundingOpened);
            Assert.False(state.IsabellaEarlyVisit);
        }

        [Fact]
        public void Settlement_BailoutKeepsTheGuildFromCountingTheWeekAsNegative_AndStopsTheMonth()
        {
            var state = NewGame(gold: 0); // 週給で所持金がマイナスになる
            var result = TestWeek().ProcessWeek(state);
            Assert.NotNull(result.DeficitRelief);
            Assert.True(result.Flags.DeficitRelieved);
            Assert.True(result.Flags.ShouldStopMonth);
            Assert.True(state.Gold >= 0);
            Assert.Equal(0, state.ConsecutiveNegativeGoldWeeks);
            Assert.False(result.Flags.GoldTurnedNegative);
        }

        private static WeekProcessingSystem TestWeek() =>
            new WeekProcessingSystem(
                new MasterMoodSystem(), new EconomySystem(), new TrainingSystem(), new InjuryRecoverySystem(), new RestRecoverySystem(),
                new GrowthSystem(new SeededRng(7)), new SatisfactionSystem(), new AgingSystem(new SeededRng(7)), new FacilitySystem(), new DefeatSystem(),
                new RecruitmentSystem(new SeededRng(7)));

        // ---------------- 場面 ----------------

        [Fact]
        public void BailoutScene_FillsTheAmount_LuminaLinesOnlyAfterTheRescue()
        {
            var state = NewGame(gold: -10);
            Assert.DoesNotContain("s01_bailout", Due(state));
            FundingSystem.ProcessEarlyDeficit(state);
            var texts = Texts(state, "s01_bailout");
            Assert.Contains(texts, t => t.Contains($"{FundingBalance.AlbertBailoutGold}G"));
            Assert.DoesNotContain(texts, t => t.Contains("試薬を買うためのお金")); // ルミナの行（救出の前は出さない）
            StorySystem.MarkSeen(state, "s01_lumina");
            Assert.Contains(Texts(state, "s01_bailout"), t => t.Contains("試薬を買うためのお金"));
        }

        [Fact]
        public void VisitScene_UsesTheForestLines_OrTheRumorLinesForTheEarlyVisit()
        {
            var forest = NewGame();
            Defeat(forest, "forest", IsabellaBalance.VisitFloor);
            IsabellaSystem.CheckVisit(forest);
            var texts = Texts(forest, "s02_visit");
            Assert.Contains(texts, t => t.Contains("森の40階を抜けたとか"));
            Assert.DoesNotContain(texts, t => t.Contains("お給金の払い"));

            var early = NewGame(gold: -10);
            early.AlbertBailoutWeek = 2;
            FundingSystem.ProcessEarlyDeficit(early);
            var earlyTexts = Texts(early, "s02_visit");
            Assert.Contains(earlyTexts, t => t.Contains("お給金の払いにも困っているとか"));
            Assert.DoesNotContain(earlyTexts, t => t.Contains("森の40階"));
            Assert.Contains("s02_ledger", Due(early)); // 帳簿も同じ月に
            Assert.Contains(Texts(early, "s02_ledger"), t => t.Contains("赤字、ですって"));
        }

        [Fact]
        public void LedgerScene_OnlyWhenTheLedgerOpenedInThisGame()
        {
            var state = NewGame();
            Assert.DoesNotContain("s02_ledger", Due(state));
            state.FundingOpenedWeek = 13;
            Assert.Contains("s02_ledger", Due(state));

            var old = NewGame();
            old.FundingOpenedWeek = 1; // 旧セーブ・手ほどき「なし」（初めから開いていた）
            Assert.DoesNotContain("s02_ledger", Due(old));
        }

        [Fact]
        public void FaceReveals_AfterThe30FOfEachField_OnlyAfterTheRescue()
        {
            var state = NewGame();
            // 救出の前（20Fと30Fを同じ月に倒したときは、ルミナの場面のすぐあとに続けて出る＝DueScenes の並び）
            state.DungeonFields.First(f => f.Id == "forest").Bosses.First(b => b.Floor == 30).IsDefeated = true;
            Assert.DoesNotContain("s05_face1", Due(state));
            Defeat(state, "forest", 30);
            StorySystem.MarkSeen(state, "s01_lumina");
            Assert.Contains("s05_face1", Due(state));
            Assert.DoesNotContain("s05_face2", Due(state));
            Defeat(state, "cave", 30);
            Assert.Contains("s05_face2", Due(state));
            Assert.Contains(Texts(state, "s05_face1"), t => t.Contains("古代エルフの都の記録"));
        }

        [Fact]
        public void Hunter_NotBeforeTheRescue()
        {
            var state = NewGame();
            var field = state.DungeonFields.First(f => f.Id == "forest");
            var boss = field.Bosses.First(b => b.Floor == 20);
            for (int i = 0; i < 2; i++)
            {
                var result = new WeeklySettlementResult();
                result.DungeonMissionResolutions.Add(new DungeonMissionResolution(new Party(), boss, field, DungeonMissionType.Scouting)
                    { ArrivedAtBossDoor = true, DoorRetreatReason = "勝ち目が薄い" });
                StorySystem.ProcessWeek(state, result);
            }
            Assert.DoesNotContain("s01_hunter", Due(state));
        }

        [Fact]
        public void NewYear_InTheSecondYear_AndNegotiation_WithTheName()
        {
            var state = NewGame(week: GameCalendar.WeeksPerYear);
            Assert.DoesNotContain("s01_newyear", Due(state));
            state.WeekNumber = GameCalendar.WeeksPerYear + 1;
            Assert.Contains("s01_newyear", Due(state));
            Assert.DoesNotContain(Texts(state, "s01_newyear"), t => t.Contains("イザベラさまから")); // 来訪の前
            Defeat(state, "forest", IsabellaBalance.VisitFloor);
            IsabellaSystem.CheckVisit(state);
            Assert.Contains(Texts(state, "s01_newyear"), t => t.Contains("イザベラさまから"));

            Assert.DoesNotContain("s01_negotiation", Due(state));
            state.Adventurers[1].NeedsNegotiation = true;
            Assert.Contains(Texts(state, "s01_negotiation"), t => t.Contains($"{state.Adventurers[1].Name}さんから"));
        }

        // ---------------- セーブ ----------------

        [Fact]
        public void Save_RoundTrip_KeepsTheEarlyFlow()
        {
            var state = NewGame(gold: -10);
            state.AlbertBailoutWeek = 2;
            FundingSystem.ProcessEarlyDeficit(state);
            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(state.ToSaveData()))!);
            Assert.Equal(state.FundingOpenedWeek, restored.FundingOpenedWeek);
            Assert.Equal(2, restored.AlbertBailoutWeek);
            Assert.True(restored.IsabellaEarlyVisit);

            var closed = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(NewGame().ToSaveData()))!);
            Assert.False(FundingSystem.IsOpen(closed)); // 新しいゲームのセーブは閉じたまま
        }

        [Fact]
        public void OldSave_FundingIsOpen_AndPassedEarlyScenesAreSeen()
        {
            var state = NewGame(week: GameCalendar.WeeksPerYear * 2 + 3);
            StorySystem.MarkSeen(state, "s01_prologue");
            StorySystem.MarkSeen(state, "s01_lumina");
            Defeat(state, "forest", 50);
            var node = JsonNode.Parse(JsonSerializer.Serialize(state.ToSaveData()))!.AsObject();
            node.Remove("FundingOpenedWeek");
            node["StoryRulesVersion"] = 1;
            var old = GameState.FromSaveData(node.Deserialize<SaveData>()!);

            Assert.True(FundingSystem.IsOpen(old)); // §0.140より前は初めから開いていた
            Assert.True(StorySystem.Seen(old, "s01_newyear"));
            Assert.True(StorySystem.Seen(old, "s01_negotiation"));
            Assert.True(StorySystem.Seen(old, "s05_face1")); // 森の30Fはもう倒している
            Assert.False(StorySystem.Seen(old, "s05_face2"));
            var due = Due(old);
            Assert.DoesNotContain("s02_ledger", due);
            Assert.DoesNotContain("s05_face1", due);
        }
    }
}
