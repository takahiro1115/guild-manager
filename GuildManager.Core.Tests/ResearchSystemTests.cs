using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// アルベールの研究室（素材投資システム、→ ResearchSystem・ResearchBalance）のテスト。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~Research`
    /// </summary>
    public class ResearchSystemTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private static Adventurer MakeAdventurer(int agiDex, int ldr = 0)
        {
            var a = new Adventurer { STR = 10, AGI = agiDex, VIT = 30, MND = 10, DEX = agiDex, LDR = ldr, INT = 10 };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            return party;
        }

        // ---------------- research.csv・ResearchBalance ----------------

        [Fact]
        public void ResearchBalance_LoadsAllFourDefinitions_WithMaterialsParsed()
        {
            var all = ResearchBalance.GetAll();

            Assert.Equal(4, all.Count);
            Assert.All(all, r => Assert.NotEmpty(r.RequiredMaterials));
            Assert.All(all, r => Assert.True(r.RequiredGold > 0));

            var scoutReagent = ResearchBalance.Find(ResearchIds.ScoutReagent);
            Assert.NotNull(scoutReagent);
            Assert.Equal(ResearchEffectType.IntelRateBonus, scoutReagent!.EffectType);
            Assert.True(scoutReagent.RequiredMaterials.ContainsKey(MaterialCatalog.HerbMoonlightId));

            Assert.Equal(ResearchEffectType.GatheringYieldBonus, ResearchBalance.Find(ResearchIds.GatheringBag)!.EffectType);
            Assert.Equal(ResearchEffectType.TraversalBonus, ResearchBalance.Find(ResearchIds.LightTread)!.EffectType);
            Assert.Equal(ResearchEffectType.HpRecoveryBonus, ResearchBalance.Find(ResearchIds.InfirmaryElixir)!.EffectType);
        }

        // ---------------- ResearchSystem ----------------

        [Fact]
        public void ResearchSystem_CompleteResearch_DeductsResourcesAndMarksComplete()
        {
            var research = ResearchBalance.Find(ResearchIds.ScoutReagent)!;
            var state = new GameState { Gold = research.RequiredGold + 100 };
            foreach (var (materialId, count) in research.RequiredMaterials)
                state.AddMaterial(materialId, count + 2); // 少し余分に持たせる

            Assert.True(ResearchSystem.CanStartResearch(state, research));
            Assert.False(state.IsResearchCompleted(research.Id));

            Assert.True(ResearchSystem.CompleteResearch(state, research));

            Assert.True(state.IsResearchCompleted(research.Id));
            Assert.Contains(research.Id, state.CompletedResearchIds);
            Assert.Equal(100, state.Gold); // RequiredGold分が引かれ、余剰の100だけ残る
            foreach (var (materialId, count) in research.RequiredMaterials)
                Assert.Equal(2, state.Materials[materialId]); // 消費後、余分に持たせた2だけ残る

            // 完了済みの研究はもう一度実行できない（CanStartResearchがfalseを返す）。
            Assert.False(ResearchSystem.CanStartResearch(state, research));
            Assert.False(ResearchSystem.CompleteResearch(state, research));
        }

        [Fact]
        public void ResearchSystem_CannotResearch_WhenResourcesInsufficient()
        {
            var research = ResearchBalance.Find(ResearchIds.GatheringBag)!;

            // ①ゴールド不足（素材は十分）
            var goldShort = new GameState { Gold = research.RequiredGold - 1 };
            foreach (var (materialId, count) in research.RequiredMaterials)
                goldShort.AddMaterial(materialId, count);
            Assert.False(ResearchSystem.CanStartResearch(goldShort, research));
            Assert.False(ResearchSystem.CompleteResearch(goldShort, research));
            Assert.Equal(research.RequiredGold - 1, goldShort.Gold); // 失敗時は減算されない

            // ②素材不足（ゴールドは十分）
            var materialShort = new GameState { Gold = research.RequiredGold };
            var firstMaterial = research.RequiredMaterials.First();
            materialShort.AddMaterial(firstMaterial.Key, firstMaterial.Value - 1);
            Assert.False(ResearchSystem.CanStartResearch(materialShort, research));
            Assert.False(ResearchSystem.CompleteResearch(materialShort, research));

            // ③何も持っていない
            var nothing = new GameState { Gold = 0 };
            Assert.False(ResearchSystem.CanStartResearch(nothing, research));
        }

        // ---------------- 研究バフの適用（各Resolverへの参照追加） ----------------

        [Fact]
        public void ScoutingResolver_AppliesResearchBonus()
        {
            var boss = new FloorBoss { Name = "テスト用ボス", Floor = 1, MaxHp = 500, CurrentHp = 500 };
            var party = PartyOf(MakeAdventurer(agiDex: 100, ldr: 50));

            // 研究無し
            var withoutResearch = new ScoutingResolver(new AlwaysMinRng())
                .Resolve(party, new FloorBoss { Name = boss.Name, Floor = boss.Floor, MaxHp = boss.MaxHp, CurrentHp = boss.CurrentHp }, state: null);

            // 研究あり（秘薬の斥候試薬 完了済み）
            var state = new GameState();
            state.CompletedResearchIds.Add(ResearchIds.ScoutReagent);
            var withResearch = new ScoutingResolver(new AlwaysMinRng())
                .Resolve(PartyOf(MakeAdventurer(agiDex: 100, ldr: 50)), new FloorBoss { Name = boss.Name, Floor = boss.Floor, MaxHp = boss.MaxHp, CurrentHp = boss.CurrentHp }, state);

            Assert.True(withResearch.IntelGained > withoutResearch.IntelGained,
                "研究完了済みなら解析率獲得量が増えるはず（→ IntelRateBonus）");
        }

        [Fact]
        public void GatheringResolver_AppliesResearchBonus()
        {
            var field = new DungeonField { Id = "forest", Name = "森", Order = 1, IsUnlocked = true, ReachedFloor = 1 };

            var withoutResearch = new GatheringResolver(new AlwaysMinRng())
                .Resolve(PartyOf(MakeAdventurer(agiDex: 40, ldr: 20)), field, state: null);

            var state = new GameState();
            state.CompletedResearchIds.Add(ResearchIds.GatheringBag);
            var withResearch = new GatheringResolver(new AlwaysMinRng())
                .Resolve(PartyOf(MakeAdventurer(agiDex: 40, ldr: 20)), field, state);

            Assert.True(withResearch.MaterialCount > withoutResearch.MaterialCount,
                "研究完了済みなら採取素材の獲得数が増えるはず（→ GatheringYieldBonus）");
            Assert.Equal(withoutResearch.MaterialCount + 1, withResearch.MaterialCount); // res_gathering_bagは+1固定
        }

        [Fact]
        public void DungeonTraversalResolver_AppliesResearchBonus_ToScoreAndForecast()
        {
            var party = PartyOf(MakeAdventurer(agiDex: 40, ldr: 20));

            double scoreWithoutResearch = DungeonTraversalResolver.CalculateTraversalScore(party, state: null);

            var state = new GameState();
            state.CompletedResearchIds.Add(ResearchIds.LightTread);
            double scoreWithResearch = DungeonTraversalResolver.CalculateTraversalScore(party, state);

            var research = ResearchBalance.Find(ResearchIds.LightTread)!;
            Assert.Equal(scoreWithoutResearch + research.EffectValue, scoreWithResearch, precision: 6);
        }

        [Fact]
        public void RestRecoverySystem_AppliesResearchBonus_ToHpRecovery()
        {
            var injured = new Adventurer { VIT = 50 };
            injured.CurrentHP = 1;
            var stateWithout = new GameState { Adventurers = { injured } };
            new RestRecoverySystem().ProcessWeeklyRest(stateWithout, new System.Collections.Generic.HashSet<System.Guid>());
            int recoveryWithout = injured.CurrentHP - 1;

            var injured2 = new Adventurer { VIT = 50 };
            injured2.CurrentHP = 1;
            var stateWith = new GameState { Adventurers = { injured2 } };
            stateWith.CompletedResearchIds.Add(ResearchIds.InfirmaryElixir);
            new RestRecoverySystem().ProcessWeeklyRest(stateWith, new System.Collections.Generic.HashSet<System.Guid>());
            int recoveryWith = injured2.CurrentHP - 1;

            Assert.True(recoveryWith > recoveryWithout, "研究完了済みなら静養時のHP回復量が増えるはず（→ HpRecoveryBonus）");
        }
    }
}
