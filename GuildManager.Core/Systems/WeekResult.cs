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

        /// <summary>今週クリアしたか（深淵100Fのボス撃破、→ 03 §8.2・§0.59）。旧名 FinalQuestNewlyUnlocked。</summary>
        public bool GameCleared { get; set; }

        /// <summary>
        /// 敗北（破産）が今週新たに確定したか。指示書の8停止条件には
        /// 明示されていなかったが、放置すると敗北後も自動スキップが無意味に
        /// 進み続けてしまうため追加した（→ クラス doc コメント参照）。
        /// </summary>
        public bool DefeatOccurred { get; set; }

        /// <summary>魂魄融和の子が今週誕生したか（→ SoulFusionSystem、03 §5.4・§0.58）。新しい仲間を見てもらうため自動スキップを止める。</summary>
        public bool SoulFusionBirthOccurred { get; set; }

        /// <summary>出撃した部隊に重傷者が出たか（§0.63。方針の自動出撃を続けたまま自動スキップしても、ここで止める）。</summary>
        public bool SevereInjuryOccurred { get; set; }

        /// <summary>階層ボスを倒したか（§0.63。進み具合を見てもらうため止める）。</summary>
        public bool BossDefeated { get; set; }

        /// <summary>季節のはじめに依頼が届いたか（§0.64。受けるかどうかを決めてもらうため止める）。</summary>
        public bool CommissionsOffered { get; set; }

        /// <summary>迷宮の異変が予告されたか（§0.64。来週からどこへ行くかを考えてもらうため止める）。</summary>
        public bool AnomalyAnnounced { get; set; }

        /// <summary>受けた依頼の期限が近づいたか（§0.64、→ CommissionBalance.DeadlineWarningWeeks）。</summary>
        public bool CommissionDeadlineNear { get; set; }

        /// <summary>階層ボスの撃破で新しいフィールドが開いたか（§0.70。方針の場所を考えてもらうため、月の途中でも止める）。</summary>
        public bool FieldUnlocked { get; set; }

        /// <summary>
        /// 月を1ターンとして進めているとき（→ AutoSkipService.AdvanceMonth、§0.70）、月の途中でも止めるか。
        /// 本当に決めることがあるときだけ止める：強制除籍・新しいフィールドの開放・娘の誕生・採用試験・依頼の期限・
        /// 契約交渉（2週以内に応えないと退団）・依頼の到着（季節の切れ目＝月の切れ目に来る）・クリア・敗北。
        /// 重傷とボス撃破では止めない（方針の自動出撃が静養で待つ・月報で見せる）。施設の完成でも止めない。
        /// 迷宮の異変の予告は月の最後の週の前に来るが、異変は次の月から効くので止めずに月報で知らせる。
        /// </summary>
        public bool ShouldStopMonth =>
            DeathOrPermanentInjuryOccurred || FieldUnlocked || SoulFusionBirthOccurred ||
            RecruitmentTrialOccurred || CommissionDeadlineNear || SatisfactionWarningOccurred ||
            CommissionsOffered ||
            GameCleared || DefeatOccurred;

        public bool ShouldStopAutoSkip =>
            CommissionsOffered || AnomalyAnnounced || CommissionDeadlineNear ||
            RecruitmentTrialOccurred || SatisfactionWarningOccurred ||
            FacilityConstructionCompleted || SoulFusionBirthOccurred ||
            SevereInjuryOccurred || BossDefeated ||
            DeathOrPermanentInjuryOccurred ||
            GameCleared ||
            DefeatOccurred;
    }
}
