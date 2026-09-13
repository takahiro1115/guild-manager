namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 「強敵との遭遇」イベント（→ 03 §4.2.3、項目65）の3値判定。探索・護衛のみで発生する。
    ///
    /// 押し切り・苦戦の場合は、その場の物理的な結果として討伐フローのHP消費レンジ・
    /// 致死判定（生存／古傷／戦死）をそのまま流用する。退避の場合は通常の軽量HP消費のまま。
    /// いずれの場合も、探索・護衛としての成否（大成功／成功／失敗）自体は変更しない。
    /// </summary>
    public enum StrongEnemyOutcome
    {
        /// <summary>押し切った（押し切りスコアが遭遇要求値に到達）。辛勝相当の損害＋追加報酬。</summary>
        PushedThrough,

        /// <summary>退避した（押し切れなかったが退避スコアが到達）。損害は通常どおり。</summary>
        Evaded,

        /// <summary>苦戦した（いずれの閾値にも届かず）。苦戦敗退相当の損害。追加報酬なし。</summary>
        Struggled
    }
}
