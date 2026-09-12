namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 氏名ジェネレーター（NameGenerator）関連の暫定バランス値。仕様書 03 §2.4 参照。
    /// 05技術メモ§3の方針（数値を各Systemクラスへ直書きしない）に沿い、ここへ集約した。
    /// 現状は仮値（→ BAL: 採用/文化圏比率）。
    /// </summary>
    public static class NameGeneratorBalance
    {
        /// <summary>
        /// 文化圏の出現比率：洋名(Western)の出現率（%）。既存の初期メンバー・職業体系が
        /// 洋風中心であることを踏まえた暫定比率。残りは和名(Eastern)になる。
        /// </summary>
        public const int WesternCultureChancePercent = 80;

        /// <summary>性別の出現比率：男性(Male)の出現率（%）。男女50/50。</summary>
        public const int MaleGenderChancePercent = 50;

        /// <summary>重複回避のリトライ上限回数（→ NameGenerator.GenerateUniqueFirstName）。</summary>
        public const int UniqueNameRetryLimit = 50;
    }
}
