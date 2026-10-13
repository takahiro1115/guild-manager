using System.Collections.Generic;
using System.Linq;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 部隊長の報告（2026年10月・§0.116、→ Systems.SquadReportSystem）の値と台詞の型。
    /// 値は squad_report.csv、台詞は report_lines.csv（Kind＝報告の種類、Speaker＝temper:慎重さ／drive:志向／char:物語帳の人物、Text＝一言）。
    /// 台詞の {主}・{階}・{フィールド}・{名前}・{成果}・{解析} は報告を作るときに埋める。
    /// </summary>
    public static class SquadReportBalance
    {
        private const string FileName = "squad_report.csv";
        private const string LinesFile = "report_lines.csv";

        public static readonly int GrowthMinGain = BalanceData.GetInt(FileName, "GrowthMinGain");
        public static readonly int TemperamentCautiousMax = BalanceData.GetInt(FileName, "TemperamentCautiousMax");
        public static readonly int TemperamentBoldMin = BalanceData.GetInt(FileName, "TemperamentBoldMin");

        private static readonly Dictionary<(string Kind, string Speaker), IReadOnlyList<string>> Lines = LoadLines();

        /// <summary>報告の種類と話し手の型の台詞（無ければ空）。</summary>
        public static IReadOnlyList<string> Get(string kind, string speaker) =>
            Lines.TryGetValue((kind, speaker), out var lines) ? lines : System.Array.Empty<string>();

        private static Dictionary<(string, string), IReadOnlyList<string>> LoadLines()
        {
            var (_, rows) = BalanceData.GetTable(LinesFile);
            return rows.GroupBy(r => (r[0], r[1])).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r[2]).ToList());
        }
    }
}
