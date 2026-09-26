using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 階層ボス討伐の部隊火力（→ DungeonPowerCalculator、03 §4.5.4）のテスト。
    /// 2026年9月：職業×配置の個人CP補正を撤廃し、個人CP＝(Σ実効ステータス×重み＋装備の個人CPボーナス)×HP比率、
    /// 部隊火力＝個人CPの単純合算（完全解析なら+20%）へ純化した（→ 03 §0.23）。
    /// </summary>
    public class DungeonPowerCalculatorTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private static Adventurer Make(JobClass job, Placement placement, int stat = 40)
        {
            var a = new Adventurer
            {
                JobClass = job, Placement = placement,
                STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat,
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        /// <summary>重み付き能力値の合計（→ dungeon.csv BossPowerWeight_*）。装備なし・全能力が同値の場合。</summary>
        private static double WeightedSum(int stat) => DungeonBalance.BossPowerWeights.Sum(w => stat * w.Weight);

        [Theory]
        [InlineData(JobClass.Warrior)]
        [InlineData(JobClass.Knight)]
        [InlineData(JobClass.Ranger)]
        [InlineData(JobClass.Thief)]
        [InlineData(JobClass.Mage)]
        [InlineData(JobClass.Cleric)]
        [InlineData(JobClass.Scholar)]
        public void MemberPower_IsIndependentOfPlacement(JobClass job)
        {
            double front = DungeonPowerCalculator.MemberPower(Make(job, Placement.Front));
            double back = DungeonPowerCalculator.MemberPower(Make(job, Placement.Back));

            Assert.Equal(front, back, precision: 10);
            Assert.Equal(WeightedSum(40), front, precision: 10); // 配置補正の乗算が無い
        }

        [Fact]
        public void MemberPower_IsIdenticalAcrossJobClasses_ForSameStatsAndHp()
        {
            // 前衛職（旧1.2倍）・機動職（旧1.0倍）・後衛職（旧1.2倍）で差が付かない。
            var powers = Enum.GetValues<JobClass>()
                .Select(job => DungeonPowerCalculator.MemberPower(Make(job, PlacementRules.GetDefault(job))))
                .ToList();

            Assert.All(powers, p => Assert.Equal(powers[0], p, precision: 10));
        }

        [Fact]
        public void MemberPower_ScalesWithHpRatio()
        {
            var a = Make(JobClass.Warrior, Placement.Front);
            double full = DungeonPowerCalculator.MemberPower(a);
            a.CurrentHP = a.MaxHP / 2;

            Assert.Equal(full * ((double)a.CurrentHP / a.MaxHP), DungeonPowerCalculator.MemberPower(a), precision: 10);
        }

        [Fact]
        public void PartyPower_IsSimpleSumOfMemberPowers()
        {
            var members = new[]
            {
                Make(JobClass.Warrior, Placement.Front, 50),
                Make(JobClass.Mage, Placement.Back, 30),
                Make(JobClass.Thief, Placement.Front, 20),
            };
            members[1].CurrentHP = members[1].MaxHP / 3;

            double expected = members.Sum(m => DungeonPowerCalculator.MemberPower(m));

            Assert.Equal(expected, DungeonPowerCalculator.PartyPower(members), precision: 10);
            Assert.Equal(WeightedSum(50) + WeightedSum(20) + WeightedSum(30) * ((double)members[1].CurrentHP / members[1].MaxHP),
                DungeonPowerCalculator.PartyPower(members), precision: 10);
        }

        [Theory]
        [InlineData(1.0, true)]
        [InlineData(0.99, false)]
        public void DungeonResolver_PartyPower_IsSumTimesFullIntelBonus(double intelRate, bool expectBonus)
        {
            var members = new[] { Make(JobClass.Knight, Placement.Front), Make(JobClass.Cleric, Placement.Back) };
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            var boss = new FloorBoss { Name = "検証用の主", Floor = 10, MaxHp = 1, CurrentHp = 1, IntelRate = intelRate };
            double sum = members.Sum(m => DungeonPowerCalculator.MemberPower(m));

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(party, boss);

            Assert.Equal(expectBonus, result.FullIntelBonusApplied);
            Assert.Equal(expectBonus ? sum * (1.0 + DungeonBalance.FullIntelDamageBonus) : sum, result.PartyPower, precision: 10);
        }
    }
}
