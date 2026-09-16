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

        // ---------------- ギミック対策の判定（OR条件） ----------------

        [Fact]
        public void IsCountered_ByRole()
        {
            var gimmick = new BossGimmick { Type = BossGimmickType.Poison, RequiredCounterRole = JobClass.Cleric, DangerLevel = 2 };

            Assert.True(DungeonResolver.IsCountered(gimmick, PartyOf(MakeRanger(), MakeCleric())));
            Assert.False(DungeonResolver.IsCountered(gimmick, PartyOf(MakeRanger(), MakeRanger())));
        }

        [Fact]
        public void IsCountered_ByStatThreshold()
        {
            var gimmick = new BossGimmick
            {
                Type = BossGimmickType.Poison, RequiredCounterStat = "MND", RequiredCounterStatThreshold = 100, DangerLevel = 2,
            };

            Assert.True(DungeonResolver.IsCountered(gimmick, PartyOf(MakeRanger(60), MakeRanger(60))));  // 合算120
            Assert.False(DungeonResolver.IsCountered(gimmick, PartyOf(MakeRanger(40))));                 // 合算40
        }

        [Fact]
        public void IsCountered_ByCarriedItem()
        {
            var gimmick = new BossGimmick
            {
                Type = BossGimmickType.Poison, RequiredItemId = ConsumableCatalog.AntidoteId, DangerLevel = 2,
            };

            var withItem = PartyOf(MakeRanger());
            withItem.TryAddConsumable(ConsumableCatalog.AntidoteId);

            Assert.True(DungeonResolver.IsCountered(gimmick, withItem));
            Assert.False(DungeonResolver.IsCountered(gimmick, PartyOf(MakeRanger())));
        }

        [Fact]
        public void IsCountered_ReturnsFalse_WhenGimmickHasNoCounterPortDefined()
        {
            // 対策口が1つも設定されていないギミックは「対策不能」として常に未対策にする
            // （ボス定義側の設定漏れを黙って握り潰さないため）。
            var malformed = new BossGimmick { Type = BossGimmickType.Flying, DangerLevel = 1 };

            Assert.False(DungeonResolver.IsCountered(malformed, PartyOf(MakeRanger(90), MakeCleric(90))));
        }

        // ---------------- 火力判定（撃破／撤退） ----------------

        [Fact]
        public void Resolve_Victory_WhenPartyPowerMeetsRequirement()
        {
            var boss = MakeBoss(floor: 1);
            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(MakeRanger()), boss);

            Assert.Equal(DungeonOutcome.Victory, result.Outcome);
            Assert.True(boss.IsDefeated);
            Assert.Equal(0, boss.CurrentHp);
        }

        [Fact]
        public void Resolve_Retreat_WhenPartyPowerIsInsufficient()
        {
            var boss = MakeBoss(floor: 10); // 要求火力＝450
            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(MakeRanger(10)), boss);

            Assert.Equal(DungeonOutcome.Retreat, result.Outcome);
            Assert.False(boss.IsDefeated);
            Assert.True(boss.CurrentHp > 0, "撤退ではボスのHPは削り切られない（次回は仕切り直し）");
        }

        // ---------------- 完全解析ボーナス（調査との連動） ----------------

        [Fact]
        public void Resolve_FullIntel_AddsDamageBonus_AndCanFlipRetreatIntoVictory()
        {
            // 斥候（配置補正1.0）全能力50 → 素の火力は 50×4.2＝210。
            // 階層5の要求火力は225のため素では届かないが、完全解析の+20%（252）で覆る。
            var withoutIntel = MakeBoss(floor: 5, intelRate: 0.0);
            var withIntel = MakeBoss(floor: 5, intelRate: 1.0);
            var resolver = new DungeonResolver(new AlwaysMinRng());

            var retreat = resolver.Resolve(PartyOf(MakeRanger()), withoutIntel);
            var victory = resolver.Resolve(PartyOf(MakeRanger()), withIntel);

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

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(MakeRanger()), boss);

            Assert.False(result.FullIntelBonusApplied);
        }

        // ---------------- 未対策ペナルティ ----------------

        [Fact]
        public void Resolve_UncounteredGimmick_IncreasesDamageMultiplier()
        {
            var gimmick = new BossGimmick
            {
                Type = BossGimmickType.HeavyArmor, RequiredCounterStat = "STR", RequiredCounterStatThreshold = 500, DangerLevel = 2,
            };
            var boss = MakeBoss(floor: 1, intelRate: 0.0, gimmick);

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(MakeRanger()), boss);

            Assert.Contains(BossGimmickType.HeavyArmor, result.UncounteredGimmicks);
            // 1.0 + 危険度2 × 0.75 = 2.5倍
            Assert.Equal(1.0 + 2 * DungeonBalance.UncounteredDamageMultiplierPerDangerLevel, result.DamageMultiplier, precision: 10);
        }

        [Fact]
        public void Resolve_AllGimmicksCountered_KeepsDamageMultiplierAtOne()
        {
            var gimmick = new BossGimmick
            {
                Type = BossGimmickType.Poison, RequiredCounterRole = JobClass.Cleric, DangerLevel = 3,
            };
            var boss = MakeBoss(floor: 1, intelRate: 0.0, gimmick);

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(MakeRanger(), MakeCleric()), boss);

            Assert.Contains(BossGimmickType.Poison, result.CounteredGimmicks);
            Assert.Empty(result.UncounteredGimmicks);
            Assert.Equal(1.0, result.DamageMultiplier, precision: 10);
        }

        [Fact]
        public void Resolve_UncounteredGimmick_CausesHeavierHpLoss()
        {
            var counterable = new BossGimmick
            {
                Type = BossGimmickType.Poison, RequiredCounterRole = JobClass.Cleric, DangerLevel = 2,
            };
            var resolver = new DungeonResolver(new AlwaysMaxRng());

            var prepared = MakeRanger();
            var unprepared = MakeRanger();

            resolver.Resolve(PartyOf(prepared, MakeCleric()), MakeBoss(1, 0.0, counterable));
            var reckless = resolver.Resolve(PartyOf(unprepared), MakeBoss(1, 0.0, counterable));

            Assert.True(reckless.HpLostByAdventurer[unprepared.Id] > 0);
            Assert.True(unprepared.CurrentHP < prepared.CurrentHP,
                "未対策で挑んだ側の方がHPを大きく失うはず");
        }

        // ---------------- 即死級ギミックと強制除籍（ロスト） ----------------

        [Fact]
        public void Resolve_UncounteredInstantKill_ForceRetiresEveryone()
        {
            // 無調査での突撃は壊滅する：即死級を未対策で踏むとHPを全損し、強制除籍になる。
            var doomed = MakeRanger();
            var boss = MakeBoss(floor: 1, intelRate: 0.0, new BossGimmick
            {
                Type = BossGimmickType.InstantKill, RequiredItemId = ConsumableCatalog.CharmId, DangerLevel = 5,
            });

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(doomed), boss);

            Assert.Contains(BossGimmickType.InstantKill, result.UncounteredGimmicks);
            Assert.Equal(0, doomed.CurrentHP);
            Assert.Contains(doomed.Id, result.ForceRetiredAdventurerIds);
        }

        [Fact]
        public void Resolve_CounteredInstantKill_LeavesPartyAlive()
        {
            // 対策アイテムを携行していれば即死級を無効化でき、全員生還する。
            var survivor = MakeRanger();
            var party = PartyOf(survivor);
            party.TryAddConsumable(ConsumableCatalog.CharmId);

            var boss = MakeBoss(floor: 1, intelRate: 0.0, new BossGimmick
            {
                Type = BossGimmickType.InstantKill, RequiredItemId = ConsumableCatalog.CharmId, DangerLevel = 5,
            });

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(party, boss);

            Assert.Contains(BossGimmickType.InstantKill, result.CounteredGimmicks);
            Assert.True(survivor.CurrentHP > 0);
            Assert.Empty(result.ForceRetiredAdventurerIds);
        }

        // ---------------- 携行アイテムの消費 ----------------

        [Fact]
        public void Resolve_ConsumesCarriedItems()
        {
            var party = PartyOf(MakeRanger());
            party.TryAddConsumable(ConsumableCatalog.CharmId);

            new DungeonResolver(new AlwaysMinRng()).Resolve(party, MakeBoss(floor: 1));

            Assert.Empty(party.ConsumableItemIds);
        }

        [Fact]
        public void Resolve_ThrowsForEmptyParty()
        {
            Assert.Throws<InvalidOperationException>(() =>
                new DungeonResolver(new AlwaysMinRng()).Resolve(new Party(), MakeBoss()));
        }
    }
}
