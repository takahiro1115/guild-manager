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
    /// <summary>冒険者の個性（段4a、§0.100）：志向・気質・相性の素地の決め方、週の満足度、見立て、セーブのテスト。</summary>
    public class PersonalityTests
    {
        private static Adventurer Make(string name, Personality? p = null)
        {
            var a = new Adventurer { Name = name, JobClass = JobClass.Warrior, STR = 40, AGI = 40, VIT = 40, MND = 40, DEX = 40, LDR = 40, INT = 40, WeeklyWage = 1000, Satisfaction = 50 };
            a.CurrentHP = a.MaxHP;
            a.Personality = p;
            return a;
        }

        private static Personality P(Drive main, Drive? sub = null, Drive? disliked = null, int temperament = 0, int lastFame = 1) =>
            new() { Main = main, Sub = sub, Disliked = disliked, Temperament = temperament, LastFameWeek = lastFame };

        private static GameState State(params Adventurer[] adventurers)
        {
            var state = new GameState { WeekNumber = 5, DungeonFields = SampleData.CreateDefaultFields() };
            state.Adventurers.AddRange(adventurers);
            return state;
        }

        private static ActiveDungeonMission Mission(DungeonMissionType type, params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            return new ActiveDungeonMission { Party = party, MissionType = type };
        }

        private static List<PersonalityDiscovery> Week(GameState state, IReadOnlyList<ActiveDungeonMission>? missions = null,
            IReadOnlyList<DungeonMissionResolution>? resolutions = null, Dictionary<Guid, WeekActivity>? activities = null) =>
            PersonalitySystem.ProcessWeek(state, state.WeekNumber, missions ?? new List<ActiveDungeonMission>(),
                resolutions ?? new List<DungeonMissionResolution>(), activities ?? new Dictionary<Guid, WeekActivity>());

        // ---------------- 決め方 ----------------

        [Fact]
        public void Assign_FollowsTheShapeRules()
        {
            for (int seed = 0; seed < 300; seed++)
            {
                var a = Make("新人");
                var p = PersonalitySystem.Assign(State(), a, new SeededRng(seed));

                Assert.Same(p, a.Personality);
                Assert.NotEqual(p.Main, p.Sub);
                if (p.Disliked is Drive d)
                {
                    Assert.Contains(d, PersonalitySystem.WorkDrives);
                    Assert.NotEqual(p.Main, d);
                    Assert.NotEqual(p.Sub, d);
                }
                Assert.InRange(p.Temperament, -2, 2);
                Assert.InRange(p.Affinity, 0, 255);
            }
        }

        [Fact]
        public void Assign_IsDeterministicForTheSameSeed()
        {
            var p1 = PersonalitySystem.Assign(State(), Make("A"), new SeededRng(42));
            var p2 = PersonalitySystem.Assign(State(), Make("B"), new SeededRng(42));

            Assert.Equal((p1.Main, p1.Sub, p1.Disliked, p1.Temperament, p1.Affinity, p1.HintDrive), (p2.Main, p2.Sub, p2.Disliked, p2.Temperament, p2.Affinity, p2.HintDrive));
        }

        [Fact]
        public void Assign_RatesAreRoughlyAsInTheCsv()
        {
            int n = 2000, sub = 0, disliked = 0, hintTrue = 0;
            for (int seed = 0; seed < n; seed++)
            {
                var p = PersonalitySystem.Assign(State(), Make("新人"), new SeededRng(seed * 7919 + 1));
                if (p.Sub != null) sub++;
                if (p.Disliked != null) disliked++;
                if (p.HintDrive == p.Main) hintTrue++;
            }
            Assert.InRange(sub / (double)n, PersonalityBalance.SubDriveChancePercent / 100.0 - 0.05, PersonalityBalance.SubDriveChancePercent / 100.0 + 0.05);
            Assert.InRange(disliked / (double)n, PersonalityBalance.DislikeChancePercent / 100.0 - 0.05, PersonalityBalance.DislikeChancePercent / 100.0 + 0.05);
            Assert.InRange(hintTrue / (double)n, PersonalityBalance.HintTruthPercent / 100.0 - 0.05, PersonalityBalance.HintTruthPercent / 100.0 + 0.05);
        }

        [Fact]
        public void Assign_TraitsPullTheTemperament()
        {
            for (int seed = 0; seed < 100; seed++)
            {
                var reckless = Make("猪");
                reckless.TryAddTrait(TraitCatalog.RecklessId);
                Assert.True(PersonalitySystem.Assign(State(), reckless, new SeededRng(seed)).Temperament >= 1);
                var coward = Make("臆");
                coward.TryAddTrait(TraitCatalog.CowardId);
                Assert.True(PersonalitySystem.Assign(State(), coward, new SeededRng(seed)).Temperament <= -1);
            }
        }

        [Fact]
        public void Recruits_AndStarters_HaveAPersonality()
        {
            var state = new GameState { WeekNumber = 1, DungeonFields = SampleData.CreateDefaultFields() };
            var offers = new RecruitmentSystem(new SeededRng(3)).GenerateCandidates(state, candidateCount: 3);

            Assert.All(offers, o => Assert.NotNull(o.Candidate.Personality));
            Assert.All(SampleData.CreateStarterAdventurers(), a => Assert.NotNull(a.Personality));
        }

        // ---------------- 相性の素地（円の上） ----------------

        [Theory]
        [InlineData(0, 255, 1)]
        [InlineData(255, 0, 1)]
        [InlineData(10, 138, 128)]
        [InlineData(100, 120, 20)]
        public void AffinityDistance_WrapsAround(int a, int b, int expected) =>
            Assert.Equal(expected, PersonalitySystem.AffinityDistance(a, b));

        [Theory]
        [InlineData(250, 10, 2)]
        [InlineData(10, 250, 2)]
        [InlineData(100, 120, 110)]
        [InlineData(0, 128, 64)]
        public void CircularMidpoint_TakesTheNearSide(int a, int b, int expected) =>
            Assert.Equal(expected, PersonalitySystem.CircularMidpoint(a, b));

        [Fact]
        public void AssignChild_InheritsAffinityNearTheParentsMidpoint()
        {
            var mother = Make("母", new Personality { Affinity = 250 });
            var other = Make("母2", new Personality { Affinity = 10 });
            for (int seed = 0; seed < 50; seed++)
            {
                var child = Make("娘");
                var p = PersonalitySystem.AssignChild(State(), child, mother, other, new SeededRng(seed));
                Assert.True(PersonalitySystem.AffinityDistance(p.Affinity, 2) <= PersonalityBalance.AffinityInheritSpread);
            }
        }

        // ---------------- 週の満足度 ----------------

        [Fact]
        public void LikedWork_RaisesSatisfaction_DislikedWorkLowersIt()
        {
            var gatherer = Make("収穫派", P(Drive.Gather));
            var subGatherer = Make("副", P(Drive.Dive, sub: Drive.Gather));
            var hater = Make("嫌い", P(Drive.Dive, disliked: Drive.Training));
            var state = State(gatherer, subGatherer, hater);

            Week(state, new[] { Mission(DungeonMissionType.Gathering, gatherer, subGatherer) },
                activities: new Dictionary<Guid, WeekActivity> { [hater.Id] = WeekActivity.Training });

            Assert.Equal(50 + PersonalityBalance.SatisfactionMain, gatherer.Satisfaction);
            Assert.Equal(50 + PersonalityBalance.SatisfactionSub, subGatherer.Satisfaction);
            Assert.Equal(50 + PersonalityBalance.SatisfactionDisliked, hater.Satisfaction);
            Assert.Equal(1, gatherer.Personality!.Exposure["Gather"]);
            Assert.Equal(1, hater.Personality!.Exposure["Training"]);
        }

        [Fact]
        public void NoPersonality_IsLeftAlone()
        {
            var old = Make("旧セーブの子");
            var state = State(old);

            Week(state, new[] { Mission(DungeonMissionType.Gathering, old) });

            Assert.Equal(50, old.Satisfaction);
        }

        [Fact]
        public void Comrades_LikeAFriendInTheSquad_AndHateAHostileOne()
        {
            var friendly = Make("仲間思い", P(Drive.Comrades));
            var friend = Make("友");
            var lonely = Make("仲間思い2", P(Drive.Comrades));
            var foe = Make("犬猿");
            var state = State(friendly, friend, lonely, foe);
            state.Compatibility[CompatibilitySystem.NormalizeKey(friendly.Id, friend.Id)] = PersonalityBalance.ComradesFriendThreshold;
            state.Compatibility[CompatibilitySystem.NormalizeKey(lonely.Id, foe.Id)] = CompatibilityBalance.HostileThreshold - 1;

            Week(state, new[] { Mission(DungeonMissionType.Survey, friendly, friend), Mission(DungeonMissionType.Survey, lonely, foe) });

            Assert.Equal(50 + PersonalityBalance.SatisfactionMain, friendly.Satisfaction);
            Assert.Equal(50 + PersonalityBalance.MotiveNegativeMain, lonely.Satisfaction);
        }

        [Fact]
        public void Money_LikesARaiseOrBonus_OnTheNextSettlement()
        {
            var greedy = Make("稼ぎ屋", P(Drive.Money));
            var state = State(greedy);
            state.Gold = 100000;

            new SatisfactionSystem().PayBonus(state, greedy);
            Assert.Equal(1, greedy.Personality!.PendingMoneyJoy);
            Week(state);

            Assert.Equal(50 + PersonalityBalance.SatisfactionMain, greedy.Satisfaction);
            Assert.Equal(0, greedy.Personality.PendingMoneyJoy);
        }

        [Fact]
        public void Fame_FrettsAfterADrought_OncePerSeason()
        {
            var glory = Make("功名派", P(Drive.Fame, lastFame: 1));
            var state = State(glory);

            state.WeekNumber = 1 + PersonalityBalance.FameDroughtWeeks;
            Week(state);
            Assert.Equal(50 + PersonalityBalance.MotiveNegativeMain, glory.Satisfaction);

            state.WeekNumber++;
            Week(state);
            Assert.Equal(50 + PersonalityBalance.MotiveNegativeMain, glory.Satisfaction); // 次の季節まで焦らない
        }

        [Theory]
        [InlineData(DoorStance.Bold, 1, 1)]       // 強気×大胆＝合う
        [InlineData(DoorStance.Bold, -2, -2)]     // 強気×とても慎重＝合わない（2倍）
        [InlineData(DoorStance.Cautious, -1, 1)]  // 慎重×慎重＝合う
        [InlineData(DoorStance.Standard, 2, 0)]   // 標準は動かない
        public void Temperament_ReactsToTheStanceAtTheDoor(DoorStance stance, int temperament, int expectedSign)
        {
            var a = Make("気質", P(Drive.Survey, temperament: temperament));
            var state = State(a);
            var saved = new SavedParty { Name = "主力", Stance = stance, MemberIds = { a.Id } };
            state.SavedParties.Add(saved);
            var mission = Mission(DungeonMissionType.BossAssault, a);
            mission.SavedPartyId = saved.Id;
            var door = new DungeonMissionResolution(mission.Party, null, state.DungeonFields[0], DungeonMissionType.BossAssault) { ArrivedAtBossDoor = true };

            Week(state, new[] { mission }, new[] { door });

            // 主志向は調査なので、潜行に出たこと自体では満足度は動かない（気質の分だけ）
            int expected = expectedSign switch
            {
                > 0 => PersonalityBalance.TemperamentMatch,
                < 0 => PersonalityBalance.TemperamentMismatch * Math.Abs(temperament),
                _ => 0,
            };
            Assert.Equal(50 + expected, a.Satisfaction);
            Assert.Equal(1, a.Personality!.Exposure[PersonalitySystem.TemperamentKey]);
        }

        // ---------------- 見立て ----------------

        [Fact]
        public void Reveal_LikelyThenSure()
        {
            var a = Make("収穫派", P(Drive.Gather));
            var state = State(a);
            var (likely, sure) = PersonalitySystem.RevealThresholds(state, a);
            Assert.Equal((PersonalityBalance.RevealLikely, PersonalityBalance.RevealSure), (likely, sure)); // 目利き0

            var found = new List<PersonalityDiscovery>();
            for (int i = 0; i < sure; i++)
                found.AddRange(Week(state, new[] { Mission(DungeonMissionType.Gathering, a) }));

            Assert.Equal(new[] { PersonalityKnowledge.Likely, PersonalityKnowledge.Sure }, found.Select(f => f.Level));
            Assert.Equal(PersonalityKnowledge.Sure, PersonalitySystem.Knowledge(state, a, Drive.Gather));
            Assert.Equal(PersonalityKnowledge.Unknown, PersonalitySystem.Knowledge(state, a, Drive.Dive));
            Assert.Contains("採取がいちばん好きだと分かった", PersonalitySystem.DiscoveryText(found[1]));
        }

        [Fact]
        public void Reveal_IsFasterForSoulFusionDaughters()
        {
            var child = Make("娘", P(Drive.Gather));
            child.ParentIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
            var (likely, sure) = PersonalitySystem.RevealThresholds(State(child), child);

            Assert.Equal(Math.Max(1, (int)Math.Round(PersonalityBalance.RevealLikely * 0.5)), likely);
            Assert.Equal(Math.Max(likely, (int)Math.Round(PersonalityBalance.RevealSure * 0.5)), sure);
        }

        [Fact]
        public void DiscoveryText_IsSilentForNeutralDrives()
        {
            var a = Make("ふつう", P(Drive.Dive));
            Assert.Null(PersonalitySystem.DiscoveryText(new PersonalityDiscovery(a, "Gather", PersonalityKnowledge.Sure)));
            Assert.NotNull(PersonalitySystem.DiscoveryText(new PersonalityDiscovery(a, "Dive", PersonalityKnowledge.Likely)));
        }

        // ---------------- セーブ ----------------

        [Fact]
        public void Personality_RoundTrips_AndOldSavesReadNull()
        {
            var a = Make("A", P(Drive.Money, Drive.Fame, Drive.Training, temperament: -1));
            a.Personality!.Affinity = 200;
            a.Personality.Exposure["Money"] = 3;
            var state = State(a);

            var json = JsonSerializer.Serialize(state.ToSaveData());
            var back = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!).Adventurers.Single().Personality!;
            Assert.Equal((Drive.Money, (Drive?)Drive.Fame, (Drive?)Drive.Training, -1, 200, 3), (back.Main, back.Sub, back.Disliked, back.Temperament, back.Affinity, back.Exposure["Money"]));

            var noPersonality = State(Make("旧"));
            var oldJson = JsonSerializer.Serialize(noPersonality.ToSaveData()).Replace("\"Personality\":null,", "").Replace(",\"Personality\":null", "");
            Assert.DoesNotContain("\"Personality\"", oldJson);
            Assert.Null(GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(oldJson)!).Adventurers.Single().Personality);
        }

        [Fact]
        public void SaveWithTheRemovedFocusList_StillLoads()
        {
            // §0.151：副官の注目（§0.117）は外した。注目の一覧が残った旧セーブも、その項目を無視して読める
            var state = State(Make("A", P(Drive.Dive)));
            var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(state.ToSaveData()))!;
            node["FocusedAdventurerIds"] = new System.Text.Json.Nodes.JsonArray(state.Adventurers[0].Id.ToString());
            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(node.ToJsonString())!);
            Assert.Equal("A", restored.Adventurers.Single().Name);
        }
    }
}
