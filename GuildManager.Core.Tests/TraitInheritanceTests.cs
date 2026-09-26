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
    /// 教官深化 Step 1（2026年9月、→ 03 §0.34）のテスト：教官からの特性伝授（奥義継承）の確率・候補・枠満杯の見送り・
    /// 週報の文面、およびボス戦撤退時の古傷判定のHP10%緩和。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~TraitInheritance`
    /// </summary>
    public class TraitInheritanceTests
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

        /// <summary>戦士訓練所に教官（traitIds を持つ）と生徒1名を配置した状態。</summary>
        private static (GameState State, Adventurer Trainer, Adventurer Student) Setup(params string[] trainerTraits)
        {
            var state = new GameState();
            var trainer = new Adventurer { Name = "クラウディア", IsRetired = true, Age = 26 };
            foreach (var id in trainerTraits) Assert.True(trainer.TryAddTrait(id));
            state.RetiredAdventurers.Add(trainer);
            state.AssignedTrainers[FacilityType.WarriorHall] = trainer.Id;

            var student = new Adventurer { Name = "リナ", Age = 20 };
            state.Adventurers.Add(student);
            state.TrainingAssignments[student.Id] = FacilityType.WarriorHall;
            return (state, trainer, student);
        }

        // ==================== 確率 ====================

        [Fact]
        public void CsvValues_AreLoaded()
        {
            Assert.Equal(0.05, TrainingBalance.TraitInheritanceBaseChance, precision: 6);
            Assert.Equal(0.15, TrainingBalance.MentorTraitInheritanceBonus, precision: 6);
        }

        [Theory]
        [InlineData(5, true)]  // 基礎5%：閾値5
        [InlineData(6, false)]
        public void BaseChance_IsFivePercent(int roll, bool expectInherit)
        {
            var (state, _, student) = Setup(TraitCatalog.BraveId);

            var events = new TrainingSystem(new FixedRng(roll)).ProcessWeeklyTraitTransmission(state);

            Assert.Equal(expectInherit ? 1 : 0, events.Count);
            Assert.Equal(expectInherit, student.HasTrait(TraitCatalog.BraveId));
        }

        [Theory]
        [InlineData(20, true)] // 師匠肌で5%+15%＝20%：閾値20
        [InlineData(21, false)]
        public void Mentor_RaisesChanceToTwentyPercent(int roll, bool expectInherit)
        {
            var (state, trainer, student) = Setup(TraitCatalog.MentorId, TraitCatalog.BraveId);
            Assert.Equal(0.20, TrainingSystem.GetInheritanceChance(trainer), precision: 6);

            var events = new TrainingSystem(new FixedRng(roll)).ProcessWeeklyTraitTransmission(state);

            Assert.Equal(expectInherit ? 1 : 0, events.Count);
            // 候補は教官の特性枠の並び順で最初の1つ（師匠肌）。
            Assert.Equal(expectInherit, student.HasTrait(TraitCatalog.MentorId));
        }

        // ==================== 候補の選び方 ====================

        [Fact]
        public void NormalTrait_IsInherited_OnSuccess()
        {
            var (state, trainer, student) = Setup(TraitCatalog.NightVisionId);

            var e = Assert.Single(new TrainingSystem(new AlwaysMinRng()).ProcessWeeklyTraitTransmission(state));

            Assert.Equal(TraitCatalog.NightVisionId, e.TraitId);
            Assert.Same(trainer, e.Trainer);
            Assert.Same(student, e.Student);
            Assert.True(student.HasTrait(TraitCatalog.NightVisionId));
        }

        [Fact]
        public void CurseTraits_AreNeverInherited()
        {
            var (state, _, student) = Setup(TraitCatalog.OldWoundId, TraitCatalog.TraumaId);

            var events = new TrainingSystem(new AlwaysMinRng()).ProcessWeeklyTraitTransmission(state);

            Assert.Empty(events);
            Assert.Empty(student.TraitIds);
        }

        [Fact]
        public void CurseTraits_AreSkipped_InFavorOfNextNormalTrait()
        {
            var (state, _, student) = Setup(TraitCatalog.OldWoundId, TraitCatalog.DiligentId);

            var e = Assert.Single(new TrainingSystem(new AlwaysMinRng()).ProcessWeeklyTraitTransmission(state));

            Assert.Equal(TraitCatalog.DiligentId, e.TraitId);
            Assert.False(student.HasTrait(TraitCatalog.OldWoundId));
        }

        [Fact]
        public void TraitsTheStudentAlreadyHas_AreNotCandidates()
        {
            var (state, _, student) = Setup(TraitCatalog.BraveId, TraitCatalog.AttentiveId);
            student.TryAddTrait(TraitCatalog.BraveId);

            var e = Assert.Single(new TrainingSystem(new AlwaysMinRng()).ProcessWeeklyTraitTransmission(state));

            Assert.Equal(TraitCatalog.AttentiveId, e.TraitId);
        }

        [Fact]
        public void NoRoll_WhenStudentAlreadyHasEverything()
        {
            var (state, _, student) = Setup(TraitCatalog.BraveId);
            student.TryAddTrait(TraitCatalog.BraveId);

            Assert.Empty(new TrainingSystem(new AlwaysMinRng()).ProcessWeeklyTraitTransmission(state));
        }

        [Fact]
        public void Beautiful_StaysNonTransmittable()
        {
            // 容姿秀麗は先天的な容姿の特性として、従来どおり伝授対象外（→ TraitCatalog.Beautiful）。
            var (state, _, student) = Setup(TraitCatalog.BeautifulId);

            Assert.Empty(new TrainingSystem(new AlwaysMinRng()).ProcessWeeklyTraitTransmission(state));
            Assert.False(student.HasTrait(TraitCatalog.BeautifulId));
        }

        [Fact]
        public void AllNonCurseTraitsExceptBeautiful_AreTransmittable()
        {
            foreach (var def in TraitCatalog.GetAll())
            {
                bool expected = !def.IsCurseOrInjury && def.Id != TraitCatalog.BeautifulId;
                Assert.True(expected == def.IsTransmittable, $"{def.Id} の IsTransmittable が想定と違う");
            }
        }

        // ==================== 枠満杯の見送り ====================

        [Fact]
        public void FullStudent_IsSkipped_WithoutErosion()
        {
            var (state, _, student) = Setup(TraitCatalog.GiantHunterId);
            foreach (var id in new[] { TraitCatalog.BraveId, TraitCatalog.AttentiveId, TraitCatalog.CountryBredId,
                                       TraitCatalog.MentorId, TraitCatalog.DiligentId })
                student.TryAddTrait(id);
            var before = student.TraitIds.ToList();

            var events = new TrainingSystem(new AlwaysMinRng()).ProcessWeeklyTraitTransmission(state);

            Assert.Empty(events);
            Assert.Equal(before, student.TraitIds);
        }

        // ==================== 週報の文面 ====================

        [Fact]
        public void LogText_MatchesSpec()
        {
            var (state, _, _) = Setup(TraitCatalog.BraveId);
            var e = Assert.Single(new TrainingSystem(new AlwaysMinRng()).ProcessWeeklyTraitTransmission(state));

            Assert.Equal("【奥義継承】教官クラウディアの指導により、リナは特性『豪胆』を会得した！", e.ToLogText());
        }

        // ==================== ボス戦撤退時の古傷判定：最大HPの10%以下へ緩和 ====================

        [Fact]
        public void RetreatThreshold_IsTenPercentOfMaxHp_AtLeastOne()
        {
            Assert.Equal(0.10, CombatBalance.OldWoundRetreatHpThresholdPct, precision: 6);

            var big = new Adventurer { VIT = 100 };   // MaxHP 250 → 25
            var tiny = new Adventurer { VIT = 0 };    // MaxHP 50 → 5
            Assert.Equal(big.MaxHP / 10, CriticalInjury.RetreatHpThreshold(big));
            Assert.Equal(5, CriticalInjury.RetreatHpThreshold(tiny));
            Assert.True(CriticalInjury.RetreatHpThreshold(new Adventurer()) >= 1);
        }

        private static Adventurer RetreatingMember(int hpLeftAfterRetreat)
        {
            // 撤退の損耗は AlwaysMinRng で RetreatHpLossPctMin（20%）。撤退後に hpLeftAfterRetreat が残るよう現在HPを合わせる。
            var a = new Adventurer { Name = "撤退者", JobClass = JobClass.Ranger, STR = 1, AGI = 1, VIT = 100, MND = 1, DEX = 1, LDR = 1, INT = 1 };
            a.CurrentHP = a.MaxHP * DungeonBalance.RetreatHpLossPctMin / 100 + hpLeftAfterRetreat;
            return a;
        }

        private static DungeonResult Retreat(Adventurer member)
        {
            var party = new Party();
            party.TryAdd(member);
            var boss = new FloorBoss { Name = "強敵", Floor = 100, MaxHp = 999, CurrentHp = 999 };
            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(party, boss);
            Assert.Equal(DungeonOutcome.Retreat, result.Outcome);
            return result;
        }

        [Fact]
        public void BossRetreat_RollsOldWound_AtTenPercentHp()
        {
            var probe = RetreatingMember(0);
            int threshold = CriticalInjury.RetreatHpThreshold(probe);
            Assert.True(threshold > 1); // HP1より広い範囲が対象になっている

            var member = RetreatingMember(threshold);
            var result = Retreat(member);

            Assert.Equal(threshold, member.CurrentHP);
            Assert.Equal(TraitCatalog.OldWoundId, Assert.Single(result.TraitGrantEvents).TraitId);
        }

        [Fact]
        public void BossRetreat_NoRoll_AboveTenPercentHp()
        {
            var probe = RetreatingMember(0);
            int threshold = CriticalInjury.RetreatHpThreshold(probe);

            var member = RetreatingMember(threshold + 1);
            var result = Retreat(member);

            Assert.Equal(threshold + 1, member.CurrentHP);
            Assert.Empty(result.TraitGrantEvents);
            Assert.False(member.HasTrait(TraitCatalog.OldWoundId));
        }

        [Fact]
        public void Traversal_StillUsesHpOneThreshold()
        {
            // 道中進軍は従来どおり「HP下限1まで落ちた時」だけ（→ 03 §4.3.2）。緩和はボス戦撤退のみ。
            var a = new Adventurer { VIT = 100 };
            a.CurrentHP = 2;
            Assert.Null(CriticalInjury.RollOldWound(a, new AlwaysMinRng()));
        }
    }
}
