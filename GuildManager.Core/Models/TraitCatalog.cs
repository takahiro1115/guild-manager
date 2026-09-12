using System.Collections.Generic;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// ゲーム内で使用可能な特性の静的カタログ。仕様書 03 §5.3・§4.3 参照。
    /// v1.4改訂：古傷（後天的）に加え、トラウマ（後天的）・豪胆／注意深い／容姿秀麗
    /// （いずれも先天的）を追加した（→ 03 §5.3.2）。「頑強」「師匠肌」等は引き続き
    /// post-MVP（→ §11）。
    /// </summary>
    public static class TraitCatalog
    {
        public const string OldWoundId = "OldWound";
        public const string TraumaId = "Trauma";
        public const string BraveId = "Brave";
        public const string AttentiveId = "Attentive";
        public const string BeautifulId = "Beautiful";

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

        /// <summary>
        /// トラウマ（後天的）。戦死の現場に居合わせた生存者に確率で付与される（→ 03 §5.1・§5.3.1）。
        /// MNDを15%（暫定値）恒久的に低下させる。
        /// </summary>
        public static readonly TraitDefinition Trauma = new TraitDefinition
        {
            Id = TraumaId,
            DisplayName = "トラウマ",
            BlocksDeployment = false,
            Effects = new List<TraitEffect>
            {
                new TraitEffect { EffectType = TraitEffectType.StatPercentReduction, TargetStat = "MND", Value = -0.15 } // → BAL: 特性/トラウマ減少率
            }
        };

        /// <summary>
        /// 豪胆（先天的）。新人採用時に低確率で付与される。致死判定のSurvivalThreshold
        /// （→ 03 §4.3）に固定ボーナスを与える。
        /// </summary>
        public static readonly TraitDefinition Brave = new TraitDefinition
        {
            Id = BraveId,
            DisplayName = "豪胆",
            BlocksDeployment = false,
            Effects = new List<TraitEffect>
            {
                new TraitEffect { EffectType = TraitEffectType.SurvivalThresholdModifier, TargetStat = "", Value = 5 } // → BAL: 特性/豪胆ボーナス
            }
        };

        /// <summary>
        /// 注意深い（先天的）。新人採用時に低確率で付与される。索敵フェーズ（→ 03 §4.1）の
        /// DEX寄与を+10%（暫定値）する。
        /// </summary>
        public static readonly TraitDefinition Attentive = new TraitDefinition
        {
            Id = AttentiveId,
            DisplayName = "注意深い",
            BlocksDeployment = false,
            Effects = new List<TraitEffect>
            {
                new TraitEffect { EffectType = TraitEffectType.ScoutingModifier, TargetStat = "DEX", Value = 0.10 } // → BAL: 特性/注意深い補正
            }
        };

        /// <summary>
        /// 容姿秀麗（先天的）。新人採用時に低確率で付与される。このキャラクターが関与する
        /// 相性ペアの上昇量に倍率がかかる（下降量には影響しない。→ 03 §5.3.1）。
        /// </summary>
        public static readonly TraitDefinition Beautiful = new TraitDefinition
        {
            Id = BeautifulId,
            DisplayName = "容姿秀麗",
            BlocksDeployment = false,
            Effects = new List<TraitEffect>
            {
                new TraitEffect { EffectType = TraitEffectType.CompatibilityGainMultiplier, TargetStat = "", Value = 1.5 } // → BAL: 特性/容姿秀麗倍率
            }
        };

        // 将来ここに「頑強」「師匠肌」等を追加していく（post-MVP）。

        public static TraitDefinition? FindById(string id) => id switch
        {
            OldWoundId => OldWound,
            TraumaId => Trauma,
            BraveId => Brave,
            AttentiveId => Attentive,
            BeautifulId => Beautiful,
            _ => null,
        };
    }
}
