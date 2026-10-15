namespace GuildManager.Core.Balance
{
    /// <summary>ギルドの店（道具屋・酒場、§0.134）のバランス値（→ docs/04_バランス表/shop.csv）。</summary>
    public static class ShopBalance
    {
        private const string FileName = "shop.csv";

        public static readonly int ItemShopSlots = BalanceData.GetInt(FileName, "ItemShopSlots");
        public static readonly int TavernSlotsPerLevel = BalanceData.GetInt(FileName, "TavernSlotsPerLevel");
        public static readonly int ShopBaseSales = BalanceData.GetInt(FileName, "ShopBaseSales");
        public static readonly int ShopStatLine = BalanceData.GetInt(FileName, "ShopStatLine");
        public static readonly int ShopSalesPerStat = BalanceData.GetInt(FileName, "ShopSalesPerStat");
        public static readonly double ItemShopStockPerUnit = BalanceData.GetDouble(FileName, "ItemShopStockPerUnit");
        public static readonly int ItemShopStockCap = BalanceData.GetInt(FileName, "ItemShopStockCap");
        public static readonly int TavernFameHonor = BalanceData.GetInt(FileName, "TavernFameHonor");
        public static readonly int TavernFameName = BalanceData.GetInt(FileName, "TavernFameName");
        public static readonly int TavernFameLegend = BalanceData.GetInt(FileName, "TavernFameLegend");
        public static readonly int TavernHallOfFameBonus = BalanceData.GetInt(FileName, "TavernHallOfFameBonus");
    }
}
