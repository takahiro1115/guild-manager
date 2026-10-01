using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 2026年10月・§0.60のテスト：世代を重ねて強くなる段階研究（採用の質・成長の確率・秘薬の純度・培養槽の増設）、
    /// 研究の前提、魂魄融和の娘のPAを高い方の親へ寄せる基準。
    /// 実行方法: `dotnet test GuildManager.Core.Tests --filter FullyQualifiedName~ProgressionResearch`
    /// </summary>
    public class ProgressionResearchTests
    {
        private class FixedRng : IRng
        {
            private readonly int _value;
            public FixedRng(int value) { _value = value; }
            public int NextInt(int min, int max) => Math.Clamp(_value, min, max);
        }

        private class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        private static readonly string[] AllStats = { "STR", "AGI", "VIT", "MND", "DEX", "LDR", "INT" };

        private static GameState WithResearch(params string[] ids)
        {
            var state = new GameState();
            foreach (var id in ids) state.CompletedResearchIds.Add(id);
            return state;
        }

        // ---------------- CSV・前提 ----------------

        [Fact]
        public void StagedResearch_IsDefined_WithPrerequisites()
        {
            Assert.Equal(0.75, SoulFusionBalance.HigherParentWeight, precision: 6);

            Assert.Null(ResearchBalance.Find("res_scout_network_1")!.PrerequisiteId);
            Assert.Equal("res_scout_network_1", ResearchBalance.Find("res_scout_network_2")!.PrerequisiteId);
            Assert.Equal("res_scout_network_2", ResearchBalance.Find("res_scout_network_3")!.PrerequisiteId);
            Assert.Equal("res_training_method_2", ResearchBalance.Find("res_training_method_3")!.PrerequisiteId);
            Assert.Equal(ResearchIds.SoulFusion, ResearchBalance.Find("res_elixir_purity_1")!.PrerequisiteId);
            Assert.Equal(ResearchIds.SoulFusion, ResearchBalance.Find("res_culture_tank_2")!.PrerequisiteId);

            // 既存の研究には前提が無い
            Assert.Null(ResearchBalance.Find(ResearchIds.ScoutReagent)!.PrerequisiteId);
            Assert.Null(ResearchBalance.Find(ResearchIds.SoulFusion)!.PrerequisiteId);
            // 前提はすべて実在する研究を指す
            Assert.All(ResearchBalance.GetAll().Where(r => r.PrerequisiteId != null),
                r => Assert.NotNull(ResearchBalance.Find(r.PrerequisiteId!)));
            // 段階研究の素材もすべて実在する
            Assert.All(ResearchBalance.GetAll().SelectMany(r => r.RequiredMaterials.Keys),
                id => Assert.NotNull(MaterialBalance.Find(id)));
        }

        [Fact]
        public void Research_RequiresPrerequisite()
        {
            var tier2 = ResearchBalance.Find("res_scout_network_2")!;
            var state = new GameState { Gold = 1_000_000 };
            foreach (var (id, n) in tier2.RequiredMaterials) state.AddMaterial(id, n);

            Assert.False(ResearchSystem.IsPrerequisiteMet(state, tier2));
            Assert.False(ResearchSystem.CanStartResearch(state, tier2));
            Assert.False(ResearchSystem.CompleteResearch(state, tier2));

            state.CompletedResearchIds.Add("res_scout_network_1");
            Assert.True(ResearchSystem.CanStartResearch(state, tier2));
            Assert.True(ResearchSystem.CompleteResearch(state, tier2));
        }

        // ---------------- 採用の質 ----------------

        [Fact]
        public void RecruitPaBonus_RaisesCandidatePa_WithSameDraws()
        {
            Assert.Equal(0, RecruitmentSystem.GetRecruitPaBonus(new GameState()));
            Assert.Equal(10, RecruitmentSystem.GetRecruitPaBonus(WithResearch("res_scout_network_1", "res_scout_network_2")));

            var plain = new RecruitmentSystem(new SeededRng(42)).GenerateCandidates(new GameState(), candidateCount: 8);
            var boosted = new RecruitmentSystem(new SeededRng(42)).GenerateCandidates(WithResearch("res_scout_network_1"), candidateCount: 8);

            for (int i = 0; i < plain.Count; i++)
            {
                Assert.Equal(plain[i].Candidate.Name, boosted[i].Candidate.Name); // 乱数の引き方は同じ
                foreach (var stat in AllStats)
                {
                    int before = Pa(plain[i].Candidate, stat), after = Pa(boosted[i].Candidate, stat);
                    Assert.Equal(Math.Min(100, before + 5), after == 100 && before >= 95 ? 100 : after);
                    Assert.InRange(after, before, 100);
                }
            }
        }

        // ---------------- 成長の確率 ----------------

        [Fact]
        public void GrowthRateBonus_MultipliesTrainingAndExpeditionChance()
        {
            Assert.Equal(1.0, GrowthSystem.GetResearchGrowthMultiplier(new GameState()), precision: 6);
            Assert.Equal(1.2, GrowthSystem.GetResearchGrowthMultiplier(WithResearch("res_training_method_1")), precision: 6);
            Assert.Equal(1.4, GrowthSystem.GetResearchGrowthMultiplier(WithResearch("res_training_method_1", "res_training_method_2")), precision: 6);

            // 出撃成長：基礎42%。乱数45は研究なしでは外れ、研究I（×1.2＝50%）では当たる。
            int expectedChance = (int)Math.Round(DungeonBalance.GrowthBaseChancePercent * 1.2);
            Assert.InRange(45, DungeonBalance.GrowthBaseChancePercent + 1, expectedChance);
            Assert.Empty(ExpeditionGrowth(new GameState()));
            Assert.NotEmpty(ExpeditionGrowth(WithResearch("res_training_method_1")));

            // 訓練：新鋭期の基礎確率42%。同じく乱数45で、研究の有無で結果が分かれる。
            Assert.Empty(TrainingGrowth(new GameState()));
            Assert.NotEmpty(TrainingGrowth(WithResearch("res_training_method_1")));
        }

        private static List<GrowthEvent> ExpeditionGrowth(GameState state)
        {
            var a = new Adventurer { Age = 18, STR = 10, AGI = 10, VIT = 10, DEX = 10, PA_STR = 90, PA_AGI = 90, PA_VIT = 90, PA_DEX = 90 };
            a.CurrentHP = a.MaxHP;
            state.Adventurers.Add(a);
            var party = new Party();
            party.TryAdd(a);
            return new GrowthSystem(new FixedRng(45)).ApplyExpeditionGrowth(state, party, DungeonMissionType.Scouting, isBossVictory: false);
        }

        private static List<GrowthEvent> TrainingGrowth(GameState state)
        {
            var a = new Adventurer { Age = 18, STR = 10, VIT = 10, PA_STR = 90, PA_VIT = 90 };
            a.CurrentHP = a.MaxHP;
            state.Adventurers.Add(a);
            state.TrainingAssignments[a.Id] = FacilityType.WarriorHall;
            return new GrowthSystem(new FixedRng(45)).ProcessTrainingGrowth(state, new HashSet<Guid>());
        }

        // ---------------- 魂魄融和 ----------------

        [Theory]
        [InlineData(80, 61, 75)]   // 80×0.75＋61×0.25＝75.25
        [InlineData(61, 80, 75)]   // 順番によらない
        [InlineData(100, 100, 100)]
        [InlineData(90, 70, 85)]
        public void BasePa_LeansToHigherParent(int a, int b, int expected)
        {
            Assert.Equal(expected, SoulFusionSystem.BasePa(a, b));
        }

        [Fact]
        public void ElixirPurity_AddsToDaughterPa()
        {
            Assert.Equal(6, SoulFusionSystem.GetResearchPaBonus(WithResearch(ResearchIds.SoulFusion, "res_elixir_purity_1", "res_elixir_purity_2")));

            Adventurer Parent(string name) => new()
            {
                Name = name, JobClass = JobClass.Warrior,
                PA_STR = 70, PA_AGI = 70, PA_VIT = 70, PA_MND = 70, PA_DEX = 70, PA_LDR = 70, PA_INT = 70,
            };
            var a = Parent("A");
            var b = Parent("B");
            var plain = new SoulFusionSystem(new AlwaysMaxRng()).CreateChild(new GameState(), a, b, JobClass.Warrior, null).Child;
            var pure = new SoulFusionSystem(new AlwaysMaxRng()).CreateChild(WithResearch(ResearchIds.SoulFusion, "res_elixir_purity_1"), a, b, JobClass.Warrior, null).Child;

            Assert.All(AllStats, stat => Assert.Equal(80, Pa(plain, stat)));  // 70＋ばらつき10
            Assert.All(AllStats, stat => Assert.Equal(83, Pa(pure, stat)));   // ＋純度I 3
        }

        [Fact]
        public void CultureTankResearch_AllowsMoreCultures()
        {
            var state = WithResearch(ResearchIds.SoulFusion);
            Assert.Equal(1, SoulFusionSystem.GetTankCount(state));
            state.SoulFusionCultures.Add(new SoulFusionCulture());
            Assert.False(SoulFusionSystem.HasFreeTank(state));

            state.CompletedResearchIds.Add("res_culture_tank_2");
            Assert.Equal(2, SoulFusionSystem.GetTankCount(state));
            Assert.True(SoulFusionSystem.HasFreeTank(state));

            state.CompletedResearchIds.Add("res_culture_tank_3");
            Assert.Equal(3, SoulFusionSystem.GetTankCount(state));
        }

        private static int Pa(Adventurer a, string stat) => stat switch
        {
            "STR" => a.PA_STR, "AGI" => a.PA_AGI, "VIT" => a.PA_VIT, "MND" => a.PA_MND,
            "DEX" => a.PA_DEX, "LDR" => a.PA_LDR, _ => a.PA_INT,
        };
    }
}
