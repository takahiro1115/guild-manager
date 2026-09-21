namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 迷宮調査（Survey）における解析判定の3区分（→ 03 §4.5.3、ScoutingResolver）。
    /// 解析Ratio（解析スコア÷要求値）を閾値で区切って決まり、区分に応じて解析率の
    /// 上昇量が変わる（→ BAL: scouting.csv `RatioThreshold*`・`IntelGain*`）。
    /// 隠密に失敗した週は1段階格下げされる（大成功→成功→失敗）。
    ///
    /// 旧称 `QuestEventOutcome`（旧通常クエストのランダムイベント3段階判定と共用していた
    /// 名残）。2026年9月、旧クエスト撤去後も迷宮調査だけが使い続けていた実態に合わせて改名した
    /// （→ 03 §0.13）。値・挙動は変わらない。
    /// </summary>
    public enum SurveyOutcome
    {
        /// <summary>大成功：解析率の上昇量が最も大きい。</summary>
        GreatSuccess,

        /// <summary>成功：標準の上昇量。</summary>
        Success,

        /// <summary>失敗：解析率は上がらない（または最小限）。</summary>
        Failure
    }
}
