using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 大迷宮への出撃1件分の解決結果（→ DungeonExpeditionSystem.ProcessWeeklyMissions）。
    /// UI側（Godot）はこれを見て週報ログに表示する（DispatchResolutionと同じ役割）。
    ///
    /// 調査任務（→ MissionType.Scouting）は、解決時の状況でさらに2つに分岐する
    /// （→ 03 §4.5.2）：道中進軍中なら TraversalResult、既にボス階層に到達していれば
    /// ScoutingResult。ボス討伐（→ MissionType.BossAssault）なら DungeonResult。
    /// 探索・採取（→ MissionType.Gathering）なら GatheringResult。4つのうち、いずれか1つだけが入る。
    /// </summary>
    public class DungeonMissionResolution
    {
        public Party Party { get; }

        /// <summary>対象のボス。調査・討伐のみ非null。採取（Gathering）は特定のボスを対象にしないためnull。</summary>
        public FloorBoss? Boss { get; }

        /// <summary>出撃先のフィールド。調査・討伐・採取のいずれも必ず設定される。</summary>
        public DungeonField Field { get; }

        public DungeonMissionType MissionType { get; }

        /// <summary>解決前の解析率（週報で「○% → ○%」と表示するため）。道中進軍・採取では変化しない。</summary>
        public double IntelRateBefore { get; }

        public ScoutingResult? ScoutingResult { get; }
        public DungeonResult? DungeonResult { get; }
        public TraversalResult? TraversalResult { get; }
        public GatheringResult? GatheringResult { get; }

        /// <summary>
        /// この撃破で同時出撃枠が拡張された場合、拡張後の枠数（→ 「古代エルフ通信技術の復元」）。
        /// 拡張が起きなかった撃破・撤退・調査・採取ではnull。ボス討伐（DungeonResult）でのみ設定されうる。
        /// </summary>
        public int? SquadSlotsExpandedTo { get; } = null;

        /// <summary>
        /// この撃破で新たに開放されたフィールド（2026年9月新設、→ DungeonExpeditionSystem.
        /// ApplyFieldProgression）。開放が起きなかった撃破・撤退・調査・採取ではnull。
        /// ボス討伐（DungeonResult）でのみ設定されうる。
        /// </summary>
        public DungeonField? FieldNewlyUnlocked { get; } = null;

        public DungeonMissionResolution(Party party, FloorBoss boss, DungeonField field, double intelRateBefore, ScoutingResult scoutingResult)
        {
            Party = party;
            Boss = boss;
            Field = field;
            MissionType = DungeonMissionType.Scouting;
            IntelRateBefore = intelRateBefore;
            ScoutingResult = scoutingResult;
        }

        public DungeonMissionResolution(
            Party party, FloorBoss boss, DungeonField field, double intelRateBefore, DungeonResult dungeonResult,
            int? squadSlotsExpandedTo = null, DungeonField? fieldNewlyUnlocked = null)
        {
            Party = party;
            Boss = boss;
            Field = field;
            MissionType = DungeonMissionType.BossAssault;
            IntelRateBefore = intelRateBefore;
            DungeonResult = dungeonResult;
            SquadSlotsExpandedTo = squadSlotsExpandedTo;
            FieldNewlyUnlocked = fieldNewlyUnlocked;
        }

        public DungeonMissionResolution(Party party, FloorBoss boss, DungeonField field, double intelRateBefore, TraversalResult traversalResult)
        {
            Party = party;
            Boss = boss;
            Field = field;
            MissionType = DungeonMissionType.Scouting;
            IntelRateBefore = intelRateBefore;
            TraversalResult = traversalResult;
        }

        /// <summary>採取（Gathering）用。ボス・解析率は関係しないため受け取らない。</summary>
        public DungeonMissionResolution(Party party, DungeonField field, GatheringResult gatheringResult)
        {
            Party = party;
            Boss = null;
            Field = field;
            MissionType = DungeonMissionType.Gathering;
            IntelRateBefore = 0;
            GatheringResult = gatheringResult;
        }
    }
}
