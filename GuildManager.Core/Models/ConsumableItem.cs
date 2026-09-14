namespace GuildManager.Core.Models
{
    /// <summary>
    /// パーティ携行アイテム（消耗品）の定義（カタログ）。「パーティ携行アイテム」刷新仕様参照。
    /// 個体ごとにパラメータが変わらない前提の静的定義。TraitDefinition・Itemと同じパターン
    /// （→ ConsumableCatalog）。装備（Item）と異なり出撃のたびに使い切りで、
    /// クエスト解決時に一括消費される（→ Party.ConsumableItemIds、QuestResolver.Resolve）。
    /// </summary>
    public class ConsumableItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public ConsumableEffectType EffectType { get; set; }

        /// <summary>EffectType=GimmickCounterの場合のみ使用。相殺する環境ギミック。</summary>
        public EnvironmentTag? CounterTag { get; set; }

        /// <summary>効果量（種別ごとに意味が異なる。→ BAL: 携行アイテム/consumables.csv）。</summary>
        public double EffectValue { get; set; }

        public int Price { get; set; }
    }
}
