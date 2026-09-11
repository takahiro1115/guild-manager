namespace GuildManager.Core.Models
{
    /// <summary>施設の種類。仕様書 03 §6「4大施設」参照。</summary>
    public enum FacilityType
    {
        /// <summary>宿舎・医務室のうち宿舎側（現役枠に連動）。</summary>
        Dormitory,

        /// <summary>宿舎・医務室のうち医務室側（負傷回復・HP自然回復の速度に連動）。</summary>
        Infirmary,

        /// <summary>訓練場・道場（訓練場配置の枠数に連動）。</summary>
        TrainingGround,

        /// <summary>作戦資料室（勝率計算精度・事故率抑制。§7と連動、現状は未接続）。</summary>
        WarRoom,

        /// <summary>ギルド酒場（満足度の自然回復量に連動）。</summary>
        Tavern
    }
}
