namespace GuildManager.Core.Models
{
    /// <summary>
    /// 施設の種類。仕様書 03 §6 参照。
    /// v1.3改訂：旧「訓練場・道場（TrainingGround、単一施設）」を専門特化した4施設に分割した。
    /// v1.4改訂：スカウト（Scout Master）の配置先として冒険者支援室（RecruitmentOffice）を
    /// 新設した（→ §7.3）。あわせて、宿舎・医務室・ギルド酒場を除く全施設の初期LvをLv0（未建設）に変更した。
    /// §0.75（2026年10月）：訓練施設を鍛錬所・学問所・技巧所の3施設に組み直した（教会と魔法研究所を学問所に統合）。
    /// 3施設とも2つの能力を扱い、施設ごとに「両方」か「片方に特化」かを選ぶ（→ GameState.TrainingSpecialtyStats）。
    /// </summary>
    public enum FacilityType
    {
        /// <summary>宿舎・医務室のうち宿舎側（現役枠に連動）。初期Lv1。</summary>
        Dormitory,

        /// <summary>宿舎・医務室のうち医務室側（負傷回復・HP自然回復の速度に連動）。初期Lv1。</summary>
        Infirmary,

        /// <summary>作戦資料室（参謀の配置先。§7.2）。初期Lv0（未建設）。</summary>
        WarRoom,

        /// <summary>ギルド酒場（満足度の自然回復量に連動）。初期Lv1。</summary>
        Tavern,

        /// <summary>鍛錬所：STR・VITを鍛える（旧戦士訓練所。§0.75）。初期Lv0。</summary>
        DrillHall,

        /// <summary>学問所：MND・INTを鍛える（旧教会＋旧魔法研究所。§0.75）。初期Lv0。</summary>
        Academy,

        /// <summary>技巧所：AGI・DEXを鍛える（旧斥候所。§0.75）。初期Lv0。</summary>
        SkillHall,

        /// <summary>
        /// 冒険者支援室（新設、v1.4改訂）：スカウト（Scout Master）の配置先（§7.3）。
        /// 初期Lv0（未建設）。Lv0の間はスカウトを配置できず、新春採用試験（§2.4）自体は
        /// 例年どおり発生するが応募率バフはかからない。
        /// </summary>
        RecruitmentOffice,
    }
}
