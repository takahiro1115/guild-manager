using System;
using System.Collections.Generic;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 探索（採取）任務1回分の結果（→ GatheringResolver.Resolve）。UI側（Godot）はこれを読んで
    /// 週報ログへ表示する（ScoutingResult・TraversalResultと同じ役割）。
    /// </summary>
    public class GatheringResult
    {
        /// <summary>抽選で獲得した素材のId（→ Models.MaterialCatalog）。</summary>
        public string MaterialId { get; set; } = "";

        /// <summary>獲得した素材の個数。</summary>
        public int MaterialCount { get; set; }

        /// <summary>採取と並行して得た換金ゴールド（少量の一時金）。</summary>
        public int GoldEarned { get; set; }

        /// <summary>冒険者IDごとの、今回の採取で失ったHP量。</summary>
        public Dictionary<Guid, int> HpLostByAdventurer { get; set; } = new();
    }
}
