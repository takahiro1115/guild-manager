namespace GuildManager.Core.Balance
{
    /// <summary>
    /// ギルドマスターの後方支援（緊急撤退・緊急回復）に関するバランス値。
    /// → コアシステム刷新仕様「(3) ギルドマスター後方支援機能」参照。
    /// 値は docs/04_バランス表/emergency.csv から読み込む（→ 03 §10.1）。
    /// </summary>
    public static class EmergencyBalance
    {
        private const string FileName = "emergency.csv";

        /// <summary>
        /// 緊急回復の発動コスト。仕様では「ギルド備蓄資材を消費」だが、素材・市場システムは
        /// 未実装（→ docs/04_バランス表/README「市場・素材価格」）のため、現時点では
        /// 資金（Gold）で表現している。素材を導入する際はここが差し替え点になる。
        /// </summary>
        public static readonly int EmergencyHealCostGold = BalanceData.GetInt(FileName, "EmergencyHealCostGold");

        /// <summary>緊急回復で戻るHP量＝最大HP×この比率（部隊全員が対象）。</summary>
        public static readonly double EmergencyHealRatio = BalanceData.GetDouble(FileName, "EmergencyHealRatio");
    }
}
