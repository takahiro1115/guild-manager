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
            // 既踏の階層（最高到達階層100Fの記録があるフィールドを1Fから進む）だけを進む場合の、
            // 進軍ランク別の消費率を確かめる（未踏破を含む進軍は重損耗になるため、→ 下の未踏破テスト）。
            var maxRng = new AlwaysMaxRng();
            var lightning = new DungeonTraversalResolver(maxRng).Resolve(strongParty, MakeField(100), currentFloor: 1);
            Assert.False(lightning.EnteredUnexplored);
            Assert.InRange(lightning.HpLostByAdventurer[strongParty.Members[0].Id],
                0, strongParty.Members[0].MaxHP * DungeonTraversalBalance.HpLossPctMaxLightning / 100);

            var struggling = new DungeonTraversalResolver(maxRng).Resolve(weakParty, MakeField(100), currentFloor: 1);
            Assert.Equal(1, weakMember.CurrentHP); // 下限1でクランプ（HPロスは0扱い）
            Assert.Equal(0, struggling.HpLostByAdventurer[weakMember.Id]);
        }

        // ---------------- 調査度連動の電撃走破（2026年9月新設） ----------------

        [Theory]
        [InlineData(0.0, 1.0)]
        [InlineData(0.5, 2.0)]
        [InlineData(1.0, 3.0)]
        public void IntelSpeedMultiplier_ScalesFromOneToThree(double intelRate, double expected) =>
            Assert.Equal(expected, DungeonTraversalResolver.IntelSpeedMultiplier(new FloorBoss { IntelRate = intelRate }), precision: 6);

        [Fact]
        public void Traversal_WithFullIntel_AppliesTripleSpeedAndLowDamage()
        {
            // 同じ電撃進軍（+4階層相当の予算）でも、区間担当ボスが完全解析済みなら3倍の12階層進み、
            // HP損耗は70%軽減される。比較用に未調査の同条件も解決する。
            var fullMember = MakeSpecialist(agiDex: 500, ldr: 100);
            var noneMember = MakeSpecialist(agiDex: 500, ldr: 100);
            var fullField = MakeField();
            fullField.Bosses.Add(new FloorBoss { Name = "解析済みのボス", Floor = 50, MaxHp = 1, IntelRate = 1.0 });
            var noneField = MakeField();
            noneField.Bosses.Add(new FloorBoss { Name = "未調査のボス", Floor = 50, MaxHp = 1, IntelRate = 0.0 });

            var full = new DungeonTraversalResolver(new AlwaysMaxRng()).Resolve(PartyOf(fullMember), fullField, currentFloor: 1);
            var none = new DungeonTraversalResolver(new AlwaysMaxRng()).Resolve(PartyOf(noneMember), noneField, currentFloor: 1);

            Assert.Equal(TraversalRank.Lightning, full.Rank);
            Assert.Equal(4, none.FloorAfter - none.FloorBefore);
            Assert.Equal(12, full.FloorAfter - full.FloorBefore); // 3倍
            Assert.Equal(3.0, full.IntelSpeedMultiplier, precision: 6);
            Assert.Equal(DungeonTraversalBalance.FullIntelDamageMultiplier, full.DamageTakenMultiplier, precision: 6);
            Assert.Equal(1.0, none.DamageTakenMultiplier, precision: 6);

            int fullLoss = full.HpLostByAdventurer[fullMember.Id];
            int noneLoss = none.HpLostByAdventurer[noneMember.Id];
            Assert.True(noneLoss > 0);
            Assert.Equal((int)(noneLoss * DungeonTraversalBalance.FullIntelDamageMultiplier), fullLoss);
        }

        [Fact]
        public void Resolve_FromCurrentFloor_StopsAtFirstUndefeatedBoss_AndKeepsReachedFloorRecord()
        {
            // 潜行中の部隊は現在階層から進み、撃破済みボスは素通りして最初の未撃破ボスで止まる。
            // フィールドの最高到達階層（記録）は、より深く潜れた場合しか更新しない。
            var field = MakeField(reachedFloor: 30);
            var defeated = new FloorBoss { Name = "撃破済み", Floor = 3, MaxHp = 1, IsDefeated = true };
            var target = new FloorBoss { Name = "未撃破", Floor = 4, MaxHp = 1 };
            field.Bosses.Add(defeated);
            field.Bosses.Add(target);
            var party = PartyOf(MakeSpecialist(agiDex: 500, ldr: 100));

            var result = new DungeonTraversalResolver(new AlwaysMinRng()).Resolve(party, field, currentFloor: 1);

            Assert.Equal(4, result.FloorAfter);
            Assert.True(result.StopperTriggered);
            Assert.Same(target, result.TargetBoss);
            Assert.Equal(30, field.ReachedFloor);
            Assert.Equal(3 * DungeonTraversalBalance.LootGoldPerFloor, result.LootGold);
        }

        // ---------------- 未踏破階層の重損耗×調査度連動（2026年9月新設） ----------------

        /// <summary>未踏破（最高到達階層1F）のフィールドに、指定の解析率のボスを50Fに置く。</summary>
        private static DungeonField MakeUnexploredField(double intelRate)
        {
            var field = MakeField(reachedFloor: 1);
            field.Bosses.Add(new FloorBoss { Name = "区間のボス", Floor = 50, MaxHp = 1, IntelRate = intelRate });
            return field;
        }

        private static Adventurer MakeSturdySpecialist()
        {
            // MaxHPを大きくして、%消費の整数丸めの影響を小さくする。
            var a = new Adventurer { STR = 10, AGI = 500, VIT = 300, MND = 10, DEX = 500, LDR = 100, INT = 10 };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Traversal_IntelRate_Zero_AppliesFullUnexploredDamage(bool maxRoll)
        {
            // 解析率0%：走破倍率1.0倍（電撃進軍の4階層そのまま）、未踏破の重損耗30〜50%を軽減なしで受ける。
            var member = MakeSturdySpecialist();
            IRng rng = maxRoll ? new AlwaysMaxRng() : new AlwaysMinRng();

            var result = new DungeonTraversalResolver(rng).Resolve(PartyOf(member), MakeUnexploredField(0.0), currentFloor: 1);

            int pct = maxRoll ? DungeonBalance.UnexploredHpLossPctMax : DungeonBalance.UnexploredHpLossPctMin;
            Assert.Equal(30, DungeonBalance.UnexploredHpLossPctMin);
            Assert.Equal(50, DungeonBalance.UnexploredHpLossPctMax);
            Assert.True(result.EnteredUnexplored);
            Assert.Equal(1.0, result.IntelSpeedMultiplier, precision: 6);
            Assert.Equal(1.0, result.DamageTakenMultiplier, precision: 6);
            Assert.Equal(DungeonTraversalBalance.FloorsAdvancedLightning, result.FloorAfter - result.FloorBefore);
            Assert.Equal(member.MaxHP * pct / 100, result.HpLostByAdventurer[member.Id]);
        }

        [Fact]
        public void Traversal_IntelRate_Complete_AppliesMaxBoostAndReduction()
        {
            // 解析率100%：進む階層数が3倍（4→12）、未踏破の重損耗も70%カット（50%→15%）。
            var member = MakeSturdySpecialist();

            var result = new DungeonTraversalResolver(new AlwaysMaxRng()).Resolve(PartyOf(member), MakeUnexploredField(1.0), currentFloor: 1);

            Assert.True(result.EnteredUnexplored);
            Assert.Equal(3.0, result.IntelSpeedMultiplier, precision: 6);
            Assert.Equal(DungeonTraversalBalance.FloorsAdvancedLightning * 3, result.FloorAfter - result.FloorBefore);
            Assert.Equal(0.3, result.DamageTakenMultiplier, precision: 6);
            int baseLoss = member.MaxHP * DungeonBalance.UnexploredHpLossPctMax / 100;
            Assert.Equal((int)(baseLoss * 0.3), result.HpLostByAdventurer[member.Id]);
        }

        [Fact]
        public void Traversal_IntelRate_Partial_AppliesLinearInterpolation()
        {
            // 解析率50%：走破倍率は線形補間で2.0倍（4→8階層）。被ダメージ軽減は完全解析区間のみの
            // 仕組みを正本として維持するため、50%時点では軽減なし（未踏破の重損耗をそのまま受ける）。
            var member = MakeSturdySpecialist();

            var result = new DungeonTraversalResolver(new AlwaysMaxRng()).Resolve(PartyOf(member), MakeUnexploredField(0.5), currentFloor: 1);

            Assert.Equal(2.0, result.IntelSpeedMultiplier, precision: 6);
            Assert.Equal(DungeonTraversalBalance.FloorsAdvancedLightning * 2, result.FloorAfter - result.FloorBefore);
            Assert.Equal(1.0, result.DamageTakenMultiplier, precision: 6);
            Assert.Equal(member.MaxHP * DungeonBalance.UnexploredHpLossPctMax / 100, result.HpLostByAdventurer[member.Id]);
        }

        [Fact]
        public void Traversal_Unexplored_NeverKillsOrInjures()
        {
            // 重損耗でもHP下限1で止まり、負傷・除籍には接続しない。
            var member = MakeSturdySpecialist();
            member.CurrentHP = 2;

            new DungeonTraversalResolver(new AlwaysMaxRng()).Resolve(PartyOf(member), MakeUnexploredField(0.0), currentFloor: 1);

            Assert.Equal(1, member.CurrentHP);
            Assert.Equal(InjurySeverity.None, member.Injury);
            Assert.False(member.IsRetired);
        }

        [Fact]
        public void Resolve_EmptyParty_Throws()
        {
            var resolver = new DungeonTraversalResolver(new AlwaysMinRng());
            Assert.Throws<InvalidOperationException>(() =>
                resolver.Resolve(new Party(), MakeField(), new FloorBoss { Floor = 10, MaxHp = 1 }));
        }

        // ---------------- 参謀のルート指導（→ AdvisorSystem.GetAdvisorTraversalPowerBonus、2026年9月再配線） ----------------

        /// <summary>7能力がすべて statAverage の参謀を作戦資料室に任命した状態。</summary>
        private static GameState StateWithAdvisor(int statAverage)
        {
            var advisor = new Adventurer
            {
                Name = "参謀ガレス",
                STR = statAverage, AGI = statAverage, VIT = statAverage, MND = statAverage,
                DEX = statAverage, LDR = statAverage, INT = statAverage,
            };
            return new GameState { RetiredAdventurers = { advisor }, AssignedAdvisor = advisor.Id };
        }

        [Fact]
        public void CalculateTraversalScore_AddsAdvisorBonus_WhenAdvisorIsAssigned()
        {
            // 7能力平均50 × Advisor_TraversalPowerBonusCoeff(0.2) ＝ +10。
            var party = PartyOf(MakeSpecialist(agiDex: 40, ldr: 20)); // Σ(AGI+DEX)=80 ＋ LDR20×0.5 ＝ 90
            double withoutState = DungeonTraversalResolver.CalculateTraversalScore(party);

            double withAdvisor = DungeonTraversalResolver.CalculateTraversalScore(party, StateWithAdvisor(statAverage: 50));

            Assert.Equal(90, withoutState, precision: 6);
            Assert.Equal(90 + 10, withAdvisor, precision: 6);
            Assert.Equal(0.2, AdvisorBalance.TraversalPowerBonusCoefficient, precision: 6);
        }

        [Fact]
        public void CalculateTraversalScore_NoAdvisorBonus_WhenUnassigned()
        {
            var party = PartyOf(MakeSpecialist(agiDex: 40, ldr: 20));

            double withoutAdvisor = DungeonTraversalResolver.CalculateTraversalScore(party, new GameState());

            Assert.Equal(90, withoutAdvisor, precision: 6);
        }

        [Fact]
        public void Resolve_AdvisorBonus_RaisesEffectiveTraversalPower_AndIsReported()
        {
            // 1Fの要求値15。部隊だけなら走破力12（Ratio0.8＝苦戦・+1階層）だが、
            // 参謀の+10で22（Ratio1.47＝迅速・+3階層）まで押し上がる。
            var field = MakeField(reachedFloor: 1);
            field.Bosses.Add(new FloorBoss { Name = "遠くのボス", Floor = 50, MaxHp = 1 });
            var resolver = new DungeonTraversalResolver(new AlwaysMinRng());

            var withoutAdvisor = resolver.Resolve(PartyOf(MakeSpecialist(agiDex: 6)), MakeField(reachedFloor: 1), currentFloor: 1);
            var withAdvisor = resolver.Resolve(PartyOf(MakeSpecialist(agiDex: 6)), field, currentFloor: 1, state: StateWithAdvisor(statAverage: 50));

            Assert.Equal(TraversalRank.Struggling, withoutAdvisor.Rank);
            Assert.Equal(0, withoutAdvisor.AdvisorTraversalBonus, precision: 6);
            Assert.Null(withoutAdvisor.AdvisorName);

            Assert.Equal(TraversalRank.Swift, withAdvisor.Rank);
            Assert.Equal(10, withAdvisor.AdvisorTraversalBonus, precision: 6);
            Assert.Equal("参謀ガレス", withAdvisor.AdvisorName);
            Assert.True(withAdvisor.FloorAfter - withAdvisor.FloorBefore > withoutAdvisor.FloorAfter - withoutAdvisor.FloorBefore,
                "参謀のルート指導で、同じ部隊でもより深くまで進めるはず");
        }
    }
}
