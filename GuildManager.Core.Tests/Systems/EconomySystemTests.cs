using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests.Systems
{
    /// <summary>
    /// 在庫の換金（→ EconomySystem.TrySellMaterial、03 §4.8 売却）の単体テスト。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~Economy`
    ///
    /// 単価は materials.csv の SellPrice（→ MaterialBalance）を正本とし、テスト側でも
    /// 期待値をそこから引く（CSVを直した時にテストが嘘をつかないようにするため）。
    /// </summary>
    public class EconomySystemTests
    {
        private static GameState StateWith(string materialId, int stock, int gold = 1000)
        {
            var state = new GameState { Gold = gold };
            state.AddMaterial(materialId, stock);
            return state;
        }

        // ---------------- 素材売却（指示書指定テスト） ----------------

        [Fact]
        public void SellMaterial_Single_DeductsStock_AndAddsGold()
        {
            var state = StateWith(MaterialIds.ForestHerb, stock: 5);
            int unitPrice = MaterialBalance.GetSellPrice(MaterialIds.ForestHerb);

            Assert.True(new EconomySystem().TrySellMaterial(state, MaterialIds.ForestHerb, 1, out int gold));

            Assert.Equal(unitPrice, gold);
            Assert.Equal(1000 + unitPrice, state.Gold);
            Assert.Equal(4, state.Materials[MaterialIds.ForestHerb]);
        }

        [Fact]
        public void SellMaterial_Bulk_DeductsStock_AndAddsGold()
        {
            var state = StateWith(MaterialIds.CaveOre, stock: 10);
            int unitPrice = MaterialBalance.GetSellPrice(MaterialIds.CaveOre);
            var economy = new EconomySystem();

            // 複数個まとめ売り。
            Assert.True(economy.TrySellMaterial(state, MaterialIds.CaveOre, 3, out int bulkGold));
            Assert.Equal(unitPrice * 3, bulkGold);
            Assert.Equal(7, state.Materials[MaterialIds.CaveOre]);

            // 残り全数売り。在庫が0になった素材はキーごと消える
            // （→ GameState.AddMaterial の「未所持＝キーが無い」という不変条件に合わせる）。
            Assert.True(economy.TrySellAllOfMaterial(state, MaterialIds.CaveOre, out int allGold));
            Assert.Equal(unitPrice * 7, allGold);
            Assert.DoesNotContain(MaterialIds.CaveOre, state.Materials.Keys);
            Assert.Equal(1000 + unitPrice * 10, state.Gold);
        }

        [Fact]
        public void SellMaterial_Fails_WhenCountExceedsStock()
        {
            var state = StateWith(MaterialIds.ForestWood, stock: 2);

            Assert.False(new EconomySystem().TrySellMaterial(state, MaterialIds.ForestWood, 3, out int gold));

            Assert.Equal(0, gold);
            Assert.Equal(2, state.Materials[MaterialIds.ForestWood]); // 在庫は1個も減らない
            Assert.Equal(1000, state.Gold);
        }

        [Fact]
        public void SellMaterial_Fails_ForZeroOrNegativeCount()
        {
            var state = StateWith(MaterialIds.ForestHerb, stock: 5);
            var economy = new EconomySystem();

            Assert.False(economy.TrySellMaterial(state, MaterialIds.ForestHerb, 0, out _));
            Assert.False(economy.TrySellMaterial(state, MaterialIds.ForestHerb, -1, out _));
            Assert.Equal(5, state.Materials[MaterialIds.ForestHerb]);
            Assert.Equal(1000, state.Gold);
        }

        [Fact]
        public void SellMaterial_Fails_ForUnknownOrUnownedMaterial()
        {
            var state = new GameState { Gold = 1000 };
            var economy = new EconomySystem();

            // 所持していない素材。
            Assert.False(economy.TrySellMaterial(state, MaterialIds.ForestHerb, 1, out _));

            // materials.csv に定義の無いId（単価が決まらないため売れない）。
            state.AddMaterial("mat_unknown", 5);
            Assert.False(economy.TrySellMaterial(state, "mat_unknown", 1, out _));
            Assert.Equal(5, state.Materials["mat_unknown"]);
            Assert.Equal(1000, state.Gold);
        }

        [Fact]
        public void SellAllOfMaterial_Fails_WhenNothingInStock()
        {
            var state = new GameState { Gold = 1000 };

            Assert.False(new EconomySystem().TrySellAllOfMaterial(state, MaterialIds.AbyssCrystal, out int gold));

            Assert.Equal(0, gold);
            Assert.Equal(1000, state.Gold);
        }

        // ---------------- 売却単価テーブル（materials.csv） ----------------

        [Fact]
        public void SellPrice_IsDefined_AndPositive_ForEveryMaterial()
        {
            Assert.All(MaterialBalance.GetAll(), m => Assert.True(m.SellPrice > 0,
                $"{m.Id} の SellPrice が未設定（0以下）です。"));
        }

        [Fact]
        public void SellPrice_IsHigherForDeeperFieldMaterials()
        {
            // 深いフィールドの素材ほど高く売れる（採取先を選ぶ動機づけ）。
            Assert.True(MaterialBalance.GetSellPrice(MaterialIds.AbyssCrystal)
                > MaterialBalance.GetSellPrice(MaterialIds.ForestHerb));
            Assert.True(MaterialBalance.GetSellPrice(MaterialIds.CaveOre)
                > MaterialBalance.GetSellPrice(MaterialIds.CaveMoss));
        }

        [Fact]
        public void SellMaterial_DoesNotDisturb_OtherMaterials()
        {
            var state = StateWith(MaterialIds.ForestHerb, stock: 5);
            state.AddMaterial(MaterialIds.ForestWood, 3);

            Assert.True(new EconomySystem().TrySellMaterial(state, MaterialIds.ForestHerb, 5, out _));

            Assert.DoesNotContain(MaterialIds.ForestHerb, state.Materials.Keys);
            Assert.Equal(3, state.Materials[MaterialIds.ForestWood]);
        }

        // ---------------- 既存の週次資金処理との併存 ----------------

        [Fact]
        public void ApplyWeeklyWages_StillDeductsTotalWages()
        {
            var state = new GameState
            {
                Gold = 1000,
                Adventurers = { new Adventurer { WeeklyWage = 40 }, new Adventurer { WeeklyWage = 60 } },
            };

            new EconomySystem().ApplyWeeklyWages(state);

            Assert.Equal(900, state.Gold);
        }

        // ---------------- アルベールの内職売上 × 内職強化研究（SideBusinessGoldBonus、2026年9月新設） ----------------

        private static GameState SideJobWeekState(int mood, params string[] completedResearchIds)
        {
            var state = new GameState { WeekNumber = EconomyBalance.SideJobIntervalWeeks, MasterMood = mood, Gold = 0 };
            foreach (var id in completedResearchIds)
                state.CompletedResearchIds.Add(id);
            return state;
        }

        [Fact]
        public void SideJobIncome_WithoutResearch_BaseIs200()
        {
            var state = SideJobWeekState(mood: 60);

            var income = new EconomySystem().ProcessWeeklySideJobIncome(state)!;

            Assert.Equal(200, EconomyBalance.SideJobBaseAmount);
            Assert.Equal(0, income.ResearchBonus);
            Assert.Equal(200, income.BaseGold);
            Assert.Equal(200, income.FinalGold);
        }

        [Theory]
        [InlineData(ResearchIds.BeautyLotion, 150)]
        [InlineData(ResearchIds.EnergyTonic, 250)]
        [InlineData(ResearchIds.TradeRoute, 400)]
        [InlineData(ResearchIds.VitalityElixir, 600)]
        public void SideJobIncome_EachResearch_AddsItsBonusToBase(string researchId, int expectedBonus)
        {
            Assert.Equal(ResearchEffectType.SideBusinessGoldBonus, ResearchBalance.Find(researchId)!.EffectType);
            var state = SideJobWeekState(60, researchId);

            var income = new EconomySystem().ProcessWeeklySideJobIncome(state)!;

            Assert.Equal(expectedBonus, income.ResearchBonus);
            Assert.Equal(200 + expectedBonus, income.BaseGold);
            Assert.Equal(200 + expectedBonus, income.FinalGold); // 平常×1.0
        }

        [Fact]
        public void SideJobIncome_ResearchBonusesStack_AndMoodMultiplierApplies()
        {
            // 例（指示書）：基本 350G［初期200+研究150］×上機嫌1.5 ＝ 525G。
            var single = SideJobWeekState(90, ResearchIds.BeautyLotion);
            var incomeSingle = new EconomySystem().ProcessWeeklySideJobIncome(single)!;
            Assert.Equal(350, incomeSingle.BaseGold);
            Assert.Equal(1.5, incomeSingle.Multiplier, precision: 6);
            Assert.Equal(525, incomeSingle.FinalGold);
            Assert.Equal(525, single.Gold);

            // 4段階すべて完了：200+150+250+400+600＝1600G。不機嫌×0.5で800G、危機×0.0で0G。
            var all = new[] { ResearchIds.BeautyLotion, ResearchIds.EnergyTonic, ResearchIds.TradeRoute, ResearchIds.VitalityElixir };
            Assert.Equal(1600, EconomySystem.GetSideJobBaseGold(SideJobWeekState(30, all)));
            Assert.Equal(800, new EconomySystem().ProcessWeeklySideJobIncome(SideJobWeekState(30, all))!.FinalGold);
            Assert.Equal(0, new EconomySystem().ProcessWeeklySideJobIncome(SideJobWeekState(10, all))!.FinalGold);
        }

        [Fact]
        public void ResearchBalance_SideBusinessGoldBonus_TotalIsSumOfCompleted()
        {
            var state = SideJobWeekState(60, ResearchIds.EnergyTonic, ResearchIds.VitalityElixir);

            Assert.Equal(850, ResearchBalance.GetTotalEffectValue(state, ResearchEffectType.SideBusinessGoldBonus), precision: 6);
            Assert.Equal(4, ResearchBalance.GetAll().Count(r => r.EffectType == ResearchEffectType.SideBusinessGoldBonus));
        }
    }
}
