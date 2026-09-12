namespace GuildManager.Core.Models
{
    /// <summary>
    /// 装備の効果種別。仕様書 03 §4.2.2 参照。
    /// 武器は常にPersonalCpBonus、防具は常にMaxHpBonusだが、アクセサリーは
    /// アイテムごとにどちらか一方を持つ（→ Item.EffectType）。
    /// </summary>
    public enum EquipmentEffectType
    {
        /// <summary>個人CPへの固定加算（→ 03 §4.2）。</summary>
        PersonalCpBonus,

        /// <summary>最大HPへの固定加算（VIT×2+50の式への加算、→ 03 §2.3）。</summary>
        MaxHpBonus,
    }
}
