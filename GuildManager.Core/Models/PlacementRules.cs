namespace GuildManager.Core.Models
{
    /// <summary>
    /// 配置（Placement）に関する職業ルール。仕様書 03 §4.2・§2.1 参照。
    ///
    /// 配置は職業で固定されない。どの職業でも前衛/後衛を自由に選べる
    /// （ユーザー決定：「魔法使いや僧侶も前衛になることができる。職業で固定になることはない」）。
    /// 職業ごとの初期配置（生成時のデフォルト値）だけは、役割に沿った自然な値を提示する：
    /// - 魔導士（Mage）・神官（Cleric）：後衛がデフォルト（変更は自由）。
    /// - 重戦士（Warrior）・斥候（Ranger）：前衛がデフォルト（変更は自由）。
    /// 役割から外れた配置を選んだ場合のペナルティは個人CP補正（→ Balance/PlacementBalance）で表現する。
    /// </summary>
    public static class PlacementRules
    {
        /// <summary>職業から決まるデフォルト配置。冒険者生成時（初期メンバー・採用試験）に設定する。</summary>
        public static Placement GetDefault(JobClass jobClass) => jobClass switch
        {
            JobClass.Mage => Placement.Back,
            JobClass.Cleric => Placement.Back,
            _ => Placement.Front, // Warrior, Ranger
        };
    }
}
