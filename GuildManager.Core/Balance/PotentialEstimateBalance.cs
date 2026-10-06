namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 副官の見立て（目利き、2026年10月・§0.74、→ Systems.PotentialEstimateSystem）のバランス値。
    /// 値は docs/04_バランス表/potential_estimate.csv から読み込む。
    /// </summary>
    public static class PotentialEstimateBalance
    {
        private const string FileName = "potential_estimate.csv";

        /// <summary>見立てのずれの幅（目利き0・在籍0か月のとき ±この値まで）。</summary>
        public static readonly double BaseWidth = BalanceData.GetDouble(FileName, "BaseWidth");

        /// <summary>目利きの上限。</summary>
        public static readonly double MaxEye = BalanceData.GetDouble(FileName, "MaxEye");

        /// <summary>目利き：スカウトの顧問の (LDR+DEX)/2 に掛ける係数。</summary>
        public static readonly double ScoutMasterEyeCoeff = BalanceData.GetDouble(FileName, "ScoutMasterEyeCoeff");

        /// <summary>目利き：段階研究（RecruitPaBonus）1段ごとの値。</summary>
        public static readonly double ResearchEyePerStep = BalanceData.GetDouble(FileName, "ResearchEyePerStep");

        /// <summary>目利き：在籍の学者の最も高い INT に掛ける係数。</summary>
        public static readonly double ScholarEyeCoeff = BalanceData.GetDouble(FileName, "ScholarEyeCoeff");

        /// <summary>在籍1か月ごとに幅を狭める割合。</summary>
        public static readonly double MonthlyNarrowRate = BalanceData.GetDouble(FileName, "MonthlyNarrowRate");

        /// <summary>魂魄融和で生まれた娘の幅に掛ける倍率。</summary>
        public static readonly double SoulFusionWidthRate = BalanceData.GetDouble(FileName, "SoulFusionWidthRate");

        /// <summary>幅がこの値以下なら見立てを確かとする。</summary>
        public static readonly double ConfirmWidth = BalanceData.GetDouble(FileName, "ConfirmWidth");

        public static readonly int RankS = BalanceData.GetInt(FileName, "RankS");
        public static readonly int RankA = BalanceData.GetInt(FileName, "RankA");
        public static readonly int RankB = BalanceData.GetInt(FileName, "RankB");
        public static readonly int RankC = BalanceData.GetInt(FileName, "RankC");
    }
}
