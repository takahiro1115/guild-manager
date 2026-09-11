using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 配置（Placement）関連の暫定バランス値。仕様書 03 §4.1・§4.2 参照。
    /// 05技術メモ§3の方針（数値を各Systemクラスへ直書きしない）に沿い、ここへ集約した。
    /// 04_バランス表.xlsx からの読み込みへの置き換え（Phase 4での外部化）はまだ行っておらず、
    /// 現状はすべて仮値の定数。
    /// </summary>
    public static class PlacementBalance
    {
        /// <summary>
        /// 職業×配置による個人CP補正倍率（→ 03 §4.2「職業配置補正(職業, 配置)」。→ BAL: 戦闘/配置補正）。
        /// 本来の役割どおりの配置（前衛職が前衛、後衛専任職が後衛）はボーナス、外れた配置はペナルティ。
        /// Ranger は前衛/後衛どちらも適性がある職業のため補正なし（仕様書 03 §2.1）。
        /// 配置は職業で固定されないため（→ PlacementRules）、Mage/ClericのFrontも実際に選べる
        /// 組み合わせ：役割から外れる分のペナルティ（0.8倍）としてここに反映している。
        /// </summary>
        public static double GetPersonalCpCorrection(JobClass jobClass, Placement placement) => (jobClass, placement) switch
        {
            (JobClass.Warrior, Placement.Front) => 1.2,
            (JobClass.Warrior, Placement.Back) => 0.8,
            (JobClass.Ranger, Placement.Front) => 1.0,
            (JobClass.Ranger, Placement.Back) => 1.0,
            (JobClass.Mage, Placement.Back) => 1.2,
            (JobClass.Mage, Placement.Front) => 0.8,
            (JobClass.Cleric, Placement.Back) => 1.2,
            (JobClass.Cleric, Placement.Front) => 0.8,
            _ => 1.0,
        };

        // ---- 不意打ち時の被弾ウェイト（→ 03 §4.1「後衛の被弾ウェイト上昇」・BAL: 戦闘/被弾ウェイト） ----
        // 奇襲成功・通常交戦では適用しない（QuestResolver側でEncounter==Ambushedの時のみ参照する）。
        public const double AmbushBackRowWeight = 1.5;
        public const double AmbushFrontRowWeight = 1.0;
    }
}
