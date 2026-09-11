using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 加齢・衰微モデル（仕様書 03 §3）のテスト。「伸びる」側（成長トリガー）は
    /// GrowthSystem へ移動したため GrowthSystemTests でカバーする。
    /// 実行方法: このフォルダで `dotnet test`
    ///
    /// AlwaysMinRng で乱数を「常に範囲の下限」に固定し、衰微対象の抽選・
    /// 低下量ロールをすべて決定的にしてから境界を検証する。
    /// </summary>
    public class AgingSystemTests
    {
        /// <summary>NextInt(min, max) が常に min を返すテスト用スタブ。</summary>
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private static GameState CreateState(Adventurer adventurer, int weekNumber) =>
            new GameState { WeekNumber = weekNumber, Adventurers = { adventurer } };

        // ---------------- AgeBand 境界（仕様書 03 §3.0） ----------------

        [Theory]
        [InlineData(15, AgeBand.GrowthPeriod)]
        [InlineData(21, AgeBand.GrowthPeriod)]
        [InlineData(22, AgeBand.PrimePeriod)]
        [InlineData(27, AgeBand.PrimePeriod)]
        [InlineData(28, AgeBand.MaturePeriod)]
        [InlineData(34, AgeBand.MaturePeriod)]
        [InlineData(35, AgeBand.LimitPeriod)]
        [InlineData(40, AgeBand.LimitPeriod)]
        public void AgeBand_MatchesDefinitionTable(int age, AgeBand expected)
        {
            var adventurer = new Adventurer { Age = age };
            Assert.Equal(expected, adventurer.AgeBand);
        }

        // ---------------- 成長期・全盛期はAgingSystem内では何もしない（成長はGrowthSystemへ移動） ----------------

        [Fact]
        public void ProcessWeeklyAging_GrowthPeriod_DoesNothingToStats()
        {
            var adventurer = new Adventurer
            {
                Age = 18,
                STR = 10, PA_STR = 50,
                AGI = 20, PA_AGI = 20,
                END = 20, PA_END = 20,
            };
            var state = CreateState(adventurer, weekNumber: 5);
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.Equal(10, adventurer.STR); // AgingSystemはもう成長を扱わない（→ GrowthSystem）
            Assert.Equal(20, adventurer.AGI);
            Assert.Equal(20, adventurer.END);
        }

        [Fact]
        public void ProcessWeeklyAging_PrimePeriod_NeitherGrowsNorDeclines()
        {
            var adventurer = new Adventurer
            {
                Age = 24,
                STR = 40, PA_STR = 80,
                AGI = 40, PA_AGI = 80,
                END = 40, PA_END = 80,
            };
            // 年度末（衰微が起きうる週）でも全盛期は対象外であることを確認する。
            var state = CreateState(adventurer, weekNumber: 48);
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.Equal(40, adventurer.STR);
            Assert.Equal(40, adventurer.AGI);
            Assert.Equal(40, adventurer.END);
        }

        // ---------------- 円熟期の衰微（年1回・年度末） ----------------

        [Fact]
        public void ProcessWeeklyAging_MaturePeriod_DeclinesOnlyAtYearEndWeek()
        {
            var adventurer = new Adventurer { Age = 30, STR = 40, PA_STR = 80, AGI = 40, PA_AGI = 80, END = 40, PA_END = 80 };
            var state = CreateState(adventurer, weekNumber: 47); // 年度末の1週前
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.Equal(40, adventurer.STR);
            Assert.Equal(40, adventurer.AGI);
            Assert.Equal(40, adventurer.END);
        }

        [Fact]
        public void ProcessWeeklyAging_MaturePeriod_DeclinesOneStatAtYearEnd()
        {
            var adventurer = new Adventurer { Age = 30, STR = 40, PA_STR = 80, AGI = 40, PA_AGI = 80, END = 40, PA_END = 80 };
            var state = CreateState(adventurer, weekNumber: 48); // 年度末
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            // AlwaysMinRng: 対象数=1、抽選プール["STR","AGI","END"]の先頭=STR、低下量=下限(1)
            Assert.Equal(39, adventurer.STR);
            Assert.Equal(80, adventurer.PA_STR); // PAには影響しない（成長上限は変わらない。新仕様）
            Assert.Equal(40, adventurer.AGI);
            Assert.Equal(40, adventurer.END);
        }

        [Fact]
        public void ProcessWeeklyAging_Decline_NeverDropsBelowZero()
        {
            var adventurer = new Adventurer { Age = 30, STR = 0, PA_STR = 80, AGI = 40, PA_AGI = 80, END = 40, PA_END = 80 };
            var state = CreateState(adventurer, weekNumber: 48);
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.Equal(0, adventurer.STR); // 実効値の下限は0（新仕様）
            Assert.Equal(80, adventurer.PA_STR); // PAは不変
        }

        // ---------------- 限界期の衰微（年2回・低下量拡大） ----------------

        [Theory]
        [InlineData(24)]
        [InlineData(48)]
        public void ProcessWeeklyAging_LimitPeriod_DeclinesTwiceAYear(int weekOfYear)
        {
            var adventurer = new Adventurer { Age = 37, STR = 40, PA_STR = 80, AGI = 40, PA_AGI = 80, END = 40, PA_END = 80 };
            var state = CreateState(adventurer, weekNumber: weekOfYear);
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.Equal(37, adventurer.STR); // 限界期の下限(3)だけ低下
        }

        [Fact]
        public void ProcessWeeklyAging_LimitPeriod_DeclineAmountIsLargerThanMaturePeriod()
        {
            var mature = new Adventurer { Age = 30, STR = 40, PA_STR = 80 };
            var limit = new Adventurer { Age = 37, STR = 40, PA_STR = 80 };
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(CreateState(mature, weekNumber: 48));
            system.ProcessWeeklyAging(CreateState(limit, weekNumber: 48));

            int matureDrop = 40 - mature.STR;
            int limitDrop = 40 - limit.STR;
            Assert.True(limitDrop > matureDrop, $"限界期の低下量({limitDrop})は円熟期({matureDrop})より大きいはず");
        }

        // ---------------- 年齢の進行と強制引退（§3.7） ----------------

        [Fact]
        public void ProcessWeeklyAging_AdvancesAgeAtYearEnd()
        {
            var adventurer = new Adventurer { Age = 20 };
            var state = CreateState(adventurer, weekNumber: 48);
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.Equal(21, adventurer.Age);
            Assert.False(adventurer.IsRetired);
        }

        [Fact]
        public void ProcessWeeklyAging_DoesNotAdvanceAgeMidYear()
        {
            var adventurer = new Adventurer { Age = 20 };
            var state = CreateState(adventurer, weekNumber: 25);
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.Equal(20, adventurer.Age);
        }

        [Fact]
        public void ProcessWeeklyAging_RetiresAtFortyYearEnd()
        {
            var adventurer = new Adventurer { Age = 40, STR = 30, PA_STR = 60, WeeklyWage = 50 };
            var state = CreateState(adventurer, weekNumber: 48);
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.True(adventurer.IsRetired);
            Assert.Equal(40, adventurer.Age); // それ以上は加齢しない
            Assert.False(adventurer.IsAvailable);
        }

        [Fact]
        public void ProcessWeeklyAging_Retirement_MovesFromActiveToRetiredRoster()
        {
            var adventurer = new Adventurer { Age = 40 };
            var state = CreateState(adventurer, weekNumber: 48);
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.DoesNotContain(adventurer, state.Adventurers);
            Assert.Contains(adventurer, state.RetiredAdventurers);
        }

        [Fact]
        public void ProcessWeeklyAging_Retirement_PaysSeveranceOfTwelveWeeksWage()
        {
            var adventurer = new Adventurer { Age = 40, WeeklyWage = 50 };
            var state = CreateState(adventurer, weekNumber: 48);
            state.Gold = 1000;
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.Equal(1000 - 50 * 12, state.Gold);
            Assert.True(adventurer.SeverancePaid);
        }

        [Fact]
        public void ProcessWeeklyAging_Retirement_RecordsAgeAndWeek()
        {
            var adventurer = new Adventurer { Age = 40 };
            var state = CreateState(adventurer, weekNumber: 48);
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.Equal(40, adventurer.RetiredAtAge);
            Assert.Equal(48, adventurer.RetiredAtWeek);
        }

        [Fact]
        public void ProcessWeeklyAging_Retirement_FreesTrainingSlot()
        {
            var adventurer = new Adventurer { Age = 40 };
            var state = CreateState(adventurer, weekNumber: 48);
            state.TrainingAssignments.Add(adventurer.Id);
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.DoesNotContain(adventurer.Id, state.TrainingAssignments);
        }

        [Fact]
        public void ProcessWeeklyAging_SkipsRetiredAdventurersEntirely()
        {
            var adventurer = new Adventurer { Age = 40, IsRetired = true, STR = 30, PA_STR = 60 };
            var state = CreateState(adventurer, weekNumber: 48);
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.Equal(30, adventurer.STR); // 衰微処理も年齢処理も走らない
            Assert.Equal(40, adventurer.Age);
        }

        [Fact]
        public void ProcessWeeklyAging_OneRetirement_DoesNotSkipProcessingOtherAdventurers()
        {
            // state.Adventurersを直接foreachすると、途中でRemoveしたときに
            // コレクション変更例外や後続要素のスキップが起こりうる。スナップショットで
            // 回避できていることを、複数人・年度末処理で確認する。
            var retiring = new Adventurer { Age = 40 };
            var stayingYoung = new Adventurer { Age = 20 };
            var state = new GameState
            {
                WeekNumber = 48,
                Adventurers = { retiring, stayingYoung },
            };
            var system = new AgingSystem(new AlwaysMinRng());

            system.ProcessWeeklyAging(state);

            Assert.Contains(retiring, state.RetiredAdventurers);
            Assert.Contains(stayingYoung, state.Adventurers);
            Assert.Equal(21, stayingYoung.Age); // 年度末の加齢が正しく適用されている
        }
    }
}
