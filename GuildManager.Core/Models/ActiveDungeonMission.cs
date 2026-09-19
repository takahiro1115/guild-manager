using System.Collections.Generic;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// 大迷宮へ出撃中の部隊1件（→ Systems.DungeonExpeditionSystem）。
    ///
    /// 通常クエストの派遣（→ ActiveDispatch）と同じく、出撃操作の時点では解決せず、
    /// 週次決算でまとめて解決する。同時出撃枠（→ GameState.UnlockedSquadSlots）は
    /// 通常クエストの派遣と共有する。
    ///
    /// 「毎回1Fリセット・複数週潜行型」（2026年9月改訂）：道中調査（Scouting）の部隊は
    /// 出撃時に必ず1階層から潜り始め、帰還するまで複数週にわたってこの1件が残り続ける
    /// （→ Status・CurrentFloor）。帰還（撤退・勝利・全滅）でこの1件は解除され、
    /// 次回の出撃はまた1階層から始まる。探索（採取、Gathering）は従来どおり1週で帰還する。
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
        /// 出撃時点の攻略対象の階層ボス。Field.Bosses 内の同一インスタンスを指す（解決時に IntelRate・
        /// IsDefeated を直接書き換えるため）。調査（Scouting）・討伐（BossAssault）のみ設定される。
        /// 採取（Gathering）は特定のボスではなくフィールドそのものを対象にするためnull。
        /// 実際に足止めされたボスは TargetedBoss を参照すること。
        /// </summary>
        public FloorBoss? Boss { get; set; }

        public DungeonMissionType MissionType { get; set; }

        /// <summary>遠征状態（→ ExpeditionStatus）。</summary>
        public ExpeditionStatus Status { get; set; } = ExpeditionStatus.Advancing;

        /// <summary>現在潜行中の階層。出撃時は常に1から始まる（→ 1Fリセットルール）。</summary>
        public int CurrentFloor { get; set; } = 1;

        /// <summary>扉前に到達した（まだ倒していない）ボス。到達前はnull。Field.Bosses 内の同一インスタンス。</summary>
        public FloorBoss? TargetedBoss { get; set; }

        /// <summary>この出撃が週次決算を経た回数（0＝まだ出発前で、取り消しが可能）。</summary>
        public int WeeksElapsed { get; set; }

        /// <summary>道中で拾い集めた素材（素材Id→個数）。ギルドへ帰還した時点で GameState.Materials へ格納される。</summary>
        public Dictionary<string, int> CarriedMaterials { get; set; } = new();

        /// <summary>道中で拾い集めたゴールド。ギルドへ帰還した時点で GameState.Gold へ格納される。</summary>
        public int CarriedGold { get; set; }
    }
}
