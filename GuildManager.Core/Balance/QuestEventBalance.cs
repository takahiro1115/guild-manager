namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 探索・護衛クエストのランダムイベント（→ 03 §4.2.3、項目65で新設）のバランス値。
    /// 値は docs/04_バランス表/quest_events.csv から読み込む（→ 03 §10.1）。
    ///
    /// 3イベントとも「低確率で発生し、発生した回のみ能力値・特性による内部判定を行う」形。
    /// プレイヤーに選択肢は提示せず、結果だけが記録される（→ QuestEventResults）。
    ///  - 強敵との遭遇（探索・護衛）：押し切り／退避／苦戦の3値判定
    ///  - 宝物庫の発見（探索のみ）：大成功／成功／失敗の3段階（罠は失敗時のみ）
    ///  - 深追い（探索・護衛）：大成功／成功／失敗の3段階（報酬とHP消費のトレードオフ）
    ///
    /// 討伐（Subjugation）はもともと毎回が戦闘解決そのものなので、いずれの
    /// イベントの対象にもしない（→ QuestResolver）。
    /// </summary>
    public static class QuestEventBalance
    {
        private const string FileName = "quest_events.csv";

        // ---- ①強敵との遭遇（探索・護衛のみ） ----

        public static readonly int StrongEnemyOccurrenceChancePercent = BalanceData.GetInt(FileName, "StrongEnemy_OccurrenceChancePercent");

        /// <summary>遭遇要求値＝クエストDifficulty×この係数。押し切り／退避の両スコアが比較される。</summary>
        public static readonly double StrongEnemyRequirementCoefficient = BalanceData.GetDouble(FileName, "StrongEnemy_RequirementCoefficient");

        /// <summary>押し切りスコアにおける隊長LDRの寄与係数。</summary>
        public static readonly double StrongEnemyLeaderLdrCoefficient = BalanceData.GetDouble(FileName, "StrongEnemy_LeaderLdrCoefficient");

        // 特性による補正はいずれも「パーティ内に保有者が1人でもいれば1回だけ」適用する
        // （人数分の重ね掛けはしない）。
        public static readonly double StrongEnemyBraveBonus = BalanceData.GetDouble(FileName, "StrongEnemy_BraveBonus");
        public static readonly double StrongEnemyAttentiveBonus = BalanceData.GetDouble(FileName, "StrongEnemy_AttentiveBonus");
        public static readonly double StrongEnemyTraumaPenalty = BalanceData.GetDouble(FileName, "StrongEnemy_TraumaPenalty");
        public static readonly double StrongEnemyOldWoundPenalty = BalanceData.GetDouble(FileName, "StrongEnemy_OldWoundPenalty");

        /// <summary>押し切った場合の追加報酬（退避・苦戦では無し）。</summary>
        public static readonly int StrongEnemyPushThroughRewardGold = BalanceData.GetInt(FileName, "StrongEnemy_PushThroughRewardGold");

        // ---- ②宝物庫の発見（探索のみ） ----

        public static readonly int TreasureVaultOccurrenceChancePercent = BalanceData.GetInt(FileName, "TreasureVault_OccurrenceChancePercent");
        public static readonly double TreasureVaultRequirementCoefficient = BalanceData.GetDouble(FileName, "TreasureVault_RequirementCoefficient");
        public static readonly double TreasureVaultAttentiveBonus = BalanceData.GetDouble(FileName, "TreasureVault_AttentiveBonus");

        // 討伐の4区分・探索/護衛の3区分とは別の、このイベント専用の閾値（→ 03 §4.2.3）。
        public static readonly double TreasureVaultRatioThresholdGreatSuccess = BalanceData.GetDouble(FileName, "TreasureVault_RatioThreshold_GreatSuccess");
        public static readonly double TreasureVaultRatioThresholdSuccess = BalanceData.GetDouble(FileName, "TreasureVault_RatioThreshold_Success");

        public static readonly int TreasureVaultGreatSuccessRewardGold = BalanceData.GetInt(FileName, "TreasureVault_GreatSuccessRewardGold");
        public static readonly int TreasureVaultSuccessRewardGold = BalanceData.GetInt(FileName, "TreasureVault_SuccessRewardGold");

        /// <summary>失敗時のみ抽選される罠の作動確率と、作動時の追加HP消費率レンジ。</summary>
        public static readonly int TreasureVaultTrapChancePercent = BalanceData.GetInt(FileName, "TreasureVault_TrapChancePercent");
        public static readonly int TreasureVaultTrapHpLossPctMin = BalanceData.GetInt(FileName, "TreasureVault_TrapHpLossPctMin");
        public static readonly int TreasureVaultTrapHpLossPctMax = BalanceData.GetInt(FileName, "TreasureVault_TrapHpLossPctMax");

        // ---- ③深追い（探索・護衛） ----

        public static readonly int PushingOnOccurrenceChancePercent = BalanceData.GetInt(FileName, "PushingOn_OccurrenceChancePercent");
        public static readonly double PushingOnRequirementCoefficient = BalanceData.GetDouble(FileName, "PushingOn_RequirementCoefficient");

        public static readonly double PushingOnRatioThresholdGreatSuccess = BalanceData.GetDouble(FileName, "PushingOn_RatioThreshold_GreatSuccess");
        public static readonly double PushingOnRatioThresholdSuccess = BalanceData.GetDouble(FileName, "PushingOn_RatioThreshold_Success");

        public static readonly int PushingOnGreatSuccessRewardGold = BalanceData.GetInt(FileName, "PushingOn_GreatSuccessRewardGold");
        public static readonly int PushingOnGreatSuccessHpLossPctMin = BalanceData.GetInt(FileName, "PushingOn_GreatSuccessHpLossPctMin");
        public static readonly int PushingOnGreatSuccessHpLossPctMax = BalanceData.GetInt(FileName, "PushingOn_GreatSuccessHpLossPctMax");

        public static readonly int PushingOnSuccessRewardGold = BalanceData.GetInt(FileName, "PushingOn_SuccessRewardGold");

        public static readonly int PushingOnFailureHpLossPctMin = BalanceData.GetInt(FileName, "PushingOn_FailureHpLossPctMin");
        public static readonly int PushingOnFailureHpLossPctMax = BalanceData.GetInt(FileName, "PushingOn_FailureHpLossPctMax");
    }
}
