using GuildManager.Core.Models;
using GuildManager.Core.Rng;
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
        /// <summary>NextInt(min, max) が常に max を返すテスト用スタブ。HP消費%を確実に上限にできる。</summary>
        private class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

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

        /// <summary>
        /// Resolve()がHP0到達を検知したら、LevelingSystem（→ 03 §3.8）が経験値対象から
        /// 除外できるよう DownedAdventurerIds に記録することを確認する。
        /// </summary>
        [Fact]
        public void Resolve_RecordsDownedAdventurer_WhenHpHitsZero()
        {
            // 極端に弱いパーティ×極端に高難易度のクエストでRatioを確実に0.6未満（戦線崩壊）にし、
            // AlwaysMaxRngでHP消費%を常に上限（Rout帯は70〜100%）にすることでHP0到達を保証する。
            var weakling = new Adventurer { STR = 1, AGI = 1, END = 1, MAG = 1, SCT = 1, LDR = 1 };
            weakling.CurrentHP = weakling.MaxHP;
            var party = new Party();
            party.TryAdd(weakling);
            var quest = new Quest { Difficulty = 100, ScoutRequirement = 1 };
            var resolver = new QuestResolver(new AlwaysMaxRng());

            var result = resolver.Resolve(party, quest);

            Assert.Equal(CombatOutcome.Rout, result.Outcome);
            Assert.Contains(weakling.Id, result.DownedAdventurerIds);
            Assert.Equal(1, weakling.CurrentHP); // §4.3のMVP簡易版：ダウン後はHP1で重傷に留まる
        }
    }
}
