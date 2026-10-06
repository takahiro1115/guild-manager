using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 研究のツリー（§0.77）：系統・段・前提の並び、列（素材のフィールド）、まだ入っていないフィールドの研究を「？」にする判定のテスト。
    /// </summary>
    public class ResearchTreeTests
    {
        private static GameState NewGame() => new GameState { DungeonFields = SampleData.CreateDefaultFields() };

        [Theory]
        [InlineData("res_cave_ointment", "res_herb_poultice")]
        [InlineData("res_abyss_preservation", "res_cave_ointment")]
        [InlineData("res_ruins_tactics", "res_scout_reagent")]
        [InlineData("res_canyon_tread", "res_light_tread")]
        [InlineData("res_energy_tonic", "res_beauty_lotion")]
        [InlineData("res_trade_route", "res_energy_tonic")]
        [InlineData("res_vitality_elixir", "res_trade_route")]
        [InlineData("res_elixir_brewing", "res_training_method_1")]
        public void NewPrerequisites_LinkTheTree(string researchId, string prerequisiteId)
        {
            Assert.Equal(prerequisiteId, ResearchBalance.Find(researchId)!.PrerequisiteId);
        }

        [Fact]
        public void Prerequisite_IsInSameBranch_AndNotToTheRight()
        {
            var state = NewGame();
            foreach (var research in ResearchBalance.GetAll().Where(r => r.PrerequisiteId != null))
            {
                var parent = ResearchBalance.Find(research.PrerequisiteId!)!;
                Assert.Equal(parent.Branch, research.Branch);
                Assert.True(ResearchSystem.GetFieldOrder(state, parent) <= ResearchSystem.GetFieldOrder(state, research),
                    $"{research.Id} の前提 {parent.Id} が右の列にある");
            }
        }

        [Fact]
        public void EveryBranch_HasResearch_AndLanesStartAtZero()
        {
            foreach (var group in ResearchBalance.GetAll().GroupBy(r => r.Branch))
            {
                Assert.Contains(group, r => r.Lane == 0);
                Assert.All(group, r => Assert.InRange(r.Lane, 0, 1));
            }
            Assert.Equal(System.Enum.GetValues<ResearchBranch>().Length, ResearchBalance.GetAll().Select(r => r.Branch).Distinct().Count());
        }

        [Theory]
        [InlineData("res_herb_poultice", 1)]
        [InlineData("res_cave_ointment", 2)]
        [InlineData("res_training_method_1", 3)]
        [InlineData("res_elixir_brewing", 3)]
        [InlineData("res_vitality_elixir", 4)]
        [InlineData("res_abyss_preservation", 5)]
        public void GetFieldOrder_IsDeepestMaterialField(string researchId, int expected)
        {
            Assert.Equal(expected, ResearchSystem.GetFieldOrder(NewGame(), ResearchBalance.Find(researchId)!));
        }

        [Fact]
        public void IsRevealed_OnlyForUnlockedFieldMaterials()
        {
            var state = NewGame(); // はじめは森だけ
            var poultice = ResearchBalance.Find("res_herb_poultice")!;
            var ointment = ResearchBalance.Find("res_cave_ointment")!;

            Assert.True(ResearchSystem.IsRevealed(state, poultice));
            Assert.False(ResearchSystem.IsRevealed(state, ointment));

            state.DungeonFields.First(f => f.Order == 2).IsUnlocked = true;
            Assert.True(ResearchSystem.IsRevealed(state, ointment));
        }

        [Fact]
        public void IsRevealed_WhenCompleted_EvenIfFieldLocked()
        {
            var state = NewGame();
            state.CompletedResearchIds.Add("res_abyss_preservation");

            Assert.True(ResearchSystem.IsRevealed(state, ResearchBalance.Find("res_abyss_preservation")!));
        }

        [Fact]
        public void CaveOintment_NeedsHerbPoultice()
        {
            var ointment = ResearchBalance.Find("res_cave_ointment")!;
            var state = new GameState { Gold = ointment.RequiredGold };
            foreach (var (id, count) in ointment.RequiredMaterials)
                state.AddMaterial(id, count);

            Assert.False(ResearchSystem.CanStartResearch(state, ointment));
            state.CompletedResearchIds.Add("res_herb_poultice");
            Assert.True(ResearchSystem.CanStartResearch(state, ointment));
        }
    }
}
