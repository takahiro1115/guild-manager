using GuildManager.Core.Models;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 配置（Placement）のルール（仕様書 03 §4.2）のテスト。
    /// 配置は職業で固定されない：どの職業でも自由に前衛/後衛を選べる
    /// （ユーザー決定：「魔法使いや僧侶も前衛になることができる。職業で固定になることはない」）。
    /// 職業ごとに決まるのは生成時のデフォルト値のみ。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class PlacementTests
    {
        // ---------------- 職業デフォルト（PlacementRules.GetDefault） ----------------

        [Theory]
        [InlineData(JobClass.Warrior, Placement.Front)]
        [InlineData(JobClass.Ranger, Placement.Front)]
        [InlineData(JobClass.Mage, Placement.Back)]
        [InlineData(JobClass.Cleric, Placement.Back)]
        public void GetDefault_MatchesJobClassRule(JobClass jobClass, Placement expected)
        {
            Assert.Equal(expected, PlacementRules.GetDefault(jobClass));
        }

        // ---------------- Adventurer.TrySetPlacement：全職業で自由に変更できる ----------------

        [Theory]
        [InlineData(JobClass.Warrior)]
        [InlineData(JobClass.Ranger)]
        [InlineData(JobClass.Mage)]
        [InlineData(JobClass.Cleric)]
        public void TrySetPlacement_AllowsFrontForEveryJobClass(JobClass jobClass)
        {
            var adventurer = new Adventurer { JobClass = jobClass, Placement = Placement.Back };

            bool result = adventurer.TrySetPlacement(Placement.Front);

            Assert.True(result);
            Assert.Equal(Placement.Front, adventurer.Placement);
        }

        [Theory]
        [InlineData(JobClass.Warrior)]
        [InlineData(JobClass.Ranger)]
        [InlineData(JobClass.Mage)]
        [InlineData(JobClass.Cleric)]
        public void TrySetPlacement_AllowsBackForEveryJobClass(JobClass jobClass)
        {
            var adventurer = new Adventurer { JobClass = jobClass, Placement = Placement.Front };

            bool result = adventurer.TrySetPlacement(Placement.Back);

            Assert.True(result);
            Assert.Equal(Placement.Back, adventurer.Placement);
        }
    }
}
