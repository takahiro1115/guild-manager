using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 出撃前の成功率予測エンジン（→ コアシステム刷新仕様「(1) 成功率予測エンジンの計算式」）。
    /// プレイヤーが編成を変えるたびに即座に再計算できる、乱数を使わない純粋計算。
    ///
    /// 計算手順：
    ///  1. 点数S＝Σメンバー点数＋ペア特性シナジー（→ QuestScoreCalculator・PairSynergyCalculator）。
    ///     環境ギミックのうちフェーズ2（点数）に効くものは、出撃前に確定している情報
    ///     （パーティのステータスと携行アイテム）だけで判定できるため予測にも反映する
    ///     ＝「解毒薬を持たせると勝算が上がる」という因果がプレイヤーに伝わる。
    ///  2. 要求値D＝クエストDifficulty×種別係数。
    ///  3. 基本成功率＝clamp(S/D × BaseCoefficient, MinRate, MaxRate)。
    ///  4. 人数・編成補正（採取の単独上限／討伐のロール不足／ボスの人数不足）。
    ///
    /// 予測に含めないもの（実解決時にしか決まらない乱数要素）：遭遇判定（奇襲/不意打ち）、
    /// ランダムイベント、HP消費ロール。そのため予測は「通常交戦を前提とした目安」であり、
    /// 実際の結果とは必ずしも一致しない（この揺らぎ自体がゲーム性の一部）。
    ///
    /// **算出した確率をそのままUIへ出さないこと。** 情報公開の原則（→ コミットd7c7f39）に
    /// 従い、GetConfidenceで定性表現（→ SuccessConfidence）へ丸めてから提示する。
    /// </summary>
    public static class SuccessRateCalculator
    {
        private static readonly IReadOnlyList<string> NoConsumables = new List<string>();

        /// <summary>
        /// 編成中のパーティでこのクエストに挑んだ場合の成功率（0.0〜1.0）を予測する。
        /// 携行アイテム（→ Party.ConsumableItemIds）も判定に含める。
        /// </summary>
        public static double Estimate(Party party, Quest quest) =>
            Estimate(party.Members, quest, party.ConsumableItemIds);

        /// <summary>
        /// メンバー一覧を直接渡す版（編成確定前のプレビュー用）。
        /// 空編成では出撃できないため0を返す（→ QuestResolver.Resolveは例外を投げる）。
        /// </summary>
        public static double Estimate(IReadOnlyList<Adventurer> members, Quest quest, IReadOnlyList<string>? consumableItemIds = null)
        {
            if (members.Count == 0)
                return 0;

            var items = consumableItemIds ?? NoConsumables;

            double score = QuestScoreCalculator.SumMemberScores(members, quest.QuestType)
                           + PairSynergyCalculator.Calculate(members, quest.QuestType);
            score *= GimmickEvaluator.GetPhaseMultiplier(quest.EnvironmentTags, GimmickPhase.Score, members, items);

            double requirement = QuestScoreCalculator.Requirement(quest);
            if (requirement <= 0)
                return SuccessRateBalance.MaxRate; // 難易度0のクエスト（理論上）は常に上限扱い

            double rate = Math.Clamp(
                score / requirement * SuccessRateBalance.BaseCoefficient,
                SuccessRateBalance.MinRate,
                SuccessRateBalance.MaxRate);

            rate -= GetRolePenalty(members, quest);
            rate -= GetBossUndermannedPenalty(members, quest);
            rate = ApplySoloGatheringCap(rate, members, quest);

            return Math.Clamp(rate, SuccessRateBalance.MinRate, SuccessRateBalance.MaxRate);
        }

        /// <summary>
        /// 討伐任務のロール不足ペナルティ（→ 仕様「ロール（前衛・後衛等）が不足している場合、
        /// 成功率にマイナス補正」）。前衛（Placement.Front）が1人もいない編成を「不足」とみなす。
        /// 討伐以外では0。
        /// </summary>
        private static double GetRolePenalty(IReadOnlyList<Adventurer> members, Quest quest)
        {
            if (quest.QuestType != QuestType.Subjugation)
                return 0;

            bool hasFrontLine = members.Any(m => m.Placement == Placement.Front);
            return hasFrontLine ? 0 : SuccessRateBalance.SubjugationMissingRolePenalty;
        }

        /// <summary>
        /// ボス（昇格試験）任務の人数不足ペナルティ（→ 仕様「参加人数が3名以下の場合は
        /// 大幅ペナルティ（1人欠けるごとに-25%）」）。総力戦の想定人数に足りない分だけ減算する。
        /// </summary>
        private static double GetBossUndermannedPenalty(IReadOnlyList<Adventurer> members, Quest quest)
        {
            if (!quest.IsBoss)
                return 0;

            int missing = SuccessRateBalance.BossFullMemberCount - members.Count;
            if (missing <= 0)
                return 0;

            return missing * SuccessRateBalance.BossUndermannedPenaltyPerMember;
        }

        /// <summary>
        /// 採取任務を単独で行う場合の上限（→ 仕様「単独（1名）でも適性ステータスが高ければ
        /// 成功率最大85%まで到達可能」）。単独採取は人数不足のペナルティを受けない代わりに、
        /// 通常の上限（95%）までは届かない。
        /// </summary>
        private static double ApplySoloGatheringCap(double rate, IReadOnlyList<Adventurer> members, Quest quest)
        {
            if (quest.QuestType != QuestType.Gathering || members.Count != 1)
                return rate;

            return Math.Min(rate, SuccessRateBalance.SoloGatheringMaxRate);
        }

        /// <summary>
        /// 閾値比較の許容誤差。成功率は「上限0.95から人数不足ペナルティ0.25×3を引く」といった
        /// 減算の積み重ねで作られるため、本来ちょうど閾値（例：0.20）になるはずの組み合わせが
        /// 0.19999999999999996 のようにわずかに下回り、帯が1段ずれることがある。
        /// しかもこの値は「基本成功率が上限に張り付く強い編成が単独でボスに挑んだ場合」に
        /// 必ず生じる再現性のある境界のため、誤差を吸収してCSVの意図（「0.20以上はRisky」）
        /// どおりに判定する。
        /// </summary>
        private const double ThresholdEpsilon = 1e-9;

        /// <summary>
        /// 成功率を、UIへ提示してよい定性表現（→ SuccessConfidence）へ丸める。
        /// 情報公開の原則（→ コミットd7c7f39）により、UIにはこちらだけを出す。
        /// </summary>
        public static SuccessConfidence GetConfidence(double rate)
        {
            if (AtLeast(rate, SuccessRateBalance.ConfidenceThresholdOverwhelming)) return SuccessConfidence.Overwhelming;
            if (AtLeast(rate, SuccessRateBalance.ConfidenceThresholdFavorable)) return SuccessConfidence.Favorable;
            if (AtLeast(rate, SuccessRateBalance.ConfidenceThresholdEven)) return SuccessConfidence.Even;
            if (AtLeast(rate, SuccessRateBalance.ConfidenceThresholdRisky)) return SuccessConfidence.Risky;
            return SuccessConfidence.Reckless;
        }

        private static bool AtLeast(double rate, double threshold) => rate >= threshold - ThresholdEpsilon;

        /// <summary>編成から直接、定性表現を得るショートカット（UIの編成画面用）。</summary>
        public static SuccessConfidence GetConfidence(Party party, Quest quest) =>
            GetConfidence(Estimate(party, quest));
    }
}
