using System.Collections.Generic;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 出撃前の成功率予測エンジン（SuccessRateCalculator、→ コアシステム刷新仕様
    /// 「(1) 成功率予測エンジンの計算式」）のテスト。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class SuccessRateCalculatorTests
    {
        /// <summary>指定ステータスで全能力が揃った、HP満タンの冒険者を作る。</summary>
        private static Adventurer MakeAdventurer(int stat = 50, Placement placement = Placement.Front)
        {
            var a = new Adventurer
            {
                STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat,
                Placement = placement,
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members)
                party.TryAdd(m);
            return party;
        }

        // ---------------- 基本式：Ratio1.0（点数＝要求値）でちょうど50% ----------------

        [Fact]
        public void Estimate_ReturnsFiftyPercent_WhenScoreEqualsRequirement()
        {
            // 巡回（Patrol）は全ステータス重み0.5。1名・全能力50なら点数＝50×0.5×7＝175。
            // 要求値＝Difficulty×2.0 なので、Difficulty=87.5相当で Ratio=1.0 になる。
            var member = MakeAdventurer(50);
            var quest = new Quest { QuestType = QuestType.Patrol, Difficulty = 88 };

            double rate = SuccessRateCalculator.Estimate(PartyOf(member), quest);

            // Ratio≒0.994 → 約49.7%。基本係数0.5が効いていることを確認する。
            Assert.InRange(rate, 0.48, 0.52);
        }

        [Fact]
        public void Estimate_IsClampedToMinRate_WhenPartyIsHopelesslyWeak()
        {
            var weak = MakeAdventurer(1);
            var quest = new Quest { QuestType = QuestType.Patrol, Difficulty = 100 };

            double rate = SuccessRateCalculator.Estimate(PartyOf(weak), quest);

            Assert.Equal(SuccessRateBalance.MinRate, rate);
        }

        [Fact]
        public void Estimate_IsClampedToMaxRate_WhenPartyIsOverwhelming()
        {
            var quest = new Quest { QuestType = QuestType.Patrol, Difficulty = 1 };

            double rate = SuccessRateCalculator.Estimate(
                PartyOf(MakeAdventurer(100), MakeAdventurer(100), MakeAdventurer(100), MakeAdventurer(100)), quest);

            Assert.Equal(SuccessRateBalance.MaxRate, rate);
        }

        [Fact]
        public void Estimate_ReturnsZero_ForEmptyParty()
        {
            // 空編成は出撃できない（QuestResolver.Resolveは例外を投げる）。予測側は0を返す。
            var quest = new Quest { QuestType = QuestType.Patrol, Difficulty = 10 };

            Assert.Equal(0, SuccessRateCalculator.Estimate(new Party(), quest));
        }

        // ---------------- 単独採取（→ 仕様「単独でも適性が高ければ最大85%」） ----------------

        [Fact]
        public void Estimate_SoloGathering_IsCappedAtSoloMaxRate_NotGlobalMaxRate()
        {
            // 適性（AGI・DEX）が非常に高い1名での採取。通常上限95%ではなく単独上限85%で頭打ちになる。
            var specialist = MakeAdventurer(100);
            var quest = new Quest { QuestType = QuestType.Gathering, Difficulty = 5 };

            double rate = SuccessRateCalculator.Estimate(PartyOf(specialist), quest);

            Assert.Equal(SuccessRateBalance.SoloGatheringMaxRate, rate);
        }

        [Fact]
        public void Estimate_SoloGathering_ReachesSoloCap_WhenStatsAreHigh()
        {
            // 「単独でも適性ステータスが高ければ85%まで到達可能」であること
            //（＝単独であること自体にはペナルティを課していない）。
            var specialist = MakeAdventurer(90);
            var quest = new Quest { QuestType = QuestType.Gathering, Difficulty = 10 };

            double rate = SuccessRateCalculator.Estimate(PartyOf(specialist), quest);

            Assert.Equal(SuccessRateBalance.SoloGatheringMaxRate, rate);
        }

        [Fact]
        public void Estimate_MultiMemberGathering_CanExceedSoloCap()
        {
            // 単独上限（85%）は1名編成にのみ適用される。2名以上なら通常上限（95%）まで伸びる。
            var quest = new Quest { QuestType = QuestType.Gathering, Difficulty = 5 };

            double rate = SuccessRateCalculator.Estimate(PartyOf(MakeAdventurer(100), MakeAdventurer(100)), quest);

            Assert.True(rate > SuccessRateBalance.SoloGatheringMaxRate,
                $"2名編成の成功率({rate})は単独上限({SuccessRateBalance.SoloGatheringMaxRate})を超えられるはず");
        }

        [Fact]
        public void Estimate_SoloCap_DoesNotApplyToOtherQuestTypes()
        {
            // 採取以外の単独出撃には単独上限を適用しない（通常上限まで到達しうる）。
            var quest = new Quest { QuestType = QuestType.Patrol, Difficulty = 1 };

            double rate = SuccessRateCalculator.Estimate(PartyOf(MakeAdventurer(100)), quest);

            Assert.Equal(SuccessRateBalance.MaxRate, rate);
        }

        // ---------------- 討伐のロール不足補正（→ 仕様「-15%」） ----------------

        [Fact]
        public void Estimate_Subjugation_AppliesPenalty_WhenNoFrontLineMember()
        {
            // 同じ能力値でも、全員が後衛の討伐編成はロール不足として減算される。
            var quest = new Quest { QuestType = QuestType.Subjugation, Difficulty = 40 };

            var withFront = SuccessRateCalculator.Estimate(
                PartyOf(MakeAdventurer(50, Placement.Front), MakeAdventurer(50, Placement.Back)), quest);
            var allBack = SuccessRateCalculator.Estimate(
                PartyOf(MakeAdventurer(50, Placement.Back), MakeAdventurer(50, Placement.Back)), quest);

            Assert.True(withFront > allBack,
                $"前衛ありの成功率({withFront})は前衛不在({allBack})より高いはず");
        }

        [Fact]
        public void Estimate_RolePenalty_DoesNotApplyToNonSubjugationQuests()
        {
            // 採取・巡回・探索・護衛にはロール不足の概念を持ち込まない。
            var quest = new Quest { QuestType = QuestType.Gathering, Difficulty = 30 };

            var withFront = SuccessRateCalculator.Estimate(PartyOf(MakeAdventurer(50, Placement.Front), MakeAdventurer(50, Placement.Front)), quest);
            var allBack = SuccessRateCalculator.Estimate(PartyOf(MakeAdventurer(50, Placement.Back), MakeAdventurer(50, Placement.Back)), quest);

            Assert.Equal(withFront, allBack);
        }

        // ---------------- ボス任務の人数不足補正（→ 仕様「1人欠けるごとに-25%」） ----------------

        [Fact]
        public void Estimate_BossQuest_AppliesPenaltyPerMissingMember()
        {
            // 同条件の編成で人数だけを変え、1名減るごとに BossUndermannedPenaltyPerMember 分
            // 下がることを確認する（クランプに当たらないよう中庸な難易度に設定）。
            var quest = new Quest { QuestType = QuestType.Subjugation, Difficulty = 20, IsBoss = true };

            double four = SuccessRateCalculator.Estimate(
                PartyOf(MakeAdventurer(20), MakeAdventurer(20), MakeAdventurer(20), MakeAdventurer(20)), quest);
            double three = SuccessRateCalculator.Estimate(
                PartyOf(MakeAdventurer(20), MakeAdventurer(20), MakeAdventurer(20)), quest);

            // 3名は「1名欠け」なので、4名時より最低でもペナルティ分は低い
            //（点数自体も1名分減るため、差はペナルティ以上になる）。
            Assert.True(four - three >= SuccessRateBalance.BossUndermannedPenaltyPerMember - 0.0001,
                $"4名({four})と3名({three})の差はペナルティ({SuccessRateBalance.BossUndermannedPenaltyPerMember})以上のはず");
        }

        [Fact]
        public void Estimate_BossQuest_NoPenalty_WhenFullyManned()
        {
            // 総力戦（4名）ならボスペナルティは一切かからない。
            // 同じ編成・同じ難易度で IsBoss だけを切り替えて比較する。
            var members = new List<Adventurer> { MakeAdventurer(30), MakeAdventurer(30), MakeAdventurer(30), MakeAdventurer(30) };
            var normalQuest = new Quest { QuestType = QuestType.Subjugation, Difficulty = 40 };
            var bossQuest = new Quest { QuestType = QuestType.Subjugation, Difficulty = 40, IsBoss = true };

            Assert.Equal(
                SuccessRateCalculator.Estimate(members, normalQuest),
                SuccessRateCalculator.Estimate(members, bossQuest));
        }

        [Fact]
        public void Estimate_BossQuest_SoloSuffersFullUndermannedPenalty()
        {
            // 1名でのボス挑戦は3名欠け＝ペナルティ3回分。能力値を十分高くして基本成功率を
            // 上限（95%）に張り付かせた上で、そこからペナルティ分だけ引かれることを確認する。
            var quest = new Quest { QuestType = QuestType.Subjugation, Difficulty = 20, IsBoss = true };

            double rate = SuccessRateCalculator.Estimate(PartyOf(MakeAdventurer(60)), quest);

            double expected = SuccessRateBalance.MaxRate - 3 * SuccessRateBalance.BossUndermannedPenaltyPerMember;
            Assert.Equal(expected, rate, precision: 10);

            // 万全の編成なら「楽勝」帯に入る能力値でも、単独ボス挑戦では「かなり厳しい」まで落ちる。
            Assert.Equal(SuccessConfidence.Risky, SuccessRateCalculator.GetConfidence(rate));
        }

        // ---------------- 携行アイテム・環境ギミックの反映（出撃前に確定している情報） ----------------

        [Fact]
        public void Estimate_ReflectsGimmickCounterItem_InPartyPouch()
        {
            // 霊体（Undead）ギミックはフェーズ2（点数）にペナルティを与える。対策アイテム
            //（聖水）を携行させると対策達成度が上がり、予測成功率も上がる
            // ＝「持ち物を変えると勝算が変わる」因果がプレイヤーに伝わる。
            var quest = new Quest
            {
                QuestType = QuestType.Patrol,
                Difficulty = 40,
                EnvironmentTags = { EnvironmentTag.Undead },
            };

            var withoutItem = PartyOf(MakeAdventurer(20));
            var withItem = PartyOf(MakeAdventurer(20));
            withItem.TryAddConsumable(ConsumableCatalog.HolyWaterId);

            double rateWithout = SuccessRateCalculator.Estimate(withoutItem, quest);
            double rateWith = SuccessRateCalculator.Estimate(withItem, quest);

            Assert.True(rateWith > rateWithout,
                $"聖水を携行した場合の成功率({rateWith})は未携行({rateWithout})より高いはず");
        }

        // ---------------- 定性表現への丸め（情報公開の原則。→ コミットd7c7f39） ----------------

        [Theory]
        [InlineData(0.95, SuccessConfidence.Overwhelming)]
        [InlineData(0.80, SuccessConfidence.Overwhelming)] // 閾値ちょうど
        [InlineData(0.79, SuccessConfidence.Favorable)]
        [InlineData(0.60, SuccessConfidence.Favorable)]
        [InlineData(0.59, SuccessConfidence.Even)]
        [InlineData(0.40, SuccessConfidence.Even)]
        [InlineData(0.39, SuccessConfidence.Risky)]
        [InlineData(0.20, SuccessConfidence.Risky)]
        [InlineData(0.19, SuccessConfidence.Reckless)]
        [InlineData(0.10, SuccessConfidence.Reckless)]
        public void GetConfidence_MapsRateToQualitativeBand(double rate, SuccessConfidence expected)
        {
            Assert.Equal(expected, SuccessRateCalculator.GetConfidence(rate));
        }
    }
}
