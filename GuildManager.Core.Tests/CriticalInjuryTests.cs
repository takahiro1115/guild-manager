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
    /// 特性深化 Step 3（2026年9月、→ 03 §0.33）のテスト：重傷（HP1）生還の古傷、障害特性の付与・侵食の週報開示、
    /// 通常特性の手動忘却。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~CriticalInjury`
    /// </summary>
    public class CriticalInjuryTests
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

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            return party;
        }

        private static Adventurer WithHp(int hp, string name = "リナ")
        {
            var a = new Adventurer { Name = name, VIT = 50 };
            a.CurrentHP = hp;
            return a;
        }

        private static void FillWithNormalTraits(Adventurer a)
        {
            foreach (var id in new[] { TraitCatalog.BraveId, TraitCatalog.AttentiveId, TraitCatalog.CountryBredId,
                                       TraitCatalog.MentorId, TraitCatalog.DiligentId })
                Assert.True(a.TryAddTrait(id));
        }

        // ==================== RollOldWound（判定そのもの） ====================

        [Fact]
        public void CsvValue_IsLoaded()
        {
            Assert.Equal(0.10, CombatBalance.OldWoundCriticalChance, precision: 6);
        }

        [Theory]
        [InlineData(10, true)]  // 10%以下で当選
        [InlineData(11, false)]
        public void RollOldWound_UsesTenPercentThreshold(int roll, bool expectGrant)
        {
            var a = WithHp(1);

            var grant = CriticalInjury.RollOldWound(a, new FixedRng(roll));

            Assert.Equal(expectGrant, grant != null);
            Assert.Equal(expectGrant, a.HasTrait(TraitCatalog.OldWoundId));
        }

        [Theory]
        [InlineData(2)]
        [InlineData(50)]
        public void RollOldWound_NeverGrants_WhenHpAboveOne(int hp)
        {
            var a = WithHp(hp);

            Assert.Null(CriticalInjury.RollOldWound(a, new AlwaysMinRng()));
            Assert.False(a.HasTrait(TraitCatalog.OldWoundId));
        }

        [Fact]
        public void RollOldWound_DoesNotDoubleGrant()
        {
            var a = WithHp(1);
            Assert.NotNull(CriticalInjury.RollOldWound(a, new AlwaysMinRng()));

            Assert.Null(CriticalInjury.RollOldWound(a, new AlwaysMinRng()));
            Assert.Single(a.TraitIds, TraitCatalog.OldWoundId);
        }

        [Fact]
        public void RollOldWound_ErodesLastNormalTrait_WhenFull()
        {
            var a = WithHp(1);
            FillWithNormalTraits(a);

            var grant = CriticalInjury.RollOldWound(a, new AlwaysMinRng())!;

            Assert.Equal(TraitCatalog.DiligentId, grant.ErodedTraitId);
            Assert.Equal(TraitCatalog.OldWoundId, a.TraitIds[4]);
            Assert.Equal(5, a.TraitIds.Count);
        }

        // ==================== 道中進軍（→ DungeonTraversalResolver） ====================

        private static Adventurer MakeTraveler()
        {
            // 10Fから出発すると電撃（4階層）で未踏破へ踏み込む走破力300。MaxHPが大きいので、どの損耗率でもHP下限1に落ちる。
            var a = new Adventurer { Name = "旅人", STR = 10, AGI = 500, VIT = 300, MND = 0, DEX = 500, LDR = 0, INT = 10 };
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
        public void Traversal_GrantsOldWound_WhenMemberFallsToHpOne()
        {
            var member = MakeTraveler();
            member.CurrentHP = 2; // 未踏破の重損耗で必ず下限1まで落ちる

            var result = new DungeonTraversalResolver(new AlwaysMinRng()).Resolve(PartyOf(member), MakeUnexploredField(), currentFloor: 10);

            Assert.Equal(1, member.CurrentHP);
            var grant = Assert.Single(result.TraitGrantEvents);
            Assert.Equal(member.Id, grant.AdventurerId);
            Assert.Equal(TraitCatalog.OldWoundId, grant.TraitId);
            Assert.Equal(TraitGrantCause.CriticalInjury, grant.Cause);
            Assert.True(member.HasTrait(TraitCatalog.OldWoundId));
        }

        [Fact]
        public void Traversal_DoesNotRoll_WhenHpStaysAboveOne()
        {
            var member = MakeTraveler(); // 満タンから30%程度の損耗ではHP1にならない

            var result = new DungeonTraversalResolver(new AlwaysMinRng()).Resolve(PartyOf(member), MakeUnexploredField(), currentFloor: 10);

            Assert.True(member.CurrentHP > 1);
            Assert.Empty(result.TraitGrantEvents);
            Assert.False(member.HasTrait(TraitCatalog.OldWoundId));
        }

        [Fact]
        public void Traversal_DoesNotReroll_MemberAlreadyAtHpOne()
        {
            // 既にHP1のまま潜行を続けている隊員は、その週に新たに瀕死へ落ちたわけではないのでロールしない。
            var member = MakeTraveler();
            member.CurrentHP = 1;

            var result = new DungeonTraversalResolver(new AlwaysMinRng()).Resolve(PartyOf(member), MakeUnexploredField(), currentFloor: 10);

            Assert.Empty(result.TraitGrantEvents);
            Assert.False(member.HasTrait(TraitCatalog.OldWoundId));
        }

        // ==================== 階層ボス討伐の撤退（→ DungeonResolver） ====================

        [Fact]
        public void BossRetreat_GrantsOldWound_ToSurvivorAtHpOne()
        {
            // 撤退時の損耗は AlwaysMinRng で RetreatHpLossPctMin（20%）。ちょうどHP1が残るよう現在HPを合わせる。
            var member = new Adventurer { Name = "撤退者", JobClass = JobClass.Ranger, STR = 1, AGI = 1, VIT = 50, MND = 1, DEX = 1, LDR = 1, INT = 1 };
            int loss = member.MaxHP * DungeonBalance.RetreatHpLossPctMin / 100;
            member.CurrentHP = loss + 1;
            var boss = new FloorBoss { Name = "強敵", Floor = 100, MaxHp = 999, CurrentHp = 999 };

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(member), boss);

            Assert.Equal(DungeonOutcome.Retreat, result.Outcome);
            Assert.Equal(1, member.CurrentHP);
            Assert.Equal(TraitCatalog.OldWoundId, Assert.Single(result.TraitGrantEvents).TraitId);
        }

        [Fact]
        public void BossRetreat_NoRoll_WhenHpAboveOne()
        {
            var member = new Adventurer { Name = "撤退者", JobClass = JobClass.Ranger, STR = 1, AGI = 1, VIT = 50, MND = 1, DEX = 1, LDR = 1, INT = 1 };
            member.CurrentHP = member.MaxHP;
            var boss = new FloorBoss { Name = "強敵", Floor = 100, MaxHp = 999, CurrentHp = 999 };

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(member), boss);

            Assert.Equal(DungeonOutcome.Retreat, result.Outcome);
            Assert.Empty(result.TraitGrantEvents);
        }

        // ==================== トラウマの開示（→ CompatibilitySystem.ApplyDeathAftermath） ====================

        [Fact]
        public void Trauma_IsReturnedAsGrantEvent_WithErosion()
        {
            var fallen = WithHp(0, "倒れた仲間");
            var survivor = WithHp(40, "フィオナ");
            FillWithNormalTraits(survivor);

            var grants = new CompatibilitySystem(new AlwaysMinRng()).ApplyDeathAftermath(new GameState(), PartyOf(fallen, survivor), fallen.Id);

            var grant = Assert.Single(grants);
            Assert.Equal(TraitCatalog.TraumaId, grant.TraitId);
            Assert.Equal(TraitGrantCause.ComradeRetired, grant.Cause);
            Assert.Equal(TraitCatalog.DiligentId, grant.ErodedTraitId);
        }

        [Fact]
        public void Trauma_NotGiven_ToCoRetiredMembers()
        {
            var fallen = WithHp(0, "A");
            var alsoFallen = WithHp(0, "B");
            var survivor = WithHp(40, "C");

            var grants = new CompatibilitySystem(new AlwaysMinRng()).ApplyDeathAftermath(
                new GameState(), PartyOf(fallen, alsoFallen, survivor), fallen.Id, new[] { fallen.Id, alsoFallen.Id });

            Assert.Equal(survivor.Id, Assert.Single(grants).AdventurerId);
            Assert.False(alsoFallen.HasTrait(TraitCatalog.TraumaId));
        }

        // ==================== 週報の文面（→ TraitGrantEvent.ToLogText） ====================

        [Fact]
        public void LogText_OldWound()
        {
            var e = new TraitGrantEvent(Guid.NewGuid(), "リナ", TraitCatalog.OldWoundId, null, TraitGrantCause.CriticalInjury);

            Assert.Equal("【不可逆障害】リナ は重傷の後遺症により『古傷』を負った", e.ToLogText());
        }

        [Fact]
        public void LogText_Trauma()
        {
            var e = new TraitGrantEvent(Guid.NewGuid(), "フィオナ", TraitCatalog.TraumaId, null, TraitGrantCause.ComradeRetired);

            Assert.Equal("【精神的打撃】フィオナ は仲間除籍の衝撃により『トラウマ』を負った", e.ToLogText());
        }

        [Fact]
        public void LogText_AppendsErosion()
        {
            var e = new TraitGrantEvent(Guid.NewGuid(), "リナ", TraitCatalog.OldWoundId, TraitCatalog.DiligentId, TraitGrantCause.CriticalInjury);

            Assert.Equal("【不可逆障害】リナ は重傷の後遺症により『古傷』を負った（特性枠満杯のため『勤勉』を忘却）", e.ToLogText());
        }

        [Fact]
        public void BossAssault_Resolution_CarriesTraumaEvents_ForSurvivors()
        {
            // 週報は DungeonMissionResolution.TraitGrantEvents を1件1行で出す（→ MainDashboard）。
            // 除籍者が出たボス戦で、生存者のトラウマが解決結果へ載ることを確かめる。
            var doomed = new Adventurer { Name = "倒れる者", JobClass = JobClass.Ranger, VIT = 1 };
            doomed.CurrentHP = 1;
            var survivor = new Adventurer { Name = "生き残る者", JobClass = JobClass.Ranger, VIT = 500 };
            survivor.CurrentHP = survivor.MaxHP;

            var state = new GameState();
            state.Adventurers.AddRange(new[] { doomed, survivor });
            var field = new DungeonField { Id = "f", Name = "F", Order = 1, IsUnlocked = true, ReachedFloor = 1 };
            var boss = new FloorBoss { Name = "強敵", Floor = 100, MaxHp = 999, CurrentHp = 999 };
            field.Bosses.Add(boss);
            state.DungeonFields.Add(field);

            var system = new DungeonExpeditionSystem(
                new ScoutingResolver(new AlwaysMinRng()), new DungeonResolver(new AlwaysMaxRng()),
                new SatisfactionSystem(), new CompatibilitySystem(new AlwaysMinRng()));
            Assert.True(system.TryDispatch(state, PartyOf(doomed, survivor), boss, DungeonMissionType.BossAssault));

            var resolution = system.ProcessWeeklyMissions(state).Single();

            Assert.Contains(doomed.Id, resolution.DungeonResult!.ForceRetiredAdventurerIds);
            var trauma = Assert.Single(resolution.TraitGrantEvents, g => g.Cause == TraitGrantCause.ComradeRetired);
            Assert.Equal(survivor.Id, trauma.AdventurerId);
            Assert.Contains("生き残る者 は仲間除籍の衝撃により『トラウマ』を負った", trauma.ToLogText());
        }

        // ==================== 手動忘却（→ Adventurer.TryRemoveTrait、UIの「忘却」ボタン） ====================

        [Fact]
        public void Forget_NormalTrait_FreesSlot()
        {
            var a = WithHp(40);
            FillWithNormalTraits(a);
            Assert.False(a.CanAddTrait(TraitCatalog.NightVisionId));

            Assert.True(a.TryRemoveTrait(TraitCatalog.BraveId));

            Assert.Equal(4, a.TraitIds.Count);
            Assert.True(a.CanAddTrait(TraitCatalog.NightVisionId));
        }

        [Theory]
        [InlineData(TraitCatalog.OldWoundId)]
        [InlineData(TraitCatalog.TraumaId)]
        public void Forget_CurseTrait_IsRejected(string curseId)
        {
            var a = WithHp(40);
            a.TryAddTrait(curseId);

            Assert.False(a.CanRemoveTrait(curseId));
            Assert.False(a.TryRemoveTrait(curseId));
            Assert.Contains(curseId, a.TraitIds);
        }
    }
}
