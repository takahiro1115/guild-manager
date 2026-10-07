using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 大会（2026年10月・§0.82）のバランス値。大会の定義は docs/04_バランス表/tournaments.csv、
    /// 試合・出場・暦の値は tournament.csv、施設が開いたときの台詞は facility_unlock_lines.csv から読む。
    /// </summary>
    public static class TournamentBalance
    {
        private const string TableFile = "tournaments.csv";
        private const string FileName = "tournament.csv";
        private const string LinesFile = "facility_unlock_lines.csv";

        public static readonly IReadOnlyList<TournamentDefinition> Definitions = LoadDefinitions();

        public static readonly double YearlyGrowth = BalanceData.GetDouble(FileName, "YearlyGrowth");
        public static readonly int BracketSize = BalanceData.GetInt(FileName, "BracketSize");
        public static readonly int EntrantsPerGuild = BalanceData.GetInt(FileName, "EntrantsPerGuild");
        public static readonly double WinExponent = BalanceData.GetDouble(FileName, "WinExponent");
        public static readonly double LuckMin = BalanceData.GetDouble(FileName, "LuckMin");
        public static readonly double LuckMax = BalanceData.GetDouble(FileName, "LuckMax");
        public static readonly double HpFactorBase = BalanceData.GetDouble(FileName, "HpFactorBase");
        public static readonly double HpCostPerMatch = BalanceData.GetDouble(FileName, "HpCostPerMatch");
        public static readonly int SatisfactionLowThreshold = BalanceData.GetInt(FileName, "SatisfactionLowThreshold");
        public static readonly double SatisfactionLowFactor = BalanceData.GetDouble(FileName, "SatisfactionLowFactor");
        public static readonly int SatisfactionHighThreshold = BalanceData.GetInt(FileName, "SatisfactionHighThreshold");
        public static readonly double SatisfactionHighFactor = BalanceData.GetDouble(FileName, "SatisfactionHighFactor");
        public static readonly double BraveFactor = BalanceData.GetDouble(FileName, "BraveFactor");
        public static readonly double PartyCompatibilityBonusMax = BalanceData.GetDouble(FileName, "PartyCompatibilityBonusMax");
        public static readonly int WinnerSatisfaction = BalanceData.GetInt(FileName, "WinnerSatisfaction");
        public static readonly double RestRecoveryMultiplier = BalanceData.GetDouble(FileName, "RestRecoveryMultiplier");
        public static readonly int PushHpCost = BalanceData.GetInt(FileName, "PushHpCost");
        public static readonly double PushGrowthMultiplier = BalanceData.GetDouble(FileName, "PushGrowthMultiplier");
        public static readonly int LocalBaseCount = BalanceData.GetInt(FileName, "LocalBaseCount");
        public static readonly int LocalYearBonusMax = BalanceData.GetInt(FileName, "LocalYearBonusMax");
        public static readonly int LocalPlacingsPer = BalanceData.GetInt(FileName, "LocalPlacingsPer");
        public static readonly int LocalPlacingsBonusMax = BalanceData.GetInt(FileName, "LocalPlacingsBonusMax");
        public static readonly int LocalMax = BalanceData.GetInt(FileName, "LocalMax");
        public static readonly IReadOnlyList<string> LocalRegions =
            BalanceData.GetString(FileName, "LocalRegions").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        public static readonly int InviteLeadMonths = BalanceData.GetInt(FileName, "InviteLeadMonths");
        public static readonly int PartyEntryMinFloor = BalanceData.GetInt(FileName, "PartyEntryMinFloor");

        /// <summary>宿舎をLv2〜Lv5まで開くのに要る入賞の累計（添字0＝Lv2）。</summary>
        public static readonly int[] DormPlacings =
            { BalanceData.GetInt(FileName, "DormPlacings_Lv2"), BalanceData.GetInt(FileName, "DormPlacings_Lv3"), BalanceData.GetInt(FileName, "DormPlacings_Lv4"), BalanceData.GetInt(FileName, "DormPlacings_Lv5") };

        /// <summary>ギルド酒場をLv2〜Lv5まで開くのに要る賞金の累計（添字0＝Lv2）。</summary>
        public static readonly int[] TavernPrize =
            { BalanceData.GetInt(FileName, "TavernPrize_Lv2"), BalanceData.GetInt(FileName, "TavernPrize_Lv3"), BalanceData.GetInt(FileName, "TavernPrize_Lv4"), BalanceData.GetInt(FileName, "TavernPrize_Lv5") };

        private static readonly Dictionary<TournamentDiscipline, double> DisciplineFactors = new()
        {
            [TournamentDiscipline.Sword] = BalanceData.GetDouble(FileName, "DisciplineFactor_Sword"),
            [TournamentDiscipline.Magic] = BalanceData.GetDouble(FileName, "DisciplineFactor_Magic"),
            [TournamentDiscipline.Skill] = BalanceData.GetDouble(FileName, "DisciplineFactor_Skill"),
            [TournamentDiscipline.Party] = BalanceData.GetDouble(FileName, "DisciplineFactor_Party"),
        };

        /// <summary>施設が開いたときの台詞（型・施設→台詞の一覧）。施設は Training／Infirmary／… ／Any。</summary>
        public static readonly IReadOnlyList<(string Style, string Facility, string Text)> UnlockLines = LoadLines();

        /// <summary>大会の相手の強さに掛ける部門の倍率（Best・Random は呼ぶ側で決まった部門を渡す。無ければ1）。</summary>
        public static double DisciplineFactor(TournamentDiscipline discipline) =>
            DisciplineFactors.TryGetValue(discipline, out var f) ? f : 1.0;

        public static TournamentDefinition? Find(string id) => Definitions.FirstOrDefault(d => d.Id == id);

        private static List<TournamentDefinition> LoadDefinitions()
        {
            var (_, rows) = BalanceData.GetTable(TableFile);
            var result = new List<TournamentDefinition>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                result.Add(new TournamentDefinition
                {
                    Id = r[0],
                    Name = r[1],
                    Kind = ParseEnum<TournamentKind>(r[2], i, "Kind"),
                    Discipline = ParseEnum<TournamentDiscipline>(r[3], i, "Discipline"),
                    Grade = ParseEnum<TournamentGrade>(r[4], i, "Grade"),
                    Month = ParseInt(r[5], i),
                    Week = ParseInt(r[6], i),
                    MinStrength = ParseDouble(r[7], i),
                    MaxStrength = ParseDouble(r[8], i),
                    Prize1 = ParseInt(r[9], i),
                    Prize2 = ParseInt(r[10], i),
                    Prize4 = ParseInt(r[11], i),
                    Mood1 = ParseInt(r[12], i),
                    Mood2 = ParseInt(r[13], i),
                    Mood4 = ParseInt(r[14], i),
                });
            }
            return result;
        }

        private static List<(string, string, string)> LoadLines()
        {
            var (_, rows) = BalanceData.GetTable(LinesFile);
            return rows.Select(r => (r[0], r[1], r[2])).ToList();
        }

        private static T ParseEnum<T>(string raw, int row, string column) where T : struct, Enum =>
            Enum.TryParse<T>(raw, out var v) ? v : throw new BalanceDataException($"{TableFile} の{row + 2}行目の{column}「{raw}」を解釈できません。");

        private static int ParseInt(string raw, int row) =>
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : throw new BalanceDataException($"{TableFile} の{row + 2}行目の「{raw}」を整数として解釈できません。");

        private static double ParseDouble(string raw, int row) =>
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : throw new BalanceDataException($"{TableFile} の{row + 2}行目の「{raw}」を数値として解釈できません。");
    }
}
