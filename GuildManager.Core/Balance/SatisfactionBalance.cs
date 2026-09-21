namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 満足度・契約交渉関連のバランス値。仕様書 03 §5.1・§5.2 参照。
    /// 値は docs/04_バランス表/satisfaction.csv（大半）・economy.csv
    /// （AppropriateWageCoefficientのみ、賃金妥当性判定の基準として経済ファイル側にある）
    /// から読み込む（→ 03 §10.1、項目58）。Min/Maxは満足度のクランプ上下限（構造値）の
    /// ためCSV化せずコードに残す。
    /// </summary>
    public static class SatisfactionBalance
    {
        private const string FileName = "satisfaction.csv";
        private const string EconomyFileName = "economy.csv";

        public const int Min = 0;
        public const int Max = 100;

        // ---- 出場機会（§5.1）。対象は全盛期（23〜26歳、→ 03 §3.0 の年齢帯定義）。 ----
        public static readonly int NoDeploymentWeeksThreshold = BalanceData.GetInt(FileName, "NoDeploymentWeeksThreshold");
        public static readonly int NoDeploymentPenalty = BalanceData.GetInt(FileName, "NoDeploymentPenalty");

        /// <summary>
        /// 出場機会ペナルティの対象年齢の下限・上限。年齢帯の3区分化（→ 03 §0.11）に伴い、
        /// 旧・全盛期の22〜27歳から新・全盛期の23〜26歳へ整合させた（→ 03 §0.12）。
        /// 22歳（成長期）は育成猶予として対象外になる。
        /// </summary>
        public static readonly int OpportunityPenaltyMinAge = BalanceData.GetInt(FileName, "OpportunityPenaltyMinAge");
        public static readonly int OpportunityPenaltyMaxAge = BalanceData.GetInt(FileName, "OpportunityPenaltyMaxAge");

        // ---- 賃金妥当性（§5.1）。→ BAL: 満足度/賃金妥当性 ----
        public static readonly double WageAdequacyRatio = BalanceData.GetDouble(FileName, "WageAdequacyRatio");
        public static readonly int UnderpaidPenalty = BalanceData.GetInt(FileName, "UnderpaidPenalty");

        /// <summary>
        /// 適正週給の算出係数（総合PA×この値）。
        /// TODO(→ 03 §8 勝敗判定条件・格付け): 本来は「実力ランク」から適正週給を
        /// 算出すべきだが、ギルド格付け・冒険者ランクシステムが未実装のため、
        /// 総合PAを実力の代理指標として使う（→ BAL: 経済/週給基準）。
        /// </summary>
        public static readonly double AppropriateWageCoefficient = BalanceData.GetDouble(EconomyFileName, "AppropriateWageCoefficient");

        // ---- 大迷宮の任務成果による士気の変動（§5.1「勝利・功績」、→ SatisfactionSystem.ApplyExpeditionSatisfaction） ----
        // 旧通常クエスト（Bランク以上の達成で+10＝VictoryBonus）の撤去後、大迷宮の任務成果へ再配線した。

        /// <summary>階層ボス撃破時（生存隊員全員）。</summary>
        public static readonly int ExpeditionBossVictory = BalanceData.GetInt(FileName, "Satisfaction_BossVictory");

        /// <summary>ボス戦で撤退・敗退した時（生存隊員全員、負の値）。</summary>
        public static readonly int ExpeditionBossDefeat = BalanceData.GetInt(FileName, "Satisfaction_BossDefeat");

        /// <summary>迷宮調査：護衛「余裕」。</summary>
        public static readonly int ExpeditionSurveyAbundant = BalanceData.GetInt(FileName, "Satisfaction_SurveyAbundant");

        /// <summary>迷宮調査：護衛「十分」（「充足」は変動なし）。</summary>
        public static readonly int ExpeditionSurveySufficient = BalanceData.GetInt(FileName, "Satisfaction_SurveySufficient");

        /// <summary>迷宮調査：護衛「不足」による潰走（負の値）。</summary>
        public static readonly int ExpeditionSurveyDeficient = BalanceData.GetInt(FileName, "Satisfaction_SurveyDeficient");

        /// <summary>道中進軍で1階層以上進み生還した週。</summary>
        public static readonly int ExpeditionTraversalSuccess = BalanceData.GetInt(FileName, "Satisfaction_TraversalSuccess");

        /// <summary>採取で素材を持ち帰れた時。</summary>
        public static readonly int ExpeditionGatheringSuccess = BalanceData.GetInt(FileName, "Satisfaction_GatheringSuccess");

        /// <summary>仲間ロストの余波（§5.1）。→ 03 §4.3の致死判定（戦死）に接続済み。</summary>
        public static readonly int PartyLossPenalty = BalanceData.GetInt(FileName, "PartyLossPenalty");

        /// <summary>
        /// 人間関係：相性「険悪」（→ 03 §5.3.1、CompatibilityBalance.HostileThreshold未満）の
        /// ペアと同パーティで出撃した週、毎週この分だけ減点する（v1.5からの保留を解消）。
        /// 1人が複数の険悪ペアに同時に該当する場合は、その件数分だけ加算される。
        /// </summary>
        public static readonly int HostilePairPenalty = BalanceData.GetInt(FileName, "HostilePairPenalty");

        // ---- 契約交渉（§5.2） ----
        public static readonly int NegotiationThreshold = BalanceData.GetInt(FileName, "NegotiationThreshold");
        public static readonly int NegotiationGraceWeeks = BalanceData.GetInt(FileName, "NegotiationGraceWeeks");

        /// <summary>昇給倍率の許容レンジ（週給1.5〜2.0倍提示、§5.2）。</summary>
        public static readonly double MinRaiseMultiplier = BalanceData.GetDouble(FileName, "MinRaiseMultiplier");
        public static readonly double MaxRaiseMultiplier = BalanceData.GetDouble(FileName, "MaxRaiseMultiplier");

        /// <summary>一時金（ボーナス）＝週給のこの倍数。→ BAL: 満足度/契約交渉。</summary>
        public static readonly int BonusWeeksEquivalent = BalanceData.GetInt(FileName, "BonusWeeksEquivalent");
    }
}
