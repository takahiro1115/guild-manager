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
            new CompatibilitySystem(new AlwaysMinRng()),
            new DungeonTraversalResolver(new AlwaysMinRng()));

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
            // 5Fボス撃破後（ReachedFloor=6）の道中調査は、5Fで足止めされず、
            // 次の未撃破ボスである10Fを目標に素通りで進軍できること。
            var defeated5F = new FloorBoss { Name = "撃破済みの5Fボス", Floor = 5, MaxHp = 100, IsDefeated = true };
            var (state, field, boss10F) = MakeTraversalState(reachedFloor: 6, nextBossFloor: 10, defeated5F);
            var party = PartyOf(MakeAdventurer(JobClass.Thief, 300), MakeAdventurer(JobClass.Ranger, 300));
            var system = BuildSystem();
            system.TryDispatch(state, party, boss10F, DungeonMissionType.Scouting);

            var resolution = Assert.Single(system.ProcessWeeklyMissions(state));

            Assert.NotNull(resolution.TraversalResult);
            Assert.True(resolution.TraversalResult!.FloorAfter > 6, "5Fで止まらず、6Fより先へ進めるはず");
            Assert.True(resolution.TraversalResult.FloorAfter <= 10); // 10Fボスでストッパーがかかる可能性はある
            Assert.Equal(field.ReachedFloor, resolution.TraversalResult.FloorAfter);
        }

        [Fact]
        public void DungeonScouting_AtBossFloor_PerformsIntelAnalysis()
        {
            // ReachedFloorが未撃破ボスの階層と一致している時は、道中進軍ではなく
            // 既存のボス解析（ScoutingResolver）が実行され、IntelRateが上昇すること。
            var (state, field, boss) = MakeTraversalState(reachedFloor: 10, nextBossFloor: 10);
            var party = PartyOf(MakeAdventurer(JobClass.Ranger, 40), MakeAdventurer(JobClass.Scholar, 40));
            var system = BuildSystem();
            system.TryDispatch(state, party, boss, DungeonMissionType.Scouting);

            var resolution = Assert.Single(system.ProcessWeeklyMissions(state));

            Assert.NotNull(resolution.ScoutingResult);
            Assert.Null(resolution.TraversalResult);
            Assert.Null(resolution.DungeonResult);
            Assert.True(boss.IntelRate > 0.0);
            Assert.Equal(10, field.ReachedFloor); // ボス解析では到達階層は動かない
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
            Assert.Equal(7, state.Reputation);
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
            // 後方互換性テスト：GetCurrentFloorBoss()は GetActiveField()?.GetNextActiveBoss() の
            // 薄いラッパーになったが、呼び出し側から見た挙動（最も浅い未撃破階層を返す）は変わらない。
            var state = new GameState { DungeonFields = SampleData.CreateDefaultFields() };
            Assert.Equal(5, state.GetCurrentFloorBoss()!.Floor); // 森の最初のボスは5階

            var forest = state.DungeonFields.Single(f => f.Order == 1);
            forest.Bosses.Single(b => b.Floor == 5).IsDefeated = true;
            Assert.Equal(10, state.GetCurrentFloorBoss()!.Floor);

            foreach (var boss in forest.Bosses) boss.IsDefeated = true;
            // 森は制覇済みだが、洞窟（第2フィールド）はまだ開放されていないため攻略対象がない。
            Assert.Null(state.GetCurrentFloorBoss());
        }

        [Fact]
        public void DefaultFields_EveryGimmickHasRoleOrStatCounter()
        {
            // 携行アイテムをUIから持ち込む手段がまだ無いため、アイテムだけが対策口のギミックは攻略不能になる。
            var bosses = SampleData.CreateDefaultFields().SelectMany(f => f.Bosses).ToList();

            Assert.Equal(100, bosses.Count); // 5フィールド × 20体
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
        public void FromSaveData_Throws_WhenMissionReferencesUnknownBoss()
        {
            var (state, a, b, boss) = MakeState();
            BuildSystem().TryDispatch(state, PartyOf(a, b), boss, DungeonMissionType.Scouting);
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
