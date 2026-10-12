using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>満足度と相性の段階（段4b、§0.101）：段階とのりしろ・力の倍率・志向の力と成長・相棒と険悪・かばい合い・さぼり・不満の伝染・相性の素地の帯のテスト。</summary>
    public class MoraleTests
    {
        private sealed class AlwaysMinRng : IRng { public int NextInt(int min, int max) => min; }
        private sealed class AlwaysMaxRng : IRng { public int NextInt(int min, int max) => max; }

        private static Adventurer Make(string name, int satisfaction = 60, Personality? p = null, int stat = 50)
        {
            var a = new Adventurer
            {
                Name = name, JobClass = JobClass.Warrior, Satisfaction = satisfaction, Personality = p,
                STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat,
            };
            a.CurrentHP = a.MaxHP;
            MoraleSystem.UpdateStage(a);
            return a;
        }

        private static Personality P(Drive main, Drive? sub = null, Drive? disliked = null, int affinity = 0) =>
            new() { Main = main, Sub = sub, Disliked = disliked, Affinity = affinity };

        private static GameState State(params Adventurer[] adventurers)
        {
            var state = new GameState { WeekNumber = 5, DungeonFields = SampleData.CreateDefaultFields() };
            state.Adventurers.AddRange(adventurers);
            return state;
        }

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            return party;
        }

        private static void SetCompat(GameState state, Adventurer a, Adventurer b, int value) =>
            state.Compatibility[CompatibilitySystem.NormalizeKey(a.Id, b.Id)] = value;

        // ---------------- 満足度の段階 ----------------

        [Theory]
        [InlineData(MoraleStage.Normal, 80, MoraleStage.Cheerful)]
        [InlineData(MoraleStage.Normal, 79, MoraleStage.Normal)]
        [InlineData(MoraleStage.Cheerful, 76, MoraleStage.Cheerful)]   // のりしろ：75までは上機嫌のまま
        [InlineData(MoraleStage.Cheerful, 74, MoraleStage.Normal)]
        [InlineData(MoraleStage.Normal, 39, MoraleStage.Discontent)]
        [InlineData(MoraleStage.Discontent, 44, MoraleStage.Discontent)] // 45に戻るまで不満
        [InlineData(MoraleStage.Discontent, 45, MoraleStage.Normal)]
        [InlineData(MoraleStage.Normal, 19, MoraleStage.Breaking)]
        [InlineData(MoraleStage.Breaking, 24, MoraleStage.Breaking)]     // 25に戻るまで限界
        [InlineData(MoraleStage.Breaking, 25, MoraleStage.Discontent)]
        public void NextStage_HasAMargin(MoraleStage current, int satisfaction, MoraleStage expected) =>
            Assert.Equal(expected, MoraleSystem.NextStage(current, satisfaction));

        [Fact]
        public void MemberPower_FollowsTheStageAndTheDiveDrive()
        {
            var normal = Make("ふつう");
            var cheerful = Make("上機嫌", 90);
            var breaking = Make("限界", 10);
            var diver = Make("武闘派", p: P(Drive.Dive));
            var hater = Make("潜行嫌い", p: P(Drive.Gather, disliked: Drive.Dive));
            double basePower = DungeonPowerCalculator.MemberPower(normal);

            Assert.Equal(basePower * PersonalityBalance.MoraleMultiplierCheerful, DungeonPowerCalculator.MemberPower(cheerful), 6);
            Assert.Equal(basePower * PersonalityBalance.MoraleMultiplierBreaking, DungeonPowerCalculator.MemberPower(breaking), 6);
            Assert.Equal(basePower * (1 + PersonalityBalance.DrivePowerMain), DungeonPowerCalculator.MemberPower(diver), 6);
            Assert.Equal(basePower * (1 + PersonalityBalance.DrivePowerDisliked), DungeonPowerCalculator.MemberPower(hater), 6);
        }

        [Fact]
        public void SurveyAndGathering_UseTheirOwnDrive()
        {
            var normal = Make("ふつう");
            var seeker = Make("探求派", p: P(Drive.Survey));
            var picker = Make("収穫派", p: P(Drive.Gather));

            Assert.Equal(ScoutingResolver.GetAnalysisValue(normal) * (1 + PersonalityBalance.DrivePowerMain), ScoutingResolver.GetAnalysisValue(seeker), 6);
            Assert.Equal(ScoutingResolver.GetAnalysisValue(normal), ScoutingResolver.GetAnalysisValue(picker), 6);
            Assert.Equal(GatheringResolver.GetMemberGatheringValue(normal) * (1 + PersonalityBalance.DrivePowerMain), GatheringResolver.GetMemberGatheringValue(picker), 6);
            Assert.Equal(ScoutingResolver.GetGuardValue(normal), ScoutingResolver.GetGuardValue(seeker), 6); // 護衛に志向は掛けない
        }

        [Fact]
        public void TournamentStrength_UsesTheStageAndTheTournamentDrive()
        {
            var normal = Make("ふつう");
            var star = Make("栄光派", 90, P(Drive.Tournament));

            Assert.Equal(TournamentSystem.MatchStrength(normal, TournamentDiscipline.Sword) * PersonalityBalance.MoraleMultiplierCheerful * (1 + PersonalityBalance.DrivePowerMain),
                TournamentSystem.MatchStrength(star, TournamentDiscipline.Sword), 6);
        }

        [Fact]
        public void GrowthBonus_AddsTheDriveAndCheerfulness()
        {
            var trainer = Make("求道派", 90, P(Drive.Training));
            var lazy = Make("訓練嫌い", 60, P(Drive.Dive, disliked: Drive.Training));

            Assert.Equal(PersonalityBalance.DriveGrowthMain + PersonalityBalance.MoraleCheerfulGrowthBonus, MoraleSystem.GrowthBonus(trainer, Drive.Training), 6);
            Assert.Equal(PersonalityBalance.DriveGrowthDisliked, MoraleSystem.GrowthBonus(lazy, Drive.Training), 6);
            Assert.Equal(PersonalityBalance.MoraleCheerfulHelpCreditMultiplier, MoraleSystem.HelpCreditMultiplier(trainer));
        }

        // ---------------- 相棒と険悪 ----------------

        [Fact]
        public void PairFactor_RaisesForABuddy_AndLowersForAFoe()
        {
            var a = Make("A");
            var buddy = Make("相棒");
            var foe = Make("犬猿");
            var state = State(a, buddy, foe);
            SetCompat(state, a, buddy, PersonalityBalance.BuddyThreshold);
            SetCompat(state, a, foe, CompatibilityBalance.HostileThreshold - 1);

            Assert.Equal(1 + PersonalityBalance.BuddyPowerBonus, MoraleSystem.PairFactor(state, a, new[] { a, buddy }), 6);
            Assert.Equal(1 + PersonalityBalance.HostilePowerPenalty, MoraleSystem.PairFactor(state, a, new[] { a, foe }), 6);
            Assert.Equal(1.0, MoraleSystem.PairFactor(null, a, new[] { a, buddy }));
            Assert.Equal(PersonalityBalance.HostileTraversalHpLossMultiplier, MoraleSystem.TraversalHpLossMultiplier(state, a, new[] { a, foe }), 6);
        }

        [Fact]
        public void BossPower_IncludesThePairFactorOnlyWithState()
        {
            var a = Make("A");
            var b = Make("B");
            var state = State(a, b);
            SetCompat(state, a, b, 100);
            var party = PartyOf(a, b);
            var boss = state.DungeonFields[0].Bosses[0];

            double without = DungeonResolver.CalculateBossPower(party, boss);
            double with = DungeonResolver.CalculateBossPower(party, boss, state);

            Assert.Equal(without * (1 + PersonalityBalance.BuddyPowerBonus), with, 6);
        }

        [Fact]
        public void Guard_SharesTheLossWhenABuddyIsInDanger()
        {
            var weak = Make("危ない子");
            weak.CurrentHP = 5; // どんな損耗でも重傷の基準を下回る
            var buddy = Make("相棒");
            var state = State(weak, buddy);
            SetCompat(state, weak, buddy, 100);
            var boss = state.DungeonFields[0].Bosses[0];
            var resolver = new DungeonResolver(new AlwaysMaxRng(), new AlwaysMinRng(), new AlwaysMinRng()); // かばい合いの判定は必ず通る

            var result = resolver.Resolve(PartyOf(weak, buddy), boss, state);

            var guard = Assert.Single(result.GuardEvents, g => g.ProtectedId == weak.Id);
            Assert.Equal(buddy.Id, guard.GuardianId);
            Assert.True(guard.Share > 0);
        }

        [Fact]
        public void Guard_DoesNotHappenWithoutABuddy()
        {
            var weak = Make("危ない子");
            weak.CurrentHP = 5;
            var other = Make("ふつうの仲");
            var state = State(weak, other);
            var boss = state.DungeonFields[0].Bosses[0];

            var result = new DungeonResolver(new AlwaysMaxRng(), new AlwaysMinRng(), new AlwaysMinRng()).Resolve(PartyOf(weak, other), boss, state);

            Assert.Empty(result.GuardEvents);
        }

        // ---------------- さぼり・不満の伝染 ----------------

        [Fact]
        public void RollSlackers_OnlyLowMoraleIdleWork()
        {
            var unhappyTrainee = Make("不満の訓練生", 30);
            var unhappyDiver = Make("不満の出撃", 30);
            var happyTrainee = Make("ふつうの訓練生", 60);
            var state = State(unhappyTrainee, unhappyDiver, happyTrainee);
            var activities = new Dictionary<Guid, WeekActivity>
            {
                [unhappyTrainee.Id] = WeekActivity.Training,
                [unhappyDiver.Id] = WeekActivity.Dispatched,
                [happyTrainee.Id] = WeekActivity.Training,
            };

            var slackers = MoraleSystem.RollSlackers(state, activities, new AlwaysMinRng()); // 1を引く＝必ずさぼる

            Assert.Equal(new[] { unhappyTrainee.Id }, slackers);
            Assert.Empty(MoraleSystem.RollSlackers(state, activities, new AlwaysMaxRng()));
        }

        [Fact]
        public void Contagion_LowersSquadmatesOfABreakingAdventurer()
        {
            var breaking = Make("限界", 10);
            var mate = Make("仲間", 60);
            var state = State(breaking, mate);
            var mission = new ActiveDungeonMission { Party = PartyOf(breaking, mate) };

            MoraleSystem.ApplyContagion(state, new[] { mission });

            Assert.Equal(60 + PersonalityBalance.MoraleBreakingContagion, mate.Satisfaction);
            Assert.Equal(10, breaking.Satisfaction); // 本人は伝染しない（限界の仲間がいないので）
        }

        // ---------------- 相性の素地の帯 ----------------

        [Theory]
        [InlineData(0, 10, AffinityBand.Resonant)]
        [InlineData(0, 40, AffinityBand.Harmonious)]
        [InlineData(0, 80, AffinityBand.Neutral)]
        [InlineData(0, 128, AffinityBand.Dissonant)]
        [InlineData(250, 5, AffinityBand.Resonant)] // 循環
        public void BandOf_UsesTheCircularDistance(int x, int y, AffinityBand expected) =>
            Assert.Equal(expected, MoraleSystem.BandOf(Make("A", p: P(Drive.Dive, affinity: x)), Make("B", p: P(Drive.Dive, affinity: y))));

        [Fact]
        public void BandOf_CharismaIsCloser_MoodyIsFarther_NoPersonalityIsNeutral()
        {
            var charismatic = Make("カリスマ", p: P(Drive.Dive, affinity: 0));
            charismatic.TryAddTrait(TraitCatalog.CharismaId);
            var moody = Make("気難しい", p: P(Drive.Dive, affinity: 0));
            moody.TryAddTrait(TraitCatalog.MoodyId);
            var far = Make("遠い", p: P(Drive.Dive, affinity: 128));
            var near = Make("近い", p: P(Drive.Dive, affinity: 10));

            Assert.Equal(AffinityBand.Neutral, MoraleSystem.BandOf(charismatic, far));
            Assert.Equal(AffinityBand.Harmonious, MoraleSystem.BandOf(moody, near));
            Assert.Equal(AffinityBand.Neutral, MoraleSystem.BandOf(Make("旧"), far));
        }

        [Fact]
        public void Compatibility_StartsAndGrowsByBand()
        {
            var a = Make("A", p: P(Drive.Dive, affinity: 0));
            var resonant = Make("共鳴", p: P(Drive.Dive, affinity: 5));
            var dissonant = Make("不協和", p: P(Drive.Dive, affinity: 128));
            var state = State(a, resonant, dissonant);

            Assert.Equal(PersonalityBalance.AffinityInitial[0], CompatibilitySystem.GetCompatibility(state, a.Id, resonant.Id));
            Assert.Equal(PersonalityBalance.AffinityInitial[3], CompatibilitySystem.GetCompatibility(state, a.Id, dissonant.Id));

            var compat = new CompatibilitySystem(new AlwaysMinRng());
            compat.ApplyExpeditionOutcome(state, PartyOf(a, resonant), DungeonMissionType.BossAssault, true);
            compat.ApplyExpeditionOutcome(state, PartyOf(a, dissonant), DungeonMissionType.BossAssault, true);

            int gain = CompatibilityBalance.ExpeditionGainBossVictory;
            Assert.Equal(PersonalityBalance.AffinityInitial[0] + (int)Math.Round(gain * PersonalityBalance.AffinityGain[0], MidpointRounding.AwayFromZero),
                CompatibilitySystem.GetCompatibility(state, a.Id, resonant.Id));
            Assert.Equal(PersonalityBalance.AffinityInitial[3] + (int)Math.Round(gain * PersonalityBalance.AffinityGain[3], MidpointRounding.AwayFromZero),
                CompatibilitySystem.GetCompatibility(state, a.Id, dissonant.Id));
        }

        [Fact]
        public void WeekProcessing_UpdatesTheStageAtTheEndOfTheWeek()
        {
            var a = Make("A", 60);
            a.Satisfaction = 10;
            var state = State(a);
            state.Gold = 100000;

            new WeekProcessingSystem(new MasterMoodSystem(), new EconomySystem(), new TrainingSystem(), new InjuryRecoverySystem(),
                new RestRecoverySystem(), new GrowthSystem(new SeededRng(1)), new SatisfactionSystem(), new AgingSystem(new SeededRng(2)),
                new FacilitySystem(), new DefeatSystem(), new RecruitmentSystem(new SeededRng(3))).ProcessWeek(state);

            Assert.Equal(MoraleStage.Breaking, a.MoraleStage);
        }
    }
}
