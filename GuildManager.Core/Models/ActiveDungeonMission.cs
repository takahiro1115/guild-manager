namespace GuildManager.Core.Models
{
    /// <summary>
    /// 大迷宮へ出撃中の部隊1件（→ Systems.DungeonExpeditionSystem）。
    ///
    /// 通常クエストの派遣（→ ActiveDispatch）と同じく、出撃操作の時点では解決せず、
    /// 次の週次決算でまとめて解決する。拘束は常に1週（調査・討伐とも複数週にはしない）。
    /// 同時出撃枠（→ GameState.UnlockedSquadSlots）は通常クエストの派遣と共有する。
    /// </summary>
    public class ActiveDungeonMission
    {
        public Party Party { get; set; } = new();

        /// <summary>
        /// 対象の階層ボス。GameState.FloorBosses 内の同一インスタンスを指す
        /// （解決時に IntelRate・IsDefeated を直接書き換えるため）。
        /// </summary>
        public FloorBoss Boss { get; set; } = new();

        public DungeonMissionType MissionType { get; set; }
    }
}
