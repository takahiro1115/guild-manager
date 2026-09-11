namespace GuildManager.Core.Models
{
    /// <summary>
    /// ギルド格付け。仕様書 03 §8.1 参照。クエストランク（QuestRank）とは別の8段階体系
    /// （QuestRankにはG・Fが存在しないため、独立した enum として定義する）。
    /// 宣言順が昇順（G が最下位、S が最上位）であることに依存するコードがある
    /// （GuildRankBalance.NextRank/PreviousRank、比較演算子による昇格・降格判定）。
    /// </summary>
    public enum GuildRank
    {
        G,
        F,
        E,
        D,
        C,
        B,
        A,
        S
    }
}
