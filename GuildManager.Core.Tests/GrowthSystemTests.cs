using System;
using System.Collections.Generic;
using System.Linq;
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

            var events = system.ProcessDeploymentGrowth(party, new Quest { Difficulty = 50 });

            // Warrior重み表の先頭(累積>=1)はSTR。成長量もAlwaysMinRngで下限(1)。
            Assert.Equal(41, adventurer.STR);

            // 実際に伸びた分がGrowthEventとして返る（→ UI週報表示用）。
            var growthEvent = Assert.Single(events);
            Assert.Same(adventurer, growthEvent.Adventurer);
            Assert.Equal("STR", growthEvent.Stat);
            Assert.Equal(40, growthEvent.Before);
            Assert.Equal(41, growthEvent.After);
        }

        [Fact]
        public void ProcessDeploymentGrowth_NoGrowth_WhenRollFails()
        {
            var adventurer = new Adventurer { Age = 18, JobClass = JobClass.Warrior, STR = 40, PA_STR = 80 };
            var party = new Party();
            party.TryAdd(adventurer);
            var system = new GrowthSystem(new AlwaysMaxRng());

            var events = system.ProcessDeploymentGrowth(party, new Quest { Difficulty = 100 });

            Assert.Equal(40, adventurer.STR); // 成長期・難易度100でも閾値は60%止まり、roll=100は必ず外れる
            Assert.Empty(events);
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

        private static int TotalStats(Adventurer a) => a.STR + a.AGI + a.VIT + a.MND + a.DEX + a.LDR;

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
        public void ProcessDeploymentGrowth_NoEvent_WhenAlreadyAtPaCap()
        {
            // 既にPA上限（STR=PA_STR=80）のため、ロールが成功しても実質変化なし＝報告しない。
            var adventurer = new Adventurer { Age = 18, JobClass = JobClass.Warrior, STR = 80, PA_STR = 80 };
            var party = new Party();
            party.TryAdd(adventurer);
            var system = new GrowthSystem(new FixedRollRng(3));

            var events = system.ProcessDeploymentGrowth(party, new Quest { Difficulty = 0 });

            Assert.Equal(80, adventurer.STR);
            Assert.Empty(events);
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

        // ---------------- 経路2：訓練施設配置による成長（v1.3改訂：施設ごとに対象ステータスを限定） ----------------

        [Fact]
        public void ProcessTrainingGrowth_GrowsTrainingAssignedNonDispatchedMember()
        {
            var adventurer = new Adventurer { Age = 18, STR = 40, PA_STR = 80 };
            var state = new GameState { Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id, FacilityType.WarriorHall);
            var system = new GrowthSystem(new AlwaysMinRng());

            system.ProcessTrainingGrowth(state, NoDispatch);

            // 戦士訓練所の対象ステータスは[STR,VIT]。AlwaysMinRngは先頭=STRを選ぶ。
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
            state.TrainingAssignments.Add(adventurer.Id, FacilityType.WarriorHall);
            var system = new GrowthSystem(new AlwaysMinRng());

            system.ProcessTrainingGrowth(state, new HashSet<Guid> { adventurer.Id });

            Assert.Equal(40, adventurer.STR); // 二重成長防止：出撃者は経路2の対象外
        }

        [Fact]
        public void ProcessTrainingGrowth_RestrictsGrowthTargetToFacilityStats_SingleStatFacility()
        {
            // 教会（Church）の対象ステータスはMNDのみ。ロール値に関わらずMND以外は絶対に伸びない。
            var adventurer = new Adventurer { Age = 18, STR = 40, PA_STR = 80, MND = 40, PA_MND = 80 };
            var state = new GameState { Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id, FacilityType.Church);
            var system = new GrowthSystem(new AlwaysMinRng());

            var events = system.ProcessTrainingGrowth(state, NoDispatch);

            Assert.Single(events);
            Assert.Equal("MND", events[0].Stat);
            Assert.Equal(40, adventurer.STR); // 対象外のSTRは変化しない
        }

        [Fact]
        public void ProcessTrainingGrowth_RestrictsGrowthTargetToFacilityStats_TwoStatFacility()
        {
            // 斥候所（ScoutPost）の対象ステータスは[AGI,DEX]の2つ。FixedRollRng(1)はインデックス1=DEXを指す。
            var adventurer = new Adventurer { Age = 18, AGI = 40, PA_AGI = 80, DEX = 40, PA_DEX = 80 };
            var state = new GameState { Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id, FacilityType.ScoutPost);
            var system = new GrowthSystem(new FixedRollRng(1));

            var events = system.ProcessTrainingGrowth(state, NoDispatch);

            Assert.Single(events);
            Assert.Equal("DEX", events[0].Stat);
        }

        [Fact]
        public void ProcessTrainingGrowth_ClampsAtPaCap()
        {
            var adventurer = new Adventurer { Age = 18, MND = 79, PA_MND = 80 };
            var state = new GameState { Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id, FacilityType.Church);
            var system = new GrowthSystem(new AlwaysMinRng());

            system.ProcessTrainingGrowth(state, NoDispatch);

            Assert.Equal(80, adventurer.MND);
        }

        [Fact]
        public void ProcessTrainingGrowth_CanGrowInt_ViaMageLab()
        {
            // v1.3改訂：INTを鍛えられるのは魔法研究所（MageLab）のみ。
            var adventurer = new Adventurer { Age = 18, INT = 79, PA_INT = 80 };
            var state = new GameState { Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id, FacilityType.MageLab);
            var system = new GrowthSystem(new AlwaysMinRng());

            var events = system.ProcessTrainingGrowth(state, NoDispatch);

            Assert.Single(events);
            Assert.Equal("INT", events[0].Stat);
            Assert.Equal(80, adventurer.INT);
        }

        [Fact]
        public void ProcessTrainingGrowth_IncreasesEffectiveStat_WhenStatGrows()
        {
            // 成長ロール成功時は実効値だけが上がる（生涯ピーク値の更新処理は撤廃済み、v2.0）。
            var adventurer = new Adventurer { Age = 18, STR = 40, PA_STR = 80 };
            var state = new GameState { Adventurers = { adventurer } };
            state.TrainingAssignments.Add(adventurer.Id, FacilityType.WarriorHall);
            var system = new GrowthSystem(new AlwaysMinRng());

            var growth = Assert.Single(system.ProcessTrainingGrowth(state, NoDispatch));

            Assert.Equal("STR", growth.Stat);
            Assert.Equal(41, adventurer.STR);
        }

        [Fact]
        public void ProcessTrainingGrowth_AppliesTrainerBonus_IncreasingGrowthChance()
        {
            // 教官（生涯ピークSTR/VIT平均が高い）を戦士訓練所に配置すると、成長確率倍率が
            // 上乗せされる（→ 03 §7.1）。ここでは、教官が居なければ成長ロールが必ず失敗する
            // ぎりぎりの roll を使い、教官ボーナスが乗ることで成長が成立することを確認する。
            var trainee = new Adventurer { Age = 25, STR = 40, PA_STR = 80 }; // 全盛期(22〜27)：基礎確率12%
            var trainer = new Adventurer { STR = 100, VIT = 100 }; // 生涯ピーク平均100
            var state = new GameState
            {
                Adventurers = { trainee },
                RetiredAdventurers = { trainer },
            };
            state.TrainingAssignments.Add(trainee.Id, FacilityType.WarriorHall);
            state.AssignedTrainers[FacilityType.WarriorHall] = trainer.Id;

            // roll=13：教官なしの基礎確率12%だけでは失敗(13>12)、教官ボーナス(100*0.005=0.5→+50%)が
            // 乗ると12%+50%=62%となり成立する(13<=62)。
            var system = new GrowthSystem(new FixedRollRng(13));

            var events = system.ProcessTrainingGrowth(state, NoDispatch);

            Assert.Single(events);
        }

        // ---------------- 職業別の成長ステータス重み（GrowthBalance） ----------------

        [Theory]
        [InlineData(1, "STR")] // Warrior累積: STR=3,AGI=4,VIT=7,MND=7,DEX=8,LDR=9
        [InlineData(4, "AGI")]
        [InlineData(9, "LDR")]
        public void PickJobWeightedStat_Warrior_FollowsWeightTable(int roll, string expectedStat)
        {
            var stat = GrowthBalance.PickJobWeightedStat(JobClass.Warrior, new FixedRollRng(roll));
            Assert.Equal(expectedStat, stat);
        }

        [Theory]
        // 7職業化で神官の重みを合計9へ改訂（旧 0,1,1,2,1,2,0 → 新 0,1,2,3,1,2,0）。
        // Cleric累積: AGI=1,VIT=3,MND=6,DEX=7,LDR=9（STR・INTは重み0）
        [InlineData(2, "VIT")]
        [InlineData(4, "MND")]
        [InlineData(6, "MND")]
        [InlineData(7, "DEX")]
        [InlineData(9, "LDR")]
        public void PickJobWeightedStat_Cleric_FavorsMagAndLdr(int roll, string expectedStat)
        {
            var stat = GrowthBalance.PickJobWeightedStat(JobClass.Cleric, new FixedRollRng(roll));
            Assert.Equal(expectedStat, stat);
        }

        [Theory]
        [InlineData(5, "MND")] // Mage累積: AGI=1,VIT=2,MND=5,DEX=6,LDR=7,INT=9（STRは重み0）
        [InlineData(8, "INT")] // INTを成長対象に持つのは魔導士と学者（7職業化で学者を追加）
        [InlineData(9, "INT")]
        public void PickJobWeightedStat_Mage_IncludesIntInWeightTable(int roll, string expectedStat)
        {
            var stat = GrowthBalance.PickJobWeightedStat(JobClass.Mage, new FixedRollRng(roll));
            Assert.Equal(expectedStat, stat);
        }

        // ---------------- 7職業化：成長重みテーブル（→ growth_job_weights.csv） ----------------

        [Fact]
        public void JobStatWeights_AreLoadedForAllSevenJobClasses()
        {
            // 列挙型の全職業について行が存在すること（行が欠けていると例外になる）。
            var allJobs = Enum.GetValues<JobClass>();

            Assert.Equal(7, allJobs.Length);
            foreach (var job in allJobs)
                Assert.NotEmpty(GrowthBalance.GetJobStatWeights(job));
        }

        [Theory]
        [InlineData(JobClass.Warrior)]
        [InlineData(JobClass.Knight)]
        [InlineData(JobClass.Ranger)]
        [InlineData(JobClass.Thief)]
        [InlineData(JobClass.Mage)]
        [InlineData(JobClass.Cleric)]
        [InlineData(JobClass.Scholar)]
        public void JobStatWeights_SumToNine_ForEveryJobClass(JobClass job)
        {
            // 全職業の重み合計を9で統一している（職業間で成長機会の総量に差をつけないため）。
            int total = GrowthBalance.GetJobStatWeights(job).Sum(w => w.Weight);

            Assert.Equal(9, total);
        }

        [Theory]
        // 新3職の特徴的なステータスが最大重みであること（各職の役割付けの検証）。
        [InlineData(JobClass.Knight, "VIT", 4)]
        [InlineData(JobClass.Thief, "AGI", 4)]
        [InlineData(JobClass.Scholar, "INT", 4)]
        public void JobStatWeights_NewJobClasses_HaveExpectedSpecialty(JobClass job, string specialtyStat, int expectedWeight)
        {
            var weights = GrowthBalance.GetJobStatWeights(job);

            Assert.Equal(expectedWeight, weights.Single(w => w.Stat == specialtyStat).Weight);
            Assert.Equal(expectedWeight, weights.Max(w => w.Weight));
        }

        [Theory]
        // Knight累積: STR=2,VIT=6,MND=7,LDR=9（AGI・DEX・INTは重み0）
        // ※CSVの列順は STR,AGI,VIT,MND,DEX,LDR,INT。重み0の列は抽選対象にならない。
        [InlineData(JobClass.Knight, 1, "STR")]
        [InlineData(JobClass.Knight, 3, "VIT")]
        [InlineData(JobClass.Knight, 9, "LDR")]
        // Thief累積: STR=1,AGI=5,VIT=6,DEX=9
        [InlineData(JobClass.Thief, 2, "AGI")]
        [InlineData(JobClass.Thief, 5, "AGI")]
        [InlineData(JobClass.Thief, 9, "DEX")]
        // Scholar累積: AGI=1,MND=3,DEX=4,LDR=5,INT=9
        [InlineData(JobClass.Scholar, 2, "MND")]
        [InlineData(JobClass.Scholar, 6, "INT")]
        [InlineData(JobClass.Scholar, 9, "INT")]
        public void PickJobWeightedStat_NewJobClasses_FollowWeightTable(JobClass job, int roll, string expectedStat)
        {
            var stat = GrowthBalance.PickJobWeightedStat(job, new FixedRollRng(roll));
            Assert.Equal(expectedStat, stat);
        }
    }
}
