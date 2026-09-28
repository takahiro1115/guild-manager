using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests.Balance
{
    /// <summary>
    /// 大迷宮の要求値の目安（§0.45）を固定するテスト。1つ目のフィールド（翠緑の原生林）について、
    ///  - 10F：各能力25の部隊が届く
    ///  - 100F：各能力90の部隊が届く（討伐は完全解析の+20%込み）
    /// を確かめる。討伐・走破は4人部隊、調査（護衛・隠密・解析）は斥候1名を含む3人の調査隊を基準にする。
    /// CSVの係数を動かしてこの目安が崩れたら、ここが落ちる。
    /// </summary>
    public class DungeonRequirementAnchorTests
    {
        private static Adventurer Make(JobClass job, int stat)
        {
            var a = new Adventurer
            {
                JobClass = job,
                STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat,
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            return party;
        }

        /// <summary>4人部隊（前衛・後衛の混成。重装の騎士を含む）。</summary>
        private static Party AssaultParty(int stat) =>
            PartyOf(Make(JobClass.Knight, stat), Make(JobClass.Ranger, stat), Make(JobClass.Mage, stat), Make(JobClass.Cleric, stat));

        /// <summary>3人の調査隊（斥候1名・重装なし）。</summary>
        private static Party SurveyParty(int stat) =>
            PartyOf(Make(JobClass.Ranger, stat), Make(JobClass.Mage, stat), Make(JobClass.Scholar, stat));

        private static FloorBoss Boss(int floor, double intelRate = 0.0) =>
            new() { Name = "目安のボス", Floor = floor, FieldOrder = 1, MaxHp = 1, CurrentHp = 1, IntelRate = intelRate };

        [Theory]
        [InlineData(10, 25, 0.0)]
        [InlineData(100, 90, 1.0)]
        public void BossPower_ReachesRequirement(int floor, int stat, double intelRate)
        {
            var boss = Boss(floor, intelRate);
            Assert.True(DungeonResolver.CalculateBossPower(AssaultParty(stat), boss) >= DungeonResolver.RequiredPower(boss));
        }

        [Theory]
        [InlineData(10, 25)]
        [InlineData(100, 90)]
        public void Survey_ReachesAllRequirements(int floor, int stat)
        {
            var boss = Boss(floor);
            var party = SurveyParty(stat);

            Assert.True(ScoutingResolver.PreviewGuardTier(party, boss) is GuardTier.Sufficient or GuardTier.Abundant);
            Assert.True(ScoutingResolver.CalculateStealthScore(party) >= ScoutingResolver.StealthRequirement(boss));
            Assert.True(ScoutingResolver.CalculateAnalysisScore(party) >= ScoutingResolver.AnalysisRequirement(boss));
        }

        [Fact]
        public void Traversal_EliteParty_IsAboutEvenAt100F()
        {
            // 走破は「基礎値0・階層に比例」の形（浅い階を速く抜けるため）なので、100Fで比率およそ1.0を目安にする。
            var field = new DungeonField { Id = "forest", Order = 1 };
            double ratio = DungeonTraversalResolver.CalculateTraversalScore(AssaultParty(90))
                / DungeonTraversalResolver.FloorRequirement(field, 100);
            Assert.InRange(ratio, 0.95, 1.2);
        }

        [Fact]
        public void OldRequirements_WereOutOfReach_ButNewOnesAreNot()
        {
            // 旧式（階層×係数）では、全能力100の4人部隊でも100Fの討伐火力4500に届かなかった（最大 100×4.2×4×1.2＝2016）。
            // 新しい式では同じ部隊が100Fの要求を上回る。
            double maxPower = DungeonResolver.CalculateBossPower(AssaultParty(100), Boss(100, intelRate: 1.0));
            Assert.True(maxPower < 100 * 45);
            Assert.True(maxPower >= DungeonResolver.RequiredPower(Boss(100)));
        }

        [Fact]
        public void FieldMultipliers_AreDefinedForAllFiveFields()
        {
            Assert.All(Enumerable.Range(1, 5), order => Assert.True(DungeonBalance.GetFieldRequirementMultiplier(order) > 0));
            Assert.Equal(1.0, DungeonBalance.GetFieldRequirementMultiplier(1), precision: 6);
        }
    }
}
