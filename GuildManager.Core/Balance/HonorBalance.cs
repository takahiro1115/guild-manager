using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 戦績・称号・殿堂・引退式（大会と育成の栄光 段2）のバランス値。称号の定義は docs/04_バランス表/titles.csv、
    /// 殿堂・関係タグ・引退式の値は honor.csv、引退式のアルベールの言葉は retirement_lines.csv から読む。
    /// </summary>
    public static class HonorBalance
    {
        private const string TitlesFile = "titles.csv";
        private const string FileName = "honor.csv";
        private const string LinesFile = "retirement_lines.csv";

        public static readonly IReadOnlyList<TitleDefinition> Titles = LoadTitles();

        public static readonly int HallOfFameG1Wins = BalanceData.GetInt(FileName, "HallOfFameG1Wins");
        public static readonly double HallOfFameAdvisorMultiplier = BalanceData.GetDouble(FileName, "HallOfFameAdvisorMultiplier");
        public static readonly int BuddyCompatibility = BalanceData.GetInt(FileName, "BuddyCompatibility");
        public static readonly int BuddyBossKills = BalanceData.GetInt(FileName, "BuddyBossKills");
        public static readonly int PerfectPairCompatibility = BalanceData.GetInt(FileName, "PerfectPairCompatibility");
        public static readonly int MentorWeeks = BalanceData.GetInt(FileName, "MentorWeeks");
        public static readonly int RelationsShown = BalanceData.GetInt(FileName, "RelationsShown");
        public static readonly int CeremonyHighlights = BalanceData.GetInt(FileName, "CeremonyHighlights");
        public static readonly int CeremonyTitleLines = BalanceData.GetInt(FileName, "CeremonyTitleLines");
        public static readonly int ShortCeremonyMaxEntries = BalanceData.GetInt(FileName, "ShortCeremonyMaxEntries");

        /// <summary>引退式のアルベールの言葉（キー→台詞の一覧）。キーは Legend／Name／Honor／Plain／Short／HallOfFame。</summary>
        public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RetirementLines = LoadLines();

        /// <summary>称号の定義を Id で引く（無ければ例外。titles.csv に行が無いのはデータの誤り）。</summary>
        public static TitleDefinition Title(string id) =>
            Titles.FirstOrDefault(t => t.Id == id) ?? throw new BalanceDataException($"{TitlesFile} に称号「{id}」がありません");

        private static List<TitleDefinition> LoadTitles()
        {
            var (_, rows) = BalanceData.GetTable(TitlesFile);
            var result = new List<TitleDefinition>();
            foreach (var r in rows)
            {
                if (!Enum.TryParse<TitleRank>(r[2], out var rank))
                    throw new BalanceDataException($"{TitlesFile} の称号「{r[0]}」の格「{r[2]}」が読めません");
                result.Add(new TitleDefinition { Id = r[0], Name = r[1], Rank = rank, Param = int.Parse(r[3]) });
            }
            return result;
        }

        private static Dictionary<string, IReadOnlyList<string>> LoadLines()
        {
            var (_, rows) = BalanceData.GetTable(LinesFile);
            return rows.GroupBy(r => r[0]).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r[1]).ToList());
        }
    }
}
