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
    /// 2026年10月・§0.55で足した特性10種（鷹の目・危機察知・守り手・健脚・頑強・治りが早い・快活・働き者・火の魔術師・ソードマスター）の
    /// 効果と、先天プール・確率の変更のテスト。
    /// 実行方法: `dotnet test GuildManager.Core.Tests --filter FullyQualifiedName~NewTraits`
    /// </summary>
    public class NewTraitsTests
    {
        private class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        private static readonly string[] NewTraitIds =
        {
            TraitCatalog.HawkEyeId, TraitCatalog.SixthSenseId, TraitCatalog.GuardianId, TraitCatalog.PathfinderId,
            TraitCatalog.SturdyId, TraitCatalog.QuickHealerId, TraitCatalog.CheerfulId, TraitCatalog.HardworkerId,
            TraitCatalog.FireMageId, TraitCatalog.SwordMasterId,
        };

        private static Adventurer Make(int stat = 50, params string[] traits)
        {
            var a = new Adventurer { Name = "リナ", JobClass = JobClass.Ranger, STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat };
            foreach (var id in traits) Assert.True(a.TryAddTrait(id));
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            return party;
        }

        private static FloorBoss MakeBoss(params BossGimmick[] gimmicks)
        {
            var boss = new FloorBoss { Name = "階層の主", Floor = 1, MaxHp = 500, CurrentHp = 500 };
            boss.Gimmicks.AddRange(gimmicks);
            return boss;
        }

        // 対策役（Cleric）がいない部隊では対策できないギミック。
        private static BossGimmick Uncounterable(BossGimmickType type, int danger = 2) => new()
        {
            Type = type, DangerLevel = danger, RequiredCounterRole = JobClass.Cleric,
        };

        // ==================== 定義・CSV ====================

        [Fact]
        public void NewTraits_AreDefined_NormalAndTransmittable()
        {
            foreach (var id in NewTraitIds)
            {
                var def = TraitCatalog.FindById(id);
                Assert.NotNull(def);
                Assert.False(string.IsNullOrWhiteSpace(def!.DisplayName));
                Assert.False(string.IsNullOrWhiteSpace(def.Description));
                Assert.False(def.IsCurseOrInjury);
                Assert.True(def.IsTransmittable);
            }
        }

        [Fact]
        public void DisplayNames_MatchSpec()
        {
            Assert.Equal(
                new[] { "鷹の目", "危機察知", "守り手", "健脚", "頑強", "治りが早い", "快活", "働き者", "火の魔術師", "ソードマスター" },
                NewTraitIds.Select(id => TraitCatalog.FindById(id)!.DisplayName));
        }

        [Fact]
        public void CsvValues_AreLoaded()
        {
            Assert.Equal(0.50, TraitBalance.HawkEyeFlyingDamageReductionRate, precision: 6);
            Assert.Equal(60, TraitBalance.SixthSenseInstantKillHpLossPct);
            Assert.Equal(0.20, TraitBalance.GuardianGuardBonus, precision: 6);
            Assert.Equal(0.15, TraitBalance.PathfinderTraversalBonus, precision: 6);
            Assert.Equal(0.10, TraitBalance.SturdyMaxHpBonus, precision: 6);
            Assert.Equal(1, TraitBalance.QuickHealerInjuryRecoveryBonus);
            Assert.Equal(0.25, TraitBalance.QuickHealerRestRecoveryBonus, precision: 6);
            Assert.Equal(1, TraitBalance.CheerfulSatisfactionBonus);
            Assert.Equal(1.5, TraitBalance.HardworkerIdleHelpGoldMultiplier, precision: 6);
            Assert.Equal(0.10, TraitBalance.FireMageIntBonus, precision: 6);
            Assert.Equal(0.10, TraitBalance.SwordMasterStatBonus, precision: 6);
        }

        // ==================== 先天プール ====================

        [Fact]
        public void InnateTraitPool_HasAllNewTraits_AndChanceIsThreePercent()
        {
            foreach (var id in NewTraitIds)
                Assert.Contains(id, RecruitmentSystem.InnateTraitPool);
            Assert.Equal(20, RecruitmentSystem.InnateTraitPool.Length); // §0.56で地図読みを追加
            Assert.Equal(3, RecruitmentBalance.InnateTraitChancePercent);
        }

        // ==================== 鷹の目 ====================

        [Fact]
        public void HawkEye_HalvesUncounteredFlyingTerm()
        {
            double term = 2 * DungeonBalance.UncounteredDamageMultiplierPerDangerLevel;

            var plain = new DungeonResolver(new AlwaysMaxRng()).Resolve(PartyOf(Make()), MakeBoss(Uncounterable(BossGimmickType.Flying)));
            var eye = Make(50, TraitCatalog.HawkEyeId);
            var helped = new DungeonResolver(new AlwaysMaxRng()).Resolve(PartyOf(eye, Make()), MakeBoss(Uncounterable(BossGimmickType.Flying)));

            Assert.Equal(1.0 + term, plain.DamageMultiplier, precision: 6);
            Assert.False(plain.HawkEyeApplied);
            Assert.Equal(1.0 + term * 0.5, helped.DamageMultiplier, precision: 6);
            Assert.True(helped.HawkEyeApplied);
        }

        [Fact]
        public void HawkEye_DoesNotAffectOtherGimmicks()
        {
            var result = new DungeonResolver(new AlwaysMaxRng())
                .Resolve(PartyOf(Make(50, TraitCatalog.HawkEyeId)), MakeBoss(Uncounterable(BossGimmickType.Poison)));

            Assert.False(result.HawkEyeApplied);
            Assert.Equal(1.0 + 2 * DungeonBalance.UncounteredDamageMultiplierPerDangerLevel, result.DamageMultiplier, precision: 6);
        }

        // ==================== 危機察知 ====================

        [Fact]
        public void SixthSense_StopsInstantKillAtSixtyPercent_ForHolderOnly()
        {
            var sensor = Make(50, TraitCatalog.SixthSenseId);
            var other = Make();
            int sensorMax = sensor.MaxHP;

            var result = new DungeonResolver(new AlwaysMaxRng()).Resolve(PartyOf(sensor, other), MakeBoss(Uncounterable(BossGimmickType.InstantKill)));

            Assert.Contains(BossGimmickType.InstantKill, result.UncounteredGimmicks);
            Assert.Equal(sensorMax * 60 / 100, result.HpLostByAdventurer[sensor.Id]);
            Assert.True(sensor.CurrentHP > 0);
            Assert.Equal(0, other.CurrentHP); // 保有していない隊員は従来どおり全損
            Assert.Equal(new[] { sensor.Id }, result.SixthSenseAdventurerIds);
        }

        [Fact]
        public void SixthSense_NotRecorded_WhenInstantKillIsCountered()
        {
            var sensor = Make(50, TraitCatalog.SixthSenseId);
            var cleric = Make();
            cleric.JobClass = JobClass.Cleric;

            var result = new DungeonResolver(new AlwaysMaxRng()).Resolve(PartyOf(sensor, cleric), MakeBoss(Uncounterable(BossGimmickType.InstantKill)));

            Assert.Empty(result.SixthSenseAdventurerIds);
        }

        // ==================== 守り手・健脚 ====================

        [Fact]
        public void Guardian_RaisesGuardValueAndCarrier()
        {
            var plain = Make(40);
            var guardian = Make(40, TraitCatalog.GuardianId);

            Assert.Equal(40 * 1.2, ScoutingResolver.GetGuardValue(guardian), precision: 6);
            Assert.Equal(40, ScoutingResolver.GetGuardValue(plain), precision: 6);

            var carrier = ScoutingResolver.FindGuardCarrier(PartyOf(plain, guardian));
            Assert.Same(guardian, carrier.Member);
            Assert.Equal(48, carrier.Value, precision: 6);
            Assert.Equal(48 + 40 * ScoutingBalance.GuardSupportRatio, ScoutingResolver.CalculateGuardPower(PartyOf(plain, guardian)), precision: 6);
        }

        [Fact]
        public void Pathfinder_RaisesTraversalContribution()
        {
            double plain = DungeonTraversalResolver.GetMemberTraversalValue(Make(40));
            double walker = DungeonTraversalResolver.GetMemberTraversalValue(Make(40, TraitCatalog.PathfinderId));

            Assert.True(plain > 0);
            Assert.Equal(plain * 1.15, walker, precision: 6);
        }

        // ==================== 頑強 ====================

        [Fact]
        public void Sturdy_RaisesMaxHpBasePart()
        {
            var plain = Make(40);
            var sturdy = Make(40, TraitCatalog.SturdyId);

            int basePart = (int)(40 * CombatBalance.MaxHpVitCoefficient) + CombatBalance.MaxHpBase;
            Assert.Equal(basePart, plain.MaxHP);
            Assert.Equal((int)((40 * CombatBalance.MaxHpVitCoefficient + CombatBalance.MaxHpBase) * 1.10), sturdy.MaxHP);
            Assert.True(sturdy.MaxHP > plain.MaxHP);
        }

        [Fact]
        public void ForgettingSturdy_ClampsCurrentHp()
        {
            var a = Make(40, TraitCatalog.SturdyId);
            Assert.Equal(a.MaxHP, a.CurrentHP);

            Assert.True(a.TryRemoveTrait(TraitCatalog.SturdyId));

            Assert.Equal(a.MaxHP, a.CurrentHP);
        }

        [Fact]
        public void ErodingSturdy_ClampsCurrentHp()
        {
            var a = Make(40, TraitCatalog.BraveId, TraitCatalog.AttentiveId, TraitCatalog.CountryBredId, TraitCatalog.MentorId, TraitCatalog.SturdyId);
            Assert.True(a.TryAddCurseTrait(TraitCatalog.TraumaId, out var eroded));

            Assert.Equal(TraitCatalog.SturdyId, eroded);
            Assert.Equal(a.MaxHP, a.CurrentHP);
        }

        // ==================== 治りが早い ====================

        [Fact]
        public void QuickHealer_RecoversInjuryOneWeekFaster()
        {
            var state = new GameState();
            int speed = FacilityBalance.GetInfirmaryInjuryRecoverySpeed(state.GetFacilityLevel(FacilityType.Infirmary));
            var plain = Make(40);
            var healer = Make(40, TraitCatalog.QuickHealerId);
            foreach (var a in new[] { plain, healer })
            {
                a.Injury = InjurySeverity.Severe;
                a.InjuryWeeksRemaining = 6;
                state.Adventurers.Add(a);
            }

            new InjuryRecoverySystem().ProcessWeeklyRecovery(state);

            Assert.Equal(6 - speed, plain.InjuryWeeksRemaining);
            Assert.Equal(6 - speed - 1, healer.InjuryWeeksRemaining);
        }

        [Fact]
        public void QuickHealer_RestsFaster()
        {
            var state = new GameState();
            var plain = Make(40);
            var healer = Make(40, TraitCatalog.QuickHealerId);
            plain.CurrentHP = 1;
            healer.CurrentHP = 1;
            state.Adventurers.Add(plain);
            state.Adventurers.Add(healer);

            new RestRecoverySystem().ProcessWeeklyRest(state, new HashSet<Guid>());

            int plainGain = plain.CurrentHP - 1;
            int healerGain = healer.CurrentHP - 1;
            Assert.True(plainGain > 0);
            Assert.InRange(healerGain, (int)(plainGain * 1.25) - 1, (int)(plainGain * 1.25) + 1); // 端数の切り捨て位置が違うため±1を許す
            Assert.True(healerGain > plainGain);
        }

        // ==================== 快活 ====================

        [Fact]
        public void Cheerful_GainsOneSatisfactionPerWeek()
        {
            var state = new GameState();
            var plain = Make(40);
            var cheerful = Make(40, TraitCatalog.CheerfulId);
            plain.Satisfaction = 50;
            cheerful.Satisfaction = 50;
            state.Adventurers.Add(plain);
            state.Adventurers.Add(cheerful);

            new SatisfactionSystem().ProcessWeeklySatisfaction(state, new HashSet<Guid>());

            Assert.Equal(plain.Satisfaction + 1, cheerful.Satisfaction);
        }

        // ==================== 働き者 ====================

        [Fact]
        public void Hardworker_EarnsOneAndAHalfTimes_Rounded()
        {
            var state = new GameState();
            var plain = Make(40);
            var worker = Make(40, TraitCatalog.HardworkerId);
            state.Adventurers.Add(plain);
            state.Adventurers.Add(worker);
            int goldBefore = state.Gold;

            var entries = MasterMoodSystem.ProcessIdleHelp(state, new HashSet<Guid>(), new MasterMoodReport());

            int baseGold = MasterMoodBalance.IdleAdventurerHelpGold;
            int workerGold = (int)Math.Round(baseGold * 1.5, MidpointRounding.AwayFromZero);
            Assert.Equal(baseGold, entries.Single(e => e.AdventurerId == plain.Id).Gold);
            Assert.Equal(workerGold, entries.Single(e => e.AdventurerId == worker.Id).Gold);
            Assert.Equal(goldBefore + baseGold + workerGold, state.Gold);
        }

        // ==================== 火の魔術師 ====================

        [Fact]
        public void FireMage_RaisesInt_AndStacksWithReductions()
        {
            var mage = Make(40, TraitCatalog.FireMageId);
            Assert.Equal(44, mage.GetEffectiveStat("INT"), precision: 6);
            Assert.Equal(40, mage.GetEffectiveStat("STR"), precision: 6);

            // トラウマはMNDなので無関係。割合の合算はINTの素の値に掛かる（装備補正は掛けない）。
            var mageWithStaff = Make(40, TraitCatalog.FireMageId);
            mageWithStaff.EquippedWeapon = EquipmentItem.FromCatalog(ItemCatalog.FindById(ItemCatalog.MageStaffId)!);
            Assert.Equal(44 + mageWithStaff.GetEquipmentStatBonus("INT"), mageWithStaff.GetEffectiveStat("INT"), precision: 6);
        }

        // ==================== ソードマスター ====================

        [Theory]
        [InlineData(ItemCatalog.IronSwordId, true)]
        [InlineData(ItemCatalog.GreatSwordId, true)]
        [InlineData(ItemCatalog.DaggerId, false)]
        [InlineData(ItemCatalog.SpearId, false)]
        public void SwordMaster_RaisesStrAndAgi_OnlyWithSword(string weaponId, bool expectBonus)
        {
            var a = Make(40, TraitCatalog.SwordMasterId);
            a.EquippedWeapon = EquipmentItem.FromCatalog(ItemCatalog.FindById(weaponId)!);
            double rate = expectBonus ? 1.10 : 1.0;

            Assert.Equal(expectBonus, a.SwordMasterApplies);
            Assert.Equal(40 * rate + a.GetEquipmentStatBonus("STR"), a.GetEffectiveStat("STR"), precision: 6);
            Assert.Equal(40 * rate + a.GetEquipmentStatBonus("AGI"), a.GetEffectiveStat("AGI"), precision: 6);
            Assert.Equal(40 + a.GetEquipmentStatBonus("VIT"), a.GetEffectiveStat("VIT"), precision: 6);
        }

        [Fact]
        public void SwordMaster_NoBonus_WithoutTraitOrWeapon()
        {
            var noTrait = Make(40);
            noTrait.EquippedWeapon = EquipmentItem.FromCatalog(ItemCatalog.FindById(ItemCatalog.IronSwordId)!);
            var noWeapon = Make(40, TraitCatalog.SwordMasterId);

            Assert.False(noTrait.SwordMasterApplies);
            Assert.False(noWeapon.SwordMasterApplies);
            Assert.Equal(40, noWeapon.GetEffectiveStat("STR"), precision: 6);
        }

        // ==================== 伝授 ====================

        [Fact]
        public void NewTraits_CanBeTransmittedByTrainer()
        {
            var trainer = Make(40, TraitCatalog.SwordMasterId, TraitCatalog.CheerfulId);
            trainer.IsRetired = true;

            Assert.Equal(new[] { TraitCatalog.SwordMasterId, TraitCatalog.CheerfulId }, TrainingSystem.GetTransmittableTraitIds(trainer));
        }
    }
}
