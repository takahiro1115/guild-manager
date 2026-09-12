using GuildManager.Core.Models;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// Adventurerモデル単体のテスト（仕様書 03 §2.2）。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class AdventurerTests
    {
        [Fact]
        public void TotalPA_AveragesAllSevenPaFields_IncludingInt()
        {
            // v1.2改訂：INT活性化に伴い、総合PAは6値平均から7値平均に変わった（→ 03 §2.2）。
            var a = new Adventurer
            {
                PA_STR = 10, PA_AGI = 20, PA_VIT = 30, PA_MND = 40,
                PA_DEX = 50, PA_LDR = 60, PA_INT = 70,
            };

            // (10+20+30+40+50+60+70)/7 = 280/7 = 40
            Assert.Equal(40.0, a.TotalPA);
        }

        [Fact]
        public void TotalPA_DefaultsTo100_WhenNoPaFieldsAreSet()
        {
            // 全PAフィールドのデフォルトは100（実効値の成長上限なし相当）。
            var a = new Adventurer();

            Assert.Equal(100.0, a.TotalPA);
        }
    }
}
