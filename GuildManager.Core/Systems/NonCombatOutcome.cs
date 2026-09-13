namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 探索（Exploration）・護衛（Escort）の判定区分。仕様書 03 §4.2.3 参照（項目63で新設）。
    ///
    /// 討伐（Subjugation）の4区分（→ CombatOutcome）とは別概念として扱う：
    /// こちらは3区分で、致死判定・古傷・戦死は発生せず、HP消費も軽量レンジになる。
    /// 大成功・成功は「達成」（WeekResolutionResult.QuestAchieved = true）、
    /// 失敗は「未達成」として、報酬等は討伐と同じ枠組みで扱う。
    /// </summary>
    public enum NonCombatOutcome
    {
        GreatSuccess, // 大成功（Ratio >= RatioThresholdGreatSuccess）
        Success,      // 成功（RatioThresholdSuccess <= Ratio < 大成功ライン）
        Failure       // 失敗（Ratio < RatioThresholdSuccess）
    }
}
