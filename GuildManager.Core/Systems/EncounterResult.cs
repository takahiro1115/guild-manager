namespace GuildManager.Core.Systems
{
    /// <summary>
    /// フェーズ1（索敵・遭遇判定）の結果。仕様書 03 §4.1 参照。
    /// </summary>
    public enum EncounterResult
    {
        Surprise,   // 奇襲成功
        Normal,     // 通常交戦
        Ambushed    // 不意打ち
    }
}
