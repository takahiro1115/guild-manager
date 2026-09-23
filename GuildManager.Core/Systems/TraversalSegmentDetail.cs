using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 道中進軍の1区間ぶんの内訳（→ TraversalResult.Segments、DungeonTraversalResolver.DescribeSegments）。
    /// 区間担当ボス（→ DungeonField.GetSegmentBoss）と未踏破かどうかが同じ、連続した階層のまとまり。
    /// </summary>
    public class TraversalSegmentDetail
    {
        /// <summary>区間の出発階層（この階層から歩き出す）。</summary>
        public int FromFloor { get; set; }

        /// <summary>区間の到達階層（この区間で最後に踏み入れた階層）。</summary>
        public int ToFloor { get; set; }

        /// <summary>この区間で歩いた階層数。</summary>
        public int Floors { get; set; }

        /// <summary>区間担当ボス。最深部より先などで担当がいない場合はnull。</summary>
        public FloorBoss? Boss { get; set; }

        /// <summary>区間担当ボスの解析率（0.0〜1.0）。</summary>
        public double IntelRate { get; set; }

        /// <summary>この区間の階層が未踏破（進軍前の最高到達階層より深い）か。</summary>
        public bool IsUnexplored { get; set; }

        /// <summary>この区間の走破倍率（1.0〜3.0）。</summary>
        public double SpeedMultiplier { get; set; }

        /// <summary>この区間の被ダメージ倍率（完全解析なら0.3、それ以外1.0）。</summary>
        public double DamageMultiplier { get; set; }

        /// <summary>この区間の基礎損耗率（部隊平均の%。プレビューでは0）。</summary>
        public double BaseLossPct { get; set; }

        /// <summary>この区間の実効損耗率＝基礎損耗率×被ダメージ倍率（歩いた階層すべてがこの区間だった場合の%）。</summary>
        public double EffectiveLossPct => BaseLossPct * DamageMultiplier;
    }
}
