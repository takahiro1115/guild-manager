using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>働き手1人の、その月の売上。</summary>
    public sealed record ShopSale(ShopKind Kind, Guid WorkerId, string WorkerName, int Gold);

    /// <summary>
    /// ギルドの店（道具屋・酒場、§0.134）。引退した子を働き手にして、毎月の売上をギルドに入れる。
    /// 働き手は教官・顧問と同じく役職の1つで、ギルドに残り（→ GuildPolicySystem.AppointedIds）、手当を受け取り（→ GuildFinanceSystem.PaidRetirees）、
    /// 秘薬の親にもなれる（→ SoulFusionSystem.GuildMembers）。1人が就ける役職は1つ（→ AdvisorSystem）。
    /// 売上は4週ごと（アルベールの内職と同じ週）に入る。
    /// </summary>
    public static class GuildShopSystem
    {
        /// <summary>店の名前。</summary>
        public static string Label(ShopKind kind) => kind == ShopKind.ItemShop ? "道具屋" : "酒場";

        /// <summary>働ける引退者の数（道具屋は ItemShopSlots、酒場は酒場のLv×TavernSlotsPerLevel）。</summary>
        public static int Slots(GameState state, ShopKind kind) => kind == ShopKind.ItemShop
            ? ShopBalance.ItemShopSlots
            : state.GetFacilityLevel(FacilityType.Tavern) * ShopBalance.TavernSlotsPerLevel;

        /// <summary>その店の働き手の Id。</summary>
        public static List<Guid> Workers(GameState state, ShopKind kind)
        {
            if (!state.ShopWorkers.TryGetValue(kind, out var list))
                state.ShopWorkers[kind] = list = new List<Guid>();
            return list;
        }

        /// <summary>店で働いている引退者の Id（どちらの店でも）。</summary>
        public static IEnumerable<Guid> AllWorkers(GameState state) => state.ShopWorkers.Values.SelectMany(v => v);

        /// <summary>働いている店（働いていなければ null）。</summary>
        public static ShopKind? ShopOf(GameState state, Guid id) =>
            state.ShopWorkers.Where(kv => kv.Value.Contains(id)).Select(kv => (ShopKind?)kv.Key).FirstOrDefault();

        /// <summary>得意な能力の平均（引退したときの値）：道具屋＝DEX・INT、酒場＝LDR・MND。</summary>
        public static double SkillOf(Adventurer a, ShopKind kind) => kind == ShopKind.ItemShop
            ? (a.DEX + a.INT) / 2.0
            : (a.LDR + a.MND) / 2.0;

        /// <summary>酒場の名声の上乗せ：いちばん高い称号の格と、殿堂入り。</summary>
        public static int FameBonus(GameState state, Adventurer a)
        {
            var titles = HonorSystem.GetTitles(state, a);
            int bonus = titles.Count == 0 ? 0 : titles.Max(t => t.Rank) switch
            {
                TitleRank.Legend => ShopBalance.TavernFameLegend,
                TitleRank.Name => ShopBalance.TavernFameName,
                TitleRank.Honor => ShopBalance.TavernFameHonor,
                _ => 0,
            };
            if (HonorSystem.HallOfFameReason(state, a) != null) bonus += ShopBalance.TavernHallOfFameBonus;
            return bonus;
        }

        /// <summary>働き手1人の毎月の売上（見込み）。</summary>
        public static int SalesOf(GameState state, Adventurer a, ShopKind kind)
        {
            double sales = ShopBalance.ShopBaseSales + Math.Max(0, SkillOf(a, kind) - ShopBalance.ShopStatLine) * ShopBalance.ShopSalesPerStat;
            if (kind == ShopKind.ItemShop)
                sales += Math.Min(ShopBalance.ItemShopStockCap, state.Materials.Values.Sum() * ShopBalance.ItemShopStockPerUnit);
            else
                sales += FameBonus(state, a);
            return (int)Math.Round(sales);
        }

        /// <summary>
        /// 働き手にする（月のいつでも）。引退していてギルドにいる子（派遣の教官は不可）で、枠が空いていること。
        /// 教官・顧問・ほかの店からは外す（1人1役職）。
        /// </summary>
        public static bool TryAssign(GameState state, ShopKind kind, Guid id)
        {
            var r = state.RetiredAdventurers.FirstOrDefault(x => x.Id == id);
            if (r == null || r.LeftGuild || r.IsGuest) return false;
            if (Workers(state, kind).Contains(id)) return true;
            if (Workers(state, kind).Count >= Slots(state, kind)) return false;
            AdvisorSystem.UnassignFromAllPosts(state, id);
            Workers(state, kind).Add(id);
            return true;
        }

        /// <summary>店から外す。</summary>
        public static void Unassign(GameState state, Guid id)
        {
            foreach (var list in state.ShopWorkers.Values) list.Remove(id);
        }

        /// <summary>
        /// 4週ごとの売上（アルベールの内職と同じ週、→ EconomySystem.ProcessWeeklySideJobIncome）：働き手ごとの売上を入金する。
        /// 枠を超えている働き手（酒場のLvが下がったときなど）は、後から入った子を外す。入金週でなければ空。
        /// </summary>
        public static List<ShopSale> ProcessMonthlySales(GameState state)
        {
            var sales = new List<ShopSale>();
            foreach (var kind in Enum.GetValues<ShopKind>())
            {
                var list = Workers(state, kind);
                list.RemoveAll(id => state.RetiredAdventurers.All(r => r.Id != id || r.LeftGuild));
                while (list.Count > Slots(state, kind)) list.RemoveAt(list.Count - 1);
            }
            if (state.WeekNumber % EconomyBalance.SideJobIntervalWeeks != 0) return sales;
            foreach (var kind in Enum.GetValues<ShopKind>())
                foreach (var id in Workers(state, kind))
                    if (state.RetiredAdventurers.FirstOrDefault(r => r.Id == id) is { } a)
                    {
                        int gold = SalesOf(state, a, kind);
                        state.Gold += gold;
                        sales.Add(new ShopSale(kind, a.Id, a.Name, gold));
                    }
            return sales;
        }
    }
}
