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
    /// 相性（Compatibility）システムのテスト（仕様書 03 §5.3.1）。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class CompatibilitySystemTests
    {
        /// <summary>NextInt(min, max) が常に min を返すテスト用スタブ（＝常に確率判定に成功する）。</summary>
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        /// <summary>NextInt(min, max) が常に max を返すテスト用スタブ（＝常に確率判定に失敗する）。</summary>
        private class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members)
                party.TryAdd(m);
            return party;
        }

        // ---------------- NormalizeKey ----------------

        [Fact]
        public void NormalizeKey_IsSymmetric_RegardlessOfArgumentOrder()
        {
            var a = System.Guid.NewGuid();
            var b = System.Guid.NewGuid();

            Assert.Equal(CompatibilitySystem.NormalizeKey(a, b), CompatibilitySystem.NormalizeKey(b, a));
        }

        [Fact]
        public void NormalizeKey_OrdersSmallerGuidFirst()
        {
            var a = System.Guid.NewGuid();
            var b = System.Guid.NewGuid();
            var (first, second) = CompatibilitySystem.NormalizeKey(a, b);

            Assert.True(first.CompareTo(second) <= 0);
        }

        // ---------------- GetCompatibility / IsHostile ----------------

        [Fact]
        public void GetCompatibility_DefaultsTo50_ForUnregisteredPair()
        {
            var state = new GameState();
            var a = System.Guid.NewGuid();
            var b = System.Guid.NewGuid();

            Assert.Equal(CompatibilityBalance.InitialValue, CompatibilitySystem.GetCompatibility(state, a, b));
            Assert.Empty(state.Compatibility); // 参照しただけではエントリを作らない
        }

        [Fact]
        public void IsHostile_FalseAtDefaultValue()
        {
            var state = new GameState();
            Assert.False(CompatibilitySystem.IsHostile(state, System.Guid.NewGuid(), System.Guid.NewGuid()));
        }

        [Fact]
        public void IsHostile_TrueBelowThreshold()
        {
            var state = new GameState();
            var a = System.Guid.NewGuid();
            var b = System.Guid.NewGuid();
            state.Compatibility[CompatibilitySystem.NormalizeKey(a, b)] = CompatibilityBalance.HostileThreshold - 1;

            Assert.True(CompatibilitySystem.IsHostile(state, a, b));
        }

        [Fact]
        public void IsHostile_FalseAtThresholdItself()
        {
            var state = new GameState();
            var a = System.Guid.NewGuid();
            var b = System.Guid.NewGuid();
            state.Compatibility[CompatibilitySystem.NormalizeKey(a, b)] = CompatibilityBalance.HostileThreshold;

            Assert.False(CompatibilitySystem.IsHostile(state, a, b));
        }

        // ---------------- ApplyExpeditionOutcome（大迷宮の任務達成による相性上昇、2026年9月再配線） ----------------

        /// <summary>全員をロースターに載せた状態（生還者判定は GameState.Adventurers で行うため）。</summary>
        private static GameState StateWith(params Adventurer[] members)
        {
            var state = new GameState();
            state.Adventurers.AddRange(members);
            return state;
        }

        private static void AssertAllPairs(GameState state, int expected, params Adventurer[] members)
        {
            for (int i = 0; i < members.Length; i++)
                for (int j = i + 1; j < members.Length; j++)
                    Assert.Equal(expected, CompatibilitySystem.GetCompatibility(state, members[i].Id, members[j].Id));
        }

        [Fact]
        public void ApplyExpeditionOutcome_BossVictory_RaisesAllPairsByThree()
        {
            var a = new Adventurer(); var b = new Adventurer(); var c = new Adventurer();
            var state = StateWith(a, b, c);

            int gain = new CompatibilitySystem(new AlwaysMaxRng())
                .ApplyExpeditionOutcome(state, PartyOf(a, b, c), DungeonMissionType.BossAssault, isSuccess: true);

            Assert.Equal(3, gain);
            Assert.Equal(3, CompatibilityBalance.ExpeditionGainBossVictory);
            AssertAllPairs(state, CompatibilityBalance.InitialValue + 3, a, b, c);
        }

        [Theory]
        [InlineData(DungeonMissionType.Scouting)]
        [InlineData(DungeonMissionType.Survey)]
        [InlineData(DungeonMissionType.Gathering)]
        public void ApplyExpeditionOutcome_TraversalSurveyGathering_RaiseAllPairsByOne(DungeonMissionType missionType)
        {
            var a = new Adventurer(); var b = new Adventurer(); var c = new Adventurer();
            var state = StateWith(a, b, c);

            int gain = new CompatibilitySystem(new AlwaysMaxRng())
                .ApplyExpeditionOutcome(state, PartyOf(a, b, c), missionType, isSuccess: true);

            Assert.Equal(1, gain);
            AssertAllPairs(state, CompatibilityBalance.InitialValue + 1, a, b, c);
        }

        [Fact]
        public void ApplyExpeditionOutcome_NotSuccessful_DoesNotChangeCompatibility()
        {
            // 撤退・潰走・進軍なし・素材なし：上昇も下降もしない（旧クエストの「失敗で下降」は廃止）。
            var a = new Adventurer(); var b = new Adventurer();
            var state = StateWith(a, b);

            int gain = new CompatibilitySystem(new AlwaysMaxRng())
                .ApplyExpeditionOutcome(state, PartyOf(a, b), DungeonMissionType.Survey, isSuccess: false);

            Assert.Equal(0, gain);
            Assert.Empty(state.Compatibility);
        }

        [Fact]
        public void ApplyExpeditionOutcome_ExcludesMembersNoLongerOnRoster()
        {
            // 強制除籍でロースターを離れた者（→ FallenAdventurers）とのペアは上がらない。
            var survivorA = new Adventurer(); var survivorB = new Adventurer(); var retired = new Adventurer();
            var state = StateWith(survivorA, survivorB);

            new CompatibilitySystem(new AlwaysMaxRng())
                .ApplyExpeditionOutcome(state, PartyOf(survivorA, survivorB, retired), DungeonMissionType.BossAssault, isSuccess: true);

            Assert.Equal(CompatibilityBalance.InitialValue + 3, CompatibilitySystem.GetCompatibility(state, survivorA.Id, survivorB.Id));
            Assert.Equal(CompatibilityBalance.InitialValue, CompatibilitySystem.GetCompatibility(state, survivorA.Id, retired.Id));
        }

        [Fact]
        public void ApplyExpeditionOutcome_AppliesBeautifulMultiplier_OnGain()
        {
            var beautiful = new Adventurer();
            beautiful.TryAddTrait(TraitCatalog.BeautifulId);
            var other = new Adventurer();
            var state = StateWith(beautiful, other);

            new CompatibilitySystem(new AlwaysMaxRng())
                .ApplyExpeditionOutcome(state, PartyOf(beautiful, other), DungeonMissionType.BossAssault, isSuccess: true);

            var beautifulEffect = TraitCatalog.Beautiful.Effects[0].Value; // 1.5倍
            int expectedGain = (int)System.Math.Round(CompatibilityBalance.ExpeditionGainBossVictory * beautifulEffect);
            Assert.Equal(CompatibilityBalance.InitialValue + expectedGain,
                CompatibilitySystem.GetCompatibility(state, beautiful.Id, other.Id));
        }

        [Fact]
        public void ApplyExpeditionOutcome_ClampsAtMax100()
        {
            var a = new Adventurer(); var b = new Adventurer();
            var state = StateWith(a, b);
            state.Compatibility[CompatibilitySystem.NormalizeKey(a.Id, b.Id)] = CompatibilityBalance.MaxValue;

            new CompatibilitySystem(new AlwaysMaxRng())
                .ApplyExpeditionOutcome(state, PartyOf(a, b), DungeonMissionType.BossAssault, isSuccess: true);

            Assert.Equal(CompatibilityBalance.MaxValue, CompatibilitySystem.GetCompatibility(state, a.Id, b.Id));
        }

        // ---------------- ApplyDeathAftermath ----------------

        [Fact]
        public void ApplyDeathAftermath_DecreasesCompatibilityAmongSurvivorsOnly()
        {
            var state = new GameState();
            var fallen = new Adventurer();
            var survivorA = new Adventurer();
            var survivorB = new Adventurer();
            var party = PartyOf(fallen, survivorA, survivorB);
            var system = new CompatibilitySystem(new AlwaysMaxRng()); // トラウマ付与は失敗させておく

            system.ApplyDeathAftermath(state, party, fallen.Id);

            Assert.Equal(CompatibilityBalance.InitialValue - CompatibilityBalance.DeathWitnessLoss,
                CompatibilitySystem.GetCompatibility(state, survivorA.Id, survivorB.Id));
            // 死亡者本人が関与するペアは調整対象外（本人はロースターから除外されるため、以後は無意味だが
            // 少なくともこの呼び出し単体では他のペアに影響しない）
            Assert.DoesNotContain(state.Compatibility.Keys, k => k.Item1 == fallen.Id || k.Item2 == fallen.Id);
        }

        [Fact]
        public void ApplyDeathAftermath_GrantsTrauma_WhenRollSucceeds()
        {
            var state = new GameState();
            var fallen = new Adventurer();
            var survivor = new Adventurer();
            var party = PartyOf(fallen, survivor);
            var system = new CompatibilitySystem(new AlwaysMinRng()); // NextInt(1,100)=1 <= 30% → 必ず成功

            system.ApplyDeathAftermath(state, party, fallen.Id);

            Assert.True(survivor.HasTrait(TraitCatalog.TraumaId));
        }

        [Fact]
        public void ApplyDeathAftermath_DoesNotGrantTrauma_WhenRollFails()
        {
            var state = new GameState();
            var fallen = new Adventurer();
            var survivor = new Adventurer();
            var party = PartyOf(fallen, survivor);
            var system = new CompatibilitySystem(new AlwaysMaxRng()); // NextInt(1,100)=100 > 30% → 必ず失敗

            system.ApplyDeathAftermath(state, party, fallen.Id);

            Assert.False(survivor.HasTrait(TraitCatalog.TraumaId));
        }
    }
}
