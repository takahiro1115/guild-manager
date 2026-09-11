using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 施設Lv投資（着工・工事期間・単一建設キュー）のテスト（仕様書 03 §6・§6.1）。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class FacilitySystemTests
    {
        private static Facility GetFacility(GameState state, FacilityType type)
        {
            foreach (var f in state.Facilities)
                if (f.Type == type) return f;
            return null!;
        }

        // ---------------- 着工（TryStartConstruction） ----------------

        [Fact]
        public void TryStartConstruction_Succeeds_WhenNothingUnderConstruction()
        {
            var state = new GameState { Gold = 10000 };
            var system = new FacilitySystem();

            bool result = system.TryStartConstruction(state, FacilityType.Dormitory);

            Assert.True(result);
            Assert.NotNull(state.UnderConstruction);
            Assert.Equal(FacilityType.Dormitory, state.UnderConstruction!.Type);
            Assert.Equal(2, state.UnderConstruction.TargetLevel); // Lv1→Lv2
        }

        [Fact]
        public void TryStartConstruction_DeductsCost_AsPrepayment()
        {
            var state = new GameState { Gold = 10000 };
            var system = new FacilitySystem();

            system.TryStartConstruction(state, FacilityType.Dormitory);

            int expectedCost = FacilityBalance.GetUpgradeCost(FacilityType.Dormitory, 1);
            Assert.Equal(10000 - expectedCost, state.Gold);
        }

        [Fact]
        public void TryStartConstruction_DoesNotChangeCurrentLevel_UntilCompletion()
        {
            // 着工中も、着工前の現在Lvの効果はそのまま維持される（→ 03 §6.1）。
            var state = new GameState { Gold = 10000 };
            var system = new FacilitySystem();

            system.TryStartConstruction(state, FacilityType.Dormitory);

            Assert.Equal(1, GetFacility(state, FacilityType.Dormitory).CurrentLevel);
        }

        [Fact]
        public void TryStartConstruction_Fails_WhenAnotherFacilityIsUnderConstruction()
        {
            var state = new GameState { Gold = 10000 };
            var system = new FacilitySystem();
            system.TryStartConstruction(state, FacilityType.Dormitory);

            bool result = system.TryStartConstruction(state, FacilityType.Infirmary);

            Assert.False(result);
            Assert.Equal(FacilityType.Dormitory, state.UnderConstruction!.Type); // 変わらない
        }

        [Fact]
        public void TryStartConstruction_Fails_WhenInsufficientGold()
        {
            var state = new GameState { Gold = 0 };
            var system = new FacilitySystem();

            bool result = system.TryStartConstruction(state, FacilityType.Dormitory);

            Assert.False(result);
            Assert.Null(state.UnderConstruction);
            Assert.Equal(0, state.Gold); // 変化しない
        }

        [Fact]
        public void TryStartConstruction_Fails_WhenAlreadyAtMaxLevel()
        {
            var state = new GameState { Gold = 999999 };
            GetFacility(state, FacilityType.Dormitory).CurrentLevel = FacilityBalance.MaxLevel;
            var system = new FacilitySystem();

            bool result = system.TryStartConstruction(state, FacilityType.Dormitory);

            Assert.False(result);
            Assert.Null(state.UnderConstruction);
        }

        // ---------------- 週次決算（ProcessWeeklyConstruction） ----------------

        [Fact]
        public void ProcessWeeklyConstruction_ReturnsNull_WhenNothingUnderConstruction()
        {
            var state = new GameState();
            var system = new FacilitySystem();

            var completed = system.ProcessWeeklyConstruction(state);

            Assert.Null(completed);
        }

        [Fact]
        public void ProcessWeeklyConstruction_DecrementsWeeksRemaining_WhileStillInProgress()
        {
            var state = new GameState { Gold = 10000 };
            var system = new FacilitySystem();
            system.TryStartConstruction(state, FacilityType.Dormitory); // Lv1→2: 工事期間1週（仮値）
            // 工事期間を強制的に2週に伸ばして「まだ完成しない週」を検証する
            state.UnderConstruction!.WeeksRemaining = 2;

            var completed = system.ProcessWeeklyConstruction(state);

            Assert.Null(completed);
            Assert.Equal(1, state.UnderConstruction!.WeeksRemaining);
            Assert.Equal(1, GetFacility(state, FacilityType.Dormitory).CurrentLevel); // まだ未完成
        }

        [Fact]
        public void ProcessWeeklyConstruction_CompletesAndRaisesLevel_WhenWeeksReachZero()
        {
            var state = new GameState { Gold = 10000 };
            var system = new FacilitySystem();
            system.TryStartConstruction(state, FacilityType.Dormitory);
            state.UnderConstruction!.WeeksRemaining = 1; // 次回で完成させる

            var completed = system.ProcessWeeklyConstruction(state);

            Assert.NotNull(completed);
            Assert.Equal(FacilityType.Dormitory, completed!.Type);
            Assert.Equal(2, completed.CurrentLevel);
            Assert.Equal(2, GetFacility(state, FacilityType.Dormitory).CurrentLevel);
            Assert.Null(state.UnderConstruction); // キューが空になる
        }

        [Fact]
        public void ProcessWeeklyConstruction_AllowsNewConstruction_AfterCompletion()
        {
            var state = new GameState { Gold = 999999 };
            var system = new FacilitySystem();
            system.TryStartConstruction(state, FacilityType.Dormitory);
            state.UnderConstruction!.WeeksRemaining = 1;
            system.ProcessWeeklyConstruction(state);

            bool result = system.TryStartConstruction(state, FacilityType.Infirmary);

            Assert.True(result);
        }
    }
}
