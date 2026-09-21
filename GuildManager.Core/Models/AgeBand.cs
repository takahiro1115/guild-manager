namespace GuildManager.Core.Models
{
    /// <summary>
    /// 年齢帯。仕様書 03 §3.0「年齢帯の統一定義」参照。
    /// 境界は Adventurer.AgeBand で判定する（〜18 / 19〜22 / 23〜）。
    ///
    /// v2.0の8年稼働モデル（18歳固定加入・26歳満期引退・加齢衰微の完全廃止）により、
    /// 旧モデルの円熟期（28〜34）・限界期（35〜40）は到達不能になったため撤去した
    /// （→ 03 §0.11）。現役として存在しうるのは以下の3区分のみ。
    /// </summary>
    public enum AgeBand
    {
        /// <summary>新鋭期（〜18）：加入初年度。成長ロール基礎確率が最も高い。</summary>
        Young,

        /// <summary>成長期（19〜22）：育成の主な窓。</summary>
        Growing,

        /// <summary>全盛期（23〜）：上下動が最も安定。昇給要求が発生しやすい。満期引退（26歳）まで。</summary>
        Peak
    }
}
