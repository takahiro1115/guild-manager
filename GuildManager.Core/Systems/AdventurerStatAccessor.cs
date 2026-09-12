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
        // v1.2改訂：INTを予約フィールドから活性化し、成長ロール対象に追加（→ 03 §2.2・§10）。
        public static readonly string[] AllStatNames = { "STR", "AGI", "VIT", "MND", "DEX", "LDR", "INT" };

        public static int GetStat(Adventurer a, string name) => name switch
        {
            "STR" => a.STR,
            "AGI" => a.AGI,
            "VIT" => a.VIT,
            "MND" => a.MND,
            "DEX" => a.DEX,
            "LDR" => a.LDR,
            "INT" => a.INT,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "未知のステータス名")
        };

        public static void SetStat(Adventurer a, string name, int value)
        {
            switch (name)
            {
                case "STR": a.STR = value; break;
                case "AGI": a.AGI = value; break;
                case "VIT": a.VIT = value; break;
                case "MND": a.MND = value; break;
                case "DEX": a.DEX = value; break;
                case "LDR": a.LDR = value; break;
                case "INT": a.INT = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(name), name, "未知のステータス名");
            }
        }

        public static int GetPa(Adventurer a, string name) => name switch
        {
            "STR" => a.PA_STR,
            "AGI" => a.PA_AGI,
            "VIT" => a.PA_VIT,
            "MND" => a.PA_MND,
            "DEX" => a.PA_DEX,
            "LDR" => a.PA_LDR,
            "INT" => a.PA_INT,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "未知のステータス名")
        };

        public static void SetPa(Adventurer a, string name, int value)
        {
            switch (name)
            {
                case "STR": a.PA_STR = value; break;
                case "AGI": a.PA_AGI = value; break;
                case "VIT": a.PA_VIT = value; break;
                case "MND": a.PA_MND = value; break;
                case "DEX": a.PA_DEX = value; break;
                case "LDR": a.PA_LDR = value; break;
                case "INT": a.PA_INT = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(name), name, "未知のステータス名");
            }
        }

        /// <summary>生涯ピーク値の取得（→ 03 §2.2新規・§7）。顧問効果算出の基準に使う。</summary>
        public static int GetPeak(Adventurer a, string name) => name switch
        {
            "STR" => a.PeakSTR,
            "AGI" => a.PeakAGI,
            "VIT" => a.PeakVIT,
            "MND" => a.PeakMND,
            "DEX" => a.PeakDEX,
            "LDR" => a.PeakLDR,
            "INT" => a.PeakINT,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "未知のステータス名")
        };

        /// <summary>
        /// 生涯ピーク値の更新。成長ロールで実効値が上昇した際にGrowthSystemから呼ばれる。
        /// Adventurer側のプロパティが Max(記録済み値, 現在の実効値) を返す設計のため、
        /// 実際に記録されるかどうか（縮む方向への代入が無視されるか）はプロパティ側に委ねてよい。
        /// </summary>
        public static void SetPeak(Adventurer a, string name, int value)
        {
            switch (name)
            {
                case "STR": a.PeakSTR = value; break;
                case "AGI": a.PeakAGI = value; break;
                case "VIT": a.PeakVIT = value; break;
                case "MND": a.PeakMND = value; break;
                case "DEX": a.PeakDEX = value; break;
                case "LDR": a.PeakLDR = value; break;
                case "INT": a.PeakINT = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(name), name, "未知のステータス名");
            }
        }
    }
}
