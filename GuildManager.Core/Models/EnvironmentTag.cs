namespace GuildManager.Core.Models
{
    /// <summary>
    /// クエストに設定される環境ギミック。仕様書「環境ギミック」刷新仕様参照。
    /// クエストは0件以上のタグを持ち（→ Quest.EnvironmentTags）、各タグごとに
    /// パーティの対策度合いが3段階（未充足/一部/完全）で評価される
    /// （→ Systems.GimmickEvaluator・Balance.GimmickBalance）。
    ///
    /// 各タグが影響するフェーズ・対象ステータス・相殺アイテムは gimmick.csv 側の定義
    /// （構造ではなく調整対象の数値のため）。ここでは「5種のギミックが存在する」という
    /// 構造そのものだけを定義する。
    /// </summary>
    public enum EnvironmentTag
    {
        /// <summary>瘴気：神官等のMND合算による浄化。未対策だと損耗（HP消費）が重くなる。</summary>
        Miasma,

        /// <summary>霊体：MND合算による浄化。未対策だとフェーズ2の点数が伸びにくい。</summary>
        Undead,

        /// <summary>暗黒：DEX合算による視界確保。未対策だとフェーズ1の索敵値が下がる。</summary>
        Darkness,

        /// <summary>隘路：AGI合算による身のこなし。未対策だとフェーズ2の点数が伸びにくい。</summary>
        NarrowPath,

        /// <summary>巨躯：STR合算による力押し。未対策だと損耗（HP消費）が重くなる。</summary>
        Colossal,
    }
}
