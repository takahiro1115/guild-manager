namespace GuildManager.Core.Models
{
    /// <summary>
    /// 装備スロット。仕様書 03 §4.2.2 参照。冒険者1人につき各1枠（計4枠）。
    /// Accessory1・Accessory2は別々の物理スロットであり、Itemの`Slot`が
    /// どちらに対応するかを個別に指定する（アイテム側でスロットを固定する設計）。
    /// </summary>
    public enum EquipmentSlot
    {
        Weapon,
        Armor,
        Accessory1,
        Accessory2,
    }
}
