using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>部隊長の報告（§0.116）のテスト。</summary>
    public class SquadReportTests
    {
        private static GameState NewState()
        {
            var state = new GameState { WeekNumber = 5, DungeonFields = SampleData.CreateDefaultFields() };
            state.Adventurers.AddRange(SampleData.CreateStarterAdventurers()); // クラウディア・リナ・フィオナ
            return state;
        }

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            return party;
        }

        private static DungeonField Forest(GameState s) => s.DungeonFields.First(f => f.Id == "forest");

        private static (SquadReportKind Kind, Dictionary<string, string> Values) Classify(GameState s, List<DungeonMissionResolution> rs, MonthlyReport? report = null) =>
            SquadReportSystem.Classify(s, s.Adventurers[0], s.Adventurers, rs, report ?? new MonthlyReport());

        [Fact]
        public void Victory_ComesFirst_OverRetreat()
        {
            var s = NewState();
            var party = PartyOf(s.Adventurers.ToArray());
            var boss = Forest(s).Bosses[0];
            var retreat = new DungeonMissionResolution(party, boss, Forest(s), DungeonMissionType.Scouting) { ArrivedAtBossDoor = true, DoorRetreatReason = "勝ち目が薄い" };
            var won = new DungeonMissionResolution(party, boss, Forest(s), 1.0, new DungeonResult { Outcome = DungeonOutcome.Victory });
            var (kind, values) = Classify(s, new() { retreat, won });
            Assert.Equal(SquadReportKind.Victory, kind);
            Assert.Equal(boss.Name, values["{主}"]);

            (kind, values) = Classify(s, new() { retreat });
            Assert.Equal(SquadReportKind.Retreat, kind);
            Assert.Equal(boss.Name, values["{主}"]);
        }

        [Fact]
        public void Gather_SumsMaterials_AndIdleWhenNothingHappened()
        {
            var s = NewState();
            var party = PartyOf(s.Adventurers.ToArray());
            var g1 = new DungeonMissionResolution(party, Forest(s), new GatheringResult { MaterialId = MaterialIds.ForestHerb, MaterialCount = 3 });
            var g2 = new DungeonMissionResolution(party, Forest(s), new GatheringResult { MaterialId = MaterialIds.ForestHerb, MaterialCount = 2 });
            var (kind, values) = Classify(s, new() { g1, g2 });
            Assert.Equal(SquadReportKind.Gather, kind);
            Assert.Equal($"{MaterialBalance.GetName(MaterialIds.ForestHerb)}×5", values["{成果}"]);

            Assert.Equal(SquadReportKind.Idle, Classify(s, new()).Kind);
        }

        [Fact]
        public void Growth_OfAMemberOtherThanTheLeader_AtLeastTheThreshold()
        {
            var s = NewState();
            var report = new MonthlyReport();
            report.Growth.Add(new MonthlyGrowth { Name = s.Adventurers[0].Name, Gains = { ["STR"] = 10 } }); // 部隊長は数えない
            report.Growth.Add(new MonthlyGrowth { Name = s.Adventurers[1].Name, Gains = { ["AGI"] = SquadReportBalance.GrowthMinGain - 1 } });
            Assert.Equal(SquadReportKind.Idle, Classify(s, new(), report).Kind);

            report.Growth[1].Gains["DEX"] = 1;
            var (kind, values) = Classify(s, new(), report);
            Assert.Equal(SquadReportKind.Growth, kind);
            Assert.Equal(s.Adventurers[1].Name, values["{名前}"]);
        }

        [Fact]
        public void StoryCharacters_UseTheirOwnLines_OthersUseTemperAndDrive()
        {
            var s = NewState();
            var claudia = s.Adventurers[0];
            Assert.Equal("claudia", SquadReportSystem.CharacterKey(claudia));
            Assert.Contains(SquadReportSystem.PickLine(claudia, SquadReportKind.Victory, 0), SquadReportBalance.Get("Victory", "char:claudia"));

            var recruit = new Adventurer { Name = "ミレイ", PortraitId = "p_001", Personality = new Personality { Main = Drive.Money, Temperament = 2 } };
            Assert.Null(SquadReportSystem.CharacterKey(recruit));
            Assert.Equal("bold", SquadReportSystem.TemperKey(recruit));
            var pool = SquadReportBalance.Get("Victory", "temper:bold").Concat(SquadReportBalance.Get("Victory", "drive:Money")).ToList();
            var picked = Enumerable.Range(0, pool.Count).Select(t => SquadReportSystem.PickLine(recruit, SquadReportKind.Victory, t)).ToList();
            Assert.Equal(pool.OrderBy(x => x), picked.OrderBy(x => x)); // 月ごとに回して全部使う

            Assert.Equal("cautious", SquadReportSystem.TemperKey(new Adventurer { Personality = new Personality { Temperament = -1 } }));
            Assert.Equal("normal", SquadReportSystem.TemperKey(new Adventurer())); // 個性の無い子は中庸
        }

        [Theory]
        [InlineData(SquadReportKind.Victory, new[] { "{主}" })]
        [InlineData(SquadReportKind.Defeat, new[] { "{主}" })]
        [InlineData(SquadReportKind.Injury, new[] { "{名前}" })]
        [InlineData(SquadReportKind.Retreat, new[] { "{主}" })]
        [InlineData(SquadReportKind.Growth, new[] { "{名前}" })]
        [InlineData(SquadReportKind.Survey, new[] { "{主}", "{解析}" })]
        [InlineData(SquadReportKind.Gather, new[] { "{成果}" })]
        [InlineData(SquadReportKind.Dive, new[] { "{フィールド}", "{階}" })]
        [InlineData(SquadReportKind.Idle, new string[0])]
        public void EveryKind_HasLinesForEachTemperAndCharacter_UsingOnlyItsPlaceholders(SquadReportKind kind, string[] placeholders)
        {
            var speakers = new[] { "temper:cautious", "temper:normal", "temper:bold", "char:claudia", "char:rina", "char:fiona" };
            foreach (var speaker in speakers)
                Assert.NotEmpty(SquadReportBalance.Get(kind.ToString(), speaker));
            var all = speakers.Concat(Enum.GetNames<Drive>().Select(d => $"drive:{d}")).SelectMany(sp => SquadReportBalance.Get(kind.ToString(), sp));
            foreach (var line in all)
            {
                string rest = placeholders.Aggregate(line, (t, p) => t.Replace(p, ""));
                Assert.DoesNotContain("{", rest);
            }
        }

        [Fact]
        public void MonthlyReport_HasAReportPerOrderedSquad_AndItSurvivesTheSave()
        {
            var s = NewState();
            s.SavedParties.Add(new SavedParty { Name = "第1部隊", MemberIds = { s.Adventurers[1].Id, s.Adventurers[0].Id }, Order = SquadOrder.Dive });
            s.SavedParties.Add(new SavedParty { Name = "第2部隊", MemberIds = { s.Adventurers[2].Id }, Order = SquadOrder.None }); // 方針の無い部隊は報告しない
            var report = MonthlyReport.Build(s, new List<AutoSkipWeek>(), 0);

            var r = Assert.Single(report.LeaderReports);
            Assert.Equal("第1部隊", r.SquadName);
            Assert.Equal(s.Adventurers[1].Name, r.LeaderName); // 部隊の1人目＝部隊長
            Assert.Equal(SquadReportKind.Idle, r.Kind);
            Assert.Contains(r.Line, SquadReportBalance.Get("Idle", "char:rina"));

            s.MonthlyReports.Add(report);
            s.HideSquadReports = true;
            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(s.ToSaveData()))!);
            Assert.True(restored.HideSquadReports);
            Assert.Equal(r.Line, restored.MonthlyReports.Single().LeaderReports.Single().Line);

            var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(s.ToSaveData()))!;
            node.AsObject().Remove("HideSquadReports"); // §0.116 より前のセーブ
            Assert.False(GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(node.ToJsonString())!).HideSquadReports);
        }
    }
}
