namespace GuildManager.Core.Models
{
    /// <summary>
    /// 大迷宮（ダンジョン）への出撃の種別（→ ActiveDungeonMission）。
    /// 「調査で情報を買う → 対策を組んで討伐する」という2段構えのどちらで出るかを表す。
    /// </summary>
    public enum DungeonMissionType
    {
        /// <summary>調査任務：解析率を上げる低リスク経路（→ Systems.ScoutingResolver）。</summary>
        Scouting,

        /// <summary>ボス討伐：未対策ギミックがあれば強制除籍の危険がある決戦（→ Systems.DungeonResolver）。</summary>
        BossAssault,
    }
}
