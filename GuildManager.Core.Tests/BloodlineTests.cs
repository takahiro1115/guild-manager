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
    /// <summary>秘薬の世代・血の蓄え・近い血（§0.133）のテスト。</summary>
    public class BloodlineTests
    {
        private class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        private static readonly string[] AllStats = { "STR", "AGI", "VIT", "MND", "DEX", "LDR", "INT" };

        private static void SetPa(Adventurer a, string stat, int v) => typeof(Adventurer).GetProperty("PA_" + stat)!.SetValue(a, v);
        private static int GetPa(Adventurer a, string stat) => (int)typeof(Adventurer).GetProperty("PA_" + stat)!.GetValue(a)!;

        private static Adventurer Make(string name, int pa, params Adventurer[] parents)
        {
            var a = new Adventurer { Name = name, JobClass = JobClass.Warrior, STR = 30, AGI = 30, VIT = 30, MND = 30, DEX = 30, LDR = 30, INT = 30 };
            foreach (var s in AllStats) SetPa(a, s, pa);
            a.ParentIds = parents.Select(p => p.Id).ToList();
            a.Generation = parents.Length == 0 ? 1 : parents.Max(p => p.Generation) + 1;
            return a;
        }

        private static GameState StateOf(params Adventurer[] all)
        {
            var state = new GameState { Gold = 100000, Adventurers = all.ToList() };
            state.CompletedResearchIds.Add(ResearchIds.SoulFusion);
            return state;
        }

        [Fact]
        public void TheCapRises_WithEachGeneration_UpToTheFourth()
        {
            Assert.Equal(100, SoulFusionSystem.PaCapOf(1, breakthrough: false));
            Assert.Equal(105, SoulFusionSystem.PaCapOf(2, breakthrough: false));
            Assert.Equal(115, SoulFusionSystem.PaCapOf(4, breakthrough: false));
            Assert.Equal(115, SoulFusionSystem.PaCapOf(7, breakthrough: false));
            Assert.Equal(SoulFusionBalance.BreakthroughPaCap + 10, SoulFusionSystem.PaCapOf(3, breakthrough: true));
        }

        [Fact]
        public void AGrandchild_IsTheThirdGeneration_AndCanPassTheOldCap()
        {
            var a = Make("祖母A", 100);
            var b = Make("祖母B", 100);
            var c = Make("母C", 105, a, b);
            var d = Make("母D", 100);
            var state = StateOf(a, b, c, d);
            var child = new SoulFusionSystem(new AlwaysMaxRng()).CreateChild(state, c, d, JobClass.Warrior, null).Child;
            Assert.Equal(3, child.Generation);
            Assert.All(AllStats, s => Assert.Equal(110, GetPa(child, s))); // 104＋10 → 3世代の上限110
        }

        [Fact]
        public void Kinship_IsCloseForMotherAndDaughter_NearForHalfSistersAndCousins()
        {
            var a = Make("A", 80);
            var b = Make("B", 80);
            var x = Make("X", 80);
            var y = Make("Y", 80);
            var ab1 = Make("AB1", 80, a, b);
            var ab2 = Make("AB2", 80, a, b);
            var ax = Make("AX", 80, a, x);
            var by = Make("BY", 80, b, y);
            var cousinA = Make("いとこA", 80, ab1, x);
            var cousinB = Make("いとこB", 80, ab2, y);
            var state = StateOf(a, b, x, y, ab1, ab2, ax, by, cousinA, cousinB);
            Assert.Equal(Kinship.Close, SoulFusionSystem.KinshipOf(state, a, ab1)); // 親子
            Assert.Equal(Kinship.Close, SoulFusionSystem.KinshipOf(state, ab1, ab2)); // 同じ両親の姉妹
            Assert.Equal(Kinship.Near, SoulFusionSystem.KinshipOf(state, ab1, ax)); // 片親が同じ
            Assert.Equal(Kinship.Near, SoulFusionSystem.KinshipOf(state, a, cousinA)); // 祖母と孫
            Assert.Equal(Kinship.Near, SoulFusionSystem.KinshipOf(state, cousinA, cousinB)); // いとこ
            Assert.Equal(Kinship.None, SoulFusionSystem.KinshipOf(state, ax, by));
        }

        [Fact]
        public void CloseBlood_LowersTheDaughter()
        {
            var a = Make("A", 80);
            var b = Make("B", 80);
            var ab = Make("AB", 80, a, b);
            var state = StateOf(a, b, ab);
            var child = new SoulFusionSystem(new AlwaysMaxRng()).CreateChild(state, a, ab, JobClass.Warrior, null).Child;
            Assert.All(AllStats, s => Assert.Equal(80 + SoulFusionBalance.PaVarianceMax - SoulFusionBalance.InbreedPenaltyClose, GetPa(child, s)));
        }

        [Fact]
        public void AParent_CanBeAParentAgain_WithAnotherPartner()
        {
            var a = Make("A", 80);
            var b = Make("B", 80);
            var c = Make("C", 80);
            var state = StateOf(a, b, c);
            state.Compatibility[CompatibilitySystem.NormalizeKey(a.Id, b.Id)] = 100;
            state.Compatibility[CompatibilitySystem.NormalizeKey(a.Id, c.Id)] = 100;
            var sf = new SoulFusionSystem(new AlwaysMaxRng());
            Assert.NotNull(sf.TryPrescribe(state, a, b, JobClass.Warrior));
            state.SoulFusionCultures.Clear(); // 培養槽を空ける
            Assert.NotNull(sf.TryPrescribe(state, a, c, JobClass.Warrior));
        }

        [Fact]
        public void Generations_AreRecomputed_ForOldSaves()
        {
            var a = Make("A", 80);
            var b = Make("B", 80);
            var ab = Make("AB", 80, a, b);
            var abc = Make("ABC", 80, ab, a);
            var state = StateOf(ab, abc);
            state.RetiredAdventurers.Add(a);
            state.RetiredAdventurers.Add(b);
            ab.Generation = 1; abc.Generation = 1; // 旧セーブは1で読まれる
            var loaded = GameState.FromSaveData(state.ToSaveData());
            Assert.Equal(2, loaded.Adventurers.First(x => x.Name == "AB").Generation);
            Assert.Equal(3, loaded.Adventurers.First(x => x.Name == "ABC").Generation);
        }

        [Fact]
        public void Elixirs_StopAtTheGenerationCap()
        {
            var a = Make("三世代", 108);
            a.Generation = 3;
            Assert.Equal(110, SoulFusionSystem.PaCapOf(a, "STR"));
        }
    }
}
