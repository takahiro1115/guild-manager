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

        // ---------------- 初期状態 ----------------

        /// <summary>
        /// 施設投資ポップアップに5施設中2件しか表示されない不具合の調査時に追加した回帰テスト。
        /// 原因はGameState.Facilities自体ではなくFacilityPopup側のレイアウトだったが
        /// （→ facility_popup.tscn修正）、データ生成側が全種を持つことを保証する意味で残す。
        /// v1.3改訂：訓練場・道場の4分割により5種から8種に拡張。
        /// v1.4改訂：冒険者支援室（RecruitmentOffice）を新設し9種に拡張。あわせて、
        /// 宿舎・医務室・ギルド酒場（基幹3施設）以外は初期LvがLv0（未建設）に変更された（→ 03 §6）。
        /// </summary>
        [Fact]
        public void NewGameState_HasAllNineFacilityTypes_BaseThreeAtLevel1_RestAtLevelZero()
        {
            var state = new GameState();

            Assert.Equal(9, state.Facilities.Count);

            foreach (var type in new[] { FacilityType.Dormitory, FacilityType.Infirmary, FacilityType.Tavern })
                Assert.Equal(1, GetFacility(state, type)?.CurrentLevel);

            foreach (var type in new[]
                     {
                         FacilityType.WarRoom, FacilityType.WarriorHall, FacilityType.Church,
                         FacilityType.MageLab, FacilityType.ScoutPost, FacilityType.RecruitmentOffice,
                     })
                Assert.Equal(0, GetFacility(state, type)?.CurrentLevel);
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

        // ---------------- Lv0（未建設）からの着工（→ 03 §6、v1.4改訂・新設） ----------------

        [Fact]
        public void TryStartConstruction_Succeeds_FromLevelZero()
        {
            // 戦士訓練所はLv0（未建設）スタート。Lv0→Lv1の着工も既存ルールに従う。
            var state = new GameState { Gold = 10000 };
            var system = new FacilitySystem();

            bool result = system.TryStartConstruction(state, FacilityType.WarriorHall);

            Assert.True(result);
            Assert.Equal(1, state.UnderConstruction!.TargetLevel); // Lv0→Lv1
        }

        [Fact]
        public void TryStartConstruction_FromLevelZero_ChargesNonZeroCost()
        {
            // Lv0→Lv1の着工が無料になってしまうバグの修正確認
            // （旧実装：GetUpgradeCost = currentLevel*500 だとLv0の場合0Gになっていた）。
            var state = new GameState { Gold = 10000 };
            var system = new FacilitySystem();

            system.TryStartConstruction(state, FacilityType.WarriorHall);

            Assert.True(state.Gold < 10000);
            Assert.Equal(FacilityBalance.GetUpgradeCost(FacilityType.WarriorHall, 0), 10000 - state.Gold);
            Assert.NotEqual(0, FacilityBalance.GetUpgradeCost(FacilityType.WarriorHall, 0));
        }

        [Fact]
        public void ProcessWeeklyConstruction_CompletesLevelZeroToLevelOne()
        {
            var state = new GameState { Gold = 10000 };
            var system = new FacilitySystem();
            system.TryStartConstruction(state, FacilityType.WarriorHall);
            state.UnderConstruction!.WeeksRemaining = 1;

            var completed = system.ProcessWeeklyConstruction(state);

            Assert.NotNull(completed);
            Assert.Equal(1, completed!.CurrentLevel);
            Assert.Equal(1, GetFacility(state, FacilityType.WarriorHall).CurrentLevel);
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
