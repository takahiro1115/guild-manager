namespace GuildManager.Core.Models
{
    /// <summary>
    /// 階層ボスのギミック種別（→ ダンジョン攻略システム）。
    ///
    /// 設計メモ：旧・通常クエストの環境ギミック（→ EnvironmentTag。評価器は 03 §0.12 で撤去済み）とは
    /// **別体系**として新規に設ける（ユーザー決定）。両者の違い：
    ///  - EnvironmentTag：通常クエストの「場」に付く属性。ステータス合算と携行品で
    ///    3段階に緩和し、点数・索敵・損耗へ係数として効く（連続的）。
    ///  - BossGimmickType：階層ボス個体が持つ「対策の要否」。対策ロール・対策ステータス・
    ///    対策アイテムのいずれかを満たしているかの離散判定で、未対策なら被害が跳ね上がる。
    /// </summary>
    public enum BossGimmickType
    {
        /// <summary>猛毒：継続ダメージ。MND（状態異常耐性）または解毒アイテムで対策する。</summary>
        Poison,

        /// <summary>重装甲：物理が通りにくい。STR（装甲貫通）または魔法火力で対策する。</summary>
        HeavyArmor,

        /// <summary>飛行：地上からの攻撃が届かない。DEX（遠距離命中）等で対策する。</summary>
        Flying,

        /// <summary>即死級攻撃：対策していないと一撃で戦闘不能域まで持っていかれる。</summary>
        InstantKill,
    }
}
