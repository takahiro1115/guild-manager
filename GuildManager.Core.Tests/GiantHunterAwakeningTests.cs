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
    /// 教官深化 Step 2（2026年9月、→ 03 §0.35）のテスト：重装甲ボス撃破時の巨獣狩りの後天開眼と、
    /// 施設画面の「伝授可能」表示が使う教官の伝授可能特性の抽出（→ TrainingSystem.GetTransmittableTraitIds）。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~GiantHunterAwakening`
    /// </summary>
    public class GiantHunterAwakeningTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private class FixedRng : IRng
        {
            private readonly int _value;
            public FixedRng(int value) => _value = value;
            public int NextInt(int min, int max) => Math.Clamp(_value, min, max);
        }

        private static Adventurer Strong(string name)
        {
            var a = new Adventurer { Name = name, JobClass = JobClass.Warrior, STR = 100, VIT = 100, AGI = 100, DEX = 100, INT = 100, MND = 100, LDR = 100 };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            return party;
        }

        /// <summary>1Fのボス（Strong 1人で撃破できる）。heavyArmor なら「重装甲」ギミック（戦士で対策成立）を持たせる。</summary>
        private static FloorBoss Boss(bool heavyArmor, int floor = 1)
        {
            var boss = new FloorBoss { Name = "甲殻の大猪", Floor = floor, MaxHp = 500, CurrentHp = 500 };
            if (heavyArmor)
                boss.Gimmicks.Add(new BossGimmick { Type = BossGimmickType.HeavyArmor, DangerLevel = 1, RequiredCounterRole = JobClass.Warrior });
            return boss;
        }

        [Fact]
        public void CsvValue_IsLoaded()
        {
            Assert.Equal(0.20, CombatBalance.GiantHunterAwakeningChance, precision: 6);
        }

        [Fact]
        public void HeavyArmorVictory_AwakensSurvivors()
        {
            var a = Strong("クラウディア");
            var b = Strong("ミレイ");

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(a, b), Boss(heavyArmor: true));

            Assert.Equal(DungeonOutcome.Victory, result.Outcome);
            Assert.True(a.HasTrait(TraitCatalog.GiantHunterId));
            Assert.True(b.HasTrait(TraitCatalog.GiantHunterId));
            Assert.Equal(2, result.TraitGrantEvents.Count(e => e.Cause == TraitGrantCause.Awakening));
            Assert.All(result.TraitGrantEvents, e => Assert.Null(e.ErodedTraitId));
        }

        [Theory]
        [InlineData(20, true)] // 20%：閾値20
        [InlineData(21, false)]
        public void AwakeningChance_IsTwentyPercent(int roll, bool expectAwaken)
        {
            var a = Strong("クラウディア");

            var result = new DungeonResolver(new FixedRng(roll)).Resolve(PartyOf(a), Boss(heavyArmor: true));

            Assert.Equal(DungeonOutcome.Victory, result.Outcome);
            Assert.Equal(expectAwaken, a.HasTrait(TraitCatalog.GiantHunterId));
        }

        [Fact]
        public void NoAwakening_AgainstBossWithoutHeavyArmor()
        {
            var a = Strong("クラウディア");

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(a), Boss(heavyArmor: false));

            Assert.Equal(DungeonOutcome.Victory, result.Outcome);
            Assert.False(a.HasTrait(TraitCatalog.GiantHunterId));
            Assert.Empty(result.TraitGrantEvents);
        }

        [Fact]
        public void NoAwakening_OnRetreat()
        {
            var a = Strong("クラウディア");

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(a), Boss(heavyArmor: true, floor: 100));

            Assert.Equal(DungeonOutcome.Retreat, result.Outcome);
            Assert.False(a.HasTrait(TraitCatalog.GiantHunterId));
            Assert.DoesNotContain(result.TraitGrantEvents, e => e.Cause == TraitGrantCause.Awakening);
        }

        [Fact]
        public void NoDoubleGrant_ForExistingHolder()
        {
            var a = Strong("クラウディア");
            a.TryAddTrait(TraitCatalog.GiantHunterId);

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(a), Boss(heavyArmor: true));

            Assert.Single(a.TraitIds, TraitCatalog.GiantHunterId);
            Assert.Empty(result.TraitGrantEvents);
        }

        [Fact]
        public void FullSlots_AreSkipped_WithoutErosion()
        {
            var a = Strong("クラウディア");
            foreach (var id in new[] { TraitCatalog.BraveId, TraitCatalog.AttentiveId, TraitCatalog.CountryBredId,
                                       TraitCatalog.MentorId, TraitCatalog.DiligentId })
                a.TryAddTrait(id);
            var before = a.TraitIds.ToList();

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(a), Boss(heavyArmor: true));

            Assert.Equal(DungeonOutcome.Victory, result.Outcome);
            Assert.Equal(before, a.TraitIds);
            Assert.Empty(result.TraitGrantEvents);
        }

        [Fact]
        public void ForceRetiredMembers_DoNotAwaken()
        {
            var survivor = Strong("クラウディア");
            var doomed = Strong("倒れる者");
            doomed.CurrentHP = 1; // 撃破時の損耗（12%〜）でHP0＝強制除籍

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(survivor, doomed), Boss(heavyArmor: true));

            Assert.Equal(DungeonOutcome.Victory, result.Outcome);
            Assert.Contains(doomed.Id, result.ForceRetiredAdventurerIds);
            Assert.False(doomed.HasTrait(TraitCatalog.GiantHunterId));
            Assert.True(survivor.HasTrait(TraitCatalog.GiantHunterId));
        }

        [Fact]
        public void LogText_MatchesSpec()
        {
            var e = new TraitGrantEvent(Guid.NewGuid(), "クラウディア", TraitCatalog.GiantHunterId, null, TraitGrantCause.Awakening);

            Assert.Equal("【実績開眼】クラウディア は強敵との死闘を経て特性『巨獣狩り』を開眼した！", e.ToLogText());
        }

        // ==================== 教官の伝授可能特性（施設画面の表示） ====================

        [Fact]
        public void TransmittableTraits_ExcludeCursesAndBeautiful_InSlotOrder()
        {
            var trainer = new Adventurer();
            foreach (var id in new[] { TraitCatalog.OldWoundId, TraitCatalog.MentorId, TraitCatalog.BeautifulId,
                                       TraitCatalog.GiantHunterId, TraitCatalog.TraumaId })
                trainer.TryAddTrait(id);

            Assert.Equal(new[] { TraitCatalog.MentorId, TraitCatalog.GiantHunterId }, TrainingSystem.GetTransmittableTraitIds(trainer));
        }

        [Fact]
        public void TransmittableTraits_EmptyForCursesOnly()
        {
            var trainer = new Adventurer();
            trainer.TryAddTrait(TraitCatalog.TraumaId);

            Assert.Empty(TrainingSystem.GetTransmittableTraitIds(trainer));
        }
    }
}
