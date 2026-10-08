using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>霊薬を飲ませられるかの判定結果（→ ElixirSystem.Check）。Ok 以外は飲ませられない理由。</summary>
    public enum ElixirCheck
    {
        Ok,
        /// <summary>研究「霊薬の調合法」が済んでいない。</summary>
        NotUnlocked,
        /// <summary>知らない霊薬のId。</summary>
        UnknownElixir,
        /// <summary>現役ロースターにいない（引退者・除籍者には飲ませない）。</summary>
        NotInRoster,
        /// <summary>出撃中。</summary>
        Dispatched,
        /// <summary>もう上限まで飲んだ（→ elixir.csv MaxPerAdventurer）。</summary>
        LimitReached,
        NotEnoughGold,
        NotEnoughMaterials,
    }

    /// <summary>
    /// 霊薬（能力を伸ばす秘薬。→ 03 §4.6.2・§0.61、2026年10月新設）。研究「霊薬の調合法」で解禁し、研究室で素材とゴールドから
    /// 調合してその場で冒険者に飲ませる。対象の能力の PA（伸びしろ）が恒久的に上がり、実効値も少し上がる。
    /// 1人が飲めるのは生涯 MaxPerAdventurer 本まで（→ Adventurer.ElixirsTaken）。中盤以降に余るお金と素材の使い道。
    /// PA は通常100で止め、能力限界突破（→ §5.4）で100を超えている能力は BreakthroughPaCap（120）まで上がる。
    /// 実効値は上がった後の PA を超えない。乱数は使わない。
    /// </summary>
    public static class ElixirSystem
    {
        public static bool IsUnlocked(GameState state) => state.IsResearchCompleted(ResearchIds.ElixirBrewing);

        public static ElixirCheck Check(GameState state, Adventurer adventurer, string elixirId)
        {
            if (!IsUnlocked(state)) return ElixirCheck.NotUnlocked;
            var recipe = ElixirBalance.Find(elixirId);
            if (recipe == null) return ElixirCheck.UnknownElixir;
            if (!state.Adventurers.Contains(adventurer) || adventurer.IsRetired) return ElixirCheck.NotInRoster;
            if (adventurer.IsDispatched || adventurer.IsOnLoan) return ElixirCheck.Dispatched; // 派遣中（§0.85）も飲ませられない
            if (adventurer.ElixirsTaken >= ElixirBalance.MaxPerAdventurer) return ElixirCheck.LimitReached;
            if (state.Gold < recipe.RequiredGold) return ElixirCheck.NotEnoughGold;
            foreach (var (materialId, count) in recipe.RequiredMaterials)
                if ((state.Materials.TryGetValue(materialId, out int have) ? have : 0) < count)
                    return ElixirCheck.NotEnoughMaterials;
            return ElixirCheck.Ok;
        }

        /// <summary>霊薬を調合して飲ませる。Check が Ok でなければ何もせず false。</summary>
        public static bool TryGive(GameState state, Adventurer adventurer, string elixirId)
        {
            if (Check(state, adventurer, elixirId) != ElixirCheck.Ok)
                return false;

            var recipe = ElixirBalance.Find(elixirId)!;
            state.Gold -= recipe.RequiredGold;
            foreach (var (materialId, count) in recipe.RequiredMaterials)
                state.Materials[materialId] -= count;

            foreach (var stat in recipe.TargetStats)
            {
                int pa = AdventurerStatAccessor.GetPa(adventurer, stat);
                int cap = pa > SoulFusionBalance.NormalPaCap ? SoulFusionBalance.BreakthroughPaCap : SoulFusionBalance.NormalPaCap;
                int newPa = Math.Max(pa, Math.Min(cap, pa + recipe.PaBonus));
                AdventurerStatAccessor.SetPa(adventurer, stat, newPa);
                int current = AdventurerStatAccessor.GetStat(adventurer, stat);
                AdventurerStatAccessor.SetStat(adventurer, stat, Math.Max(current, Math.Min(newPa, current + recipe.StatBonus)));
            }
            adventurer.ElixirsTaken++;
            return true;
        }
    }
}
