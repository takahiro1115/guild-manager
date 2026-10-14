using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>部隊長の報告の種類（§0.116）。並びは優先の順（その月に起きたことのうち、いちばん上のものを報告する）。</summary>
    public enum SquadReportKind
    {
        /// <summary>主を倒した。</summary>
        Victory,
        /// <summary>主との決戦で退いた。</summary>
        Defeat,
        /// <summary>隊員が重傷を負った。</summary>
        Injury,
        /// <summary>主の扉の前で引き返した。</summary>
        Retreat,
        /// <summary>隊員（部隊長のほか）が大きく伸びた。</summary>
        Growth,
        /// <summary>主を調べた（解析）。</summary>
        Survey,
        /// <summary>素材を集めた。</summary>
        Gather,
        /// <summary>迷宮を奥へ進んだ。</summary>
        Dive,
        /// <summary>出撃しなかった（方針はあるが待機した）。</summary>
        Idle,
    }

    /// <summary>部隊長の報告1件（月報に入れてセーブにも残す。§0.116）。</summary>
    public sealed class SquadLeaderReport
    {
        public string SquadName { get; set; } = "";
        public Guid LeaderId { get; set; }
        public string LeaderName { get; set; } = "";
        /// <summary>部隊長の顔（→ assets/portraits）。無ければ null。</summary>
        public string? PortraitId { get; set; }
        public SquadReportKind Kind { get; set; }
        /// <summary>
        /// 報告（台詞の型を埋めたもの。かぎかっこは付けない）。「｜」で段に分かれ、〔…〕の段は地の文（→ <see cref="SquadReportSystem.Lines"/>）。
        /// 段に分ける前（§0.124 より前）のセーブでは「｜」の無い一言。
        /// </summary>
        public string Line { get; set; } = "";
    }

    /// <summary>
    /// 部隊長の報告（2026年10月・§0.116。テストプレイの感想「月報として、各部隊のリーダーが結果を報告しに来る。冒険者に愛着が持てるように」）。
    /// 月報を作るときに、隊員がいて方針のある部隊ごとに、部隊長（部隊の1人目。走破・採取・調査で統率が効く子）がその月のいちばん大きな出来事を一言で報告する。
    /// 口調は、物語帳に人物設定のある子（初期の3人・ルミナ）は専用の台詞、ほかの子は気質（慎重・中庸・大胆）の台詞と主志向の台詞から選ぶ（→ report_lines.csv）。
    /// 乱数は使わない（月と部隊の並びで台詞を回す）。
    /// </summary>
    public static class SquadReportSystem
    {
        /// <summary>物語帳に専用の台詞がある人物の鍵（ルミナは StoryCharacterId、初期の3人は顔の Id）。</summary>
        public static string? CharacterKey(Adventurer a) =>
            a.StoryCharacterId ?? (a.PortraitId is "claudia" or "rina" or "fiona" ? a.PortraitId : null);

        /// <summary>気質の口調：慎重・中庸・大胆（個性の無い子は中庸）。</summary>
        public static string TemperKey(Adventurer a)
        {
            int t = a.Personality?.Temperament ?? 0;
            return t <= SquadReportBalance.TemperamentCautiousMax ? "cautious" : t >= SquadReportBalance.TemperamentBoldMin ? "bold" : "normal";
        }

        /// <summary>
        /// その月の報告を部隊の並び順に作る。report は同じ月の月報（成長の集計を使う）。隊員がいない部隊・方針の無い部隊は報告しない。
        /// </summary>
        public static List<SquadLeaderReport> Build(GameState state, IReadOnlyList<AutoSkipWeek> weeks, MonthlyReport report)
        {
            var list = new List<SquadLeaderReport>();
            int monthIndex = (state.WeekNumber - 1) / GameCalendar.WeeksPerMonth;
            int squadIndex = 0;
            foreach (var party in state.SavedParties.Where(p => p.MemberIds.Count > 0 && p.Order != SquadOrder.None))
            {
                var members = party.MemberIds.Select(state.FindAdventurer).Where(a => a != null && !a.IsRetired).Select(a => a!).ToList();
                if (members.Count == 0) continue;
                var leader = members[0];
                var resolutions = weeks.SelectMany(w => w.Settlement.DungeonMissionResolutions)
                    .Where(r => r.Party.Members.Any(m => party.MemberIds.Contains(m.Id))).ToList();
                var (kind, values) = Classify(state, leader, members, resolutions, report);
                values["{部隊長}"] = leader.Name;
                list.Add(new SquadLeaderReport
                {
                    SquadName = party.Name,
                    LeaderId = leader.Id,
                    LeaderName = leader.Name,
                    PortraitId = leader.PortraitId,
                    Kind = kind,
                    Line = Fill(PickLine(leader, kind, monthIndex + squadIndex), values),
                });
                squadIndex++;
            }
            return list;
        }

        /// <summary>その月の出来事のうち、いちばん優先の高い種類と、台詞に埋める値。</summary>
        public static (SquadReportKind Kind, Dictionary<string, string> Values) Classify(
            GameState state, Adventurer leader, IReadOnlyList<Adventurer> members, IReadOnlyList<DungeonMissionResolution> resolutions, MonthlyReport report)
        {
            var values = new Dictionary<string, string>();
            static string BossName(DungeonMissionResolution r) => r.Boss?.Name ?? r.Field.Name;

            if (resolutions.LastOrDefault(r => r.DungeonResult?.Outcome == DungeonOutcome.Victory) is { } won)
            {
                values["{主}"] = BossName(won);
                return (SquadReportKind.Victory, values);
            }
            if (resolutions.LastOrDefault(r => r.DungeonResult != null) is { } lost)
            {
                values["{主}"] = BossName(lost);
                return (SquadReportKind.Defeat, values);
            }
            if (resolutions.SelectMany(r => r.InjuryEvents).LastOrDefault(e => e.Severity == InjurySeverity.Severe) is { } injury)
            {
                values["{名前}"] = injury.Name;
                return (SquadReportKind.Injury, values);
            }
            if (resolutions.LastOrDefault(r => r.ArrivedAtBossDoor && r.DoorRetreatReason != null) is { } retreat)
            {
                values["{主}"] = BossName(retreat);
                return (SquadReportKind.Retreat, values);
            }
            var grower = report.Growth.Where(g => members.Skip(1).Any(m => m.Name == g.Name))
                .Select(g => (g.Name, Gain: g.Gains.Values.Sum())).OrderByDescending(x => x.Gain).FirstOrDefault();
            if (grower.Name != null && grower.Gain >= SquadReportBalance.GrowthMinGain)
            {
                values["{名前}"] = grower.Name;
                return (SquadReportKind.Growth, values);
            }
            if (resolutions.LastOrDefault(r => r.ScoutingResult != null) is { } survey)
            {
                values["{主}"] = BossName(survey);
                values["{解析}"] = $"{survey.ScoutingResult!.IntelRateAfter * 100:0}";
                return (SquadReportKind.Survey, values);
            }
            var gathered = resolutions.Where(r => r.GatheringResult is { MaterialCount: > 0 })
                .GroupBy(r => r.GatheringResult!.MaterialId).Select(g => $"{MaterialBalance.GetName(g.Key)}×{g.Sum(r => r.GatheringResult!.MaterialCount)}").ToList();
            if (gathered.Count > 0)
            {
                values["{成果}"] = string.Join("と", gathered);
                return (SquadReportKind.Gather, values);
            }
            if (resolutions.Where(r => r.TraversalResult is { } t && t.FloorAfter > t.FloorBefore).OrderBy(r => r.TraversalResult!.FloorAfter).LastOrDefault() is { } dive)
            {
                values["{フィールド}"] = dive.Field.Name;
                values["{階}"] = dive.TraversalResult!.FloorAfter.ToString();
                return (SquadReportKind.Dive, values);
            }
            return (SquadReportKind.Idle, values);
        }

        /// <summary>
        /// 台詞を選ぶ：物語帳の人物は専用の台詞だけ。ほかの子は気質の台詞と主志向の台詞を合わせた中から、turn で回して1つ。
        /// どれも無ければ気質「中庸」の台詞（それも無ければ空）。
        /// </summary>
        public static string PickLine(Adventurer leader, SquadReportKind kind, int turn)
        {
            string k = kind.ToString();
            IReadOnlyList<string> candidates = CharacterKey(leader) is { } ch && SquadReportBalance.Get(k, $"char:{ch}") is { Count: > 0 } own
                ? own
                : SquadReportBalance.Get(k, $"temper:{TemperKey(leader)}")
                    .Concat(leader.Personality is { } p ? SquadReportBalance.Get(k, $"drive:{p.Main}") : Array.Empty<string>()).ToList();
            if (candidates.Count == 0) candidates = SquadReportBalance.Get(k, "temper:normal");
            return candidates.Count == 0 ? "" : candidates[Math.Abs(turn) % candidates.Count];
        }

        /// <summary>段の区切り（report_lines.csv の Text）。</summary>
        public const char SegmentSeparator = '｜';

        /// <summary>
        /// 報告を段に分ける（§0.124）：「｜」で区切り、〔…〕の段は地の文、ほかは部隊長の台詞（話す人は「名前（部隊名の部隊長）」）。
        /// 空の段は飛ばす。「｜」の無い旧セーブの一言は台詞1段になる。
        /// </summary>
        public static List<StoryLine> Lines(SquadLeaderReport report)
        {
            string speaker = $"{report.LeaderName}（{report.SquadName}の部隊長）";
            var lines = new List<StoryLine>();
            foreach (var raw in report.Line.Split(SegmentSeparator))
            {
                string text = raw.Trim();
                if (text.Length == 0) continue;
                lines.Add(text.StartsWith('〔') && text.EndsWith('〕')
                    ? new StoryLine(StoryLineKind.Narration, text[1..^1])
                    : new StoryLine(StoryLineKind.Speech, text, speaker));
            }
            return lines;
        }

        private static string Fill(string line, Dictionary<string, string> values) =>
            values.Aggregate(line, (text, kv) => text.Replace(kv.Key, kv.Value));
    }
}
