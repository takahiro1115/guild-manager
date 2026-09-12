namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 相性（Compatibility）関連の暫定バランス値。仕様書 03 §5.3.1 参照。
    /// 現状はすべて仮値の定数（→ BAL: 相性）。
    /// </summary>
    public static class CompatibilityBalance
    {
        public const int MinValue = 0;
        public const int MaxValue = 100;

        /// <summary>未登録のペアが持つ初期値（中立）。</summary>
        public const int InitialValue = 50;

        /// <summary>この値未満で「険悪」と判定する（→ 03 §5.1の満足度ペナルティに接続）。</summary>
        public const int HostileThreshold = 30;

        /// <summary>同パーティでクエスト達成（完全勝利・辛勝）した時の上昇量。→ BAL: 相性</summary>
        public const int AchievementGain = 3;

        /// <summary>同パーティで苦戦敗退・戦線崩壊した時の下降量。→ BAL: 相性</summary>
        public const int FailureLoss = 2;

        /// <summary>
        /// 同パーティのメンバーが戦死した時、居合わせた生存者同士の下降量（FailureLossより大きい）。
        /// → BAL: 相性
        /// </summary>
        public const int DeathWitnessLoss = 15;

        /// <summary>
        /// 戦死の現場に居合わせた生存者に「トラウマ」特性が付与される確率（%）。
        /// → BAL: 特性/トラウマ付与率
        /// </summary>
        public const int TraumaGrantChancePercent = 30;
    }
}
