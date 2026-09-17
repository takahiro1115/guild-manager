namespace GuildManager.Core.Models
{
    /// <summary>
    /// 大迷宮へ出撃中の部隊1件（→ Systems.DungeonExpeditionSystem）。
    ///
    /// 通常クエストの派遣（→ ActiveDispatch）と同じく、出撃操作の時点では解決せず、
    /// 次の週次決算でまとめて解決する。拘束は常に1週（調査・討伐・採取のいずれも複数週にはしない）。
    /// 同時出撃枠（→ GameState.UnlockedSquadSlots）は通常クエストの派遣と共有する。
    /// </summary>
    public class ActiveDungeonMission
    {
        public Party Party { get; set; } = new();

        /// <summary>
        /// 出撃先のフィールド。GameState.DungeonFields 内の同一インスタンスを指す
        /// （解決時に ReachedFloor・IsUnlocked を直接書き換えるため）。調査・討伐・採取の
        /// いずれも出撃時点でこのフィールドに固定される。
        /// </summary>
        public DungeonField Field { get; set; } = new();

        /// <summary>
        /// 対象の階層ボス。Field.Bosses 内の同一インスタンスを指す（解決時に IntelRate・
        /// IsDefeated を直接書き換えるため）。調査（Scouting）・討伐（BossAssault）のみ設定される。
        /// 採取（Gathering）は特定のボスではなくフィールドそのものを対象にするためnull。
        /// </summary>
        public FloorBoss? Boss { get; set; }

        public DungeonMissionType MissionType { get; set; }
    }
}
