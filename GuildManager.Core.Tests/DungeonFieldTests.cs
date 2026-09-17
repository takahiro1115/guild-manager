using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 大迷宮5フィールド拡張仕様（→ DungeonField・SampleData.CreateDefaultFields・
    /// DungeonExpeditionSystem.ApplyFieldProgression）と、ボス配置間隔10階化・出撃枠拡張
    /// （「古代エルフ通信技術の復元」、→ 大迷宮ボス間隔・敗北条件改訂）のテスト。
    ///
    /// 指示書は本ファイルを `GuildManager.Core.Tests/Models/DungeonFieldTests.cs` に置く想定だったが、
    /// このテストプロジェクトは一貫してサブフォルダを持たない（全ファイルがフラット配置）ため、
    /// 既存の慣習に合わせてフラット配置にした（1ファイルだけ`Models/`配下に置くと逆に一貫性を崩すため）。
    ///
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~DungeonField`
    /// </summary>
    public class DungeonFieldTests
    {
        [Fact]
        public void DungeonField_Has10Bosses_With10FloorInterval()
        {
            var fields = SampleData.CreateDefaultFields();

            Assert.Equal(5, fields.Count);
            Assert.Equal(10, DungeonField.BossInterval); // → BAL: dungeon.csv BossIntervalFloors
            var expectedFloors = Enumerable.Range(1, 10).Select(i => i * DungeonField.BossInterval).ToArray();

            foreach (var field in fields)
            {
                Assert.Equal(10, field.Bosses.Count);
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
        public void DungeonField_Defeating30FBoss_DoesNotUnlockNextField()
        {
            // 10階刻みモデルでは10Fが森の最初のボス（かつ唯一の次フィールド開放トリガー）のため、
            // 「開放しない」ことを確認する対象には中間の30Fを使う。
            var fields = SampleData.CreateDefaultFields();
            var state = new GameState { DungeonFields = fields };
            var forest = fields.Single(f => f.Order == 1);
            var boss30F = forest.Bosses.Single(b => b.Floor == 30);
            boss30F.IsDefeated = true;

            DungeonExpeditionSystem.ApplyFieldProgression(state, boss30F);

            var cave = fields.Single(f => f.Order == 2);
            Assert.False(cave.IsUnlocked);
            Assert.Equal(31, forest.ReachedFloor);
        }

        [Fact]
        public void Defeating_Forest_10F_Boss_Unlocks_Slot2_And_NextField()
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

            // 「古代エルフの多頭通信術式が復元」：同時出撃枠が1→2に拡張され、
            // ギルド格付けがRank E相当（名声もEの昇格ラインまで）に同期される。
            Assert.Equal(2, state.UnlockedSquadSlots);
            Assert.True(state.GuildRank >= GuildRank.E);
            Assert.True(state.Reputation >= GuildRankBalance.GetThreshold(GuildRank.E).PromoteAt);
        }

        [Fact]
        public void Defeating_Forest_20F_Boss_Unlocks_Slot3()
        {
            var fields = SampleData.CreateDefaultFields();
            var state = new GameState { DungeonFields = fields };
            var forest = fields.Single(f => f.Order == 1);
            // 10Fを経由せず20Fだけを撃破しても、Math.Maxにより一気に3枠まで到達する
            // （「既に到達値以上の場合はダウングレードしない」の裏として、途中を飛ばしても機能する）。
            var boss20F = forest.Bosses.Single(b => b.Floor == 20);
            boss20F.IsDefeated = true;

            DungeonExpeditionSystem.ApplyFieldProgression(state, boss20F);

            Assert.Equal(3, state.UnlockedSquadSlots);
            Assert.True(state.GuildRank >= GuildRank.D);
            Assert.True(state.Reputation >= GuildRankBalance.GetThreshold(GuildRank.D).PromoteAt);
        }

        [Fact]
        public void Defeating_Forest_10F_Boss_DoesNotDowngrade_SlotsOrRank_WhenAlreadyHigher()
        {
            var fields = SampleData.CreateDefaultFields();
            var state = new GameState { DungeonFields = fields, UnlockedSquadSlots = 5, GuildRank = GuildRank.A };
            state.Reputation = GuildRankBalance.GetThreshold(GuildRank.A).PromoteAt;
            var forest = fields.Single(f => f.Order == 1);
            var boss10F = forest.Bosses.Single(b => b.Floor == 10);
            boss10F.IsDefeated = true;

            DungeonExpeditionSystem.ApplyFieldProgression(state, boss10F);

            Assert.Equal(5, state.UnlockedSquadSlots);
            Assert.Equal(GuildRank.A, state.GuildRank);
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
            // 出撃枠拡張（10F・20F限定）と干渉しない中立な階層として30Fを使う。
            var fields = SampleData.CreateDefaultFields();
            var state = new GameState { DungeonFields = fields, Gold = 0, Reputation = 0 };
            var forest = fields.Single(f => f.Order == 1);
            var boss30F = forest.Bosses.Single(b => b.Floor == 30);
            boss30F.IsDefeated = true;

            DungeonExpeditionSystem.ApplyFieldProgression(state, boss30F);

            Assert.Equal(boss30F.RewardGold, state.Gold);
            Assert.Equal(boss30F.RewardReputation, state.Reputation);
            Assert.True(boss30F.RewardGold > 0);
        }

        [Fact]
        public void DungeonField_BossRewardsAndHp_ScaleWithStageAndFieldOrder()
        {
            // → BAL: dungeon.csv BossBaseHp/BossHpFloorMultiplier/BossBaseRewardGold/
            // BossBaseReputation/BossRewardGoldMultiplier。森10F（段1）が基準値そのものになり、
            // 段が進む・フィールードが深くなるほど乗算係数で滑らかに増える。
            var fields = SampleData.CreateDefaultFields();
            var forest = fields.Single(f => f.Order == 1);
            var cave = fields.Single(f => f.Order == 2);

            var forest10F = forest.Bosses.Single(b => b.Floor == 10);
            Assert.Equal((int)DungeonBalance.BossBaseHp, forest10F.MaxHp);
            Assert.Equal((int)DungeonBalance.BossBaseRewardGold, forest10F.RewardGold);
            Assert.Equal((int)DungeonBalance.BossBaseReputation, forest10F.RewardReputation);

            var forest20F = forest.Bosses.Single(b => b.Floor == 20);
            Assert.True(forest20F.MaxHp > forest10F.MaxHp, "段が進むほどHPは増えるはず");
            Assert.True(forest20F.RewardGold > forest10F.RewardGold, "段が進むほど報酬は増えるはず");

            // 洞窟（Order=2）の10Fは、フィールドの格上げ1段ぶん森20F相当になる。
            var cave10F = cave.Bosses.Single(b => b.Floor == 10);
            Assert.Equal(forest20F.MaxHp, cave10F.MaxHp);
            Assert.Equal(forest20F.RewardGold, cave10F.RewardGold);
        }
    }
}
