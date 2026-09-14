using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 統一点数計算式（→ 03 §4.2.3）のうち、乱数に依存しない決定論的な部分を切り出した
    /// 純粋計算クラス。QuestResolver（実際の解決）と SuccessRateCalculator（出撃前の
    /// 成功率予測）の両方がここを呼ぶ。
    ///
    /// 分離した理由（→ コアシステム刷新仕様「成功率予測エンジン」）：
    /// 予測と実解決で別々の式を持つと、片方だけを調整した際に「表示された成功率と
    /// 実際の結果が食い違う」という最悪の不具合になる。点数・要求値の算出は必ず
    /// この1箇所を経由させ、二重管理を構造的に不可能にしている
    /// （→ 03 §10.1「値の二重管理を避ける」と同じ考え方をロジックにも適用）。
    ///
    /// ここに含めないもの（実解決時にしか決まらない要素）：
    ///  - 遭遇判定による戦闘力倍率（奇襲/不意打ち。→ QuestResolverのフェーズ1）
    ///  - 不意打ち時の要求値増加
    /// これらは乱数で決まるため、予測側では「通常交戦」を前提に据え置く。
    /// </summary>
    public static class QuestScoreCalculator
    {
        /// <summary>
        /// メンバー1名分の点数（→ 03 §4.2.3）。討伐ではこれが従来の「個人CP」と
        /// 完全に同じ式になる。装備ボーナス・個人特性ボーナスを加算し、配置補正と
        /// 負傷時の効率低下（現在HP/最大HP）を掛ける。
        /// </summary>
        public static double MemberScore(Adventurer a, QuestType questType)
        {
            double hpRatio = (double)a.CurrentHP / a.MaxHP;

            double statSum = 0;
            foreach (var (stat, weight) in QuestScoringBalance.GetStatWeights(questType))
                statSum += a.GetEffectiveStat(stat) * weight;

            double baseScore = statSum + GetEquipmentBonus(a, questType) + GetTraitScoreBonus(a, questType);
            return baseScore * GetPlacementCorrection(a, questType) * hpRatio;
        }

        /// <summary>パーティ全員分の点数合計（ペア特性シナジー・遭遇倍率は含まない）。</summary>
        public static double SumMemberScores(IReadOnlyList<Adventurer> members, QuestType questType) =>
            members.Sum(m => MemberScore(m, questType));

        /// <summary>
        /// クエストの要求値＝Difficulty×種別ごとの要求係数（→ QuestScoringBalance）。
        /// 不意打ちによる増加は含まない（実解決時にQuestResolverが上乗せする）。
        /// </summary>
        public static double Requirement(Quest quest) =>
            quest.Difficulty * QuestScoringBalance.GetRequirementCoefficient(quest.QuestType);

        /// <summary>
        /// クエスト種別限定の個人特性ボーナス（→ 03 §4.2.3、項目64）。「田舎育ち」の探索ボーナス等。
        /// TraitEffectType.QuestTypeScoreBonus は TargetStat にクエスト種別名を持つため、
        /// 既存の SumTraitEffect のフィルタをそのまま使える。該当特性が無ければ0。
        /// </summary>
        private static double GetTraitScoreBonus(Adventurer a, QuestType questType) =>
            a.SumTraitEffect(TraitEffectType.QuestTypeScoreBonus, questType.ToString());

        /// <summary>
        /// 装備ボーナス。討伐では装備（武器・アクセサリー）のCP固定加算（→ 03 §4.2.2）を
        /// ステータス由来の寄与と同じ扱いで加算し、負傷時の効率低下(hpRatio)・配置補正の対象にする。
        ///
        /// 討伐以外（探索・護衛・採取・巡回）は「予約枠」として常に0を返す（→ 03 §4.2.3）。
        /// 将来それらで効く装備効果を追加する際は、このメソッドが拡張点になる。
        /// </summary>
        private static double GetEquipmentBonus(Adventurer a, QuestType questType) =>
            QuestScoringBalance.UsesCombatResolution(questType)
                ? a.GetEquipmentBonus(EquipmentEffectType.PersonalCpBonus)
                : 0;

        /// <summary>配置補正（前衛/後衛）。討伐のみ適用する（→ 03 §4.2.3）。</summary>
        private static double GetPlacementCorrection(Adventurer a, QuestType questType) =>
            QuestScoringBalance.UsesCombatResolution(questType)
                ? PlacementBalance.GetPersonalCpCorrection(a.JobClass, a.Placement)
                : 1.0;
    }
}
