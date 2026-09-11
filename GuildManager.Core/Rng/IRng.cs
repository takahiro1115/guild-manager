namespace GuildManager.Core.Rng
{
    /// <summary>
    /// 乱数の抽象化。Godotの乱数に依存させず、テストやリプレイで差し替え可能にする
    /// （→ 05 技術メモ §5「決定性・再現性」）。
    /// </summary>
    public interface IRng
    {
        /// <summary>min以上max以下（両端を含む）の整数を返す。</summary>
        int NextInt(int min, int max);
    }
}
