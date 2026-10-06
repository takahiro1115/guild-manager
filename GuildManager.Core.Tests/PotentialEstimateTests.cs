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
    /// 2026年10月・§0.74のテスト：PA を隠して副官の見立て（段階 S〜D・確か／「?」）にする。目利き・在籍の月日・伸び止まり・給与。
    /// 実行方法: `dotnet test GuildManager.Core.Tests --filter FullyQualifiedName~PotentialEstimate`
    /// </summary>
    public class PotentialEstimateTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        private static readonly string[] Stats = { "STR", "AGI", "VIT", "MND", "DEX", "LDR", "INT" };

        /// <summary>全能力 PA70・素の値40、ずれ offset（−100〜+100）の冒険者。</summary>
        private static Adventurer Make(int offset, JobClass job = JobClass.Warrior)
        {
            var a = new Adventurer
            {
                Name = "リナ", JobClass = job,
                PA_STR = 70, PA_AGI = 70, PA_VIT = 70, PA_MND = 70, PA_DEX = 70, PA_LDR = 70, PA_INT = 70,
                STR = 40, AGI = 40, VIT = 40, MND = 40, DEX = 40, LDR = 40, INT = 40,
            };
            foreach (var stat in Stats)
                a.PaEstimateOffsets[stat] = offset;
            return a;
        }

        [Fact]
        public void CsvValues_AreLoaded()
        {
            Assert.Equal(20, PotentialEstimateBalance.BaseWidth, precision: 6);
            Assert.Equal(0.8, PotentialEstimateBalance.MaxEye, precision: 6);
            Assert.Equal(0.004, PotentialEstimateBalance.ScoutMasterEyeCoeff, precision: 6);
            Assert.Equal(0.1, PotentialEstimateBalance.ResearchEyePerStep, precision: 6);
            Assert.Equal(0.002, PotentialEstimateBalance.ScholarEyeCoeff, precision: 6);
            Assert.Equal(0.1, PotentialEstimateBalance.MonthlyNarrowRate, precision: 6);
            Assert.Equal(0.5, PotentialEstimateBalance.SoulFusionWidthRate, precision: 6);
            Assert.Equal(1, PotentialEstimateBalance.ConfirmWidth, precision: 6);
            Assert.Equal((90, 80, 70, 55), (PotentialEstimateBalance.RankS, PotentialEstimateBalance.RankA, PotentialEstimateBalance.RankB, PotentialEstimateBalance.RankC));
        }

        [Theory]
        [InlineData(95, "S")]
        [InlineData(90, "S")]
        [InlineData(89, "A")]
        [InlineData(80, "A")]
        [InlineData(70, "B")]
        [InlineData(69, "C")]
        [InlineData(55, "C")]
        [InlineData(54, "D")]
        public void Rank_UsesThresholds(int pa, string rank) => Assert.Equal(rank, PotentialEstimateSystem.Rank(pa));

        [Theory]
        [InlineData(100, 90)]  // 70＋1.0×20
        [InlineData(-100, 50)] // 70−1.0×20
        [InlineData(50, 80)]
        [InlineData(0, 70)]
        public void Estimate_IsPaPlusOffsetTimesWidth_WithoutEye(int offset, int expected)
        {
            var state = new GameState();
            var a = Make(offset);

            Assert.Equal(20, PotentialEstimateSystem.GetWidth(state, a), precision: 6);
            Assert.Equal(expected, PotentialEstimateSystem.EstimatePa(state, a, "STR"));
            Assert.False(PotentialEstimateSystem.IsConfirmed(state, a, "STR"));
            Assert.EndsWith("?", PotentialEstimateSystem.RankLabel(state, a, "STR"));
        }

        [Fact]
        public void Estimate_IsNeverBelowCurrentStat()
        {
            var state = new GameState();
            var a = Make(-100);
            a.STR = 60; // 見立て50より今の値が高い

            Assert.Equal(60, PotentialEstimateSystem.EstimatePa(state, a, "STR"));
        }

        [Fact]
        public void StatAtPa_IsConfirmedAndExact()
        {
            var state = new GameState();
            var a = Make(100);
            a.STR = 70; // 伸び止まった

            Assert.Equal(70, PotentialEstimateSystem.EstimatePa(state, a, "STR"));
            Assert.True(PotentialEstimateSystem.IsConfirmed(state, a, "STR"));
            Assert.Equal("B", PotentialEstimateSystem.RankLabel(state, a, "STR"));
            Assert.Equal("S?", PotentialEstimateSystem.RankLabel(state, a, "VIT")); // ほかの能力はまだ見立て
        }

        [Fact]
        public void Eye_AddsScoutMasterResearchAndScholar_UpToMax()
        {
            var state = new GameState();
            Assert.Equal(0, PotentialEstimateSystem.GetEye(state), precision: 6);

            var scout = new Adventurer { Name = "スカウト", IsRetired = true, LDR = 80, DEX = 80 };
            state.RetiredAdventurers.Add(scout);
            state.AssignedScoutMaster = scout.Id;
            Assert.Equal(0.32, PotentialEstimateSystem.GetEye(state), precision: 6);

            foreach (var r in ResearchBalance.GetAll().Where(r => r.EffectType == ResearchEffectType.RecruitPaBonus))
                state.CompletedResearchIds.Add(r.Id);
            Assert.Equal(0.62, PotentialEstimateSystem.GetEye(state), precision: 6); // 研究3段 +0.3

            state.Adventurers.Add(new Adventurer { Name = "学者", JobClass = JobClass.Scholar, INT = 80 });
            Assert.Equal(0.78, PotentialEstimateSystem.GetEye(state), precision: 6); // +0.16

            state.Adventurers.Add(new Adventurer { Name = "学者2", JobClass = JobClass.Scholar, INT = 100 });
            Assert.Equal(0.8, PotentialEstimateSystem.GetEye(state), precision: 6); // 上限
        }

        [Fact]
        public void Width_NarrowsWithEye_Months_AndSoulFusionParents()
        {
            var state = new GameState();
            state.Adventurers.Add(new Adventurer { Name = "学者", JobClass = JobClass.Scholar, INT = 100 }); // 目利き0.2
            var a = Make(100);

            Assert.Equal(16, PotentialEstimateSystem.GetWidth(state, a), precision: 6);
            a.ActiveWeeks = 4 * 5; // 5か月
            Assert.Equal(8, PotentialEstimateSystem.GetWidth(state, a), precision: 6);
            a.ParentIds = new() { Guid.NewGuid(), Guid.NewGuid() };
            Assert.Equal(4, PotentialEstimateSystem.GetWidth(state, a), precision: 6);

            a.ParentIds.Clear();
            a.ActiveWeeks = 4 * 10; // 10か月で確定
            Assert.Equal(0, PotentialEstimateSystem.GetWidth(state, a), precision: 6);
            Assert.Equal("B", PotentialEstimateSystem.RankLabel(state, a, "STR"));
            Assert.Equal("B", PotentialEstimateSystem.TotalRankLabel(state, a));
        }

        [Fact]
        public void TotalEstimate_IsAverageOfEstimates()
        {
            var state = new GameState();
            var a = Make(0);
            a.PaEstimateOffsets["STR"] = 100; // STR だけ +20

            Assert.Equal((90 + 70 * 6) / 7.0, PotentialEstimateSystem.EstimateTotalPa(state, a), precision: 6);
            Assert.Equal("B?", PotentialEstimateSystem.TotalRankLabel(state, a));
        }

        [Fact]
        public void AdventurerWithoutOffsets_IsEstimatedAtTruePa()
        {
            var state = new GameState();
            var a = Make(0);
            a.PaEstimateOffsets.Clear();
            Assert.Equal(70, PotentialEstimateSystem.EstimatePa(state, a, "INT"));
        }

        // ==================== 給与＝副官の見立て ====================

        [Fact]
        public void Recruitment_AssignsOffsets_AndWageFollowsTheEstimate()
        {
            var state = new GameState();
            var low = new RecruitmentSystem(new AlwaysMinRng()).GenerateCandidates(state)[0];
            var high = new RecruitmentSystem(new AlwaysMaxRng()).GenerateCandidates(state)[0];

            foreach (var offer in new[] { low, high })
            {
                var c = offer.Candidate;
                Assert.Equal(7, c.PaEstimateOffsets.Count);
                double estimate = PotentialEstimateSystem.EstimateTotalPa(state, c);
                int wage = Math.Max(1, (int)(estimate * EconomyBalance.WeeklyWageCoefficient));
                if (c.HasTrait(TraitCatalog.SpendthriftId)) wage = Math.Max(1, (int)(wage * TraitBalance.SpendthriftWageMultiplier));
                Assert.Equal(wage, c.WeeklyWage);
                Assert.Equal((int)(estimate * c.Age * EconomyBalance.SigningBonusCoefficient), offer.SigningBonus);
            }
            Assert.All(low.Candidate.PaEstimateOffsets.Values, o => Assert.Equal(-100, o)); // 低く見立てた
            Assert.True(PotentialEstimateSystem.EstimateTotalPa(state, low.Candidate) < low.Candidate.TotalPA);
        }

        [Fact]
        public void UnderestimatedHire_FeelsUnderpaid()
        {
            // 見立てが低すぎて、本当の素質に見合う週給（適正×0.8）に届かないと、毎週の賃金の不満が出る
            var state = new GameState();
            var a = Make(-100);
            a.PA_STR = a.PA_AGI = a.PA_VIT = a.PA_MND = a.PA_DEX = a.PA_LDR = a.PA_INT = 100; // 本当は全能力100、見立ては80
            a.WeeklyWage = (int)(PotentialEstimateSystem.EstimateTotalPa(state, a) * EconomyBalance.WeeklyWageCoefficient * 0.7);
            a.Satisfaction = 50;
            state.Adventurers.Add(a);

            new SatisfactionSystem().ProcessWeeklySatisfaction(state, new System.Collections.Generic.HashSet<Guid>());

            Assert.True(a.Satisfaction < 50);
        }
    }
}
