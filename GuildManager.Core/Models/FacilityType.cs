namespace GuildManager.Core.Models
{
    /// <summary>
    /// 施設の種類。仕様書 03 §6「8施設」参照。
    /// v1.3改訂：旧「訓練場・道場（TrainingGround、単一施設）」を、専門特化した
    /// 4施設（WarriorHall/Church/MageLab/ScoutPost）に分割した。
    /// </summary>
    public enum FacilityType
    {
        /// <summary>宿舎・医務室のうち宿舎側（現役枠に連動）。</summary>
        Dormitory,

        /// <summary>宿舎・医務室のうち医務室側（負傷回復・HP自然回復の速度に連動）。</summary>
        Infirmary,

        /// <summary>作戦資料室（勝率計算精度・事故率抑制。§7と連動、現状は未接続）。</summary>
        WarRoom,

        /// <summary>ギルド酒場（満足度の自然回復量に連動）。</summary>
        Tavern,

        /// <summary>戦士訓練所：STR・VITを鍛える（旧訓練場・道場の分割先。v1.3改訂）。</summary>
        WarriorHall,

        /// <summary>教会：MNDを鍛える（旧訓練場・道場の分割先。v1.3改訂）。</summary>
        Church,

        /// <summary>魔法研究所：INTを鍛える（旧訓練場・道場の分割先。v1.3改訂）。</summary>
        MageLab,

        /// <summary>斥候所：AGI・DEXを鍛える（旧訓練場・道場の分割先。v1.3改訂）。</summary>
        ScoutPost,
    }
}
