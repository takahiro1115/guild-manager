using System;
using System.Collections.Generic;
using System.Globalization;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// クエスト種別ごとの統一点数計算式（→ 03 §4.2.3、項目63で新設）のバランス値。
    ///
    /// 討伐・探索・護衛は「同じ点数計算式の、対象ステータス・重みが違うバリエーション」として
    /// 統一されている：
    /// <code>
    /// 点数   = Σ(各メンバーの対象ステータス合計 × HP比率) + 装備ボーナス（討伐のみ） × 配置補正（討伐のみ）
    /// 要求値 = クエストDifficulty × 種別ごとの要求係数
    /// Ratio  = 点数 ÷ 要求値
    /// </code>
    ///
    /// 値の出どころ：
    ///  - 対象ステータス・重み → docs/04_バランス表/quest_type_weights.csv（種別×ステータスのテーブル）
    ///  - 探索・護衛の要求係数・Ratio閾値・軽量HP消費レンジ → docs/04_バランス表/quest_scoring.csv
    ///  - **討伐の要求係数は CombatBalance.EnemyCpCoefficient（combat.csv）をそのまま使う。**
    ///    討伐にとっての要求値は従来どおり「敵CP」であり、同じ値をquest_scoring.csvにも
    ///    置くと二重管理になる（→ 03 §10.1「値の二重管理を避ける」）ため、あえて重複させない。
    /// </summary>
    public static class QuestScoringBalance
    {
        private const string WeightsFileName = "quest_type_weights.csv";
        private const string ScoringFileName = "quest_scoring.csv";

        private static readonly Dictionary<QuestType, (string Stat, double Weight)[]> StatWeights = BuildStatWeights();

        private static Dictionary<QuestType, (string Stat, double Weight)[]> BuildStatWeights()
        {
            var (header, rows) = BalanceData.GetTable(WeightsFileName);
            // header[0]="quest_type"、header[1..]がステータス名（STR,AGI,VIT,MND,DEX,LDR,INT）。
            var result = new Dictionary<QuestType, (string Stat, double Weight)[]>();

            foreach (var row in rows)
            {
                if (!Enum.TryParse<QuestType>(row[0], out var questType))
                    throw new BalanceDataException($"{WeightsFileName} のquest_type列「{row[0]}」をQuestTypeとして解釈できません。");

                var weights = new (string Stat, double Weight)[header.Length - 1];
                for (int col = 1; col < header.Length; col++)
                {
                    if (!double.TryParse(row[col], NumberStyles.Float, CultureInfo.InvariantCulture, out double weight))
                        throw new BalanceDataException($"{WeightsFileName} の{row[0]}行・{header[col]}列の値「{row[col]}」を数値として解釈できません。");
                    weights[col - 1] = (header[col], weight);
                }
                result[questType] = weights;
            }

            return result;
        }

        /// <summary>
        /// 指定クエスト種別の対象ステータスと重み。対象外のステータスは重み0で含まれる
        /// （0を掛けても結果に寄与しないため、呼び出し側は種別を意識せず一律に合算できる）。
        /// </summary>
        public static (string Stat, double Weight)[] GetStatWeights(QuestType questType)
        {
            if (!StatWeights.TryGetValue(questType, out var weights))
                throw new BalanceDataException($"{WeightsFileName} にクエスト種別「{questType}」の行がありません。");
            return weights;
        }

        /// <summary>
        /// 種別ごとの要求係数（要求値＝クエストDifficulty×この係数）。討伐は従来の敵CP係数
        /// （CombatBalance.EnemyCpCoefficient）をそのまま使う（→ クラスdocコメント）。
        /// </summary>
        public static double GetRequirementCoefficient(QuestType questType) => questType switch
        {
            QuestType.Subjugation => CombatBalance.EnemyCpCoefficient,
            QuestType.Exploration => BalanceData.GetDouble(ScoringFileName, "RequirementCoefficient_Exploration"),
            QuestType.Escort => BalanceData.GetDouble(ScoringFileName, "RequirementCoefficient_Escort"),
            QuestType.Gathering => BalanceData.GetDouble(ScoringFileName, "RequirementCoefficient_Gathering"),
            QuestType.Patrol => BalanceData.GetDouble(ScoringFileName, "RequirementCoefficient_Patrol"),
            _ => CombatBalance.EnemyCpCoefficient,
        };

        /// <summary>
        /// この種別が討伐系の解決フロー（4区分・致死判定・古傷・戦死・装備ボーナス・配置補正）を
        /// 使うかどうか。探索・護衛・採取・巡回は3区分＋軽量HP消費の別フローになる
        /// （→ 03 §4.2.3）。採取・巡回が討伐フローを通らないことが、序盤の「即詰み防止」
        /// （キャラロストが起こらない）をそのまま担保している（→ コアシステム刷新仕様）。
        /// </summary>
        public static bool UsesCombatResolution(QuestType questType) => questType == QuestType.Subjugation;

        /// <summary>
        /// この種別の報酬が派遣人数に正比例するかどうか（→ コアシステム刷新仕様
        /// 「採取量の変動：素材数 ＝ 基本数 × 派遣人数」）。採取のみtrue。
        /// 「4人で行けば4倍採れるが、他の任務に人を回せなくなる」という編成上の
        /// トレードオフを作るための仕組み（討伐・探索等は人数を増やしても報酬は増えない）。
        /// </summary>
        public static bool ScalesRewardWithMemberCount(QuestType questType) => questType == QuestType.Gathering;

        // ---- 探索・護衛の3区分Ratio閾値（→ 03 §4.2.3）。討伐の4区分はCombatBalance側。 ----
        public static readonly double RatioThresholdGreatSuccess = BalanceData.GetDouble(ScoringFileName, "RatioThreshold_GreatSuccess");
        public static readonly double RatioThresholdSuccess = BalanceData.GetDouble(ScoringFileName, "RatioThreshold_Success");

        // ---- 探索・護衛の軽量HP消費レンジ（→ 03 §4.2.3）。致死判定には接続しない。 ----
        public static readonly int HpLossPctGreatSuccessMin = BalanceData.GetInt(ScoringFileName, "HpLossPct_GreatSuccess_Min");
        public static readonly int HpLossPctGreatSuccessMax = BalanceData.GetInt(ScoringFileName, "HpLossPct_GreatSuccess_Max");
        public static readonly int HpLossPctSuccessMin = BalanceData.GetInt(ScoringFileName, "HpLossPct_Success_Min");
        public static readonly int HpLossPctSuccessMax = BalanceData.GetInt(ScoringFileName, "HpLossPct_Success_Max");
        public static readonly int HpLossPctFailureMin = BalanceData.GetInt(ScoringFileName, "HpLossPct_Failure_Min");
        public static readonly int HpLossPctFailureMax = BalanceData.GetInt(ScoringFileName, "HpLossPct_Failure_Max");

        // ---- 低危険度任務の軽傷判定（→ コアシステム刷新仕様「負傷判定（即詰み防止）」） ----
        // 討伐フロー（致死判定）を通らない任務でも、HPを大きく削られたメンバーは
        // 数週間の休養が必要になる。キャラロストは発生しないが「無傷で回し続けられる」
        // わけでもない、という中間の消耗を表現する。

        /// <summary>残HP比率がこれ以下になったメンバーは軽傷を負う。</summary>
        public static readonly double LightInjuryHpRatioThreshold = BalanceData.GetDouble(ScoringFileName, "LightInjury_HpRatioThreshold");

        /// <summary>軽傷の全治週数レンジ。</summary>
        public static readonly int LightInjuryWeeksMin = BalanceData.GetInt(ScoringFileName, "LightInjury_WeeksMin");
        public static readonly int LightInjuryWeeksMax = BalanceData.GetInt(ScoringFileName, "LightInjury_WeeksMax");
    }
}
