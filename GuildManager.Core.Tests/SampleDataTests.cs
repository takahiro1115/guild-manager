using System.Linq;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// MVP動作確認用の固定データ（SampleData）のテスト。
    /// 初期編成改訂仕様：固定初期メンバーは3名（前衛の重戦士・斥候、後衛の神官）のみとし、
    /// 残り3名は第1週のチュートリアル採用試験（→ RecruitmentSystem.
    /// IsTutorialRecruitmentWeek・RecruitmentBalance.TutorialCandidateCount）で
    /// プレイヤー自身が選抜契約することで、計6名体制になる。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class SampleDataTests
    {
        [Fact]
        public void SampleData_ShouldContainExactlyThreeAdventurers()
        {
            var adventurers = SampleData.CreateStarterAdventurers();

            Assert.Equal(3, adventurers.Count);
        }

        [Fact]
        public void SampleData_AllStarterAdventurers_AreFemale()
        {
            // 世界観設定（女性限定ギルド仕様）：初期ロースターは全員Gender.Female。
            var adventurers = SampleData.CreateStarterAdventurers();

            Assert.All(adventurers, a => Assert.Equal(Gender.Female, a.Gender));
        }

        [Theory]
        [InlineData("cave", MaterialIds.CaveOre)]
        [InlineData("ruins", MaterialIds.RuinsRune)]
        [InlineData("canyon", MaterialIds.CanyonGem)]
        [InlineData("abyss", MaterialIds.AbyssCrystal)]
        public void FloorBoss_NewFieldBosses_HaveRewardMaterials(string fieldId, string expectedMaterialId)
        {
            // 洞窟・廃墟・峡谷・深淵の各フィールドは、節目ボス（10F・20F）のみフィールド固有の
            // 希少素材を確定ドロップする（2026年9月、候補Aの4フィールド拡張、→ 03 §4.5.5）。
            var field = SampleData.CreateDefaultFields().Single(f => f.Id == fieldId);
            var boss10F = field.Bosses.Single(b => b.Floor == 10);
            var boss20F = field.Bosses.Single(b => b.Floor == 20);

            foreach (var boss in new[] { boss10F, boss20F })
            {
                Assert.Equal(expectedMaterialId, boss.RewardMaterialId);
                Assert.InRange(boss.RewardMaterialCount, 3, 5);
            }

            // 10F・20F以外のボスは素材ドロップを持たない（節目ボス限定の仕様）。
            var otherBosses = field.Bosses.Where(b => b.Floor != 10 && b.Floor != 20);
            Assert.All(otherBosses, b => Assert.Null(b.RewardMaterialId));
        }

        [Fact]
        public void FloorBoss_ForestBosses_CycleThroughAllThreeMaterials()
        {
            // forestフィールドは候補A最初の実装分として、全10体で3種を巡回する
            // （cave等の「節目ボスのみ」仕様とは異なる、→ 03 §0.4）。
            var field = SampleData.CreateDefaultFields().Single(f => f.Id == "forest");

            Assert.All(field.Bosses, b => Assert.NotNull(b.RewardMaterialId));
            var distinctMaterials = field.Bosses.Select(b => b.RewardMaterialId).Distinct().ToList();
            Assert.Equal(3, distinctMaterials.Count);
        }
    }
}
