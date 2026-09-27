using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests.Systems
{
    /// <summary>
    /// 固有武具（固定アーティファクト・伝説級、→ UniqueItemSystem、03 §4.7.5・§0.45、ハクスラ Step 3）のテスト。
    /// 入手（鑑定での化け・ボス初回撃破）、1セーブ1本の制約、実効値と表示、ギミック対策、売却、セーブ／ロードを確かめる。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~UniqueItem`
    /// </summary>
    public class UniqueItemTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        /// <summary>用意した値を順に返し（範囲にクランプ）、使い切ったら下限を返す乱数。</summary>
        private class SequenceRng : IRng
        {
            private readonly Queue<int> _values;
            public SequenceRng(params int[] values) => _values = new Queue<int>(values);
            public int NextInt(int min, int max) => _values.Count > 0 ? Math.Clamp(_values.Dequeue(), min, max) : min;
        }

        private static UniqueDefinition Def(string id) => UniqueBalance.FindById(id)!;

        private static UnidentifiedItem MakeRelic(ItemRarity rarity) => new()
        {
            Name = "？？？ テスト用の封印箱", Rarity = rarity, OriginFieldId = "forest", OriginFloor = 30,
            AppraisalCost = RelicBalance.GetAppraisalCost(rarity),
        };

        private static Adventurer MakeAdventurer(JobClass job, int stat = 30)
        {
            var a = new Adventurer
            {
                Name = job.ToString(), Age = 18, JobClass = job, Placement = Placement.Front,
                STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat,
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        // ---------------- 個体の性能・表示 ----------------

        [Fact]
        public void FromUnique_UsesUniqueName_AndAddsBonusesOnTopOfBaseItem()
        {
            var blade = EquipmentItem.FromUnique(Def("ArtifactElvenBlade"), 5, "テスト", ItemRarity.Epic);

            Assert.True(blade.IsUnique);
            Assert.False(blade.HasAffix);
            Assert.Equal(ItemCatalog.IronSwordId, blade.ItemId);
            Assert.Equal("古代エルフの双刃", blade.DisplayName);
            // 鉄の剣（STR+2・VIT+1）＋固有（STR+5・AGI+2・DEX+5）
            Assert.Equal(7, blade.GetTotalStatBonus("STR"));
            Assert.Equal(1, blade.GetTotalStatBonus("VIT"));
            Assert.Equal(5, blade.GetTotalStatBonus("DEX"));
            Assert.Equal($"{ItemCatalog.IronSword.DescribeEffects()} [固有: STR+5・AGI+2・DEX+5]", blade.DescribeEffects());
        }

        [Fact]
        public void Legendary_DescribesGimmickCounter()
        {
            var cleaver = EquipmentItem.FromUnique(Def("LegendCaveCleaver"));

            Assert.Equal("穿岩の大剣", cleaver.DisplayName);
            Assert.EndsWith("[固有: STR+8・VIT+3] [重装甲対策]", cleaver.DescribeEffects());
            Assert.True(cleaver.CountersGimmick(BossGimmickType.HeavyArmor));
            Assert.False(cleaver.CountersGimmick(BossGimmickType.Poison));
        }

        [Fact]
        public void Equipped_Unique_RaisesEffectiveStatsAndMaxHp()
        {
            var cleric = MakeAdventurer(JobClass.Cleric);
            double mndBefore = cleric.GetEffectiveStat("MND");
            int hpBefore = cleric.MaxHP;
            var state = new GameState { Adventurers = { cleric } };
            var sigil = UniqueItemSystem.Grant(state, Def("LegendForestSigil"), "テスト")!;

            Assert.True(new EquipmentSystem().TryEquip(state, cleric, EquipmentSlot.Accessory2, sigil));

            // 守りのお守り（最大HP+15）＋固有（最大HP+20・MND+5）
            Assert.Equal(mndBefore + 5, cleric.GetEffectiveStat("MND"));
            Assert.True(cleric.MaxHP >= hpBefore + 35);
            Assert.True(cleric.CountersGimmickByEquipment(BossGimmickType.Poison));
        }

        [Fact]
        public void UnknownUniqueId_FallsBackToPlainBaseItem()
        {
            var orphan = EquipmentItem.FromCatalog(ItemCatalog.IronSword);
            orphan.UniqueId = "RemovedFromCsv";

            Assert.True(orphan.IsUnique);
            Assert.Null(orphan.GetUniqueDefinition());
            Assert.Equal("鉄の剣", orphan.DisplayName);
            Assert.Equal(2, orphan.GetTotalStatBonus("STR"));
            Assert.True(EquipmentSystem.CanSell(orphan));
            Assert.Equal(ItemCatalog.IronSword.Price / 2, EquipmentSystem.GetSellPrice(orphan));
        }

        // ---------------- ギミック対策（第4の対策口） ----------------

        [Fact]
        public void IsCountered_TrueWhenAMemberEquipsMatchingLegendary()
        {
            // アイテムしか対策口の無い即死級。ポーチは空。
            var gimmick = new BossGimmick { Type = BossGimmickType.InstantKill, RequiredItemId = ConsumableCatalog.CharmId, DangerLevel = 5 };
            var knight = MakeAdventurer(JobClass.Knight);
            var state = new GameState { Adventurers = { knight } };
            var party = new Party();
            party.TryAdd(knight);

            Assert.False(DungeonResolver.IsCountered(gimmick, party));

            var crown = UniqueItemSystem.Grant(state, Def("LegendCanyonCrown"), "テスト")!;
            Assert.True(new EquipmentSystem().TryEquip(state, knight, EquipmentSlot.Accessory1, crown));
            Assert.True(DungeonResolver.IsCountered(gimmick, party));

            // 別のギミックには効かない
            var poison = new BossGimmick { Type = BossGimmickType.Poison, RequiredCounterStat = "MND", RequiredCounterStatThreshold = 9999 };
            Assert.False(DungeonResolver.IsCountered(poison, party));
        }

        // ---------------- 入手：1セーブ1本 ----------------

        [Fact]
        public void Grant_OnlyOncePerSave_EvenAfterSelling()
        {
            var state = new GameState();
            var first = UniqueItemSystem.Grant(state, Def("ArtifactKingSeal"), "テスト");

            Assert.NotNull(first);
            Assert.Contains("ArtifactKingSeal", state.ObtainedUniqueIds);
            Assert.True(EquipmentSystem.TrySellEquipments(state, new[] { first!.Id.ToString() }, out _));
            Assert.Empty(state.Armory);

            Assert.Null(UniqueItemSystem.Grant(state, Def("ArtifactKingSeal"), "テスト"));
            Assert.Empty(state.Armory);
            Assert.DoesNotContain(UniqueItemSystem.GetAvailableArtifacts(state), d => d.Id == "ArtifactKingSeal");
        }

        // ---------------- 入手：鑑定で固定アーティファクトに化ける ----------------

        [Fact]
        public void Appraise_Epic_ArtifactRollHit_PicksFirstAvailableArtifact()
        {
            var relic = MakeRelic(ItemRarity.Epic);
            var state = new GameState { Gold = 10000, WeekNumber = 20, UnidentifiedItems = { relic } };

            // 種別ロール1＝武具 → 化け判定1（≤4%）で当選 → 抽選0＝未入手の先頭
            var result = new AppraisalSystem(new SequenceRng(1, 1, 0)).Appraise(state, relic.Id)!;

            var item = Assert.Single(state.Armory);
            Assert.True(result.IsArtifact);
            Assert.Same(item, result.ResultEquipment);
            Assert.Equal("ArtifactElvenBlade", item.UniqueId);
            Assert.Equal("古代エルフの双刃", result.ItemName);
            Assert.Equal(ItemRarity.Epic, item.Rarity);
            Assert.Equal(20, item.AcquiredAtWeek);
            Assert.False(item.HasAffix); // 固有武具にアフィックスは付かない
            Assert.Contains("古代エルフの双刃", result.FlavorText);
            Assert.Contains("ArtifactElvenBlade", state.ObtainedUniqueIds);
        }

        [Fact]
        public void Appraise_Epic_ArtifactRollMiss_FallsBackToNormalEquipment()
        {
            var relic = MakeRelic(ItemRarity.Epic);
            var state = new GameState { Gold = 10000, UnidentifiedItems = { relic } };

            // 化け判定5（＞4%）で外れ → 以後は下限＝プール先頭の通常武具
            var result = new AppraisalSystem(new SequenceRng(1, 5)).Appraise(state, relic.Id)!;

            var item = Assert.Single(state.Armory);
            Assert.False(result.IsArtifact);
            Assert.False(item.IsUnique);
            Assert.Equal(RelicBalance.Get(ItemRarity.Epic).EquipmentPool[0], item.ItemId);
            Assert.Empty(state.ObtainedUniqueIds);
        }

        [Fact]
        public void Appraise_SkipsAlreadyObtainedArtifacts()
        {
            var relic = MakeRelic(ItemRarity.Legendary);
            var state = new GameState { Gold = 10000, UnidentifiedItems = { relic } };
            state.ObtainedUniqueIds.Add("ArtifactElvenBlade");

            var result = new AppraisalSystem(new AlwaysMinRng()).Appraise(state, relic.Id)!;

            Assert.Equal("ArtifactSageBranch", Assert.Single(state.Armory).UniqueId);
            Assert.Equal(ItemRarity.Legendary, result.ResultEquipment!.Rarity);
        }

        [Fact]
        public void Appraise_Rare_NeverBecomesArtifact()
        {
            var relic = MakeRelic(ItemRarity.Rare);
            var state = new GameState { Gold = 10000, UnidentifiedItems = { relic } };

            new AppraisalSystem(new AlwaysMinRng()).Appraise(state, relic.Id);

            Assert.False(Assert.Single(state.Armory).IsUnique);
            Assert.Empty(state.ObtainedUniqueIds);
        }

        // ---------------- 入手：ボスの初回撃破で伝説級 ----------------

        private static DungeonExpeditionSystem BuildExpeditionSystem() => new(
            new ScoutingResolver(new AlwaysMinRng()),
            new DungeonResolver(new AlwaysMinRng()),
            new SatisfactionSystem(),
            new CompatibilitySystem(new AlwaysMinRng()),
            new DungeonTraversalResolver(new AlwaysMinRng()),
            new GatheringResolver(new AlwaysMinRng()));

        private static (GameState State, DungeonMissionResolution Resolution) DefeatBoss(string fieldId, int floor)
        {
            var boss = new FloorBoss { Name = "弱いボス", Floor = floor, MaxHp = 1, CurrentHp = 1 };
            var field = new DungeonField { Id = fieldId, Name = "テストの原生林", Order = 1, IsUnlocked = true, Bosses = { boss } };
            var state = new GameState { WeekNumber = 40, DungeonFields = { field } };
            var party = new Party();
            // 要求火力は階層に比例する（→ DungeonResolver.RequiredPower）。40Fでも確実に勝てる能力値にしておく。
            party.TryAdd(MakeAdventurer(JobClass.Warrior, 2000));
            party.TryAdd(MakeAdventurer(JobClass.Cleric, 2000));

            var system = BuildExpeditionSystem();
            Assert.True(system.TryDispatch(state, party, boss, DungeonMissionType.BossAssault));
            var resolution = Assert.Single(system.ProcessWeeklyMissions(state));
            Assert.Equal(DungeonOutcome.Victory, resolution.DungeonResult!.Outcome);
            return (state, resolution);
        }

        [Fact]
        public void BossVictory_AtAssignedBoss_GrantsLegendary()
        {
            var (state, resolution) = DefeatBoss("forest", 40);

            var legendary = resolution.LegendaryFound;
            Assert.NotNull(legendary);
            Assert.Equal("LegendForestSigil", legendary!.UniqueId);
            Assert.Contains(legendary, state.Armory);
            Assert.Null(legendary.Rarity);
            Assert.Equal(40, legendary.AcquiredAtWeek);
            Assert.Contains("第40層の主「弱いボス」を撃破", legendary.AcquiredFrom);
            Assert.Contains("LegendForestSigil", state.ObtainedUniqueIds);

            // 同じボスの割り当ては2度目は出ない
            Assert.Null(UniqueItemSystem.TryGrantBossDrop(state, state.DungeonFields[0], state.DungeonFields[0].Bosses[0]));
        }

        [Fact]
        public void BossVictory_WithoutAssignment_GrantsNothing()
        {
            var (state, resolution) = DefeatBoss("forest", 10);

            Assert.Null(resolution.LegendaryFound);
            Assert.DoesNotContain(state.Armory, e => e.IsUnique);
            Assert.Empty(state.ObtainedUniqueIds);
        }

        // ---------------- 売却 ----------------

        [Fact]
        public void Sell_ArtifactUsesCsvPrice_LegendaryIsRejected()
        {
            var state = new GameState { Gold = 0 };
            var artifact = UniqueItemSystem.Grant(state, Def("ArtifactWindCloak"), "テスト", ItemRarity.Legendary)!;
            var legendary = UniqueItemSystem.Grant(state, Def("LegendRuinsBow"), "テスト")!;

            Assert.Equal(1500, EquipmentSystem.GetSellPrice(artifact));
            Assert.Equal(0, EquipmentSystem.GetSellPrice(legendary));
            Assert.True(EquipmentSystem.CanSell(artifact));
            Assert.False(EquipmentSystem.CanSell(legendary));
            Assert.False(EquipmentSystem.IsBulkSellable(artifact));
            Assert.False(EquipmentSystem.IsBulkSellable(legendary));

            // 伝説級を含む売却は全か無か：何も動かない
            Assert.False(EquipmentSystem.TrySellEquipments(state, new[] { artifact.Id.ToString(), legendary.Id.ToString() }, out _));
            Assert.Equal(2, state.Armory.Count);
            Assert.Equal(0, state.Gold);

            Assert.True(EquipmentSystem.TrySellEquipments(state, new[] { artifact.Id.ToString() }, out int gold));
            Assert.Equal(1500, gold);
            Assert.Equal(1500, state.Gold);
            Assert.Same(legendary, Assert.Single(state.Armory));
        }

        [Fact]
        public void IndividuallySoldArmory_PutsLegendaryThenArtifactFirst()
        {
            var state = new GameState();
            var affixed = EquipmentItem.FromCatalog(ItemCatalog.GreatSword, rarity: ItemRarity.Legendary);
            affixed.ApplyAffix(AffixBalance.FindById("PrefixMightT3")!, 5);
            state.Armory.Add(affixed);
            var artifact = UniqueItemSystem.Grant(state, Def("ArtifactSageBranch"), "テスト", ItemRarity.Epic)!;
            var legendary = UniqueItemSystem.Grant(state, Def("LegendAbyssScepter"), "テスト")!;

            var list = EquipmentSystem.GetIndividuallySoldArmory(state);

            Assert.Equal(new[] { legendary, artifact, affixed }, list);
            Assert.Empty(EquipmentSystem.GroupArmoryForSale(state));
        }

        // ---------------- セーブ／ロード ----------------

        [Fact]
        public void RoundTrip_PreservesUniqueIdsAndObtainedSet()
        {
            var dir = Path.Combine(Path.GetTempPath(), "GuildManagerUniqueTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var warrior = MakeAdventurer(JobClass.Warrior);
                var state = new GameState { Adventurers = { warrior } };
                var cleaver = UniqueItemSystem.Grant(state, Def("LegendCaveCleaver"), "テスト")!;
                UniqueItemSystem.Grant(state, Def("ArtifactWardenShell"), "テスト", ItemRarity.Epic);
                state.ObtainedUniqueIds.Add("ArtifactKingSeal"); // 売却済み等で手元に無い入手済みId
                Assert.True(new EquipmentSystem().TryEquip(state, warrior, EquipmentSlot.Weapon, cleaver));
                double strBefore = warrior.GetEffectiveStat("STR");

                var service = new SaveLoadService(dir);
                service.Save(state);
                var loaded = service.Load()!;

                var restored = Assert.Single(loaded.Adventurers);
                Assert.Equal("LegendCaveCleaver", restored.EquippedWeapon!.UniqueId);
                Assert.Equal("穿岩の大剣", restored.EquippedWeapon.DisplayName);
                Assert.Equal(strBefore, restored.GetEffectiveStat("STR"));
                Assert.Equal("ArtifactWardenShell", Assert.Single(loaded.Armory).UniqueId);
                Assert.Equal(
                    new[] { "ArtifactKingSeal", "ArtifactWardenShell", "LegendCaveCleaver" },
                    loaded.ObtainedUniqueIds.OrderBy(id => id, StringComparer.Ordinal));
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        [Fact]
        public void OldSave_WithoutUniqueKeys_LoadsAsEmpty()
        {
            var data = new GameState { Armory = { EquipmentItem.FromCatalog(ItemCatalog.IronSword) } }.ToSaveData();
            data.ObtainedUniqueIds = null!;

            var state = GameState.FromSaveData(data);

            Assert.Empty(state.ObtainedUniqueIds);
            Assert.False(Assert.Single(state.Armory).IsUnique);
        }
    }
}
