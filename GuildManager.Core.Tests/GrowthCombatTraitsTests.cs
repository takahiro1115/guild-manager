using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 2026年10月・§0.62のテスト（クリアへのバランス 第3段）：成長・素質系（大器晩成・早熟・魂魄の申し子）と
    /// 戦闘系（歴戦の勇士・鼓舞）の特性。
    /// 実行方法: `dotnet test GuildManager.Core.Tests --filter FullyQualifiedName~GrowthCombatTraits`
    /// </summary>
    public class GrowthCombatTraitsTests
    {
        private class FixedRng : IRng
        {
            private readonly int _value;
            public FixedRng(int value) { _value = value; }
            public int NextInt(int min, int max) => Math.Clamp(_value, min, max);
        }

        private class SequenceRng : IRng
        {
            private readonly Queue<int> _values;
            private int _last;
            public SequenceRng(params int[] values) { _values = new Queue<int>(values); _last = values.LastOrDefault(); }
            public int NextInt(int min, int max)
            {
                if (_values.Count > 0) _last = _values.Dequeue();
                return Math.Clamp(_last, min, max);
            }
        }

        private static Adventurer Make(int age, params string[] traits)
        {
            var a = new Adventurer
            {
                Age = age, JobClass = JobClass.Warrior,
                STR = 40, AGI = 40, VIT = 40, MND = 40, DEX = 40, LDR = 40, INT = 40,
                PA_STR = 90, PA_AGI = 90, PA_VIT = 90, PA_MND = 90, PA_DEX = 90, PA_LDR = 90, PA_INT = 90,
            };
            foreach (var id in traits) Assert.True(a.TryAddTrait(id), id);
            a.CurrentHP = a.MaxHP;
            return a;
        }

        [Fact]
        public void CsvValues_AndCatalog()
        {
            Assert.Equal(1.6, TraitBalance.LateBloomerPeakGrowthMultiplier, precision: 6);
            Assert.Equal(1.3, TraitBalance.EarlyBloomerYouthGrowthMultiplier, precision: 6);
            Assert.Equal(0.05, TraitBalance.SoulChildStatBonus, precision: 6);
            Assert.Equal(1.15, TraitBalance.SoulChildGrowthMultiplier, precision: 6);
            Assert.Equal(0.10, TraitBalance.VeteranPowerBonus, precision: 6);
            Assert.Equal(0.04, TraitBalance.InspiringPartyPowerBonus, precision: 6);
            Assert.Equal(50, SoulFusionBalance.SoulChildChancePercent);

            Assert.Equal("大器晩成", TraitCatalog.LateBloomer.DisplayName);
            Assert.Equal("魂魄の申し子", TraitCatalog.SoulChild.DisplayName);
            foreach (var id in new[] { TraitCatalog.LateBloomerId, TraitCatalog.EarlyBloomerId, TraitCatalog.VeteranId, TraitCatalog.InspiringId })
            {
                Assert.Contains(id, RecruitmentSystem.InnateTraitPool);
                Assert.True(TraitCatalog.FindById(id)!.IsTransmittable, id);
            }
            Assert.DoesNotContain(TraitCatalog.SoulChildId, RecruitmentSystem.InnateTraitPool);
            Assert.False(TraitCatalog.SoulChild.IsTransmittable);
        }

        // ---------------- 成長・素質系 ----------------

        [Theory]
        [InlineData(20, 1.0)]  // 大器晩成は全盛期だけ
        [InlineData(24, 1.6)]
        public void LateBloomer_GrowsInPeak(int age, double expected)
        {
            Assert.Equal(expected, GrowthSystem.TraitGrowthMultiplier(Make(age, TraitCatalog.LateBloomerId)), precision: 6);
        }

        [Theory]
        [InlineData(18, 1.3)]
        [InlineData(22, 1.3)]
        [InlineData(23, 1.0)]  // 早熟は22歳まで
        public void EarlyBloomer_GrowsWhileYoung(int age, double expected)
        {
            Assert.Equal(expected, GrowthSystem.TraitGrowthMultiplier(Make(age, TraitCatalog.EarlyBloomerId)), precision: 6);
        }

        [Fact]
        public void SoulChild_AlwaysGrowsFaster_AndStacks()
        {
            Assert.Equal(1.15, GrowthSystem.TraitGrowthMultiplier(Make(25, TraitCatalog.SoulChildId)), precision: 6);
            Assert.Equal(1.6 * 1.15, GrowthSystem.TraitGrowthMultiplier(Make(25, TraitCatalog.SoulChildId, TraitCatalog.LateBloomerId)), precision: 6);
            Assert.Equal(1.0, GrowthSystem.TraitGrowthMultiplier(Make(25)), precision: 6);
        }

        [Fact]
        public void SoulChild_RaisesAllStatsSlightly()
        {
            var a = Make(20, TraitCatalog.SoulChildId);
            Assert.Equal(42.0, a.GetEffectiveStat("STR"), precision: 6); // 40×1.05
            Assert.Equal(42.0, a.GetEffectiveStat("LDR"), precision: 6);
        }

        [Fact]
        public void LateBloomer_AppliesToTrainingAndExpedition()
        {
            // 全盛期の訓練：基礎0.15。大器晩成で×1.6＝24%。乱数20は無しでは外れ、有りでは当たる。
            Assert.Empty(Train(Make(24)));
            Assert.NotEmpty(Train(Make(24, TraitCatalog.LateBloomerId)));

            // 出撃：基礎42%。×1.6＝67%。乱数60で分かれる。
            Assert.Empty(Expedition(Make(24), 60));
            Assert.NotEmpty(Expedition(Make(24, TraitCatalog.LateBloomerId), 60));
        }

        private static List<GrowthEvent> Train(Adventurer a)
        {
            var state = new GameState { Adventurers = { a } };
            state.TrainingAssignments[a.Id] = FacilityType.DrillHall;
            return new GrowthSystem(new FixedRng(20)).ProcessTrainingGrowth(state, new HashSet<Guid>());
        }

        private static List<GrowthEvent> Expedition(Adventurer a, int roll)
        {
            var state = new GameState { Adventurers = { a } };
            var party = new Party();
            party.TryAdd(a);
            return new GrowthSystem(new FixedRng(roll)).ApplyExpeditionGrowth(state, party, DungeonMissionType.Scouting, isBossVictory: false);
        }

        // ---------------- 戦闘系 ----------------

        [Fact]
        public void Veteran_RaisesOwnPower()
        {
            double plain = DungeonPowerCalculator.MemberPower(Make(24));
            double veteran = DungeonPowerCalculator.MemberPower(Make(24, TraitCatalog.VeteranId));
            Assert.Equal(plain * 1.10, veteran, precision: 6);
        }

        [Fact]
        public void Inspiring_RaisesPartyPower_OnceEvenIfSeveral()
        {
            var plain = new[] { Make(24), Make(24), Make(24) };
            double basePower = DungeonPowerCalculator.PartyPower(plain);

            var one = new[] { Make(24, TraitCatalog.InspiringId), Make(24), Make(24) };
            var two = new[] { Make(24, TraitCatalog.InspiringId), Make(24, TraitCatalog.InspiringId), Make(24) };
            Assert.Equal(basePower * 1.04, DungeonPowerCalculator.PartyPower(one), precision: 6);
            Assert.Equal(basePower * 1.04, DungeonPowerCalculator.PartyPower(two), precision: 6);
        }

        // ---------------- 魂魄の申し子（秘薬の娘） ----------------

        [Fact]
        public void SoulChild_IsRolledAtBirth_NotInherited()
        {
            var a = Make(24, TraitCatalog.SoulChildId);
            var b = Make(24);
            var state = new GameState { Adventurers = { a, b } };
            Assert.Equal(0, SoulFusionSystem.InheritChance(TraitCatalog.SoulChildId, null));

            // 文化圏・名前 → ばらつき×7 → 突破100（外れ）→ 継承（申し子は判定しない）→ 先天（外れ）→ 申し子の判定
            int innateRolls = 1 + RecruitmentSystem.InnateTraitPool.Length + RecruitmentSystem.InnateFlawPool.Length;
            var luckyRolls = new[] { 100, 0, 0, 0, 0, 0, 0, 0, 0, 100 }.Concat(Enumerable.Repeat(100, innateRolls)).Append(50).Append(100).ToArray();
            var lucky = new SoulFusionSystem(new SequenceRng(luckyRolls)).CreateChild(state, a, b, JobClass.Warrior, null).Child;
            Assert.Contains(TraitCatalog.SoulChildId, lucky.TraitIds);

            var unlucky = new SoulFusionSystem(new SequenceRng(100, 0, 0, 0, 0, 0, 0, 0, 0, 100, 100))
                .CreateChild(state, a, b, JobClass.Warrior, null).Child;
            Assert.DoesNotContain(TraitCatalog.SoulChildId, unlucky.TraitIds);
        }
    }
}
