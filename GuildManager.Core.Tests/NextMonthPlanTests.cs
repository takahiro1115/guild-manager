using System;
using System.Linq;
using System.Text.Json;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// §0.98のテスト：来月の作戦（潜行の部隊が主に勝てずに戻った月に止め、月報で次の手を選ぶ。検証レポート GD-02）。
    /// 実行方法: `dotnet test GuildManager.Core.Tests --filter FullyQualifiedName~NextMonthPlan`
    /// </summary>
    public class NextMonthPlanTests
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

        /// <summary>
        /// 森（10Fの主は要求火力の高い主＝フィールド倍率を深い所のものにした）・草原（10Fの弱い主）と、森へ潜行を続ける2人部隊。
        /// 主の手前の9Fまでは潜行が済んでいる。2年目の夏 1の月 第2週から（採用試験・依頼の到着と重ならないように）。
        /// </summary>
        private static (GameState State, SavedParty Saved, FloorBoss Hard, FloorBoss Easy) Setup()
        {
            var hard = new FloorBoss { Name = "森の主", Floor = 10, FieldOrder = 6, MaxHp = 100, CurrentHp = 100 };
            var easy = new FloorBoss { Name = "草原の主", Floor = 10, FieldOrder = 1, MaxHp = 100, CurrentHp = 100 };
            var forest = new DungeonField { Id = "forest", Name = "森", Order = 1, IsUnlocked = true, ReachedFloor = 9, Bosses = { hard } };
            var plains = new DungeonField { Id = "plains", Name = "草原", Order = 2, IsUnlocked = true, ReachedFloor = 0, Bosses = { easy } };
            var a = Make("アリス", 45);
            var b = Make("セリア", 45);
            var saved = new SavedParty { Name = "主力", MemberIds = { a.Id, b.Id }, Order = SquadOrder.Dive, OrderFieldId = "forest" };
            var state = new GameState
            {
                Gold = 10000, MasterMood = 60, WeekNumber = GameCalendar.WeeksPerYear + 13 + 1,
                Adventurers = { a, b }, DungeonFields = { forest, plains }, SavedParties = { saved },
            };
            return (state, saved, hard, easy);
        }

        /// <summary>扉前で撤退した週（火力と要求は power・required、勝率はそこから求める）。</summary>
        private static WeeklySettlementResult DoorRetreat(FloorBoss boss, DungeonField field, Party party, double power, double required,
            string reason = "討伐火力が足りない見込み", int hp = 80)
        {
            var result = new WeeklySettlementResult();
            result.DungeonMissionResolutions.Add(new DungeonMissionResolution(party, boss, field, DungeonMissionType.Scouting)
            {
                ArrivedAtBossDoor = true, DoorRetreatReason = reason, DoorHpPercent = hp,
                DoorPower = power, DoorRequiredPower = required, DoorWinChance = DungeonResolver.WinChance(power, required),
            });
            return result;
        }

        private static Party PartyOf(GameState state, SavedParty saved)
        {
            var party = new Party();
            foreach (var id in saved.MemberIds) party.TryAdd(state.Adventurers.First(a => a.Id == id));
            return party;
        }

        [Fact]
        public void AdvanceMonth_StopsWhenTheDiveSquadCannotBeatTheBoss_OnlyTheFirstTime()
        {
            var (state, saved, hard, _) = Setup();
            var service = Service(Expedition());

            var weeks = service.AdvanceMonth(state);

            var last = weeks[^1].Settlement;
            Assert.True(last.Flags.SquadStalledAtBoss);
            Assert.False(GameCalendar.IsFirstWeekOfMonth(state.WeekNumber)); // 月の途中で止まった
            var stall = Assert.Single(last.SquadStalls);
            Assert.Same(saved, stall.Party);
            Assert.Same(hard, stall.Boss);
            Assert.True(stall.First);
            Assert.Equal(hard.Id, saved.StallNoticedBossId);
            // 扉前に着いたときの火力・要求・勝率が残る（道中で減ったHP込み）
            Assert.True(stall.Power > 0 && stall.RequiredPower > 0);
            Assert.Equal(DungeonResolver.WinChance(stall.Power, stall.RequiredPower), stall.WinChance);

            var report = MonthlyReport.Build(state, weeks, state.Gold);
            Assert.Contains("潜行の部隊が番人に勝てずに戻った", report.StopReasons);
            var plan = Assert.Single(report.Plans);
            Assert.False(plan.Repeated);
            Assert.Equal("主力", plan.PartyName);
            Assert.Contains("森 第10層「森の主」", plan.BossLabel);
            Assert.Contains(plan.Options, o => o.Choice == PlanChoice.Continue);

            // 同じ主で2回目からは止めない（月の最後まで進む）
            var rest = service.AdvanceMonth(state);
            Assert.True(GameCalendar.IsFirstWeekOfMonth(state.WeekNumber));
            Assert.DoesNotContain(rest, w => w.Settlement.Flags.SquadStalledAtBoss);
        }

        [Fact]
        public void ProcessWeek_CountsBattleLosses_AndClearsOnVictory()
        {
            var (state, saved, hard, _) = Setup();
            var forest = state.DungeonFields[0];
            var party = PartyOf(state, saved);

            var lost = new WeeklySettlementResult();
            lost.DungeonMissionResolutions.Add(new DungeonMissionResolution(party, hard, forest, 0.5,
                new DungeonResult { Outcome = DungeonOutcome.Retreat, WinChance = 0.55 }));
            NextMonthPlanSystem.ProcessWeek(state, lost);
            var stall = Assert.Single(lost.SquadStalls);
            Assert.True(stall.Lost);
            Assert.Equal(0.55, stall.WinChance);
            Assert.True(lost.Flags.SquadStalledAtBoss);

            var won = new WeeklySettlementResult();
            won.DungeonMissionResolutions.Add(new DungeonMissionResolution(party, hard, forest, 0.5,
                new DungeonResult { Outcome = DungeonOutcome.Victory }));
            NextMonthPlanSystem.ProcessWeek(state, won);
            Assert.Empty(won.SquadStalls);
            Assert.Null(saved.StallNoticedBossId);
        }

        [Fact]
        public void ProcessWeek_IgnoresSquadsNotDiving()
        {
            var (state, saved, hard, _) = Setup();
            var forest = state.DungeonFields[0];
            var party = PartyOf(state, saved);

            saved.Order = SquadOrder.Survey;
            var surveying = DoorRetreat(hard, forest, party, 600, 800);
            NextMonthPlanSystem.ProcessWeek(state, surveying);
            Assert.Empty(surveying.SquadStalls);
            Assert.Null(saved.StallNoticedBossId);
        }

        [Fact]
        public void Plan_AdvisesAboutHpLostOnTheWay_AndMissingCounters()
        {
            var (state, saved, hard, _) = Setup();
            var forest = state.DungeonFields[0];
            var party = PartyOf(state, saved);

            var worn = DoorRetreat(hard, forest, party, 44, 800, hp: 6);
            NextMonthPlanSystem.ProcessWeek(state, worn);
            var plan = Assert.Single(NextMonthPlanSystem.BuildPlans(state, worn.SquadStalls));
            Assert.Contains("HP6%", plan.Situation);
            Assert.Contains("道中の損耗", plan.Advice);

            var unready = DoorRetreat(hard, forest, party, 900, 800, reason: "魅了への備えが足りないため撤退", hp: 90);
            NextMonthPlanSystem.ProcessWeek(state, unready);
            var plan2 = Assert.Single(NextMonthPlanSystem.BuildPlans(state, unready.SquadStalls));
            Assert.Contains("魅了への備えが足りない", plan2.Situation);
            Assert.Contains("備えが足りない", plan2.Advice);
        }

        [Fact]
        public void ProcessWeek_BattleLoss_TakesTheHpAtTheDoorFromTheSameMission()
        {
            var (state, saved, hard, _) = Setup();
            var forest = state.DungeonFields[0];
            var party = PartyOf(state, saved);
            var week = new WeeklySettlementResult();
            week.DungeonMissionResolutions.Add(new DungeonMissionResolution(party, hard, forest, DungeonMissionType.Scouting)
                { ArrivedAtBossDoor = true, DoorHpPercent = 73 });
            week.DungeonMissionResolutions.Add(new DungeonMissionResolution(party, hard, forest, 0.5,
                new DungeonResult { Outcome = DungeonOutcome.Retreat, WinChance = 0.4, PartyPower = 500, RequiredPower = 520 }));
            NextMonthPlanSystem.ProcessWeek(state, week);
            var stall = Assert.Single(week.SquadStalls);
            Assert.True(stall.Lost);
            Assert.Equal(73, stall.HpPercent);
            Assert.Contains("決戦に負けた", Assert.Single(NextMonthPlanSystem.BuildPlans(state, week.SquadStalls)).Situation);
        }

        [Fact]
        public void Plan_RepeatedStall_FoldsAndDoesNotStop()
        {
            var (state, saved, hard, _) = Setup();
            var forest = state.DungeonFields[0];
            saved.StallNoticedBossId = hard.Id; // 先月に知らせ済み

            var week = DoorRetreat(hard, forest, PartyOf(state, saved), 700, 800);
            NextMonthPlanSystem.ProcessWeek(state, week);

            Assert.False(week.Flags.SquadStalledAtBoss);
            var plan = Assert.Single(NextMonthPlanSystem.BuildPlans(state, week.SquadStalls));
            Assert.True(plan.Repeated);
        }

        [Fact]
        public void Plan_OffersSurveyWithTheFullIntelChance_AndAnEasierBoss()
        {
            var (state, saved, hard, easy) = Setup();
            var forest = state.DungeonFields[0];
            var party = PartyOf(state, saved);
            // 扉前では道中でHPが減っていた：今の顔ぶれでの火力の8割
            double power = DungeonResolver.CalculateBossPower(party, hard) * 0.8;
            double required = DungeonResolver.RequiredPower(hard, state, party);
            double now = DungeonResolver.WinChance(power, required);

            var week = DoorRetreat(hard, forest, party, power, required);
            NextMonthPlanSystem.ProcessWeek(state, week);
            var plan = Assert.Single(NextMonthPlanSystem.BuildPlans(state, week.SquadStalls));

            Assert.Equal(new[] { PlanChoice.Survey, PlanChoice.OtherBoss, PlanChoice.Continue }, plan.Options.Select(o => o.Choice));
            var survey = plan.Options[0];
            Assert.Contains("完全解析", survey.Detail);
            Assert.Equal(DungeonResolver.WinChance(power * (1 + GuildManager.Core.Balance.DungeonBalance.FullIntelDamageBonus), required), survey.WinChance, 6);
            var other = plan.Options[1];
            Assert.Contains("草原 第10層「草原の主」", other.Detail);
            // 別の主の見込みも、扉前の火力（HPの減り）を元にする
            double otherPower = power * DungeonResolver.CalculateBossPower(party, easy) / DungeonResolver.CalculateBossPower(party, hard);
            Assert.Equal(DungeonResolver.WinChance(otherPower, DungeonResolver.RequiredPower(easy, state, party)), other.WinChance, 6);
            var keep = plan.Options[2];
            Assert.Equal(now, keep.WinChance, 6);
            Assert.Contains("扉前で引き返した", plan.Situation);
            Assert.NotEqual("", plan.Advice); // どの手も標準の6割に届かない

            // 倒された主には作戦を出さない
            hard.IsDefeated = true;
            Assert.Empty(NextMonthPlanSystem.BuildPlans(state, week.SquadStalls));
        }

        [Fact]
        public void Plan_NoSurveyOption_WhenTheBossIsFullyAnalyzed()
        {
            var (state, saved, hard, _) = Setup();
            hard.IntelRate = 1.0;
            var week = DoorRetreat(hard, state.DungeonFields[0], PartyOf(state, saved), 600, 800);
            NextMonthPlanSystem.ProcessWeek(state, week);
            var plan = Assert.Single(NextMonthPlanSystem.BuildPlans(state, week.SquadStalls));
            Assert.DoesNotContain(plan.Options, o => o.Choice == PlanChoice.Survey);
        }

        [Fact]
        public void WinChanceWithFullIntel_AddsTheFullIntelBonusOnlyBeforeCompletion()
        {
            var (state, saved, hard, _) = Setup();
            var party = PartyOf(state, saved);
            double power = DungeonResolver.CalculateBossPower(party, hard);
            double required = DungeonResolver.RequiredPower(hard, state, party);
            Assert.Equal(DungeonResolver.WinChance(power * (1 + GuildManager.Core.Balance.DungeonBalance.FullIntelDamageBonus), required),
                DungeonResolver.WinChanceWithFullIntel(party, hard, state));

            hard.IntelRate = 1.0;
            Assert.Equal(DungeonResolver.WinChance(party, hard, state), DungeonResolver.WinChanceWithFullIntel(party, hard, state));
        }

        [Fact]
        public void Apply_Survey_ThenResumesDiveWhenTheFieldIsAnalyzed()
        {
            var (state, saved, hard, _) = Setup();
            var plan = new MonthlyPlan { PartyId = saved.Id, FieldId = "forest", BossId = hard.Id };

            Assert.True(NextMonthPlanSystem.Apply(state, plan, PlanChoice.Survey));
            Assert.Equal(SquadOrder.Survey, saved.Order);
            Assert.Equal("forest", saved.OrderFieldId);
            Assert.True(saved.ResumeDiveAfterSurvey);

            // まだ調べることがある間は調査のまま
            state.DungeonFields[0].ReachedFloor = 10;
            Assert.False(SquadOrderSystem.ResumeDiveIfSurveyed(state, saved));
            Assert.Equal(SquadOrder.Survey, saved.Order);

            // 完全解析になったら潜行へ戻る
            hard.IntelRate = 1.0;
            Assert.True(SquadOrderSystem.ResumeDiveIfSurveyed(state, saved));
            Assert.Equal(SquadOrder.Dive, saved.Order);
            Assert.False(saved.ResumeDiveAfterSurvey);
        }

        [Fact]
        public void ResumeDive_IsForgotten_WhenThePlayerChangesTheOrderByHand()
        {
            var (state, saved, hard, _) = Setup();
            saved.ResumeDiveAfterSurvey = true;
            saved.Order = SquadOrder.Gather; // 手で採取に変えた
            hard.IntelRate = 1.0;
            Assert.False(SquadOrderSystem.ResumeDiveIfSurveyed(state, saved));
            Assert.Equal(SquadOrder.Gather, saved.Order);
            Assert.False(saved.ResumeDiveAfterSurvey);
        }

        [Fact]
        public void Apply_OtherBoss_SetsAuto_Continue_ChangesNothing()
        {
            var (state, saved, hard, _) = Setup();
            var plan = new MonthlyPlan { PartyId = saved.Id, FieldId = "forest", BossId = hard.Id };

            Assert.False(NextMonthPlanSystem.Apply(state, plan, PlanChoice.Continue));
            Assert.Equal("forest", saved.OrderFieldId);

            Assert.True(NextMonthPlanSystem.Apply(state, plan, PlanChoice.OtherBoss));
            Assert.Equal(SquadOrder.Dive, saved.Order);
            Assert.Equal(SavedParty.AutoFieldId, saved.OrderFieldId);
            Assert.Equal("plains", SquadOrderSystem.ResolveField(state, saved)!.Id); // おまかせは勝てそうな草原を選ぶ
        }

        [Fact]
        public void Load_OldSaveWithoutPlanFields_ReadsDefaults()
        {
            var (state, saved, _, _) = Setup();
            state.MonthlyReports.Add(new MonthlyReport { FirstWeek = 1, LastWeek = 4, MonthCompleted = true });
            string json = JsonSerializer.Serialize(state.ToSaveData());
            string oldJson = json.Replace("\"StallNoticedBossId\":null,", "").Replace("\"ResumeDiveAfterSurvey\":false", "\"Stance\":1")
                .Replace("\"Plans\":[],", "");
            Assert.DoesNotContain("StallNoticedBossId", oldJson);
            Assert.DoesNotContain("ResumeDiveAfterSurvey", oldJson);
            Assert.DoesNotContain("\"Plans\"", oldJson);

            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(oldJson)!);

            var party = Assert.Single(restored.SavedParties);
            Assert.Null(party.StallNoticedBossId);
            Assert.False(party.ResumeDiveAfterSurvey);
            Assert.Empty(Assert.Single(restored.MonthlyReports).Plans);
        }

        [Fact]
        public void RoundTrip_KeepsPlanFields()
        {
            var (state, saved, hard, _) = Setup();
            saved.StallNoticedBossId = hard.Id;
            saved.ResumeDiveAfterSurvey = true;
            state.MonthlyReports.Add(new MonthlyReport
            {
                Plans = { new MonthlyPlan { PartyId = saved.Id, PartyName = "主力", Options = { new PlanOption { Choice = PlanChoice.Survey, Label = "調査に切り替える" } } } },
            });

            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(state.ToSaveData()))!);

            var party = Assert.Single(restored.SavedParties);
            Assert.Equal(hard.Id, party.StallNoticedBossId);
            Assert.True(party.ResumeDiveAfterSurvey);
            var plan = Assert.Single(Assert.Single(restored.MonthlyReports).Plans);
            Assert.Equal(PlanChoice.Survey, Assert.Single(plan.Options).Choice);
            Assert.Null(plan.Chosen); // 選ぶ前（旧セーブも）
        }

        [Fact]
        public void ChosenPlan_RoundTripsInTheSave()
        {
            // §0.151：部隊長の報告の場で選んだ手は、月報の見返しで結果の1行にするので、セーブに残す
            var (state, saved, _, _) = Setup();
            state.MonthlyReports.Add(new MonthlyReport
            {
                Plans = { new MonthlyPlan { PartyId = saved.Id, PartyName = "主力", Chosen = PlanChoice.OtherBoss, Options = { new PlanOption { Choice = PlanChoice.OtherBoss, Label = "別の主を狙う" } } } },
            });
            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(state.ToSaveData()))!);
            Assert.Equal(PlanChoice.OtherBoss, Assert.Single(Assert.Single(restored.MonthlyReports).Plans).Chosen);
        }
    }
}
