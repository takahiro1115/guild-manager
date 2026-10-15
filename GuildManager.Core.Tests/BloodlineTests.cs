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
        public void TheCapRises_WithEachGeneration_UpToTheMaxGeneration()
        {
            int Cap(int gen) => SoulFusionBalance.GenerationBasePaCap + SoulFusionBalance.GenerationPaCapStep * (gen - 1);
            int max = SoulFusionBalance.GenerationPaCapMaxGen;
            Assert.Equal(SoulFusionBalance.GenerationBasePaCap, SoulFusionSystem.PaCapOf(1, breakthrough: false));
            Assert.Equal(Cap(2), SoulFusionSystem.PaCapOf(2, breakthrough: false));
            Assert.True(SoulFusionSystem.PaCapOf(2, false) > SoulFusionSystem.PaCapOf(1, false));
            Assert.Equal(Cap(max), SoulFusionSystem.PaCapOf(max, breakthrough: false));
            Assert.Equal(Cap(max), SoulFusionSystem.PaCapOf(max + 3, breakthrough: false)); // それより先の世代は伸びない
            Assert.Equal(Cap(3) + SoulFusionBalance.BreakthroughPaCap - SoulFusionBalance.NormalPaCap, SoulFusionSystem.PaCapOf(3, breakthrough: true));
        }

        [Fact]
        public void AGrandchild_IsTheThirdGeneration_AndCanPassTheOldCap()
        {
            var a = Make("祖母A", 100);
            var b = Make("祖母B", 100);
            var c = Make("母C", 110, a, b);
            var d = Make("母D", 110);
            var state = StateOf(a, b, c, d);
            var child = new SoulFusionSystem(new AlwaysMaxRng()).CreateChild(state, c, d, JobClass.Warrior, null).Child;
            Assert.Equal(3, child.Generation);
            // 3世代の上限＋上限のずれ（常に最大の乱数なので+PaCapSpread）で止まる。2世代の上限は超える
            Assert.All(AllStats, s => Assert.Equal(SoulFusionSystem.PaCapOf(3, false) + SoulFusionBalance.PaCapSpread, GetPa(child, s)));
            Assert.All(AllStats, s => Assert.True(GetPa(child, s) > SoulFusionSystem.PaCapOf(2, false) + SoulFusionBalance.PaCapSpread));
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
            var a = Make("三世代", 50);
            a.Generation = 3;
            Assert.Equal(SoulFusionSystem.PaCapOf(3, false), SoulFusionSystem.PaCapOf(a, "STR")); // ずれを持たない子はずれ0
            a.PaCapOffsets["STR"] = -3;
            a.PaCapOffsets["AGI"] = 4;
            Assert.Equal(SoulFusionSystem.PaCapOf(3, false) - 3, SoulFusionSystem.PaCapOf(a, "STR")); // 上限のずれ（§0.135）
            Assert.Equal(SoulFusionSystem.PaCapOf(3, false) + 4, SoulFusionSystem.PaCapOf(a, "AGI"));
            // 個人の欄に出す幅（限界突破した能力は除く）
            Assert.Equal((SoulFusionSystem.PaCapOf(3, false) - 3, SoulFusionSystem.PaCapOf(3, false) + 4), SoulFusionSystem.PaCapRangeOf(a));
            a.FusionBreakthroughStats = new List<string> { "AGI" };
            Assert.Equal((SoulFusionSystem.PaCapOf(3, false) - 3, SoulFusionSystem.PaCapOf(3, false)), SoulFusionSystem.PaCapRangeOf(a));
        }

        // ---------------- 子ごと・能力ごとの上限のずれ（§0.135） ----------------

        [Fact]
        public void Recruits_TopPa_VariesAroundTheFirstGenerationCap_NotAllTheSame()
        {
            int spread = SoulFusionBalance.PaCapSpread, baseCap = SoulFusionBalance.GenerationBasePaCap;
            var candidates = Enumerable.Range(1, 40)
                .SelectMany(seed => new RecruitmentSystem(new SeededRng(seed)).GenerateCandidates(new GameState(), scoutMasterBonus: 1.0, candidateCount: 8))
                .Select(r => r.Candidate).ToList();
            foreach (var c in candidates)
                foreach (var s in AllStats)
                {
                    Assert.InRange(SoulFusionSystem.PaCapOffsetOf(c, s), -spread, spread);
                    Assert.InRange(GetPa(c, s), 1, SoulFusionSystem.PaCapOf(c, s)); // その子のその能力の上限は超えない
                }
            // 有望な新人の素質は、1世代の上限ちょうどにそろわず、上下に散らばる
            var top = candidates.SelectMany(c => AllStats.Select(s => GetPa(c, s))).Where(pa => pa >= baseCap - spread).ToList();
            Assert.Contains(top, pa => pa > baseCap);
            Assert.Contains(top, pa => pa < baseCap);
            Assert.True(top.Distinct().Count() >= spread, "素質の上のほうが同じ値にそろっている");
        }

        [Fact]
        public void Starters_HaveTheSameCapOffsets_EveryNewGame()
        {
            var first = Data.SampleData.CreateStarterAdventurers();
            var second = Data.SampleData.CreateStarterAdventurers();
            for (int i = 0; i < first.Count; i++)
                foreach (var s in AllStats)
                {
                    Assert.Equal(SoulFusionSystem.PaCapOffsetOf(first[i], s), SoulFusionSystem.PaCapOffsetOf(second[i], s));
                    Assert.Equal(GetPa(first[i], s), GetPa(second[i], s));
                    Assert.InRange(SoulFusionSystem.PaCapOffsetOf(first[i], s), -SoulFusionBalance.PaCapSpread, SoulFusionBalance.PaCapSpread);
                    Assert.InRange(GetPa(first[i], s), 1, SoulFusionSystem.PaCapOf(first[i], s));
                }
            Assert.True(first.SelectMany(a => AllStats.Select(s => SoulFusionSystem.PaCapOffsetOf(a, s))).Distinct().Count() > 1);
        }

        [Fact]
        public void CapOffsets_AreKeptBySave_AndFilledForOldSaves()
        {
            var kept = Make("今のセーブ", 50);
            kept.PaCapOffsets["STR"] = -4;
            var old = Make("旧セーブ", 50); // 上限のずれを持たない
            var state = StateOf(kept, old);

            var json = System.Text.Json.JsonSerializer.Serialize(state.ToSaveData());
            var loaded = GameState.FromSaveData(System.Text.Json.JsonSerializer.Deserialize<SaveData>(json)!);
            Assert.Equal(-4, SoulFusionSystem.PaCapOffsetOf(loaded.Adventurers.First(x => x.Name == "今のセーブ"), "STR"));

            var filled = loaded.Adventurers.First(x => x.Name == "旧セーブ");
            Assert.Equal(AllStats.Length, filled.PaCapOffsets.Count);
            Assert.All(AllStats, s => Assert.InRange(SoulFusionSystem.PaCapOffsetOf(filled, s), -SoulFusionBalance.PaCapSpread, SoulFusionBalance.PaCapSpread));

            // 補ったずれは Id から決めるので、もう一度旧セーブを読んでも同じになる
            var again = GameState.FromSaveData(System.Text.Json.JsonSerializer.Deserialize<SaveData>(json)!).Adventurers.First(x => x.Name == "旧セーブ");
            Assert.All(AllStats, s => Assert.Equal(SoulFusionSystem.PaCapOffsetOf(filled, s), SoulFusionSystem.PaCapOffsetOf(again, s)));
        }

        // ---------------- 親の名前（退団した親） ----------------

        [Fact]
        public void ParentNames_AreKept_WhenAParentLeavesTheGuild()
        {
            var a = Make("母A", 50);
            var b = Make("母B", 50);
            var state = StateOf(a, b);
            var child = new SoulFusionSystem(new AlwaysMaxRng()).CreateChild(state, a, b, JobClass.Warrior, null).Child;
            Assert.Equal(new[] { "母A", "母B" }, child.ParentNames);
            state.Adventurers.Add(child);

            state.Adventurers.Remove(b); // 退団（記録から消える）
            Assert.Equal("母A", SoulFusionSystem.ParentName(state, child, 0));
            Assert.Equal("母B", SoulFusionSystem.ParentName(state, child, 1)); // 生まれたときの名前で
            a.Name = "改名した母A";
            Assert.Equal("改名した母A", SoulFusionSystem.ParentName(state, child, 0)); // いる親はいまの名前

            // 旧セーブ（親の名前なし）：見つかる親だけ補い、見つからない親は「？」
            child.ParentNames.Clear();
            var loaded = GameState.FromSaveData(state.ToSaveData());
            var lc = loaded.Adventurers.First(x => x.Id == child.Id);
            Assert.Equal("改名した母A", lc.ParentNames[0]);
            Assert.Equal("？", SoulFusionSystem.ParentName(loaded, lc, 1));
        }
    }
}
