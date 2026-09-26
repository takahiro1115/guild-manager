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
    /// 特性深化 Step 2（2026年9月、→ 03 §0.32）のテスト：障害特性の通常枠侵食、新規特性4種
    /// （夜目・耐毒体質・巨獣狩り・勤勉）の効果、採用時の先天特性プール。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~TraitEffect`
    /// </summary>
    public class TraitEffectTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        /// <summary>NextInt(min, max) が固定値を [min, max] にクランプして返す。</summary>
        private class FixedRng : IRng
        {
            private readonly int _value;
            public FixedRng(int value) => _value = value;
            public int NextInt(int min, int max) => Math.Clamp(_value, min, max);
        }

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            return party;
        }

        // ==================== 障害特性の侵食（→ Adventurer.TryAddCurseTrait） ====================

        private static Adventurer FullOfNormalTraits()
        {
            var a = new Adventurer();
            foreach (var id in new[] { TraitCatalog.BraveId, TraitCatalog.AttentiveId, TraitCatalog.CountryBredId,
                                       TraitCatalog.MentorId, TraitCatalog.DiligentId })
                Assert.True(a.TryAddTrait(id));
            return a;
        }

        [Theory]
        [InlineData(TraitCatalog.OldWoundId)]
        [InlineData(TraitCatalog.TraumaId)]
        public void CurseTrait_ErodesLastNormalTrait_WhenSlotsAreFull(string curseId)
        {
            var a = FullOfNormalTraits(); // [豪胆, 注意深い, 田舎育ち, 師匠肌, 勤勉]

            Assert.True(a.TryAddCurseTrait(curseId, out var eroded));

            Assert.Equal(TraitCatalog.DiligentId, eroded); // 最も後ろの通常特性が消える
            Assert.Equal(5, a.TraitIds.Count);
            Assert.Equal(curseId, a.TraitIds[4]); // 消えた枠を障害特性が上書きする
            Assert.DoesNotContain(TraitCatalog.DiligentId, a.TraitIds);
        }

        [Fact]
        public void TryAddTrait_ForCurseTrait_AlsoErodes()
        {
            // 既存の付与経路（トラウマの付与＝CompatibilitySystem）は TryAddTrait を呼ぶため、そこでも侵食が効くこと。
            var a = FullOfNormalTraits();

            Assert.True(a.TryAddTrait(TraitCatalog.TraumaId));

            Assert.Contains(TraitCatalog.TraumaId, a.TraitIds);
            Assert.Equal(5, a.TraitIds.Count);
        }

        [Fact]
        public void CurseTrait_SkipsOtherCurses_WhenChoosingWhatToErode()
        {
            var a = new Adventurer();
            foreach (var id in new[] { TraitCatalog.BraveId, TraitCatalog.AttentiveId, TraitCatalog.CountryBredId,
                                       TraitCatalog.MentorId, TraitCatalog.TraumaId })
                Assert.True(a.TryAddTrait(id));

            Assert.True(a.TryAddCurseTrait(TraitCatalog.OldWoundId, out var eroded));

            Assert.Equal(TraitCatalog.MentorId, eroded); // 末尾のトラウマは飛ばし、その手前の通常特性
            Assert.Equal(new[] { TraitCatalog.BraveId, TraitCatalog.AttentiveId, TraitCatalog.CountryBredId,
                                 TraitCatalog.OldWoundId, TraitCatalog.TraumaId }, a.TraitIds);
        }

        [Fact]
        public void CurseTrait_UsesFreeSlot_WithoutErosion()
        {
            var a = new Adventurer();
            a.TryAddTrait(TraitCatalog.BraveId);

            Assert.True(a.TryAddCurseTrait(TraitCatalog.OldWoundId, out var eroded));

            Assert.Null(eroded);
            Assert.Equal(new[] { TraitCatalog.BraveId, TraitCatalog.OldWoundId }, a.TraitIds);
        }

        [Fact]
        public void CurseTraits_AreNeverEroded_ByLaterCurses()
        {
            // 障害特性は現状2種（古傷・トラウマ）しか無く重複も持てないため、「5枠すべてが障害特性」の状態は
            // 正規の経路では作れない。代わりに、侵食が何度起きても障害特性だけは消えないことを確かめる
            // （5枠すべてが障害になった時は、侵食できる通常特性が無いので追加不可になる、→ Adventurer.TryAddCurseTrait）。
            var a = FullOfNormalTraits();
            Assert.True(a.TryAddCurseTrait(TraitCatalog.TraumaId, out _));
            Assert.True(a.TryAddCurseTrait(TraitCatalog.OldWoundId, out var eroded));

            Assert.NotEqual(TraitCatalog.TraumaId, eroded);
            Assert.Contains(TraitCatalog.TraumaId, a.TraitIds);
            Assert.Contains(TraitCatalog.OldWoundId, a.TraitIds);
            Assert.Equal(3, a.TraitIds.Count(id => !TraitCatalog.FindById(id)!.IsCurseOrInjury));
        }

        [Fact]
        public void CurseTrait_NotAdded_WhenAlreadyHeld()
        {
            var a = FullOfNormalTraits();
            a.TryAddCurseTrait(TraitCatalog.OldWoundId, out _);
            var before = a.TraitIds.ToList();

            Assert.False(a.TryAddCurseTrait(TraitCatalog.OldWoundId, out var eroded));

            Assert.Null(eroded);
            Assert.Equal(before, a.TraitIds); // 2回目で通常特性がさらに消えたりしない
        }

        [Fact]
        public void TryAddCurseTrait_WithNormalTrait_NeverErodes()
        {
            var a = FullOfNormalTraits();

            Assert.False(a.TryAddCurseTrait(TraitCatalog.ResistPoisonId, out var eroded));

            Assert.Null(eroded);
            Assert.DoesNotContain(TraitCatalog.ResistPoisonId, a.TraitIds);
        }

        // ==================== 夜目：未踏破の損耗軽減（→ DungeonTraversalResolver） ====================

        private static Adventurer MakeSturdyTraveler()
        {
            // MakeSturdySpecialist（DungeonTraversalResolverTests）と同じ：10Fから出発して電撃（4階層）進軍になる走破力300。
            var a = new Adventurer { STR = 10, AGI = 500, VIT = 300, MND = 0, DEX = 500, LDR = 0, INT = 10 };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static DungeonField MakeUnexploredField()
        {
            var field = new DungeonField { Id = "test", Name = "テスト用フィールド", Order = 1, IsUnlocked = true, ReachedFloor = 1 };
            field.Bosses.Add(new FloorBoss { Name = "区間のボス", Floor = 50, MaxHp = 1, IntelRate = 0.0 });
            return field;
        }

        [Fact]
        public void NightVision_ReducesUnexploredLoss_ByTenPercent()
        {
            Assert.Equal(0.10, DungeonTraversalBalance.NightVisionUnexploredDamageReductionRate, precision: 6);

            var plain = MakeSturdyTraveler();
            var withNightVision = MakeSturdyTraveler();
            withNightVision.TryAddTrait(TraitCatalog.NightVisionId);

            var plainResult = new DungeonTraversalResolver(new AlwaysMaxRng()).Resolve(PartyOf(plain), MakeUnexploredField(), currentFloor: 10);
            var nvResult = new DungeonTraversalResolver(new AlwaysMaxRng()).Resolve(PartyOf(withNightVision), MakeUnexploredField(), currentFloor: 10);

            Assert.True(plainResult.EnteredUnexplored);
            Assert.False(plainResult.NightVisionApplied);
            Assert.True(nvResult.NightVisionApplied);
            Assert.Equal(DungeonBalance.UnexploredHpLossPctMax, plainResult.UnexploredLossPct, precision: 6);
            Assert.Equal(DungeonBalance.UnexploredHpLossPctMax * 0.9, nvResult.UnexploredLossPct, precision: 6);
            Assert.Equal((int)Math.Floor(withNightVision.MaxHP * DungeonBalance.UnexploredHpLossPctMax * 0.9 / 100.0 + 1e-9),
                nvResult.HpLostByAdventurer[withNightVision.Id]);
            Assert.True(nvResult.HpLostByAdventurer[withNightVision.Id] < plainResult.HpLostByAdventurer[plain.Id]);
        }

        [Fact]
        public void NightVision_OneHolder_ProtectsWholeParty()
        {
            var holder = MakeSturdyTraveler();
            holder.TryAddTrait(TraitCatalog.NightVisionId);
            var other = MakeSturdyTraveler();

            Assert.Equal(0.9, DungeonTraversalResolver.UnexploredLossMultiplier(PartyOf(holder, other)), precision: 6);
            Assert.Equal(1.0, DungeonTraversalResolver.UnexploredLossMultiplier(PartyOf(other)), precision: 6);
        }

        // ==================== 耐毒体質・巨獣狩り（→ DungeonResolver・DungeonPowerCalculator） ====================

        private static Adventurer MakeRanger(int stat = 50)
        {
            var a = new Adventurer { JobClass = JobClass.Ranger, STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static FloorBoss MakeBoss(params BossGimmick[] gimmicks)
        {
            var boss = new FloorBoss { Name = "階層の主", Floor = 1, MaxHp = 500, CurrentHp = 500 };
            boss.Gimmicks.AddRange(gimmicks);
            return boss;
        }

        private static BossGimmick UncounterablePoison(int danger = 2) => new()
        {
            Type = BossGimmickType.Poison, DangerLevel = danger, RequiredCounterRole = JobClass.Cleric,
        };

        [Fact]
        public void ResistPoison_ReducesUncounteredPoisonDamage_ByTwentyPercent()
        {
            Assert.Equal(0.20, CombatBalance.ResistPoisonDamageReductionRate, precision: 6);
            double poisonTerm = 2 * DungeonBalance.UncounteredDamageMultiplierPerDangerLevel;

            var plain = new DungeonResolver(new AlwaysMaxRng()).Resolve(PartyOf(MakeRanger()), MakeBoss(UncounterablePoison()));
            var holder = MakeRanger();
            holder.TryAddTrait(TraitCatalog.ResistPoisonId);
            var resisted = new DungeonResolver(new AlwaysMaxRng()).Resolve(PartyOf(holder, MakeRanger()), MakeBoss(UncounterablePoison()));

            Assert.Equal(1.0 + poisonTerm, plain.DamageMultiplier, precision: 6);
            Assert.False(plain.ResistPoisonApplied);
            Assert.Equal(1.0 + poisonTerm * 0.8, resisted.DamageMultiplier, precision: 6);
            Assert.True(resisted.ResistPoisonApplied);
            Assert.True(resisted.HpLostByAdventurer[holder.Id] < plain.HpLostByAdventurer.Values.Single());
        }

        [Fact]
        public void ResistPoison_OnlyAffectsPoisonTerm()
        {
            var holder = MakeRanger();
            holder.TryAddTrait(TraitCatalog.ResistPoisonId);
            var heavy = new BossGimmick { Type = BossGimmickType.HeavyArmor, DangerLevel = 2, RequiredCounterRole = JobClass.Cleric };

            var result = new DungeonResolver(new AlwaysMaxRng()).Resolve(PartyOf(holder), MakeBoss(heavy));

            Assert.False(result.ResistPoisonApplied);
            Assert.Equal(1.0 + 2 * DungeonBalance.UncounteredDamageMultiplierPerDangerLevel, result.DamageMultiplier, precision: 6);
        }

        [Fact]
        public void ResistPoison_NoEffect_WhenPoisonIsCountered()
        {
            var holder = MakeRanger();
            holder.TryAddTrait(TraitCatalog.ResistPoisonId);
            var cleric = MakeRanger();
            cleric.JobClass = JobClass.Cleric;

            var result = new DungeonResolver(new AlwaysMaxRng()).Resolve(PartyOf(holder, cleric), MakeBoss(UncounterablePoison()));

            Assert.Contains(BossGimmickType.Poison, result.CounteredGimmicks);
            Assert.False(result.ResistPoisonApplied);
            Assert.Equal(1.0, result.DamageMultiplier, precision: 6);
        }

        [Fact]
        public void GiantHunter_AddsFifteenPercentCp_AgainstHeavyArmorBoss()
        {
            Assert.Equal(0.15, CombatBalance.GiantHunterDamageBonusRate, precision: 6);
            var hunter = MakeRanger();
            hunter.TryAddTrait(TraitCatalog.GiantHunterId);
            var heavyBoss = MakeBoss(new BossGimmick { Type = BossGimmickType.HeavyArmor, DangerLevel = 1, RequiredCounterRole = JobClass.Ranger });

            double basePower = DungeonPowerCalculator.MemberPower(hunter);

            Assert.Equal(basePower * 1.15, DungeonPowerCalculator.MemberPower(hunter, heavyBoss), precision: 6);
            Assert.True(DungeonPowerCalculator.GiantHunterApplies(hunter, heavyBoss));
        }

        [Fact]
        public void GiantHunter_NoBonus_AgainstOtherBoss_OrForNonHolder()
        {
            var hunter = MakeRanger();
            hunter.TryAddTrait(TraitCatalog.GiantHunterId);
            var plain = MakeRanger();
            var poisonBoss = MakeBoss(UncounterablePoison());
            var heavyBoss = MakeBoss(new BossGimmick { Type = BossGimmickType.HeavyArmor, DangerLevel = 1, RequiredCounterRole = JobClass.Ranger });

            Assert.Equal(DungeonPowerCalculator.MemberPower(hunter), DungeonPowerCalculator.MemberPower(hunter, poisonBoss), precision: 6);
            Assert.Equal(DungeonPowerCalculator.MemberPower(plain), DungeonPowerCalculator.MemberPower(plain, heavyBoss), precision: 6);
        }

        [Fact]
        public void GiantHunter_RaisesPartyPower_InBossResolution()
        {
            var heavy = new BossGimmick { Type = BossGimmickType.HeavyArmor, DangerLevel = 1, RequiredCounterRole = JobClass.Ranger };
            var hunter = MakeRanger();
            hunter.TryAddTrait(TraitCatalog.GiantHunterId);
            var other = MakeRanger();
            double expected = DungeonPowerCalculator.MemberPower(hunter) * 1.15 + DungeonPowerCalculator.MemberPower(other);

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(hunter, other), MakeBoss(heavy));

            Assert.Equal(expected, result.PartyPower, precision: 6);
            Assert.Equal(new[] { hunter.Id }, result.GiantHunterAdventurerIds);
        }

        // ==================== 勤勉：訓練の成長確率（→ GrowthSystem.ProcessTrainingGrowth） ====================

        private static (GameState State, Adventurer Trainee) TrainingState(bool diligent)
        {
            var a = new Adventurer { Age = 18, STR = 30, VIT = 30, PA_STR = 90, PA_VIT = 90 };
            if (diligent) a.TryAddTrait(TraitCatalog.DiligentId);
            var state = new GameState();
            state.Adventurers.Add(a);
            state.TrainingAssignments[a.Id] = FacilityType.WarriorHall;
            return (state, a);
        }

        [Theory]
        // 新鋭期（18歳）の基礎確率0.35＝閾値35。勤勉は+0.10で閾値45（施設倍率1.0・教官なし）。
        [InlineData(35, false, true)]
        [InlineData(36, false, false)]
        [InlineData(36, true, true)]
        [InlineData(45, true, true)]
        [InlineData(46, true, false)]
        public void Diligent_AddsTenPointsToTrainingGrowthChance(int roll, bool diligent, bool expectGrowth)
        {
            Assert.Equal(0.10, TrainingBalance.DiligentGrowthRateBonus, precision: 6);
            Assert.Equal(0.35, GrowthBalance.GetBaseProbability(AgeBand.Young), precision: 6);
            var (state, _) = TrainingState(diligent);

            var events = new GrowthSystem(new FixedRng(roll)).ProcessTrainingGrowth(state, new HashSet<Guid>());

            Assert.Equal(expectGrowth ? 1 : 0, events.Count);
        }

        // ==================== 採用時の先天特性プール（→ RecruitmentSystem.InnateTraitPool） ====================

        [Fact]
        public void InnateTraitPool_IncludesNewTraitsAndMentor_ButNotGiantHunter()
        {
            var pool = RecruitmentSystem.InnateTraitPool;

            foreach (var id in new[] { TraitCatalog.ResistPoisonId, TraitCatalog.NightVisionId, TraitCatalog.DiligentId, TraitCatalog.MentorId })
                Assert.Contains(id, pool);
            Assert.DoesNotContain(TraitCatalog.GiantHunterId, pool);
            Assert.DoesNotContain(TraitCatalog.OldWoundId, pool);
            Assert.DoesNotContain(TraitCatalog.TraumaId, pool);
        }

        [Fact]
        public void Recruitment_CanActuallyGrantNewInnateTraits()
        {
            // 全判定が当たる乱数（NextInt(1,100)→1）：プールの先頭から5枠ぶん付く。新規特性が付くかは
            // 並び順に依存するため、5枠を超える分は付かないことと、付いた特性がすべてプール内であることを確かめる。
            var offers = new RecruitmentSystem(new AlwaysMinRng()).GenerateCandidates(new GameState(), candidateCount: 1);
            var traits = offers[0].Candidate.TraitIds;

            Assert.Equal(Adventurer.MaxTraitCount, traits.Count);
            Assert.All(traits, t => Assert.Contains(t, RecruitmentSystem.InnateTraitPool));
        }

        [Fact]
        public void Recruitment_DraftCandidates_UseSamePool()
        {
            // ドラフトの候補も同じ抽選を通る（→ RecruitmentSystem.GenerateOne）。
            var draft = new RecruitmentSystem(new AlwaysMinRng()).StartInitialDraft(new GameState());

            Assert.All(draft.Offers, o => Assert.All(o.Candidate.TraitIds, t => Assert.Contains(t, RecruitmentSystem.InnateTraitPool)));
        }
    }
}
