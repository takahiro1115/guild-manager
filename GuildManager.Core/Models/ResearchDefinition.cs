using System.Collections.Generic;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// 研究の効果種別（→ アルベールの研究室、素材投資システム）。
    /// EffectValueの意味は種別ごとに異なる（→ 各Resolver・System側のコメント参照）。
    /// </summary>
    public enum ResearchEffectType
    {
        /// <summary>
        /// 静養時のHP自然回復量への加算倍率（→ Systems.RestRecoverySystem）。
        /// EffectValue（例：0.15）は「+15%」を意味する（既存の回復量に対する乗算の加算分）。
        /// </summary>
        HpRecoveryBonus,

        /// <summary>
        /// ボス調査（解析）時の解析率獲得量への加算倍率（→ Systems.ScoutingResolver）。
        /// EffectValue（例：0.25）は「+25%」を意味し、HpRecoveryBonusと同じ考え方で
        /// (1+EffectValue) を今回の解析率上昇量に掛ける。
        /// </summary>
        IntelRateBonus,

        /// <summary>
        /// 採取任務での素材獲得数への加算（→ Systems.GatheringResolver）。
        /// EffectValue（例：1）は獲得数へそのまま加算する（整数として扱う）。
        /// </summary>
        GatheringYieldBonus,

        /// <summary>
        /// 道中進軍の走破力スコアへの加算（→ Systems.DungeonTraversalResolver）。
        /// EffectValue（例：15）はスコア計算の最後に加算する。
        /// </summary>
        TraversalBonus,
    }

    /// <summary>
    /// 研究1件分の定義（→ アルベールの研究室、素材投資システム）。値は
    /// docs/04_バランス表/research.csv から読み込む（→ Balance.ResearchBalance）。
    ///
    /// 採取した素材（→ GameState.Materials）とゴールドを投じて実行する、恒久的な
    /// インフラバフ。一度完了すると取り消せない（→ GameState.CompletedResearchIds）。
    /// </summary>
    public class ResearchDefinition
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";

        /// <summary>必要素材（素材Id→必要個数）。複数種類を要求できる。</summary>
        public Dictionary<string, int> RequiredMaterials { get; init; } = new();

        public int RequiredGold { get; init; }

        public ResearchEffectType EffectType { get; init; }

        /// <summary>効果量。意味は EffectType ごとに異なる（→ ResearchEffectTypeの各コメント）。</summary>
        public float EffectValue { get; init; }
    }

    /// <summary>
    /// 各解決エンジンが参照する研究Idの定数（→ ScoutingResolver・GatheringResolver・
    /// DungeonTraversalResolver・RestRecoverySystem）。マジックストリングを1箇所へ集約する。
    /// 対応する定義の詳細（必要素材・効果量）はresearch.csv側で管理する。
    /// </summary>
    public static class ResearchIds
    {
        /// <summary>生体蛍光試薬：ボス調査の解析率獲得量にボーナス（→ IntelRateBonus）。</summary>
        public const string ScoutReagent = "res_scout_reagent";

        /// <summary>特殊保存嚢：採取任務の素材獲得数にボーナス（→ GatheringYieldBonus）。</summary>
        public const string GatheringBag = "res_gathering_bag";

        /// <summary>軽量踏破靴：道中進軍の走破力スコアにボーナス（→ TraversalBonus）。</summary>
        public const string LightTread = "res_light_tread";

        /// <summary>薬草湿布の調合：静養時のHP自然回復量にボーナス（→ HpRecoveryBonus）。</summary>
        public const string HerbPoultice = "res_herb_poultice";
    }
}
