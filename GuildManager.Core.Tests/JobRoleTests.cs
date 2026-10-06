using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 2026年10月・§0.72のテスト：職業で強さと伸び方を分ける（職業ごとの討伐火力の重み・神官の加護）。
    /// 実行方法: `dotnet test GuildManager.Core.Tests --filter FullyQualifiedName~JobRole`
    /// </summary>
    public class JobRoleTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private static Adventurer Make(JobClass job, int str = 30, int agi = 30, int vit = 30, int mnd = 30, int dex = 30, int ldr = 30, int intel = 30)
        {
            var a = new Adventurer { JobClass = job, STR = str, AGI = agi, VIT = vit, MND = mnd, DEX = dex, LDR = ldr, INT = intel };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        [Fact]
        public void EveryJob_HasPowerWeights_SummingToTheSharedTotal()
        {
            double shared = DungeonBalance.BossPowerWeights.Sum(w => w.Weight);
            foreach (var job in System.Enum.GetValues<JobClass>())
                Assert.Equal(shared, DungeonBalance.GetBossPowerWeights(job).Sum(w => w.Weight), precision: 6);
        }

        [Fact]
        public void Power_FollowsTheJobsMainStats()
        {
            // 同じ能力の配分でも、職業に合う能力が高い方が火力が大きい
            var strongWarrior = Make(JobClass.Warrior, str: 80, vit: 80);
            var strongMage = Make(JobClass.Mage, str: 80, vit: 80);
            Assert.True(DungeonPowerCalculator.MemberPower(strongWarrior) > DungeonPowerCalculator.MemberPower(strongMage));

            var smartMage = Make(JobClass.Mage, intel: 80, mnd: 80);
            var smartWarrior = Make(JobClass.Warrior, intel: 80, mnd: 80);
            Assert.True(DungeonPowerCalculator.MemberPower(smartMage) > DungeonPowerCalculator.MemberPower(smartWarrior));
        }

        [Fact]
        public void ClericBlessing_ScalesWithTheBestClericsMnd()
        {
            var cleric = Make(JobClass.Cleric, mnd: 100);
            var weakCleric = Make(JobClass.Cleric, mnd: 50);
            var ranger = Make(JobClass.Ranger);

            Assert.Equal(0, ClericBlessing.LossReductionPct(new[] { ranger }));
            Assert.Equal(1.0, ClericBlessing.SevereThresholdMultiplier(new[] { ranger }));
            Assert.Equal(100 * CombatBalance.ClericBlessingLossPctPerMnd, ClericBlessing.LossReductionPct(new[] { ranger, weakCleric, cleric }), precision: 6);
            Assert.Equal(CombatBalance.ClericBlessingSevereThresholdRate, ClericBlessing.SevereThresholdMultiplier(new[] { ranger, cleric }), precision: 6);
        }

        [Fact]
        public void ClericBlessing_LowersBossFightLoss()
        {
            FloorBoss Boss() => new() { Name = "主", Floor = 1, MaxHp = 100, CurrentHp = 100 };
            var withCleric = new Party();
            var r1 = Make(JobClass.Ranger);
            withCleric.TryAdd(r1);
            withCleric.TryAdd(Make(JobClass.Cleric, mnd: 100));
            var without = new Party();
            var r2 = Make(JobClass.Ranger);
            without.TryAdd(r2);
            without.TryAdd(Make(JobClass.Ranger));

            var blessed = new DungeonResolver(new AlwaysMinRng()).Resolve(withCleric, Boss());
            var plain = new DungeonResolver(new AlwaysMinRng()).Resolve(without, Boss());

            Assert.Equal(100 * CombatBalance.ClericBlessingLossPctPerMnd, blessed.ClericBlessingPct, precision: 6);
            Assert.Equal(0, plain.ClericBlessingPct);
            Assert.True(blessed.HpLostByAdventurer[r1.Id] < plain.HpLostByAdventurer[r2.Id]);
        }
    }
}
