namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 相性（Compatibility）関連のバランス値。仕様書 03 §5.3.1 参照。
    /// 値は docs/04_バランス表/compatibility_advisor.csv から読み込む（→ 03 §10.1、項目58）。
    /// MinValue/MaxValueは相性値のクランプ上下限（構造値）のためCSV化せずコードに残す。
    /// </summary>
    public static class CompatibilityBalance
    {
        private const string FileName = "compatibility_advisor.csv";

        public const int MinValue = 0;
        public const int MaxValue = 100;

        /// <summary>未登録のペアが持つ初期値（中立）。</summary>
        public static readonly int InitialValue = BalanceData.GetInt(FileName, "InitialValue");

        /// <summary>この値未満で「険悪」と判定する（→ 03 §5.1の満足度ペナルティに接続）。</summary>
        public static readonly int HostileThreshold = BalanceData.GetInt(FileName, "HostileThreshold");

        /// <summary>同パーティでクエスト達成（完全勝利・辛勝）した時の上昇量。→ BAL: 相性</summary>
        public static readonly int AchievementGain = BalanceData.GetInt(FileName, "AchievementGain");

        /// <summary>同パーティで苦戦敗退・戦線崩壊した時の下降量。→ BAL: 相性</summary>
        public static readonly int FailureLoss = BalanceData.GetInt(FileName, "FailureLoss");

        /// <summary>
        /// 同パーティのメンバーが戦死した時、居合わせた生存者同士の下降量（FailureLossより大きい）。
        /// → BAL: 相性
        /// </summary>
        public static readonly int DeathWitnessLoss = BalanceData.GetInt(FileName, "DeathWitnessLoss");

        /// <summary>
        /// 戦死の現場に居合わせた生存者に「トラウマ」特性が付与される確率（%）。
        /// → BAL: 特性/トラウマ付与率
        /// </summary>
        public static readonly int TraumaGrantChancePercent = BalanceData.GetInt(FileName, "TraumaGrantChancePercent");
    }
}
