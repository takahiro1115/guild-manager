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

        /// <summary>
        /// 教官からの週次伝授（→ 特性伝授刷新仕様・TrainingSystem.ProcessWeeklyTraitTransmission）
        /// の対象になりうるか。古傷・トラウマ等の後天的な障害特性はfalse（伝授で広まってしまうのは
        /// 不自然なため）。既定はfalse＝明示的にtrueにした特性のみ伝授対象になる。
        /// </summary>
        public bool IsTransmittable { get; set; } = false;
    }
}
