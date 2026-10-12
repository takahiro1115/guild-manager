using System;
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
    /// <summary>出来事と事故（段4c、§0.102）：満足度・相性の出来事、引き抜きの話、ボイコット・編成の拒否・命令無視、⚠ の危険のテスト。</summary>
    public class IncidentTests
    {
        private sealed class AlwaysMinRng : IRng { public int NextInt(int min, int max) => min; }
        private sealed class AlwaysMaxRng : IRng { public int NextInt(int min, int max) => max; }

        /// <summary>渡した値を順に返す（範囲に丸める）。尽きたら最小値。</summary>
        private sealed class SequenceRng : IRng
        {
            private readonly Queue<int> _values;
            public SequenceRng(params int[] values) => _values = new Queue<int>(values);
            public int NextInt(int min, int max) => _values.Count == 0 ? min : Math.Clamp(_values.Dequeue(), min, max);
        }

        private static Adventurer Make(string name, int satisfaction = 60, int age = 20, int temperament = 0, int stat = 50)
        {
            var a = new Adventurer
            {
                Name = name, JobClass = JobClass.Warrior, Age = age, Satisfaction = satisfaction, WeeklyWage = 50,
                STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat,
                Personality = new Personality { Main = Drive.Gather, Temperament = temperament },
            };
            a.CurrentHP = a.MaxHP;
            MoraleSystem.UpdateStage(a);
            return a;
        }

        private static GameState State(params Adventurer[] adventurers)
        {
            var state = new GameState { WeekNumber = 10, Gold = 10000, DungeonFields = SampleData.CreateDefaultFields() };
            state.Adventurers.AddRange(adventurers);
            return state;
        }

        private static SavedParty Squad(GameState state, params Adventurer[] members)
        {
            var saved = new SavedParty { Name = "主力", MemberIds = members.Select(m => m.Id).ToList() };
            state.SavedParties.Add(saved);
            return saved;
        }

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            return party;
        }

        private static void SetCompat(GameState state, Adventurer a, Adventurer b, int value) =>
            state.Compatibility[CompatibilitySystem.NormalizeKey(a.Id, b.Id)] = value;

        private static List<IncidentEvent> Week(GameState state, IRng rng, out bool poach) =>
            IncidentSystem.ProcessWeek(state, state.WeekNumber, new List<ActiveDungeonMission>(), rng, out poach);

        // ---------------- 満足度の出来事 ----------------

        [Fact]
        public void NoEvent_WhenTheRollMisses()
        {
            var a = Make("限界", 10);
            var state = State(a);

            Assert.Empty(Week(state, new AlwaysMaxRng(), out _));
        }

        [Fact]
        public void Discontent_Quarrel_LowersCompatibilityAndSatisfaction()
        {
            var a = Make("不満", 30);
            var mate = Make("仲間", 60);
            var state = State(a, mate);
            Squad(state, a, mate);
            SetCompat(state, a, mate, 50);

            var events = Week(state, new AlwaysMinRng(), out _); // 起きる・いちばん目の出来事（口論）

            Assert.Contains(events, e => e.Kind == IncidentKind.Quarrel);
            int loss = (int)Math.Round(PersonalityBalance.QuarrelCompatLoss * MoraleSystem.LossMultiplier(MoraleSystem.BandOf(a, mate)), MidpointRounding.AwayFromZero); // 2人とも素地0＝共鳴
            Assert.Equal(50 - loss, CompatibilitySystem.GetCompatibility(state, a.Id, mate.Id));
            Assert.Equal(30 + PersonalityBalance.QuarrelSatisfaction, a.Satisfaction);
        }

        [Fact]
        public void Cheerful_Mentor_HelpsTheYoungestSquadmate()
        {
            var senior = Make("先輩", 90, age: 24);
            var junior = Make("後輩", 60, age: 18);
            var state = State(senior, junior);
            Squad(state, senior, junior);
            SetCompat(state, senior, junior, 50);

            // 先輩：起きる→面倒（いちばん目）。後輩：ふつうの子は起きない（100を引く）
            var events = Week(state, new SequenceRng(1, 1, 100), out _);

            var ev = Assert.Single(events);
            Assert.Equal(IncidentKind.Mentor, ev.Kind);
            Assert.Equal(50 + PersonalityBalance.MentorCompatGain, CompatibilitySystem.GetCompatibility(state, senior.Id, junior.Id));
            Assert.Equal(60 + PersonalityBalance.MentorSatisfaction, junior.Satisfaction);
        }

        [Fact]
        public void Gloom_CountsDown_AndKeepsTheChildFromSorties()
        {
            var a = Make("ふさぎ込み");
            a.GloomWeeksRemaining = 2;
            var state = State(a);

            Assert.Empty(PartyFormationSystem.BuildDispatchParty(state, new[] { a.Id }).Members);
            Week(state, new AlwaysMaxRng(), out _);
            Assert.Equal(1, a.GloomWeeksRemaining);
        }

        [Fact]
        public void PumpedAndFretful_LastOnlyUntilTheNextSortie()
        {
            var a = Make("張り切り");
            double basePower = DungeonPowerCalculator.MemberPower(a);
            a.Pumped = true;
            a.Fretful = true;
            Assert.Equal(basePower * (1 + PersonalityBalance.PumpedPowerBonus), DungeonPowerCalculator.MemberPower(a), 6);

            var state = State(a);
            IncidentSystem.ProcessWeek(state, state.WeekNumber, new[] { new ActiveDungeonMission { Party = PartyOf(a) } }, new AlwaysMaxRng(), out _);

            Assert.False(a.Pumped);
            Assert.False(a.Fretful);
        }

        // ---------------- 引き抜きの話 ----------------

        private static (GameState State, Adventurer A) PoachSetup()
        {
            var a = Make("限界の子", 10);
            var state = State(a);
            state.RivalGuilds.Add(new RivalGuild { Id = "g", Name = "白百合の杖" });
            return (state, a);
        }

        [Fact]
        public void Breaking_CanGetAPoachOffer_ThatStopsTheMonth()
        {
            var (state, a) = PoachSetup();

            // 起きる → 重みの最後（引き抜き）
            var events = Week(state, new SequenceRng(1, 1000), out bool poach);

            Assert.True(poach);
            Assert.Contains(events, e => e.Kind == IncidentKind.Poach);
            var offer = Assert.Single(state.PoachOffers);
            Assert.Equal(a.Id, offer.AdventurerId);
            Assert.Equal(state.WeekNumber + PersonalityBalance.PoachDeadlineWeeks, offer.DeadlineWeek);
            Assert.True(new WeekResult { PoachOffered = true }.ShouldStopMonth);
        }

        [Fact]
        public void PoachOffer_LeavesAtTheDeadline_IfUnanswered()
        {
            var (state, a) = PoachSetup();
            state.PoachOffers.Add(new PoachOffer { AdventurerId = a.Id, AdventurerName = a.Name, GuildName = "白百合の杖", DeadlineWeek = state.WeekNumber });

            var events = Week(state, new AlwaysMaxRng(), out _);

            Assert.Contains(events, e => e.Kind == IncidentKind.PoachLeft);
            Assert.DoesNotContain(a, state.Adventurers);
            Assert.Empty(state.PoachOffers);
        }

        [Fact]
        public void ResolvePoach_RaiseKeepsHer_ReleaseLetsHerGo()
        {
            var (state, a) = PoachSetup();
            var offer = new PoachOffer { AdventurerId = a.Id, AdventurerName = a.Name, GuildName = "白百合の杖", DeadlineWeek = 99 };
            state.PoachOffers.Add(offer);

            Assert.True(IncidentSystem.ResolvePoach(state, offer, PoachChoice.Raise, new SatisfactionSystem()));
            Assert.Contains(a, state.Adventurers);
            Assert.Equal((int)Math.Round(50 * SatisfactionBalance.MinRaiseMultiplier), a.WeeklyWage);
            Assert.Equal(10 + PersonalityBalance.PoachRaiseSatisfaction, a.Satisfaction);

            var b = Make("もう1人", 10);
            state.Adventurers.Add(b);
            var offer2 = new PoachOffer { AdventurerId = b.Id, AdventurerName = b.Name, GuildName = "白百合の杖", DeadlineWeek = 99 };
            state.PoachOffers.Add(offer2);
            IncidentSystem.ResolvePoach(state, offer2, PoachChoice.Release, new SatisfactionSystem());
            Assert.DoesNotContain(b, state.Adventurers);
        }

        [Fact]
        public void PoachOffers_RoundTripInTheSave()
        {
            var (state, a) = PoachSetup();
            state.PoachOffers.Add(new PoachOffer { AdventurerId = a.Id, AdventurerName = a.Name, GuildName = "白百合の杖", DeadlineWeek = 12 });

            var back = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(state.ToSaveData()))!);

            var offer = Assert.Single(back.PoachOffers);
            Assert.Equal((a.Id, "白百合の杖", 12), (offer.AdventurerId, offer.GuildName, offer.DeadlineWeek));
        }

        // ---------------- 相性の出来事 ----------------

        [Fact]
        public void Buddies_Bond_AndFoes_Brawl()
        {
            var x = Make("X");
            var y = Make("Y");
            var p = Make("P");
            var q = Make("Q");
            var state = State(x, y, p, q);
            Squad(state, x, y);
            Squad(state, p, q);
            SetCompat(state, x, y, 90);
            SetCompat(state, p, q, 20);

            // 子ごとの出来事は起きない（100×4）→ 相棒の組：起きる（1）→ 険悪の組：起きる（1）
            var events = Week(state, new SequenceRng(100, 100, 100, 100, 1, 1), out _);

            Assert.Contains(events, e => e.Kind == IncidentKind.Bond);
            Assert.Contains(events, e => e.Kind == IncidentKind.Brawl);
            Assert.Equal(90 + PersonalityBalance.BondCompatGain, CompatibilitySystem.GetCompatibility(state, x.Id, y.Id));
            Assert.Equal(InjurySeverity.Light, p.Injury);
            Assert.Equal(InjurySeverity.Light, q.Injury);
        }

        // ---------------- ボイコット・編成の拒否 ----------------

        [Fact]
        public void Feud_BlocksAssignment_AndRefusesTheSortie()
        {
            var a = Make("A", 50);
            var b = Make("B", 60);
            var state = State(a, b);
            SetCompat(state, a, b, PersonalityBalance.FeudThreshold - 1);
            var saved = Squad(state, b);

            Assert.NotNull(IncidentSystem.AssignBlockReason(state, saved, a.Id));
            Assert.False(new PartyFormationSystem().TryAssignMember(state, saved, a.Id));

            var refused = IncidentSystem.RollBoycotts(state, PartyOf(a, b), Drive.Dive, new AlwaysMaxRng());
            var r = Assert.Single(refused);
            Assert.Same(a, r.Adventurer); // 満足度の低い方が拒む
            Assert.Equal(IncidentKind.Refuse, r.Kind);
        }

        [Fact]
        public void Boycott_OnlyLowMorale_OrHostile()
        {
            var unhappy = Make("不満", 30);
            var fine = Make("ふつう", 60);
            var state = State(unhappy, fine);

            var refused = IncidentSystem.RollBoycotts(state, PartyOf(unhappy, fine), Drive.Dive, new AlwaysMinRng());

            var r = Assert.Single(refused);
            Assert.Same(unhappy, r.Adventurer);
            Assert.Equal(IncidentKind.Boycott, r.Kind);
            Assert.Empty(IncidentSystem.RollBoycotts(state, PartyOf(unhappy, fine), Drive.Dive, new AlwaysMaxRng()));
        }

        // ---------------- 命令無視 ----------------

        [Fact]
        public void Disobey_CautiousUnhappyChildFleesFromAnEngagement()
        {
            var leader = Make("リーダー", 60, stat: 10);
            var timid = Make("慎重", 30, temperament: -1);
            var state = State(leader, timid);

            var result = IncidentSystem.RollDisobey(state, PartyOf(leader, timid), go: true, new AlwaysMinRng());

            Assert.NotNull(result);
            Assert.Same(timid, result!.Value.Who);
            Assert.False(result.Value.Charge);
        }

        [Fact]
        public void Disobey_BoldChildChargesWhenRetreating_ButNotUnderACharismaticLeader()
        {
            var leader = Make("リーダー", 60, stat: 10);
            var bold = Make("大胆", 30, temperament: 2);
            var state = State(leader, bold);

            var charge = IncidentSystem.RollDisobey(state, PartyOf(leader, bold), go: false, new AlwaysMinRng());
            Assert.True(charge!.Value.Charge);
            Assert.Null(IncidentSystem.RollDisobey(state, PartyOf(leader, bold), go: true, new AlwaysMinRng())); // 判断と同じ向きなら従う

            leader.TryAddTrait(TraitCatalog.CharismaId);
            Assert.Null(IncidentSystem.RollDisobey(state, PartyOf(leader, bold), go: false, new AlwaysMinRng()));
        }

        [Fact]
        public void Disobey_ContentChildrenAlwaysObey()
        {
            var leader = Make("リーダー", 60, stat: 10);
            var timid = Make("慎重", 60, temperament: -2);
            var state = State(leader, timid);

            Assert.Null(IncidentSystem.RollDisobey(state, PartyOf(leader, timid), go: true, new AlwaysMinRng()));
        }

        // ---------------- 危険の表示 ----------------

        [Fact]
        public void Risks_ListLowMoraleAndBadPairs()
        {
            var unhappy = Make("不満", 30);
            var a = Make("A");
            var b = Make("B");
            var state = State(unhappy, a, b);
            SetCompat(state, a, b, PersonalityBalance.FeudThreshold - 1);

            var risks = IncidentSystem.Risks(state, new[] { unhappy, a, b });

            Assert.Contains(risks, r => r.Contains("不満") && r.Contains("出撃しない"));
            Assert.Contains(risks, r => r.Contains("犬猿"));
            Assert.Empty(IncidentSystem.Risks(state, new[] { Make("C"), Make("D") }));
        }
    }
}
