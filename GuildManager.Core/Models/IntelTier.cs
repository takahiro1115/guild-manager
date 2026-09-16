namespace GuildManager.Core.Models
{
    /// <summary>
    /// 階層ボスについて現在どこまで判明しているか（→ FloorBoss.IntelRate から導出）。
    /// 調査任務を重ねるほど上の段階へ進む（→ Systems.ScoutingResolver.GetTier）。
    ///
    /// 情報公開の原則（→ 03 §4.2・コミットd7c7f39）との関係：
    /// ここで開示するのは「何が起きるか」という定性的な事実（猛毒がある・重装甲である等）であり、
    /// 判定用の内部数値（Ratio・閾値・成功率）ではない。解析率そのものの数値も
    /// UIへは段階表現として提示する想定。
    /// </summary>
    public enum IntelTier
    {
        /// <summary>未調査：名前と階層しか分からない。突撃は無謀。</summary>
        Unknown,

        /// <summary>基本情報：おおよその規模（HP）と属性が分かる。</summary>
        Basic,

        /// <summary>危険情報：どの危険ギミックを持つか（猛毒・重装甲など）が判明。</summary>
        Hazards,

        /// <summary>対策情報：各ギミックへの対策ロール・必要アイテムまで判明。</summary>
        Countermeasures,

        /// <summary>完全解析：弱点を突けるようになり、討伐時の与ダメージにボーナスが付く。</summary>
        Complete,
    }
}
