using System.Linq;
using System.Text.Json;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// ボスのギミックの定義（§0.68：7種・ボスごとに効く職業・深いほど複数）と、旧セーブの読み込み（定義の作り直し・
    /// 撤去した携行ポーチの返金）のテスト。
    /// </summary>
    public class BossGimmickDefinitionTests
    {
        [Fact]
        public void CsvValues_AreLoaded()
        {
            Assert.Equal(0.5, BossGimmickBalance.GimmickRoleReadiness, precision: 6);
            Assert.Equal(3, BossGimmickBalance.GimmickCountMax);
            Assert.Equal(4, BossGimmickBalance.PoisonStatusWeeks);
        }

        [Fact]
        public void EveryJob_CountersExactlyTwoGimmickTypes_AndEveryStatIsUsedOnce()
        {
            var types = System.Enum.GetValues<BossGimmickType>();
            Assert.Equal(7, types.Length);
            var roles = types.SelectMany(BossGimmickInfo.RoleCandidates).GroupBy(r => r).ToDictionary(g => g.Key, g => g.Count());
            Assert.Equal(7, roles.Count);
            Assert.All(roles.Values, count => Assert.Equal(2, count));
            Assert.Equal(7, types.Select(BossGimmickInfo.CounterStat).Distinct().Count());
        }

        [Fact]
        public void ForestFirstBoss_IsPoison_CounteredByCleric()
        {
            // 初期メンバーの神官（フィオナ）で備えられる最初の導線（→ SampleData.CreateDefaultFields）。
            var forest = SampleData.CreateDefaultFields().Single(f => f.Id == "forest");
            var first = forest.Bosses.Single(b => b.Floor == 10);

            var gimmick = Assert.Single(first.Gimmicks);
            Assert.Equal(BossGimmickType.Poison, gimmick.Type);
            Assert.Equal(new[] { JobClass.Cleric }, gimmick.CounterRoles);
            Assert.Contains("毒蜘蛛", first.Name);
        }

        [Fact]
        public void GimmickCount_GrowsWithDepth_AndTypesNeverRepeatWithinABoss()
        {
            var fields = SampleData.CreateDefaultFields();
            var forest = fields.Single(f => f.Order == 1);
            Assert.All(forest.Bosses.Where(b => b.Floor <= 50), b => Assert.Single(b.Gimmicks));
            Assert.All(forest.Bosses.Where(b => b.Floor > 50), b => Assert.Equal(2, b.Gimmicks.Count));
            Assert.All(fields.Single(f => f.Order == 5).Bosses, b => Assert.InRange(b.Gimmicks.Count, 2, 3));

            var all = fields.SelectMany(f => f.Bosses).ToList();
            Assert.All(all, b => Assert.Equal(b.Gimmicks.Count, b.Gimmicks.Select(g => g.Type).Distinct().Count()));
            // 7種すべてが、どこかのボスの一番の特徴になる
            Assert.Equal(7, all.Select(b => b.Gimmicks[0].Type).Distinct().Count());
            // ボスによって効く職業が違う（同じ種類でも、候補の片方だけ・両方がある）
            var poisonRoleSets = all.SelectMany(b => b.Gimmicks).Where(g => g.Type == BossGimmickType.Poison)
                .Select(g => string.Join(",", g.CounterRoles)).Distinct().Count();
            Assert.Equal(3, poisonRoleSets);
        }

        [Fact]
        public void BossDefinitions_SurviveSave()
        {
            var state = new GameState { DungeonFields = SampleData.CreateDefaultFields() };

            var json = JsonSerializer.Serialize(state.ToSaveData());
            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!);

            var before = state.DungeonFields.SelectMany(f => f.Bosses).SelectMany(b => b.Gimmicks).ToList();
            var after = restored.DungeonFields.SelectMany(f => f.Bosses).SelectMany(b => b.Gimmicks).ToList();
            Assert.Equal(before.Count, after.Count);
            for (int i = 0; i < before.Count; i++)
            {
                Assert.Equal(before[i].Type, after[i].Type);
                Assert.Equal(before[i].CounterRoles, after[i].CounterRoles);
            }
        }

        [Fact]
        public void PoisonStatus_SurvivesSave()
        {
            var a = new Adventurer { Name = "アリス", JobClass = JobClass.Warrior, PoisonWeeksRemaining = 3, PoisonStatPenalty = 0.2 };
            var state = new GameState { Adventurers = { a } };

            var json = JsonSerializer.Serialize(state.ToSaveData());
            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!).Adventurers.Single();

            Assert.Equal(3, restored.PoisonWeeksRemaining);
            Assert.Equal(0.2, restored.PoisonStatPenalty, precision: 6);
            Assert.True(restored.IsPoisoned);
        }
    }
}
