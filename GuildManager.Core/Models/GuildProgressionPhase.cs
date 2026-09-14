namespace GuildManager.Core.Models
{
    /// <summary>
    /// ギルド進行フェーズ（→ コアシステム刷新仕様「4. 進行管理」）。
    /// プレイ開始40〜60分（4〜5回目の出撃）で Expanded（第2部隊枠の開放）へ到達させることを
    /// 狙った、序盤専用のステートマシン。判定は GuildProgressionSystem.GetPhase が行う
    /// （GameStateには生の条件値だけを持たせ、フェーズ自体は導出値にしている）。
    /// </summary>
    public enum GuildProgressionPhase
    {
        /// <summary>
        /// Phase 1：初期状態。同時出撃枠1（1〜4名の分割は自由）、低危険度の採取・巡回が中心。
        /// </summary>
        Foundation,

        /// <summary>
        /// Phase 2：昇格試験が受注可能一覧に提示された状態（未突破）。
        /// 条件＝累計出撃回数と資金が基準に達した（→ ProgressionBalance）。
        /// </summary>
        ExamOffered,

        /// <summary>
        /// Phase 3：昇格試験クエストへ出撃中（決戦）。UI側はこのフェーズの間、
        /// ログをステップ再生モードで表示する想定（→ Quest.IsBoss）。
        /// </summary>
        ExamInProgress,

        /// <summary>
        /// Phase 4：昇格試験を突破し、ランクE・同時出撃枠2へ拡張された状態。
        /// </summary>
        Expanded,
    }
}
