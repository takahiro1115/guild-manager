using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>ギルドの方針・引退後の進路・育成への投資（段4f、§0.105）のテスト。</summary>
    public class GuildPolicyTests
    {
        private static Adventurer Make(string name, int satisfaction = 50, Drive main = Drive.Gather)
        {
            var a = new Adventurer
            {
                Name = name, JobClass = JobClass.Warrior, Satisfaction = satisfaction, WeeklyWage = 100, Age = 20,
                STR = 30, AGI = 30, VIT = 30, MND = 30, DEX = 30, LDR = 30, INT = 30,
                Personality = new Personality { Main = main },
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static GameState State(params Adventurer[] adventurers)
        {
            var state = new GameState { WeekNumber = 5, Gold = 100000, DungeonFields = SampleData.CreateDefaultFields() };
            state.Adventurers.AddRange(adventurers);
            return state;
        }

        private static void SetLevel(GameState state, FacilityType type, int level)
        {
            var f = state.Facilities.FirstOrDefault(x => x.Type == type);
            if (f == null) { f = new Facility { Type = type }; state.Facilities.Add(f); }
            f.CurrentLevel = level;
        }

        // ---------------- 方針 ----------------

        [Fact]
        public void TrainingPolicy_ChangesGrowthMultiplier()
        {
            var state = State();
            Assert.Equal(1.0, GuildPolicySystem.TrainingGrowthMultiplier(state));
            state.TrainingPolicy = TrainingPolicy.Hard;
            Assert.Equal(PolicyBalance.TrainingHardGrowthMultiplier, GuildPolicySystem.TrainingGrowthMultiplier(state));
            state.TrainingPolicy = TrainingPolicy.Gentle;
            Assert.Equal(PolicyBalance.TrainingGentleGrowthMultiplier, GuildPolicySystem.TrainingGrowthMultiplier(state));
        }

        [Fact]
        public void HardTraining_LowersSatisfaction_ExceptForTrainingLovers_AndCanInjure()
        {
            var plain = Make("ふつうの子");
            var lover = Make("訓練好き", main: Drive.Training);
            var idle = Make("訓練していない子");
            var state = State(plain, lover, idle);
            state.TrainingAssignments[plain.Id] = FacilityType.DrillHall;
            state.TrainingAssignments[lover.Id] = FacilityType.DrillHall;
            state.TrainingPolicy = TrainingPolicy.Hard;

            GuildPolicySystem.ProcessTrainingPolicy(state, new HashSet<System.Guid>(), new SeededRng(1));

            Assert.Equal(50 + PolicyBalance.TrainingHardSatisfaction, plain.Satisfaction);
            Assert.Equal(50, lover.Satisfaction);
            Assert.Equal(50, idle.Satisfaction);

            int injuries = 0;
            for (int i = 0; i < 400; i++)
            {
                plain.Injury = InjurySeverity.None;
                injuries += GuildPolicySystem.ProcessTrainingPolicy(state, new HashSet<System.Guid> { lover.Id }, new SeededRng(i)).Count;
            }
            Assert.InRange(injuries, 1, 40); // 3%前後
        }

        [Fact]
        public void GentleTraining_RaisesSatisfaction()
        {
            var a = Make("A");
            var state = State(a);
            state.TrainingAssignments[a.Id] = FacilityType.Academy;
            state.TrainingPolicy = TrainingPolicy.Gentle;
            Assert.Empty(GuildPolicySystem.ProcessTrainingPolicy(state, new HashSet<System.Guid>(), new SeededRng(1)));
            Assert.Equal(50 + PolicyBalance.TrainingGentleSatisfaction, a.Satisfaction);
        }

        [Fact]
        public void PrizeShare_SplitsThePrize_AndPleasesTheWinner()
        {
            var fan = Make("大会好き", main: Drive.Tournament);
            var state = State(fan);
            Assert.Equal(1000, GuildPolicySystem.GuildShareOfPrize(state, 1000));
            GuildPolicySystem.RewardPrizeShare(state, fan, 1000);
            Assert.Equal(50, fan.Satisfaction); // ギルドが取るなら何もしない

            state.PrizePolicy = PrizePolicy.Share;
            Assert.Equal(1000 - (int)System.Math.Round(1000 * PolicyBalance.PrizeShareRate), GuildPolicySystem.GuildShareOfPrize(state, 1000));
            GuildPolicySystem.RewardPrizeShare(state, fan, 1000);
            Assert.Equal(50 + PolicyBalance.PrizeShareSatisfaction + PolicyBalance.PrizeShareDriveBonus, fan.Satisfaction);
        }

        // ---------------- 引退後の進路 ----------------

        [Fact]
        public void UnappointedRetiree_LeavesAfterTheGraceWeeks_AndCannotBeAppointed()
        {
            var r = Make("引退者");
            r.IsRetired = true;
            r.RetiredAtWeek = 10;
            r.UnassignedSinceWeek = 10;
            var state = State();
            state.RetiredAdventurers.Add(r);
            state.WeekNumber = 10;
            SetLevel(state, FacilityType.DrillHall, 1);

            int last = 10 + PolicyBalance.RetireeRoleGraceWeeks - 1;
            Assert.Equal(last, GuildPolicySystem.LeaveWeek(state, r));
            Assert.Empty(GuildPolicySystem.ProcessRetirees(state, last - 1));
            Assert.Single(GuildPolicySystem.ProcessRetirees(state, last));
            Assert.True(r.LeftGuild);
            Assert.Equal(last, r.LeftGuildAtWeek); // 初めての引退式の順に使う（§0.122）
            Assert.False(new AdvisorSystem().TryAssignTrainer(state, FacilityType.DrillHall, r.Id));
        }

        [Fact]
        public void AppointedRetiree_Stays_AndLosingTheRoleStartsTheCount()
        {
            var r = Make("教官");
            r.IsRetired = true;
            r.RetiredAtWeek = 10;
            r.UnassignedSinceWeek = 10;
            var state = State();
            state.RetiredAdventurers.Add(r);
            SetLevel(state, FacilityType.DrillHall, 1);
            Assert.True(new AdvisorSystem().TryAssignTrainer(state, FacilityType.DrillHall, r.Id));

            Assert.Empty(GuildPolicySystem.ProcessRetirees(state, 100));
            Assert.Null(GuildPolicySystem.LeaveWeek(state, r));

            state.AssignedTrainers[FacilityType.DrillHall] = null; // 役職を外れた
            Assert.Empty(GuildPolicySystem.ProcessRetirees(state, 200));
            Assert.Equal(200 + PolicyBalance.RetireeRoleGraceWeeks - 1, GuildPolicySystem.LeaveWeek(state, r));
            Assert.Single(GuildPolicySystem.ProcessRetirees(state, 200 + PolicyBalance.RetireeRoleGraceWeeks - 1));
        }

        [Fact]
        public void GuestTrainer_NeverLeavesThisWay()
        {
            var guest = Make("派遣の教官");
            guest.IsRetired = true;
            guest.IsGuest = true;
            var state = State();
            state.RetiredAdventurers.Add(guest);
            Assert.Empty(GuildPolicySystem.ProcessRetirees(state, 999));
            Assert.False(guest.LeftGuild);
        }

        // ---------------- 個人特訓 ----------------

        [Fact]
        public void Coaching_CostsUpFront_GrowsEveryWeek_AndEnds()
        {
            var a = Make("期待の新人");
            var state = State(a);
            Assert.True(GuildPolicySystem.StartCoaching(state, a));
            Assert.Equal(100000 - PolicyBalance.CoachingCost, state.Gold);
            Assert.NotNull(GuildPolicySystem.CoachingBlockReason(state, a)); // もう特訓中
            var other = Make("もう1人");
            state.Adventurers.Add(other);
            Assert.NotNull(GuildPolicySystem.CoachingBlockReason(state, other)); // 枠は1人

            int before = a.STR + a.AGI + a.VIT + a.MND + a.DEX + a.LDR + a.INT;
            var growth = new GrowthSystem(new SeededRng(3));
            int events = 0;
            int until = state.Coachings.Single().UntilWeek;
            for (int w = state.WeekNumber; w <= until; w++)
                events += GuildPolicySystem.ProcessCoaching(state, growth, w).Count;

            Assert.True(events > 0);
            Assert.True(a.STR + a.AGI + a.VIT + a.MND + a.DEX + a.LDR + a.INT >= before + events); // 1回で2以上伸びることもある
            Assert.Empty(state.Coachings);
        }

        // ---------------- スカウト網の予算 ----------------

        [Fact]
        public void ScoutBudget_AddsCandidates_ForNextSpringOnly()
        {
            var state = State();
            state.WeekNumber = GameCalendar.WeeksPerYear - 2; // 1年目の冬
            int baseCount = new RecruitmentSystem(new SeededRng(1)).GenerateCandidates(state).Count;

            Assert.True(GuildPolicySystem.PayScoutBudget(state));
            Assert.NotNull(GuildPolicySystem.ScoutBudgetBlockReason(state));
            Assert.False(GuildPolicySystem.ScoutBudgetActive(state));

            state.WeekNumber = GameCalendar.WeeksPerYear + 1; // 2年目の新春
            Assert.True(GuildPolicySystem.ScoutBudgetActive(state));
            Assert.Equal(baseCount + PolicyBalance.ScoutBudgetExtraCandidates, new RecruitmentSystem(new SeededRng(1)).GenerateCandidates(state).Count);
        }

        [Fact]
        public void PolicyFields_RoundTripInTheSave()
        {
            var a = Make("A");
            var state = State(a);
            state.TrainingPolicy = TrainingPolicy.Gentle;
            state.PrizePolicy = PrizePolicy.Share;
            state.ScoutBudgetYear = 3;
            GuildPolicySystem.StartCoaching(state, a);
            var r = Make("去った子");
            r.IsRetired = true;
            r.LeftGuild = true;
            state.RetiredAdventurers.Add(r);

            var back = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(state.ToSaveData()))!);

            Assert.Equal(TrainingPolicy.Gentle, back.TrainingPolicy);
            Assert.Equal(PrizePolicy.Share, back.PrizePolicy);
            Assert.Equal(3, back.ScoutBudgetYear);
            Assert.Equal(a.Id, back.Coachings.Single().AdventurerId);
            Assert.True(back.RetiredAdventurers.Single().LeftGuild);
            Assert.Null(back.RetiredAdventurers.Single().RetiredAtWeek); // 去った週の無い旧セーブ（§0.122）は、引退した週（無ければ今の週）＋役職を待つ週数で補う
            Assert.Equal(back.WeekNumber + PolicyBalance.RetireeRoleGraceWeeks - 1, back.RetiredAdventurers.Single().LeftGuildAtWeek);

            var legacy = JsonSerializer.Deserialize<SaveData>("{}")!;
            legacy.Coachings = null;
            var fresh = GameState.FromSaveData(legacy);
            Assert.Equal(TrainingPolicy.Normal, fresh.TrainingPolicy);
            Assert.Equal(PrizePolicy.Guild, fresh.PrizePolicy);
            Assert.Empty(fresh.Coachings);
        }
    }
}
