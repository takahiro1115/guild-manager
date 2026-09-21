using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 敗北条件（破産・治安崩壊）のテスト（仕様書 03 §8.3）。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class DefeatSystemTests
    {
        // ---------------- 破産（4週連続） ----------------

        [Fact]
        public void ProcessWeeklySettlement_IncrementsCounter_WhenGoldIsNegative()
        {
            var state = new GameState { Gold = -1 };
            var system = new DefeatSystem();

            system.ProcessWeeklySettlement(state);

            Assert.Equal(1, state.ConsecutiveNegativeGoldWeeks);
            Assert.Null(state.DefeatReason);
        }

        [Fact]
        public void ProcessWeeklySettlement_ResetsCounter_WhenGoldIsNonNegative()
        {
            var state = new GameState { Gold = 100, ConsecutiveNegativeGoldWeeks = 3 };
            var system = new DefeatSystem();

            system.ProcessWeeklySettlement(state);

            Assert.Equal(0, state.ConsecutiveNegativeGoldWeeks);
        }

        [Fact]
        public void ProcessWeeklySettlement_DoesNotDefeat_BeforeFourConsecutiveWeeks()
        {
            var state = new GameState { Gold = -1, ConsecutiveNegativeGoldWeeks = 2 };
            var system = new DefeatSystem();

            var result = system.ProcessWeeklySettlement(state);

            Assert.Null(result);
            Assert.Null(state.DefeatReason);
        }

        [Fact]
        public void ProcessWeeklySettlement_DeclaresBankruptcy_OnFourthConsecutiveNegativeWeek()
        {
            var state = new GameState { Gold = -1, ConsecutiveNegativeGoldWeeks = 3 };
            var system = new DefeatSystem();

            var result = system.ProcessWeeklySettlement(state);

            Assert.Equal(DefeatReason.Bankruptcy, result);
            Assert.Equal(DefeatReason.Bankruptcy, state.DefeatReason);
            Assert.Equal(4, state.ConsecutiveNegativeGoldWeeks);
        }

        // ---------------- 治安崩壊は敗北条件から撤廃済み ----------------
        // 経営破綻（資金ショート）のみへの一本化改訂により、脅威度は何%であっても
        // それ単体で敗北を引き起こさなくなった（→ DefeatSystem・03 §8.3）。

        [Fact]
        public void GameOver_OnlyTriggeredByBankruptcy()
        {
            // 資金が健全（破産カウンタ未達）なら敗北しない。
            var healthy = new GameState { Gold = 100 };
            Assert.Null(new DefeatSystem().ProcessWeeklySettlement(healthy));
            Assert.Null(healthy.DefeatReason);

            // 資金マイナスが4週連続なら破産で敗北する（＝唯一の敗北条件は破産）。
            var bankrupt = new GameState { Gold = -1, ConsecutiveNegativeGoldWeeks = 3 };
            var result = new DefeatSystem().ProcessWeeklySettlement(bankrupt);
            Assert.Equal(DefeatReason.Bankruptcy, result);
            Assert.Equal(DefeatReason.Bankruptcy, bankrupt.DefeatReason);
        }

        // ---------------- 一度確定したら上書きしない ----------------

        [Fact]
        public void ProcessWeeklySettlement_DoesNotOverwriteExistingDefeatReason()
        {
            var state = new GameState { Gold = 100, DefeatReason = DefeatReason.Bankruptcy };
            var system = new DefeatSystem();

            var result = system.ProcessWeeklySettlement(state);

            Assert.Null(result); // 新規確定は無し（既に敗北済みのため）
            Assert.Equal(DefeatReason.Bankruptcy, state.DefeatReason); // 上書きされていない
        }
    }
}
