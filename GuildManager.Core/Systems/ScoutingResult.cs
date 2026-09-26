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
        public SurveyOutcome AnalysisOutcome { get; set; }

        /// <summary>今回の調査で上昇した解析率。</summary>
        public double IntelGained { get; set; }

        /// <summary>調査後のボスの解析率（0.0〜1.0）。</summary>
        public double IntelRateAfter { get; set; }

        /// <summary>調査後に到達した情報開示段階。</summary>
        public IntelTier TierAfter { get; set; }

        /// <summary>この調査で新たに段階が上がったか（UIで「新情報を持ち帰った」と強調するため）。</summary>
        public bool TierAdvanced { get; set; }

        /// <summary>護衛評価（→ GuardTier、2026年9月新設）。解析率上昇量の倍率とHP消費率を決める。</summary>
        public GuardTier GuardTier { get; set; }

        /// <summary>護衛比率＝部隊護衛力÷要求護衛値（週報・デバッグ用）。</summary>
        public double GuardRatio { get; set; }

        /// <summary>参謀の作戦分析による解析率上昇量への加算率（→ AdvisorSystem.GetAdvisorSurveyIntelBonus）。未任命なら0。</summary>
        public double AdvisorIntelBonus { get; set; }

        /// <summary>支援した参謀の名前（週報表示用）。未任命・ボーナス0ならnull。</summary>
        public string? AdvisorName { get; set; }

        // ---- 判定内訳の開示用（→ 03 §4.2.3「開発・バランス調整期間の特記事項」） ----

        /// <summary>部隊護衛力（→ ScoutingResolver.CalculateGuardPower）。</summary>
        public double GuardPower { get; set; }

        /// <summary>要求護衛値（→ ScoutingResolver.RequiredGuardPower）。</summary>
        public double GuardRequirement { get; set; }

        /// <summary>護衛力を担った隊員の名前（→ ScoutingResolver.FindGuardCarrier）。</summary>
        public string GuardCarrierName { get; set; } = "";

        /// <summary>護衛力を担った隊員の能力名（STR/VIT/INT）。</summary>
        public string GuardCarrierStat { get; set; } = "";

        /// <summary>主護衛の護衛値（→ ScoutingResolver.FindGuardCarrier の値。§0.41）。</summary>
        public double GuardCarrierValue { get; set; }

        /// <summary>主護衛以外の隊員による支援分（→ ScoutingResolver.CalculateGuardSupportPower。§0.41）。</summary>
        public double GuardSupportPower { get; set; }

        /// <summary>護衛段階による解析成果の倍率（→ ScoutingResolver.GuardIntelMultiplier）。</summary>
        public double GuardIntelMultiplier { get; set; }

        /// <summary>護衛段階による各員のHP消費率（%、→ ScoutingResolver.GuardHpLossPercent）。</summary>
        public int HpLossPercent { get; set; }

        /// <summary>隠密適性（→ ScoutingResolver.CalculateStealthScore）。</summary>
        public double StealthScore { get; set; }

        /// <summary>隠密の要求値（→ ScoutingResolver.StealthRequirement）。</summary>
        public double StealthRequirement { get; set; }

        /// <summary>解析スコア（→ ScoutingResolver.CalculateAnalysisScore）。</summary>
        public double AnalysisScore { get; set; }

        /// <summary>解析の要求値（→ ScoutingResolver.AnalysisRequirement）。</summary>
        public double AnalysisRequirement { get; set; }

        /// <summary>解析スコア÷解析の要求値。</summary>
        public double AnalysisRatio { get; set; }

        /// <summary>調査前の解析率。</summary>
        public double IntelRateBefore { get; set; }

        /// <summary>冒険者IDごとの、今回の調査で失ったHP量。</summary>
        public Dictionary<Guid, int> HpLostByAdventurer { get; set; } = new();
    }
}
