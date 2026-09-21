namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 1週分の週次決算処理で発生した出来事のフラグ。仕様書 03 §1.3（自動スキップ）参照。
    /// 自動スキップ（→ AutoSkipService）が停止すべきかどうかの判定に使う。
    ///
    /// 事前調査メモ（項目56）：指示書のサンプルにはブール値フラグのみが定義されていたが、
    /// 「敗北（破産）」が停止条件8種のいずれにも明示的に含まれていなかった。
    /// 破産（4週連続の所持金マイナス）は他のどのフラグとも
    /// 連動しないため、敗北発生時に自動スキップが止まらず無意味に週を進め続けてしまう
    /// （DefeatSystem自体は敗北後何もしなくなるため実害は薄いが、プレイヤーが
    /// ゲームオーバーに気付けないまま週が進み続けるのは望ましくない）。そのため
    /// DefeatOccurredを追加し、ShouldStopAutoSkipに含めた。
    /// </summary>
    public class WeekResult
    {
        /// <summary>この結果が対応する週番号（決算処理前の週番号）。</summary>
        public int Week { get; set; }

        public bool RecruitmentTrialOccurred { get; set; }
        public bool SatisfactionWarningOccurred { get; set; }
        public bool FacilityConstructionCompleted { get; set; }
        public bool DeathOrPermanentInjuryOccurred { get; set; }

        public bool FinalQuestNewlyUnlocked { get; set; }

        /// <summary>
        /// 敗北（破産）が今週新たに確定したか。指示書の8停止条件には
        /// 明示されていなかったが、放置すると敗北後も自動スキップが無意味に
        /// 進み続けてしまうため追加した（→ クラス doc コメント参照）。
        /// </summary>
        public bool DefeatOccurred { get; set; }

        /// <summary>
        /// 大迷宮へ潜行中の部隊が、今週未撃破ボスの扉前に到達したか（→ ExpeditionStatus.AwaitingBossDecision、
        /// 2026年9月新設）。「挑む／撤退」の判断をプレイヤーに委ねるため、自動スキップを止める。
        /// </summary>
        public bool BossDoorReached { get; set; }

        public bool ShouldStopAutoSkip =>
            RecruitmentTrialOccurred || SatisfactionWarningOccurred ||
            FacilityConstructionCompleted ||
            DeathOrPermanentInjuryOccurred ||
            FinalQuestNewlyUnlocked ||
            DefeatOccurred || BossDoorReached;
    }
}
