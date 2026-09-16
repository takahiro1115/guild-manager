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
            new CompatibilitySystem(new AlwaysMinRng()));

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

        private static (GameState State, Adventurer A, Adventurer B, FloorBoss Boss) MakeState(FloorBoss? boss = null)
        {
            var a = MakeAdventurer(JobClass.Ranger, 40);
            var b = MakeAdventurer(JobClass.Scholar, 40);
            boss ??= new FloorBoss { Name = "階層の主", Floor = 1, MaxHp = 500, CurrentHp = 500 };
            var state = new GameState { Adventurers = { a, b }, FloorBosses = { boss } };
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
            var (state, a, b, boss) = MakeState();
            var system = BuildSystem();
            system.TryDispatch(state, PartyOf(a, b), boss, DungeonMissionType.Scouting);

            var resolutions = system.ProcessWeeklyMissions(state);

            var resolution = Assert.Single(resolutions);
            Assert.Equal(DungeonMissionType.Scouting, resolution.MissionType);
            Assert.NotNull(resolution.ScoutingResult);
            Assert.Null(resolution.DungeonResult);
            Assert.Equal(0.0, resolution.IntelRateBefore);
            Assert.True(boss.IntelRate > 0.0);
            Assert.Equal(boss.IntelRate, resolution.ScoutingResult!.IntelRateAfter);

            Assert.Empty(state.ActiveDungeonMissions);
            Assert.False(a.IsDispatched);
            Assert.False(b.IsDispatched);
            Assert.Equal(2, state.Adventurers.Count);
            Assert.Equal(1, state.TotalDispatchCount);
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
        public void ProcessWeeklyMissions_SecondPartyAgainstAlreadyDefeatedBoss_ReturnsWithoutResolution()
        {
            var (state, a, b, boss) = MakeState();
            state.UnlockedSquadSlots = 2;
            var system = BuildSystem();
            system.TryDispatch(state, PartyOf(a), boss, DungeonMissionType.BossAssault);
            system.TryDispatch(state, PartyOf(b), boss, DungeonMissionType.Scouting);
            boss.IsDefeated = true; // 先行部隊が撃破した状況を再現

            var resolutions = system.ProcessWeeklyMissions(state);

            Assert.Empty(resolutions);
            Assert.False(a.IsDispatched);
            Assert.False(b.IsDispatched);
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

        // ---------------- GameState・セーブ/ロード ----------------

        [Fact]
        public void GetCurrentFloorBoss_ReturnsShallowestUndefeated()
        {
            var state = new GameState { FloorBosses = SampleData.CreateFloorBosses() };
            Assert.Equal(1, state.GetCurrentFloorBoss()!.Floor);

            state.FloorBosses.Single(b => b.Floor == 1).IsDefeated = true;
            Assert.Equal(2, state.GetCurrentFloorBoss()!.Floor);

            foreach (var boss in state.FloorBosses) boss.IsDefeated = true;
            Assert.Null(state.GetCurrentFloorBoss());
        }

        [Fact]
        public void SampleFloorBosses_EveryGimmickHasRoleOrStatCounter()
        {
            // 携行アイテムをUIから持ち込む手段がまだ無いため、アイテムだけが対策口のギミックは攻略不能になる。
            var bosses = SampleData.CreateFloorBosses();

            Assert.Equal(bosses.Count, bosses.Select(b => b.Floor).Distinct().Count());
            Assert.All(bosses.SelectMany(b => b.Gimmicks), g =>
                Assert.True(g.RequiredCounterRole.HasValue || !string.IsNullOrEmpty(g.RequiredCounterStat)));
            Assert.All(bosses, b => Assert.Equal(b.MaxHp, b.CurrentHp));
        }

        [Fact]
        public void RoundTrip_PreservesFloorBossesAndDungeonMissions_ThroughJson()
        {
            var (state, a, b, boss) = MakeState(MakeDeadlyBoss());
            boss.IntelRate = 0.75;
            var cleared = new FloorBoss { Name = "踏破済み", Floor = 0, MaxHp = 100, IsDefeated = true };
            state.FloorBosses.Add(cleared);
            var party = PartyOf(a, b);
            party.TryAddConsumable(ConsumableCatalog.CharmId);
            BuildSystem().TryDispatch(state, party, boss, DungeonMissionType.BossAssault);

            var json = JsonSerializer.Serialize(state.ToSaveData());
            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!);

            Assert.Equal(2, restored.FloorBosses.Count);
            var restoredBoss = restored.FloorBosses.Single(x => x.Id == boss.Id);
            Assert.Equal(0.75, restoredBoss.IntelRate);
            Assert.Equal(BossGimmickType.InstantKill, restoredBoss.Gimmicks.Single().Type);
            Assert.True(restored.FloorBosses.Single(x => x.Id == cleared.Id).IsDefeated);

            var mission = Assert.Single(restored.ActiveDungeonMissions);
            Assert.Equal(DungeonMissionType.BossAssault, mission.MissionType);
            Assert.Same(restoredBoss, mission.Boss); // 解決時にFloorBosses側の状態が更新されるよう同一インスタンス
            Assert.All(mission.Party.Members, m => Assert.Contains(m, restored.Adventurers));
            Assert.Equal(new[] { ConsumableCatalog.CharmId }, mission.Party.ConsumableItemIds);
        }

        [Fact]
        public void FromSaveData_Throws_WhenMissionReferencesUnknownBoss()
        {
            var (state, a, b, boss) = MakeState();
            BuildSystem().TryDispatch(state, PartyOf(a, b), boss, DungeonMissionType.Scouting);
            var data = state.ToSaveData();
            data.FloorBosses.Clear();

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
