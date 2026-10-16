using System.Collections.Generic;
using System.Linq;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 資金の調達（段4e、§0.104）：商人ギルドの融資・イザベラの融資・スポンサー契約の値。
    /// 値は docs/04_バランス表/funding.csv から読み込む（→ Systems.FundingSystem）。
    /// </summary>
    public static class FundingBalance
    {
        private const string FileName = "funding.csv";

        public static readonly IReadOnlyList<int> MerchantLoanAmounts = BalanceData.GetString(FileName, "MerchantLoanAmounts")
            .Split(';').Select(s => int.Parse(s.Trim())).ToList();
        public static readonly double MerchantLoanFee = BalanceData.GetDouble(FileName, "MerchantLoanFee");
        public static readonly int MerchantLoanMonths = BalanceData.GetInt(FileName, "MerchantLoanMonths");
        public static readonly int MerchantLoanMoodLoss = BalanceData.GetInt(FileName, "MerchantLoanMoodLoss");

        public static readonly int IsabellaLoanBase = BalanceData.GetInt(FileName, "IsabellaLoanBase");
        public static readonly int IsabellaLoanPerExchangeWin = BalanceData.GetInt(FileName, "IsabellaLoanPerExchangeWin");
        public static readonly int IsabellaLoanMax = BalanceData.GetInt(FileName, "IsabellaLoanMax");
        public static readonly int IsabellaLoanStep = BalanceData.GetInt(FileName, "IsabellaLoanStep");
        public static readonly double IsabellaLoanFee = BalanceData.GetDouble(FileName, "IsabellaLoanFee");
        public static readonly int IsabellaLoanMonths = BalanceData.GetInt(FileName, "IsabellaLoanMonths");
        public static readonly int IsabellaLoanPerYear = BalanceData.GetInt(FileName, "IsabellaLoanPerYear");

        /// <summary>アルベールのへそくり（§0.140）：資金の画面が開く前に所持金がマイナスになったとき、一度だけ足す額。</summary>
        public static readonly int AlbertBailoutGold = BalanceData.GetInt(FileName, "AlbertBailoutGold");

        public static readonly int SponsorFirstWeek = BalanceData.GetInt(FileName, "SponsorFirstWeek");
        public static readonly int SponsorOffers = BalanceData.GetInt(FileName, "SponsorOffers");
        public static readonly int SponsorWeeks = BalanceData.GetInt(FileName, "SponsorWeeks");
        public static readonly int SponsorAdvanceBaseCap = BalanceData.GetInt(FileName, "SponsorAdvanceBaseCap");
        public static readonly double SponsorAdvanceModest = BalanceData.GetDouble(FileName, "SponsorAdvance_Modest");
        public static readonly double SponsorAdvanceBold = BalanceData.GetDouble(FileName, "SponsorAdvance_Bold");
        public static readonly double SponsorBonus = BalanceData.GetDouble(FileName, "SponsorBonus");
        public static readonly double SponsorPenalty = BalanceData.GetDouble(FileName, "SponsorPenalty");
        public static readonly int SponsorSuccessMood = BalanceData.GetInt(FileName, "SponsorSuccessMood");
        public static readonly int SponsorFailureMood = BalanceData.GetInt(FileName, "SponsorFailureMood");
        public static readonly IReadOnlyList<string> SponsorNames = BalanceData.GetString(FileName, "SponsorNames")
            .Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        private static readonly int TargetBossesModest = BalanceData.GetInt(FileName, "SponsorTarget_Bosses_Modest");
        private static readonly int TargetBossesBold = BalanceData.GetInt(FileName, "SponsorTarget_Bosses_Bold");
        private static readonly int TargetPlacingsModest = BalanceData.GetInt(FileName, "SponsorTarget_Placings_Modest");
        private static readonly int TargetPlacingsBold = BalanceData.GetInt(FileName, "SponsorTarget_Placings_Bold");
        private static readonly int TargetCommissionsModest = BalanceData.GetInt(FileName, "SponsorTarget_Commissions_Modest");
        private static readonly int TargetCommissionsBold = BalanceData.GetInt(FileName, "SponsorTarget_Commissions_Bold");

        /// <summary>ノルマの数（種類と、強気か）。</summary>
        public static int GetTarget(Models.SponsorQuota quota, bool bold) => quota switch
        {
            Models.SponsorQuota.Placings => bold ? TargetPlacingsBold : TargetPlacingsModest,
            Models.SponsorQuota.Commissions => bold ? TargetCommissionsBold : TargetCommissionsModest,
            _ => bold ? TargetBossesBold : TargetBossesModest,
        };
    }
}
