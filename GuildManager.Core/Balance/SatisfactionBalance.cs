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

        // ---- 出場機会（§5.1）。対象年齢帯は22〜27歳（全盛期）固定（仕様書どおり）。 ----
        public static readonly int NoDeploymentWeeksThreshold = BalanceData.GetInt(FileName, "NoDeploymentWeeksThreshold");
        public static readonly int NoDeploymentPenalty = BalanceData.GetInt(FileName, "NoDeploymentPenalty");

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

        // ---- 勝利・功績（§5.1）。Bランク以上のクエスト達成が対象。 ----
        public static readonly int VictoryBonus = BalanceData.GetInt(FileName, "VictoryBonus");

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
