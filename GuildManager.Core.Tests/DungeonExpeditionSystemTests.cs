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
    /// <summary>
    /// 大迷宮への出撃の派遣・週次解決（DungeonExpeditionSystem）と、そのゲーム進行への接続
    /// （GameState保持・同時出撃枠・週次決算・セーブ/ロード）のテスト。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class DungeonExpeditionSystemTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private static DungeonExpeditionSystem BuildSystem() => new(
            new ScoutingResolver(new AlwaysMinRng()),
            new DungeonResolver(new AlwaysMinRng()),
            new SatisfactionSystem(),
            new CompatibilitySystem(new AlwaysMinRng()),
            new DungeonTraversalResolver(new AlwaysMinRng()),
            new GatheringResolver(new AlwaysMinRng()));

        private static Adventurer MakeAdventurer(JobClass job, int stat)
        {
            var a = new Adventurer
            {
                Name = job.ToString(), Age = 18, JobClass = job, Placement = Placement.Front,
                STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat,
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            return party;
        }

        /// <summary>即死級（護符でしか対策できない）を持つボス。無対策で突撃すると全員が強制除籍になる。</summary>
        private static FloorBoss MakeDeadlyBoss() => new()
        {
            Name = "試験用の番人", Floor = 1, MaxHp = 500, CurrentHp = 500,
            Gimmicks = { new BossGimmick { Type = BossGimmickType.InstantKill, RequiredItemId = ConsumableCatalog.CharmId, DangerLevel = 5 } },
        };

        /// <summary>単体テスト用の使い捨てフィールド（→ DungeonField）に、指定した1体だけを収める。</summary>
        private static (GameState State, Adventurer A, Adventurer B, FloorBoss Boss) MakeState(FloorBoss? boss = null)
        {
            var a = MakeAdventurer(JobClass.Ranger, 40);
            var b = MakeAdventurer(JobClass.Scholar, 40);
            boss ??= new FloorBoss { Name = "階層の主", Floor = 1, MaxHp = 500, CurrentHp = 500 };
            var field = new DungeonField { Id = "test", Name = "テスト用フィールド", Order = 1, IsUnlocked = true, Bosses = { boss } };
            var state = new GameState { Adventurers = { a, b }, DungeonFields = { field } };
            return (state, a, b, boss);
        }

        // ---------------- 派遣・取り消し ----------------

        [Fact]
        public void TryDispatch_MarksMembersDispatched_AndConsumesSharedSquadSlot()
        {
            var (state, a, b, boss) = MakeState();
            Assert.Equal(1, state.UnlockedSquadSlots);

            Assert.True(BuildSystem().TryDispatch(state, PartyOf(a, b), boss, DungeonMissionType.Scouting));

            Assert.Single(state.ActiveDungeonMissions);
            Assert.True(a.IsDispatched);
            Assert.True(b.IsDispatched);
            // 通常クエストと同じ枠を共有する：1枠のうち1部隊が大迷宮へ出ているので、もう出せない。
            Assert.False(QuestDispatchSystem.CanDispatch(state));
        }

        [Fact]
        public void TryDispatch_Fails_WhenSlotFull_BossDefeated_OrMemberUnavailable()
        {
            var system = BuildSystem();

            var (full, a1, b1, boss1) = MakeState();
            full.ActiveDispatches.Add(new ActiveDispatch());
            Assert.False(system.TryDispatch(full, PartyOf(a1, b1), boss1, DungeonMissionType.Scouting));

            var (defeated, a2, b2, boss2) = MakeState();
            boss2.IsDefeated = true;
            Assert.False(system.TryDispatch(defeated, PartyOf(a2, b2), boss2, DungeonMissionType.BossAssault));

            var (injured, a3, b3, boss3) = MakeState();
            b3.Injury = InjurySeverity.Severe;
            Assert.False(system.TryDispatch(injured, PartyOf(a3, b3), boss3, DungeonMissionType.Scouting));

            var (empty, _, _, boss4) = MakeState();
            Assert.False(system.TryDispatch(empty, new Party(), boss4, DungeonMissionType.Scouting));

            Assert.Empty(full.ActiveDungeonMissions);
            Assert.Empty(defeated.ActiveDungeonMissions);
            Assert.Empty(injured.ActiveDungeonMissions);
            Assert.False(a3.IsDispatched);
        }

        [Fact]
        public void DungeonExpedition_SpecificFieldDispatch()
        {
            // 複数フィールド（森・洞窟）が存在する環境で、洞窟を指定した調査・討伐派遣が
            // 森ではなく洞窟の進行として解決されること（→ 大迷宮フィールド選択UI仕様）。
            var forestBoss = new FloorBoss { Name = "森のボス", Floor = 1, MaxHp = 999_999, CurrentHp = 999_999 };
            var forest = new DungeonField { Id = "forest", Name = "森", Order = 1, IsUnlocked = true, ReachedFloor = 1, Bosses = { forestBoss } };

            var caveBoss = new FloorBoss { Name = "洞窟のボス", Floor = 1, MaxHp = 1, CurrentHp = 1 };
            var cave = new DungeonField { Id = "cave", Name = "洞窟", Order = 2, IsUnlocked = true, ReachedFloor = 1, Bosses = { caveBoss } };

            var state = new GameState { DungeonFields = { forest, cave }, UnlockedSquadSlots = 2 };
            var system = BuildSystem();

            // ①調査派遣：洞窟を指定 → 1週目で洞窟1Fボスの扉前に着き、2週目に扉前で偵察して
            // 洞窟のIntelRateだけが上がる。森は無傷。偵察後は撤退させて枠を空ける。
            var scoutParty = PartyOf(MakeAdventurer(JobClass.Ranger, 40), MakeAdventurer(JobClass.Scholar, 40));
            Assert.True(system.TryDispatch(state, scoutParty, caveBoss, DungeonMissionType.Scouting));
            Assert.True(Assert.Single(system.ProcessWeeklyMissions(state)).ArrivedAtBossDoor);
            var scoutResolution = Assert.Single(system.ProcessWeeklyMissions(state));

            Assert.Same(caveBoss, scoutResolution.Boss);
            Assert.NotNull(scoutResolution.ScoutingResult);
            Assert.True(caveBoss.IntelRate > 0.0);
            Assert.Equal(0.0, forestBoss.IntelRate);
            Assert.Equal(1, forest.ReachedFloor); // 森は一切進行していない
            Assert.NotNull(system.TryRetreat(state, state.ActiveDungeonMissions[0]));

            // ②討伐派遣：洞窟を指定 → 洞窟のボスだけが撃破され、ReachedFloorも洞窟だけが進む。森は無傷。
            var assaultParty = PartyOf(MakeAdventurer(JobClass.Warrior, 300), MakeAdventurer(JobClass.Knight, 300));
            Assert.True(system.TryDispatch(state, assaultParty, caveBoss, DungeonMissionType.BossAssault));
            var assaultResolution = Assert.Single(system.ProcessWeeklyMissions(state));

            Assert.Same(caveBoss, assaultResolution.Boss);
            Assert.Equal(DungeonOutcome.Victory, assaultResolution.DungeonResult!.Outcome);
            Assert.True(caveBoss.IsDefeated);
            Assert.Equal(2, cave.ReachedFloor); // defeatedBoss.Floor(1) + 1
            Assert.False(forestBoss.IsDefeated);
            Assert.Equal(1, forest.ReachedFloor); // 森は依然として無傷
        }

        [Fact]
        public void DungeonExpedition_Gathering_AddsMaterialsToState()
        {
            // 探索（採取）任務の週次解決によって、state.Materials に指定素材が加算されること
            // （→ 第3の任務「探索（Gathering）」仕様）。
            var forestBoss = new FloorBoss { Name = "森のボス", Floor = 1, MaxHp = 999_999, CurrentHp = 999_999 };
            var forest = new DungeonField { Id = "forest", Name = "森", Order = 1, IsUnlocked = true, ReachedFloor = 1, Bosses = { forestBoss } };
            var state = new GameState { DungeonFields = { forest } };
            var system = BuildSystem();

            var party = PartyOf(MakeAdventurer(JobClass.Ranger, 40), MakeAdventurer(JobClass.Thief, 40));
            Assert.True(system.TryDispatchGathering(state, party, forest));
            Assert.Single(state.ActiveDungeonMissions);
            Assert.True(party.Members.All(m => m.IsDispatched));

            var resolution = Assert.Single(system.ProcessWeeklyMissions(state));

            Assert.Equal(DungeonMissionType.Gathering, resolution.MissionType);
            Assert.Null(resolution.Boss);
            Assert.NotNull(resolution.GatheringResult);
            var gathering = resolution.GatheringResult!;

            Assert.True(gathering.MaterialCount > 0);
            Assert.NotEmpty(gathering.MaterialId);
            Assert.True(state.Materials.ContainsKey(gathering.MaterialId));
            Assert.Equal(gathering.MaterialCount, state.Materials[gathering.MaterialId]);
            Assert.True(state.Gold >= gathering.GoldEarned); // 初期資金＋採取ゴールド

            Assert.Empty(state.ActiveDungeonMissions);
            Assert.True(party.Members.All(m => !m.IsDispatched));
            Assert.Equal(1, state.TotalDispatchCount);
            // 採取はボスを対象にしないため、森のボスは無傷のまま。
            Assert.False(forestBoss.IsDefeated);
        }

        [Fact]
        public void TryDispatchGathering_Fails_WhenFieldLocked_OrPartyUnavailable()
        {
            var lockedField = new DungeonField { Id = "cave", Name = "洞窟", Order = 2, IsUnlocked = false };
            var unlockedField = new DungeonField { Id = "forest", Name = "森", Order = 1, IsUnlocked = true };
            var state = new GameState { DungeonFields = { unlockedField, lockedField } };
            var system = BuildSystem();

            Assert.False(system.TryDispatchGathering(state, PartyOf(MakeAdventurer(JobClass.Ranger, 40)), lockedField));
            Assert.False(system.TryDispatchGathering(state, new Party(), unlockedField));

            Assert.Empty(state.ActiveDungeonMissions);
        }

        [Fact]
        public void TryCancel_ReleasesMembers_AndFreesTheSlot()
        {
            var (state, a, b, boss) = MakeState();
            var system = BuildSystem();
            system.TryDispatch(state, PartyOf(a, b), boss, DungeonMissionType.Scouting);

            Assert.True(system.TryCancel(state, state.ActiveDungeonMissions[0]));

            Assert.Empty(state.ActiveDungeonMissions);
            Assert.False(a.IsDispatched);
            Assert.True(QuestDispatchSystem.CanDispatch(state));
            Assert.Equal(0, state.TotalDispatchCount); // 取り消した出撃は累計に数えない
        }

        // ---------------- 週次解決 ----------------

        [Fact]
        public void ProcessWeeklyMissions_Scouting_RaisesIntel_AndBringsEveryoneHome()
        {
            // 1Fボスなので1週目は出発直後に扉前へ到着し、2週目に扉前で偵察する。撤退で全員帰還する。
            var (state, a, b, boss) = MakeState();
            var system = BuildSystem();
            system.TryDispatch(state, PartyOf(a, b), boss, DungeonMissionType.Scouting);
            Assert.True(Assert.Single(system.ProcessWeeklyMissions(state)).ArrivedAtBossDoor);

            var resolutions = system.ProcessWeeklyMissions(state);

            var resolution = Assert.Single(resolutions);
            Assert.Equal(DungeonMissionType.Scouting, resolution.MissionType);
            Assert.NotNull(resolution.ScoutingResult);
            Assert.Null(resolution.DungeonResult);
            Assert.Equal(0.0, resolution.IntelRateBefore);
            Assert.True(boss.IntelRate > 0.0);
            Assert.Equal(boss.IntelRate, resolution.ScoutingResult!.IntelRateAfter);
            Assert.Equal(ExpeditionStatus.AwaitingBossDecision, resolution.StatusAfter);
            Assert.Equal(0, state.TotalDispatchCount); // 帰還するまでは数えない

            Assert.NotNull(system.TryRetreat(state, state.ActiveDungeonMissions[0]));
            Assert.Empty(state.ActiveDungeonMissions);
            Assert.False(a.IsDispatched);
            Assert.False(b.IsDispatched);
            Assert.Equal(2, state.Adventurers.Count);
            Assert.Equal(1, state.TotalDispatchCount);
        }

        // ---------------- 道中進軍（調査任務の分岐、→ 03 §4.5.2） ----------------

        /// <summary>
        /// 道中進軍テスト用のフィールド。デフォルトのMakeStateとは異なり、ReachedFloorとボスの
        /// 階層を意図的に切り離せるようにしている（分岐A＝道中進軍を発生させるため）。
        /// </summary>
        private static (GameState State, DungeonField Field, FloorBoss NextBoss) MakeTraversalState(
            int reachedFloor, int nextBossFloor, params FloorBoss[] otherBosses)
        {
            var nextBoss = new FloorBoss { Name = $"第{nextBossFloor}階層のボス", Floor = nextBossFloor, MaxHp = 999_999, CurrentHp = 999_999 };
            var field = new DungeonField
            {
                Id = "test", Name = "テスト用フィールド", Order = 1, IsUnlocked = true,
                ReachedFloor = reachedFloor,
            };
            field.Bosses.AddRange(otherBosses);
            field.Bosses.Add(nextBoss);
            var state = new GameState { DungeonFields = { field } };
            return (state, field, nextBoss);
        }

        [Fact]
        public void DungeonTraversal_HighAgilityParty_AdvancesMultipleFloors()
        {
            // 高AGI/DEX部隊なら、道中調査1回で1階層だけでなく複数階層（電撃/迅速/通常進軍）を
            // 一気に進めること。目標ボスは十分遠くに置き、ストッパーに引っかからないようにする。
            var (state, field, nextBoss) = MakeTraversalState(reachedFloor: 1, nextBossFloor: 100);
            var party = PartyOf(MakeAdventurer(JobClass.Thief, 300), MakeAdventurer(JobClass.Ranger, 300));
            var system = BuildSystem();
            system.TryDispatch(state, party, nextBoss, DungeonMissionType.Scouting);

            var resolution = Assert.Single(system.ProcessWeeklyMissions(state));

            Assert.NotNull(resolution.TraversalResult);
            Assert.Null(resolution.ScoutingResult);
            Assert.Null(resolution.DungeonResult);
            Assert.True(resolution.TraversalResult!.FloorAfter - resolution.TraversalResult.FloorBefore >= 2,
                "高AGI/DEX部隊なら1階層ではなく複数階層（+2〜+4）進むはず");
            Assert.Equal(field.ReachedFloor, resolution.TraversalResult.FloorAfter);
            Assert.False(resolution.TraversalResult.StopperTriggered);
        }

        [Fact]
        public void DungeonTraversal_ClampsAtNextUndefeatedBossFloor()
        {
            // 1Fから進軍した際、走破力が十分（電撃進軍相当）であっても、未撃破の5Fボスで
            // 強制的に足止めされること（5Fを超えて9F等へは進めない）。
            var (state, field, boss5F) = MakeTraversalState(reachedFloor: 1, nextBossFloor: 5);
            var party = PartyOf(MakeAdventurer(JobClass.Thief, 300), MakeAdventurer(JobClass.Ranger, 300));
            var system = BuildSystem();
            system.TryDispatch(state, party, boss5F, DungeonMissionType.Scouting);

            var resolution = Assert.Single(system.ProcessWeeklyMissions(state));

            Assert.Equal(5, resolution.TraversalResult!.FloorAfter);
            Assert.Equal(5, field.ReachedFloor);
            Assert.True(resolution.TraversalResult.StopperTriggered);
            Assert.Same(boss5F, resolution.TraversalResult.TargetBoss);
        }

        [Fact]
        public void DungeonTraversal_PassesDefeatedBossFloor()
        {
            // 5Fボス撃破後の道中調査は、1Fから潜っても5Fで足止めされず、次の未撃破ボスである
            // 10Fを目標に素通りで進軍できること。撃破済み（完全解析済み）区間は3倍速で抜ける。
            var defeated5F = new FloorBoss { Name = "撃破済みの5Fボス", Floor = 5, MaxHp = 100, IsDefeated = true, IntelRate = 1.0 };
            var (state, field, boss10F) = MakeTraversalState(reachedFloor: 6, nextBossFloor: 10, defeated5F);
            var party = PartyOf(MakeAdventurer(JobClass.Thief, 300), MakeAdventurer(JobClass.Ranger, 300));
            var system = BuildSystem();
            system.TryDispatch(state, party, boss10F, DungeonMissionType.Scouting);

            var resolution = Assert.Single(system.ProcessWeeklyMissions(state));

            Assert.NotNull(resolution.TraversalResult);
            Assert.Equal(1, resolution.TraversalResult!.FloorBefore);
            Assert.True(resolution.TraversalResult.FloorAfter > 5, "5Fで止まらず、その先へ進めるはず");
            Assert.True(resolution.TraversalResult.FloorAfter <= 10); // 10Fボスでストッパーがかかる可能性はある
            Assert.Equal(Math.Max(6, resolution.TraversalResult.FloorAfter), field.ReachedFloor);
        }

        [Fact]
        public void DungeonScouting_AtBossFloor_PerformsIntelAnalysis()
        {
            // 扉前で判断待ちのまま週を越した部隊は、道中進軍ではなく既存のボス解析
            // （ScoutingResolver）を行い、IntelRateが上昇すること。到達階層・現在階層は動かない。
            var (state, field, boss) = MakeTraversalState(reachedFloor: 10, nextBossFloor: 10);
            var party = PartyOf(MakeAdventurer(JobClass.Ranger, 40), MakeAdventurer(JobClass.Scholar, 40));
            var system = BuildSystem();
            system.TryDispatch(state, party, boss, DungeonMissionType.Scouting);
            var mission = state.ActiveDungeonMissions[0];
            mission.Status = ExpeditionStatus.AwaitingBossDecision;
            mission.CurrentFloor = 10;
            mission.TargetedBoss = boss;
            mission.WeeksElapsed = 3;

            var resolution = Assert.Single(system.ProcessWeeklyMissions(state));

            Assert.NotNull(resolution.ScoutingResult);
            Assert.Null(resolution.TraversalResult);
            Assert.Null(resolution.DungeonResult);
            Assert.True(boss.IntelRate > 0.0);
            Assert.Equal(10, field.ReachedFloor);
            Assert.Equal(10, mission.CurrentFloor);
            Assert.Equal(ExpeditionStatus.AwaitingBossDecision, mission.Status);
        }

        [Fact]
        public void ProcessWeeklyMissions_RecklessAssault_ForceRetiresMembersIntoFallenRecord()
        {
            var (state, a, b, boss) = MakeState(MakeDeadlyBoss());
            state.WeekNumber = 12;
            var system = BuildSystem();
            system.TryDispatch(state, PartyOf(a, b), boss, DungeonMissionType.BossAssault);

            var resolution = Assert.Single(system.ProcessWeeklyMissions(state));

            Assert.NotNull(resolution.DungeonResult);
            Assert.Equal(2, resolution.DungeonResult!.ForceRetiredAdventurerIds.Count);
            Assert.Empty(state.Adventurers);
            Assert.Contains(a, state.FallenAdventurers);
            Assert.Contains(b, state.FallenAdventurers);
            Assert.Equal(12, a.FellAtWeek);
            Assert.False(a.IsDispatched);
        }

        [Fact]
        public void ProcessWeeklyMissions_Victory_AppliesFieldProgression_EndToEnd()
        {
            // DungeonFieldTests は ApplyFieldProgression を直接呼ぶ単体テストだが、
            // 実際のゲームプレイ経路（出撃→週次決算での自動解決）からも正しく呼ばれることを確認する。
            // field1のOrder==1・Floor==10のため、報酬付与に加えて出撃枠拡張（→ 「古代エルフの
            // 多頭通信術式」復元）も同時に発生する（→ Defeating_Forest_10F_Boss_Unlocks_Slot2_And_NextField
            // と同じ経路。ここではTryDispatch→ProcessWeeklyMissionsの実際の配線を確認する）。
            var boss = new FloorBoss { Name = "弱いボス", Floor = 10, MaxHp = 1, CurrentHp = 1, RewardGold = 300, RewardReputation = 7 };
            var field1 = new DungeonField { Id = "f1", Name = "第1フィールド", Order = 1, IsUnlocked = true, Bosses = { boss } };
            var field2 = new DungeonField { Id = "f2", Name = "第2フィールド", Order = 2, IsUnlocked = false };
            var state = new GameState { Gold = 0, Reputation = 0, DungeonFields = { field1, field2 } };
            var strongParty = PartyOf(MakeAdventurer(JobClass.Warrior, 200), MakeAdventurer(JobClass.Cleric, 200));

            var system = BuildSystem();
            Assert.True(system.TryDispatch(state, strongParty, boss, DungeonMissionType.BossAssault));

            var resolution = Assert.Single(system.ProcessWeeklyMissions(state));

            Assert.Equal(DungeonOutcome.Victory, resolution.DungeonResult!.Outcome);
            Assert.True(boss.IsDefeated);
            Assert.Equal(11, field1.ReachedFloor);
            Assert.True(field2.IsUnlocked); // Floor==10撃破で次フィールドが自動的に開く
            Assert.Equal(300, state.Gold);
            // 名声：報酬7が先に加算された後、出撃枠拡張の名声同期（→ Rank Eの昇格ラインまで
            // 引き上げ）で上書きされる（Math.Maxのため、報酬7より確実に大きい）。
            Assert.Equal(GuildRankBalance.GetThreshold(GuildRank.E).PromoteAt, state.Reputation);
            Assert.Equal(2, state.UnlockedSquadSlots);
            Assert.True(state.GuildRank >= GuildRank.E);
            Assert.Equal(2, resolution.SquadSlotsExpandedTo);
        }

        [Fact]
        public void ProcessWeeklyMissions_SecondPartyAgainstAlreadyDefeatedBoss_ReturnsWithoutResolution()
        {
            // 討伐に向かったボスが先行部隊に倒されていた場合、決戦判定を行わずに帰還する。
            // 道中調査の部隊は、そのボスに足止めされず先へ進軍を続ける（→ 複数週潜行型）。
            var (state, a, b, boss) = MakeState();
            state.UnlockedSquadSlots = 2;
            var system = BuildSystem();
            system.TryDispatch(state, PartyOf(a), boss, DungeonMissionType.BossAssault);
            system.TryDispatch(state, PartyOf(b), boss, DungeonMissionType.Scouting);
            boss.IsDefeated = true; // 先行部隊が撃破した状況を再現

            var resolutions = system.ProcessWeeklyMissions(state);

            var assault = Assert.Single(resolutions, r => r.Party.Members.Contains(a));
            Assert.Null(assault.DungeonResult);
            Assert.True(assault.ReturnedHome);
            Assert.False(a.IsDispatched);

            var traversal = Assert.Single(resolutions, r => r.Party.Members.Contains(b));
            Assert.NotNull(traversal.TraversalResult);
            Assert.False(traversal.TraversalResult!.StopperTriggered);
            Assert.True(b.IsDispatched); // まだ潜行中
        }

        // ---------------- 解決後の状態解除・道中進軍の出撃登録（2026年9月改訂） ----------------

        [Fact]
        public void DungeonExpeditionSystem_AfterGatheringResolution_PartyCanDispatchNextWeek()
        {
            // 採取任務から帰還した部隊は、翌週そのまま道中調査（深度開拓）へ再出撃できること。
            // 解決時に ActiveDungeonMission がリストから除去され、メンバーの IsDispatched が
            // 解除される（＝出撃枠も解放される）ことを、実際の再出撃まで通して確認する。
            var boss = new FloorBoss { Name = "第10階層のボス", Floor = 10, MaxHp = 999_999, CurrentHp = 999_999 };
            var field = new DungeonField
            {
                Id = "forest", Name = "翠緑の原生林", Order = 1, IsUnlocked = true, ReachedFloor = 1,
                Bosses = { boss },
            };
            var a = MakeAdventurer(JobClass.Ranger, 40);
            var b = MakeAdventurer(JobClass.Thief, 40);
            var state = new GameState { Adventurers = { a, b }, DungeonFields = { field } };
            var system = BuildSystem();

            Assert.True(system.TryDispatchGathering(state, PartyOf(a, b), field));
            Assert.True(a.IsDispatched);
            Assert.False(QuestDispatchSystem.CanDispatch(state)); // 1枠を採取が占有

            Assert.Single(system.ProcessWeeklyMissions(state));

            // 解決後：出撃予定は空、メンバーは待機中（出撃可能）へ戻り、枠も空く。
            Assert.Empty(state.ActiveDungeonMissions);
            Assert.False(a.IsDispatched);
            Assert.False(b.IsDispatched);
            Assert.True(a.IsAvailable);
            Assert.True(b.IsAvailable);
            Assert.True(QuestDispatchSystem.CanDispatch(state));

            // 翌週：同じ部隊で道中調査へ再出撃できる（ReachedFloor=1 < ボス10F → 道中進軍）。
            Assert.True(system.TryDispatch(state, PartyOf(a, b), boss, DungeonMissionType.Scouting));
            var resolution = Assert.Single(system.ProcessWeeklyMissions(state));
            Assert.NotNull(resolution.TraversalResult);
        }

        /// <summary>
        /// 道中進軍→扉前到達→撤退、扉前偵察→撤退、ボス討伐の3種を、出撃から帰還まで進める
        /// （→ 毎回1Fリセット・複数週潜行型）。調査出撃は扉前に着くまで週を進め、
        /// scoutAtDoor なら扉前でさらに1週偵察させてから撤退する。討伐は1週で決着して帰還する。
        /// </summary>
        private static (GameState State, Adventurer A, Adventurer B, DungeonExpeditionSystem System, FloorBoss Boss)
            DispatchAndResolve(DungeonMissionType missionType, int reachedFloor, bool scoutAtDoor = false)
        {
            var boss = new FloorBoss { Name = "第5階層の主", Floor = 5, MaxHp = 999_999, CurrentHp = 999_999, IntelRate = scoutAtDoor ? 0.0 : 1.0 };
            var field = new DungeonField
            {
                Id = "test", Name = "テスト用フィールド", Order = 1, IsUnlocked = true, ReachedFloor = reachedFloor,
                Bosses = { boss },
            };
            var a = MakeAdventurer(JobClass.Warrior, 60);
            var b = MakeAdventurer(JobClass.Ranger, 60);
            var state = new GameState { Adventurers = { a, b }, DungeonFields = { field } };
            var system = BuildSystem();

            Assert.True(system.TryDispatch(state, PartyOf(a, b), boss, missionType));
            Assert.True(a.IsDispatched);
            Assert.False(QuestDispatchSystem.CanDispatch(state));

            if (missionType == DungeonMissionType.BossAssault)
            {
                Assert.True(Assert.Single(system.ProcessWeeklyMissions(state)).ReturnedHome);
                return (state, a, b, system, boss);
            }

            var mission = state.ActiveDungeonMissions[0];
            for (int week = 0; week < 10 && mission.Status == ExpeditionStatus.Advancing; week++)
                system.ProcessWeeklyMissions(state);
            Assert.Equal(ExpeditionStatus.AwaitingBossDecision, mission.Status);
            if (scoutAtDoor)
                Assert.NotNull(Assert.Single(system.ProcessWeeklyMissions(state)).ScoutingResult);

            Assert.True(system.TryRetreat(state, mission)!.ReturnedHome);
            return (state, a, b, system, boss);
        }

        [Theory]
        [InlineData(DungeonMissionType.Scouting, 5, true)]     // 扉前でボス解析→撤退
        [InlineData(DungeonMissionType.Scouting, 1, false)]    // 道中進軍→扉前到達→撤退
        [InlineData(DungeonMissionType.BossAssault, 5, false)] // ボス討伐
        public void Expedition_Resolve_RestoresAdventurerStateToIdle(DungeonMissionType missionType, int reachedFloor, bool scoutAtDoor)
        {
            var (state, a, b, _, _) = DispatchAndResolve(missionType, reachedFloor, scoutAtDoor);

            // 生還者（＝ロースターに残っている者）は全員が待機中へ戻り、出撃可能になっている。
            foreach (var member in new[] { a, b }.Where(m => state.Adventurers.Contains(m)))
            {
                Assert.False(member.IsDispatched);
                Assert.True(member.CurrentHP > 0);
                Assert.True(member.IsAvailable || member.Injury == InjurySeverity.Severe);
            }
            // 除籍者はFallenAdventurersへ移り、ロースターには残らない（既存どおり）。
            Assert.All(state.FallenAdventurers, f => Assert.DoesNotContain(f, state.Adventurers));
        }

        [Theory]
        [InlineData(DungeonMissionType.Scouting, 5, true)]
        [InlineData(DungeonMissionType.Scouting, 1, false)]
        [InlineData(DungeonMissionType.BossAssault, 5, false)]
        public void Expedition_Resolve_FreesSquadSlot(DungeonMissionType missionType, int reachedFloor, bool scoutAtDoor)
        {
            var (state, a, b, system, _) = DispatchAndResolve(missionType, reachedFloor, scoutAtDoor);

            // 解決済みの出撃はリストから消え、使用中の枠は0に戻る。
            Assert.Empty(state.ActiveDungeonMissions);
            Assert.Equal(0, state.ActiveDispatches.Count + state.ActiveDungeonMissions.Count);
            Assert.True(QuestDispatchSystem.CanDispatch(state));

            // 次週：生還者でそのまま再出撃できる（採取はボスの撃破状況に左右されない）。
            var survivors = new[] { a, b }.Where(m => state.Adventurers.Contains(m) && m.IsAvailable).ToArray();
            if (survivors.Length > 0)
                Assert.True(system.TryDispatchGathering(state, PartyOf(survivors), state.DungeonFields[0]));
        }

        [Fact]
        public void ProcessWeeklyMissions_ReleasesOrphanedDispatchFlags()
        {
            // どの出撃にも属さずIsDispatchedだけが残った不整合（旧セーブ等）は、週次解決で待機中へ戻る。
            var (state, a, b, _) = MakeState();
            a.IsDispatched = true;

            BuildSystem().ProcessWeeklyMissions(state);

            Assert.False(a.IsDispatched);
            Assert.True(a.IsAvailable);
        }

        [Fact]
        public void DungeonExpeditionSystem_CanDispatchTraversal_WhenFloorBelowBoss()
        {
            // 調査出撃は道中進軍として登録・解決され、部隊は（最高到達階層の記録に関係なく）
            // 1階層から潜り始めて前進すること（→ 毎回1Fリセット・複数週潜行型）。
            var (state, field, boss10F) = MakeTraversalState(reachedFloor: 9, nextBossFloor: 10);
            var party = PartyOf(MakeAdventurer(JobClass.Thief, 60), MakeAdventurer(JobClass.Ranger, 60));
            var system = BuildSystem();

            Assert.True(QuestDispatchSystem.CanDispatch(state));
            Assert.True(system.TryDispatch(state, party, boss10F, DungeonMissionType.Scouting));

            var mission = Assert.Single(state.ActiveDungeonMissions);
            Assert.Equal(DungeonMissionType.Scouting, mission.MissionType);
            Assert.Same(boss10F, mission.Boss);
            Assert.Equal(1, mission.CurrentFloor);
            Assert.Equal(ExpeditionStatus.Advancing, mission.Status);

            var resolution = Assert.Single(system.ProcessWeeklyMissions(state));

            Assert.NotNull(resolution.TraversalResult); // 解析ではなく道中進軍として解決される
            Assert.Null(resolution.ScoutingResult);
            Assert.Equal(1, resolution.TraversalResult!.FloorBefore);
            Assert.True(mission.CurrentFloor > 1);
            Assert.Equal(9, field.ReachedFloor);        // 記録（9F）より浅い間は更新されない
            Assert.Single(state.ActiveDungeonMissions); // まだ潜行中＝出撃は続いている
        }

        [Fact]
        public void DungeonExpeditionSystem_CanDispatchTraversal_EvenWhenBossFullyAnalyzed()
        {
            // 解析率が100%のボスでも、まだその階層へ到達していなければ道中調査（深度開拓）には
            // 出撃できること。UI側の「完全解析済みなので調査不要」という抑止が道中進軍にまで
            // 及んでいた不整合の回帰テスト（→ DungeonPanel.OnDispatchPressed、2026年9月改訂）。
            var (state, field, boss10F) = MakeTraversalState(reachedFloor: 5, nextBossFloor: 10);
            boss10F.IntelRate = 1.0;
            var party = PartyOf(MakeAdventurer(JobClass.Thief, 60), MakeAdventurer(JobClass.Ranger, 60));
            var system = BuildSystem();

            Assert.True(system.TryDispatch(state, party, boss10F, DungeonMissionType.Scouting));
            var resolution = Assert.Single(system.ProcessWeeklyMissions(state));

            Assert.NotNull(resolution.TraversalResult);
            Assert.True(field.ReachedFloor > 5, "完全解析済みでも道中進軍で深度は前進するはず");
        }

        // ---------------- 撃破報酬（ゴールド・名声・素材ドロップ、2026年9月新設） ----------------

        [Fact]
        public void BossDefeat_GrantsGoldReputationAndMaterials()
        {
            var boss = new FloorBoss
            {
                Name = "報酬確認用ボス", Floor = 5, MaxHp = 1, CurrentHp = 1,
                RewardGold = 800, RewardReputation = 15,
                RewardMaterialId = MaterialIds.ForestSpore, RewardMaterialCount = 4,
            };
            var field = new DungeonField { Id = "f1", Name = "テスト用フィールド", Order = 1, IsUnlocked = true, Bosses = { boss } };
            var state = new GameState { Gold = 0, Reputation = 0, DungeonFields = { field } };
            var strongParty = PartyOf(MakeAdventurer(JobClass.Warrior, 200), MakeAdventurer(JobClass.Cleric, 200));
            var system = BuildSystem();

            Assert.True(system.TryDispatch(state, strongParty, boss, DungeonMissionType.BossAssault));
            var resolution = Assert.Single(system.ProcessWeeklyMissions(state));

            Assert.Equal(DungeonOutcome.Victory, resolution.DungeonResult!.Outcome);
            Assert.Equal(800, state.Gold);
            Assert.Equal(15, state.Reputation);
            Assert.Equal(4, state.Materials[MaterialIds.ForestSpore]);
        }

        [Fact]
        public void BossDefeat_GeneratesRewardAndProgressionLogs()
        {
            // 週報ログ（MainDashboard.LogDungeonMission）はこのメソッドの戻り値
            // （DungeonMissionResolution）だけを読んで組み立てられる。ログ文字列自体はGodot層の
            // 責務のため、ここではログが必要とするデータ（報酬・新フィールド開放・出撃枠拡張）が
            // すべて揃っていることを確認する。
            var boss = new FloorBoss
            {
                Name = "弱いボス", Floor = 10, MaxHp = 1, CurrentHp = 1,
                RewardGold = 300, RewardReputation = 7,
                RewardMaterialId = MaterialIds.ForestHerb, RewardMaterialCount = 3,
            };
            var field1 = new DungeonField { Id = "f1", Name = "第1フィールド", Order = 1, IsUnlocked = true, Bosses = { boss } };
            var field2 = new DungeonField { Id = "f2", Name = "第2フィールド", Order = 2, IsUnlocked = false };
            var state = new GameState { Gold = 0, Reputation = 0, DungeonFields = { field1, field2 } };
            var strongParty = PartyOf(MakeAdventurer(JobClass.Warrior, 200), MakeAdventurer(JobClass.Cleric, 200));
            var system = BuildSystem();

            Assert.True(system.TryDispatch(state, strongParty, boss, DungeonMissionType.BossAssault));
            var resolution = Assert.Single(system.ProcessWeeklyMissions(state));

            // 💰報奨・👑名声・📦素材（→ 週報の「報奨獲得」行）。
            Assert.Equal(300, resolution.Boss!.RewardGold);
            Assert.Equal(7, resolution.Boss.RewardReputation);
            Assert.Equal(MaterialIds.ForestHerb, resolution.Boss.RewardMaterialId);
            Assert.Equal(3, resolution.Boss.RewardMaterialCount);
            // 🗺新フィールド開放（→ 週報の「探索域拡大」行）。
            Assert.NotNull(resolution.FieldNewlyUnlocked);
            Assert.Equal("第2フィールド", resolution.FieldNewlyUnlocked!.Name);
            // 📡出撃枠拡張（→ 週報の「古代通信術式復元」行）。
            Assert.Equal(2, resolution.SquadSlotsExpandedTo);
        }

        [Fact]
        public void DefeatedBoss_CannotBeTargetedForAssault()
        {
            var (state, a, b, boss) = MakeState();
            boss.IsDefeated = true;
            var system = BuildSystem();

            Assert.False(system.TryDispatch(state, PartyOf(a, b), boss, DungeonMissionType.BossAssault));
            Assert.Empty(state.ActiveDungeonMissions);
            Assert.False(a.IsDispatched);
        }

        // ---------------- パーティ携行アイテムポーチ（2026年9月新設） ----------------

        [Fact]
        public void AssaultDispatch_DeductsGold_ForCarriedItems()
        {
            var (state, a, b, boss) = MakeState();
            state.Gold = 1000;
            var party = PartyOf(a, b);
            party.TryAddConsumable(ConsumableCatalog.AntidoteId);
            party.TryAddConsumable(ConsumableCatalog.CharmId);
            int expectedCost = ConsumableCatalog.Antidote.Price + ConsumableCatalog.Charm.Price;

            Assert.True(BuildSystem().TryDispatch(state, party, boss, DungeonMissionType.BossAssault));

            Assert.Equal(1000 - expectedCost, state.Gold);
        }

        [Fact]
        public void AssaultCancel_RefundsGold_ForCarriedItems()
        {
            var (state, a, b, boss) = MakeState();
            state.Gold = 1000;
            var party = PartyOf(a, b);
            party.TryAddConsumable(ConsumableCatalog.AntidoteId);
            party.TryAddConsumable(ConsumableCatalog.CharmId);
            int expectedCost = ConsumableCatalog.Antidote.Price + ConsumableCatalog.Charm.Price;
            var system = BuildSystem();
            Assert.True(system.TryDispatch(state, party, boss, DungeonMissionType.BossAssault));
            Assert.Equal(1000 - expectedCost, state.Gold);

            var mission = Assert.Single(state.ActiveDungeonMissions);
            Assert.True(system.TryCancel(state, mission));

            Assert.Equal(1000, state.Gold);
        }

        [Fact]
        public void AssaultDispatch_Fails_WhenInsufficientGoldForItems()
        {
            var (state, a, b, boss) = MakeState();
            state.Gold = ConsumableCatalog.Charm.Price - 1;
            var party = PartyOf(a, b);
            party.TryAddConsumable(ConsumableCatalog.CharmId);

            Assert.False(BuildSystem().TryDispatch(state, party, boss, DungeonMissionType.BossAssault));

            Assert.Empty(state.ActiveDungeonMissions);
            Assert.Equal(ConsumableCatalog.Charm.Price - 1, state.Gold); // 失敗時は減算されない
            Assert.False(a.IsDispatched);
        }

        [Fact]
        public void GimmickMitigation_Satisfied_ByCarriedItem()
        {
            // 対策職・対策ステータスのどちらも足りない部隊でも、対応する携行アイテムがあれば
            // ギミック対策が成立し、未対策ペナルティ（大ダメージ・即死）を回避できる
            // （→ DungeonResolver.IsCountered、OR条件の3つ目の対策口）。
            var boss = new FloorBoss
            {
                Name = "重装甲の番人", Floor = 1, MaxHp = 1, CurrentHp = 1,
                Gimmicks = { new BossGimmick
                {
                    Type = BossGimmickType.HeavyArmor, DangerLevel = 5,
                    RequiredCounterRole = JobClass.Mage, RequiredCounterStat = "STR", RequiredCounterStatThreshold = 9999,
                    RequiredItemId = ConsumableCatalog.AcidFlaskId,
                }},
            };
            var field = new DungeonField { Id = "f1", Name = "テスト用フィールド", Order = 1, IsUnlocked = true, Bosses = { boss } };
            // Warrior1名：Mageでもなく、STR合算は要求値(9999)に遠く届かない（職業・ステータス双方の対策口が不成立）。
            var weakling = MakeAdventurer(JobClass.Warrior, 10);
            var state = new GameState { Adventurers = { weakling }, DungeonFields = { field }, Gold = 1000 };
            var party = PartyOf(weakling);
            party.TryAddConsumable(ConsumableCatalog.AcidFlaskId);
            var system = BuildSystem();

            Assert.True(system.TryDispatch(state, party, boss, DungeonMissionType.BossAssault));
            var resolution = Assert.Single(system.ProcessWeeklyMissions(state));

            Assert.Contains(BossGimmickType.HeavyArmor, resolution.DungeonResult!.CounteredGimmicks);
            Assert.Empty(resolution.DungeonResult.UncounteredGimmicks);
            Assert.Equal(1.0, resolution.DungeonResult.DamageMultiplier); // 全対策済みなので倍率は据え置き
        }

        [Fact]
        public void WeekProcessingSystem_ResolvesDungeonMissions_AndStopsAutoSkipOnForcedRetirement()
        {
            var (state, a, b, boss) = MakeState(MakeDeadlyBoss());
            state.Gold = 100_000;
            BuildSystem().TryDispatch(state, PartyOf(a, b), boss, DungeonMissionType.BossAssault);

            var growth = new GrowthSystem(new AlwaysMinRng());
            var economy = new EconomySystem();
            var satisfaction = new SatisfactionSystem();
            var compatibility = new CompatibilitySystem(new AlwaysMinRng());
            var week = new WeekProcessingSystem(
                new QuestDispatchSystem(new QuestResolver(new AlwaysMinRng()), growth, economy, satisfaction, compatibility),
                new GuildRankSystem(), new SecuritySystem(new AlwaysMinRng()), new QuestBoardSystem(new AlwaysMinRng()),
                economy, new SubsidySystem(), new TrainingSystem(), new InjuryRecoverySystem(), new RestRecoverySystem(),
                growth, satisfaction, new AgingSystem(new AlwaysMinRng()), new FacilitySystem(), new DefeatSystem(),
                new RecruitmentSystem(new AlwaysMinRng()),
                dungeonExpeditionSystem: BuildSystem());

            var settlement = week.ProcessWeek(state);

            Assert.Single(settlement.DungeonMissionResolutions);
            Assert.True(settlement.Flags.DeathOrPermanentInjuryOccurred);
            Assert.Empty(state.ActiveDungeonMissions);
        }

        // ---------------- 毎回1Fリセット・複数週潜行型（2026年9月新設） ----------------

        private static WeekProcessingSystem BuildWeekSystem(DungeonExpeditionSystem expedition)
        {
            var growth = new GrowthSystem(new AlwaysMinRng());
            var economy = new EconomySystem();
            var satisfaction = new SatisfactionSystem();
            var compatibility = new CompatibilitySystem(new AlwaysMinRng());
            return new WeekProcessingSystem(
                new QuestDispatchSystem(new QuestResolver(new AlwaysMinRng()), growth, economy, satisfaction, compatibility),
                new GuildRankSystem(), new SecuritySystem(new AlwaysMinRng()), new QuestBoardSystem(new AlwaysMinRng()),
                economy, new SubsidySystem(), new TrainingSystem(), new InjuryRecoverySystem(), new RestRecoverySystem(),
                growth, satisfaction, new AgingSystem(new AlwaysMinRng()), new FacilitySystem(), new DefeatSystem(),
                new RecruitmentSystem(new AlwaysMinRng()),
                dungeonExpeditionSystem: expedition);
        }

        /// <summary>
        /// 素材定義のある「forest」フィールドに、指定階層の未撃破ボスを1体置いた状態。
        /// 高AGI/DEXの2名を待機させる（→ 電撃進軍：1週あたり+4階層）。
        /// </summary>
        private static (GameState State, DungeonField Field, FloorBoss Boss, Adventurer A, Adventurer B) MakeDeepDiveState(
            int bossFloor, int bossHp = 999_999)
        {
            var boss = new FloorBoss { Name = $"第{bossFloor}階層の主", Floor = bossFloor, MaxHp = bossHp, CurrentHp = bossHp };
            var field = new DungeonField { Id = "forest", Name = "翠緑の原生林", Order = 1, IsUnlocked = true, Bosses = { boss } };
            var a = MakeAdventurer(JobClass.Thief, 300);
            var b = MakeAdventurer(JobClass.Warrior, 300);
            var state = new GameState { Adventurers = { a, b }, DungeonFields = { field }, Gold = 10_000 };
            return (state, field, boss, a, b);
        }

        [Fact]
        public void Expedition_StopsAtBossFloor_AndSetsAwaitingDecision()
        {
            var (state, field, boss, a, b) = MakeDeepDiveState(bossFloor: 9);
            var system = BuildSystem();
            Assert.True(system.TryDispatch(state, PartyOf(a, b), boss, DungeonMissionType.Scouting));
            var mission = state.ActiveDungeonMissions[0];

            // 1週目：1F→5F（まだボス階層ではないので進軍を続ける）。
            var week1 = Assert.Single(system.ProcessWeeklyMissions(state));
            Assert.False(week1.ArrivedAtBossDoor);
            Assert.Equal(ExpeditionStatus.Advancing, mission.Status);
            Assert.Equal(5, mission.CurrentFloor);

            // 2週目：5F→9F（未撃破ボス階層でストップし、判断待ちになる）。
            var week2 = Assert.Single(system.ProcessWeeklyMissions(state));
            Assert.True(week2.ArrivedAtBossDoor);
            Assert.Equal(ExpeditionStatus.AwaitingBossDecision, mission.Status);
            Assert.Equal(ExpeditionStatus.AwaitingBossDecision, week2.StatusAfter);
            Assert.Equal(9, mission.CurrentFloor);
            Assert.Same(boss, mission.TargetedBoss);
            Assert.Null(week2.DungeonResult); // 自動では突入しない
            Assert.False(boss.IsDefeated);
            Assert.True(a.IsDispatched);      // 部隊は扉前に留まったまま

            // 指令が無いまま週を越しても突入はせず、扉前で偵察を続ける。
            var week3 = Assert.Single(system.ProcessWeeklyMissions(state));
            Assert.Null(week3.DungeonResult);
            Assert.NotNull(week3.ScoutingResult);
            Assert.Equal(9, mission.CurrentFloor);
            Assert.Equal(ExpeditionStatus.AwaitingBossDecision, mission.Status);
        }

        [Fact]
        public void Expedition_AutoSkip_StopsWhenAwaitingBossDecision()
        {
            var (state, _, boss, a, b) = MakeDeepDiveState(bossFloor: 9);
            var expedition = BuildSystem();
            Assert.True(expedition.TryDispatch(state, PartyOf(a, b), boss, DungeonMissionType.Scouting));

            var results = new AutoSkipService(BuildWeekSystem(expedition)).AutoSkip(state, maxWeeks: 50);

            // 1週目（1F→5F）では止まらず、2週目（5F→9F、扉前到達）で止まる。
            Assert.Equal(2, results.Count);
            Assert.False(results[0].ShouldStopAutoSkip);
            Assert.True(results[1].BossDoorReached);
            Assert.True(results[1].ShouldStopAutoSkip);
            Assert.Equal(ExpeditionStatus.AwaitingBossDecision, state.ActiveDungeonMissions[0].Status);
        }

        [Fact]
        public void Expedition_Retreat_ReturnsPartyToIdle_AndResetsFloorToOneOnNextDispatch()
        {
            var (state, field, boss, a, b) = MakeDeepDiveState(bossFloor: 9);
            var system = BuildSystem();
            Assert.True(system.TryDispatch(state, PartyOf(a, b), boss, DungeonMissionType.Scouting));
            var mission = state.ActiveDungeonMissions[0];
            system.ProcessWeeklyMissions(state);
            system.ProcessWeeklyMissions(state);
            Assert.Equal(ExpeditionStatus.AwaitingBossDecision, mission.Status);

            int carriedGold = mission.CarriedGold;
            var carriedMaterials = new Dictionary<string, int>(mission.CarriedMaterials);
            Assert.Equal(8 * DungeonTraversalBalance.LootGoldPerFloor, carriedGold); // 1F→9Fの8階層分
            Assert.NotEmpty(carriedMaterials);
            int goldBefore = state.Gold;

            var retreat = system.TryRetreat(state, mission);

            // 討伐は行わずに即時帰還：拾得物をギルドへ格納し、全員が待機中へ戻り、枠も空く。
            Assert.NotNull(retreat);
            Assert.True(retreat!.ReturnedHome);
            Assert.Null(retreat.DungeonResult);
            Assert.False(boss.IsDefeated);
            Assert.Equal(goldBefore + carriedGold, state.Gold);
            Assert.Equal(carriedGold, retreat.DepositedGold);
            foreach (var kv in carriedMaterials)
                Assert.Equal(kv.Value, state.Materials[kv.Key]);
            Assert.Empty(state.ActiveDungeonMissions);
            Assert.False(a.IsDispatched);
            Assert.False(b.IsDispatched);
            Assert.True(a.IsAvailable);
            Assert.True(QuestDispatchSystem.CanDispatch(state));
            Assert.Equal(9, field.ReachedFloor); // 最高到達階層の記録は残る

            // 次回の出撃は、記録（9F）に関係なく必ず1階層から再スタートする。
            Assert.True(system.TryDispatch(state, PartyOf(a, b), boss, DungeonMissionType.Scouting));
            var next = state.ActiveDungeonMissions[0];
            Assert.Equal(1, next.CurrentFloor);
            Assert.Equal(ExpeditionStatus.Advancing, next.Status);
            Assert.Equal(0, next.CarriedGold);
            Assert.Equal(1, Assert.Single(system.ProcessWeeklyMissions(state)).TraversalResult!.FloorBefore);
        }

        [Fact]
        public void Expedition_FightBoss_ResolvesCombatNextWeek()
        {
            var (state, field, boss, a, b) = MakeDeepDiveState(bossFloor: 5, bossHp: 1);
            var system = BuildSystem();
            Assert.True(system.TryDispatch(state, PartyOf(a, b), boss, DungeonMissionType.Scouting));
            var mission = state.ActiveDungeonMissions[0];
            Assert.True(Assert.Single(system.ProcessWeeklyMissions(state)).ArrivedAtBossDoor);
            int carriedGold = mission.CarriedGold;

            // 挑む指令：携行ポーチの代金を即座に支払い、EngagingBossへ。この時点ではまだ判定しない。
            int goldBefore = state.Gold;
            Assert.True(system.TryEngageBoss(state, mission, new[] { ConsumableCatalog.CharmId }));
            Assert.Equal(ExpeditionStatus.EngagingBoss, mission.Status);
            Assert.Equal(goldBefore - ConsumableCatalog.FindById(ConsumableCatalog.CharmId)!.Price, state.Gold);
            Assert.Contains(ConsumableCatalog.CharmId, mission.Party.ConsumableItemIds);
            Assert.False(boss.IsDefeated);

            // 次週の決算で決戦判定が行われ、決着後は帰還する。
            int goldBeforeFight = state.Gold;
            var fight = Assert.Single(system.ProcessWeeklyMissions(state));

            Assert.NotNull(fight.DungeonResult);
            Assert.Equal(DungeonOutcome.Victory, fight.DungeonResult!.Outcome);
            Assert.True(boss.IsDefeated);
            Assert.True(fight.ReturnedHome);
            Assert.Empty(state.ActiveDungeonMissions);
            Assert.False(a.IsDispatched);
            Assert.Equal(carriedGold, fight.DepositedGold);
            Assert.Equal(goldBeforeFight + carriedGold + boss.RewardGold, state.Gold);
            Assert.Equal(6, field.ReachedFloor); // 撃破で記録が更新される
        }

        [Fact]
        public void TryEngageBoss_Fails_WhenNotAwaitingDecision_OrGoldShort()
        {
            var (state, _, boss, a, b) = MakeDeepDiveState(bossFloor: 9);
            var system = BuildSystem();
            system.TryDispatch(state, PartyOf(a, b), boss, DungeonMissionType.Scouting);
            var mission = state.ActiveDungeonMissions[0];

            Assert.False(system.TryEngageBoss(state, mission, Array.Empty<string>())); // まだ進軍中
            Assert.Null(system.TryRetreat(state, mission));                          // 出発前は撤退ではなく取り消し

            system.ProcessWeeklyMissions(state);
            system.ProcessWeeklyMissions(state);
            state.Gold = 0;
            Assert.False(system.TryEngageBoss(state, mission, new[] { ConsumableCatalog.CharmId }));
            Assert.Equal(ExpeditionStatus.AwaitingBossDecision, mission.Status);
            Assert.Empty(mission.Party.ConsumableItemIds);
            Assert.False(system.TryCancel(state, mission)); // 出発済みの部隊は取り消せない
        }

        /// <summary>9Fボスの扉前まで潜行させた状態（2週）を作る。</summary>
        private static (GameState State, FloorBoss Boss, Adventurer A, Adventurer B, DungeonExpeditionSystem System, ActiveDungeonMission Mission)
            ReachBossDoor()
        {
            var (state, _, boss, a, b) = MakeDeepDiveState(bossFloor: 9);
            var system = BuildSystem();
            Assert.True(system.TryDispatch(state, PartyOf(a, b), boss, DungeonMissionType.Scouting));
            var mission = state.ActiveDungeonMissions[0];
            system.ProcessWeeklyMissions(state);
            system.ProcessWeeklyMissions(state);
            Assert.Equal(ExpeditionStatus.AwaitingBossDecision, mission.Status);
            return (state, boss, a, b, system, mission);
        }

        [Fact]
        public void TryEngageBoss_DeductsPouchCost_And_TransitionsToEngaging()
        {
            var (state, boss, a, _, system, mission) = ReachBossDoor();
            var pouch = new[] { ConsumableCatalog.AntidoteId, ConsumableCatalog.CharmId };
            int expectedCost = DungeonExpeditionSystem.CalculateConsumableCost(pouch);
            int goldBefore = state.Gold;

            Assert.True(system.TryEngageBoss(state, mission, pouch));

            Assert.True(expectedCost > 0);
            Assert.Equal(goldBefore - expectedCost, state.Gold);
            Assert.Equal(ExpeditionStatus.EngagingBoss, mission.Status);
            Assert.Equal(pouch, mission.Party.ConsumableItemIds);
            Assert.Same(boss, mission.TargetedBoss);
            Assert.True(a.IsDispatched);          // 突入待ち：まだ帰還していない
            Assert.False(boss.IsDefeated);        // 決戦は次週の決算
        }

        [Fact]
        public void TryEngageBoss_Fails_WhenInsufficientGold()
        {
            var (state, _, _, _, system, mission) = ReachBossDoor();
            var pouch = new[] { ConsumableCatalog.AntidoteId, ConsumableCatalog.CharmId };
            state.Gold = DungeonExpeditionSystem.CalculateConsumableCost(pouch) - 1;
            int goldBefore = state.Gold;

            Assert.False(system.TryEngageBoss(state, mission, pouch));

            Assert.Equal(goldBefore, state.Gold);                           // 引き落とされない
            Assert.Equal(ExpeditionStatus.AwaitingBossDecision, mission.Status); // 扉前で待機のまま
            Assert.Empty(mission.Party.ConsumableItemIds);                   // ポーチも積まれない

            // ポーチを空にすれば（0G）同じ所持金でも挑める。
            Assert.True(system.TryEngageBoss(state, mission, Array.Empty<string>()));
            Assert.Equal(goldBefore, state.Gold);
        }

        [Fact]
        public void TryRetreat_AddsLootToGuild_And_ResetsPartyToIdle()
        {
            var (state, _, a, b, system, mission) = ReachBossDoor();
            int carriedGold = mission.CarriedGold;
            var carriedMaterials = new Dictionary<string, int>(mission.CarriedMaterials);
            int hpA = a.CurrentHP;
            int goldBefore = state.Gold;
            Assert.True(carriedGold > 0);
            Assert.NotEmpty(carriedMaterials);

            var resolution = system.TryRetreat(state, mission);

            Assert.NotNull(resolution);
            Assert.Equal(goldBefore + carriedGold, state.Gold);
            foreach (var kv in carriedMaterials)
                Assert.Equal(kv.Value, state.Materials[kv.Key]);
            Assert.Equal(carriedGold, resolution!.DepositedGold);
            Assert.Empty(state.ActiveDungeonMissions);
            Assert.False(a.IsDispatched);
            Assert.False(b.IsDispatched);
            Assert.True(a.IsAvailable && b.IsAvailable);
            Assert.Equal(hpA, a.CurrentHP);                 // 撤退自体ではHPを失わない
            Assert.True(QuestDispatchSystem.CanDispatch(state)); // 出撃枠も即座に空く
        }

        [Fact]
        public void RoundTrip_PreservesExpeditionProgress_ThroughJson()
        {
            var (state, _, boss, a, b) = MakeDeepDiveState(bossFloor: 9);
            var system = BuildSystem();
            system.TryDispatch(state, PartyOf(a, b), boss, DungeonMissionType.Scouting);
            system.ProcessWeeklyMissions(state);
            system.ProcessWeeklyMissions(state);
            var original = state.ActiveDungeonMissions[0];

            var json = JsonSerializer.Serialize(state.ToSaveData());
            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!);

            var mission = Assert.Single(restored.ActiveDungeonMissions);
            Assert.Equal(ExpeditionStatus.AwaitingBossDecision, mission.Status);
            Assert.Equal(9, mission.CurrentFloor);
            Assert.Same(restored.DungeonFields[0].Bosses[0], mission.TargetedBoss);
            Assert.Equal(original.WeeksElapsed, mission.WeeksElapsed);
            Assert.Equal(original.CarriedGold, mission.CarriedGold);
            Assert.Equal(original.CarriedMaterials, mission.CarriedMaterials);
        }

        // ---------------- 迷宮調査（Survey、2026年9月新設） ----------------

        [Fact]
        public void DungeonExpeditionSystem_DispatchScouting_IncreasesIntelRateOnSettlement()
        {
            // 「🔍 迷宮調査に出撃」：潜行（1Fから）とは別の1週任務。次週の決算で対象ボスを調査し、
            // 解析率を加算して帰還する（出撃枠も空く）。
            var (state, field, boss, a, b) = MakeDeepDiveState(bossFloor: 10);
            var system = BuildSystem();

            Assert.True(system.TryDispatchSurvey(state, PartyOf(a, b), boss));
            var mission = Assert.Single(state.ActiveDungeonMissions);
            Assert.Equal(DungeonMissionType.Survey, mission.MissionType);
            Assert.True(a.IsDispatched);
            Assert.False(QuestDispatchSystem.CanDispatch(state));
            Assert.Equal(0.0, boss.IntelRate);

            var resolution = Assert.Single(system.ProcessWeeklyMissions(state));

            Assert.Equal(DungeonMissionType.Survey, resolution.MissionType);
            Assert.NotNull(resolution.ScoutingResult);
            Assert.Null(resolution.TraversalResult);
            Assert.True(boss.IntelRate > 0.0);
            Assert.Equal(boss.IntelRate, resolution.ScoutingResult!.IntelRateAfter, precision: 10);
            Assert.Equal(GuardTier.Abundant, resolution.ScoutingResult.GuardTier); // 能力値300の部隊 vs 要求35
            Assert.True(resolution.ReturnedHome);
            Assert.Empty(state.ActiveDungeonMissions);
            Assert.False(a.IsDispatched);
            Assert.True(QuestDispatchSystem.CanDispatch(state));
            Assert.Equal(1, field.ReachedFloor); // 調査では潜行しない＝到達階層は動かない
        }

        [Fact]
        public void DispatchSurvey_ClampsIntelRateAtOne_AndRefusesFullyAnalyzedBoss()
        {
            var (state, _, boss, a, b) = MakeDeepDiveState(bossFloor: 10);
            var system = BuildSystem();
            boss.IntelRate = 0.95;

            Assert.True(system.TryDispatchSurvey(state, PartyOf(a, b), boss));
            system.ProcessWeeklyMissions(state);

            Assert.Equal(1.0, boss.IntelRate, precision: 10); // 上限1.0でクランプ
            Assert.False(system.TryDispatchSurvey(state, PartyOf(a, b), boss)); // 完全解析済みは出撃不可
            Assert.Empty(state.ActiveDungeonMissions);
        }

        [Fact]
        public void DispatchSurvey_Fails_WhenSlotFull_BossDefeated_OrPartyUnavailable()
        {
            var (state, _, boss, a, b) = MakeDeepDiveState(bossFloor: 10);
            var system = BuildSystem();

            Assert.False(system.TryDispatchSurvey(state, new Party(), boss));
            a.IsDispatched = true;
            Assert.False(system.TryDispatchSurvey(state, PartyOf(a, b), boss));
            a.IsDispatched = false;

            Assert.True(system.TryDispatchSurvey(state, PartyOf(a), boss));
            Assert.False(system.TryDispatchSurvey(state, PartyOf(b), boss)); // 枠が埋まっている

            boss.IsDefeated = true;
            state.UnlockedSquadSlots = 2;
            Assert.False(system.TryDispatchSurvey(state, PartyOf(b), boss));
        }

        [Fact]
        public void RoundTrip_PreservesSurveyMission_ThroughJson()
        {
            var (state, _, boss, a, b) = MakeDeepDiveState(bossFloor: 10);
            BuildSystem().TryDispatchSurvey(state, PartyOf(a, b), boss);

            var json = JsonSerializer.Serialize(state.ToSaveData());
            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!);

            var mission = Assert.Single(restored.ActiveDungeonMissions);
            Assert.Equal(DungeonMissionType.Survey, mission.MissionType);
            Assert.Same(restored.DungeonFields[0].Bosses[0], mission.TargetedBoss);
        }

        // ---------------- GameState・セーブ/ロード ----------------

        [Fact]
        public void GetCurrentFloorBoss_ReturnsShallowestUndefeated()
        {
            // 後方互換性テスト：GetCurrentFloorBoss()は GetActiveField()?.GetNextActiveBoss() の
            // 薄いラッパーになったが、呼び出し側から見た挙動（最も浅い未撃破階層を返す）は変わらない。
            var state = new GameState { DungeonFields = SampleData.CreateDefaultFields() };
            Assert.Equal(10, state.GetCurrentFloorBoss()!.Floor); // 森の最初のボスは10階（→ BossIntervalFloors）

            var forest = state.DungeonFields.Single(f => f.Order == 1);
            forest.Bosses.Single(b => b.Floor == 10).IsDefeated = true;
            Assert.Equal(20, state.GetCurrentFloorBoss()!.Floor);

            foreach (var boss in forest.Bosses) boss.IsDefeated = true;
            // 森は制覇済みだが、洞窟（第2フィールド）はまだ開放されていないため攻略対象がない。
            Assert.Null(state.GetCurrentFloorBoss());
        }

        [Fact]
        public void DefaultFields_EveryGimmickHasRoleOrStatCounter()
        {
            // 携行アイテムをUIから持ち込む手段がまだ無いため、アイテムだけが対策口のギミックは攻略不能になる。
            var bosses = SampleData.CreateDefaultFields().SelectMany(f => f.Bosses).ToList();

            Assert.Equal(50, bosses.Count); // 5フィールド × 10体（→ BAL: dungeon.csv BossIntervalFloors）
            Assert.All(bosses.SelectMany(b => b.Gimmicks), g =>
                Assert.True(g.RequiredCounterRole.HasValue || !string.IsNullOrEmpty(g.RequiredCounterStat)));
            Assert.All(bosses, b => Assert.Equal(b.MaxHp, b.CurrentHp));
        }

        [Fact]
        public void RoundTrip_PreservesDungeonFieldsAndMissions_ThroughJson()
        {
            var (state, a, b, boss) = MakeState(MakeDeadlyBoss());
            boss.IntelRate = 0.75;
            var cleared = new FloorBoss { Name = "踏破済み", Floor = 0, MaxHp = 100, IsDefeated = true };
            state.DungeonFields[0].Bosses.Add(cleared);
            var party = PartyOf(a, b);
            party.TryAddConsumable(ConsumableCatalog.CharmId);
            BuildSystem().TryDispatch(state, party, boss, DungeonMissionType.BossAssault);

            var json = JsonSerializer.Serialize(state.ToSaveData());
            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!);

            var restoredBosses = restored.DungeonFields.SelectMany(f => f.Bosses).ToList();
            Assert.Equal(2, restoredBosses.Count);
            var restoredBoss = restoredBosses.Single(x => x.Id == boss.Id);
            Assert.Equal(0.75, restoredBoss.IntelRate);
            Assert.Equal(BossGimmickType.InstantKill, restoredBoss.Gimmicks.Single().Type);
            Assert.True(restoredBosses.Single(x => x.Id == cleared.Id).IsDefeated);

            var mission = Assert.Single(restored.ActiveDungeonMissions);
            Assert.Equal(DungeonMissionType.BossAssault, mission.MissionType);
            Assert.Same(restoredBoss, mission.Boss); // 解決時にDungeonField.Bosses側の状態が更新されるよう同一インスタンス
            Assert.All(mission.Party.Members, m => Assert.Contains(m, restored.Adventurers));
            Assert.Equal(new[] { ConsumableCatalog.CharmId }, mission.Party.ConsumableItemIds);
        }

        [Fact]
        public void RoundTrip_PreservesMaterialsAndGatheringMission_ThroughJson()
        {
            // 採取任務はボスを持たない（Boss=null）ため、FieldIdだけを頼りに復元できることを確認する
            // （→ 第3の任務「探索（Gathering）」仕様）。
            var forestBoss = new FloorBoss { Name = "森のボス", Floor = 1, MaxHp = 999_999, CurrentHp = 999_999 };
            var forest = new DungeonField { Id = "forest", Name = "森", Order = 1, IsUnlocked = true, ReachedFloor = 1, Bosses = { forestBoss } };
            var a = MakeAdventurer(JobClass.Ranger, 40);
            var state = new GameState { Adventurers = { a }, DungeonFields = { forest } };
            state.AddMaterial(MaterialIds.ForestHerb, 3);
            BuildSystem().TryDispatchGathering(state, PartyOf(a), forest);

            var json = JsonSerializer.Serialize(state.ToSaveData());
            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!);

            Assert.Equal(3, restored.Materials[MaterialIds.ForestHerb]);

            var mission = Assert.Single(restored.ActiveDungeonMissions);
            Assert.Equal(DungeonMissionType.Gathering, mission.MissionType);
            Assert.Null(mission.Boss);
            Assert.Equal("forest", mission.Field.Id);
            Assert.Same(restored.DungeonFields.Single(f => f.Id == "forest"), mission.Field); // 同一インスタンス
            Assert.All(mission.Party.Members, m => Assert.Contains(m, restored.Adventurers));
        }

        [Fact]
        public void FromSaveData_Throws_WhenMissionReferencesUnknownBoss()
        {
            var (state, a, b, boss) = MakeState();
            BuildSystem().TryDispatch(state, PartyOf(a, b), boss, DungeonMissionType.Scouting);
            var data = state.ToSaveData();
            data.DungeonFields.Clear();

            Assert.Throws<FormatException>(() => GameState.FromSaveData(data));
        }

        [Fact]
        public void FromSaveData_Throws_WhenGatheringMissionReferencesUnknownField()
        {
            var forest = new DungeonField { Id = "forest", Name = "森", Order = 1, IsUnlocked = true };
            var a = MakeAdventurer(JobClass.Ranger, 40);
            var state = new GameState { Adventurers = { a }, DungeonFields = { forest } };
            BuildSystem().TryDispatchGathering(state, PartyOf(a), forest);

            var data = state.ToSaveData();
            data.DungeonFields.Clear();

            Assert.Throws<FormatException>(() => GameState.FromSaveData(data));
        }

        // ---------------- 出撃前プレビュー ----------------

        [Fact]
        public void ScoutingPreviewScores_MatchResolverFormula()
        {
            var party = PartyOf(MakeAdventurer(JobClass.Thief, 30), MakeAdventurer(JobClass.Scholar, 20));

            // Σ(AGI+DEX)=100 ×1.0 ＋ 部隊長LDR30×0.5 ＝ 115、Σ(INT)=50（→ scouting.csv）
            Assert.Equal(115, ScoutingResolver.CalculateStealthScore(party), precision: 6);
            Assert.Equal(50, ScoutingResolver.CalculateAnalysisScore(party), precision: 6);
            Assert.Equal(0, ScoutingResolver.CalculateStealthScore(new Party()));
        }
    }
}
