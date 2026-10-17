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
        public void Survey_WithNoProgress_ReportsTheFailure_NotProgress()
        {
            // 護衛が足りず解析が進まなかった月に「見えてきた！　解析0%」と言わない（§0.147）
            var s = NewState();
            var party = PartyOf(s.Adventurers.ToArray());
            var boss = Forest(s).Bosses[0];
            var stuck = new DungeonMissionResolution(party, boss, Forest(s), 0, new ScoutingResult { IntelRateBefore = 0, IntelGained = 0, IntelRateAfter = 0 });
            var (kind, values) = Classify(s, new() { stuck });
            Assert.Equal(SquadReportKind.SurveyFailed, kind);
            Assert.Equal("0", values["{解析}"]);

            var moved = new DungeonMissionResolution(party, boss, Forest(s), 0, new ScoutingResult { IntelRateBefore = 0, IntelGained = 0.2, IntelRateAfter = 0.2 });
            Assert.Equal(SquadReportKind.Survey, Classify(s, new() { moved }).Kind);
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
        [InlineData(SquadReportKind.SurveyFailed, new[] { "{主}", "{解析}" })]
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
                string rest = placeholders.Append("{部隊長}").Aggregate(line, (t, p) => t.Replace(p, ""));
                Assert.DoesNotContain("{", rest);
            }
        }

        [Fact]
        public void EveryLine_IsANarrationThenSpeech_AndRinaDoesNotDrawOutHerEndings()
        {
            // §0.124：1行は「〔地の文〕｜台詞｜台詞」。地の文で始まり、台詞が1段以上
            var kinds = Enum.GetNames<SquadReportKind>();
            var speakers = new[] { "temper:cautious", "temper:normal", "temper:bold", "char:claudia", "char:rina", "char:fiona" }
                .Concat(Enum.GetNames<Drive>().Select(d => $"drive:{d}"));
            var report = new SquadLeaderReport { LeaderName = "ミレイ", SquadName = "第1部隊" };
            foreach (var kind in kinds)
                foreach (var speaker in speakers)
                    foreach (var line in SquadReportBalance.Get(kind, speaker))
                    {
                        report.Line = line;
                        var lines = SquadReportSystem.Lines(report);
                        Assert.Equal(StoryLineKind.Narration, lines[0].Kind);
                        Assert.Contains(lines.Skip(1), l => l.Kind == StoryLineKind.Speech);
                        if (speaker == "char:rina") // リナの語尾は伸ばさない（「だよぉ」ではなく「だよ」）
                            foreach (var speech in lines.Where(l => l.Kind == StoryLineKind.Speech))
                                Assert.DoesNotMatch("[ぁぃぅぇぉー]$", speech.Text.TrimEnd('！', '？', '…', '。', '　'));
                    }
        }

        [Fact]
        public void Lines_SplitsNarrationAndSpeech_AndAnOldOneLinerIsOneSpeech()
        {
            var report = new SquadLeaderReport { LeaderName = "ミレイ", SquadName = "第1部隊", Line = "〔ミレイは兜を脱いだ〕｜主を倒しました｜｜誰も怪我をしていません" };
            var lines = SquadReportSystem.Lines(report);
            Assert.Equal(3, lines.Count); // 空の段は飛ばす
            Assert.Equal(new StoryLine(StoryLineKind.Narration, "ミレイは兜を脱いだ"), lines[0]);
            Assert.Equal(new StoryLine(StoryLineKind.Speech, "主を倒しました", "ミレイ（第1部隊の部隊長）"), lines[1]);
            Assert.Equal("誰も怪我をしていません", lines[2].Text);

            report.Line = "主を倒してきたよ。みんな、よく踏ん張ってくれた"; // §0.124 より前のセーブ
            var old = Assert.Single(SquadReportSystem.Lines(report));
            Assert.Equal(StoryLineKind.Speech, old.Kind);
            Assert.Equal(report.Line, old.Text);
        }

        [Fact]
        public void TheLeaderName_FillsTheNarration()
        {
            var s = NewState();
            var recruit = new Adventurer { Name = "ミレイ", PortraitId = "p_001", Personality = new Personality { Main = Drive.Dive, Temperament = 0 } };
            s.Adventurers.Add(recruit);
            s.SavedParties.Add(new SavedParty { Name = "第1部隊", MemberIds = { recruit.Id }, Order = SquadOrder.Dive });
            var r = Assert.Single(MonthlyReport.Build(s, new List<AutoSkipWeek>(), 0).LeaderReports);
            Assert.Equal(SquadReportKind.Idle, r.Kind);
            Assert.DoesNotContain("{", r.Line);
            var narration = SquadReportSystem.Lines(r)[0];
            Assert.Equal(StoryLineKind.Narration, narration.Kind);
            // 中庸の待機・潜行志向の待機のどちらの地の文も {部隊長} を使う
            Assert.StartsWith("ミレイは", narration.Text);
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
