using System;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// クエストデータモデル。仕様書 03 §4.0 参照。
    /// </summary>
    public class Quest
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public QuestRank Rank { get; set; }

        /// <summary>1〜100。値が高いほど難しい。→ BAL: クエスト</summary>
        public int Difficulty { get; set; }

        /// <summary>1〜100。SCTと同スケール。→ BAL: クエスト</summary>
        public int ScoutRequirement { get; set; }

        public int RewardGold { get; set; }
        public int DeadlineWeeks { get; set; }
    }
}
