namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 1回の遠征解決で発生したランダムイベント（→ 03 §4.2.3、項目65）の結果一式。
    /// WeekResolutionResult.Events として保持する。
    ///
    /// 発生しなかったイベントのプロパティはnullのまま（＝「発生有無」もこれで表現する）。
    /// 3イベントの発生ロールは互いに独立しているため、同じクエストで複数発生しうる。
    /// 週報への文章化は後続の指示書（④）で行う想定で、ここではデータとして保持するのみ。
    /// なお本データはその週の解決結果（一時情報）であり、SaveDataには保存しない。
    /// </summary>
    public class QuestEventResults
    {
        /// <summary>①強敵との遭遇（探索・護衛のみ）。発生しなければnull。</summary>
        public StrongEnemyEventResult? StrongEnemy { get; set; }

        /// <summary>②宝物庫の発見（探索のみ）。発生しなければnull。</summary>
        public TreasureVaultEventResult? TreasureVault { get; set; }

        /// <summary>③深追い（探索・護衛）。発生しなければnull。</summary>
        public PushingOnEventResult? PushingOn { get; set; }

        /// <summary>いずれかのイベントが発生したか。</summary>
        public bool AnyOccurred => StrongEnemy != null || TreasureVault != null || PushingOn != null;

        /// <summary>このクエストでイベント由来で追加された報酬の合計（週報用の内訳）。</summary>
        public int TotalBonusRewardGold =>
            (StrongEnemy?.BonusRewardGold ?? 0)
            + (TreasureVault?.BonusRewardGold ?? 0)
            + (PushingOn?.BonusRewardGold ?? 0);
    }

    /// <summary>①強敵との遭遇の判定結果。</summary>
    public class StrongEnemyEventResult
    {
        public StrongEnemyOutcome Outcome { get; set; }

        /// <summary>押し切りスコア（Σ(STR+VIT)＋隊長LDR×係数＋豪胆ボーナス）。</summary>
        public double PushThroughScore { get; set; }

        /// <summary>退避スコア（Σ(AGI+INT)＋注意深いボーナス−トラウマ/古傷ペナルティ）。</summary>
        public double EvadeScore { get; set; }

        /// <summary>遭遇要求値（クエストDifficulty×係数）。両スコアがこの値と比較される。</summary>
        public double Requirement { get; set; }

        /// <summary>押し切った場合の追加報酬。退避・苦戦では0。</summary>
        public int BonusRewardGold { get; set; }

        /// <summary>
        /// このイベントの結果として、討伐フロー（HP消費レンジ＋致死判定）を流用したか。
        /// 押し切り・苦戦ではtrue、退避ではfalse（通常の軽量HP消費のまま）。
        /// </summary>
        public bool UsedCombatDamage { get; set; }
    }

    /// <summary>②宝物庫の発見の判定結果。</summary>
    public class TreasureVaultEventResult
    {
        public QuestEventOutcome Outcome { get; set; }

        /// <summary>判定スコア（Σ(DEX+INT)＋注意深いボーナス）。</summary>
        public double Score { get; set; }

        public double Requirement { get; set; }
        public double Ratio { get; set; }

        /// <summary>大成功なら大、成功なら小、失敗なら0。</summary>
        public int BonusRewardGold { get; set; }

        /// <summary>失敗時のみ抽選され、作動するとわずかなHP追加消費が入る。</summary>
        public bool TrapTriggered { get; set; }
    }

    /// <summary>③深追いの判定結果。クエストの拘束期間は変更せず、報酬とHP消費だけで表現する。</summary>
    public class PushingOnEventResult
    {
        public QuestEventOutcome Outcome { get; set; }

        /// <summary>判定スコア（Σ(VIT+MND)）。</summary>
        public double Score { get; set; }

        public double Requirement { get; set; }
        public double Ratio { get; set; }

        /// <summary>大成功なら大きめ、成功なら小さめ、失敗なら0。</summary>
        public int BonusRewardGold { get; set; }

        /// <summary>追加HP消費が発生したか（大成功＝粘った代償／失敗＝それなりの消費）。</summary>
        public bool AppliedExtraHpLoss { get; set; }
    }
}
