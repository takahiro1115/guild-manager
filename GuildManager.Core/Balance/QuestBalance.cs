using System.Collections.Generic;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// クエスト関連の暫定バランス値。仕様書 03 §4.0 参照。
    /// 05技術メモ§3の方針（数値を各Systemクラスへ直書きしない）に沿い、ここへ集約した。
    /// 04_バランス表.xlsx からの読み込みへの置き換え（Phase 4での外部化）はまだ行っておらず、
    /// 現状はすべて仮値の定数。
    /// </summary>
    public static class QuestBalance
    {
        /// <summary>
        /// 探索規模から拘束週数を導出する。→ BAL: クエスト/探索規模。
        /// 現状は各規模の代表値（幅ではなく単一の仮値）：小=1週、中=2週、大=4週。
        /// </summary>
        public static int GetDurationWeeks(QuestScale scale) => scale switch
        {
            QuestScale.Small => 1,
            QuestScale.Medium => 2,
            QuestScale.Large => 4,
            _ => 1,
        };

        /// <summary>受注可能クエスト一覧が常に維持すべき件数。→ BAL: クエスト/同時掲示数</summary>
        public const int DesiredAvailableCount = 3;

        /// <summary>
        /// クエストの期限（DeadlineWeeks）の暫定レンジ。→ BAL: クエスト/期限。
        /// v1.10改訂：複数週クエスト（探索規模：中・大）の拘束期間に対して、留守番人員が
        /// 対応できる余裕を持たせるため、旧仮値（3〜4週）から5〜8週へ引き上げた
        /// （→ 03 §4.0）。Templates各行・SampleData.CreateStarterQuestsの両方がこの
        /// レンジに収まるようにする。
        /// </summary>
        public const int MinDeadlineWeeks = 5;
        public const int MaxDeadlineWeeks = 8;

        /// <summary>
        /// 長期遠征クエスト（探索規模：中・大）の解禁に必要な現役ロースター人数（→ 03 §4.0、
        /// v1.10改訂で新設）。これ未満の間は、Scaleが中・大のテンプレートを受注可能一覧の
        /// 生成・補充対象から除外する（→ QuestBoardSystem.GenerateQuest）。小規模（Scale=小）
        /// クエストは人数に関わらず常に出現する。
        /// </summary>
        public const int LongExpeditionRosterThreshold = 6;

        /// <summary>
        /// クエスト補充用のテンプレート。仕様書03 §4.0「事前調査」時点でクエスト生成ロジックが
        /// 未実装だったため、QuestBoardSystemが受注可能一覧を補充する際にここからランダムに
        /// 1件選んで複製する（SampleData.CreateStarterQuests相当の簡易な値の組み合わせ。
        /// 各値は仮値、→ BAL: クエスト）。
        ///
        /// 事前調査メモ（項目55、v1.10改訂）：本テーブルには元々Scale（探索規模）の列が
        /// 存在せず、生成される全クエストが暗黙的にQuestScale.Small（既定値）になっていた
        /// （＝これまで自動補充で中・大規模クエストが出現したことは一度も無かった）。
        /// 今回Scale列を追加し、あわせて中・大規模のテンプレートも新設した
        /// （でなければ「6名未満で中・大が出現しない」制御自体、対象が存在せず無意味になるため）。
        /// </summary>
        public static readonly (string Name, QuestType Type, QuestRank Rank, int Difficulty, int ScoutRequirement, int RewardGold, int DeadlineWeeks, QuestScale Scale)[] Templates =
        {
            ("ゴブリン討伐", QuestType.Subjugation, QuestRank.E, 10, 10, 90, 5, QuestScale.Small),
            ("山道の盗賊退治", QuestType.Subjugation, QuestRank.D, 22, 18, 180, 6, QuestScale.Small),
            ("野盗のアジト掃討", QuestType.Subjugation, QuestRank.D, 25, 15, 200, 6, QuestScale.Small),
            ("廃坑の魔物調査", QuestType.Exploration, QuestRank.C, 35, 30, 320, 7, QuestScale.Small),
            ("古代遺跡の調査", QuestType.Exploration, QuestRank.C, 30, 35, 300, 7, QuestScale.Small),
            ("商隊の護衛", QuestType.Escort, QuestRank.D, 20, 20, 220, 6, QuestScale.Small),

            // 長期遠征クエスト（探索規模：中・大）。現役ロースター6名以上でのみ出現する
            // （→ LongExpeditionRosterThreshold・QuestBoardSystem.GenerateQuest）。
            ("辺境監視隊への物資輸送", QuestType.Escort, QuestRank.C, 40, 32, 450, 8, QuestScale.Medium),
            ("山脈越えの隊商護衛", QuestType.Escort, QuestRank.B, 55, 40, 650, 8, QuestScale.Medium),
            ("失われた谷の大規模調査", QuestType.Exploration, QuestRank.B, 60, 50, 800, 8, QuestScale.Large),
            ("辺境砦の長期籠城討伐支援", QuestType.Subjugation, QuestRank.B, 65, 45, 850, 8, QuestScale.Large),
        };
    }
}
