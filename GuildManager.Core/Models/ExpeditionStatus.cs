namespace GuildManager.Core.Models
{
    /// <summary>
    /// 大迷宮へ潜行中の部隊（→ ActiveDungeonMission）の遠征状態（2026年9月新設、
    /// 「毎回1Fリセット・複数週潜行型」への刷新）。
    ///
    /// 道中調査（Scouting）で出撃した部隊は毎回1階層から潜り始め、週次決算のたびに
    /// Advancing → AwaitingBossDecision → EngagingBoss と遷移する。ボス階層に着いても
    /// 自動では突入せず、プレイヤーの指令（挑む／撤退）を待つ（→ DungeonExpeditionSystem）。
    /// </summary>
    public enum ExpeditionStatus
    {
        /// <summary>道中進軍中。次週の決算でさらに深度を開拓する。</summary>
        Advancing,

        /// <summary>
        /// 未撃破ボスの扉前に到達し、プレイヤーの判断待ち（進軍は一時停止）。
        /// 指令が出ないまま週を越した場合、部隊は扉前でボスの偵察（解析）を続ける。
        /// </summary>
        AwaitingBossDecision,

        /// <summary>ボス討伐を指令済み。次週の決算で決戦判定（→ Systems.DungeonResolver）を行う。</summary>
        EngagingBoss,

        /// <summary>撤退中。次週の決算でギルドへ帰還する（即時撤退はこの状態を経由しない）。</summary>
        Retreating,
    }
}
