using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests.Balance
{
    /// <summary>
    /// 走破力の重み（→ dungeon_traversal.csv の Traversal_Weight_*、03 §4.5.3）と、
    /// それを使う走破力計算（→ DungeonTraversalResolver.CalculateTraversalScore）のテスト。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~Traversal`
    ///
    /// 2026年9月改訂の眼目は「走破力（VIT/MND）と隠密適性（AGI/DEX）を別系統にする」こと。
    /// 旧モデルは両方とも AGI+DEX 合算で、UIの2指標が常に同値になっていた。
    /// </summary>
    public class DungeonTraversalBalanceTests
    {
        private static Adventurer Make(int vit, int mnd, int agiDex = 10, int ldr = 0)
        {
            var a = new Adventurer
            {
                STR = 10, AGI = agiDex, VIT = vit, MND = mnd, DEX = agiDex, LDR = ldr, INT = 10,
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            return party;
        }

        // ---------------- CSVの重み ----------------

        [Fact]
        public void Weights_AreLoadedFromCsv()
        {
            Assert.Equal(1.0, DungeonTraversalBalance.WeightVit, precision: 6);
            Assert.Equal(0.8, DungeonTraversalBalance.WeightMnd, precision: 6);
            Assert.Equal(1.0, DungeonTraversalBalance.WeightLdr, precision: 6);
        }

        // ---------------- 走破力の式（指示書指定テスト） ----------------

        [Fact]
        public void CalculateTraversalPower_ConsidersVitAndMnd()
        {
            var party = PartyOf(Make(vit: 40, mnd: 30, ldr: 20), Make(vit: 20, mnd: 10));

            // Σ(VIT×1.0＋MND×0.8)=(40+24)+(20+8)=92 ＋ 部隊長LDR20×1.0 ＝ 112
            Assert.Equal(112, DungeonTraversalResolver.CalculateTraversalScore(party), precision: 6);
        }

        [Fact]
        public void CalculateTraversalPower_DoesNotChangeWithAgiOrDex()
        {
            // AGI/DEXだけを変えても走破力は1ptも動かない（＝隠密適性との差別化）。
            var nimble = PartyOf(Make(vit: 30, mnd: 20, agiDex: 99, ldr: 10));
            var clumsy = PartyOf(Make(vit: 30, mnd: 20, agiDex: 1, ldr: 10));

            Assert.Equal(
                DungeonTraversalResolver.CalculateTraversalScore(clumsy),
                DungeonTraversalResolver.CalculateTraversalScore(nimble),
                precision: 6);
        }

        [Fact]
        public void CalculateTraversalPower_EmptyParty_IsZero()
        {
            Assert.Equal(0, DungeonTraversalResolver.CalculateTraversalScore(new Party()));
            Assert.Equal(0, DungeonTraversalResolver.CalculateTraversalScore(new Party(), new GameState()));
        }

        // ---------------- 部隊長LDR・研究・参謀ボーナス（指示書指定テスト） ----------------

        [Fact]
        public void CalculateTraversalPower_IncludesLeaderLdrAndBonuses()
        {
            // ①部隊長（先頭メンバー）のLDRのみが加算される。
            var leaderHasLdr = PartyOf(Make(vit: 30, mnd: 10, ldr: 40), Make(vit: 30, mnd: 10));
            var followerHasLdr = PartyOf(Make(vit: 30, mnd: 10), Make(vit: 30, mnd: 10, ldr: 40));
            Assert.Equal(
                40 * DungeonTraversalBalance.WeightLdr,
                DungeonTraversalResolver.CalculateTraversalScore(leaderHasLdr)
                    - DungeonTraversalResolver.CalculateTraversalScore(followerHasLdr),
                precision: 6);

            // ②研究ボーナス（TraversalBonus種別、→ ResearchBalance）が加算される。
            var party = PartyOf(Make(vit: 30, mnd: 10, ldr: 10));
            var state = new GameState();
            double withoutResearch = DungeonTraversalResolver.CalculateTraversalScore(party, state);

            var research = ResearchBalance.GetAll().First(r => r.EffectType == ResearchEffectType.TraversalBonus);
            state.CompletedResearchIds.Add(research.Id);
            double withResearch = DungeonTraversalResolver.CalculateTraversalScore(party, state);
            Assert.Equal(research.EffectValue, withResearch - withoutResearch, precision: 6);

            // ③参謀のルート指導（→ AdvisorSystem.GetAdvisorTraversalPowerBonus）が加算される。
            var advisor = new Adventurer
            {
                Name = "参謀", STR = 50, AGI = 50, VIT = 50, MND = 50, DEX = 50, LDR = 50, INT = 50,
            };
            state.RetiredAdventurers.Add(advisor);
            state.AssignedAdvisor = advisor.Id;
            double withAdvisor = DungeonTraversalResolver.CalculateTraversalScore(party, state);
            Assert.Equal(50 * AdvisorBalance.TraversalPowerBonusCoefficient, withAdvisor - withResearch, precision: 6);
        }
    }
}
