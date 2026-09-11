using System;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// STR〜LDRの6ステータスを「ステータス名（文字列）」で横断的に読み書きするための内部ヘルパー。
    ///
    /// AgingSystem（加齢による恒久低下・成長期の成長）とLevelingSystem（レベルアップによる成長）は
    /// どちらも「ランダムに1ステータスを選んで実効値・PAを増減する」という同じ形の処理を行うため、
    /// Get/Set の切り替えロジックをここに共通化している。
    /// </summary>
    internal static class AdventurerStatAccessor
    {
        public static readonly string[] AllStatNames = { "STR", "AGI", "END", "MAG", "SCT", "LDR" };

        public static int GetStat(Adventurer a, string name) => name switch
        {
            "STR" => a.STR,
            "AGI" => a.AGI,
            "END" => a.END,
            "MAG" => a.MAG,
            "SCT" => a.SCT,
            "LDR" => a.LDR,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "未知のステータス名")
        };

        public static void SetStat(Adventurer a, string name, int value)
        {
            switch (name)
            {
                case "STR": a.STR = value; break;
                case "AGI": a.AGI = value; break;
                case "END": a.END = value; break;
                case "MAG": a.MAG = value; break;
                case "SCT": a.SCT = value; break;
                case "LDR": a.LDR = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(name), name, "未知のステータス名");
            }
        }

        public static int GetPa(Adventurer a, string name) => name switch
        {
            "STR" => a.PA_STR,
            "AGI" => a.PA_AGI,
            "END" => a.PA_END,
            "MAG" => a.PA_MAG,
            "SCT" => a.PA_SCT,
            "LDR" => a.PA_LDR,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "未知のステータス名")
        };

        public static void SetPa(Adventurer a, string name, int value)
        {
            switch (name)
            {
                case "STR": a.PA_STR = value; break;
                case "AGI": a.PA_AGI = value; break;
                case "END": a.PA_END = value; break;
                case "MAG": a.PA_MAG = value; break;
                case "SCT": a.PA_SCT = value; break;
                case "LDR": a.PA_LDR = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(name), name, "未知のステータス名");
            }
        }
    }
}
