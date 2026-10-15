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

        private static FloorBoss MakeBoss(int floor = 1, double intelRate = 0.0) =>
            new FloorBoss { Name = "階層の主", Floor = floor, MaxHp = 500, CurrentHp = 500, IntelRate = intelRate };

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
            // §0.98：勝ち負けは勝率の抽選。解析なし（比≒0.89）は勝率3割ほど、完全解析（比≒1.07）は8割ほど。
            // 勝ち負けの乱数を「いちばん悪い目」（10000）にして解析なしを、「いちばん良い目」（1）にして完全解析を見る
            var retreat = new DungeonResolver(new AlwaysMinRng(), new AlwaysMaxRng()).Resolve(PartyOf(MakeRanger(80)), withoutIntel);
            var victory = new DungeonResolver(new AlwaysMinRng(), new AlwaysMinRng()).Resolve(PartyOf(MakeRanger(80)), withIntel);

            Assert.Equal(DungeonOutcome.Retreat, retreat.Outcome);
            Assert.False(retreat.FullIntelBonusApplied);

            Assert.Equal(DungeonOutcome.Victory, victory.Outcome);
            Assert.True(victory.FullIntelBonusApplied);
            Assert.True(victory.PartyPower > retreat.PartyPower);
            Assert.True(victory.WinChance > retreat.WinChance + 0.3);
        }

        // ---------------- 決戦の勝率（§0.98） ----------------

        [Fact]
        public void WinChance_FollowsTheCurve_WithSureAndHopelessEnds()
        {
            Assert.Equal(0.5, DungeonResolver.WinChance(DungeonBalance.BossWinCenterRatio * 100, 100), 3);
            Assert.Equal(1.0, DungeonResolver.WinChance(DungeonBalance.BossWinSureRatio * 100, 100));
            Assert.Equal(0.0, DungeonResolver.WinChance(DungeonBalance.BossWinHopelessRatio * 100 - 1, 100));
            // 比が上がるほど勝率も上がる
            double prev = 0;
            for (double r = 0.7; r < 1.3; r += 0.05)
            {
                double c = DungeonResolver.WinChance(r * 100, 100);
                Assert.True(c >= prev);
                prev = c;
            }
            Assert.InRange(DungeonResolver.WinChance(100, 100), 0.6, 0.75); // 比1.0≒7割
        }

        [Fact]
        public void Resolve_SureWin_DoesNotDependOnTheOutcomeRoll()
        {
            var boss = MakeBoss(floor: 1, intelRate: 1.0);
            var party = PartyOf(MakeRanger(200));
            Assert.Equal(1.0, DungeonResolver.WinChance(party, boss, null));

            var result = new DungeonResolver(new AlwaysMinRng(), new AlwaysMaxRng()).Resolve(party, boss);

            Assert.Equal(DungeonOutcome.Victory, result.Outcome);
        }

        [Fact]
        public void Resolve_Hopeless_LosesEvenWithTheBestRoll()
        {
            var boss = MakeBoss(floor: 90, intelRate: 0.0);
            var party = PartyOf(MakeRanger(20));
            Assert.Equal(0.0, DungeonResolver.WinChance(party, boss, null));

            var result = new DungeonResolver(new AlwaysMinRng(), new AlwaysMinRng()).Resolve(party, boss);

            Assert.Equal(DungeonOutcome.Retreat, result.Outcome);
            Assert.False(boss.IsDefeated);
        }

        [Fact]
        public void Resolve_PartialIntel_DoesNotGrantDamageBonus()
        {
            // ボーナスは完全解析（1.0）到達時のみ。0.75では付かない。
            var boss = MakeBoss(floor: 5, intelRate: 0.75);

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(MakeRanger(80)), boss);

            Assert.False(result.FullIntelBonusApplied);
        }

        private sealed class FixedFatalRng : IRng
        {
            private readonly int _value;
            public FixedFatalRng(int value) => _value = value;
            public int NextInt(int min, int max) => Math.Clamp(_value, min, max);
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(10, true)]  // FatalChancePercent（10）以下で強制除籍
        [InlineData(11, false)]
        [InlineData(100, false)]
        public void HpZero_IsFatal_OnlyWithTheFatalChance_OtherwiseSurvivesAtOneHp_SeverelyInjured(int fatalRoll, bool fatal)
        {
            Assert.Equal(10, CombatBalance.FatalChancePercent);
            var doomed = MakeRanger(0);
            doomed.CurrentHP = 1; // どの損耗でもHP0になる（§0.138 で即死級は無くした）
            var boss = MakeBoss(floor: 1, intelRate: 0.0);

            var result = new DungeonResolver(new AlwaysMinRng(), fatalRng: new FixedFatalRng(fatalRoll)).Resolve(PartyOf(doomed), boss);

            if (fatal)
            {
                Assert.Equal(0, doomed.CurrentHP);
                Assert.Contains(doomed.Id, result.ForceRetiredAdventurerIds);
                Assert.Empty(result.NearDeathSurvivorIds);
            }
            else
            {
                // §0.125：一命を取り留め、HP1で重傷。強制除籍にはならない
                Assert.Equal(1, doomed.CurrentHP);
                Assert.Empty(result.ForceRetiredAdventurerIds);
                Assert.Contains(doomed.Id, result.NearDeathSurvivorIds);
                Assert.Equal(InjurySeverity.Severe, doomed.Injury);
                Assert.Contains(result.InjuryEvents, e => e.AdventurerId == doomed.Id && e.Severity == InjurySeverity.Severe);
                Assert.Equal(0, result.HpLostByAdventurer[doomed.Id]); // HP1で挑み、HP1で一命を取り留めた
            }
        }

        [Fact]
        public void NearDeathSurvivor_RollsAnOldWound_EvenOnVictory()
        {
            // 撃破でも、HP0から一命を取り留めた隊員は古傷のロールを受ける（§0.125。AlwaysMinRng の 1 は古傷の確率10%に当たる）
            var strong = MakeRanger(90);
            var weak = MakeRanger(90);
            weak.CurrentHP = 1; // 撃破の損耗でもHP0になる（強い仲間が倒してくれる）
            var boss = MakeBoss(floor: 1, intelRate: 1.0);

            var result = new DungeonResolver(new AlwaysMinRng(), new AlwaysMinRng(), fatalRng: new FixedFatalRng(100)).Resolve(PartyOf(strong, weak), boss);

            Assert.Equal(DungeonOutcome.Victory, result.Outcome);
            Assert.Contains(weak.Id, result.NearDeathSurvivorIds);
            Assert.Contains(result.TraitGrantEvents, g => g.AdventurerId == weak.Id && TraitCatalog.OldWoundVariantIds.Contains(g.TraitId));
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

        // ---------------- 特性（§0.138：番人のギミックを無くして置き換えた効き目） ----------------

        [Fact]
        public void SixthSense_HalvesTheFatalChance_AtHpZero()
        {
            var plain = MakeRanger(0);
            var sensed = MakeRanger(0);
            sensed.TryAddTrait(TraitCatalog.SixthSenseId);
            Assert.Equal(CombatBalance.FatalChancePercent, DungeonResolver.FatalChanceFor(plain));
            Assert.Equal((int)Math.Round(CombatBalance.FatalChancePercent * TraitBalance.SixthSenseFatalChanceRate), DungeonResolver.FatalChanceFor(sensed));

            // 判定の乱数が普通なら除籍になる値（10）でも、危機察知は一命を取り留める
            sensed.CurrentHP = 1;
            var result = new DungeonResolver(new AlwaysMinRng(), fatalRng: new FixedFatalRng(CombatBalance.FatalChancePercent)).Resolve(PartyOf(sensed), MakeBoss());
            Assert.Equal(1, sensed.CurrentHP);
            Assert.Contains(sensed.Id, result.SixthSenseAdventurerIds);
        }

        [Fact]
        public void Dread_IsRolled_ForNearDeathSurvivors()
        {
            var survivor = MakeRanger(0);
            survivor.CurrentHP = 1;
            // AlwaysMinRng の 1 は戦慄の確率（DreadChance）に当たる
            var result = new DungeonResolver(new AlwaysMinRng(), fatalRng: new FixedFatalRng(100)).Resolve(PartyOf(survivor), MakeBoss());
            Assert.Contains(survivor.Id, result.NearDeathSurvivorIds);
            Assert.Contains(TraitCatalog.DreadId, survivor.TraitIds);

            // HP0にならなければ戦慄は付かない
            var safe = MakeRanger(0);
            new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(safe), MakeBoss());
            Assert.DoesNotContain(TraitCatalog.DreadId, safe.TraitIds);
        }

        [Fact]
        public void GiantHunter_Applies_ToEveryBoss()
        {
            var hunter = MakeRanger(50);
            hunter.TryAddTrait(TraitCatalog.GiantHunterId);
            var boss = MakeBoss();
            Assert.True(DungeonPowerCalculator.GiantHunterApplies(hunter, boss));
            Assert.False(DungeonPowerCalculator.GiantHunterApplies(hunter, null));
            Assert.Equal(DungeonPowerCalculator.MemberPower(MakeRanger(50)) * (1 + CombatBalance.GiantHunterDamageBonusRate), DungeonPowerCalculator.MemberPower(hunter, boss), precision: 6);
        }

        [Fact]
        public void Resolve_ThrowsForEmptyParty()
        {
            Assert.Throws<InvalidOperationException>(() =>
                new DungeonResolver(new AlwaysMinRng()).Resolve(new Party(), MakeBoss()));
        }
    }
}
