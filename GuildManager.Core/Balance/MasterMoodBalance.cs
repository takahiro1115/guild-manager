namespace GuildManager.Core.Balance
{
    /// <summary>
    /// マスターの機嫌（→ Systems.MasterMoodSystem、03 §8.1）のバランス値。
    /// 値は docs/04_バランス表/master_mood.csv から読み込む（→ 03 §10.1）。
    /// 旧・名声（Reputation）とギルド格付け（guild_rank.csv / guild_rank_params.csv）の後継（2026年9月）。
    /// </summary>
    public static class MasterMoodBalance
    {
        private const string FileName = "master_mood.csv";

        /// <summary>機嫌の下限（構造値）。ここに達すると副官解雇＝敗北（→ DefeatSystem）。</summary>
        public const int Min = 0;

        /// <summary>機嫌の上限（構造値）。</summary>
        public const int Max = 100;

        /// <summary>機嫌の初期値（新規ゲーム・旧セーブの読み込み時）。</summary>
        public static readonly int InitialMood = BalanceData.GetInt(FileName, "InitialMood");

        /// <summary>階層ボス撃破1体ごとの上昇量。</summary>
        public static readonly int BossDefeatMoodGain = BalanceData.GetInt(FileName, "BossDefeatMoodGain");

        /// <summary>道中潜行で進軍した未踏破階層1階層ごとの上昇量。</summary>
        public static readonly int PioneerMoodPerFloor = BalanceData.GetInt(FileName, "PioneerMoodPerFloor");

        /// <summary>迷宮調査から護衛判定「余裕」「十分」で帰還した時の上昇量。</summary>
        public static readonly int SurveySuccessMoodGain = BalanceData.GetInt(FileName, "SurveySuccessMoodGain");

        /// <summary>迷宮調査で護衛判定「不足」（潰走）となった時の低下量（正の値で持つ）。</summary>
        public static readonly int SurveyRoutedMoodLoss = BalanceData.GetInt(FileName, "SurveyRoutedMoodLoss");

        /// <summary>探索採取で素材を1個以上獲得して帰還した時の上昇量。</summary>
        public static readonly int GatheringMoodGain = BalanceData.GetInt(FileName, "GatheringMoodGain");

        /// <summary>未鑑定遺物1個の鑑定完了ごとの上昇量（→ AppraisalSystem.Appraise、即時）。</summary>
        public static readonly int AppraisalMoodGain = BalanceData.GetInt(FileName, "AppraisalMoodGain");

        /// <summary>
        /// 強制除籍（不死薬による現場からの永久離脱）1名ごとの機嫌低下（アルベールの激怒、正の値で持つ。
        /// → MasterMoodSystem.ApplyForcedRetirementFury、2026年9月新設）。
        /// </summary>
        public static readonly int ForcedRetirementMoodLoss = BalanceData.GetInt(FileName, "ForcedRetirementMoodLoss");

        /// <summary>成果ゼロ週の退屈減衰量（正の値で持つ）。</summary>
        public static readonly int BoredomMoodDecay = BalanceData.GetInt(FileName, "BoredomMoodDecay");

        /// <summary>
        /// 待機お手伝い（→ MasterMoodSystem.ProcessIdleHelp、03 §8.1、2026年9月新設）：出撃せずギルドに残った
        /// 健康な冒険者1名ごとの週次収入（G）。
        /// </summary>
        public static readonly int IdleAdventurerHelpGold = BalanceData.GetInt(FileName, "IdleAdventurerHelp_Gold");

        /// <summary>待機お手伝い1名ごとの機嫌上昇（上限でクランプ）。</summary>
        public static readonly int IdleAdventurerHelpMood = BalanceData.GetInt(FileName, "IdleAdventurerHelp_Mood");

        public static readonly int TierThresholdCheerful = BalanceData.GetInt(FileName, "TierThreshold_Cheerful");
        public static readonly int TierThresholdNormal = BalanceData.GetInt(FileName, "TierThreshold_Normal");
        public static readonly int TierThresholdGrumpy = BalanceData.GetInt(FileName, "TierThreshold_Grumpy");

        public static readonly double SideJobMultiplierCheerful = BalanceData.GetDouble(FileName, "SideJobMultiplier_Cheerful");
        public static readonly double SideJobMultiplierNormal = BalanceData.GetDouble(FileName, "SideJobMultiplier_Normal");
        public static readonly double SideJobMultiplierGrumpy = BalanceData.GetDouble(FileName, "SideJobMultiplier_Grumpy");
        public static readonly double SideJobMultiplierCrisis = BalanceData.GetDouble(FileName, "SideJobMultiplier_Crisis");
    }
}
