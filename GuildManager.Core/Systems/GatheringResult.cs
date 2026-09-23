using System;
using System.Collections.Generic;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 探索（採取）任務1回分の結果（→ GatheringResolver.Resolve）。UI側（Godot）はこれを読んで
    /// 週報ログへ表示する（ScoutingResult・TraversalResultと同じ役割）。
    /// </summary>
    public class GatheringResult
    {
        /// <summary>抽選で獲得した素材のId（→ Balance.MaterialBalance）。</summary>
        public string MaterialId { get; set; } = "";

        /// <summary>獲得した素材の個数。</summary>
        public int MaterialCount { get; set; }

        /// <summary>採取と並行して得た換金ゴールド（少量の一時金）。</summary>
        public int GoldEarned { get; set; }

        /// <summary>
        /// 今回の採取で掘り当てた未鑑定の古代遺物（→ 03 §4.7）。ドロップしなかった週はnull。
        /// GameState.UnidentifiedItemsへの反映は週次解決側が行う（→ DungeonExpeditionSystem.
        /// ResolveMission。GatheringResolver自体はGameStateを書き換えない設計のため）。
        /// </summary>
        public UnidentifiedItem? UnidentifiedItemFound { get; set; }

        /// <summary>冒険者IDごとの、今回の採取で失ったHP量。</summary>
        public Dictionary<Guid, int> HpLostByAdventurer { get; set; } = new();

        // ---- 判定内訳の開示用（→ 03 §4.2.3「開発・バランス調整期間の特記事項」） ----

        /// <summary>採取スコアの内訳（→ GatheringResolver.BreakDownGatheringScore）。</summary>
        public GatheringScoreBreakdown ScoreBreakdown { get; set; } = new(0, 0, 0, 0, 0, 1);

        /// <summary>採取スコア（→ GatheringResolver.CalculateGatheringScore）。</summary>
        public double Score { get; set; }

        /// <summary>獲得数の内訳：選ばれた素材の基礎量（→ materials.csv BaseYield）。</summary>
        public int BaseYield { get; set; }

        /// <summary>獲得数の内訳：採取スコア÷MaterialYieldDivisor（切り捨て）。</summary>
        public int ScoreYield { get; set; }

        /// <summary>獲得数の内訳：到達階層÷ReachedFloorDivisor（切り捨て）。</summary>
        public int FloorYield { get; set; }

        /// <summary>獲得数の内訳：研究（GatheringYieldBonus）による加算。</summary>
        public int ResearchYield { get; set; }

        /// <summary>未鑑定遺物の発見率（%、→ RelicBalance.GetGatheringDropPercent）。</summary>
        public int RelicDropPercent { get; set; }
    }
}
