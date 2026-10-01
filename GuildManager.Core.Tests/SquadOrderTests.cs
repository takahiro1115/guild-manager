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
        private static (GameState State, SavedParty Saved, FloorBoss Boss) Setup(SquadOrder order, int stat = 40, bool autoEngage = false, params BossGimmick[] gimmicks)
        {
            var boss = new FloorBoss { Name = "森の主", Floor = 10, MaxHp = 100, CurrentHp = 100 };
            boss.Gimmicks.AddRange(gimmicks);
            var forest = new DungeonField { Id = "forest", Name = "森", Order = 1, IsUnlocked = true, Bosses = { boss } };
            var a = Make("アリス", stat);
            var b = Make("セリア", stat);
            var saved = new SavedParty { Name = "第一部隊", MemberIds = { a.Id, b.Id }, Order = order, OrderFieldId = "forest", AutoEngage = autoEngage };
            var state = new GameState { Gold = 10000, Adventurers = { a, b }, DungeonFields = { forest }, SavedParties = { saved } };
            return (state, saved, boss);
        }

        [Fact]
        public void CsvValues_AreLoaded()
        {
            Assert.Equal(70, SquadOrderBalance.AutoDispatchMinHpPercent);
            Assert.Equal(60, SquadOrderBalance.AutoEngageMinHpPercent);
            Assert.Equal(1.0, SquadOrderBalance.AutoEngagePowerMargin, precision: 6);
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

        // ---------------- 扉前の自動判断 ----------------

        private static ActiveDungeonMission AtDoor(GameState state, SavedParty saved, FloorBoss boss)
        {
            new SquadOrderSystem(Expedition()).Execute(state);
            var mission = state.ActiveDungeonMissions.Single();
            mission.Status = ExpeditionStatus.AwaitingBossDecision;
            mission.TargetedBoss = boss;
            mission.CurrentFloor = boss.Floor;
            mission.WeeksElapsed = 2;
            return mission;
        }

        [Fact]
        public void AutoEngage_Engages_WhenPromising_AndBuysCounterItems()
        {
            var poison = new BossGimmick { Type = BossGimmickType.Poison, RequiredItemId = ConsumableCatalog.AntidoteId, DangerLevel = 2 };
            var (state, saved, boss) = Setup(SquadOrder.Dive, stat: 200, autoEngage: true, poison);
            var mission = AtDoor(state, saved, boss);
            int gold = state.Gold;

            var ev = Assert.Single(new SquadOrderSystem(Expedition()).Execute(state));

            Assert.Equal(SquadOrderAction.Engaged, ev.Action);
            Assert.Equal(ExpeditionStatus.EngagingBoss, mission.Status);
            Assert.Contains(ConsumableCatalog.AntidoteId, mission.Party.ConsumableItemIds);
            Assert.Equal(gold - ConsumableCatalog.FindById(ConsumableCatalog.AntidoteId)!.Price, state.Gold);
        }

        [Fact]
        public void AutoEngage_Retreats_WhenTooWeak()
        {
            var (state, saved, boss) = Setup(SquadOrder.Dive, stat: 10, autoEngage: true);
            var mission = AtDoor(state, saved, boss);

            var events = new SquadOrderSystem(Expedition()).Execute(state);

            // 扉前から撤退して帰還し、HPが足りていれば同じ週にまた1階層から潜り直す（道中で鍛えながら機会を待つ）。
            var ev = Assert.Single(events, e => e.Action == SquadOrderAction.Retreated);
            Assert.Contains("討伐火力", ev.Detail);
            Assert.NotNull(ev.Resolution);
            Assert.DoesNotContain(mission, state.ActiveDungeonMissions);
            Assert.Single(events, e => e.Action == SquadOrderAction.Dispatched);
            Assert.Equal(1, Assert.Single(state.ActiveDungeonMissions).CurrentFloor);
        }

        [Fact]
        public void AutoEngage_Retreats_WhenGimmickCannotBeCountered()
        {
            var noCounter = new BossGimmick { Type = BossGimmickType.InstantKill, DangerLevel = 5 }; // 対策口が無い
            var (state, saved, boss) = Setup(SquadOrder.Dive, stat: 200, autoEngage: true, noCounter);
            AtDoor(state, saved, boss);

            var ev = new SquadOrderSystem(Expedition()).Execute(state).First();
            Assert.Equal(SquadOrderAction.Retreated, ev.Action);
            Assert.Contains("即死級", ev.Detail);
        }

        [Fact]
        public void AutoEngage_Retreats_WhenHpLow()
        {
            var (state, saved, boss) = Setup(SquadOrder.Dive, stat: 200, autoEngage: true);
            AtDoor(state, saved, boss);
            var a = state.Adventurers[0];
            a.CurrentHP = a.MaxHP / 2;

            var judge = SquadOrderSystem.JudgeEngage(state, state.ActiveDungeonMissions.Single());
            Assert.False(judge.Go);
            Assert.Contains("HP", judge.Reason);
        }

        [Fact]
        public void WithoutAutoEngage_DoorIsLeftToPlayer()
        {
            var (state, saved, boss) = Setup(SquadOrder.Dive, stat: 200, autoEngage: false);
            var mission = AtDoor(state, saved, boss);

            Assert.False(SquadOrderSystem.DecidesAtDoor(state, mission.Party));
            Assert.Empty(new SquadOrderSystem(Expedition()).Execute(state));
            Assert.Equal(ExpeditionStatus.AwaitingBossDecision, mission.Status);

            saved.AutoEngage = true;
            Assert.True(SquadOrderSystem.DecidesAtDoor(state, mission.Party));
        }

        [Fact]
        public void ManualMission_IsNeverDecided()
        {
            var (state, saved, boss) = Setup(SquadOrder.Dive, stat: 200, autoEngage: true);
            var mission = AtDoor(state, saved, boss);
            mission.SavedPartyId = null; // 手動の出撃
            Assert.False(SquadOrderSystem.DecidesAtDoor(state, mission.Party));
            Assert.Null(SquadOrderSystem.FindOrderedParty(state, mission));
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
            var (state, saved, _) = Setup(SquadOrder.Dive, autoEngage: true);
            new SquadOrderSystem(Expedition()).Execute(state);

            var json = JsonSerializer.Serialize(state.ToSaveData());
            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!);
            var rs = restored.SavedParties.Single();
            Assert.Equal(SquadOrder.Dive, rs.Order);
            Assert.Equal("forest", rs.OrderFieldId);
            Assert.True(rs.AutoEngage);
            Assert.Equal(saved.Id, restored.ActiveDungeonMissions.Single().SavedPartyId);

            var oldJson = System.Text.RegularExpressions.Regex.Replace(json, ",\"(Order|OrderFieldId|AutoEngage|SavedPartyId)\":(\\d+|\"[^\"]*\"|true|false|null)", "");
            Assert.DoesNotContain("SavedPartyId", oldJson);
            var old = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(oldJson)!);
            Assert.Equal(SquadOrder.None, old.SavedParties.Single().Order);
            Assert.Null(old.ActiveDungeonMissions.Single().SavedPartyId);
        }
    }
}
