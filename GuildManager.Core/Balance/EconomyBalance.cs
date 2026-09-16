namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 経済関連のうち、SubsidyBalance（月次助成金）・SatisfactionBalance（賃金妥当性判定）
    /// 以外の値をまとめたバランス値。仕様書 03 §7・§8.1・§2.4 参照。
    /// 値は docs/04_バランス表/economy.csv から読み込む（→ 03 §10.1、項目58）。
    ///
    /// 事前調査メモ（項目58）：初期資金は GameState.Gold の初期化子に、週給係数・契約金係数は
    /// RecruitmentSystem.cs に、退職金係数は AgingSystem.cs にそれぞれ直書きされていた
    /// （05技術メモ§3の方針違反）。今回新設したこのクラスへ集約した。
    /// </summary>
    public static class EconomyBalance
    {
        private const string FileName = "economy.csv";

        /// <summary>開始時の所持金（→ GameState.Gold の初期値）。</summary>
        public static readonly int InitialGold = BalanceData.GetInt(FileName, "InitialGold");

        /// <summary>週給＝総合PA×この係数（採用時の初期週給算出。→ RecruitmentSystem）。</summary>
        public static readonly double WeeklyWageCoefficient = BalanceData.GetDouble(FileName, "WeeklyWageCoefficient");

        /// <summary>契約金＝総合PA×年齢×この係数（→ RecruitmentSystem）。</summary>
        public static readonly double SigningBonusCoefficient = BalanceData.GetDouble(FileName, "SigningBonusCoefficient");

        /// <summary>引退時の退職金＝週給×この週数（→ AgingSystem.Retire、仕様書 03 §7）。</summary>
        public static readonly int SeveranceWeeks = BalanceData.GetInt(FileName, "SeveranceWeeks");

        /// <summary>
        /// 退職金の功績加算係数（→ Adventurer.TotalContributionScore × この係数）。
        /// 「危険な仕事をさせた分だけ、十分な退職金を持たせて安全に自立させる」という
        /// ギルドマスターの方針の実装（→ 01_コンセプト.md・03 §3.7）。
        /// </summary>
        public static readonly double SeveranceContributionCoefficient = BalanceData.GetDouble(FileName, "SeveranceContributionCoefficient");

        /// <summary>退職金を払いきれなかった場合の名声低下量（→ AgingSystem.Retire）。</summary>
        public static readonly int SeveranceShortfallReputationPenalty = BalanceData.GetInt(FileName, "SeveranceShortfallReputationPenalty");
    }
}
