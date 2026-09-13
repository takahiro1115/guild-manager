using System.Collections.Generic;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// ペア特性シナジー（→ 03 §4.2.3、項目64）の計算ルールのテスト。
    /// 定義（pair_synergy.csv）：豪胆×注意深い＝全種別+15／知識人×豪胆＝護衛のみ-15。
    ///
    /// 遭遇区分やHP比率の影響を受けずにルールそのものを確認したいため、QuestResolver経由
    /// ではなく PairSynergyCalculator を直接呼ぶ（Resolveまで通した確認は QuestResolverTests 側）。
    /// </summary>
    public class PairSynergyCalculatorTests
    {
        private const double BravePlusAttentiveValue = 15;
        private const double ScholarPlusBraveEscortValue = -15;

        private static Adventurer Member(params string[] traitIds)
        {
            // LDR=0：負のシナジー緩和（隊長LDR依存）を効かせず、素の合計値を確認するため。
            var a = new Adventurer { STR = 50, AGI = 50, VIT = 50, MND = 50, DEX = 50, LDR = 0, INT = 50 };
            foreach (var traitId in traitIds)
                a.TryAddTrait(traitId);
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static List<Adventurer> Party(params Adventurer[] members) => new(members);

        // ---------------- 豪胆 × 注意深い（全種別 +15） ----------------

        [Theory]
        [InlineData(QuestType.Subjugation)]
        [InlineData(QuestType.Exploration)]
        [InlineData(QuestType.Escort)]
        public void BraveAndAttentive_AddsBonus_ForEveryQuestType(QuestType questType)
        {
            var party = Party(Member(TraitCatalog.BraveId), Member(TraitCatalog.AttentiveId));

            Assert.Equal(BravePlusAttentiveValue, PairSynergyCalculator.CalculateRawTotal(party, questType), precision: 10);
        }

        [Fact]
        public void BraveAndAttentive_SumsEveryEstablishedPair()
        {
            // 豪胆2名・注意深い1名 → 成立するペアは2組（→ 03 §4.2.3「複数ペアは合算」）。
            var party = Party(
                Member(TraitCatalog.BraveId),
                Member(TraitCatalog.BraveId),
                Member(TraitCatalog.AttentiveId));

            Assert.Equal(BravePlusAttentiveValue * 2, PairSynergyCalculator.CalculateRawTotal(party, QuestType.Subjugation), precision: 10);
        }

        [Fact]
        public void BraveAndAttentive_CountsPairsInBothDirections()
        {
            // 豪胆2名・注意深い2名 → 2×2＝4組。どちらが「先」でも1ペアとして数える。
            var party = Party(
                Member(TraitCatalog.BraveId),
                Member(TraitCatalog.AttentiveId),
                Member(TraitCatalog.BraveId),
                Member(TraitCatalog.AttentiveId));

            Assert.Equal(BravePlusAttentiveValue * 4, PairSynergyCalculator.CalculateRawTotal(party, QuestType.Subjugation), precision: 10);
        }

        [Fact]
        public void SingleMemberWithBothTraits_DoesNotFormPair()
        {
            // 1名が豪胆と注意深いを兼ねていても「異なる2名」の条件を満たさない（→ 03 §4.2.3）。
            var party = Party(
                Member(TraitCatalog.BraveId, TraitCatalog.AttentiveId),
                Member());

            Assert.Equal(0, PairSynergyCalculator.CalculateRawTotal(party, QuestType.Subjugation), precision: 10);
        }

        [Fact]
        public void MemberWithBothTraits_StillPairsWithAnotherMemberHoldingEitherTrait()
        {
            // 兼任者がいても、相手が片方の特性を持っていれば「異なる2名」のペアは成立する。
            var party = Party(
                Member(TraitCatalog.BraveId, TraitCatalog.AttentiveId),
                Member(TraitCatalog.BraveId));

            Assert.Equal(BravePlusAttentiveValue, PairSynergyCalculator.CalculateRawTotal(party, QuestType.Subjugation), precision: 10);
        }

        [Fact]
        public void SoloMember_NeverFormsPair()
        {
            var party = Party(Member(TraitCatalog.BraveId, TraitCatalog.AttentiveId));

            Assert.Equal(0, PairSynergyCalculator.CalculateRawTotal(party, QuestType.Subjugation), precision: 10);
        }

        // ---------------- 知識人 × 豪胆（護衛のみ -15） ----------------

        [Fact]
        public void ScholarAndBrave_SubtractsForEscortOnly()
        {
            var party = Party(Member(TraitCatalog.ScholarId), Member(TraitCatalog.BraveId));

            Assert.Equal(ScholarPlusBraveEscortValue, PairSynergyCalculator.CalculateRawTotal(party, QuestType.Escort), precision: 10);
        }

        [Theory]
        [InlineData(QuestType.Subjugation)]
        [InlineData(QuestType.Exploration)]
        public void ScholarAndBrave_HasNoEffect_OnOtherQuestTypes(QuestType questType)
        {
            var party = Party(Member(TraitCatalog.ScholarId), Member(TraitCatalog.BraveId));

            Assert.Equal(0, PairSynergyCalculator.CalculateRawTotal(party, questType), precision: 10);
        }

        [Fact]
        public void PositiveAndNegativePairs_AreSummedTogether()
        {
            // 護衛では 豪胆×注意深い(+15) と 知識人×豪胆(-15) が同時に成立し、合計0になる。
            var party = Party(
                Member(TraitCatalog.BraveId),
                Member(TraitCatalog.AttentiveId),
                Member(TraitCatalog.ScholarId));

            Assert.Equal(0, PairSynergyCalculator.CalculateRawTotal(party, QuestType.Escort), precision: 10);
            // 討伐では負のシナジー側が対象外のため、+15だけが残る。
            Assert.Equal(BravePlusAttentiveValue, PairSynergyCalculator.CalculateRawTotal(party, QuestType.Subjugation), precision: 10);
        }

        [Fact]
        public void NoTraits_YieldsZero()
        {
            var party = Party(Member(), Member(), Member(), Member());

            Assert.Equal(0, PairSynergyCalculator.CalculateRawTotal(party, QuestType.Escort), precision: 10);
        }

        // ---------------- 隊長LDRによる緩和（マイナス側のみ） ----------------

        [Theory]
        [InlineData(0, -15)]      // LDR0：緩和なし
        [InlineData(50, -7.5)]    // LDR50：緩和率50% → 絶対値が半分
        [InlineData(100, 0)]      // LDR100：緩和率100% → 完全に相殺
        [InlineData(200, 0)]      // 緩和率は1.0でクランプ。符号は反転しない
        public void ApplyLeaderMitigation_CompressesNegativeTotal_ByLeaderLdr(double leaderLdr, double expected)
        {
            Assert.Equal(expected, PairSynergyCalculator.ApplyLeaderMitigation(-15, leaderLdr), precision: 10);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(50)]
        [InlineData(100)]
        public void ApplyLeaderMitigation_NeverChangesPositiveTotal(double leaderLdr)
        {
            // 緩和は「負のシナジーを和らげる」効果としてのみ働く（→ 03 §4.2.3）。
            Assert.Equal(15, PairSynergyCalculator.ApplyLeaderMitigation(15, leaderLdr), precision: 10);
        }

        [Fact]
        public void Calculate_UsesFirstMemberAsLeader_ForMitigation()
        {
            // 隊長＝先頭メンバー（索敵・致死判定と同じ扱い）。先頭のLDRだけが緩和に効く。
            var leaderHighLdr = Member(TraitCatalog.ScholarId);
            leaderHighLdr.LDR = 100;
            var followerLowLdr = Member(TraitCatalog.BraveId);

            var mitigated = PairSynergyCalculator.Calculate(Party(leaderHighLdr, followerLowLdr), QuestType.Escort);
            var notMitigated = PairSynergyCalculator.Calculate(Party(followerLowLdr, leaderHighLdr), QuestType.Escort);

            Assert.Equal(0, mitigated, precision: 10);                              // 隊長LDR100 → 全額緩和
            Assert.Equal(ScholarPlusBraveEscortValue, notMitigated, precision: 10);  // 隊長LDR0 → 緩和なし
        }

        [Fact]
        public void Calculate_DoesNotMitigatePositiveTotal_RegardlessOfLeaderLdr()
        {
            var leader = Member(TraitCatalog.BraveId);
            leader.LDR = 100;
            var follower = Member(TraitCatalog.AttentiveId);

            Assert.Equal(BravePlusAttentiveValue, PairSynergyCalculator.Calculate(Party(leader, follower), QuestType.Subjugation), precision: 10);
        }
    }
}
