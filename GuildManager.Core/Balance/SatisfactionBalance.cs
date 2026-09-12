namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 満足度・契約交渉関連の暫定バランス値。仕様書 03 §5.1・§5.2 参照。
    /// 05技術メモ§3の方針（数値を各Systemクラスへ直書きしない）に沿い、ここへ集約した。
    /// 04_バランス表.xlsx からの読み込みへの置き換え（Phase 4での外部化）はまだ行っておらず、
    /// 現状はすべて仮値の定数。
    /// </summary>
    public static class SatisfactionBalance
    {
        public const int Min = 0;
        public const int Max = 100;

        // ---- 出場機会（§5.1）。対象年齢帯は22〜27歳（全盛期）固定（仕様書どおり）。 ----
        public const int NoDeploymentWeeksThreshold = 4;
        public const int NoDeploymentPenalty = 5;

        // ---- 賃金妥当性（§5.1）。→ BAL: 満足度/賃金妥当性。現状は仮値 ----
        public const double WageAdequacyRatio = 0.8;
        public const int UnderpaidPenalty = 8;

        /// <summary>
        /// 適正週給の算出係数（総合PA×この値）。
        /// TODO(→ 03 §8 勝敗判定条件・格付け): 本来は「実力ランク」から適正週給を
        /// 算出すべきだが、ギルド格付け・冒険者ランクシステムが未実装のため、
        /// 総合PAを実力の代理指標として使う（→ BAL: 経済/週給基準）。
        /// </summary>
        public const double AppropriateWageCoefficient = 0.6;

        // ---- 勝利・功績（§5.1）。Bランク以上のクエスト達成が対象。 ----
        public const int VictoryBonus = 10;

        /// <summary>仲間ロストの余波（§5.1）。→ 03 §4.3の致死判定（戦死）に接続済み。</summary>
        public const int PartyLossPenalty = 30;

        /// <summary>
        /// 人間関係：相性「険悪」（→ 03 §5.3.1、CompatibilityBalance.HostileThreshold未満）の
        /// ペアと同パーティで出撃した週、毎週この分だけ減点する（v1.5からの保留を解消）。
        /// 1人が複数の険悪ペアに同時に該当する場合は、その件数分だけ加算される。
        /// </summary>
        public const int HostilePairPenalty = 10;

        // ---- 契約交渉（§5.2） ----
        public const int NegotiationThreshold = 20;
        public const int NegotiationGraceWeeks = 2;

        /// <summary>昇給倍率の許容レンジ（週給1.5〜2.0倍提示、§5.2）。</summary>
        public const double MinRaiseMultiplier = 1.5;
        public const double MaxRaiseMultiplier = 2.0;

        /// <summary>一時金（ボーナス）＝週給のこの倍数。→ BAL: 満足度/契約交渉。現状は仮値。</summary>
        public const int BonusWeeksEquivalent = 8;
    }
}
