namespace GuildManager.Core.Models
{
    /// <summary>
    /// パーティ携行アイテム（消耗品ポーチ）の効果種別。「パーティ携行アイテム」刷新仕様参照。
    /// </summary>
    public enum ConsumableEffectType
    {
        /// <summary>環境ギミックの対策点数に+1する（→ ConsumableItem.CounterTag、GimmickEvaluator）。</summary>
        GimmickCounter,

        /// <summary>ダウン率（HP消費%）を半減させる（煙幕弾）。</summary>
        DownRateHalving,

        /// <summary>損耗（HP消費%）を固定量だけ軽減する（高品質傷薬）。</summary>
        DamageReduction,

        /// <summary>クエスト達成時、パーティ全員の満足度に固定加算する（携帯糧食）。</summary>
        SatisfactionBonus,
    }
}
