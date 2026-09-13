using System;
using System.Globalization;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// クエスト関連のバランス値。仕様書 03 §4.0 参照。
    /// 値は docs/04_バランス表/quest.csv（key,value形式）・quest_templates.csv
    /// （テンプレートテーブル）から読み込む（→ 03 §10.1、項目58）。
    /// </summary>
    public static class QuestBalance
    {
        private const string QuestFileName = "quest.csv";
        private const string TemplatesFileName = "quest_templates.csv";

        private static readonly int DurationWeeksSmall = BalanceData.GetInt(QuestFileName, "DurationWeeks_Small");
        private static readonly int DurationWeeksMedium = BalanceData.GetInt(QuestFileName, "DurationWeeks_Medium");
        private static readonly int DurationWeeksLarge = BalanceData.GetInt(QuestFileName, "DurationWeeks_Large");

        /// <summary>
        /// 探索規模から拘束週数を導出する。→ BAL: クエスト/探索規模。
        /// 各規模の代表値（幅ではなく単一の値）：小・中・大。
        /// </summary>
        public static int GetDurationWeeks(QuestScale scale) => scale switch
        {
            QuestScale.Small => DurationWeeksSmall,
            QuestScale.Medium => DurationWeeksMedium,
            QuestScale.Large => DurationWeeksLarge,
            _ => DurationWeeksSmall,
        };

        /// <summary>受注可能クエスト一覧が常に維持すべき件数。→ BAL: クエスト/同時掲示数</summary>
        public static readonly int DesiredAvailableCount = BalanceData.GetInt(QuestFileName, "DesiredAvailableCount");

        /// <summary>
        /// クエストの期限（DeadlineWeeks）のレンジ。→ BAL: クエスト/期限。
        /// v1.10改訂：複数週クエスト（探索規模：中・大）の拘束期間に対して、留守番人員が
        /// 対応できる余裕を持たせるため、旧値（3〜4週）から5〜8週へ引き上げた
        /// （→ 03 §4.0）。Templates各行・SampleData.CreateStarterQuestsの両方がこの
        /// レンジに収まるようにする。
        /// </summary>
        public static readonly int MinDeadlineWeeks = BalanceData.GetInt(QuestFileName, "MinDeadlineWeeks");
        public static readonly int MaxDeadlineWeeks = BalanceData.GetInt(QuestFileName, "MaxDeadlineWeeks");

        /// <summary>
        /// 長期遠征クエスト（探索規模：中・大）の解禁に必要な現役ロースター人数（→ 03 §4.0、
        /// v1.10改訂で新設）。これ未満の間は、Scaleが中・大のテンプレートを受注可能一覧の
        /// 生成・補充対象から除外する（→ QuestBoardSystem.GenerateQuest）。小規模（Scale=小）
        /// クエストは人数に関わらず常に出現する。
        /// </summary>
        public static readonly int LongExpeditionRosterThreshold = BalanceData.GetInt(QuestFileName, "LongExpeditionRosterThreshold");

        /// <summary>
        /// クエスト補充用のテンプレート。QuestBoardSystemが受注可能一覧を補充する際に
        /// ここからランダムに1件選んで複製する（SampleData.CreateStarterQuests相当の
        /// 値の組み合わせ。→ BAL: クエスト）。
        ///
        /// 事前調査メモ（項目55、v1.10改訂）：本テーブルには元々Scale（探索規模）の列が
        /// 存在せず、生成される全クエストが暗黙的にQuestScale.Small（既定値）になっていた
        /// （＝これまで自動補充で中・大規模クエストが出現したことは一度も無かった）。
        /// 今回Scale列を追加し、あわせて中・大規模のテンプレートも新設した
        /// （でなければ「6名未満で中・大が出現しない」制御自体、対象が存在せず無意味になるため）。
        /// </summary>
        public static readonly (string Name, QuestType Type, QuestRank Rank, int Difficulty, int ScoutRequirement, int RewardGold, int DeadlineWeeks, QuestScale Scale)[] Templates = BuildTemplates();

        private static (string, QuestType, QuestRank, int, int, int, int, QuestScale)[] BuildTemplates()
        {
            var (_, rows) = BalanceData.GetTable(TemplatesFileName);
            var result = new (string, QuestType, QuestRank, int, int, int, int, QuestScale)[rows.Count];

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                string name = row[0];

                if (!Enum.TryParse<QuestType>(row[1], out var type))
                    throw new BalanceDataException($"{TemplatesFileName} の{i + 2}行目のtype「{row[1]}」をQuestTypeとして解釈できません。");
                if (!Enum.TryParse<QuestRank>(row[2], out var rank))
                    throw new BalanceDataException($"{TemplatesFileName} の{i + 2}行目のrank「{row[2]}」をQuestRankとして解釈できません。");
                if (!int.TryParse(row[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int difficulty))
                    throw new BalanceDataException($"{TemplatesFileName} の{i + 2}行目のdifficulty「{row[3]}」を整数として解釈できません。");
                if (!int.TryParse(row[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int scoutRequirement))
                    throw new BalanceDataException($"{TemplatesFileName} の{i + 2}行目のscout_requirement「{row[4]}」を整数として解釈できません。");
                if (!int.TryParse(row[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rewardGold))
                    throw new BalanceDataException($"{TemplatesFileName} の{i + 2}行目のreward_gold「{row[5]}」を整数として解釈できません。");
                if (!int.TryParse(row[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int deadlineWeeks))
                    throw new BalanceDataException($"{TemplatesFileName} の{i + 2}行目のdeadline_weeks「{row[6]}」を整数として解釈できません。");
                if (!Enum.TryParse<QuestScale>(row[7], out var scale))
                    throw new BalanceDataException($"{TemplatesFileName} の{i + 2}行目のscale「{row[7]}」をQuestScaleとして解釈できません。");

                result[i] = (name, type, rank, difficulty, scoutRequirement, rewardGold, deadlineWeeks, scale);
            }

            return result;
        }
    }
}
