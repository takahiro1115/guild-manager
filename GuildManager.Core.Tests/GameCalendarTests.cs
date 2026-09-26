using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// ゲーム内の暦（→ GameCalendar、03 §1.2）のテスト。1年＝48週＝4季節×12週、第1週＝1年目の春の第1週。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~GameCalendar`
    /// </summary>
    public class GameCalendarTests
    {
        [Theory]
        [InlineData(1, 1, Season.Spring, 1, "1年目 春 第1週")]
        [InlineData(3, 1, Season.Spring, 3, "1年目 春 第3週")]
        [InlineData(12, 1, Season.Spring, 12, "1年目 春 第12週")]
        [InlineData(13, 1, Season.Summer, 1, "1年目 夏 第1週")]
        [InlineData(25, 1, Season.Autumn, 1, "1年目 秋 第1週")]
        [InlineData(37, 1, Season.Winter, 1, "1年目 冬 第1週")]
        [InlineData(48, 1, Season.Winter, 12, "1年目 冬 第12週")]
        [InlineData(49, 2, Season.Spring, 1, "2年目 春 第1週")]
        [InlineData(384, 8, Season.Winter, 12, "8年目 冬 第12週")]
        public void ConvertsAbsoluteWeek(int week, int year, Season season, int weekOfSeason, string text)
        {
            Assert.Equal(year, GameCalendar.YearOf(week));
            Assert.Equal(season, GameCalendar.SeasonOf(week));
            Assert.Equal(weekOfSeason, GameCalendar.WeekOfSeason(week));
            Assert.Equal(text, GameCalendar.Format(week));
        }

        [Fact]
        public void Constants_AreStructural()
        {
            Assert.Equal(48, GameCalendar.WeeksPerYear);
            Assert.Equal(12, GameCalendar.WeeksPerSeason);
        }

        [Theory]
        [InlineData(1, true, false)]
        [InlineData(48, false, true)]
        [InlineData(49, true, false)]
        [InlineData(96, false, true)]
        [InlineData(50, false, false)]
        public void YearBoundaries(int week, bool first, bool last)
        {
            Assert.Equal(first, GameCalendar.IsFirstWeekOfYear(week));
            Assert.Equal(last, GameCalendar.IsLastWeekOfYear(week));
        }

        [Fact]
        public void NonPositiveWeek_IsTreatedAsFirstWeek()
        {
            Assert.Equal("1年目 春 第1週", GameCalendar.Format(0));
            Assert.Equal("1年目 春 第1週", GameCalendar.Format(-5));
        }

        [Fact]
        public void RecruitmentWeek_UsesCalendar_FromSecondYear()
        {
            var system = new RecruitmentSystem(new SeededRng(1));

            Assert.False(system.IsRecruitmentWeek(1));   // 1年目の春（初期メンバー・新春ドラフトの週）
            Assert.True(system.IsRecruitmentWeek(49));   // 2年目の春 第1週
            Assert.True(system.IsRecruitmentWeek(97));
            Assert.False(system.IsRecruitmentWeek(50));
        }
    }
}
