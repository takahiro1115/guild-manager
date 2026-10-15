using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>イグニアの灼熱の底（§0.131）とイグニアの炉のテスト。</summary>
    public class IgniaTests
    {
        private sealed class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        /// <summary>STR・AGI・MND・DEX が power、VIT が vit の子（HP は hpRate だけ残す）。</summary>
        private static Adventurer Kid(string name, int power, int vit = 60, double hpRate = 1.0)
        {
            var a = new Adventurer { Name = name, JobClass = JobClass.Warrior, Age = 20, STR = power, VIT = vit, MND = power, AGI = power, DEX = power, INT = 20, LDR = 20 };
            a.CurrentHP = (int)Math.Round(a.MaxHP * hpRate);
            return a;
        }

        private static Adventurer[] Squad(string prefix, int count, int power, int vit = 60, double hpRate = 1.0) =>
            Enumerable.Range(0, count).Select(i => Kid($"{prefix}{i}", power, vit, hpRate)).ToArray();

        /// <summary>峡谷の floorCleared 階までの主を倒した状態。部隊を作り、順番どおりに出す。週は月のはじめ。</summary>
        private static GameState NewState(int floorCleared, params (string Name, Adventurer[] Members)[] squads)
        {
            var state = new GameState { WeekNumber = 5, DungeonFields = SampleData.CreateDefaultFields() };
            var canyon = state.DungeonFields.First(f => f.Id == "canyon");
            canyon.IsUnlocked = true;
            foreach (var boss in canyon.Bosses.Where(b => b.Floor <= floorCleared))
                boss.IsDefeated = true;
            foreach (var (name, members) in squads)
            {
                state.Adventurers.AddRange(members);
                state.SavedParties.Add(new SavedParty { Name = name, MemberIds = members.Select(m => m.Id).ToList() });
            }
            RulerSystem.EnsureRulers(state);
            RulerSystem.TrySetSquads(state, Ignia(state), state.SavedParties.Select(p => p.Id));
            return state;
        }

        private static RulerState Ignia(GameState s) => RulerSystem.Find(s, RulerSystem.IgniaId)!;

        private static IgniaBattle Run(IgniaBattle battle, bool use)
        {
            if (battle.Prompt != DuelPromptKind.None) battle.AutoAnswer(use); // 聞いている介入に先に答える
            while (!battle.IsOver)
            {
                battle.Advance();
                if (battle.Prompt != DuelPromptKind.None) battle.AutoAnswer(use);
            }
            battle.Finish();
            return battle;
        }

        [Fact]
        public void Ignia_WaitsAtTheCanyon70F_AndRegeneratesTwentyAMonth()
        {
            var s = NewState(60);
            var ig = Ignia(s);
            Assert.True(RulerSystem.CanChallenge(s, ig));
            Assert.True(RulerSystem.UsesSquads(ig));
            Assert.Equal("支配者イグニア", s.DungeonFields.First(f => f.Id == "canyon").Bosses.First(b => b.Floor == 70).Name);
            ig.Hp = 50;
            RulerSystem.ProcessNewWeek(s);
            Assert.Equal(50 + RulerBalance.IgniaRegenPerMonth, ig.Hp);
        }

        [Fact]
        public void SquadFit_AndHeat_FollowTheActs()
        {
            int t = IgniaBattle.Threshold(outpost: true);
            var four = Squad("前", 4, t);
            Assert.Equal(1.0, IgniaBattle.SquadFit(four, IgniaAct.Swarm, outpost: true), 3); // 合計÷（閾値×4）
            Assert.Equal(1.0, IgniaBattle.SquadFit(four.Take(1).ToList(), IgniaAct.Spell, outpost: true), 3); // 術は平均
            Assert.Equal(0.25, IgniaBattle.SquadFit(four.Take(1).ToList(), IgniaAct.Body, outpost: true), 3);
            Assert.Equal(RulerBalance.IgniaHeatPct, IgniaBattle.HeatPct(Kid("並", t, vit: t), outpost: true)); // VIT が閾値ちょうどなら 15%
            Assert.Equal(RulerBalance.IgniaHeatPctMax, IgniaBattle.HeatPct(Kid("脆い", 60, vit: 20), outpost: true));
            Assert.Equal(RulerBalance.IgniaHeatPctMin, IgniaBattle.HeatPct(Kid("硬い", 60, vit: 200), outpost: true));
        }

        [Fact]
        public void AStrongSquad_FightsAllThreeActs_AndDrivesHerBack()
        {
            var s = NewState(60, ("先鋒", Squad("先", 4, 120, vit: 90)));
            var battle = Run((IgniaBattle)RulerSystem.StartBattle(s, Ignia(s), new AlwaysMinRng())!, use: false);
            Assert.Equal(IgniaBattle.ActCount, battle.Acts.Count);
            Assert.All(battle.Acts, a => Assert.True(a.Won));
            Assert.True(battle.Retreated); // 3幕で上限20ずつ削る
            Assert.Equal("灼鱗の鎧", battle.LegendaryFound?.Name);
            Assert.Equal(3, s.Materials.GetValueOrDefault(RulerSystem.ScaleMaterialId));
        }

        [Fact]
        public void WhenTheFrontCollapses_TheNextSquadComesIn()
        {
            var s = NewState(60, ("先鋒", Squad("先", 2, 20, vit: 20, hpRate: 0.5)), ("本隊", Squad("本", 4, 70)));
            var battle = Run((IgniaBattle)RulerSystem.StartBattle(s, Ignia(s), new AlwaysMinRng())!, use: false);
            Assert.Equal("先鋒", battle.Acts[0].SquadName);
            Assert.False(battle.Acts[0].Won);
            Assert.Equal(2, battle.Acts[0].Leavers.Count);
            Assert.Equal("本隊", battle.Acts[1].SquadName);
            Assert.True(battle.Acts[1].NewFront);
        }

        [Fact]
        public void Push_LiftsTheFit_AndBurnsHarder()
        {
            var s = NewState(60, ("先鋒", Squad("先", 4, 45)));
            var battle = (IgniaBattle)RulerSystem.StartBattle(s, Ignia(s), new AlwaysMinRng())!;
            battle.Advance();
            Assert.Equal(DuelPromptKind.Push, battle.Prompt); // 0.75×1.3＝0.975 で届く
            Run(battle, use: true);
            Assert.True(battle.Acts[0].Pushed);
            Assert.True(battle.Acts[0].Won);
            Assert.Equal(IgniaBattle.HeatPct(Kid("並", 45), outpost: true, pushed: true), battle.Acts[0].HeatPct);
            Assert.False(battle.Acts[1].Won); // 介入は1回だけ
        }

        [Fact]
        public void Withdraw_EndsAHopelessBattle()
        {
            var s = NewState(60, ("先鋒", Squad("先", 4, 20)));
            var battle = (IgniaBattle)RulerSystem.StartBattle(s, Ignia(s), new AlwaysMinRng())!;
            battle.Advance();
            Assert.Equal(DuelPromptKind.Withdraw, battle.Prompt);
            Run(battle, use: true);
            Assert.True(battle.Withdrew);
            Assert.Empty(battle.Acts);
        }

        [Fact]
        public void Reserve_SwapsALeaverWithAReserve()
        {
            var s = NewState(60, ("先鋒", Squad("先", 4, 80, vit: 20, hpRate: 0.45)), ("後詰め", Squad("後", 4, 60)));
            var battle = (IgniaBattle)RulerSystem.StartBattle(s, Ignia(s), new AlwaysMinRng())!;
            battle.Advance();
            Assert.Equal(DuelPromptKind.Reserve, battle.Prompt);
            Assert.NotEmpty(battle.PromptChoices());
            Run(battle, use: true);
            Assert.NotNull(battle.Acts[0].Swap);
            Assert.StartsWith("後", battle.Acts[0].Swap!.Value.In);
        }

        [Fact]
        public void Forge_RaisesTheLowestAffixOneTier_AndCostsScales()
        {
            var s = NewState(90);
            var item = EquipmentItem.FromCatalog(ItemCatalog.FindById("IronSword")!, rarity: ItemRarity.Rare);
            item.ApplyAffix(AffixBalance.FindById("PrefixMightT1")!, 2);
            s.Armory.Add(item);
            Assert.Equal("イグニアの炉がまだ無い", ForgeSystem.BlockReason(s, item));

            foreach (var b in s.DungeonFields.First(f => f.Id == "canyon").Bosses) b.IsDefeated = true;
            s.Gold = 1000;
            s.AddMaterial(RulerSystem.ScaleMaterialId, RulerBalance.IgniaForgeScaleCost);
            Assert.True(ForgeSystem.TryForge(s, item));
            Assert.Equal("PrefixMightT2", item.PrefixId);
            Assert.Equal(ForgeSystem.UpgradedValue(2, AffixBalance.FindById("PrefixMightT2")!), item.GetAffixStatBonus("STR"));
            Assert.Equal(0, s.Materials.GetValueOrDefault(RulerSystem.ScaleMaterialId));
            Assert.Equal(1000 - RulerBalance.IgniaForgeGoldCost, s.Gold);
            Assert.NotNull(ForgeSystem.BlockReason(s, item)); // 鱗が足りない
        }
    }
}
