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

        // ---- 大迷宮の任務達成による相性上昇（2026年9月：旧クエストの AchievementGain/FailureLoss を置き換え） ----

        /// <summary>同じ部隊で階層ボスを撃破して生還した時の上昇量（→ CompatibilitySystem.ApplyExpeditionOutcome）。</summary>
        public static readonly int ExpeditionGainBossVictory = BalanceData.GetInt(FileName, "ExpeditionGain_BossVictory");

        /// <summary>同じ部隊で道中潜行を1階層以上進軍した時の上昇量。</summary>
        public static readonly int ExpeditionGainTraversal = BalanceData.GetInt(FileName, "ExpeditionGain_Traversal");

        /// <summary>同じ部隊で迷宮調査から帰還した時（護衛「不足」＝潰走以外）の上昇量。</summary>
        public static readonly int ExpeditionGainSurvey = BalanceData.GetInt(FileName, "ExpeditionGain_Survey");

        /// <summary>同じ部隊で探索採取から素材を1個以上持ち帰った時の上昇量。</summary>
        public static readonly int ExpeditionGainGathering = BalanceData.GetInt(FileName, "ExpeditionGain_Gathering");

        /// <summary>
        /// 同パーティのメンバーが戦死した時、居合わせた生存者同士の下降量（任務達成の上昇量より大きい）。
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
