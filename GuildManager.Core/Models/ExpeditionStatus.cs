namespace GuildManager.Core.Models
{
    /// <summary>
    /// 大迷宮へ潜行中の部隊（→ ActiveDungeonMission）の遠征状態（2026年9月新設、
    /// 「毎回1Fリセット・複数週潜行型」への刷新）。
    ///
    /// 道中調査（Scouting）で出撃した部隊は毎回1階層から潜り始め、週次決算のたびに進軍する（Advancing）。
    /// 2026年10月・§0.69：未撃破ボスの扉前に着いたら、その週の決算のうちに部隊の構え（→ SavedParty.Stance）で
    /// 挑むか撤退するかを決め、挑むならそのまま決戦する（扉前でプレイヤーの指令を待つ状態〈旧 AwaitingBossDecision〉は撤去）。
    /// </summary>
    public enum ExpeditionStatus
    {
        /// <summary>道中進軍中。次週の決算でさらに深度を開拓する。</summary>
        Advancing,

        /// <summary>ボスに挑む（→ Systems.DungeonResolver）。扉前に着いた週の決算でそのまま決戦する。</summary>
        EngagingBoss,

        /// <summary>撤退中。ギルドへ帰還する（即時撤退・呼び戻しはこの状態のまま帰還を済ませる）。</summary>
        Retreating,
    }
}
