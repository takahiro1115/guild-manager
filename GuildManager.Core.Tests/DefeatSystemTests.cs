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

        // ---------------- 治安崩壊（猶予なし） ----------------

        [Fact]
        public void ProcessWeeklySettlement_DeclaresSecurityCollapse_ImmediatelyAtThreatLevel100()
        {
            // 破産のカウンタは無関係（猶予なし単発判定）。
            var state = new GameState { Gold = 100, ThreatLevel = 100 };
            var system = new DefeatSystem();

            var result = system.ProcessWeeklySettlement(state);

            Assert.Equal(DefeatReason.SecurityCollapse, result);
            Assert.Equal(DefeatReason.SecurityCollapse, state.DefeatReason);
        }

        [Fact]
        public void ProcessWeeklySettlement_DoesNotDefeat_WhenThreatLevelBelow100()
        {
            var state = new GameState { Gold = 100, ThreatLevel = 99 };
            var system = new DefeatSystem();

            var result = system.ProcessWeeklySettlement(state);

            Assert.Null(result);
            Assert.Null(state.DefeatReason);
        }

        // ---------------- 一度確定したら上書きしない ----------------

        [Fact]
        public void ProcessWeeklySettlement_DoesNotOverwriteExistingDefeatReason()
        {
            var state = new GameState { Gold = 100, ThreatLevel = 100, DefeatReason = DefeatReason.Bankruptcy };
            var system = new DefeatSystem();

            var result = system.ProcessWeeklySettlement(state);

            Assert.Null(result); // 新規確定は無し（既に敗北済みのため）
            Assert.Equal(DefeatReason.Bankruptcy, state.DefeatReason); // 上書きされていない
        }
    }
}
