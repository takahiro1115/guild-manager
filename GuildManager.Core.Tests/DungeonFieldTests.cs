using System.Linq;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 大迷宮5フィールド拡張仕様（→ DungeonField・SampleData.CreateDefaultFields・
    /// DungeonExpeditionSystem.ApplyFieldProgression）のテスト。
    ///
    /// 指示書は本ファイルを `GuildManager.Core.Tests/Models/DungeonFieldTests.cs` に置く想定だったが、
    /// このテストプロジェクトは一貫してサブフォルダを持たない（全32ファイルがフラット配置）ため、
    /// 既存の慣習に合わせてフラット配置にした（1ファイルだけ`Models/`配下に置くと逆に一貫性を崩すため）。
    ///
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~DungeonField`
    /// </summary>
    public class DungeonFieldTests
    {
        [Fact]
        public void DungeonField_Has100Floors_And20Bosses()
        {
            var fields = SampleData.CreateDefaultFields();

            Assert.Equal(5, fields.Count);
            var expectedFloors = Enumerable.Range(1, 20).Select(i => i * DungeonField.BossInterval).ToArray();

            foreach (var field in fields)
            {
                Assert.Equal(20, field.Bosses.Count);
                Assert.Equal(expectedFloors, field.Bosses.Select(b => b.Floor).OrderBy(f => f).ToArray());
                Assert.All(field.Bosses, b => Assert.True(b.Floor <= DungeonField.MaxFloor));
            }
        }

        [Fact]
        public void DungeonField_InitialState_OnlyForestIsUnlocked()
        {
            var fields = SampleData.CreateDefaultFields();

            var forest = fields.Single(f => f.Order == 1);
            Assert.Equal("forest", forest.Id);
            Assert.Equal("翠緑の原生林", forest.Name);
            Assert.True(forest.IsUnlocked);

            Assert.All(fields.Where(f => f.Order != 1), f => Assert.False(f.IsUnlocked));

            // 参考までに他4フィールドのId・Nameも仕様どおりであることを確認する。
            Assert.Equal("cave", fields.Single(f => f.Order == 2).Id);
            Assert.Equal("嘆きの鍾乳洞", fields.Single(f => f.Order == 2).Name);
            Assert.Equal("ruins", fields.Single(f => f.Order == 3).Id);
            Assert.Equal("忘却の古代廃墟", fields.Single(f => f.Order == 3).Name);
            Assert.Equal("canyon", fields.Single(f => f.Order == 4).Id);
            Assert.Equal("焦熱の峡谷", fields.Single(f => f.Order == 4).Name);
            Assert.Equal("abyss", fields.Single(f => f.Order == 5).Id);
            Assert.Equal("深淵の特異点", fields.Single(f => f.Order == 5).Name);
        }

        [Fact]
        public void DungeonField_Defeating5FBoss_DoesNotUnlockNextField()
        {
            var fields = SampleData.CreateDefaultFields();
            var state = new GameState { DungeonFields = fields };
            var forest = fields.Single(f => f.Order == 1);
            var boss5F = forest.Bosses.Single(b => b.Floor == 5);
            boss5F.IsDefeated = true;

            DungeonExpeditionSystem.ApplyFieldProgression(state, boss5F);

            var cave = fields.Single(f => f.Order == 2);
            Assert.False(cave.IsUnlocked);
            Assert.Equal(6, forest.ReachedFloor);
        }

        [Fact]
        public void DungeonField_Defeating10FBoss_UnlocksNextField()
        {
            var fields = SampleData.CreateDefaultFields();
            var state = new GameState { DungeonFields = fields };
            var forest = fields.Single(f => f.Order == 1);
            var boss10F = forest.Bosses.Single(b => b.Floor == 10);
            boss10F.IsDefeated = true;

            DungeonExpeditionSystem.ApplyFieldProgression(state, boss10F);

            var cave = fields.Single(f => f.Order == 2);
            Assert.True(cave.IsUnlocked);
            Assert.Equal(11, forest.ReachedFloor);
            // 深淵（第5）はこの経路では開放されない。
            Assert.False(fields.Single(f => f.Order == 5).IsUnlocked);
        }

        [Fact]
        public void DungeonField_DefeatingAll20FBosses_UnlocksAbyss()
        {
            var fields = SampleData.CreateDefaultFields();
            var state = new GameState { DungeonFields = fields };

            foreach (var field in fields.Where(f => f.Order is >= 1 and <= 4))
            {
                var boss20F = field.Bosses.Single(b => b.Floor == 20);
                boss20F.IsDefeated = true;
                DungeonExpeditionSystem.ApplyFieldProgression(state, boss20F);

                if (field.Order < 4)
                    Assert.False(fields.Single(f => f.Order == 5).IsUnlocked, "4フィールド全ての20F撃破が揃うまでは深淵は開かないはず");
            }

            var abyss = fields.Single(f => f.Order == 5);
            Assert.True(abyss.IsUnlocked);
        }

        [Fact]
        public void DungeonField_DefeatingFinal100FBoss_UnlocksFinalQuest()
        {
            var fields = SampleData.CreateDefaultFields();
            var abyss = fields.Single(f => f.Order == 5);
            abyss.IsUnlocked = true; // 深淵の開放は本テストの対象外のため直接開けておく
            var state = new GameState { DungeonFields = fields };
            var boss100F = abyss.Bosses.Single(b => b.Floor == 100);
            boss100F.IsDefeated = true;

            Assert.False(state.FinalQuestUnlocked);
            DungeonExpeditionSystem.ApplyFieldProgression(state, boss100F);

            Assert.True(state.FinalQuestUnlocked);
            Assert.Equal(100, abyss.ReachedFloor); // MaxFloorで頭打ち（100+1ではなく100）
        }

        [Fact]
        public void DungeonField_ApplyFieldProgression_GrantsBossReward()
        {
            var fields = SampleData.CreateDefaultFields();
            var state = new GameState { DungeonFields = fields, Gold = 0, Reputation = 0 };
            var forest = fields.Single(f => f.Order == 1);
            var boss5F = forest.Bosses.Single(b => b.Floor == 5);
            boss5F.IsDefeated = true;

            DungeonExpeditionSystem.ApplyFieldProgression(state, boss5F);

            Assert.Equal(boss5F.RewardGold, state.Gold);
            Assert.Equal(boss5F.RewardReputation, state.Reputation);
            Assert.True(boss5F.RewardGold > 0);
        }
    }
}
