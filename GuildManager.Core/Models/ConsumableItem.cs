namespace GuildManager.Core.Models
{
    /// <summary>
    /// パーティ携行アイテム（消耗品）の定義（カタログ）。「パーティ携行アイテム」刷新仕様参照。
    /// 個体ごとにパラメータが変わらない前提の静的定義。TraitDefinition・Itemと同じパターン
    /// （→ ConsumableCatalog）。装備（Item）と異なり出撃のたびに使い切りで、
    /// 階層ボス討伐の解決時に消費される（→ Party.ConsumableItemIds、DungeonResolver）。
    ///
    /// 2026年9月の棚卸し（→ 03 §0.13）で、旧・通常クエストの環境ギミック（EnvironmentTag）を
    /// 相殺する用途は撤廃した。現在の携行アイテムは**大迷宮のボスギミック対策専用**。
    /// </summary>
    public class ConsumableItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public ConsumableEffectType EffectType { get; set; }

        /// <summary>
        /// 対策できる大迷宮ボスのギミック種別（→ BossGimmick.Type・RequiredItemId）。
        /// ボス側の `RequiredItemId` と対になる逆引き用の情報で、UIの対策充足表示に使う。
        /// </summary>
        public BossGimmickType TargetGimmick { get; set; }

        /// <summary>効果量（種別ごとに意味が異なる。→ BAL: 携行アイテム/consumables.csv）。</summary>
        public double EffectValue { get; set; }

        public int Price { get; set; }
    }
}
