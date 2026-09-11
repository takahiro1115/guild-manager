using System;
using GuildManager.Core.Balance;

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

        /// <summary>
        /// 探索規模。難易度とは独立した属性（仕様書 03 §4.0）。デフォルトはSmall
        /// （＝拘束1週。§4.0.1導入前の既存クエストと同じ挙動を保つ）。
        /// </summary>
        public QuestScale Scale { get; set; } = QuestScale.Small;

        /// <summary>
        /// 拘束週数。Scaleから導出する（仕様書 03 §4.0.1）。派遣してからこの週数が
        /// 満了するまで、パーティは「派遣中」状態になり索敵〜治安の解決は行われない。
        /// </summary>
        public int DurationWeeks => QuestBalance.GetDurationWeeks(Scale);

        public int RewardGold { get; set; }
        public int DeadlineWeeks { get; set; }
    }
}
