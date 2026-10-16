using System.Collections.Generic;
using System.Linq;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 出来事の報告（2026年10月・§0.141、→ Systems.EventReportSystem）の台詞の型。正本は物語帳「台詞 06 出来事の報告」。
    /// 台詞は event_lines.csv（Kind＝知らせの種類、Speaker＝any／nolumina〈ルミナがまだいないとき〉／temper:気質／char:物語帳の人物、
    /// Text＝「｜」で段に分けた台詞〈〔…〕＝地の文・名前「…」＝名前のある人物・「…」＝当人〉、Jump＝行き先の案内〈空なら無し〉）。
    /// 同じ Kind と Speaker の行が複数あれば、月ごとに順に使う。
    /// </summary>
    public static class EventReportBalance
    {
        private const string LinesFile = "event_lines.csv";

        public sealed record EventLine(string Text, string Jump);

        private static readonly Dictionary<(string Kind, string Speaker), IReadOnlyList<EventLine>> Lines = LoadLines();

        /// <summary>知らせの種類と話し手の型の台詞（無ければ空）。</summary>
        public static IReadOnlyList<EventLine> Get(string kind, string speaker) =>
            Lines.TryGetValue((kind, speaker), out var lines) ? lines : System.Array.Empty<EventLine>();

        /// <summary>台詞のある知らせの種類と話し手の型（テスト用）。</summary>
        public static IEnumerable<(string Kind, string Speaker)> Keys => Lines.Keys;

        private static Dictionary<(string, string), IReadOnlyList<EventLine>> LoadLines()
        {
            var (_, rows) = BalanceData.GetTable(LinesFile);
            return rows.GroupBy(r => (r[0], r[1]))
                .ToDictionary(g => g.Key, g => (IReadOnlyList<EventLine>)g.Select(r => new EventLine(r[2], r[3])).ToList());
        }
    }
}
