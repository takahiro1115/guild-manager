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

        /// <summary>
        /// 隠密型の冒険者（AGI/DEXが高い）。2026年9月の走破力改訂（VIT/MND化、→ 03 §4.5.3）以降、
        /// このパラメータは走破力に影響しない＝「素早いだけでは道中を進めない」ことの確認に使う。
        /// </summary>
        private static Adventurer MakeSpecialist(int agiDex, int ldr = 0)
        {
            var a = new Adventurer { STR = 10, AGI = agiDex, VIT = 30, MND = 10, DEX = agiDex, LDR = ldr, INT = 10 };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        /// <summary>
        /// 走破型の冒険者（VIT/MNDを明示する）。走破力＝Σ(VIT×1.0＋MND×0.8)＋部隊長LDR×1.0
        /// （→ dungeon_traversal.csv の Traversal_Weight_*）。
        /// </summary>
        private static Adventurer MakeTraveler(int vit, int mnd, int ldr = 0)
        {
            var a = new Adventurer { STR = 10, AGI = 10, VIT = vit, MND = mnd, DEX = 10, LDR = ldr, INT = 10 };
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
            var party = PartyOf(MakeTraveler(vit: 40, mnd: 20, ldr: 10), MakeTraveler(vit: 30, mnd: 10));

            // Σ(VIT×1.0＋MND×0.8)=(40+16)+(30+8)=94 ＋ 部隊長LDR10×1.0 ＝ 104（→ dungeon_traversal.csv）
            Assert.Equal(104, DungeonTraversalResolver.CalculateTraversalScore(party), precision: 6);
            Assert.Equal(0, DungeonTraversalResolver.CalculateTraversalScore(new Party()));
        }

        [Fact]
        public void CalculateTraversalScore_ConsidersVitAndMnd()
        {
            // VITを上げれば走破力は上がる（重み1.0）。
            double lowVit = DungeonTraversalResolver.CalculateTraversalScore(PartyOf(MakeTraveler(vit: 20, mnd: 20)));
            double highVit = DungeonTraversalResolver.CalculateTraversalScore(PartyOf(MakeTraveler(vit: 60, mnd: 20)));
            Assert.Equal(40 * DungeonTraversalBalance.WeightVit, highVit - lowVit, precision: 6);

            // MNDを上げても上がる（重み0.8）。
            double lowMnd = DungeonTraversalResolver.CalculateTraversalScore(PartyOf(MakeTraveler(vit: 20, mnd: 10)));
            double highMnd = DungeonTraversalResolver.CalculateTraversalScore(PartyOf(MakeTraveler(vit: 20, mnd: 50)));
            Assert.Equal(40 * DungeonTraversalBalance.WeightMnd, highMnd - lowMnd, precision: 6);
        }

        [Fact]
        public void CalculateTraversalScore_IgnoresAgiAndDex()
        {
            // 2026年9月改訂の眼目（→ 03 §4.5.3）：AGI/DEXをいくら盛っても走破力は動かない。
            // 旧モデルはAGI+DEX合算で、隠密適性とまったく同じ値になっていた。
            var nimble = PartyOf(MakeSpecialist(agiDex: 90, ldr: 0));
            var clumsy = PartyOf(MakeSpecialist(agiDex: 5, ldr: 0));

            Assert.Equal(
                DungeonTraversalResolver.CalculateTraversalScore(clumsy),
                DungeonTraversalResolver.CalculateTraversalScore(nimble),
                precision: 6);
        }

        [Fact]
        public void CalculateTraversalScore_IncludesLeaderLdr()
        {
            // 部隊長（先頭メンバー）のLDRのみが加算される（2人目以降のLDRは効かない）。
            double withoutLeaderLdr = DungeonTraversalResolver.CalculateTraversalScore(
                PartyOf(MakeTraveler(vit: 30, mnd: 10, ldr: 0), MakeTraveler(vit: 30, mnd: 10, ldr: 40)));
            double withLeaderLdr = DungeonTraversalResolver.CalculateTraversalScore(
                PartyOf(MakeTraveler(vit: 30, mnd: 10, ldr: 40), MakeTraveler(vit: 30, mnd: 10, ldr: 0)));

            Assert.Equal(40 * DungeonTraversalBalance.WeightLdr, withLeaderLdr - withoutLeaderLdr, precision: 6);
        }

        [Fact]
        public void CurrentFloorRequirement_ScalesWithReachedFloor()
        {
            var field = MakeField(reachedFloor: 3);
            Assert.Equal(3 * DungeonTraversalBalance.RequirementPerFloor, DungeonTraversalResolver.CurrentFloorRequirement(field), precision: 6);
        }

        [Fact]
        public void FloorRequirement_AppliesFieldMultiplier()
        {
            var field = MakeField(reachedFloor: 1);
            field.Order = 4;
            Assert.Equal(20 * DungeonTraversalBalance.RequirementPerFloor * DungeonBalance.GetFieldRequirementMultiplier(4),
                DungeonTraversalResolver.FloorRequirement(field, 20), precision: 6);
        }

        // ---------------- 比率と進軍ランク（§0.49：週に進める階層数＝floor(Ratio×4.0)、ランクの段＝floor(Ratio×2.0)と別々） ----------------

        [Theory]
        [InlineData(0.0, 1)]    // 走破力ゼロでも1階層
        [InlineData(0.24, 1)]
        [InlineData(0.49, 1)]
        [InlineData(0.99, 3)]
        [InlineData(1.0, 4)]    // 比率1.0の深さで週4階層（FloorsPerRatio）
        [InlineData(1.5, 6)]
        [InlineData(2.99, 11)]
        [InlineData(20.0, 80)]  // 上限なし
        public void CalculateBaseFloors_IsWeeklyFloorsAtThatDepth(double ratio, int expected)
        {
            Assert.Equal(4.0, DungeonTraversalBalance.FloorsPerRatio, precision: 6);
            Assert.Equal(expected, DungeonTraversalResolver.CalculateBaseFloors(ratio));
        }

        [Fact]
        public void ClassifyRatio_IsIndependentOfFloorsPerRatio()
        {
            // 進む速さ（FloorsPerRatio 4.0）を上げても、ランク＝既踏の損耗率は比率×RankRatioScale(2.0)のまま（§0.49）。
            Assert.Equal(2.0, DungeonTraversalBalance.RankRatioScale, precision: 6);
            Assert.Equal(4, DungeonTraversalResolver.CalculateBaseFloors(1.0));
            Assert.Equal(TraversalRank.Normal, DungeonTraversalResolver.ClassifyRatio(1.0)); // 週4階層でも「通常」
        }

        [Fact]
        public void CalculateBaseFloors_InfiniteRatio_IsClampedToMaxFloor()
        {
            // 要求値0（Ratio＝∞扱い）でも整数オーバーフローせず、最深部の階層数で頭打ちになる。
            Assert.Equal(DungeonField.MaxFloor, DungeonTraversalResolver.CalculateBaseFloors(double.MaxValue));
            Assert.Equal(1, DungeonTraversalResolver.CalculateBaseFloors(double.NaN));
        }

        [Theory]
        [InlineData(1, TraversalRank.Struggling)]
        [InlineData(2, TraversalRank.Normal)]
        [InlineData(3, TraversalRank.Swift)]
        [InlineData(4, TraversalRank.Lightning)]
        [InlineData(5, TraversalRank.Gale)]
        [InlineData(6, TraversalRank.Godspeed)]
        [InlineData(40, TraversalRank.Godspeed)]
        public void RankFromFloors_ReverseLooksUpRankName(int floors, TraversalRank expected) =>
            Assert.Equal(expected, DungeonTraversalResolver.RankFromFloors(floors));

        [Theory]
        [InlineData(0.99, TraversalRank.Struggling)]
        [InlineData(1.0, TraversalRank.Normal)]
        [InlineData(1.4, TraversalRank.Normal)]
        [InlineData(2.0, TraversalRank.Lightning)]
        [InlineData(2.5, TraversalRank.Gale)]
        [InlineData(3.0, TraversalRank.Godspeed)]
        [InlineData(8.0, TraversalRank.Godspeed)]
        public void ClassifyRatio_UsesRankStepReverseLookup(double ratio, TraversalRank expected) =>
            Assert.Equal(expected, DungeonTraversalResolver.ClassifyRatio(ratio));

        [Theory]
        [InlineData(TraversalRank.Gale)]
        [InlineData(TraversalRank.Godspeed)]
        public void RankHpLossRange_GaleAndGodspeed_BottomOutAtLightning(TraversalRank rank)
        {
            // 既踏階層の損耗は電撃（5%〜）で底打ち：疾風・神速でもそれ以上は軽くならない。
            Assert.Equal(DungeonTraversalResolver.RankHpLossRange(TraversalRank.Lightning), DungeonTraversalResolver.RankHpLossRange(rank));
            Assert.Equal(5, DungeonTraversalResolver.RankHpLossRange(rank).Min);
        }

        // ---------------- Resolve（進軍・ストッパー・HP消費） ----------------
        // §0.49：1歩の消費＝その階層の要求値（階層×4.5）÷（走破力×4.0）÷区間倍率、1週の予算は1。
        // 走破力45なら1歩の消費は「階層÷40」（未解析）：1〜8Fの8歩で0.90、9Fからの1歩（0.225）は入らない。

        [Fact]
        public void StepCost_GrowsWithDepth_AndShrinksWithIntel()
        {
            var field = MakeField();
            field.Bosses.Add(new FloorBoss { Name = "区間のボス", Floor = 50, MaxHp = 1, IntelRate = 0.0 });

            Assert.Equal(1.0 / 40, DungeonTraversalResolver.StepCost(field, 1, 45), precision: 9);
            Assert.Equal(10.0 / 40, DungeonTraversalResolver.StepCost(field, 10, 45), precision: 9); // 深いほど重い

            field.Bosses[0].IntelRate = 1.0; // 完全解析の区間は1/3
            Assert.Equal(10.0 / 40 / 3, DungeonTraversalResolver.StepCost(field, 10, 45), precision: 9);
            Assert.Equal(double.PositiveInfinity, DungeonTraversalResolver.StepCost(field, 10, 0));
        }

        [Fact]
        public void Resolve_AdvancesFewerFloors_FromDeeperStart()
        {
            // 同じ走破力45でも、1Fからは8階層（1F→9F）、40Fからは1階層（40Fの1歩は消費1.0で予算を使い切る）。
            var member = MakeTraveler(vit: 45, mnd: 0);

            var shallow = new DungeonTraversalResolver(new AlwaysMinRng()).Resolve(PartyOf(member), MakeField(reachedFloor: 100), currentFloor: 1);
            var deep = new DungeonTraversalResolver(new AlwaysMinRng()).Resolve(PartyOf(member), MakeField(reachedFloor: 100), currentFloor: 40);

            Assert.Equal(9, shallow.FloorAfter);
            Assert.Equal(8, shallow.FrontFloor);                       // 最後に踏み出した階層
            Assert.Equal(8 * DungeonTraversalBalance.RequirementPerFloor, shallow.Requirement, precision: 6);
            Assert.Equal(TraversalRank.Normal, shallow.Rank);         // 8Fで比率1.25＝段2
            Assert.Equal(41, deep.FloorAfter);
            Assert.Equal(TraversalRank.Struggling, deep.Rank);         // 40Fで比率0.25
        }

        [Fact]
        public void Resolve_GaleParty_WithLossFlooredAtLightning()
        {
            // 走破力36（VIT36）、4Fの未撃破ボスで止まる：先頭3Fの要求値13.5で比率2.67＝段5（疾風）。
            // 既踏階層だけを進むので損耗は電撃の率（AlwaysMin＝5%）で底打ち。
            var member = MakeTraveler(vit: 36, mnd: 0);
            var field = MakeField(reachedFloor: 100);
            field.Bosses.Add(new FloorBoss { Name = "4Fの主", Floor = 4, MaxHp = 1 });
            var result = new DungeonTraversalResolver(new AlwaysMinRng()).Resolve(PartyOf(member), field, currentFloor: 1);

            Assert.Equal(TraversalRank.Gale, result.Rank);
            Assert.Equal(4, result.FloorAfter);
            Assert.False(result.EnteredUnexplored);
            Assert.Equal((int)Math.Floor(member.MaxHP * DungeonTraversalBalance.HpLossPctMinLightning / 100.0 + 1e-9),
                result.HpLostByAdventurer[member.Id]);
        }

        [Fact]
        public void Resolve_HugeSurplus_StillStopsAtUndefeatedBoss()
        {
            // 基礎進軍は100階層ぶん（Ratio＝∞に近い）余っていても、未撃破の10Fボスで必ず止まり越境しない。
            var member = MakeTraveler(vit: 5000, mnd: 0);
            var field = MakeField(reachedFloor: 1);
            var boss10 = new FloorBoss { Name = "10Fの主", Floor = 10, MaxHp = 1 };
            field.Bosses.Add(boss10);
            field.Bosses.Add(new FloorBoss { Name = "20Fの主", Floor = 20, MaxHp = 1 });

            var result = new DungeonTraversalResolver(new AlwaysMinRng()).Resolve(PartyOf(member), field, currentFloor: 1);

            Assert.True(result.BaseFloors >= 50);
            Assert.Equal(10, result.FloorAfter);
            Assert.True(result.StopperTriggered);
            Assert.Same(boss10, result.TargetBoss);
        }

        [Theory]
        [InlineData(15, 0.0)]    // 未解析
        [InlineData(15, 1.0)]    // 完全解析（1歩が1/3）
        [InlineData(20, 0.5)]    // 解析50%
        [InlineData(5000, 1.0)]  // 予算が大きく余る → 未撃破の10Fボスで停止
        public void PredictFloorAfter_MatchesActualResolve(int vit, double intel10)
        {
            // 出撃前プレビューの到達予測（→ DungeonPanel）が、実際の解決と同じ階層・先頭・ランクを指すこと。
            DungeonField Build()
            {
                var f = MakeField(reachedFloor: 1);
                f.Bosses.Add(new FloorBoss { Name = "10Fの主", Floor = 10, MaxHp = 1, IntelRate = intel10 });
                f.Bosses.Add(new FloorBoss { Name = "20Fの主", Floor = 20, MaxHp = 1 });
                return f;
            }
            var member = MakeTraveler(vit: vit, mnd: 0);
            double score = DungeonTraversalResolver.CalculateTraversalScore(PartyOf(member));

            var plan = DungeonTraversalResolver.PlanWeek(Build(), 1, score);
            var actual = new DungeonTraversalResolver(new AlwaysMinRng()).Resolve(PartyOf(member), Build(), currentFloor: 1);

            Assert.Equal(actual.FloorAfter, DungeonTraversalResolver.PredictFloorAfter(Build(), 1, score));
            Assert.Equal(actual.FloorAfter, plan.FloorAfter);
            Assert.Equal(actual.FrontFloor, plan.FrontFloor);
            Assert.Equal(actual.BaseFloors, plan.BaseFloors);
            Assert.Equal(actual.Rank, plan.Rank);
        }

        [Fact]
        public void PredictWeeksToFloor_CountsWeeksUntilTheBossDoor()
        {
            // 走破力45・未解析：1週目 1F→9F（1〜8Fの8歩で0.90）、2週目 9F→10F で10Fボスの扉前。
            // 完全解析なら1週で着く（1〜9Fの9歩で45/120）。
            DungeonField Build(double intel)
            {
                var f = MakeField(reachedFloor: 1);
                f.Bosses.Add(new FloorBoss { Name = "10Fの主", Floor = 10, MaxHp = 1, IntelRate = intel });
                return f;
            }

            Assert.Equal(2, DungeonTraversalResolver.PredictWeeksToFloor(Build(0.0), 1, 10, 45));
            Assert.Equal(1, DungeonTraversalResolver.PredictWeeksToFloor(Build(1.0), 1, 10, 45));
            // 走破力0でも1週に1階層は進む＝9週。
            Assert.Equal(9, DungeonTraversalResolver.PredictWeeksToFloor(Build(0.0), 1, 10, 0));
        }

        [Fact]
        public void Resolve_StrongParty_AdvancesLightningRank_AndUpdatesReachedFloor()
        {
            // 走破力180（VIT180）：1歩の消費は「階層÷160」。1〜17Fの17歩で0.96を使い、18Fからの1歩は入らない。先頭17Fで比率2.35＝電撃。
            var party = PartyOf(MakeTraveler(vit: 180, mnd: 0));
            var field = MakeField(reachedFloor: 1);
            var nextBoss = new FloorBoss { Name = "遠くのボス", Floor = 100, MaxHp = 1 };
            var resolver = new DungeonTraversalResolver(new AlwaysMinRng());

            var result = resolver.Resolve(party, field, nextBoss);

            Assert.Equal(TraversalRank.Lightning, result.Rank);
            Assert.Equal(1, result.FloorBefore);
            Assert.Equal(18, result.FloorAfter);
            Assert.Equal(18, field.ReachedFloor);
            Assert.False(result.StopperTriggered);
            Assert.Null(result.TargetBoss);
        }

        [Fact]
        public void Resolve_WeakParty_StillAdvancesAtLeastOneFloor_StrugglingRank()
        {
            var party = PartyOf(MakeSpecialist(agiDex: 1));
            var field = MakeField(reachedFloor: 50);
            var nextBoss = new FloorBoss { Name = "遠くのボス", Floor = 100, MaxHp = 1 };
            var resolver = new DungeonTraversalResolver(new AlwaysMinRng());

            var result = resolver.Resolve(party, field, nextBoss);

            Assert.Equal(TraversalRank.Struggling, result.Rank);
            Assert.Equal(51, result.FloorAfter); // 走破力38で50Fの1歩の消費は1.48と予算を超えるが、必ず+1階層
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
            // 走破力180：1Fから17階層進み、先頭17Fで比率2.35＝電撃（→ Resolve_StrongParty_AdvancesLightningRank_AndUpdatesReachedFloor）。
            var strongParty = PartyOf(MakeTraveler(vit: 180, mnd: 0));
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
            // 同じ走破力36でも、区間担当ボスが完全解析済みなら1歩が1/3になり、未調査の7階層（1F→8F：1〜7Fで28/32）に対して
            // 13階層（1F→14F：1〜13Fの13歩で91/96）進み、HP損耗は70%軽減される。比較用に未調査の同条件も解決する。
            var fullMember = MakeTraveler(vit: 36, mnd: 0);
            var noneMember = MakeTraveler(vit: 36, mnd: 0);
            var fullField = MakeField();
            fullField.Bosses.Add(new FloorBoss { Name = "解析済みのボス", Floor = 50, MaxHp = 1, IntelRate = 1.0 });
            var noneField = MakeField();
            noneField.Bosses.Add(new FloorBoss { Name = "未調査のボス", Floor = 50, MaxHp = 1, IntelRate = 0.0 });

            var full = new DungeonTraversalResolver(new AlwaysMaxRng()).Resolve(PartyOf(fullMember), fullField, currentFloor: 1);
            var none = new DungeonTraversalResolver(new AlwaysMaxRng()).Resolve(PartyOf(noneMember), noneField, currentFloor: 1);

            Assert.Equal(7, none.FloorAfter - none.FloorBefore);
            Assert.Equal(13, full.FloorAfter - full.FloorBefore);
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

        /// <summary>MakeSturdySpecialist（走破力180）の出発階層。1歩の消費は「階層÷160」（未解析）。</summary>
        private const int LightningStartFloor = 20;

        /// <summary>
        /// 20Fから未解析で進める階層数：20〜25Fの6歩で135/160、26Fからの1歩（26/160）は入らない。
        /// 解析50%（1歩が1/2）なら12階層（20〜31Fで306/320）、完全解析（1/3）なら17階層（20〜36Fで476/480）。
        /// </summary>
        private const int LightningBaseFloors = 6;
        private const int HalfIntelFloors = 12;
        private const int FullIntelFloors = 17;

        private static Adventurer MakeSturdySpecialist()
        {
            // MaxHPを大きくして、%消費の整数丸めの影響を小さくする。走破力＝VIT180（MND・LDRは0）。
            var a = new Adventurer { STR = 10, AGI = 500, VIT = 180, MND = 0, DEX = 500, LDR = 0, INT = 10 };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Traversal_IntelRate_Zero_AppliesFullUnexploredDamage(bool maxRoll)
        {
            // 解析率0%：走破倍率1.0倍（20Fから6階層）、未踏破の重損耗30〜50%を軽減なしで受ける。
            var member = MakeSturdySpecialist();
            IRng rng = maxRoll ? new AlwaysMaxRng() : new AlwaysMinRng();

            var result = new DungeonTraversalResolver(rng).Resolve(PartyOf(member), MakeUnexploredField(0.0), currentFloor: LightningStartFloor);

            int pct = maxRoll ? DungeonBalance.UnexploredHpLossPctMax : DungeonBalance.UnexploredHpLossPctMin;
            Assert.Equal(30, DungeonBalance.UnexploredHpLossPctMin);
            Assert.Equal(50, DungeonBalance.UnexploredHpLossPctMax);
            Assert.True(result.EnteredUnexplored);
            Assert.Equal(1.0, result.IntelSpeedMultiplier, precision: 6);
            Assert.Equal(1.0, result.DamageTakenMultiplier, precision: 6);
            Assert.Equal(LightningBaseFloors, result.FloorAfter - result.FloorBefore);
            Assert.Equal(member.MaxHP * pct / 100, result.HpLostByAdventurer[member.Id]);
        }

        [Fact]
        public void Traversal_IntelRate_Complete_AppliesMaxBoostAndReduction()
        {
            // 解析率100%：1歩が1/3になり進む階層数も増える（6→17）、未踏破の重損耗も70%カット（50%→15%）。
            var member = MakeSturdySpecialist();

            var result = new DungeonTraversalResolver(new AlwaysMaxRng()).Resolve(PartyOf(member), MakeUnexploredField(1.0), currentFloor: LightningStartFloor);

            Assert.True(result.EnteredUnexplored);
            Assert.Equal(3.0, result.IntelSpeedMultiplier, precision: 6);
            Assert.Equal(FullIntelFloors, result.FloorAfter - result.FloorBefore);
            Assert.Equal(0.3, result.DamageTakenMultiplier, precision: 6);
            int baseLoss = member.MaxHP * DungeonBalance.UnexploredHpLossPctMax / 100;
            Assert.Equal((int)(baseLoss * 0.3), result.HpLostByAdventurer[member.Id]);
        }

        [Fact]
        public void Traversal_IntelRate_Partial_AppliesLinearInterpolation()
        {
            // 解析率50%：走破倍率は線形補間で2.0倍（1歩が1/2、6→12階層）。被ダメージ軽減は完全解析区間のみの
            // 仕組みを正本として維持するため、50%時点では軽減なし（未踏破の重損耗をそのまま受ける）。
            var member = MakeSturdySpecialist();

            var result = new DungeonTraversalResolver(new AlwaysMaxRng()).Resolve(PartyOf(member), MakeUnexploredField(0.5), currentFloor: LightningStartFloor);

            Assert.Equal(2.0, result.IntelSpeedMultiplier, precision: 6);
            Assert.Equal(HalfIntelFloors, result.FloorAfter - result.FloorBefore);
            Assert.Equal(1.0, result.DamageTakenMultiplier, precision: 6);
            Assert.Equal(member.MaxHP * DungeonBalance.UnexploredHpLossPctMax / 100, result.HpLostByAdventurer[member.Id]);
        }

        [Fact]
        public void Traversal_Unexplored_NeverKills_ButFallingToOneHp_InflictsLightInjury()
        {
            // 重損耗でもHP下限1で止まり、除籍には接続しない。HPが1まで落ちた隊員は軽傷になる（§0.53）。
            var member = MakeSturdySpecialist();
            member.CurrentHP = 2;

            new DungeonTraversalResolver(new AlwaysMaxRng()).Resolve(PartyOf(member), MakeUnexploredField(0.0), currentFloor: 1);

            Assert.Equal(1, member.CurrentHP);
            Assert.Equal(InjurySeverity.Light, member.Injury);
            Assert.Equal(CombatBalance.LightInjuryWeeksMax, member.InjuryWeeksRemaining); // AlwaysMax
            Assert.True(member.IsAvailable); // 軽傷は出撃できる
            Assert.False(member.IsRetired);
        }

        // ---------------- 区間またぎ：階層ごとの損耗積み上げ（2026年9月改訂） ----------------

        /// <summary>
        /// 10Fボス（完全解析・撃破済み）＋20Fボス（解析0%）＋30Fボスのフィールド。
        /// 最高到達階層は10F（11F以降が未踏破）。
        /// </summary>
        private static DungeonField MakeCrossingField(out FloorBoss boss10, out FloorBoss boss20)
        {
            var field = MakeField(reachedFloor: 10);
            boss10 = new FloorBoss { Name = "10Fの主", Floor = 10, MaxHp = 1, IntelRate = 1.0, IsDefeated = true };
            boss20 = new FloorBoss { Name = "20Fの主", Floor = 20, MaxHp = 1, IntelRate = 0.0 };
            field.Bosses.Add(boss10);
            field.Bosses.Add(boss20);
            field.Bosses.Add(new FloorBoss { Name = "30Fの主", Floor = 30, MaxHp = 1, IntelRate = 0.0 });
            return field;
        }

        [Fact]
        public void Traversal_CrossingIntoUnanalyzedSegment_SeparatesDamagePerFloor()
        {
            // 走破力54で7Fから：1歩の消費＝階層×4.5÷216÷区間倍率。7〜9Fは完全解析区間（計24/144）、10〜12Fは未解析（計33/48）で、
            // 合計0.85。13Fからの1歩（13/48）は入らない＝7F→13F。先頭12Fで比率1.0（要求値54）＝通常（段2）。
            // 8〜10Fは既踏（通常の率）×0.3、11〜13Fは未踏破（重損耗の率）×1.0 で階層ごとに積み上げる。
            var member = MakeTraveler(vit: 54, mnd: 0);
            var field = MakeCrossingField(out var boss10, out var boss20);

            var result = new DungeonTraversalResolver(new AlwaysMaxRng()).Resolve(PartyOf(member), field, currentFloor: 7);

            Assert.Equal(TraversalRank.Normal, result.Rank);
            Assert.Equal(13, result.FloorAfter);
            Assert.True(result.EnteredUnexplored);

            Assert.Equal(2, result.Segments.Count);
            var analyzed = result.Segments[0];
            Assert.Same(boss10, analyzed.Boss);
            Assert.Equal((7, 10, 3), (analyzed.FromFloor, analyzed.ToFloor, analyzed.Floors));
            Assert.False(analyzed.IsUnexplored);
            Assert.Equal(DungeonTraversalBalance.FullIntelDamageMultiplier, analyzed.DamageMultiplier, precision: 6);
            Assert.Equal(DungeonTraversalBalance.HpLossPctMaxNormal, analyzed.BaseLossPct, precision: 6);

            var unanalyzed = result.Segments[1];
            Assert.Same(boss20, unanalyzed.Boss);
            Assert.Equal((10, 13, 3), (unanalyzed.FromFloor, unanalyzed.ToFloor, unanalyzed.Floors));
            Assert.True(unanalyzed.IsUnexplored);
            Assert.Equal(1.0, unanalyzed.DamageMultiplier, precision: 6);
            Assert.Equal(DungeonBalance.UnexploredHpLossPctMax, unanalyzed.BaseLossPct, precision: 6);

            // 実効損耗率＝(3階層×通常16%×0.3 ＋ 3階層×未踏破50%×1.0)÷6階層。
            double expectedPct = (3 * DungeonTraversalBalance.HpLossPctMaxNormal * DungeonTraversalBalance.FullIntelDamageMultiplier
                + 3 * DungeonBalance.UnexploredHpLossPctMax * 1.0) / 6;
            Assert.Equal(expectedPct, result.EffectiveLossPct, precision: 6);
            Assert.Equal((int)Math.Floor(member.MaxHP * expectedPct / 100.0 + 1e-9), result.HpLostByAdventurer[member.Id]);

            // 旧方式（未踏破の基礎率50%×平均倍率0.65＝32.5%）より軽くも重くもなく、区間ごとに分離されている：
            // 11〜13Fの重損耗は1.0倍のまま（完全解析区間の0.3倍が未解析区間へ漏れない）。
            Assert.NotEqual(DungeonBalance.UnexploredHpLossPctMax * 0.65, result.EffectiveLossPct, precision: 3);
        }

        [Fact]
        public void Traversal_CrossingIntoUnanalyzedSegment_RecordsWholeTripAverageSpeed()
        {
            // 実効平均速度は出発区間（×3.0）ではなく、歩いた6階層の倍率（3,3,3,1,1,1）の平均＝×2.0。
            var field = MakeCrossingField(out _, out _);

            var result = new DungeonTraversalResolver(new AlwaysMinRng())
                .Resolve(PartyOf(MakeTraveler(vit: 54, mnd: 0)), field, currentFloor: 7);

            Assert.Equal(2.0, result.IntelSpeedMultiplier, precision: 6);
            Assert.Equal((3 * DungeonTraversalBalance.FullIntelDamageMultiplier + 3 * 1.0) / 6, result.DamageTakenMultiplier, precision: 6);
        }

        [Fact]
        public void Traversal_HpLoss_UsesFloatingPoint_WithoutIntermediateTruncation()
        {
            // 完全解析区間だけの未踏破進軍：失うHP＝floor(最大HP×30%×0.3)。旧式の
            // (int)((最大HP×30/100)×0.3) では途中の切り捨てで1少なくなる場合がある。
            // その差が出る最大HPになるVITを探して検証する（CSVの係数が変わってもテストが成立するように）。
            int pct = DungeonBalance.UnexploredHpLossPctMin;
            double mult = DungeonTraversalBalance.FullIntelDamageMultiplier;
            Adventurer? member = null;
            for (int vit = 100; vit <= 400; vit++)
            {
                var candidate = MakeTraveler(vit: vit, mnd: 100, ldr: 100);
                int legacy = (int)(candidate.MaxHP * pct / 100 * mult);
                int precise = (int)Math.Floor(candidate.MaxHP * pct / 100.0 * mult + 1e-9);
                if (legacy != precise) { member = candidate; break; }
            }
            Assert.NotNull(member);

            var result = new DungeonTraversalResolver(new AlwaysMinRng()).Resolve(PartyOf(member!), MakeUnexploredField(1.0), currentFloor: 1);

            Assert.Equal((int)Math.Floor(member!.MaxHP * pct / 100.0 * mult + 1e-9), result.HpLostByAdventurer[member.Id]);
            Assert.Equal(pct * mult, result.EffectiveLossPctByAdventurer[member.Id], precision: 6);
        }

        [Fact]
        public void DescribeSegments_SplitsAtBossBoundary_ForPreview()
        {
            var field = MakeCrossingField(out var boss10, out var boss20);

            var segments = DungeonTraversalResolver.DescribeSegments(field, fromFloor: 1, toFloor: 20, exploredRecord: 10);

            Assert.Equal(2, segments.Count);
            Assert.Same(boss10, segments[0].Boss);
            Assert.Equal((1, 10, 9, 3.0), (segments[0].FromFloor, segments[0].ToFloor, segments[0].Floors, segments[0].SpeedMultiplier));
            Assert.Same(boss20, segments[1].Boss);
            Assert.Equal((10, 20, 10, 1.0), (segments[1].FromFloor, segments[1].ToFloor, segments[1].Floors, segments[1].SpeedMultiplier));
            Assert.True(segments[1].IsUnexplored);
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
            var party = PartyOf(MakeTraveler(vit: 40, mnd: 20, ldr: 20)); // (40+16) ＋ LDR20×1.0 ＝ 76
            double withoutState = DungeonTraversalResolver.CalculateTraversalScore(party);

            double withAdvisor = DungeonTraversalResolver.CalculateTraversalScore(party, StateWithAdvisor(statAverage: 50));

            Assert.Equal(76, withoutState, precision: 6);
            Assert.Equal(76 + 10, withAdvisor, precision: 6);
            Assert.Equal(0.2, AdvisorBalance.TraversalPowerBonusCoefficient, precision: 6);
        }

        [Fact]
        public void CalculateTraversalScore_NoAdvisorBonus_WhenUnassigned()
        {
            var party = PartyOf(MakeTraveler(vit: 40, mnd: 20, ldr: 20));

            double withoutAdvisor = DungeonTraversalResolver.CalculateTraversalScore(party, new GameState());

            Assert.Equal(76, withoutAdvisor, precision: 6);
        }

        [Fact]
        public void Resolve_AdvisorBonus_RaisesEffectiveTraversalPower_AndIsReported()
        {
            // 2Fから出発。部隊だけなら走破力12で、2〜4Fの3歩（1歩＝階層×4.5÷48：0.19＋0.28＋0.38）の後の5Fの1歩（0.47）が入らず3階層、
            // 参謀の+10で走破力22になると、2〜5Fの4歩（0.10＋0.15＋0.20＋0.26）が入り4階層進む（→ StepCost、§0.49）。
            var field = MakeField(reachedFloor: 1);
            field.Bosses.Add(new FloorBoss { Name = "遠くのボス", Floor = 50, MaxHp = 1 });
            var resolver = new DungeonTraversalResolver(new AlwaysMinRng());

            var withoutAdvisor = resolver.Resolve(PartyOf(MakeTraveler(vit: 8, mnd: 5)), MakeField(reachedFloor: 1), currentFloor: 2);
            var withAdvisor = resolver.Resolve(PartyOf(MakeTraveler(vit: 8, mnd: 5)), field, currentFloor: 2, state: StateWithAdvisor(statAverage: 50));

            Assert.Equal(TraversalRank.Struggling, withoutAdvisor.Rank);
            Assert.Equal(0, withoutAdvisor.AdvisorTraversalBonus, precision: 6);
            Assert.Null(withoutAdvisor.AdvisorName);

            Assert.Equal(3, withoutAdvisor.FloorAfter - withoutAdvisor.FloorBefore);
            Assert.Equal(4, withAdvisor.FloorAfter - withAdvisor.FloorBefore);
            Assert.Equal(10, withAdvisor.AdvisorTraversalBonus, precision: 6);
            Assert.Equal("参謀ガレス", withAdvisor.AdvisorName);
            Assert.True(withAdvisor.FloorAfter - withAdvisor.FloorBefore > withoutAdvisor.FloorAfter - withoutAdvisor.FloorBefore,
                "参謀のルート指導で、同じ部隊でもより深くまで進めるはず");
        }
    }
}
