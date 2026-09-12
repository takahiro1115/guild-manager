using System;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 施設（Facility）関連の暫定バランス値。仕様書 03 §6・§6.1 参照。
    /// 05技術メモ§3の方針（数値を各Systemクラスへ直書きしない）に沿い、ここへ集約した。
    /// 04_バランス表.xlsx からの読み込みへの置き換え（Phase 4での外部化）はまだ行っておらず、
    /// 現状はすべて仮値の定数。
    ///
    /// 各Lv依存の値は「Lv1で、施設未実装時代の旧固定値と一致する」よう選んである
    /// （例：訓練場Lv1で枠1、宿舎Lv1で8枠、医務室Lv1で速度1倍、酒場Lv1で回復+1）。
    /// これにより施設システム導入前の既存テストの期待値を変えずに済む。
    /// </summary>
    public static class FacilityBalance
    {
        public const int MaxLevel = 5;

        /// <summary>
        /// Lvアップの改築費（現在Lvから+1する費用）。→ BAL: 施設/改築費。現状は仮値。
        /// v1.4改訂：Lv0→Lv1（新設）の着工にも既存のLv1→Lv2と同額を課す
        /// （currentLevel*500のままだとLv0の場合に0＝無料建設になってしまうバグの修正。
        /// 「資金を払って建設して初めてLv1になる」という仕様に反するため）。
        /// </summary>
        public static int GetUpgradeCost(Models.FacilityType type, int currentLevel) => Math.Max(currentLevel, 1) * 500;

        /// <summary>着工から完成までの工事期間（週）。→ BAL: 施設/工事期間。現状は仮値。</summary>
        public static int GetConstructionWeeks(Models.FacilityType type, int currentLevel) => Math.Max(currentLevel, 1);

        /// <summary>
        /// 訓練施設（戦士訓練所/教会/魔法研究所/斥候所）のLvに連動する配置枠数。
        /// → BAL: 施設/訓練場。現状は仮値（Lv=枠数。4施設それぞれ独立に適用する。v1.3改訂：
        /// 旧・単一の訓練場・道場のLv1=1名という値を、分割後の各施設にもそのまま踏襲）。
        /// </summary>
        public static int GetTrainingSlotCapacity(int facilityLevel) => facilityLevel;

        /// <summary>指定した施設種別が訓練施設（4分割後）かどうか。</summary>
        public static bool IsTrainingFacility(FacilityType type) =>
            type is FacilityType.WarriorHall or FacilityType.Church or FacilityType.MageLab or FacilityType.ScoutPost;

        /// <summary>
        /// 訓練施設ごとの成長ロール対象ステータス（経路2、→ 03 §3.1〜3.4・§6）。
        /// 戦士訓練所→STR・VIT、教会→MND、魔法研究所→INT、斥候所→AGI・DEX。
        /// 訓練施設以外を渡すと例外（呼び出し側の設計ミスを早期検知するため）。
        /// </summary>
        public static string[] GetTrainingTargetStats(FacilityType type) => type switch
        {
            FacilityType.WarriorHall => new[] { "STR", "VIT" },
            FacilityType.Church => new[] { "MND" },
            FacilityType.MageLab => new[] { "INT" },
            FacilityType.ScoutPost => new[] { "AGI", "DEX" },
            _ => throw new ArgumentException($"施設{type}は訓練施設ではありません（訓練対象ステータスを持ちません）"),
        };

        /// <summary>宿舎Lvに連動する現役枠の上限。→ BAL: 施設/宿舎。現状は仮値（Lv1=8枠、以降+4/Lv）。</summary>
        public static int GetDormitoryCapacity(int dormitoryLevel) => 8 + (dormitoryLevel - 1) * 4;

        /// <summary>
        /// 医務室Lvに連動する重傷回復速度（1週あたりのInjuryWeeksRemaining減少量）。
        /// → BAL: 施設/医務室。現状は仮値（Lv=週あたり減少量。Lv1=1＝施設未実装時と同じ速度）。
        /// </summary>
        public static int GetInfirmaryInjuryRecoverySpeed(int infirmaryLevel) => infirmaryLevel;

        /// <summary>
        /// 医務室Lvに連動するHP自然回復（§3.5改）の倍率。
        /// → BAL: 施設/医務室。現状は仮値（Lv1=1.0倍＝施設未実装時と同じ、以降+0.25倍/Lv）。
        /// </summary>
        public static double GetInfirmaryHpRecoveryMultiplier(int infirmaryLevel) => 1.0 + (infirmaryLevel - 1) * 0.25;

        /// <summary>
        /// ギルド酒場Lvに連動する満足度の自然回復量（§5.1）。
        /// → BAL: 満足度/自然回復。現状は仮値（Lv=回復量。Lv1=1＝施設未実装時と同じ）。
        /// </summary>
        public static int GetTavernSatisfactionRecovery(int tavernLevel) => tavernLevel;
    }
}
