using System.Collections.Generic;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// イザベラの来訪・交流戦・派遣の教官のバランス値（2026年10月・§0.84、→ docs/04_バランス表/isabella.csv、Systems.IsabellaSystem）。
    /// </summary>
    public static class IsabellaBalance
    {
        private const string FileName = "isabella.csv";

        public static readonly string VisitFieldId = BalanceData.GetString(FileName, "VisitFieldId");
        public static readonly int VisitFloor = BalanceData.GetInt(FileName, "VisitFloor");
        public static readonly double ExchangeGrowthPerMatch = BalanceData.GetDouble(FileName, "ExchangeGrowthPerMatch");
        public static readonly double ExchangeGrowthPerYear = BalanceData.GetDouble(FileName, "ExchangeGrowthPerYear");
        public static readonly int ExchangeWeekOfMonth = BalanceData.GetInt(FileName, "ExchangeWeekOfMonth");
        public static readonly int ExchangePrize = BalanceData.GetInt(FileName, "ExchangePrize");
        public static readonly int ExchangeMood = BalanceData.GetInt(FileName, "ExchangeMood");
        public static readonly string GuestTrainerName = BalanceData.GetString(FileName, "GuestTrainerName");
        public static readonly int GuestTrainerWeeks = BalanceData.GetInt(FileName, "GuestTrainerWeeks");
        public static readonly int GuestTrainerTopCount = BalanceData.GetInt(FileName, "GuestTrainerTopCount");
        public static readonly int GuestTrainerAge = BalanceData.GetInt(FileName, "GuestTrainerAge");
        public static readonly string GuestTrainerTraitId = BalanceData.GetString(FileName, "GuestTrainerTraitId");

        private static readonly Dictionary<TournamentDiscipline, double> Factors = new()
        {
            [TournamentDiscipline.Sword] = BalanceData.GetDouble(FileName, "ExchangeFactor_Sword"),
            [TournamentDiscipline.Magic] = BalanceData.GetDouble(FileName, "ExchangeFactor_Magic"),
            [TournamentDiscipline.Skill] = BalanceData.GetDouble(FileName, "ExchangeFactor_Skill"),
        };

        private static readonly Dictionary<TournamentDiscipline, string> Opponents = new()
        {
            [TournamentDiscipline.Sword] = BalanceData.GetString(FileName, "ExchangeOpponent_Sword"),
            [TournamentDiscipline.Magic] = BalanceData.GetString(FileName, "ExchangeOpponent_Magic"),
            [TournamentDiscipline.Skill] = BalanceData.GetString(FileName, "ExchangeOpponent_Skill"),
        };

        /// <summary>交流戦の相手の強さの倍率（剣・魔・技。ほかは1）。</summary>
        public static double ExchangeFactor(TournamentDiscipline d) => Factors.TryGetValue(d, out var f) ? f : 1.0;

        /// <summary>交流戦の部門の相手の名前。</summary>
        public static string Opponent(TournamentDiscipline d) => Opponents.TryGetValue(d, out var n) ? n : "白百合の杖の冒険者";
    }
}
