namespace GuildManager.Core.Systems
{
    /// <summary>
    /// フェーズ2（戦闘比率）の結果。仕様書 03 §4.2 の表を参照。
    /// </summary>
    public enum CombatOutcome
    {
        Victory,     // 完全勝利（Ratio >= 1.8）
        NarrowWin,   // 辛勝（1.0 <= Ratio < 1.8）
        Defeat,      // 苦戦敗退（0.6 <= Ratio < 1.0）
        Rout         // 戦線崩壊（Ratio < 0.6）
    }
}
