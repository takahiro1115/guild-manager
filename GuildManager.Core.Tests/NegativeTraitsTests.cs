using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 2026年10月・§0.56のテスト：地図読み、生まれつきの欠点9種（忘却できない・対の長所で克服・伝授されない）、
    /// 後天の障害5種（毒の後遺症・足の古傷・腕の古傷・戦慄・燃え尽き）の効果と付き方。
    /// 実行方法: `dotnet test GuildManager.Core.Tests --filter FullyQualifiedName~NegativeTraits`
    /// </summary>
    public class NegativeTraitsTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        private class FixedRng : IRng
        {
            private readonly int _value;
            public FixedRng(int value) => _value = value;
            public int NextInt(int min, int max) => Math.Clamp(_value, min, max);
        }

        private static readonly (string Flaw, string? OvercomeBy)[] Flaws =
        {
            (TraitCatalog.CowardId, TraitCatalog.BraveId),
            (TraitCatalog.ClumsyId, TraitCatalog.AttentiveId),
            (TraitCatalog.PoorDirectionId, TraitCatalog.MapReaderId),
            (TraitCatalog.RecklessId, TraitCatalog.BraveId),
            (TraitCatalog.SicklyId, TraitCatalog.SturdyId),
            (TraitCatalog.FickleId, TraitCatalog.DiligentId),
            (TraitCatalog.MoodyId, TraitCatalog.CheerfulId),
            (TraitCatalog.SlothfulId, TraitCatalog.HardworkerId),
            (TraitCatalog.SpendthriftId, null),
        };

        private static readonly string[] AcquiredCurses =
        {
            TraitCatalog.PoisonAftereffectId, TraitCatalog.LegWoundId, TraitCatalog.ArmWoundId, TraitCatalog.DreadId, TraitCatalog.BurnoutId,
        };

        private static Adventurer Make(int stat = 50, params string[] traits)
        {
            var a = new Adventurer { Name = "リナ", JobClass = JobClass.Ranger, STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat };
            foreach (var id in traits) Assert.True(a.TryAddTrait(id), id);
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
            var boss = new FloorBoss { Name = "階層の主", Floor = 1, MaxHp = 1, CurrentHp = 1 };
            boss.Gimmicks.AddRange(gimmicks);
            return boss;
        }

        private static BossGimmick Uncounterable(BossGimmickType type, int danger = 2) => new()
        {
            Type = type, DangerLevel = danger, CounterRoles = { JobClass.Cleric },
        };

        // ==================== 定義 ====================

        [Fact]
        public void Flaws_AreDefined_WithOvercomeTraits()
        {
            foreach (var (flaw, overcomeBy) in Flaws)
            {
                var def = TraitCatalog.FindById(flaw)!;
                Assert.True(def.IsFlaw, flaw);
                Assert.False(def.IsCurseOrInjury, flaw);
                Assert.False(def.IsTransmittable, flaw);
                Assert.Equal(overcomeBy, def.OvercomeByTraitId);
                Assert.False(string.IsNullOrWhiteSpace(def.DisplayName));
            }
        }

        [Fact]
        public void AcquiredCurses_AreCurseTraits()
        {
            foreach (var id in AcquiredCurses)
            {
                var def = TraitCatalog.FindById(id)!;
                Assert.True(def.IsCurseOrInjury, id);
                Assert.False(def.IsFlaw, id);
                Assert.False(def.IsTransmittable, id);
            }
        }

        [Fact]
        public void DisplayNames_MatchSpec()
        {
            Assert.Equal(
                new[] { "臆病", "不器用", "方向音痴", "猪突猛進", "病弱", "飽きっぽい", "気難しい", "怠け者", "浪費家" },
                Flaws.Select(f => TraitCatalog.FindById(f.Flaw)!.DisplayName));
            Assert.Equal(
                new[] { "毒の後遺症", "足の古傷", "腕の古傷", "戦慄", "燃え尽き" },
                AcquiredCurses.Select(id => TraitCatalog.FindById(id)!.DisplayName));
            Assert.Equal("地図読み", TraitCatalog.MapReader.DisplayName);
            Assert.True(TraitCatalog.MapReader.IsTransmittable);
        }

        [Fact]
        public void InnatePools_AndChances()
        {
            Assert.Equal(Flaws.Select(f => f.Flaw), RecruitmentSystem.InnateFlawPool);
            Assert.Contains(TraitCatalog.MapReaderId, RecruitmentSystem.InnateTraitPool);
            Assert.Equal(2, RecruitmentBalance.InnateFlawChancePercent);
            foreach (var id in AcquiredCurses)
                Assert.DoesNotContain(id, RecruitmentSystem.InnateTraitPool.Concat(RecruitmentSystem.InnateFlawPool));
        }

        // ==================== 忘却できない・侵食されない ====================

        [Fact]
        public void Flaw_CannotBeForgottenOrReplacedManually()
        {
            var a = Make(40, TraitCatalog.CowardId);

            Assert.False(a.CanRemoveTrait(TraitCatalog.CowardId));
            Assert.False(a.TryRemoveTrait(TraitCatalog.CowardId));
            Assert.False(a.TryReplaceTrait(TraitCatalog.CowardId, TraitCatalog.AttentiveId));
            Assert.Contains(TraitCatalog.CowardId, a.TraitIds);
        }

        [Fact]
        public void Flaw_IsNotEroded_ByCurseTraits()
        {
            // [臆病, 豪胆以外の長所4つ] で満杯 → 古傷は最後の長所を侵食し、欠点は残る。
            var a = Make(40, TraitCatalog.CowardId, TraitCatalog.AttentiveId, TraitCatalog.CountryBredId, TraitCatalog.MentorId, TraitCatalog.DiligentId);

            Assert.True(a.TryAddCurseTrait(TraitCatalog.TraumaId, out var eroded));

            Assert.Equal(TraitCatalog.DiligentId, eroded);
            Assert.Contains(TraitCatalog.CowardId, a.TraitIds);
        }

        // ==================== 克服 ====================

        [Fact]
        public void GainingOpposite_OvercomesFlaw_InPlace()
        {
            var a = Make(40, TraitCatalog.AttentiveId, TraitCatalog.CowardId, TraitCatalog.DiligentId);

            Assert.Equal(new[] { TraitCatalog.CowardId }, a.FlawsOvercomeBy(TraitCatalog.BraveId));
            Assert.True(a.TryAddTrait(TraitCatalog.BraveId));

            Assert.Equal(new[] { TraitCatalog.AttentiveId, TraitCatalog.BraveId, TraitCatalog.DiligentId }, a.TraitIds);
        }

        [Fact]
        public void Overcoming_WorksEvenWhenFull()
        {
            var a = Make(40, TraitCatalog.ClumsyId, TraitCatalog.BraveId, TraitCatalog.CountryBredId, TraitCatalog.MentorId, TraitCatalog.DiligentId);
            Assert.False(a.CanAddTrait(TraitCatalog.ScholarId)); // 普通の長所は満杯で付かない

            Assert.True(a.CanAddTrait(TraitCatalog.AttentiveId));
            Assert.True(a.TryAddTrait(TraitCatalog.AttentiveId));

            Assert.Equal(TraitCatalog.AttentiveId, a.TraitIds[0]);
            Assert.Equal(Adventurer.MaxTraitCount, a.TraitIds.Count);
        }

        [Fact]
        public void Brave_OvercomesBothCowardAndReckless()
        {
            var a = Make(40, TraitCatalog.CowardId, TraitCatalog.AttentiveId, TraitCatalog.RecklessId);

            Assert.True(a.TryAddTrait(TraitCatalog.BraveId));

            Assert.Equal(new[] { TraitCatalog.BraveId, TraitCatalog.AttentiveId }, a.TraitIds);
        }

        [Fact]
        public void Flaw_IsNotAdded_WhenOppositeIsHeld()
        {
            var a = Make(40, TraitCatalog.BraveId);

            Assert.False(a.CanAddTrait(TraitCatalog.CowardId));
            Assert.False(a.TryAddTrait(TraitCatalog.CowardId));
            Assert.True(a.TryAddTrait(TraitCatalog.SpendthriftId)); // 克服先の無い欠点は付く
        }

        [Fact]
        public void Trainer_TeachesOpposite_ToFullStudent_AndLogsOvercoming()
        {
            var state = new GameState();
            var trainer = Make(40, TraitCatalog.BraveId);
            trainer.Name = "クラウディア";
            trainer.IsRetired = true;
            state.RetiredAdventurers.Add(trainer);
            state.AssignedTrainers[FacilityType.WarriorHall] = trainer.Id;

            var student = Make(40, TraitCatalog.CowardId, TraitCatalog.AttentiveId, TraitCatalog.CountryBredId, TraitCatalog.MentorId, TraitCatalog.DiligentId);
            student.Age = 20;
            state.Adventurers.Add(student);
            state.TrainingAssignments[student.Id] = FacilityType.WarriorHall;

            var e = Assert.Single(new TrainingSystem(new AlwaysMinRng()).ProcessWeeklyTraitTransmission(state));

            Assert.Equal(TraitCatalog.BraveId, e.TraitId);
            Assert.Equal(new[] { TraitCatalog.CowardId }, e.OvercomeTraitIds);
            Assert.Equal(TraitCatalog.BraveId, student.TraitIds[0]);
            Assert.Equal("【奥義継承】教官クラウディアの指導により、リナは特性『豪胆』を会得し、『臆病』を克服した！", e.ToLogText());
        }

        [Fact]
        public void Flaws_AreNotTransmitted()
        {
            var trainer = Make(40, TraitCatalog.CowardId, TraitCatalog.SpendthriftId, TraitCatalog.BraveId);
            Assert.Equal(new[] { TraitCatalog.BraveId }, TrainingSystem.GetTransmittableTraitIds(trainer));
        }

        // ==================== 欠点の効果 ====================

        [Fact]
        public void Coward_AndReckless_IncreaseBossHpLoss()
        {
            var coward = Make(50, TraitCatalog.CowardId);
            var reckless = Make(50, TraitCatalog.RecklessId);
            var r1 = new DungeonResolver(new AlwaysMaxRng()).Resolve(PartyOf(coward), MakeBoss());
            var r2 = new DungeonResolver(new AlwaysMaxRng()).Resolve(PartyOf(reckless), MakeBoss());

            // AlwaysMaxRng：損耗率は撃破なら BaseHpLossPctMax、撤退なら RetreatHpLossPctMax。
            static int BasePct(DungeonResult r) => r.Outcome == DungeonOutcome.Victory ? DungeonBalance.BaseHpLossPctMax : DungeonBalance.RetreatHpLossPctMax;
            int basePct = BasePct(r1);
            Assert.Equal(basePct, BasePct(r2));
            Assert.Equal(coward.MaxHP * (basePct + 5) / 100, r1.HpLostByAdventurer[coward.Id]);
            Assert.Equal(reckless.MaxHP * (basePct + 8) / 100, r2.HpLostByAdventurer[reckless.Id]);
        }

        [Fact]
        public void Reckless_RaisesPower_AndDread_LowersIt()
        {
            double plain = DungeonPowerCalculator.MemberPower(Make());
            Assert.Equal(plain * 1.10, DungeonPowerCalculator.MemberPower(Make(50, TraitCatalog.RecklessId)), precision: 6);

            var dread = Make();
            Assert.True(dread.TryAddTrait(TraitCatalog.DreadId));
            dread.CurrentHP = dread.MaxHP;
            Assert.Equal(plain * 0.90, DungeonPowerCalculator.MemberPower(dread), precision: 6);
        }

        [Fact]
        public void Clumsy_LowersStealth_AndCancelsWithAttentiveIfBothExisted()
        {
            double plain = ScoutingResolver.GetStealthValue(Make(40));
            Assert.Equal(plain * 0.90, ScoutingResolver.GetStealthValue(Make(40, TraitCatalog.ClumsyId)), precision: 6);
        }

        [Fact]
        public void PoorDirection_AndMapReader_ChangeTraversal()
        {
            double plain = DungeonTraversalResolver.GetMemberTraversalValue(Make(40));
            Assert.Equal(plain * 0.90, DungeonTraversalResolver.GetMemberTraversalValue(Make(40, TraitCatalog.PoorDirectionId)), precision: 6);
            Assert.Equal(plain * 1.10, DungeonTraversalResolver.GetMemberTraversalValue(Make(40, TraitCatalog.MapReaderId)), precision: 6);
            Assert.Equal(plain * 1.25, DungeonTraversalResolver.GetMemberTraversalValue(Make(40, TraitCatalog.MapReaderId, TraitCatalog.PathfinderId)), precision: 6);
        }

        [Fact]
        public void Sickly_LowersMaxHp_AndIsOvercomeBySturdy()
        {
            var a = Make(40, TraitCatalog.SicklyId);
            Assert.Equal((int)((40 * CombatBalance.MaxHpVitCoefficient + CombatBalance.MaxHpBase) * 0.90), a.MaxHP);

            Assert.True(a.TryAddTrait(TraitCatalog.SturdyId));
            Assert.Equal((int)((40 * CombatBalance.MaxHpVitCoefficient + CombatBalance.MaxHpBase) * 1.10), a.MaxHP);
        }

        [Theory]
        // 新鋭期（18歳）の基礎確率0.42（§0.60）。飽きっぽいは−0.05で閾値37。
        [InlineData(37, true)]
        [InlineData(38, false)]
        public void Fickle_LowersTrainingGrowthChance(int roll, bool expectGrowth)
        {
            var a = new Adventurer { Age = 18, STR = 30, VIT = 30, PA_STR = 90, PA_VIT = 90 };
            Assert.True(a.TryAddTrait(TraitCatalog.FickleId));
            var state = new GameState();
            state.Adventurers.Add(a);
            state.TrainingAssignments[a.Id] = FacilityType.WarriorHall;

            var events = new GrowthSystem(new FixedRng(roll)).ProcessTrainingGrowth(state, new HashSet<Guid>());

            Assert.Equal(expectGrowth ? 1 : 0, events.Count);
        }

        [Fact]
        public void Moody_AndBurnout_LowerSatisfactionEachWeek()
        {
            var state = new GameState();
            var plain = Make(40);
            var moody = Make(40, TraitCatalog.MoodyId);
            var burnt = Make(40);
            Assert.True(burnt.TryAddTrait(TraitCatalog.BurnoutId));
            foreach (var a in new[] { plain, moody, burnt })
            {
                a.Satisfaction = 50;
                state.Adventurers.Add(a);
            }

            new SatisfactionSystem().ProcessWeeklySatisfaction(state, new HashSet<Guid>());

            Assert.Equal(plain.Satisfaction - 1, moody.Satisfaction);
            Assert.Equal(plain.Satisfaction - 1, burnt.Satisfaction);
        }

        [Fact]
        public void Slothful_EarnsNothing_ButStillHelpsMood()
        {
            var state = new GameState();
            var lazy = Make(40, TraitCatalog.SlothfulId);
            state.Adventurers.Add(lazy);
            int gold = state.Gold;

            var entry = Assert.Single(MasterMoodSystem.ProcessIdleHelp(state, new HashSet<Guid>(), new MasterMoodReport()));

            Assert.Equal(0, entry.Gold);
            Assert.Equal(MasterMoodBalance.IdleAdventurerHelpMood, entry.Mood);
            Assert.Equal(gold, state.Gold);
        }

        [Fact]
        public void Spendthrift_FeelsUnderpaid_AtTheNormalWage()
        {
            // 採用時の普通の週給（総合PA×係数）だと、浪費家は適正週給×1.25の80%に届かず賃金不満（毎週−8）になる。
            var state = new GameState();
            var plain = Make(40);
            var spender = Make(40, TraitCatalog.SpendthriftId);
            foreach (var a in new[] { plain, spender })
            {
                a.WeeklyWage = (int)(a.TotalPA * EconomyBalance.WeeklyWageCoefficient * 0.95);
                a.Satisfaction = 50;
                state.Adventurers.Add(a);
            }

            new SatisfactionSystem().ProcessWeeklySatisfaction(state, new HashSet<Guid>());

            Assert.Equal(plain.Satisfaction - SatisfactionBalance.UnderpaidPenalty, spender.Satisfaction);
        }

        // ==================== 後天の障害の付き方 ====================

        [Fact]
        public void PoisonAftereffect_GrantedToLowHpSurvivor_OfUncounteredPoison()
        {
            // 同じ乱数・同じ能力の隊員で損耗量を測ってから、生還時にHPが最大HPの25%未満で残るよう現在HPを合わせる。
            var probe = Make();
            var probeResult = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(probe), MakeBoss(Uncounterable(BossGimmickType.Poison)));
            int loss = probeResult.HpLostByAdventurer[probe.Id];

            var member = Make();
            member.CurrentHP = loss + member.MaxHP / 10;
            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(member), MakeBoss(Uncounterable(BossGimmickType.Poison)));

            Assert.True(member.CurrentHP > 0);
            Assert.True(member.CurrentHP < member.MaxHP * 0.25);
            Assert.True(member.HasTrait(TraitCatalog.PoisonAftereffectId));
            var grant = Assert.Single(result.TraitGrantEvents, g => g.TraitId == TraitCatalog.PoisonAftereffectId);
            Assert.Equal(TraitGrantCause.PoisonAftereffect, grant.Cause);
            Assert.Equal("【不可逆障害】リナ は猛毒に深く侵され『毒の後遺症』が残った", grant.ToLogText());
        }

        [Fact]
        public void PoisonAftereffect_NotGranted_ToHealthySurvivor()
        {
            var member = Make();
            new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(member), MakeBoss(Uncounterable(BossGimmickType.Poison)));

            Assert.True(member.CurrentHP >= member.MaxHP * 0.25);
            Assert.False(member.HasTrait(TraitCatalog.PoisonAftereffectId));
        }

        [Fact]
        public void PoisonAftereffect_LowersVit()
        {
            var a = Make(40);
            Assert.True(a.TryAddTrait(TraitCatalog.PoisonAftereffectId));
            Assert.Equal(36, a.GetEffectiveStat("VIT"), precision: 6);
        }

        [Fact]
        public void LegAndArmWounds_LowerTheirStats()
        {
            var leg = Make(40);
            Assert.True(leg.TryAddTrait(TraitCatalog.LegWoundId));
            Assert.Equal(30, leg.GetEffectiveStat("AGI"), precision: 6);
            Assert.Equal(40, leg.GetEffectiveStat("STR"), precision: 6);

            var arm = Make(40);
            Assert.True(arm.TryAddTrait(TraitCatalog.ArmWoundId));
            Assert.Equal(32, arm.GetEffectiveStat("STR"), precision: 6);
            Assert.Equal(32, arm.GetEffectiveStat("DEX"), precision: 6);
            Assert.Equal(40, arm.GetEffectiveStat("AGI"), precision: 6);
        }

        [Fact]
        public void Dread_GrantedToSurvivor_OfUncounteredInstantKill()
        {
            var sensor = Make(50, TraitCatalog.SixthSenseId); // 危機察知で生き残る
            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(sensor), MakeBoss(Uncounterable(BossGimmickType.InstantKill)));

            Assert.True(sensor.CurrentHP > 0);
            Assert.True(sensor.HasTrait(TraitCatalog.DreadId));
            Assert.Contains(result.TraitGrantEvents, g => g.TraitId == TraitCatalog.DreadId && g.Cause == TraitGrantCause.Dread);
        }

        [Fact]
        public void Dread_RollUsesThirtyPercent()
        {
            var sensor = Make(50, TraitCatalog.SixthSenseId);
            new DungeonResolver(new FixedRng(31)).Resolve(PartyOf(sensor), MakeBoss(Uncounterable(BossGimmickType.InstantKill)));
            Assert.False(sensor.HasTrait(TraitCatalog.DreadId));
        }

        [Fact]
        public void ConsecutiveDeployment_CountsUp_AndResets()
        {
            var state = new GameState();
            var a = Make(40);
            state.Adventurers.Add(a);
            var system = new SatisfactionSystem();

            system.ProcessWeeklySatisfaction(state, new HashSet<Guid> { a.Id });
            system.ProcessWeeklySatisfaction(state, new HashSet<Guid> { a.Id });
            Assert.Equal(2, a.ConsecutiveDeploymentWeeks);

            system.ProcessWeeklySatisfaction(state, new HashSet<Guid>());
            Assert.Equal(0, a.ConsecutiveDeploymentWeeks);
        }

        [Theory]
        [InlineData(11, 1, false)] // 連続11週は対象外
        [InlineData(12, 5, true)]  // 12週以上、5%以下で当選
        [InlineData(12, 6, false)]
        public void Burnout_RollsAfterTwelveStraightWeeks(int weeks, int roll, bool expectGrant)
        {
            var state = new GameState();
            var a = Make(40);
            a.ConsecutiveDeploymentWeeks = weeks;
            state.Adventurers.Add(a);

            var events = new SatisfactionSystem(new FixedRng(roll)).ProcessWeeklyBurnout(state);

            Assert.Equal(expectGrant, a.HasTrait(TraitCatalog.BurnoutId));
            Assert.Equal(expectGrant ? 1 : 0, events.Count);
            if (expectGrant)
                Assert.Equal("【精神的打撃】リナ は休みなく戦い続け『燃え尽き』に陥った", events[0].ToLogText());
        }

        [Fact]
        public void ConsecutiveDeploymentWeeks_SurvivesSave_AndOldSaveStartsAtZero()
        {
            var state = new GameState();
            var a = Make(40, TraitCatalog.CowardId);
            a.ConsecutiveDeploymentWeeks = 7;
            state.Adventurers.Add(a);

            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(state.ToSaveData()))!);
            var b = restored.Adventurers.Single(x => x.Id == a.Id);
            Assert.Equal(7, b.ConsecutiveDeploymentWeeks);
            Assert.Contains(TraitCatalog.CowardId, b.TraitIds);

            var json = JsonSerializer.Serialize(state.ToSaveData()).Replace("\"ConsecutiveDeploymentWeeks\":7,", "").Replace(",\"ConsecutiveDeploymentWeeks\":7", "");
            Assert.DoesNotContain("ConsecutiveDeploymentWeeks", json);
            var old = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!);
            Assert.Equal(0, old.Adventurers.Single(x => x.Id == a.Id).ConsecutiveDeploymentWeeks);
        }
    }
}
