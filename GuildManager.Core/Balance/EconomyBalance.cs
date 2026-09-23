namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 経済関連のうち、SatisfactionBalance（賃金妥当性判定）
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

        /// <summary>
        /// 退職金を払いきれなかった場合のマスターの機嫌の低下量（→ AgingSystem.Retire）。
        /// 旧・名声低下量（SeveranceShortfallReputationPenalty）の後継（2026年9月、名声の廃止に伴う）。
        /// </summary>
        public static readonly int SeveranceShortfallMoodPenalty = BalanceData.GetInt(FileName, "SeveranceShortfallMoodPenalty");

        /// <summary>
        /// アルベールの市販薬・内職売上の入金間隔（週、→ EconomySystem.ProcessWeeklySideJobIncome）。
        /// 旧・月次助成金の支給間隔（SubsidyBalance.WeeksPerMonth）の後継。
        /// </summary>
        public static readonly int SideJobIntervalWeeks = BalanceData.GetInt(FileName, "SideJobIntervalWeeks");

        /// <summary>
        /// 内職売上の基本額（G/回）。実入金＝基本額×マスターの機嫌の売上倍率（→ MasterMoodSystem.GetSideJobMultiplier）。
        /// 旧・格付けGの月次助成金を流用した値（格付けの廃止に伴い1本化、2026年9月）。
        /// </summary>
        public static readonly int SideJobBaseAmount = BalanceData.GetInt(FileName, "SideJobBaseAmount");

        /// <summary>
        /// 破産：所持金マイナスがこの週数連続で解消されないと敗北（→ 03 §8.3、DefeatSystem）。
        /// 脅威度システムの撤去（2026年9月）に伴い security.csv / SecurityBalance を廃止したため、
        /// 脅威度とは無関係なこの値（唯一の敗北条件）をここへ移設した。
        /// </summary>
        public static readonly int BankruptcyConsecutiveWeeksThreshold = BalanceData.GetInt(FileName, "BankruptcyConsecutiveWeeksThreshold");
    }
}
