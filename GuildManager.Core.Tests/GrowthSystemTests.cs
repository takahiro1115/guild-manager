using System;
using System.Collections.Generic;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// ステータス成長トリガー（仕様書 03 §3.1〜3.4）のテスト。
    /// 実行方法: このフォルダで `dotnet test`
    ///
    /// 成長は「出撃」（経路1）と「訓練場配置」（経路2）の2経路のみで発生し、
    /// 単純待機では発生しないこと、PA上限でクランプされることを中心に確認する。
    /// </summary>
    public class GrowthSystemTests
    {
        /// <summary>NextInt(min, max) が常に min を返すテスト用スタブ。</summary>
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        /// <summary>NextInt(min, max) が常に max を返すテスト用スタブ。</summary>
        private class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        /// <summary>NextInt(min, max) が固定値を [min, max] にクランプして返すテスト用スタブ。
        /// 成長ロールの成否と、成長量・抽選ロールを個別に制御したい場合に使う。</summary>
        private class FixedRollRng : IRng
        {
            private readonly int _value;
            public FixedRollRng(int value) => _value = value;
            public int NextInt(int min, int max) => Math.Clamp(_value, min, max);
        }

        private static readonly IReadOnlySet<Guid> NoDispatch = new HashSet<Guid>();

        // ---------------- 経路1：出撃による成長 ----------------

        [Fact]
        public void ProcessDeploymentGrowth_GrowsDispatchedMember_WhenRollSucceeds()
        {
            var adventurer = new Adventurer { Age = 18, JobClass = JobClass.Warrior, STR = 40, PA_STR = 80 };
            var party = new Party();
            party.TryAdd(adventurer);
            var system = new GrowthSystem(new AlwaysMinRng());

            system.ProcessDeploymentGrowth(party, new Quest { Difficulty = 50 });

            // Warrior重み表の先頭(累積>=1)はSTR。成長量もAlwaysMinRngで下限(1)。
            Assert.Equal(41, adventurer.STR);
        }

        [Fact]
        public void ProcessDeploymentGrowth_NoGrowth_WhenRollFails()
        {
            var adventurer = new Adventurer { Age = 18, JobClass = JobClass.Warrior, STR = 40, PA_STR = 80 };
            var party = new Party();
            party.TryAdd(adventurer);
            var system = new GrowthSystem(new AlwaysMaxRng());

            system.ProcessDeploymentGrowth(party, new Quest { Difficulty = 100 });

            Assert.Equal(40, adventurer.STR); // 成長期・難易度100でも閾値は60%止まり、roll=100は必ず外れる
        }

        [Fact]
        public void ProcessDeploymentGrowth_HigherQuestDifficulty_IncreasesGrowthChance()
        {
            // roll=45固定：難易度0(閾値30%)では失敗、難易度100(閾値60%)では成功する。
            // PA上限は全ステータス100（Adventurerのデフォルト）のままにし、どのステータスが
            // 選ばれても合計値の変化で成否を判定する（重み抽選の具体的な選択先はここでは問わない）。
            var lowDifficultyMember = new Adventurer { Age = 18, JobClass = JobClass.Warrior };
            var highDifficultyMember = new Adventurer { Age = 18, JobClass = JobClass.Warrior };
            var system = new GrowthSystem(new FixedRollRng(45));

            var lowParty = new Party();
            lowParty.TryAdd(lowDifficultyMember);
            system.ProcessDeploymentGrowth(lowParty, new Quest { Difficulty = 0 });

            var highParty = new Party();
            highParty.TryAdd(highDifficultyMember);
            system.ProcessDeploymentGrowth(highParty, new Quest { Difficulty = 100 });

            Assert.Equal(0, TotalStats(lowDifficultyMember));
            Assert.True(TotalStats(highDifficultyMember) > 0);
        }

        private static int TotalStats(Adventurer a) => a.STR + a.AGI + a.END + a.MAG + a.SCT + a.LDR;

        [Fact]
        public void ProcessDeploymentGrowth_ClampsAtPaCap()
        {
            // roll=3固定：閾値を満たして成長成功、かつ成長量も上限(3)になるため
            // 79+3=82となるはずがPA=80でクランプされることを確認する。
            var adventurer = new Adventurer { Age = 18, JobClass = JobClass.Warrior, STR = 79, PA_STR = 80 };
            var party = new Party();
            party.TryAdd(adventurer);
            var system = new GrowthSystem(new FixedRollRng(3));

            system.ProcessDeploymentGrowth(party, new Quest { Difficulty = 0 });

            Assert.Equal(80, adventurer.STR);
        }

        [Fact]
        public void ProcessDeploymentGrowth_AppliesToEveryPartyMember()
        {
            var a = new Adventurer { Age = 18, JobClass = JobClass.Warrior, STR = 40, PA_STR = 80 };
            var b = new Adventurer { Age = 18, JobClass = JobClass.Warrior, STR = 40, PA_STR = 80 };
            var party = new Party();
            party.TryAdd(a);
            party.TryAdd(b);
            var system = new GrowthSystem(new AlwaysMinRng());

            system.ProcessDeploymentGrowth(party, new Quest { Difficulty = 50 });

            Assert.Equal(41, a.STR);
            Assert.Equal(41, b.STR);
        }

        // ---------------- 経路2：訓練場配置による成長 ----------------

        [Fact]
        public void ProcessTrainingGrowth_GrowsTrainingAssignedNonDispatchedMember()
        {
            var adventurer = new Adventurer { Age = 18, STR = 40, PA_STR = 80 };
            var state = new GameState { Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id);
            var system = new GrowthSystem(new AlwaysMinRng());

            system.ProcessTrainingGrowth(state, NoDispatch);

            // 経路2は全ステータス均等抽選。AllStatNamesの先頭=STRをAlwaysMinRngが選ぶ。
            Assert.Equal(41, adventurer.STR);
        }

        [Fact]
        public void ProcessTrainingGrowth_DoesNotGrow_WhenNotAssignedToTraining()
        {
            var adventurer = new Adventurer { Age = 18, STR = 40, PA_STR = 80 };
            var state = new GameState { Adventurers = { adventurer } };
            // TrainingAssignmentsへの追加なし＝単純待機
            var system = new GrowthSystem(new AlwaysMinRng());

            system.ProcessTrainingGrowth(state, NoDispatch);

            Assert.Equal(40, adventurer.STR); // 単純待機では実効値は変化しない（§3.1〜3.4）
        }

        [Fact]
        public void ProcessTrainingGrowth_DoesNotGrow_DispatchedMemberEvenIfTrainingAssigned()
        {
            var adventurer = new Adventurer { Age = 18, STR = 40, PA_STR = 80 };
            var state = new GameState { Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id);
            var system = new GrowthSystem(new AlwaysMinRng());

            system.ProcessTrainingGrowth(state, new HashSet<Guid> { adventurer.Id });

            Assert.Equal(40, adventurer.STR); // 二重成長防止：出撃者は経路2の対象外
        }

        [Fact]
        public void ProcessTrainingGrowth_ClampsAtPaCap()
        {
            // 経路2（訓練場）は全ステータス均等抽選（0〜5のインデックス）。
            // FixedRollRng(3)はインデックス3=MAGを指すため、MAG側にPA上限を設定して検証する。
            var adventurer = new Adventurer { Age = 18, MAG = 79, PA_MAG = 80 };
            var state = new GameState { Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id);
            var system = new GrowthSystem(new FixedRollRng(3));

            system.ProcessTrainingGrowth(state, NoDispatch);

            Assert.Equal(80, adventurer.MAG);
        }

        // ---------------- 職業別の成長ステータス重み（GrowthBalance） ----------------

        [Theory]
        [InlineData(1, "STR")] // Warrior累積: STR=3,AGI=4,END=7,MAG=7,SCT=8,LDR=9
        [InlineData(4, "AGI")]
        [InlineData(9, "LDR")]
        public void PickJobWeightedStat_Warrior_FollowsWeightTable(int roll, string expectedStat)
        {
            var stat = GrowthBalance.PickJobWeightedStat(JobClass.Warrior, new FixedRollRng(roll));
            Assert.Equal(expectedStat, stat);
        }

        [Theory]
        [InlineData(4, "MAG")] // Cleric累積: AGI=1,END=2,MAG=4,SCT=5,LDR=7（STRは重み0）
        [InlineData(7, "LDR")]
        public void PickJobWeightedStat_Cleric_FavorsMagAndLdr(int roll, string expectedStat)
        {
            var stat = GrowthBalance.PickJobWeightedStat(JobClass.Cleric, new FixedRollRng(roll));
            Assert.Equal(expectedStat, stat);
        }
    }
}
