namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 訓練場配置関連の暫定バランス値。仕様書 03 §3.1〜3.4（経路2・運用ルール確定）・
    /// §3.5改（訓練週のHP処理）参照。
    ///
    /// 枠数（Lv→枠数の対応）は FacilityBalance.GetTrainingSlotCapacity に移動した
    /// （施設Lv投資システム、§6実装に伴う接続）。ここには施設Lvに依存しない、
    /// 配置1名あたりの費用・HP消費量のみを残す。
    /// 05技術メモ§3の方針（数値を各Systemクラスへ直書きしない）に沿い、ここへ集約した。
    /// 04_バランス表.xlsx からの読み込みへの置き換え（Phase 4での外部化）はまだ行っておらず、
    /// 現状はすべて仮値の定数。
    /// </summary>
    public static class TrainingBalance
    {
        /// <summary>配置1名あたりの週次利用費用（都度払い）。→ BAL: 訓練/週次費用。現状は仮値。</summary>
        public const int WeeklyCost = 20;

        /// <summary>訓練した週のHP消費量。→ BAL: 訓練/HP消費。現状は仮値。</summary>
        public const int WeeklyHpCost = 5;

        /// <summary>訓練によるHP減少の下限。致死判定（§4.3）には一切接続しない（訓練は死なない低リスク経路）。</summary>
        public const int MinHp = 1;
    }
}
