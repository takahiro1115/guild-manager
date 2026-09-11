using GuildManager.Core.Models;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 配置（Placement）のルール（仕様書 03 §4.2）のテスト。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class PlacementTests
    {
        // ---------------- 職業デフォルト（PlacementRules） ----------------

        [Theory]
        [InlineData(JobClass.Warrior, Placement.Front)]
        [InlineData(JobClass.Ranger, Placement.Front)]
        [InlineData(JobClass.Mage, Placement.Back)]
        [InlineData(JobClass.Cleric, Placement.Back)]
        public void GetDefault_MatchesJobClassRule(JobClass jobClass, Placement expected)
        {
            Assert.Equal(expected, PlacementRules.GetDefault(jobClass));
        }

        [Theory]
        [InlineData(JobClass.Mage, true)]
        [InlineData(JobClass.Cleric, true)]
        [InlineData(JobClass.Warrior, false)]
        [InlineData(JobClass.Ranger, false)]
        public void IsBackOnly_MatchesJobClassRule(JobClass jobClass, bool expected)
        {
            Assert.Equal(expected, PlacementRules.IsBackOnly(jobClass));
        }

        // ---------------- Adventurer.TrySetPlacement ----------------

        [Theory]
        [InlineData(JobClass.Warrior)]
        [InlineData(JobClass.Ranger)]
        public void TrySetPlacement_AllowsBackForFrontEligibleJobs(JobClass jobClass)
        {
            var adventurer = new Adventurer { JobClass = jobClass, Placement = Placement.Front };

            bool result = adventurer.TrySetPlacement(Placement.Back);

            Assert.True(result);
            Assert.Equal(Placement.Back, adventurer.Placement);
        }

        [Theory]
        [InlineData(JobClass.Warrior)]
        [InlineData(JobClass.Ranger)]
        public void TrySetPlacement_AllowsFrontAgain_ForFrontEligibleJobs(JobClass jobClass)
        {
            var adventurer = new Adventurer { JobClass = jobClass, Placement = Placement.Back };

            bool result = adventurer.TrySetPlacement(Placement.Front);

            Assert.True(result);
            Assert.Equal(Placement.Front, adventurer.Placement);
        }

        [Theory]
        [InlineData(JobClass.Mage)]
        [InlineData(JobClass.Cleric)]
        public void TrySetPlacement_RejectsFront_ForBackOnlyJobs(JobClass jobClass)
        {
            var adventurer = new Adventurer { JobClass = jobClass, Placement = Placement.Back };

            bool result = adventurer.TrySetPlacement(Placement.Front);

            Assert.False(result);
            Assert.Equal(Placement.Back, adventurer.Placement); // 変更されない
        }

        [Theory]
        [InlineData(JobClass.Mage)]
        [InlineData(JobClass.Cleric)]
        public void TrySetPlacement_AllowsBack_ForBackOnlyJobs(JobClass jobClass)
        {
            // 既に後衛の後衛固定職に対してBackを指定しても、当然成功する（変化なしでもOK扱い）。
            var adventurer = new Adventurer { JobClass = jobClass, Placement = Placement.Back };

            bool result = adventurer.TrySetPlacement(Placement.Back);

            Assert.True(result);
            Assert.Equal(Placement.Back, adventurer.Placement);
        }
    }
}
