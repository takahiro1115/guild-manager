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
    /// 3つのうち、いずれか1つだけが入る。
    /// </summary>
    public class DungeonMissionResolution
    {
        public Party Party { get; }
        public FloorBoss Boss { get; }
        public DungeonMissionType MissionType { get; }

        /// <summary>解決前の解析率（週報で「○% → ○%」と表示するため）。道中進軍では変化しない。</summary>
        public double IntelRateBefore { get; }

        public ScoutingResult? ScoutingResult { get; }
        public DungeonResult? DungeonResult { get; }
        public TraversalResult? TraversalResult { get; }

        public DungeonMissionResolution(Party party, FloorBoss boss, double intelRateBefore, ScoutingResult scoutingResult)
        {
            Party = party;
            Boss = boss;
            MissionType = DungeonMissionType.Scouting;
            IntelRateBefore = intelRateBefore;
            ScoutingResult = scoutingResult;
        }

        public DungeonMissionResolution(Party party, FloorBoss boss, double intelRateBefore, DungeonResult dungeonResult)
        {
            Party = party;
            Boss = boss;
            MissionType = DungeonMissionType.BossAssault;
            IntelRateBefore = intelRateBefore;
            DungeonResult = dungeonResult;
        }

        public DungeonMissionResolution(Party party, FloorBoss boss, double intelRateBefore, TraversalResult traversalResult)
        {
            Party = party;
            Boss = boss;
            MissionType = DungeonMissionType.Scouting;
            IntelRateBefore = intelRateBefore;
            TraversalResult = traversalResult;
        }
    }
}
