namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 氏名ジェネレーター（NameGenerator）関連のバランス値。仕様書 03 §2.4 参照。
    /// 値は docs/04_バランス表/recruitment.csv から読み込む（→ 03 §10.1、項目58）。
    /// </summary>
    public static class NameGeneratorBalance
    {
        private const string FileName = "recruitment.csv";

        /// <summary>
        /// 文化圏の出現比率：洋名(Western)の出現率（%）。既存の初期メンバー・職業体系が
        /// 洋風中心であることを踏まえた比率。残りは和名(Eastern)になる。
        /// </summary>
        public static readonly int WesternCultureChancePercent = BalanceData.GetInt(FileName, "WesternCultureChancePercent");

        // 性別の出現比率（旧 MaleGenderChancePercent）は撤去した。冒険者は全員女性であり、
        // 設定値で男性を復活させられる余地そのものを構造から取り除いたため
        // （→ Models.Gender・Systems.NameGenerator のクラスdocコメント）。

        /// <summary>重複回避のリトライ上限回数（→ NameGenerator.GenerateUniqueFirstName）。</summary>
        public static readonly int UniqueNameRetryLimit = BalanceData.GetInt(FileName, "UniqueNameRetryLimit");
    }
}
