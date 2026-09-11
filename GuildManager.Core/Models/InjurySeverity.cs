namespace GuildManager.Core.Models
{
    /// <summary>
    /// 負傷状態。仕様書 03 §2.3 参照。
    /// MVPでは None / Light / Severe のみ実装。Permanent（不可逆障害）と戦死判定は
    /// Phase 3「不可逆障害・戦死」で追加する（→ docs/06_タスクリスト.md）。
    /// </summary>
    public enum InjurySeverity
    {
        None,
        Light,
        Severe
    }
}
