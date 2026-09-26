namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 訓練場配置・静養関連のバランス値。仕様書 03 §3.1〜3.4（経路2・運用ルール確定）・
    /// §3.5改（訓練週のHP処理・静養によるHP自然回復）参照。
    ///
    /// 枠数（Lv→枠数の対応）は FacilityBalance.GetTrainingSlotCapacity に移動した
    /// （施設Lv投資システム、§6実装に伴う接続）。ここには施設Lvに依存しない、
    /// 配置1名あたりの費用・HP消費量・静養時のHP自然回復率のみを残す。
    /// 値は docs/04_バランス表/training.csv から読み込む（→ 03 §10.1、項目58。
    /// RestRecoveryRatioはRestRecoverySystemが参照するが、training.csvの1項目のため
    /// ここに置く）。
    /// </summary>
    public static class TrainingBalance
    {
        private const string FileName = "training.csv";

        /// <summary>配置1名あたりの週次利用費用（都度払い）。→ BAL: 訓練/週次費用。</summary>
        public static readonly int WeeklyCost = BalanceData.GetInt(FileName, "WeeklyCost");

        /// <summary>訓練した週のHP消費量。→ BAL: 訓練/HP消費。</summary>
        public static readonly int WeeklyHpCost = BalanceData.GetInt(FileName, "WeeklyHpCost");

        /// <summary>訓練によるHP減少の下限。致死判定（§4.3）には一切接続しない（訓練は死なない低リスク経路）。</summary>
        public static readonly int MinHp = BalanceData.GetInt(FileName, "MinHp");

        /// <summary>
        /// 静養（待機）時のHP自然回復率（→ 03 §3.5改）。MaxHP×この比率が基礎回復量になり、
        /// 医務室（Infirmary）のLv連動倍率（→ FacilityBalance.GetInfirmaryHpRecoveryMultiplier）
        /// を乗算する。→ BAL: 静養/待機回復。
        /// </summary>
        public static readonly double RestRecoveryRatio = BalanceData.GetDouble(FileName, "RestRecoveryRatio");

        /// <summary>
        /// 勤勉（→ TraitCatalog.Diligent、03 §5.3.2・§6.2）：訓練施設での週次成長ロールの基礎確率に加算する値
        /// （→ GrowthSystem.ProcessTrainingGrowth。+0.10＝+10%ポイント、施設倍率・教官ボーナスはこの後に掛かる）。
        /// </summary>
        public static readonly double DiligentGrowthRateBonus = BalanceData.GetDouble(FileName, "DiligentGrowthRateBonus");
    }
}
