namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 装備アイテム（→ ItemCatalog）の価格・効果量に関するバランス値。仕様書 03 §4.2.2 参照。
    /// 値は docs/04_バランス表/equipment.csv から読み込む（→ 03 §10.1、項目58フォローアップ）。
    ///
    /// 事前調査メモ（項目58）：ItemCatalog.csの各Itemが持つPrice・EffectValueは、クラス
    /// doc コメントに「値はすべて仮値（→ BAL: 装備）」と明記された状態で直書きされていた
    /// （項目58時点では対応CSVが無かったため対象外とし、本クラスを新設してフォローアップ
    /// した）。アイテムの構造（Id・Name・Slot・EffectType・AllowedJobs・VisualPartId）は
    /// 引き続きItemCatalog.cs側に残す（→ README「CSV化していない値」の方針と同じ：
    /// 構造を定義する値ではなく、調整対象の数値のみをCSV化する）。
    /// </summary>
    public static class EquipmentBalance
    {
        private const string FileName = "equipment.csv";

        public static readonly int IronSwordPrice = BalanceData.GetInt(FileName, "IronSword_Price");
        public static readonly int IronSwordEffectValue = BalanceData.GetInt(FileName, "IronSword_EffectValue");

        public static readonly int GreatSwordPrice = BalanceData.GetInt(FileName, "GreatSword_Price");
        public static readonly int GreatSwordEffectValue = BalanceData.GetInt(FileName, "GreatSword_EffectValue");

        public static readonly int MageStaffPrice = BalanceData.GetInt(FileName, "MageStaff_Price");
        public static readonly int MageStaffEffectValue = BalanceData.GetInt(FileName, "MageStaff_EffectValue");

        public static readonly int LeatherArmorPrice = BalanceData.GetInt(FileName, "LeatherArmor_Price");
        public static readonly int LeatherArmorEffectValue = BalanceData.GetInt(FileName, "LeatherArmor_EffectValue");

        public static readonly int HeavyArmorPrice = BalanceData.GetInt(FileName, "HeavyArmor_Price");
        public static readonly int HeavyArmorEffectValue = BalanceData.GetInt(FileName, "HeavyArmor_EffectValue");

        public static readonly int RobePrice = BalanceData.GetInt(FileName, "Robe_Price");
        public static readonly int RobeEffectValue = BalanceData.GetInt(FileName, "Robe_EffectValue");

        public static readonly int PowerRingPrice = BalanceData.GetInt(FileName, "PowerRing_Price");
        public static readonly int PowerRingEffectValue = BalanceData.GetInt(FileName, "PowerRing_EffectValue");

        public static readonly int LifeAmuletPrice = BalanceData.GetInt(FileName, "LifeAmulet_Price");
        public static readonly int LifeAmuletEffectValue = BalanceData.GetInt(FileName, "LifeAmulet_EffectValue");

        public static readonly int QuickBroochPrice = BalanceData.GetInt(FileName, "QuickBrooch_Price");
        public static readonly int QuickBroochEffectValue = BalanceData.GetInt(FileName, "QuickBrooch_EffectValue");

        public static readonly int GuardCharmPrice = BalanceData.GetInt(FileName, "GuardCharm_Price");
        public static readonly int GuardCharmEffectValue = BalanceData.GetInt(FileName, "GuardCharm_EffectValue");
    }
}
