namespace GuildManager.Core.Models
{
    /// <summary>
    /// 大迷宮（ダンジョン）への出撃の種別（→ ActiveDungeonMission）。
    /// 「調査で情報を買う → 対策を組んで討伐する」という2段構えに、素材採取（第3の任務）を加えた3種。
    /// </summary>
    public enum DungeonMissionType
    {
        /// <summary>調査任務：解析率を上げる低リスク経路（→ Systems.ScoutingResolver）。ボスを対象にする。</summary>
        Scouting,

        /// <summary>ボス討伐：未対策ギミックがあれば強制除籍の危険がある決戦（→ Systems.DungeonResolver）。ボスを対象にする。</summary>
        BossAssault,

        /// <summary>
        /// 探索（採取）任務：特定のボスではなくフィールドそのものを対象にする低リスク経路
        /// （→ Systems.GatheringResolver）。素材とゴールドを獲得する。
        /// </summary>
        Gathering,
    }
}
