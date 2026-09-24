namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 新春採用試験（RecruitmentSystem）関連のバランス値。仕様書 03 §2.4・§7.3 参照。
    /// 値は docs/04_バランス表/recruitment.csv から読み込む（→ 03 §10.1、項目58）。
    ///
    /// 事前調査メモ（項目58）：これらの数値はRecruitmentSystem.csに直書きされていた
    /// （05技術メモ§3の方針違反）。今回新設したこのクラスへ集約した。氏名生成そのものの
    /// パラメータ（文化圏比率・性別比率・重複回避リトライ上限）は既存のNameGeneratorBalanceに
    /// 残す（公開シグネチャを変えないため）。
    /// </summary>
    public static class RecruitmentBalance
    {
        private const string FileName = "recruitment.csv";

        /// <summary>新春採用試験の応募者数。→ BAL: 採用/応募者数</summary>
        public static readonly int CandidateCount = BalanceData.GetInt(FileName, "CandidateCount");

        /// <summary>応募者の年齢レンジ。→ BAL: 採用/年齢レンジ</summary>
        public static readonly int MinCandidateAge = BalanceData.GetInt(FileName, "MinCandidateAge");
        public static readonly int MaxCandidateAge = BalanceData.GetInt(FileName, "MaxCandidateAge");

        /// <summary>最年少(15歳)のPA補正（18歳で+0へ線形補間）。→ BAL: 採用/年齢別PA補正</summary>
        public static readonly int YoungestAgePaBonus = BalanceData.GetInt(FileName, "YoungestAgePaBonus");

        /// <summary>通常応募者のPA生成レンジ。→ BAL: 採用</summary>
        public static readonly int MinGeneratedPa = BalanceData.GetInt(FileName, "MinGeneratedPa");
        public static readonly int MaxGeneratedPa = BalanceData.GetInt(FileName, "MaxGeneratedPa");

        /// <summary>
        /// 有望新人（総合PA75以上）の基礎出現率（→ 03 §7.3）。スカウト顧問のボーナス
        /// （AdvisorBalance.ScoutMasterBonusCoefficient経由）が加算される。→ BAL: 採用/有望新人
        /// </summary>
        public static readonly double HighPotentialBaseChance = BalanceData.GetDouble(FileName, "HighPotentialBaseChance");

        /// <summary>有望新人のPA生成レンジ。→ BAL: 採用/有望新人</summary>
        public static readonly int HighPotentialMinPa = BalanceData.GetInt(FileName, "HighPotentialMinPa");
        public static readonly int HighPotentialMaxPa = BalanceData.GetInt(FileName, "HighPotentialMaxPa");

        /// <summary>
        /// 実効値/PAの比率レンジ（15歳ほどPAから遠く＝伸びしろ最大、18歳ほどPAに近い＝即戦力。
        /// → 03 §2.4）。→ BAL: 採用
        /// </summary>
        public static readonly double YoungestGrowthRatio = BalanceData.GetDouble(FileName, "YoungestGrowthRatio");
        public static readonly double OldestGrowthRatio = BalanceData.GetDouble(FileName, "OldestGrowthRatio");

        /// <summary>
        /// 先天特性（豪胆・注意深い・容姿秀麗）各々の付与確率（%）。→ 03 §5.3.2。
        /// → BAL: 採用/先天特性付与率
        /// </summary>
        public static readonly int InnateTraitChancePercent = BalanceData.GetInt(FileName, "InnateTraitChancePercent");

        /// <summary>
        /// 第1週の新春ドラフト（→ RecruitmentSystem.StartInitialDraft、03 §2.4、2026年9月）で提示する候補者数。
        /// 保証職（魔導士・学者・騎士・盗賊）の4名を下回る値でも保証職は全員出す（4名が下限）。
        /// 5名目以降は全職業から通常抽選。旧 TutorialCandidateCount（3名・有料）の置き換え。
        /// </summary>
        public static readonly int DraftCandidateCount = BalanceData.GetInt(FileName, "DraftCandidateCount");

        /// <summary>新春ドラフトで採用する人数（初期3名＋この人数＝開始時の在籍数）。</summary>
        public static readonly int DraftHireCount = BalanceData.GetInt(FileName, "DraftHireCount");
    }
}
