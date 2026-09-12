namespace GuildManager.Core.Models
{
    /// <summary>
    /// 施設の種類。仕様書 03 §6「9施設」参照。
    /// v1.3改訂：旧「訓練場・道場（TrainingGround、単一施設）」を、専門特化した
    /// 4施設（WarriorHall/Church/MageLab/ScoutPost）に分割した。
    /// v1.4改訂：スカウト（Scout Master）の配置先として冒険者支援室（RecruitmentOffice）を
    /// 新設した（→ §7.3）。あわせて、宿舎・医務室・ギルド酒場を除く全施設（訓練4施設・
    /// 作戦資料室・冒険者支援室）の初期LvをLv0（未建設）に変更した（→ §6・GameState.
    /// CreateDefaultFacilities）。
    /// </summary>
    public enum FacilityType
    {
        /// <summary>宿舎・医務室のうち宿舎側（現役枠に連動）。初期Lv1。</summary>
        Dormitory,

        /// <summary>宿舎・医務室のうち医務室側（負傷回復・HP自然回復の速度に連動）。初期Lv1。</summary>
        Infirmary,

        /// <summary>作戦資料室（参謀の配置先。§7.2）。v1.4改訂：初期LvをLv0（未建設）に変更。</summary>
        WarRoom,

        /// <summary>ギルド酒場（満足度の自然回復量に連動）。初期Lv1。</summary>
        Tavern,

        /// <summary>戦士訓練所：STR・VITを鍛える（旧訓練場・道場の分割先。v1.3改訂）。v1.4改訂：初期Lv0。</summary>
        WarriorHall,

        /// <summary>教会：MNDを鍛える（旧訓練場・道場の分割先。v1.3改訂）。v1.4改訂：初期Lv0。</summary>
        Church,

        /// <summary>魔法研究所：INTを鍛える（旧訓練場・道場の分割先。v1.3改訂）。v1.4改訂：初期Lv0。</summary>
        MageLab,

        /// <summary>斥候所：AGI・DEXを鍛える（旧訓練場・道場の分割先。v1.3改訂）。v1.4改訂：初期Lv0。</summary>
        ScoutPost,

        /// <summary>
        /// 冒険者支援室（新設、v1.4改訂）：スカウト（Scout Master）の配置先（§7.3）。
        /// 初期Lv0（未建設）。Lv0の間はスカウトを配置できず、新春採用試験（§2.4）自体は
        /// 例年どおり発生するが応募率バフはかからない。
        /// </summary>
        RecruitmentOffice,
    }
}
