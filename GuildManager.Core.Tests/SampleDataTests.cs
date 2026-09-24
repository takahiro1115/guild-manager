using System.Linq;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// MVP動作確認用の固定データ（SampleData）のテスト。
    /// 初期編成改訂仕様：固定初期メンバーは3名（前衛の重戦士・斥候、後衛の神官）のみとし、
    /// 残り2名は第1週の新春ドラフト（→ RecruitmentSystem.StartInitialDraft、
    /// RecruitmentDraftTests）でプレイヤー自身が無料で選抜契約することで、計5名体制になる。
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

        [Theory]
        [InlineData("クラウディア", ItemCatalog.IronSwordId, ItemCatalog.LeatherArmorId)]
        [InlineData("リナ", ItemCatalog.DaggerId, ItemCatalog.LeatherArmorId)]
        [InlineData("フィオナ", ItemCatalog.MaceId, ItemCatalog.RobeId)]
        public void StarterAdventurers_BeginWithBasicEquipment(string name, string weaponId, string armorId)
        {
            // 武具の7大能力値補正（2026年9月）：初期メンバーは基本装備を着た状態で加入する。
            var a = SampleData.CreateStarterAdventurers().Single(x => x.Name == name);

            Assert.Equal(weaponId, a.EquippedWeaponId);
            Assert.Equal(armorId, a.EquippedArmorId);
            Assert.Null(a.EquippedAccessory1);
            Assert.Null(a.EquippedAccessory2);
            Assert.True(a.EquippedWeapon!.IsAllowedFor(a.JobClass));
            Assert.True(a.EquippedArmor!.IsAllowedFor(a.JobClass));
            Assert.Null(a.EquippedWeapon.Rarity); // カタログ品（無銘）
        }

        [Fact]
        public void StarterAdventurers_StartAtFullHp_IncludingEquipmentBonuses()
        {
            // 防具のHP加算・VIT補正が乗った後の最大HPで満タンになっていること。
            Assert.All(SampleData.CreateStarterAdventurers(), a => Assert.Equal(a.MaxHP, a.CurrentHP));
        }
    }
}
