namespace GuildManager.Core.Balance
{
    /// <summary>
    /// パーティ携行アイテム（消耗品。→ ConsumableCatalog）の価格・効果量に関するバランス値。
    /// 「パーティ携行アイテム」刷新仕様参照。値は docs/04_バランス表/consumables.csv から読み込む
    /// （→ 03 §10.1。ItemCatalog/EquipmentBalanceと同じ「構造はModels、数値はBalance」の分担）。
    /// </summary>
    public static class ConsumableBalance
    {
        private const string FileName = "consumables.csv";

        // ---- 大迷宮ボスギミック対策アイテム4種：価格のみ（→ 03 §4.5.4「パーティ携行アイテムポーチ」） ----
        // 効果は「対策済みならそのギミックのペナルティを受けない」で固定のため、効果量は持たない。
        // 旧・環境ギミック相殺（聖水・松明・登攀具）と効果アイテム（煙幕弾・高品質傷薬・携帯糧食）の
        // キーは、参照元を失っていたため 03 §0.13 で撤去した。
        public static readonly int AntidotePrice = BalanceData.GetInt(FileName, "Antidote_Price");
        public static readonly int AcidFlaskPrice = BalanceData.GetInt(FileName, "AcidFlask_Price");
        public static readonly int NetPrice = BalanceData.GetInt(FileName, "Net_Price");
        public static readonly int CharmPrice = BalanceData.GetInt(FileName, "Charm_Price");
    }
}
