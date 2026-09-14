namespace GuildManager.Core.Models
{
    /// <summary>
    /// クエスト種別。仕様書 03 §4.0 参照。
    /// 脅威度（§4.4）への影響は討伐(Subjugation)のみが対象。
    /// クエスト適性ボーナス（→ 03 §4.2.1、v1.7改訂）で種別ごとの重視ステータスに接続済み
    /// （護衛=MND・VIT、調査・探索=AGI・DEX・INT）。敵種別によるさらなる細分化はpost-MVP（→ §11）。
    /// </summary>
    public enum QuestType
    {
        Subjugation,
        Exploration,
        Escort,

        /// <summary>
        /// 採取（→ ギルド運営コアシステム刷新仕様）。AGI・DEX中心の低危険度任務。
        /// 討伐フロー（致死判定）を通らないため序盤の「即詰み」が発生しない。
        /// 報酬（採取量）は派遣人数に正比例する（→ QuestScoringBalance.ScalesRewardWithMemberCount）。
        /// </summary>
        Gathering,

        /// <summary>
        /// 巡回（→ ギルド運営コアシステム刷新仕様）。全ステータスを均等に評価する
        /// （requiredStatType: balanced 相当）低危険度任務。採取と同じく致死判定は通らない。
        /// </summary>
        Patrol,
    }
}
