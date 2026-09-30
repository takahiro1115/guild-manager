using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests.Balance
{
    /// <summary>
    /// 大迷宮の要求値の目安（§0.47、§0.51で傾きを緩めた）を固定するテスト。
    ///  - 翠緑の原生林の10F：各能力25の部隊が届く
    ///  - 翠緑の原生林の100F：各能力55の部隊が届く（討伐は完全解析の+20%込み）
    ///  - 最終フィールド（深淵の特異点）の100F：各能力70の部隊が届く
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

        private static FloorBoss Boss(int floor, double intelRate = 0.0, int fieldOrder = 1) =>
            new() { Name = "目安のボス", Floor = floor, FieldOrder = fieldOrder, MaxHp = 1, CurrentHp = 1, IntelRate = intelRate };

        [Theory]
        [InlineData(10, 25, 0.0, 1)]
        [InlineData(100, 55, 1.0, 1)]
        [InlineData(100, 70, 1.0, 5)]
        public void BossPower_ReachesRequirement(int floor, int stat, double intelRate, int fieldOrder)
        {
            var boss = Boss(floor, intelRate, fieldOrder);
            Assert.True(DungeonResolver.CalculateBossPower(AssaultParty(stat), boss) >= DungeonResolver.RequiredPower(boss));
        }

        [Theory]
        [InlineData(10, 25, 1)]
        [InlineData(100, 55, 1)]
        [InlineData(100, 70, 5)]
        public void Survey_ReachesAllRequirements(int floor, int stat, int fieldOrder)
        {
            var boss = Boss(floor, fieldOrder: fieldOrder);
            var party = SurveyParty(stat);

            Assert.True(ScoutingResolver.PreviewGuardTier(party, boss) is GuardTier.Sufficient or GuardTier.Abundant);
            Assert.True(ScoutingResolver.CalculateStealthScore(party) >= ScoutingResolver.StealthRequirement(boss));
            Assert.True(ScoutingResolver.CalculateAnalysisScore(party) >= ScoutingResolver.AnalysisRequirement(boss));
        }

        [Theory]
        [InlineData(55, 1)]
        [InlineData(70, 5)]
        public void Traversal_EliteParty_IsAboutEvenAt100F(int stat, int fieldOrder)
        {
            // 走破は「基礎値0・階層に比例」の形（浅い階を速く抜けるため）なので、100Fで比率およそ1.0を目安にする
            // （§0.49以降、1歩の重さはその階層の要求値で決まる：比率1.0の深さでは未解析で週4階層、完全解析で週12階層）。
            var field = new DungeonField { Id = "forest", Order = fieldOrder };
            double ratio = DungeonTraversalResolver.CalculateTraversalScore(AssaultParty(stat))
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

        [Fact]
        public void FieldMultipliers_RiseByFieldOrder()
        {
            // §0.50：後のフィールドほど難しくする（森→鍾乳洞→廃墟→峡谷→深淵）。
            for (int order = 2; order <= 5; order++)
                Assert.True(DungeonBalance.GetFieldRequirementMultiplier(order) > DungeonBalance.GetFieldRequirementMultiplier(order - 1));
        }

        [Theory]
        [InlineData(1, 40, 31, 32)]  // 森40F（伝説級）
        [InlineData(2, 40, 34, 35)]  // 鍾乳洞40F（伝説級）
        [InlineData(3, 40, 36, 37)]  // 廃墟40F（伝説級）
        [InlineData(4, 40, 38, 39)]  // 峡谷40F（伝説級）
        [InlineData(5, 50, 45, 46)]  // 深淵50F（伝説級）
        [InlineData(1, 100, 54, 55)] // 森100F
        [InlineData(5, 100, 68, 69)] // 深淵100F（最終目標）
        public void LegendaryBosses_NeedStagedStats(int fieldOrder, int floor, int notEnough, int enough)
        {
            // 伝説級・100Fのボスに、完全解析・HP満タンの4人部隊で届く全能力の目安（§0.50・§0.51）。フィールドごとに段階的に上がる。
            var boss = new FloorBoss { Name = "伝説級の主", Floor = floor, FieldOrder = fieldOrder, MaxHp = 1, CurrentHp = 1, IntelRate = 1.0 };
            Assert.True(DungeonResolver.CalculateBossPower(AssaultParty(notEnough), boss) < DungeonResolver.RequiredPower(boss));
            Assert.True(DungeonResolver.CalculateBossPower(AssaultParty(enough), boss) >= DungeonResolver.RequiredPower(boss));
        }
    }
}
