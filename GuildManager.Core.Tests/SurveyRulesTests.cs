using System.Linq;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 迷宮調査の対象のルール（§0.67）：解析が完全でない最も浅いボスから調べる（撃破済みでもよい）、
    /// 潜行が進んでいない階層（フィールドの最高到達階層より深い）は調べられない。
    /// </summary>
    public class SurveyRulesTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private static DungeonExpeditionSystem Expedition() => new(
            new ScoutingResolver(new AlwaysMinRng()), new DungeonResolver(new AlwaysMinRng()), new SatisfactionSystem(),
            new CompatibilitySystem(new AlwaysMinRng()), new DungeonTraversalResolver(new AlwaysMinRng()), new GatheringResolver(new AlwaysMinRng()));

        private static Adventurer Make(string name)
        {
            var a = new Adventurer { Name = name, JobClass = JobClass.Ranger, STR = 100, AGI = 100, VIT = 100, MND = 100, DEX = 100, LDR = 100, INT = 100 };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        /// <summary>10F・20F・30Fにボスがいるフィールド（最高到達階層を指定）。</summary>
        private static (GameState State, DungeonField Field, FloorBoss B10, FloorBoss B20, FloorBoss B30) Setup(int reachedFloor)
        {
            var b10 = new FloorBoss { Name = "10F", Floor = 10, MaxHp = 100, CurrentHp = 100 };
            var b20 = new FloorBoss { Name = "20F", Floor = 20, MaxHp = 100, CurrentHp = 100 };
            var b30 = new FloorBoss { Name = "30F", Floor = 30, MaxHp = 100, CurrentHp = 100 };
            var field = new DungeonField { Id = "forest", Name = "森", Order = 1, IsUnlocked = true, ReachedFloor = reachedFloor, Bosses = { b10, b20, b30 } };
            var state = new GameState { Adventurers = { Make("a"), Make("b") }, DungeonFields = { field }, Gold = 5000 };
            return (state, field, b10, b20, b30);
        }

        private static Party PartyOf(GameState state)
        {
            var party = new Party();
            foreach (var m in state.Adventurers) party.TryAdd(m);
            return party;
        }

        [Fact]
        public void Target_IsTheShallowestIncompleteBoss_EvenIfItWasAlreadyDefeated()
        {
            var (_, field, b10, b20, _) = Setup(reachedFloor: 25);
            Assert.Same(b10, ScoutingResolver.FindSurveyTarget(field));

            // 10Fを倒して先へ進んでいても、10Fの解析が未完ならまだ10Fを調べる
            b10.IsDefeated = true;
            Assert.Same(b10, ScoutingResolver.FindSurveyTarget(field));

            b10.IntelRate = 1.0;
            Assert.Same(b20, ScoutingResolver.FindSurveyTarget(field));
        }

        [Fact]
        public void Target_IsNullWhenEverythingIsFullyAnalyzed()
        {
            var (_, field, b10, b20, b30) = Setup(reachedFloor: 30);
            b10.IntelRate = b20.IntelRate = b30.IntelRate = 1.0;
            Assert.Null(ScoutingResolver.FindSurveyTarget(field));
            Assert.Null(ScoutingResolver.FindSurveyableBoss(field));
        }

        [Fact]
        public void UnreachedFloors_CannotBeSurveyed()
        {
            var (state, field, b10, _, _) = Setup(reachedFloor: 5); // 10Fの階層まで潜行が届いていない
            Assert.Same(b10, ScoutingResolver.FindSurveyTarget(field));
            Assert.Null(ScoutingResolver.FindSurveyableBoss(field));
            Assert.False(Expedition().TryDispatchSurvey(state, PartyOf(state), b10));

            field.ReachedFloor = 10; // 10Fの扉前まで届いた
            Assert.Same(b10, ScoutingResolver.FindSurveyableBoss(field));
            Assert.True(Expedition().TryDispatchSurvey(state, PartyOf(state), b10));
        }

        [Fact]
        public void DeeperBossCannotBeSurveyed_WhileAShallowerOneIsIncomplete()
        {
            var (state, _, b10, b20, _) = Setup(reachedFloor: 25);
            b10.IntelRate = 0.4;

            Assert.False(Expedition().TryDispatchSurvey(state, PartyOf(state), b20)); // 20Fは10Fの解析が済むまで調べられない
            Assert.True(Expedition().TryDispatchSurvey(state, PartyOf(state), b10));
        }

        [Fact]
        public void SurveyingADefeatedBoss_RaisesItsAnalysis()
        {
            var (state, _, b10, _, _) = Setup(reachedFloor: 25);
            b10.IsDefeated = true;
            var system = Expedition();
            Assert.True(system.TryDispatchSurvey(state, PartyOf(state), b10));

            var resolution = Assert.Single(system.ProcessWeeklyMissions(state));

            Assert.True(b10.IntelRate > 0);
            Assert.Equal(b10.IntelRate, resolution.ScoutingResult!.IntelRateAfter, precision: 10);
        }

        [Fact]
        public void ClearedField_WithoutAnyUndefeatedBoss_IsStillSurveyedUntilEveryFloorIsFullyAnalyzed()
        {
            // 解析はボス個人ではなく区間（階層すべて）のもの。全ボスを倒して「次のボス」がいなくても、解析が100%でなければ調査を進める
            var (state, field, b10, b20, b30) = Setup(reachedFloor: 100);
            b10.IsDefeated = b20.IsDefeated = b30.IsDefeated = true;
            Assert.Null(field.GetNextActiveBoss());
            b10.IntelRate = 1.0;
            b20.IntelRate = 0.3;
            b30.IntelRate = 0.0;

            var system = Expedition();
            Assert.Same(b20, ScoutingResolver.FindSurveyableBoss(field));
            Assert.True(system.TryDispatchSurvey(state, PartyOf(state), b20));
            system.ProcessWeeklyMissions(state);
            Assert.True(b20.IntelRate > 0.3);

            // 20Fの区間が100%になれば、次は30Fの区間
            b20.IntelRate = 1.0;
            Assert.Same(b30, ScoutingResolver.FindSurveyableBoss(field));
            b30.IntelRate = 1.0;
            Assert.Null(ScoutingResolver.FindSurveyTarget(field));
        }

        [Fact]
        public void Segment_IsFromTheFloorAfterTheShallowerBoss_ToThisBoss()
        {
            var (_, field, b10, b20, b30) = Setup(reachedFloor: 30);
            Assert.Equal(1, ScoutingResolver.SegmentStartFloor(field, b10));
            Assert.Equal(11, ScoutingResolver.SegmentStartFloor(field, b20));
            Assert.Equal("21〜30F", ScoutingResolver.SegmentLabel(field, b30));
        }

        [Fact]
        public void SurveyOrder_Gathers_WhenTheFloorToSurveyIsNotReachedYet()
        {
            var (state, field, b10, _, _) = Setup(reachedFloor: 5);
            var saved = new SavedParty { Name = "調査隊", MemberIds = state.Adventurers.Select(a => a.Id).ToList(), Order = SquadOrder.Survey, OrderFieldId = field.Id };
            state.SavedParties.Add(saved);

            new SquadOrderSystem(Expedition()).Execute(state);

            Assert.Equal(DungeonMissionType.Gathering, Assert.Single(state.ActiveDungeonMissions).MissionType);
            Assert.Equal(0, b10.IntelRate);
        }
    }
}
