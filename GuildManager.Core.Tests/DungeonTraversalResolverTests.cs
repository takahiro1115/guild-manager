using System;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 道中進軍（→ DungeonTraversalResolver、03 §4.5.2「大迷宮5フィールド拡張仕様」）の単体テスト。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~DungeonTraversal`
    /// </summary>
    public class DungeonTraversalResolverTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        private static Adventurer MakeSpecialist(int agiDex, int ldr = 0)
        {
            var a = new Adventurer { STR = 10, AGI = agiDex, VIT = 30, MND = 10, DEX = agiDex, LDR = ldr, INT = 10 };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            return party;
        }

        private static DungeonField MakeField(int reachedFloor = 1) =>
            new() { Id = "test", Name = "テスト用フィールド", Order = 1, IsUnlocked = true, ReachedFloor = reachedFloor };

        // ---------------- 走破力・要求値・ランク分類の式 ----------------

        [Fact]
        public void CalculateTraversalScore_MatchesFormula()
        {
            var party = PartyOf(MakeSpecialist(agiDex: 40, ldr: 20), MakeSpecialist(agiDex: 30));

            // Σ(AGI+DEX)=(40+40)+(30+30)=140 ×1.0 ＋ 部隊長LDR20×0.5 ＝ 150（→ dungeon_traversal.csv）
            Assert.Equal(150, DungeonTraversalResolver.CalculateTraversalScore(party), precision: 6);
            Assert.Equal(0, DungeonTraversalResolver.CalculateTraversalScore(new Party()));
        }

        [Fact]
        public void CurrentFloorRequirement_ScalesWithReachedFloor()
        {
            var field = MakeField(reachedFloor: 3);
            Assert.Equal(3 * DungeonTraversalBalance.RequirementPerFloor, DungeonTraversalResolver.CurrentFloorRequirement(field), precision: 6);
        }

        [Theory]
        [InlineData(2.0, TraversalRank.Lightning)]
        [InlineData(1.8, TraversalRank.Lightning)]
        [InlineData(1.79, TraversalRank.Swift)]
        [InlineData(1.4, TraversalRank.Swift)]
        [InlineData(1.39, TraversalRank.Normal)]
        [InlineData(1.0, TraversalRank.Normal)]
        [InlineData(0.99, TraversalRank.Struggling)]
        [InlineData(0.0, TraversalRank.Struggling)]
        public void ClassifyRatio_ReturnsExpectedRank(double ratio, TraversalRank expected) =>
            Assert.Equal(expected, DungeonTraversalResolver.ClassifyRatio(ratio));

        [Theory]
        [InlineData(TraversalRank.Lightning, 4)]
        [InlineData(TraversalRank.Swift, 3)]
        [InlineData(TraversalRank.Normal, 2)]
        [InlineData(TraversalRank.Struggling, 1)]
        public void FloorsAdvanced_MatchesRankTable(TraversalRank rank, int expectedFloors) =>
            Assert.Equal(expectedFloors, DungeonTraversalResolver.FloorsAdvanced(rank));

        // ---------------- Resolve（進軍・ストッパー・HP消費） ----------------

        [Fact]
        public void Resolve_StrongParty_AdvancesLightningRank_AndUpdatesReachedFloor()
        {
            // 要求値=1×15=15。Σ(AGI+DEX)=2000で確実にRatio>=1.8（電撃進軍・+4階層）になる。
            var party = PartyOf(MakeSpecialist(agiDex: 500, ldr: 100));
            var field = MakeField(reachedFloor: 1);
            var nextBoss = new FloorBoss { Name = "遠くのボス", Floor = 100, MaxHp = 1 };
            var resolver = new DungeonTraversalResolver(new AlwaysMinRng());

            var result = resolver.Resolve(party, field, nextBoss);

            Assert.Equal(TraversalRank.Lightning, result.Rank);
            Assert.Equal(1, result.FloorBefore);
            Assert.Equal(5, result.FloorAfter); // 1 + 4
            Assert.Equal(5, field.ReachedFloor);
            Assert.False(result.StopperTriggered);
            Assert.Null(result.TargetBoss);
        }

        [Fact]
        public void Resolve_WeakParty_StillAdvancesAtLeastOneFloor_StrugglingRank()
        {
            var party = PartyOf(MakeSpecialist(agiDex: 1));
            var field = MakeField(reachedFloor: 10);
            var nextBoss = new FloorBoss { Name = "遠くのボス", Floor = 100, MaxHp = 1 };
            var resolver = new DungeonTraversalResolver(new AlwaysMinRng());

            var result = resolver.Resolve(party, field, nextBoss);

            Assert.Equal(TraversalRank.Struggling, result.Rank);
            Assert.Equal(11, result.FloorAfter); // 走破力が足りなくても必ず+1階層
            Assert.False(result.StopperTriggered);
        }

        [Fact]
        public void Resolve_ClampsAtNextBossFloor_AndReportsStopper()
        {
            // 電撃進軍でも、目の前が未撃破の6Fボスなら6Fで止まる（5F→9Fにはならない）。
            var party = PartyOf(MakeSpecialist(agiDex: 500, ldr: 100));
            var field = MakeField(reachedFloor: 5);
            var nextBoss = new FloorBoss { Name = "目前のボス", Floor = 6, MaxHp = 1 };
            var resolver = new DungeonTraversalResolver(new AlwaysMinRng());

            var result = resolver.Resolve(party, field, nextBoss);

            Assert.Equal(6, result.FloorAfter);
            Assert.Equal(6, field.ReachedFloor);
            Assert.True(result.StopperTriggered);
            Assert.Same(nextBoss, result.TargetBoss);
        }

        [Fact]
        public void Resolve_HpLoss_IsLowForLightningRank_AndHighForStrugglingRank_AndNeverBelowMinHp()
        {
            var strongParty = PartyOf(MakeSpecialist(agiDex: 500, ldr: 100));
            var weakMember = MakeSpecialist(agiDex: 1);
            weakMember.CurrentHP = 1; // 既に瀕死：それ以上は減らないはず（下限1保証）
            var weakParty = PartyOf(weakMember);
            var farBoss = new FloorBoss { Name = "遠くのボス", Floor = 100, MaxHp = 1 };

            var maxRng = new AlwaysMaxRng();
            var lightning = new DungeonTraversalResolver(maxRng).Resolve(strongParty, MakeField(1), farBoss);
            Assert.InRange(lightning.HpLostByAdventurer[strongParty.Members[0].Id],
                0, strongParty.Members[0].MaxHP * DungeonTraversalBalance.HpLossPctMaxLightning / 100);

            var struggling = new DungeonTraversalResolver(maxRng).Resolve(weakParty, MakeField(1), farBoss);
            Assert.Equal(1, weakMember.CurrentHP); // 下限1でクランプ（HPロスは0扱い）
            Assert.Equal(0, struggling.HpLostByAdventurer[weakMember.Id]);
        }

        [Fact]
        public void Resolve_EmptyParty_Throws()
        {
            var resolver = new DungeonTraversalResolver(new AlwaysMinRng());
            Assert.Throws<InvalidOperationException>(() =>
                resolver.Resolve(new Party(), MakeField(), new FloorBoss { Floor = 10, MaxHp = 1 }));
        }
    }
}
