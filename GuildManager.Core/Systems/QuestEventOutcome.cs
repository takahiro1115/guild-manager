namespace GuildManager.Core.Systems
{
    /// <summary>
    /// ランダムイベントのうち、3段階で判定するもの（宝物庫の発見・深追い。→ 03 §4.2.3、項目65）の結果。
    ///
    /// 名前は探索・護衛の NonCombatOutcome と同じ3段階だが、**判定に使う閾値も対象も別物**の
    /// ため、型として分けている（イベント専用の閾値は → BAL: クエストイベント）。
    /// </summary>
    public enum QuestEventOutcome
    {
        GreatSuccess,
        Success,
        Failure
    }
}
