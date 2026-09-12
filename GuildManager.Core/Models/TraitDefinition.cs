using System.Collections.Generic;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// 特性の定義（カタログ）。個体ごとにパラメータが変わらない前提の静的定義。
    /// 冒険者は定義のIdを保持する（→ Adventurer.TraitIds、03 §5.3）。
    /// </summary>
    public class TraitDefinition
    {
        /// <summary>一意識別子。例："OldWound"。</summary>
        public string Id { get; set; } = "";

        /// <summary>表示名。例："古傷"。</summary>
        public string DisplayName { get; set; } = "";

        public List<TraitEffect> Effects { get; set; } = new();

        /// <summary>出撃を制限するか。古傷はfalse（出撃制限は課さない設計）。</summary>
        public bool BlocksDeployment { get; set; } = false;
    }
}
