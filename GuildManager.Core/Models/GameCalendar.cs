using System;

namespace GuildManager.Core.Models
{
    /// <summary>季節（→ GameCalendar）。1年は春から始まる（第1週＝春の第1週＝新春）。</summary>
    public enum Season
    {
        Spring,
        Summer,
        Autumn,
        Winter,
    }

    /// <summary>
    /// ゲーム内の暦（→ 03 §1.2、2026年9月）。通算の週（GameState.WeekNumber、1始まり）を
    /// 「◯年目・季節・季節内の第◯週」に換算する。1年＝48週＝4季節×12週（構造値のためCSV化しない）。
    ///
    /// 第1週（年の初め）を春の第1週とする。新春採用試験・新春ドラフトの「新春」と意味を合わせるため。
    /// 表記は「1年目 春 第3週」（→ Format）。UI の季節アイコン・色は Godot 側で付ける。
    /// 年の区切り（満期引退の加齢・新春採用試験の判定）もこのクラスの定数・換算を使う（→ AgingSystem・RecruitmentSystem）。
    /// 将来の季節システム（季節ごとの効果）はここを起点に接続する。
    /// </summary>
    public static class GameCalendar
    {
        /// <summary>1年の週数。</summary>
        public const int WeeksPerYear = 48;

        /// <summary>季節の数。</summary>
        public const int SeasonsPerYear = 4;

        /// <summary>1季節の週数（＝12）。</summary>
        public const int WeeksPerSeason = WeeksPerYear / SeasonsPerYear;

        /// <summary>0以下の週（未初期化の旧データ等）は第1週として扱う。</summary>
        private static int Normalize(int week) => Math.Max(1, week);

        /// <summary>何年目か（1始まり）。</summary>
        public static int YearOf(int week) => (Normalize(week) - 1) / WeeksPerYear + 1;

        /// <summary>その年の第何週か（1〜48）。</summary>
        public static int WeekOfYear(int week) => (Normalize(week) - 1) % WeeksPerYear + 1;

        /// <summary>季節。</summary>
        public static Season SeasonOf(int week) => (Season)((WeekOfYear(week) - 1) / WeeksPerSeason);

        /// <summary>季節の中の第何週か（1〜12）。</summary>
        public static int WeekOfSeason(int week) => (WeekOfYear(week) - 1) % WeeksPerSeason + 1;

        /// <summary>年の最初の週（第1週・49週・97週…）か。</summary>
        public static bool IsFirstWeekOfYear(int week) => WeekOfYear(week) == 1;

        /// <summary>年の最後の週（第48週・96週…）か。</summary>
        public static bool IsLastWeekOfYear(int week) => WeekOfYear(week) == WeeksPerYear;

        /// <summary>季節の日本語名（春・夏・秋・冬）。</summary>
        public static string SeasonLabel(Season season) => season switch
        {
            Season.Spring => "春",
            Season.Summer => "夏",
            Season.Autumn => "秋",
            _ => "冬",
        };

        /// <summary>暦の表記「1年目 春 第3週」。</summary>
        public static string Format(int week) =>
            $"{YearOf(week)}年目 {SeasonLabel(SeasonOf(week))} 第{WeekOfSeason(week)}週";
    }
}
