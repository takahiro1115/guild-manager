using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>エンディングのあと（2026年10月・§0.90、→ docs/04_バランス表/postgame.csv、Systems.PostGameSystem）のバランス値。</summary>
    public static class PostGameBalance
    {
        private const string FileName = "postgame.csv";

        public static readonly int RevivalWeeks = BalanceData.GetInt(FileName, "RevivalWeeks");
        public static readonly IReadOnlyList<Season> PostClearAnomalySeasons = ParseSeasons(BalanceData.GetString(FileName, "PostClearAnomalySeasons"));
        public static readonly string LuminaName = BalanceData.GetString(FileName, "LuminaName");
        public static readonly JobClass LuminaJob = Enum.TryParse<JobClass>(BalanceData.GetString(FileName, "LuminaJob"), out var job)
            ? job : throw new BalanceDataException($"{FileName} の LuminaJob は職業の名前ではありません。");
        public static readonly int LuminaRescueAge = BalanceData.GetInt(FileName, "LuminaRescueAge");
        public static readonly int LuminaPa = BalanceData.GetInt(FileName, "LuminaPa");
        public static readonly IReadOnlyList<string> LuminaTraits = BalanceData.GetString(FileName, "LuminaTraits")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        public static readonly string LuminaPortraitId = BalanceData.GetString(FileName, "LuminaPortraitId");
        public static readonly string LuminaGrownPortraitId = BalanceData.GetString(FileName, "LuminaGrownPortraitId");
        public static readonly int LuminaGrownYear = BalanceData.GetInt(FileName, "LuminaGrownYear");

        private static List<Season> ParseSeasons(string raw) =>
            raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Enum.TryParse<Season>(s, out var season) ? season : throw new BalanceDataException($"{FileName} の季節「{s}」を解釈できません。"))
                .ToList();
    }
}
