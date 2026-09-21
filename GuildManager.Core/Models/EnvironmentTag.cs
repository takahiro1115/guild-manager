namespace GuildManager.Core.Models
{
    /// <summary>
    /// 旧・通常クエストに設定されていた環境ギミック。仕様書「環境ギミック」刷新仕様参照。
    /// クエストは0件以上のタグを持ち、各タグごとにパーティの対策度合いが3段階
    /// （未充足/一部/完全）で評価される設計だった。
    ///
    /// **現状：** 旧通常クエストの撤去（→ 03 §0.8）で発生源が無くなり、評価器
    /// （`GimmickEvaluator`）・定義CSV（`gimmick.csv`）は 03 §0.12 で撤去済み。
    /// この列挙型は携行アイテムの相殺対象タグ（→ ConsumableItem.CounterTag）としてのみ
    /// 残っており、大迷宮のボスギミック（→ BossGimmickType）とは別物。
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
