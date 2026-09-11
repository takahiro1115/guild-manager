namespace GuildManager.Core.Models
{
    /// <summary>
    /// 職業。仕様書 03 §2.1 参照。
    /// 旧v1.0の「Scout」は顧問「Scout Master」との名称衝突を避けるため Ranger に改名済み。
    /// </summary>
    public enum JobClass
    {
        Warrior,
        Ranger,
        Mage,
        Cleric
    }
}
