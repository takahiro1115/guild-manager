using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 仕様書 03 §4.2 の勝敗境界表が、コードの境界値と一致しているかを確認するテスト。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class QuestResolverTests
    {
        [Theory]
        [InlineData(2.5, CombatOutcome.Victory)]   // 十分な勝利
        [InlineData(1.8, CombatOutcome.Victory)]   // 完全勝利の下限ちょうど
        [InlineData(1.79, CombatOutcome.NarrowWin)] // 完全勝利のすぐ下 → 辛勝
        [InlineData(1.0, CombatOutcome.NarrowWin)]  // 辛勝の下限ちょうど
        [InlineData(0.99, CombatOutcome.Defeat)]    // 辛勝のすぐ下 → 苦戦敗退
        [InlineData(0.6, CombatOutcome.Defeat)]     // 苦戦敗退の下限ちょうど
        [InlineData(0.59, CombatOutcome.Rout)]      // 苦戦敗退のすぐ下 → 戦線崩壊
        [InlineData(0.0, CombatOutcome.Rout)]       // 完敗
        public void ClassifyOutcome_ReturnsExpectedBucket(double ratio, CombatOutcome expected)
        {
            var (outcome, _, _) = QuestResolver.ClassifyOutcome(ratio);
            Assert.Equal(expected, outcome);
        }

        [Fact]
        public void ClassifyOutcome_VictoryHasLightestHpLossRange()
        {
            var (_, min, max) = QuestResolver.ClassifyOutcome(2.0);
            Assert.Equal(5, min);
            Assert.Equal(15, max);
        }

        [Fact]
        public void ClassifyOutcome_RoutHasHeaviestHpLossRange()
        {
            var (_, min, max) = QuestResolver.ClassifyOutcome(0.1);
            Assert.Equal(70, min);
            Assert.Equal(100, max);
        }
    }
}
