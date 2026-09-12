using System.Collections.Generic;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// ゲーム内で使用可能な特性の静的カタログ。仕様書 03 §5.3・§4.3 参照。
    /// MVPでは「古傷」のみ登録し、不可逆障害の実装に使う。
    /// 「豪胆」「トラウマ」「師匠肌」等、他の特性はpost-MVPで追加していく。
    /// </summary>
    public static class TraitCatalog
    {
        public const string OldWoundId = "OldWound";

        /// <summary>
        /// 古傷（不可逆障害）。STR/VIT/AGI/DEXを15%（暫定値）ずつ恒久的に低下させる。
        /// MND/INT/LDRは対象外。出撃制限は課さない（→ 03 §4.3）。
        /// </summary>
        public static readonly TraitDefinition OldWound = new TraitDefinition
        {
            Id = OldWoundId,
            DisplayName = "古傷",
            BlocksDeployment = false,
            Effects = new List<TraitEffect>
            {
                // 減少率は暫定値。→ BAL: 戦闘/古傷減少率
                new TraitEffect { EffectType = TraitEffectType.StatPercentReduction, TargetStat = "STR", Value = -0.15 },
                new TraitEffect { EffectType = TraitEffectType.StatPercentReduction, TargetStat = "VIT", Value = -0.15 },
                new TraitEffect { EffectType = TraitEffectType.StatPercentReduction, TargetStat = "AGI", Value = -0.15 },
                new TraitEffect { EffectType = TraitEffectType.StatPercentReduction, TargetStat = "DEX", Value = -0.15 },
            }
        };

        // 将来ここに「頑強」「注意深い」等を追加していく（post-MVP）。

        public static TraitDefinition? FindById(string id) =>
            id == OldWoundId ? OldWound : null;
    }
}
