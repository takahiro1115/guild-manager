namespace GuildManager.Core.Models
{
    /// <summary>
    /// 探索規模。仕様書 03 §4.0 参照。QuestDifficulty（難易度）とは独立した属性で、
    /// クエストの拘束期間（週数）を決める（→ Quest.DurationWeeks・QuestBalance）。
    /// </summary>
    public enum QuestScale
    {
        /// <summary>小規模：短期決戦（→ BAL: クエスト/探索規模。例1週）。</summary>
        Small,

        /// <summary>中規模（例2〜3週）。</summary>
        Medium,

        /// <summary>大規模：長期の調査任務（例4週以上）。</summary>
        Large
    }
}
