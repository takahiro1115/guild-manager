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
    /// 階層ボス討伐・ギミック対策判定（→ ダンジョン攻略システム）のテスト。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class DungeonResolverTests
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
        /// 斥候（配置補正1.0＝素の点数がそのまま火力になる）のHP満タン冒険者。
        /// 火力の期待値を計算しやすくするため、テストの基準職として使う。
        /// </summary>
        private static Adventurer MakeRanger(int stat = 50)
        {
            var a = new Adventurer
            {
                JobClass = JobClass.Ranger, Placement = Placement.Front,
                STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat,
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static Adventurer MakeCleric(int stat = 50)
        {
            var a = new Adventurer
            {
                JobClass = JobClass.Cleric, Placement = Placement.Back,
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

        private static FloorBoss MakeBoss(int floor = 1, double intelRate = 0.0, params BossGimmick[] gimmicks)
        {
            var boss = new FloorBoss { Name = "階層の主", Floor = floor, MaxHp = 500, CurrentHp = 500, IntelRate = intelRate };
            boss.Gimmicks.AddRange(gimmicks);
            return boss;
        }

        // ---------------- 討伐の見立て用ヘルパー（→ 03 §0.43、大迷宮画面の出撃前の表示と共通） ----------------

        [Fact]
        public void RequiredPower_IsBasePlusFloorTimesPerFloor()
        {
            Assert.Equal(DungeonBalance.PartyPowerRequirementBase + 30 * DungeonBalance.PartyPowerRequirementPerFloor,
                DungeonResolver.RequiredPower(MakeBoss(floor: 30)), precision: 6);
        }

        [Fact]
        public void RequiredPower_AppliesFieldMultiplier()
        {
            var boss = MakeBoss(floor: 30);
            boss.FieldOrder = 3;
            Assert.Equal((DungeonBalance.PartyPowerRequirementBase + 30 * DungeonBalance.PartyPowerRequirementPerFloor)
                * DungeonBalance.GetFieldRequirementMultiplier(3), DungeonResolver.RequiredPower(boss), precision: 6);
        }

        [Fact]
        public void CalculateBossPower_AddsFullIntelBonus_AndMatchesResolve()
        {
            var party = PartyOf(MakeRanger(), MakeCleric());
            double basePower = DungeonPowerCalculator.PartyPower(party.Members);

            Assert.Equal(basePower, DungeonResolver.CalculateBossPower(party, MakeBoss(floor: 1, intelRate: 0.75)), precision: 6);
            var analyzed = MakeBoss(floor: 1, intelRate: 1.0);
            double withBonus = DungeonResolver.CalculateBossPower(party, analyzed);
            Assert.Equal(basePower * (1.0 + DungeonBalance.FullIntelDamageBonus), withBonus, precision: 6);

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(party, MakeBoss(floor: 1, intelRate: 1.0));
            Assert.Equal(withBonus, result.PartyPower, precision: 6);
            Assert.Equal(DungeonResolver.RequiredPower(analyzed), result.RequiredPower, precision: 6);
        }

        // ---------------- ギミックへの備え（§0.68：職業0.5＋能力の合計÷基準、上限1） ----------------

        private static BossGimmick Gimmick(BossGimmickType type, int danger = 2, double threshold = 1000, params JobClass[] roles) => new()
        {
            Type = type, DangerLevel = danger, CounterRoles = roles.ToList(),
            RequiredCounterStat = BossGimmickInfo.CounterStat(type), RequiredCounterStatThreshold = threshold,
        };

        [Fact]
        public void Readiness_ByRole_IsHalf()
        {
            var gimmick = Gimmick(BossGimmickType.Poison, roles: JobClass.Cleric);

            // 能力の合計は基準1000に比べてごくわずか（50＋50）なので、職業の0.5＋0.1
            Assert.Equal(BossGimmickBalance.GimmickRoleReadiness + 100.0 / 1000, DungeonResolver.Readiness(gimmick, PartyOf(MakeRanger(), MakeCleric()).Members), precision: 6);
            Assert.Equal(100.0 / 1000, DungeonResolver.Readiness(gimmick, PartyOf(MakeRanger(), MakeRanger()).Members), precision: 6);
        }

        [Fact]
        public void Readiness_ByStat_IsSumOverThreshold_CappedAtOne()
        {
            var gimmick = Gimmick(BossGimmickType.Poison, threshold: 100);

            Assert.Equal(0.4, DungeonResolver.Readiness(gimmick, PartyOf(MakeRanger(40)).Members), precision: 6);       // 合計40
            Assert.Equal(1.0, DungeonResolver.Readiness(gimmick, PartyOf(MakeRanger(60), MakeRanger(60)).Members), precision: 6); // 合計120
        }

        [Fact]
        public void Readiness_RoleAndStat_AddUpToFull()
        {
            var gimmick = Gimmick(BossGimmickType.Poison, threshold: 100, roles: JobClass.Cleric);

            // 職業0.5＋合計50÷100＝1.0（万全）
            Assert.Equal(1.0, DungeonResolver.Readiness(gimmick, PartyOf(MakeCleric(50)).Members), precision: 6);
            Assert.Equal(ReadinessTier.Full, DungeonResolver.GetReadinessTier(1.0));
            Assert.Equal(ReadinessTier.Partial, DungeonResolver.GetReadinessTier(0.5));
            Assert.Equal(ReadinessTier.None, DungeonResolver.GetReadinessTier(0));
        }

        [Fact]
        public void Readiness_OnlyRolesListedForThisBossCount()
        {
            // 猛毒の候補は神官・学者だが、このボスで効くのは学者だけ（→ BossGimmick.CounterRoles）
            var gimmick = Gimmick(BossGimmickType.Poison, roles: JobClass.Scholar);
            var scholar = MakeRanger(0);
            scholar.JobClass = JobClass.Scholar;

            Assert.Equal(0, DungeonResolver.Readiness(gimmick, PartyOf(MakeCleric(0)).Members), precision: 6);
            Assert.Equal(BossGimmickBalance.GimmickRoleReadiness, DungeonResolver.Readiness(gimmick, PartyOf(scholar).Members), precision: 6);
        }

        [Fact]
        public void Readiness_IsZero_WhenGimmickHasNoCounterPortDefined()
        {
            // 対策口が1つも設定されていないギミックは備え0（ボス定義側の設定漏れを黙って握り潰さないため）。
            var malformed = new BossGimmick { Type = BossGimmickType.Flying, DangerLevel = 1 };

            Assert.Equal(0, DungeonResolver.Readiness(malformed, PartyOf(MakeRanger(90), MakeCleric(90)).Members), precision: 6);
        }

        // ---------------- 火力判定（撃破／撤退） ----------------

        [Fact]
        public void Resolve_Victory_WhenPartyPowerMeetsRequirement()
        {
            var boss = MakeBoss(floor: 1);
            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(MakeRanger(90)), boss); // 火力90×4.2＝378 ≥ 1Fの要求352.5（345＋1×7.5）

            Assert.Equal(DungeonOutcome.Victory, result.Outcome);
            Assert.True(boss.IsDefeated);
            Assert.Equal(0, boss.CurrentHp);
        }

        [Fact]
        public void Resolve_Retreat_WhenPartyPowerIsInsufficient()
        {
            var boss = MakeBoss(floor: 10); // 要求火力＝345＋10×7.5＝420
            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(MakeRanger(10)), boss);

            Assert.Equal(DungeonOutcome.Retreat, result.Outcome);
            Assert.False(boss.IsDefeated);
            Assert.True(boss.CurrentHp > 0, "撤退ではボスのHPは削り切られない（次回は仕切り直し）");
        }

        // ---------------- 完全解析ボーナス（調査との連動） ----------------

        [Fact]
        public void Resolve_FullIntel_AddsDamageBonus_AndCanFlipRetreatIntoVictory()
        {
            // 斥候（配置補正1.0）全能力80 → 素の火力は 80×4.2＝336。
            // 階層5の要求火力は345＋5×7.5＝382.5のため素では届かないが、完全解析の+20%（403.2）で覆る。
            var withoutIntel = MakeBoss(floor: 5, intelRate: 0.0);
            var withIntel = MakeBoss(floor: 5, intelRate: 1.0);
            var resolver = new DungeonResolver(new AlwaysMinRng());

            var retreat = resolver.Resolve(PartyOf(MakeRanger(80)), withoutIntel);
            var victory = resolver.Resolve(PartyOf(MakeRanger(80)), withIntel);

            Assert.Equal(DungeonOutcome.Retreat, retreat.Outcome);
            Assert.False(retreat.FullIntelBonusApplied);

            Assert.Equal(DungeonOutcome.Victory, victory.Outcome);
            Assert.True(victory.FullIntelBonusApplied);
            Assert.True(victory.PartyPower > retreat.PartyPower);
        }

        [Fact]
        public void Resolve_PartialIntel_DoesNotGrantDamageBonus()
        {
            // ボーナスは完全解析（1.0）到達時のみ。0.75では付かない。
            var boss = MakeBoss(floor: 5, intelRate: 0.75);

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(MakeRanger(80)), boss);

            Assert.False(result.FullIntelBonusApplied);
        }

        // ---------------- 損耗が増える型（猛毒・飛行・群れ） ----------------

        [Fact]
        public void Resolve_Shortfall_ScalesDamageMultiplier()
        {
            // 飛行・危険度2・備え0.4（DEX合計40÷100）→ 1.0＋2×0.75×0.6
            var boss = MakeBoss(floor: 1, intelRate: 0.0, Gimmick(BossGimmickType.Flying, danger: 2, threshold: 100));

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(MakeRanger(40)), boss);

            Assert.Contains(BossGimmickType.Flying, result.UncounteredGimmicks);
            Assert.Equal(0.4, result.Readiness[BossGimmickType.Flying], precision: 6);
            Assert.Equal(1.0 + 2 * DungeonBalance.UncounteredDamageMultiplierPerDangerLevel * 0.6, result.DamageMultiplier, precision: 10);
        }

        [Fact]
        public void Resolve_FullReadiness_KeepsDamageMultiplierAtOne()
        {
            var boss = MakeBoss(floor: 1, intelRate: 0.0, Gimmick(BossGimmickType.Poison, danger: 3, threshold: 100, roles: JobClass.Cleric));

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(MakeRanger(), MakeCleric()), boss);

            Assert.Contains(BossGimmickType.Poison, result.CounteredGimmicks);
            Assert.Empty(result.UncounteredGimmicks);
            Assert.Equal(1.0, result.DamageMultiplier, precision: 10);
            Assert.Empty(result.PoisonWeeksByAdventurer);
        }

        [Fact]
        public void Resolve_Swarm_AddsHalfDamage_AndCutsRearPower()
        {
            var boss = MakeBoss(floor: 1, intelRate: 0.0, Gimmick(BossGimmickType.Swarm, danger: 2, threshold: 1_000_000));
            var party = PartyOf(MakeRanger(), MakeCleric());
            double front = DungeonPowerCalculator.MemberPower(party.Members[0], boss);
            double rear = DungeonPowerCalculator.MemberPower(party.Members[1], boss);

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(party, MakeBossCopy(boss));

            double shortfall = 1.0 - result.Readiness[BossGimmickType.Swarm];
            Assert.Equal(1.0 + 2 * DungeonBalance.UncounteredDamageMultiplierPerDangerLevel * shortfall * BossGimmickBalance.SwarmDamageFactor,
                result.DamageMultiplier, precision: 10);
            Assert.Equal(front + rear * (1.0 - BossGimmickBalance.SwarmRearPowerPenalty * shortfall), result.PartyPower, precision: 6);
        }

        // ---------------- 火力・要求火力で効く型（重装甲・魅了・再生） ----------------

        [Fact]
        public void HeavyArmor_LowersPower_ByShortfall_WithoutExtraDamage()
        {
            var party = PartyOf(MakeRanger());
            var plain = MakeBoss(floor: 1);
            var armored = MakeBoss(floor: 1, intelRate: 0.0, Gimmick(BossGimmickType.HeavyArmor, threshold: 1_000_000));

            double shortfall = 1.0 - DungeonResolver.Readiness(armored.Gimmicks[0], party.Members);
            Assert.Equal(DungeonResolver.CalculateBossPower(party, plain) * (1.0 - BossGimmickBalance.HeavyArmorPowerPenalty * shortfall),
                DungeonResolver.CalculateBossPower(party, armored), precision: 6);

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(party, armored);
            Assert.Equal(1.0, result.DamageMultiplier, precision: 10);
        }

        [Fact]
        public void Charm_TakesStrongestMemberOutOfFight_AndHurtsThemMore()
        {
            var strong = MakeRanger(80);
            var weak = MakeRanger(20);
            var party = PartyOf(weak, strong);
            var boss = MakeBoss(floor: 1, intelRate: 0.0, Gimmick(BossGimmickType.Charm, threshold: 1_000_000)); // 備えはほぼ0

            double readiness = DungeonResolver.Readiness(boss.Gimmicks[0], party.Members);
            Assert.Same(strong, DungeonResolver.CharmTarget(party.Members, boss));
            Assert.Equal(DungeonPowerCalculator.MemberPower(weak) + DungeonPowerCalculator.MemberPower(strong) * readiness,
                DungeonResolver.CalculateBossPower(party, boss), precision: 6);

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(party, boss);
            Assert.Equal(strong.Id, result.CharmedAdventurerId);
            int baseLoss = weak.MaxHP * DungeonBalance.RetreatHpLossPctMin / 100;
            Assert.Equal(baseLoss, result.HpLostByAdventurer[weak.Id]);
            Assert.True(result.HpLostByAdventurer[strong.Id] > strong.MaxHP * DungeonBalance.RetreatHpLossPctMin / 100,
                "操られた隊員は損耗が大きい");
        }

        [Fact]
        public void Regeneration_RaisesRequirement_ByShortfall()
        {
            var party = PartyOf(MakeRanger());
            var boss = MakeBoss(floor: 1, intelRate: 0.0, Gimmick(BossGimmickType.Regeneration, threshold: 100)); // AGI 50 → 備え0.5

            Assert.Equal(DungeonResolver.RequiredPower(boss) * (1.0 + BossGimmickBalance.RegenerationRequirementBonus * 0.5),
                DungeonResolver.RequiredPower(boss, null, party), precision: 6);
            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(party, boss);
            Assert.Equal(DungeonResolver.RequiredPower(boss, null, party), result.RequiredPower, precision: 6);
        }

        // ---------------- 即死級（損耗の下限、備え0なら全損） ----------------

        [Fact]
        public void InstantKill_WithNoReadiness_ForceRetiresEveryone()
        {
            var doomed = MakeRanger(0);
            var boss = MakeBoss(floor: 1, intelRate: 0.0, Gimmick(BossGimmickType.InstantKill, danger: 5, roles: JobClass.Knight));

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(doomed), boss);

            Assert.Contains(BossGimmickType.InstantKill, result.UncounteredGimmicks);
            Assert.Equal(0, doomed.CurrentHP);
            Assert.Contains(doomed.Id, result.ForceRetiredAdventurerIds);
        }

        [Fact]
        public void InstantKill_WithHalfReadiness_LosesAboutHalf_AndSurvives()
        {
            var knight = MakeRanger(0);
            knight.JobClass = JobClass.Knight;
            var boss = MakeBoss(floor: 1, intelRate: 0.0, Gimmick(BossGimmickType.InstantKill, danger: 5, roles: JobClass.Knight));

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(knight), boss);

            Assert.True(knight.CurrentHP > 0);
            Assert.Empty(result.ForceRetiredAdventurerIds);
            int floorPct = (int)Math.Round(DungeonBalance.InstantKillUncounteredHpLossPct * (1.0 - BossGimmickBalance.GimmickRoleReadiness));
            Assert.Equal(knight.MaxHP * floorPct / 100, result.HpLostByAdventurer[knight.Id]);
        }

        // ---------------- 猛毒の毒状態 ----------------

        [Fact]
        public void Poison_Shortfall_LeavesPoisonStatus_ThatLowersStats()
        {
            var victim = MakeRanger(50);
            var boss = MakeBoss(floor: 1, intelRate: 0.0, Gimmick(BossGimmickType.Poison, threshold: 100)); // MND 50 → 備え0.5
            double strBefore = victim.GetEffectiveStat("STR");

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(victim), boss);

            double penalty = BossGimmickBalance.PoisonStatusStatPenalty * 0.5;
            Assert.Equal(BossGimmickBalance.PoisonStatusWeeks, victim.PoisonWeeksRemaining);
            Assert.Equal(penalty, victim.PoisonStatPenalty, precision: 6);
            Assert.True(victim.IsPoisoned);
            Assert.Equal(BossGimmickBalance.PoisonStatusWeeks, result.PoisonWeeksByAdventurer[victim.Id]);
            Assert.Equal(strBefore * (1.0 - penalty), victim.GetEffectiveStat("STR"), precision: 6);
        }

        [Fact]
        public void Poison_ResistPoison_HalvesStatusWeeks()
        {
            var victim = MakeRanger(50);
            victim.TryAddTrait(TraitCatalog.ResistPoisonId);
            var boss = MakeBoss(floor: 1, intelRate: 0.0, Gimmick(BossGimmickType.Poison, threshold: 100));

            new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(victim), boss);

            Assert.Equal((int)Math.Ceiling(BossGimmickBalance.PoisonStatusWeeks * BossGimmickBalance.ResistPoisonStatusWeeksRate), victim.PoisonWeeksRemaining);
        }

        [Fact]
        public void PoisonStatus_WearsOff_WithInfirmaryRecovery()
        {
            var state = new GameState();
            var victim = MakeRanger(50);
            victim.PoisonWeeksRemaining = 1;
            victim.PoisonStatPenalty = 0.15;
            state.Adventurers.Add(victim);

            new InjuryRecoverySystem().ProcessWeeklyRecovery(state, new System.Collections.Generic.HashSet<Guid> { victim.Id });
            Assert.True(victim.IsPoisoned, "毒を受けた週は抜けない");

            new InjuryRecoverySystem().ProcessWeeklyRecovery(state);
            Assert.False(victim.IsPoisoned);
            Assert.Equal(0, victim.PoisonStatPenalty);
        }

        private static FloorBoss MakeBossCopy(FloorBoss boss)
        {
            var copy = MakeBoss(boss.Floor, boss.IntelRate);
            copy.Gimmicks.AddRange(boss.Gimmicks);
            return copy;
        }

        [Fact]
        public void Resolve_ThrowsForEmptyParty()
        {
            Assert.Throws<InvalidOperationException>(() =>
                new DungeonResolver(new AlwaysMinRng()).Resolve(new Party(), MakeBoss()));
        }
    }
}
