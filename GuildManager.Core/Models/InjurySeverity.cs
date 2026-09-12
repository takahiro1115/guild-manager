namespace GuildManager.Core.Models
{
    /// <summary>
    /// 負傷状態。仕様書 03 §2.3 参照。一時的な負傷のみを表す3値。
    /// 不可逆障害（古傷）は専用の値を持たず、汎用の特性(Trait)エンジン上の
    /// 「古傷」特性として実装する（→ Adventurer.TraitIds、03 §4.3・§5.3）。
    /// 戦死は GameState.FallenAdventurers への記録として扱う（→ 03 §4.3.1）。
    /// </summary>
    public enum InjurySeverity
    {
        None,
        Light,
        Severe
    }
}
