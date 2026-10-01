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
    /// 2026年10月・§0.57のテスト：レア特性8種（天才・大魔導士・剣聖・聖人・カリスマ・不滅の肉体・韋駄天・百発百中）の
    /// 効果・採用時の出方（1人あたり1%、天才は他の半分の重み）・伝授されないこと。
    /// 実行方法: `dotnet test GuildManager.Core.Tests --filter FullyQualifiedName~RareTraits`
    /// </summary>
    public class RareTraitsTests
    {
        /// <summary>決まった順に値を返す乱数（尽きたら最後の値を繰り返す）。範囲外は [min, max] に収める。</summary>
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

        private class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        private static readonly (string Id, string Name, string Stat)[] SingleStat =
        {
            (TraitCatalog.ArchmageId, "大魔導士", "INT"),
            (TraitCatalog.SwordSaintId, "剣聖", "STR"),
            (TraitCatalog.SaintId, "聖人", "MND"),
            (TraitCatalog.CharismaId, "カリスマ", "LDR"),
            (TraitCatalog.ImmortalBodyId, "不滅の肉体", "VIT"),
            (TraitCatalog.IdatenId, "韋駄天", "AGI"),
            (TraitCatalog.MarksmanId, "百発百中", "DEX"),
        };

        private static readonly string[] AllStats = { "STR", "VIT", "AGI", "DEX", "INT", "MND", "LDR" };

        private static Adventurer Make(int stat, params string[] traits)
        {
            var a = new Adventurer { STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat };
            foreach (var id in traits) Assert.True(a.TryAddTrait(id), id);
            return a;
        }

        [Fact]
        public void CsvValues_AreLoaded()
        {
            Assert.Equal(0.15, TraitBalance.GeniusStatBonus, precision: 6);
            Assert.Equal(0.30, TraitBalance.RareSingleStatBonus, precision: 6);
            Assert.Equal(1, RecruitmentBalance.RareTraitChancePercent);
            Assert.Equal(1, RecruitmentBalance.RareTraitWeightGenius);
            Assert.Equal(2, RecruitmentBalance.RareTraitWeightSingleStat);
        }

        [Fact]
        public void RareTraits_AreDefined_NotTransmittable()
        {
            Assert.Equal(8, RecruitmentSystem.RareTraitPool.Length);
            foreach (var id in RecruitmentSystem.RareTraitPool)
            {
                var def = TraitCatalog.FindById(id)!;
                Assert.True(def.IsRare, id);
                Assert.False(def.IsTransmittable, id);
                Assert.False(def.IsCurseOrInjury, id);
                Assert.False(def.IsFlaw, id);
                Assert.DoesNotContain(id, RecruitmentSystem.InnateTraitPool);
            }
            Assert.Equal("天才", TraitCatalog.Genius.DisplayName);
            Assert.Equal(SingleStat.Select(s => s.Name), SingleStat.Select(s => TraitCatalog.FindById(s.Id)!.DisplayName));
        }

        [Fact]
        public void Genius_RaisesAllSevenStats()
        {
            var a = Make(40, TraitCatalog.GeniusId);
            foreach (var stat in AllStats)
                Assert.Equal(46, a.GetEffectiveStat(stat), precision: 6);
        }

        [Fact]
        public void SingleStatRares_RaiseOnlyTheirStat()
        {
            foreach (var (id, _, target) in SingleStat)
            {
                var a = Make(40, id);
                foreach (var stat in AllStats)
                    Assert.Equal(stat == target ? 52 : 40, a.GetEffectiveStat(stat), precision: 6);
            }
        }

        [Fact]
        public void RareBonus_StacksWithOtherPercentTraits()
        {
            // 大魔導士（+30%）と火の魔術師（+10%）は割合で足し合わせる：40×1.40。
            var a = Make(40, TraitCatalog.ArchmageId, TraitCatalog.FireMageId);
            Assert.Equal(56, a.GetEffectiveStat("INT"), precision: 6);
        }

        [Fact]
        public void ImmortalBody_RaisesMaxHp()
        {
            Assert.True(Make(40, TraitCatalog.ImmortalBodyId).MaxHP > Make(40).MaxHP);
        }

        [Fact]
        public void RareTraits_AreNotTransmitted()
        {
            var trainer = Make(40, TraitCatalog.GeniusId, TraitCatalog.SwordSaintId, TraitCatalog.BraveId);
            Assert.Equal(new[] { TraitCatalog.BraveId }, TrainingSystem.GetTransmittableTraitIds(trainer));
        }

        [Fact]
        public void RareTraits_CanBeForgotten()
        {
            var a = Make(40, TraitCatalog.GeniusId);
            Assert.True(a.TryRemoveTrait(TraitCatalog.GeniusId));
        }

        // ==================== 採用時の出方 ====================

        [Fact]
        public void Recruitment_NoRareTrait_WhenRollMisses()
        {
            // 最初に引く乱数（レア特性の判定）が2以上なら付かない。以降は全判定外れ（100）。
            var offers = new RecruitmentSystem(new AlwaysMaxRng()).GenerateCandidates(new GameState(), candidateCount: 3);
            Assert.All(offers, o => Assert.DoesNotContain(o.Candidate.TraitIds, id => TraitCatalog.FindById(id)!.IsRare));
        }

        [Theory]
        // 重み：天才1・他2ずつ（合計15）。1→天才、2〜3→大魔導士、4〜5→剣聖 … 14〜15→百発百中。
        [InlineData(1, TraitCatalog.GeniusId)]
        [InlineData(2, TraitCatalog.ArchmageId)]
        [InlineData(3, TraitCatalog.ArchmageId)]
        [InlineData(4, TraitCatalog.SwordSaintId)]
        [InlineData(15, TraitCatalog.MarksmanId)]
        public void PickRareTrait_UsesWeights(int weightRoll, string expected)
        {
            Assert.Equal(15, RecruitmentSystem.RareTraitPool.Sum(RecruitmentSystem.RareTraitWeight));

            var method = typeof(RecruitmentSystem).GetMethod("PickRareTrait",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

            Assert.Equal(expected, method.Invoke(null, new object[] { new SequenceRng(weightRoll) }));
        }

        [Fact]
        public void Recruitment_GrantsRareTrait_WhenRollHits()
        {
            // 全判定が当たる乱数（NextInt→min）：レア特性の判定（1≦1%）が当たり、重みの抽選も1＝天才。
            var offers = new RecruitmentSystem(new SequenceRng(1)).GenerateCandidates(new GameState(), candidateCount: 1);
            Assert.Equal(TraitCatalog.GeniusId, offers[0].Candidate.TraitIds[0]);
        }
    }
}
