using System;
using System.Collections.Generic;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 調査任務1回分の結果（→ ScoutingResolver.Resolve）。
    /// UI側（Godot）はこれを読んで週報ログへ表示する（WeekResolutionResultと同じ役割）。
    /// </summary>
    public class ScoutingResult
    {
        /// <summary>隠密に成功したか（false＝見つかって手傷を負った）。</summary>
        public bool StealthSucceeded { get; set; }

        /// <summary>解析判定の区分（大成功／成功／失敗）。既存のイベント判定と同じ3区分を流用する。</summary>
        public QuestEventOutcome AnalysisOutcome { get; set; }

        /// <summary>今回の調査で上昇した解析率。</summary>
        public double IntelGained { get; set; }

        /// <summary>調査後のボスの解析率（0.0〜1.0）。</summary>
        public double IntelRateAfter { get; set; }

        /// <summary>調査後に到達した情報開示段階。</summary>
        public IntelTier TierAfter { get; set; }

        /// <summary>この調査で新たに段階が上がったか（UIで「新情報を持ち帰った」と強調するため）。</summary>
        public bool TierAdvanced { get; set; }

        /// <summary>冒険者IDごとの、今回の調査で失ったHP量。</summary>
        public Dictionary<Guid, int> HpLostByAdventurer { get; set; } = new();
    }
}
