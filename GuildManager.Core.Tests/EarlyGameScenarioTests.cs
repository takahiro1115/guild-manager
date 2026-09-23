using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 序盤の通しシナリオテスト。
    ///
    /// 個々のシステム単体ではなく、実運用と同じ WeekProcessingSystem 経由で検証する。
    /// 旧通常クエスト（掲示板・受託依頼）の撤去（2026年9月）以降、出撃先は大迷宮のみ。
    /// 旧仕様の「採取クエストで資金を貯める → 昇格試験 → 第2部隊枠」のフローは撤去済みで、
    /// ランク昇格・出撃枠拡張は大迷宮の節目ボス撃破（→ DungeonExpeditionSystem）のみが唯一のトリガー。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class EarlyGameScenarioTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        /// <summary>実運用（MainDashboard）と同じ構成で週次決算を組み立てる。</summary>
        private static (WeekProcessingSystem Week, DungeonExpeditionSystem Expedition) BuildSystems()
        {
            var growth = new GrowthSystem(new AlwaysMinRng());
            var economy = new EconomySystem();
            var satisfaction = new SatisfactionSystem();
            var compatibility = new CompatibilitySystem(new AlwaysMinRng());
            var recruitment = new RecruitmentSystem(new AlwaysMinRng());
            var expedition = new DungeonExpeditionSystem(
                new ScoutingResolver(new AlwaysMinRng()),
                new DungeonResolver(new AlwaysMinRng()),
                satisfaction,
                compatibility,
                new DungeonTraversalResolver(new AlwaysMinRng()),
                new GatheringResolver(new AlwaysMinRng()));

            var week = new WeekProcessingSystem(
                masterMoodSystem: new MasterMoodSystem(),
                economySystem: economy,
                trainingSystem: new TrainingSystem(),
                injuryRecoverySystem: new InjuryRecoverySystem(),
                restRecoverySystem: new RestRecoverySystem(),
                growthSystem: growth,
                satisfactionSystem: satisfaction,
                agingSystem: new AgingSystem(new AlwaysMinRng()),
                facilitySystem: new FacilitySystem(),
                defeatSystem: new DefeatSystem(),
                recruitmentSystem: recruitment,
                dungeonExpeditionSystem: expedition);

            return (week, expedition);
        }

        private static Adventurer MakeAdventurer(int stat)
        {
            var a = new Adventurer
            {
                STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat,
                WeeklyWage = 0,
                Placement = Placement.Front,
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

        [Fact]
        public void EarlyGame_SingleSquadSlot_BlocksSecondDispatch_UntilTheFirstReturns()
        {
            var (week, expedition) = BuildSystems();
            var a = MakeAdventurer(60);
            var b = MakeAdventurer(60);
            var state = new GameState { Gold = 0, Adventurers = { a, b }, DungeonFields = SampleData.CreateDefaultFields() };
            var forest = state.DungeonFields.First(f => f.IsUnlocked);

            // ---- 同時出撃枠は1。1枠の中で1〜4名を自由に割り振れる ----
            Assert.Equal(1, state.UnlockedSquadSlots);
            Assert.True(DungeonExpeditionSystem.CanDispatch(state));
            Assert.True(expedition.TryDispatchGathering(state, PartyOf(a), forest));
            Assert.False(expedition.TryDispatchGathering(state, PartyOf(b), forest),
                "同時出撃枠が1の間は2部隊目を出撃させられないはず");

            week.ProcessWeek(state);

            // 採取は1週で帰還し、枠が空く。累計出撃回数・素材・功績が記録される。
            Assert.Empty(state.ActiveDungeonMissions);
            Assert.Equal(1, state.TotalDispatchCount);
            Assert.NotEmpty(state.Materials);
            Assert.Equal(DungeonBalance.ContributionPerGathering, a.TotalContributionScore);
            Assert.True(expedition.TryDispatchGathering(state, PartyOf(a, b), forest));
            Assert.Equal(1, state.UnlockedSquadSlots); // 枠拡張は大迷宮の節目ボス撃破のみが唯一のトリガー
        }

        [Fact]
        public void EarlyGame_NoAdventurerIsLostToLowDangerMissions()
        {
            // 序盤の「即詰み防止」：採取・潜行（道中進軍）・扉前の偵察だけを回している限り、
            // どれだけ弱い部隊でもロースターから人が消えることはない（HP下限1）。
            var (week, expedition) = BuildSystems();
            // 満足度・週給は十分にしておき、契約交渉による退団（→ SatisfactionSystem）がこのテストに
            // 割り込まないようにする（ここで確かめたいのは任務による喪失の有無だけ）。
            var rookie = MakeAdventurer(5);
            rookie.Satisfaction = 100;
            rookie.WeeklyWage = 1_000;
            var state = new GameState { Gold = 100_000, Adventurers = { rookie }, DungeonFields = SampleData.CreateDefaultFields() };
            var forest = state.DungeonFields.First(f => f.IsUnlocked);

            for (int i = 0; i < 12; i++)
            {
                if (DungeonExpeditionSystem.CanDispatch(state) && rookie.IsAvailable)
                {
                    if (i % 2 == 0)
                        expedition.TryDispatchGathering(state, PartyOf(rookie), forest);
                    else
                        expedition.TryDispatch(state, PartyOf(rookie), forest.GetNextActiveBoss()!, DungeonMissionType.Scouting);
                }

                week.ProcessWeek(state);
            }

            Assert.Contains(rookie, state.Adventurers);
            Assert.Empty(state.FallenAdventurers);
            Assert.True(rookie.CurrentHP >= 1);
        }
    }
}
