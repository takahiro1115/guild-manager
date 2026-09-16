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
        private static readonly double KnightFront = BalanceData.GetDouble(FileName, "PlacementCorrection_Knight_Front");
        private static readonly double KnightBack = BalanceData.GetDouble(FileName, "PlacementCorrection_Knight_Back");
        private static readonly double ThiefFront = BalanceData.GetDouble(FileName, "PlacementCorrection_Thief_Front");
        private static readonly double ThiefBack = BalanceData.GetDouble(FileName, "PlacementCorrection_Thief_Back");
        private static readonly double ScholarFront = BalanceData.GetDouble(FileName, "PlacementCorrection_Scholar_Front");
        private static readonly double ScholarBack = BalanceData.GetDouble(FileName, "PlacementCorrection_Scholar_Back");

        /// <summary>
        /// 職業×配置による個人CP補正倍率（→ 03 §4.2「職業配置補正(職業, 配置)」。→ BAL: 戦闘/配置補正）。
        /// 本来の役割どおりの配置（前衛職が前衛、後衛専任職が後衛）はボーナス、外れた配置はペナルティ。
        /// Ranger は前衛/後衛どちらも適性がある職業のため補正なし（仕様書 03 §2.1）。
        ///
        /// 7職業化（改訂）：新3職にも既存職と同じ考え方の補正を与え、戦闘格差を解消した。
        /// 騎士＝重戦士と同じ（前衛1.2／後衛0.8）、学者＝魔導士と同じ（前衛0.8／後衛1.2）、
        /// 盗賊＝斥候と同じ（前後とも1.0）。
        /// 既定値（1.0）は、将来さらに職業を追加した際にCSVの行が無くても戦闘が止まらないための
        /// 防御的なフォールバックであり、現行の7職はすべて明示的な分岐で補正を得る。
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
            (JobClass.Knight, Placement.Front) => KnightFront,
            (JobClass.Knight, Placement.Back) => KnightBack,
            (JobClass.Thief, Placement.Front) => ThiefFront,
            (JobClass.Thief, Placement.Back) => ThiefBack,
            (JobClass.Scholar, Placement.Front) => ScholarFront,
            (JobClass.Scholar, Placement.Back) => ScholarBack,
            _ => 1.0,
        };

        // ---- 不意打ち時の被弾ウェイト（→ 03 §4.1「後衛の被弾ウェイト上昇」・BAL: 戦闘/被弾ウェイト） ----
        // 奇襲成功・通常交戦では適用しない（QuestResolver側でEncounter==Ambushedの時のみ参照する）。
        public static readonly double AmbushBackRowWeight = BalanceData.GetDouble(FileName, "AmbushBackRowWeight");
        public static readonly double AmbushFrontRowWeight = BalanceData.GetDouble(FileName, "AmbushFrontRowWeight");
    }
}
