namespace GuildManager.Core.Models
{
    /// <summary>
    /// パーティ携行アイテム（消耗品ポーチ）の効果種別。「パーティ携行アイテム」刷新仕様参照。
    ///
    /// 2026年9月の棚卸し（→ 03 §0.13）で、参照元を失っていた効果種別
    /// （DownRateHalving＝煙幕弾／DamageReduction＝高品質傷薬／SatisfactionBonus＝携帯糧食）は
    /// 対応するアイテムごと撤去した。現存するのは大迷宮のボスギミック対策のみ。
    /// </summary>
    public enum ConsumableEffectType
    {
        /// <summary>
        /// 大迷宮ボスのギミックを対策する（→ ConsumableItem.TargetGimmick・
        /// BossGimmick.RequiredItemId）。対策済みならそのギミックのペナルティを受けない。
        /// </summary>
        GimmickCounter,
    }
}
