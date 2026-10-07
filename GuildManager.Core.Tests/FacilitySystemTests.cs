using System.Linq;
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
        /// <summary>
        /// 倒したボスの数を count にする（施設の上限Lv、§0.79。テスト用のフィールドを1つ足し、ボスを撃破済みで並べる）。
        /// </summary>
        internal static GameState DefeatBosses(GameState state, int count)
        {
            state.DungeonFields.Add(new DungeonField
            {
                Id = "test_cleared", Order = 99, IsUnlocked = true,
                Bosses = Enumerable.Range(1, count).Select(i => new FloorBoss { Floor = i * 10, IsDefeated = true }).ToList(),
            });
            return state;
        }

        /// <summary>どの施設もLv5まで開いている状態（§0.82：施設は大会などのご褒美で開く）。</summary>
        internal static GameState AllLevelsOpen(int gold)
        {
            var state = new GameState { Gold = gold };
            foreach (var type in System.Enum.GetValues<FacilityType>())
                state.FacilityUnlockedLevels[type] = FacilityBalance.MaxLevel;
            return state;
        }

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

            Assert.Equal(8, state.Facilities.Count);

            foreach (var type in new[] { FacilityType.Dormitory, FacilityType.Infirmary, FacilityType.Tavern })
                Assert.Equal(1, GetFacility(state, type)?.CurrentLevel);

            foreach (var type in new[]
                     {
                         FacilityType.WarRoom, FacilityType.DrillHall, FacilityType.Academy,
                         FacilityType.SkillHall, FacilityType.RecruitmentOffice,
                     })
                Assert.Equal(0, GetFacility(state, type)?.CurrentLevel);
        }

        // ---------------- 着工（TryStartConstruction） ----------------

        [Fact]
        public void TryStartConstruction_Succeeds_WhenNothingUnderConstruction()
        {
            var state = AllLevelsOpen(10000);
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
            var state = AllLevelsOpen(10000);
            var system = new FacilitySystem();

            system.TryStartConstruction(state, FacilityType.Dormitory);

            int expectedCost = FacilityBalance.GetUpgradeCost(FacilityType.Dormitory, 1);
            Assert.Equal(10000 - expectedCost, state.Gold);
        }

        [Fact]
        public void TryStartConstruction_DoesNotChangeCurrentLevel_UntilCompletion()
        {
            // 着工中も、着工前の現在Lvの効果はそのまま維持される（→ 03 §6.1）。
            var state = AllLevelsOpen(10000);
            var system = new FacilitySystem();

            system.TryStartConstruction(state, FacilityType.Dormitory);

            Assert.Equal(1, GetFacility(state, FacilityType.Dormitory).CurrentLevel);
        }

        // ---------------- Lv0（未建設）からの着工（→ 03 §6、v1.4改訂・新設） ----------------

        [Fact]
        public void TryStartConstruction_Succeeds_FromLevelZero()
        {
            // 戦士訓練所はLv0（未建設）スタート。Lv0→Lv1の着工も既存ルールに従う。
            var state = AllLevelsOpen(10000);
            var system = new FacilitySystem();

            bool result = system.TryStartConstruction(state, FacilityType.DrillHall);

            Assert.True(result);
            Assert.Equal(1, state.UnderConstruction!.TargetLevel); // Lv0→Lv1
        }

        [Fact]
        public void TryStartConstruction_FromLevelZero_ChargesNonZeroCost()
        {
            // Lv0→Lv1の着工が無料になってしまうバグの修正確認
            // （旧実装：GetUpgradeCost = currentLevel*500 だとLv0の場合0Gになっていた）。
            var state = AllLevelsOpen(10000);
            var system = new FacilitySystem();

            system.TryStartConstruction(state, FacilityType.DrillHall);

            Assert.True(state.Gold < 10000);
            Assert.Equal(FacilityBalance.GetUpgradeCost(FacilityType.DrillHall, 0), 10000 - state.Gold);
            Assert.NotEqual(0, FacilityBalance.GetUpgradeCost(FacilityType.DrillHall, 0));
        }

        [Fact]
        public void ProcessWeeklyConstruction_CompletesLevelZeroToLevelOne()
        {
            var state = AllLevelsOpen(10000);
            var system = new FacilitySystem();
            system.TryStartConstruction(state, FacilityType.DrillHall);
            state.UnderConstruction!.WeeksRemaining = 1;

            var completed = system.ProcessWeeklyConstruction(state);

            Assert.NotNull(completed);
            Assert.Equal(1, completed!.CurrentLevel);
            Assert.Equal(1, GetFacility(state, FacilityType.DrillHall).CurrentLevel);
        }

        [Fact]
        public void TryStartConstruction_Fails_WhenAnotherFacilityIsUnderConstruction()
        {
            var state = AllLevelsOpen(10000);
            var system = new FacilitySystem();
            system.TryStartConstruction(state, FacilityType.Dormitory);

            bool result = system.TryStartConstruction(state, FacilityType.Infirmary);

            Assert.False(result);
            Assert.Equal(FacilityType.Dormitory, state.UnderConstruction!.Type); // 変わらない
        }

        [Fact]
        public void TryStartConstruction_Fails_WhenInsufficientGold()
        {
            var state = AllLevelsOpen(0);
            var system = new FacilitySystem();

            bool result = system.TryStartConstruction(state, FacilityType.Dormitory);

            Assert.False(result);
            Assert.Null(state.UnderConstruction);
            Assert.Equal(0, state.Gold); // 変化しない
        }

        [Fact]
        public void TryStartConstruction_Fails_WhenAlreadyAtMaxLevel()
        {
            var state = AllLevelsOpen(999999);
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
            var state = AllLevelsOpen(10000);
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
            var state = AllLevelsOpen(10000);
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
            var state = AllLevelsOpen(999999);
            var system = new FacilitySystem();
            system.TryStartConstruction(state, FacilityType.Dormitory);
            state.UnderConstruction!.WeeksRemaining = 1;
            system.ProcessWeeklyConstruction(state);

            bool result = system.TryStartConstruction(state, FacilityType.Infirmary);

            Assert.True(result);
        }

        // ---------------- 段階的な開放（§0.78） ----------------

        [Theory]
        [InlineData(FacilityType.WarRoom)]
        [InlineData(FacilityType.RecruitmentOffice)]
        public void AdvisorFacilities_AreNotAvailable_UntilFirstRetiree(FacilityType type)
        {
            var state = AllLevelsOpen(10000);
            var system = new FacilitySystem();

            Assert.False(FacilitySystem.IsAvailable(state, type));
            Assert.False(system.TryStartConstruction(state, type));
            Assert.Equal(10000, state.Gold);

            state.RetiredAdventurers.Add(new Adventurer { IsRetired = true });

            Assert.True(FacilitySystem.IsAvailable(state, type));
            Assert.True(system.TryStartConstruction(state, type));
        }

        [Fact]
        public void AdvisorFacility_StaysAvailable_OnceBuilt()
        {
            var state = new GameState();
            GetFacility(state, FacilityType.WarRoom).CurrentLevel = 1;

            Assert.True(FacilitySystem.IsAvailable(state, FacilityType.WarRoom));
        }

        [Theory]
        [InlineData(FacilityType.DrillHall)]
        [InlineData(FacilityType.Academy)]
        [InlineData(FacilityType.SkillHall)]
        [InlineData(FacilityType.Dormitory)]
        [InlineData(FacilityType.Infirmary)]
        [InlineData(FacilityType.Tavern)]
        public void OtherFacilities_AreAvailable_FromTheStart(FacilityType type)
        {
            Assert.True(FacilitySystem.IsAvailable(new GameState(), type));
        }

        // ---------------- 医務室の開放はボスの数（§0.79の表を医務室だけに残す、§0.82） ----------------

        [Theory]
        [InlineData(0, 1)]
        [InlineData(4, 1)]
        [InlineData(5, 2)]
        [InlineData(12, 3)]
        [InlineData(20, 4)]
        [InlineData(30, 5)]
        public void Infirmary_UnlocksWithDefeatedBosses(int bosses, int level)
        {
            Assert.Equal(level, FacilityBalance.GetLevelCap(bosses));
            var state = DefeatBosses(new GameState(), bosses);
            FacilityUnlockSystem.Evaluate(state);
            Assert.Equal(level, FacilityUnlockSystem.GetUnlockedLevel(state, FacilityType.Infirmary));
        }

        [Fact]
        public void TryStartConstruction_Fails_ForLevelNotYetUnlocked()
        {
            var state = new GameState { Gold = 10000 };
            var system = new FacilitySystem();

            Assert.True(FacilitySystem.IsBlockedByLevelCap(state, FacilityType.Dormitory)); // 宿舎のLv2は入賞3回から
            Assert.False(system.TryStartConstruction(state, FacilityType.Dormitory));
            Assert.False(system.TryStartConstruction(state, FacilityType.DrillHall)); // 訓練所も大会のご褒美で開く
            Assert.Equal(10000, state.Gold);

            state.FacilityUnlockedLevels[FacilityType.DrillHall] = 1;
            Assert.True(system.TryStartConstruction(state, FacilityType.DrillHall));
        }

        [Fact]
        public void HalfPriceReward_HalvesTheNextUpgradeOnly()
        {
            var state = AllLevelsOpen(10000);
            state.NextUpgradeHalfPrice = true;
            var system = new FacilitySystem();

            Assert.True(system.TryStartConstruction(state, FacilityType.Dormitory));
            Assert.Equal(10000 - FacilityBalance.GetUpgradeCost(FacilityType.Dormitory, 1) / 2, state.Gold);
            Assert.False(state.NextUpgradeHalfPrice);
        }

        [Fact]
        public void ProcessWeek_ReportsAdvisorFacilitiesOpened_OnFirstRetirement()
        {
            var veteran = new Adventurer { Name = "満期", Age = 25 };
            veteran.CurrentHP = veteran.MaxHP;
            var state = new GameState { WeekNumber = 48, Gold = 100000, Adventurers = { veteran } }; // 年度末：25歳で満期引退
            var expedition = new DungeonExpeditionSystem(
                new ScoutingResolver(new FixedRng()), new DungeonResolver(new FixedRng()), new SatisfactionSystem(),
                new CompatibilitySystem(new FixedRng()), new DungeonTraversalResolver(new FixedRng()), new GatheringResolver(new FixedRng()));
            var week = new WeekProcessingSystem(new MasterMoodSystem(), new EconomySystem(), new TrainingSystem(), new InjuryRecoverySystem(),
                new RestRecoverySystem(), new GrowthSystem(new FixedRng()), new SatisfactionSystem(), new AgingSystem(new FixedRng()),
                new FacilitySystem(), new DefeatSystem(), new RecruitmentSystem(new FixedRng()), expedition);

            var result = week.ProcessWeek(state);

            Assert.Single(state.RetiredAdventurers);
            Assert.True(result.AdvisorFacilitiesOpened);
            Assert.False(week.ProcessWeek(state).AdvisorFacilitiesOpened); // 1回だけ
        }

        private class FixedRng : GuildManager.Core.Rng.IRng
        {
            public int NextInt(int min, int max) => max;
        }
    }
}
