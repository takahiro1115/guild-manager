using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 2026年10月・§0.63のテスト：部隊の方針と自動出撃（潜行・調査・採取を続ける、扉前の自動判断、自動スキップ）。
    /// 実行方法: `dotnet test GuildManager.Core.Tests --filter FullyQualifiedName~SquadOrder`
    /// </summary>
    public class SquadOrderTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private static DungeonExpeditionSystem Expedition() => new(
            new ScoutingResolver(new AlwaysMinRng()), new DungeonResolver(new AlwaysMinRng()), new SatisfactionSystem(),
            new CompatibilitySystem(new AlwaysMinRng()), new DungeonTraversalResolver(new AlwaysMinRng()), new GatheringResolver(new AlwaysMinRng()));

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

        /// <summary>森（ボス1体）と、方針つきの2人部隊。</summary>
        private static (GameState State, SavedParty Saved, FloorBoss Boss) Setup(SquadOrder order, int stat = 40, DoorStance stance = DoorStance.Standard, params BossGimmick[] gimmicks)
        {
            var boss = new FloorBoss { Name = "森の主", Floor = 10, MaxHp = 100, CurrentHp = 100 };
            boss.Gimmicks.AddRange(gimmicks);
            var forest = new DungeonField { Id = "forest", Name = "森", Order = 1, IsUnlocked = true, ReachedFloor = 10, Bosses = { boss } };
            var a = Make("アリス", stat);
            var b = Make("セリア", stat);
            var saved = new SavedParty { Name = "第一部隊", MemberIds = { a.Id, b.Id }, Order = order, OrderFieldId = "forest", Stance = stance };
            var state = new GameState { Gold = 10000, Adventurers = { a, b }, DungeonFields = { forest }, SavedParties = { saved } };
            return (state, saved, boss);
        }

        [Fact]
        public void CsvValues_AreLoaded()
        {
            Assert.Equal(70, SquadOrderBalance.AutoDispatchMinHpPercent);
            Assert.Equal(new SquadOrderBalance.StanceRule(1.2, 80, 1.0, 1.0), SquadOrderBalance.Cautious);
            Assert.Equal(new SquadOrderBalance.StanceRule(1.0, 60, 0, 0.5), SquadOrderBalance.Standard);
            Assert.Equal(new SquadOrderBalance.StanceRule(0.9, 40, 0, 0), SquadOrderBalance.Bold);
            Assert.Same(SquadOrderBalance.Cautious, SquadOrderBalance.For(DoorStance.Cautious));
            Assert.Same(SquadOrderBalance.Bold, SquadOrderBalance.For(DoorStance.Bold));
        }

        // ---------------- 自動出撃 ----------------

        [Theory]
        [InlineData(SquadOrder.Dive, DungeonMissionType.Scouting)]
        [InlineData(SquadOrder.Survey, DungeonMissionType.Survey)]
        [InlineData(SquadOrder.Gather, DungeonMissionType.Gathering)]
        public void IdleOrderedParty_IsDispatched_ByOrder(SquadOrder order, DungeonMissionType expected)
        {
            var (state, saved, _) = Setup(order);
            var events = new SquadOrderSystem(Expedition()).Execute(state);

            var mission = Assert.Single(state.ActiveDungeonMissions);
            Assert.Equal(expected, mission.MissionType);
            Assert.Equal(saved.Id, mission.SavedPartyId);
            Assert.Equal(2, mission.Party.Members.Count);
            Assert.Equal(SquadOrderAction.Dispatched, Assert.Single(events).Action);
            Assert.True(SquadOrderSystem.IsOut(state, saved));

            // もう出撃中なので、次に呼んでも二重に出ない
            Assert.Empty(new SquadOrderSystem(Expedition()).Execute(state));
            Assert.Single(state.ActiveDungeonMissions);
        }

        [Fact]
        public void SurveyOrder_Gathers_WhenBossIsFullyAnalyzed()
        {
            var (state, _, boss) = Setup(SquadOrder.Survey);
            boss.IntelRate = 1.0;
            new SquadOrderSystem(Expedition()).Execute(state);
            Assert.Equal(DungeonMissionType.Gathering, Assert.Single(state.ActiveDungeonMissions).MissionType);
        }

        // ---------------- おまかせ（方針の対象ダンジョンを自動で選ぶ） ----------------

        /// <summary>森（ボス10F）と洞窟（ボス10F）の2フィールドと、「おまかせ」の方針つき2人部隊。</summary>
        private static (GameState State, SavedParty Saved, FloorBoss ForestBoss, FloorBoss CaveBoss) SetupAuto(SquadOrder order, int stat = 40)
        {
            var (state, saved, forestBoss) = Setup(order, stat);
            var caveBoss = new FloorBoss { Name = "洞窟の主", Floor = 10, MaxHp = 100, CurrentHp = 100, FieldOrder = 2 };
            state.DungeonFields.Add(new DungeonField { Id = "cave", Name = "洞窟", Order = 2, IsUnlocked = true, ReachedFloor = 10, Bosses = { caveBoss } });
            saved.OrderFieldId = SavedParty.AutoFieldId;
            return (state, saved, forestBoss, caveBoss);
        }

        [Fact]
        public void Auto_ConcreteField_IsReturnedAsIs_AndUnknownFieldIsNull()
        {
            var (state, saved, _, _) = SetupAuto(SquadOrder.Dive);
            saved.OrderFieldId = "cave";
            Assert.Equal("cave", SquadOrderSystem.ResolveField(state, saved)!.Id);
            saved.OrderFieldId = "nowhere";
            Assert.Null(SquadOrderSystem.ResolveField(state, saved));
        }

        [Fact]
        public void Auto_Survey_PicksTheBossWithTheLowestAnalysis()
        {
            var (state, saved, forestBoss, caveBoss) = SetupAuto(SquadOrder.Survey);
            forestBoss.IntelRate = 0.6;
            caveBoss.IntelRate = 0.1;
            Assert.Equal("cave", SquadOrderSystem.ResolveField(state, saved)!.Id);

            // 洞窟が完全解析済みなら、残る森を調べる
            caveBoss.IntelRate = 1.0;
            Assert.Equal("forest", SquadOrderSystem.ResolveField(state, saved)!.Id);
        }

        [Fact]
        public void Auto_Dive_PicksTheFieldWhereTheBossLooksMostWinnable()
        {
            var (state, saved, forestBoss, caveBoss) = SetupAuto(SquadOrder.Dive);
            forestBoss.Floor = 90; // 深いほど要求火力が高い
            caveBoss.Floor = 10;
            Assert.Equal("cave", SquadOrderSystem.ResolveField(state, saved)!.Id);
            forestBoss.Floor = 10;
            caveBoss.Floor = 90;
            Assert.Equal("forest", SquadOrderSystem.ResolveField(state, saved)!.Id);
        }

        [Fact]
        public void Auto_Gather_PrefersTheDeeperField_WhenTheGuardIsTheSame()
        {
            var (state, saved, _, _) = SetupAuto(SquadOrder.Gather, stat: 200);
            state.DungeonFields.First(f => f.Id == "cave").ReachedFloor = 5;
            state.DungeonFields.First(f => f.Id == "forest").ReachedFloor = 3;
            Assert.Equal("cave", SquadOrderSystem.ResolveField(state, saved)!.Id);
        }

        [Fact]
        public void Auto_DispatchesToTheChosenField_AndSkipsLockedFields()
        {
            var (state, saved, forestBoss, caveBoss) = SetupAuto(SquadOrder.Survey);
            forestBoss.IntelRate = 0.5;
            caveBoss.IntelRate = 0.0;
            state.DungeonFields.First(f => f.Id == "cave").IsUnlocked = false; // 洞窟は未開放なので選ばれない

            new SquadOrderSystem(Expedition()).Execute(state);

            var mission = Assert.Single(state.ActiveDungeonMissions);
            Assert.Equal("forest", mission.Field.Id);
        }

        [Fact]
        public void Auto_WithNoUsableField_Waits()
        {
            var (state, saved, _, _) = SetupAuto(SquadOrder.Gather);
            foreach (var field in state.DungeonFields) field.IsUnlocked = false;
            Assert.Contains("ダンジョンがない", SquadOrderSystem.GetWaitReason(state, saved));
        }

        [Fact]
        public void NoOrder_IsIgnored()
        {
            var (state, _, _) = Setup(SquadOrder.None);
            Assert.Empty(new SquadOrderSystem(Expedition()).Execute(state));
            Assert.Empty(state.ActiveDungeonMissions);
        }

        [Fact]
        public void Waits_WhenHpLow_SeverelyInjured_FieldLocked_OrSlotsFull()
        {
            var (state, saved, _) = Setup(SquadOrder.Gather);
            var a = state.Adventurers[0];

            a.CurrentHP = a.MaxHP * 69 / 100;
            AssertWaits(state, "HP");
            a.CurrentHP = a.MaxHP;

            a.Injury = InjurySeverity.Severe;
            AssertWaits(state, "重傷");
            a.Injury = InjurySeverity.None;

            state.DungeonFields[0].IsUnlocked = false;
            AssertWaits(state, "開放");
            state.DungeonFields[0].IsUnlocked = true;

            state.UnlockedSquadSlots = 0;
            AssertWaits(state, "出撃枠");
            state.UnlockedSquadSlots = 1;

            Assert.Null(SquadOrderSystem.GetWaitReason(state, saved));
        }

        private static void AssertWaits(GameState state, string reasonContains)
        {
            var ev = Assert.Single(new SquadOrderSystem(Expedition()).Execute(state));
            Assert.Equal(SquadOrderAction.Waiting, ev.Action);
            Assert.Contains(reasonContains, ev.Detail);
            Assert.Empty(state.ActiveDungeonMissions);
        }

        // ---------------- 扉前の構え（§0.68・§0.69：着いた週のうちに判断） ----------------

        /// <summary>方針どおりに出撃させ、扉前に着いた状態にする（JudgeEngage を直接確かめる用）。</summary>
        private static ActiveDungeonMission AtDoor(GameState state, SavedParty saved, FloorBoss boss)
        {
            new SquadOrderSystem(Expedition()).Execute(state);
            var mission = state.ActiveDungeonMissions.Single();
            mission.TargetedBoss = boss;
            mission.CurrentFloor = boss.Floor;
            mission.WeeksElapsed = 2;
            return mission;
        }

        /// <summary>方針の潜行を、扉前に着く週まで進める。その週の解決の一覧を返す。</summary>
        private static List<DungeonMissionResolution> DiveToDoor(GameState state, DungeonExpeditionSystem expedition)
        {
            new SquadOrderSystem(expedition).Execute(state);
            for (int week = 0; week < 20; week++)
            {
                var results = expedition.ProcessWeeklyMissions(state);
                if (results.Any(r => r.ArrivedAtBossDoor)) return results;
            }
            throw new Xunit.Sdk.XunitException("扉前に着かなかった");
        }

        [Fact]
        public void OrderedDive_FightsAtDoorTheSameWeek_WithoutCost()
        {
            // MND合計400÷100 → 備え万全。火力も十分なので、扉前に着いた週のうちに決戦して撃破する。
            var poison = new BossGimmick { Type = BossGimmickType.Poison, RequiredCounterStat = "MND", RequiredCounterStatThreshold = 100, DangerLevel = 2 };
            var (state, _, boss) = Setup(SquadOrder.Dive, stat: 200, stance: DoorStance.Standard, poison);
            var expedition = Expedition();

            var results = DiveToDoor(state, expedition);

            Assert.Equal(2, results.Count);
            Assert.Null(results[0].DoorRetreatReason);
            Assert.Equal(DungeonOutcome.Victory, results[1].DungeonResult!.Outcome);
            Assert.True(boss.IsDefeated);
        }

        [Theory]
        [InlineData(DoorStance.Cautious, false)] // 慎重：すべて万全でないと挑まない
        [InlineData(DoorStance.Standard, true)]  // 標準：即死級以外は備えを問わない
        [InlineData(DoorStance.Bold, true)]
        public void Stance_DecidesOnPartialReadiness(DoorStance stance, bool expectedGo)
        {
            // 飛行・DEX合計400÷1000 → 備え0.4（一部）。火力は十分。
            var flying = new BossGimmick { Type = BossGimmickType.Flying, RequiredCounterStat = "DEX", RequiredCounterStatThreshold = 1000, DangerLevel = 1 };
            var (state, saved, boss) = Setup(SquadOrder.Dive, stat: 200, stance: stance, flying);
            var mission = AtDoor(state, saved, boss);

            var judge = SquadOrderSystem.JudgeEngage(state, mission, stance);

            Assert.Equal(expectedGo, judge.Go);
            if (!expectedGo) Assert.Contains("飛行", judge.Reason);
        }

        [Theory]
        [InlineData(DoorStance.Standard, false)] // 標準：即死級の備えが半分未満なら撤退（戦死を避ける）
        [InlineData(DoorStance.Bold, true)]      // 強気：それでも挑む
        public void Stance_InstantKillThreshold(DoorStance stance, bool expectedGo)
        {
            var instantKill = new BossGimmick { Type = BossGimmickType.InstantKill, RequiredCounterStat = "LDR", RequiredCounterStatThreshold = 1000, DangerLevel = 5 };
            var (state, saved, boss) = Setup(SquadOrder.Dive, stat: 200, stance: stance, instantKill);
            var mission = AtDoor(state, saved, boss);

            Assert.Equal(expectedGo, SquadOrderSystem.JudgeEngage(state, mission, stance).Go);
        }

        [Fact]
        public void Stance_Bold_EngagesSlightlyBelowRequirement_ButStandardDoesNot()
        {
            var (state, saved, boss) = Setup(SquadOrder.Dive, stat: 40, stance: DoorStance.Bold);
            var mission = AtDoor(state, saved, boss);
            double required = DungeonResolver.RequiredPower(boss, state, mission.Party);
            // 火力を要求の95%に合わせる：2人とも同じ能力なので、全能力を同じ割合で縮める
            double ratio = 0.95 * required / DungeonResolver.CalculateBossPower(mission.Party, boss);
            foreach (var m in mission.Party.Members)
            {
                m.STR = (int)(m.STR * ratio); m.AGI = (int)(m.AGI * ratio); m.VIT = (int)(m.VIT * ratio); m.MND = (int)(m.MND * ratio);
                m.DEX = (int)(m.DEX * ratio); m.LDR = (int)(m.LDR * ratio); m.INT = (int)(m.INT * ratio);
                m.CurrentHP = m.MaxHP;
            }
            double power = DungeonResolver.CalculateBossPower(mission.Party, boss);
            Assert.InRange(power / required, 0.9, 1.0);

            Assert.True(SquadOrderSystem.JudgeEngage(state, mission, DoorStance.Bold).Go);
            Assert.False(SquadOrderSystem.JudgeEngage(state, mission, DoorStance.Standard).Go);
        }

        [Fact]
        public void OrderedDive_RetreatsAtDoor_WhenTooWeak_AndDivesAgainNextWeek()
        {
            var (state, _, boss) = Setup(SquadOrder.Dive, stat: 10, stance: DoorStance.Standard);
            var expedition = Expedition();

            var results = DiveToDoor(state, expedition);

            // 扉前から撤退して帰還する。HPが足りていれば次の週送りでまた1階層から潜り直す（道中で鍛えながら機会を待つ）。
            var door = Assert.Single(results);
            Assert.Contains("討伐火力", door.DoorRetreatReason);
            Assert.True(door.ReturnedHome);
            Assert.False(boss.IsDefeated);
            Assert.Empty(state.ActiveDungeonMissions);
            var events = new SquadOrderSystem(expedition).Execute(state);
            Assert.Single(events, e => e.Action == SquadOrderAction.Dispatched);
            Assert.Equal(1, Assert.Single(state.ActiveDungeonMissions).CurrentFloor);
        }

        [Fact]
        public void Judge_Retreats_WhenGimmickCannotBeCountered()
        {
            var noCounter = new BossGimmick { Type = BossGimmickType.InstantKill, DangerLevel = 5 }; // 対策口が無い
            var (state, saved, boss) = Setup(SquadOrder.Dive, stat: 200, stance: DoorStance.Standard, noCounter);
            var mission = AtDoor(state, saved, boss);

            var judge = SquadOrderSystem.JudgeEngage(state, mission, DoorStance.Standard);
            Assert.False(judge.Go);
            Assert.Contains("即死級", judge.Reason);
        }

        [Fact]
        public void Judge_Retreats_WhenHpLow()
        {
            var (state, saved, boss) = Setup(SquadOrder.Dive, stat: 200, stance: DoorStance.Standard);
            AtDoor(state, saved, boss);
            var a = state.Adventurers[0];
            a.CurrentHP = a.MaxHP / 2;

            var judge = SquadOrderSystem.JudgeEngage(state, state.ActiveDungeonMissions.Single(), DoorStance.Standard);
            Assert.False(judge.Go);
            Assert.Contains("HP", judge.Reason);
        }

        [Fact]
        public void Recall_CancelsBeforeDeparture_AndRetreatsMidDive()
        {
            var (state, saved, _) = Setup(SquadOrder.Dive, stat: 3, stance: DoorStance.Standard);
            var expedition = Expedition();
            var orders = new SquadOrderSystem(expedition);

            orders.Execute(state);
            Assert.True(orders.Recall(state, saved)); // 出発前：取り消し
            Assert.Empty(state.ActiveDungeonMissions);
            Assert.False(orders.Recall(state, saved)); // 出撃していなければ何もしない

            orders.Execute(state);
            expedition.ProcessWeeklyMissions(state); // 1週潜る（扉前にはまだ着かない）
            Assert.Equal(ExpeditionStatus.Advancing, Assert.Single(state.ActiveDungeonMissions).Status);
            Assert.True(orders.Recall(state, saved)); // 潜行中：即時撤退
            Assert.Empty(state.ActiveDungeonMissions);
            Assert.All(state.Adventurers, a => Assert.False(a.IsDispatched));
        }

        // ---------------- 自動スキップ ----------------

        [Fact]
        public void CanAutoSkip_OnlyWhenAllMissionsAreOrdered()
        {
            var (state, saved, _) = Setup(SquadOrder.Gather);
            Assert.True(AutoSkipService.CanAutoSkip(state));
            new SquadOrderSystem(Expedition()).Execute(state);
            Assert.True(AutoSkipService.CanAutoSkip(state));
            state.ActiveDungeonMissions[0].SavedPartyId = null;
            Assert.False(AutoSkipService.CanAutoSkip(state));
        }

        [Fact]
        public void AutoSkipDetailed_RunsOrdersEveryWeek()
        {
            var (state, _, _) = Setup(SquadOrder.Gather, stat: 80);
            var expedition = Expedition();
            var week = new WeekProcessingSystem(new MasterMoodSystem(), new EconomySystem(), new TrainingSystem(), new InjuryRecoverySystem(),
                new RestRecoverySystem(), new GrowthSystem(new AlwaysMinRng()), new SatisfactionSystem(), new AgingSystem(new AlwaysMinRng()),
                new FacilitySystem(), new DefeatSystem(), new RecruitmentSystem(new AlwaysMinRng()), expedition);
            var service = new AutoSkipService(week, new SquadOrderSystem(expedition));

            var weeks = service.AutoSkipDetailed(state, 3);

            Assert.Equal(3, weeks.Count);
            Assert.Contains(weeks[0].Orders, e => e.Action == SquadOrderAction.Dispatched);
            Assert.All(weeks, w => Assert.NotEmpty(w.Orders)); // 毎週、出撃か待機の判断をしている
            Assert.Equal(3, service.AutoSkip(state, 3).Count); // 従来の戻り値の形も使える
        }

        [Fact]
        public void NewStopConditions()
        {
            Assert.True(new WeekResult { SevereInjuryOccurred = true }.ShouldStopAutoSkip);
            Assert.True(new WeekResult { BossDefeated = true }.ShouldStopAutoSkip);
        }

        // ---------------- セーブ ----------------

        [Fact]
        public void Orders_SurviveSave_AndOldSaveDefaultsToNone()
        {
            var (state, saved, _) = Setup(SquadOrder.Dive, stance: DoorStance.Standard);
            new SquadOrderSystem(Expedition()).Execute(state);

            var json = JsonSerializer.Serialize(state.ToSaveData());
            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!);
            var rs = restored.SavedParties.Single();
            Assert.Equal(SquadOrder.Dive, rs.Order);
            Assert.Equal("forest", rs.OrderFieldId);
            Assert.Equal(DoorStance.Standard, rs.Stance);
            Assert.Equal(saved.Id, restored.ActiveDungeonMissions.Single().SavedPartyId);

            var oldJson = System.Text.RegularExpressions.Regex.Replace(json, ",\"(Order|OrderFieldId|Stance|SavedPartyId)\":(\\d+|\"[^\"]*\"|true|false|null)", "");
            Assert.DoesNotContain("SavedPartyId", oldJson);
            var old = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(oldJson)!);
            Assert.Equal(SquadOrder.None, old.SavedParties.Single().Order);
            Assert.Null(old.ActiveDungeonMissions.Single().SavedPartyId);
            Assert.Equal(DoorStance.Standard, old.SavedParties.Single().Stance);
        }

        [Fact]
        public void PoisonedMember_KeepsPartyWaiting()
        {
            var (state, saved, _) = Setup(SquadOrder.Gather, stat: 80);
            state.Adventurers[0].PoisonWeeksRemaining = 2;
            state.Adventurers[0].PoisonStatPenalty = 0.15;

            Assert.Contains("毒状態", SquadOrderSystem.GetWaitReason(state, saved));
        }
    }
}
