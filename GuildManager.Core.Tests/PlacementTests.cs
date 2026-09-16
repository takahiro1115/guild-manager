using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 配置（Placement）のルール（仕様書 03 §4.2）のテスト。
    ///
    /// 7職業化（改訂）：配置は**職業によって一意に決まる**（→ PlacementRules.GetDefault）。
    /// 前衛＝Warrior/Knight/Ranger/Thief、後衛＝Mage/Cleric/Scholar。
    /// 配置を選び直すUIは撤去済み（→ コミットd58de0b）だが、Adventurer.TrySetPlacement は
    /// Core上に残っているため、その挙動も引き続き検証する。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class PlacementTests
    {
        // ---------------- 職業から一意に決まる配置（PlacementRules.GetDefault） ----------------

        [Theory]
        [InlineData(JobClass.Warrior, Placement.Front)]
        [InlineData(JobClass.Knight, Placement.Front)]
        [InlineData(JobClass.Ranger, Placement.Front)]
        [InlineData(JobClass.Thief, Placement.Front)]
        [InlineData(JobClass.Mage, Placement.Back)]
        [InlineData(JobClass.Cleric, Placement.Back)]
        [InlineData(JobClass.Scholar, Placement.Back)]
        public void GetDefault_MatchesJobClassRule(JobClass jobClass, Placement expected)
        {
            Assert.Equal(expected, PlacementRules.GetDefault(jobClass));
        }

        [Fact]
        public void GetDefault_SplitsSevenJobClassesIntoFourFrontAndThreeBack()
        {
            var allJobs = Enum.GetValues<JobClass>();

            Assert.Equal(7, allJobs.Length);
            Assert.Equal(4, allJobs.Count(j => PlacementRules.GetDefault(j) == Placement.Front));
            Assert.Equal(3, allJobs.Count(j => PlacementRules.GetDefault(j) == Placement.Back));
        }

        [Fact]
        public void GetDefault_IsDefinedForEveryJobClass()
        {
            // 列挙型に職業を追加した際の設定漏れ検知：未定義の職業は例外になる実装のため、
            // 全職業で例外が出ないことを確認しておく。
            foreach (var job in Enum.GetValues<JobClass>())
                PlacementRules.GetDefault(job);
        }

        // ---------------- 新3職の配置補正（combat.csv。既存職との戦闘格差の解消） ----------------

        [Theory]
        [InlineData(JobClass.Knight, Placement.Front, 1.2)]
        [InlineData(JobClass.Knight, Placement.Back, 0.8)]
        [InlineData(JobClass.Thief, Placement.Front, 1.0)]
        [InlineData(JobClass.Thief, Placement.Back, 1.0)]
        [InlineData(JobClass.Scholar, Placement.Front, 0.8)]
        [InlineData(JobClass.Scholar, Placement.Back, 1.2)]
        public void PersonalCpCorrection_NewJobClasses_MatchCombatCsv(JobClass jobClass, Placement placement, double expected)
        {
            Assert.Equal(expected, PlacementBalance.GetPersonalCpCorrection(jobClass, placement), precision: 10);
        }

        [Theory]
        // 同じ役割の既存職と新職で、本来の配置での補正が一致すること（戦闘格差が無いこと）。
        [InlineData(JobClass.Warrior, JobClass.Knight, Placement.Front)]   // 前衛の盾役
        [InlineData(JobClass.Mage, JobClass.Scholar, Placement.Back)]      // 後衛の魔法職
        [InlineData(JobClass.Ranger, JobClass.Thief, Placement.Front)]     // 前衛の機動役
        public void PersonalCpCorrection_NewJobClass_MatchesExistingJobWithSameRole(
            JobClass existingJob, JobClass newJob, Placement rolePlacement)
        {
            Assert.Equal(
                PlacementBalance.GetPersonalCpCorrection(existingJob, rolePlacement),
                PlacementBalance.GetPersonalCpCorrection(newJob, rolePlacement),
                precision: 10);
        }

        [Fact]
        public void PersonalCpCorrection_EveryJobClass_GetsItsBonusInItsDefaultPlacement()
        {
            // 職業から一意に決まる本来の配置では、補正が1.0以上（ペナルティにならない）であること。
            foreach (var job in Enum.GetValues<JobClass>())
            {
                double correction = PlacementBalance.GetPersonalCpCorrection(job, PlacementRules.GetDefault(job));
                Assert.True(correction >= 1.0, $"{job}の本来の配置での補正({correction})がペナルティになっている");
            }
        }

        // ---------------- Adventurer.TrySetPlacement（Core上に残存する変更手段） ----------------

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
