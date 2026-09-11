namespace GuildManager.Core.Models
{
    /// <summary>
    /// 配置（Placement）に関する職業ルール。仕様書 03 §4.2・§2.1 参照。
    /// - 魔導士（Mage）・神官（Cleric）：後衛固定（変更不可）。
    /// - 重戦士（Warrior）・斥候（Ranger）：前衛がデフォルト。プレイヤーが自由に変更できる。
    /// </summary>
    public static class PlacementRules
    {
        /// <summary>職業から決まるデフォルト配置。冒険者生成時（初期メンバー・採用試験）に設定する。</summary>
        public static Placement GetDefault(JobClass jobClass) =>
            IsBackOnly(jobClass) ? Placement.Back : Placement.Front;

        /// <summary>後衛固定（Frontへ変更できない）職業かどうか。</summary>
        public static bool IsBackOnly(JobClass jobClass) =>
            jobClass == JobClass.Mage || jobClass == JobClass.Cleric;
    }
}
