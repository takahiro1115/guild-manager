using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 配置（Placement）関連のバランス値。仕様書 03 §4.1・§4.2 参照。
    /// 値は docs/04_バランス表/combat.csv から読み込む（→ 03 §10.1、項目58）。
    /// </summary>
    public static class PlacementBalance
    {
        private const string FileName = "combat.csv";

        private static readonly double WarriorFront = BalanceData.GetDouble(FileName, "PlacementCorrection_Warrior_Front");
        private static readonly double WarriorBack = BalanceData.GetDouble(FileName, "PlacementCorrection_Warrior_Back");
        private static readonly double RangerFront = BalanceData.GetDouble(FileName, "PlacementCorrection_Ranger_Front");
        private static readonly double RangerBack = BalanceData.GetDouble(FileName, "PlacementCorrection_Ranger_Back");
        private static readonly double MageFront = BalanceData.GetDouble(FileName, "PlacementCorrection_Mage_Front");
        private static readonly double MageBack = BalanceData.GetDouble(FileName, "PlacementCorrection_Mage_Back");
        private static readonly double ClericFront = BalanceData.GetDouble(FileName, "PlacementCorrection_Cleric_Front");
        private static readonly double ClericBack = BalanceData.GetDouble(FileName, "PlacementCorrection_Cleric_Back");

        /// <summary>
        /// 職業×配置による個人CP補正倍率（→ 03 §4.2「職業配置補正(職業, 配置)」。→ BAL: 戦闘/配置補正）。
        /// 本来の役割どおりの配置（前衛職が前衛、後衛専任職が後衛）はボーナス、外れた配置はペナルティ。
        /// Ranger は前衛/後衛どちらも適性がある職業のため補正なし（仕様書 03 §2.1）。
        /// 配置は職業で固定されないため（→ PlacementRules）、Mage/ClericのFrontも実際に選べる
        /// 組み合わせ：役割から外れる分のペナルティとしてここに反映している。
        /// </summary>
        public static double GetPersonalCpCorrection(JobClass jobClass, Placement placement) => (jobClass, placement) switch
        {
            (JobClass.Warrior, Placement.Front) => WarriorFront,
            (JobClass.Warrior, Placement.Back) => WarriorBack,
            (JobClass.Ranger, Placement.Front) => RangerFront,
            (JobClass.Ranger, Placement.Back) => RangerBack,
            (JobClass.Mage, Placement.Back) => MageBack,
            (JobClass.Mage, Placement.Front) => MageFront,
            (JobClass.Cleric, Placement.Back) => ClericBack,
            (JobClass.Cleric, Placement.Front) => ClericFront,
            _ => 1.0,
        };

        // ---- 不意打ち時の被弾ウェイト（→ 03 §4.1「後衛の被弾ウェイト上昇」・BAL: 戦闘/被弾ウェイト） ----
        // 奇襲成功・通常交戦では適用しない（QuestResolver側でEncounter==Ambushedの時のみ参照する）。
        public static readonly double AmbushBackRowWeight = BalanceData.GetDouble(FileName, "AmbushBackRowWeight");
        public static readonly double AmbushFrontRowWeight = BalanceData.GetDouble(FileName, "AmbushFrontRowWeight");
    }
}
