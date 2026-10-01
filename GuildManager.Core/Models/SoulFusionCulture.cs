using System;
using System.Collections.Generic;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// 百合相性（ニックス）の段階（→ Systems.SoulFusionSystem.GetNyxTier、03 §5.4・§0.58）。
    /// 両親の職業の組み合わせで決まり、能力限界突破の確率（→ soul_fusion.csv BreakthroughChance_*）を変える。
    /// </summary>
    public enum SoulFusionNyxTier
    {
        /// <summary>ふつう：下の2つ以外の組み合わせ。</summary>
        Ordinary,

        /// <summary>好一対：前衛の職業×後衛の職業（→ PlacementRules）。</summary>
        Complementary,

        /// <summary>運命の一対：重戦士か騎士×神官（アルベールが引き裂かれるのを見た、女勇者と女聖女の再現）。</summary>
        Destined,
    }

    /// <summary>
    /// 培養槽で育っている子1人分（→ GameState.SoulFusionCultures、03 §5.4・§0.58）。
    /// 子は処方した時点で生成し（能力値・特性・名前・顔が決まる）、培養が終わって宿舎に空きがある週の決算で
    /// ロースターへ加わる（→ SoulFusionSystem.ProcessWeeklyCultures）。プリミティブ・列挙・Adventurer だけで構成され、
    /// セーブへそのまま JSON 化できる。
    /// </summary>
    public class SoulFusionCulture
    {
        public Guid ParentAId { get; set; }
        public Guid ParentBId { get; set; }

        /// <summary>生まれてくる子（まだロースターにいない）。</summary>
        public Adventurer Child { get; set; } = new();

        /// <summary>誕生までの残り週数。0になっても宿舎に空きが無ければ、空くまで培養槽で待つ。</summary>
        public int WeeksRemaining { get; set; }

        /// <summary>投入した触媒の素材Id（null＝触媒なし）。</summary>
        public string? CatalystMaterialId { get; set; }

        public SoulFusionNyxTier NyxTier { get; set; }

        /// <summary>能力限界突破した能力名（"STR" など。突破しなければ空）。</summary>
        public List<string> BreakthroughStats { get; set; } = new();
    }
}
