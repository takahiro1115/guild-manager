namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 配置（Placement）関連のバランス値。仕様書 03 §4.1・§4.2 参照。
    /// 値は docs/04_バランス表/combat.csv から読み込む（→ 03 §10.1、項目58）。
    ///
    /// 職業×配置による個人CP補正（旧 GetPersonalCpCorrection・combat.csv の PlacementCorrection_*）は、
    /// 配置が職業で一意に決まる（→ PlacementRules）ようになって「本来の配置なら必ずボーナス」という
    /// 形骸化した倍率になっていたため、2026年9月に撤廃した（→ 03 §4.2・§4.5.4・§0.23）。
    /// 前衛／後衛は UI 上の役割表示としてのみ残っている。
    /// </summary>
    public static class PlacementBalance
    {
        private const string FileName = "combat.csv";

        // ---- 不意打ち時の被弾ウェイト（→ 03 §4.1「後衛の被弾ウェイト上昇」・BAL: 戦闘/被弾ウェイト） ----
        // 奇襲成功・通常交戦では適用しない（QuestResolver側でEncounter==Ambushedの時のみ参照する）。
        public static readonly double AmbushBackRowWeight = BalanceData.GetDouble(FileName, "AmbushBackRowWeight");
        public static readonly double AmbushFrontRowWeight = BalanceData.GetDouble(FileName, "AmbushFrontRowWeight");
    }
}
