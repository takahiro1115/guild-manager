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

        // ---- ギミック相殺アイテム5種：価格のみ（評価器 GimmickEvaluator は 03 §0.12 で撤去済み。現在は効果を持たない） ----
        public static readonly int AntidotePrice = BalanceData.GetInt(FileName, "Antidote_Price");
        public static readonly int HolyWaterPrice = BalanceData.GetInt(FileName, "HolyWater_Price");
        public static readonly int TorchPrice = BalanceData.GetInt(FileName, "Torch_Price");
        public static readonly int ClimbingGearPrice = BalanceData.GetInt(FileName, "ClimbingGear_Price");
        public static readonly int CharmPrice = BalanceData.GetInt(FileName, "Charm_Price");

        // ---- 大迷宮ボスギミック対策アイテム2種（2026年9月新設、→ 03 §4.5.4「パーティ携行アイテムポーチ」） ----
        public static readonly int AcidFlaskPrice = BalanceData.GetInt(FileName, "AcidFlask_Price");
        public static readonly int NetPrice = BalanceData.GetInt(FileName, "Net_Price");

        // ---- 煙幕弾：ダウン率（HP消費%）半減 ----
        public static readonly int SmokeBombPrice = BalanceData.GetInt(FileName, "SmokeBomb_Price");
        public static readonly double SmokeBombDownRateMultiplier = BalanceData.GetDouble(FileName, "SmokeBomb_DownRateMultiplier");

        // ---- 高品質傷薬：損耗（HP消費%）を固定量軽減 ----
        public static readonly int QualityHealingSalvePrice = BalanceData.GetInt(FileName, "QualityHealingSalve_Price");
        public static readonly double QualityHealingSalveDamageReductionPct = BalanceData.GetDouble(FileName, "QualityHealingSalve_DamageReductionPct");

        // ---- 携帯糧食：クエスト達成時に満足度+固定量 ----
        public static readonly int TravelRationsPrice = BalanceData.GetInt(FileName, "TravelRations_Price");
        public static readonly double TravelRationsSatisfactionBonus = BalanceData.GetDouble(FileName, "TravelRations_SatisfactionBonus");
    }
}
