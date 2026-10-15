using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>ギルドの店（道具屋・酒場、§0.134）のテスト。</summary>
    public class GuildShopTests
    {
        private static Adventurer Retiree(string name, int stat)
        {
            return new Adventurer
            {
                Name = name, JobClass = JobClass.Warrior, IsRetired = true,
                STR = stat, VIT = stat, AGI = stat, DEX = stat, INT = stat, MND = stat, LDR = stat,
            };
        }

        private static GameState StateWith(params Adventurer[] retirees)
        {
            var state = new GameState { Gold = 0, WeekNumber = 4 };
            state.RetiredAdventurers.AddRange(retirees);
            return state;
        }

        [Fact]
        public void Slots_ItemShopIsFixed_TavernFollowsItsLevel()
        {
            var state = StateWith();
            Assert.Equal(ShopBalance.ItemShopSlots, GuildShopSystem.Slots(state, ShopKind.ItemShop));
            Assert.Equal(state.GetFacilityLevel(FacilityType.Tavern) * ShopBalance.TavernSlotsPerLevel, GuildShopSystem.Slots(state, ShopKind.Tavern));
        }

        [Fact]
        public void Sales_GrowWithTheSkill_AndTheStock()
        {
            var plain = Retiree("並", 50);
            var skilled = Retiree("目利き", 90);
            var state = StateWith(plain, skilled);
            Assert.Equal(ShopBalance.ShopBaseSales, GuildShopSystem.SalesOf(state, plain, ShopKind.ItemShop));
            Assert.Equal(ShopBalance.ShopBaseSales + 40 * ShopBalance.ShopSalesPerStat, GuildShopSystem.SalesOf(state, skilled, ShopKind.ItemShop));
            state.Materials["mat_forest_herb"] = 10000;
            Assert.Equal(ShopBalance.ShopBaseSales + ShopBalance.ItemShopStockCap, GuildShopSystem.SalesOf(state, plain, ShopKind.ItemShop)); // 在庫は上限まで
        }

        [Fact]
        public void TheTavern_DrawsCustomersWithFame()
        {
            var plain = Retiree("並", 50);
            var star = Retiree("看板", 50);
            star.TournamentRecords.Add(new TournamentRecord { Kind = TournamentKind.Rookie, Grade = TournamentGrade.G3, Placing = 1 }); // 新人王（誉）
            var state = StateWith(plain, star);
            Assert.Equal(ShopBalance.ShopBaseSales, GuildShopSystem.SalesOf(state, plain, ShopKind.Tavern));
            Assert.Equal(ShopBalance.ShopBaseSales + ShopBalance.TavernFameHonor, GuildShopSystem.SalesOf(state, star, ShopKind.Tavern));
        }

        [Fact]
        public void Workers_StayInTheGuild_GetAStipend_EarnMonthly_AndCanBeParents()
        {
            var a = Retiree("店番", 70);
            var state = StateWith(a);
            Assert.True(GuildShopSystem.TryAssign(state, ShopKind.ItemShop, a.Id));
            Assert.Contains(a.Id, GuildPolicySystem.AppointedIds(state));
            Assert.Contains(a, GuildFinanceSystem.PaidRetirees(state));
            Assert.Contains(a, SoulFusionSystem.GuildMembers(state));

            var sales = GuildShopSystem.ProcessMonthlySales(state); // 第4週＝入金の週
            Assert.Equal(GuildShopSystem.SalesOf(state, a, ShopKind.ItemShop), Assert.Single(sales).Gold);
            Assert.Equal(sales[0].Gold, state.Gold);

            state.WeekNumber = 5;
            Assert.Empty(GuildShopSystem.ProcessMonthlySales(state));
        }

        [Fact]
        public void OnePostPerRetiree_AndFullShopsRefuse()
        {
            var a = Retiree("A", 60);
            var b = Retiree("B", 60);
            var c = Retiree("C", 60);
            var state = StateWith(a, b, c);
            foreach (var f in state.Facilities) if (f.Type == FacilityType.DrillHall) f.CurrentLevel = 1;
            Assert.True(new AdvisorSystem().TryAssignTrainer(state, FacilityType.DrillHall, a.Id));
            Assert.True(GuildShopSystem.TryAssign(state, ShopKind.ItemShop, a.Id));
            Assert.False(state.AssignedTrainers.ContainsKey(FacilityType.DrillHall)); // 教官から外れた
            Assert.True(GuildShopSystem.TryAssign(state, ShopKind.ItemShop, b.Id));
            Assert.False(GuildShopSystem.TryAssign(state, ShopKind.ItemShop, c.Id)); // 2人で満員

            Assert.True(new AdvisorSystem().TryAssignTrainer(state, FacilityType.DrillHall, b.Id));
            Assert.Null(GuildShopSystem.ShopOf(state, b.Id)); // 教官になったら店から外れる
        }

        [Fact]
        public void ShopWorkers_SurviveTheSave()
        {
            var a = Retiree("店番", 70);
            var state = StateWith(a);
            GuildShopSystem.TryAssign(state, ShopKind.Tavern, a.Id);
            var loaded = GameState.FromSaveData(state.ToSaveData());
            Assert.Equal(ShopKind.Tavern, GuildShopSystem.ShopOf(loaded, a.Id));
        }
    }
}
