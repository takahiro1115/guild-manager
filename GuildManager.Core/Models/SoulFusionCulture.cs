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

    /// <summary>秘薬の両親の血の近さ（§0.133、→ Systems.SoulFusionSystem.KinshipOf）。近いほど娘の素質が下がる。</summary>
    public enum Kinship
    {
        /// <summary>血のつながりが無い（2代さかのぼって同じ人がいない）。</summary>
        None,
        /// <summary>やや近い：片親が同じ姉妹・祖母と孫・おばと姪・いとこ。</summary>
        Near,
        /// <summary>近い：親子・同じ両親の姉妹。</summary>
        Close,
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

        /// <summary>いま選んでいる育て方（§0.118）。季節（NurtureSeasonWeeks）ごとに1回反映する。旧セーブ・選んでいなければ「まかせる」。</summary>
        public NurtureKind Nurture { get; set; } = NurtureKind.Entrust;

        /// <summary>これまでに反映した育て方（古い順）。</summary>
        public List<NurtureKind> NurtureHistory { get; set; } = new();
    }

    /// <summary>培養槽の娘の育て方（§0.118、→ Systems.SoulFusionSystem）。</summary>
    public enum NurtureKind
    {
        /// <summary>アルベールにまかせる：全能力を少しずつ。</summary>
        Entrust,
        /// <summary>体を鍛える：STR・VIT・AGI。</summary>
        Body,
        /// <summary>技を磨く：DEX・INT。</summary>
        Skill,
        /// <summary>心を育てる：MND・LDR。</summary>
        Heart,
        /// <summary>母に会わせる：母2人との相性・母の満足度。</summary>
        Mothers,
        /// <summary>外の世界を見せる：主志向・気質の見立て。</summary>
        World,
    }
}
