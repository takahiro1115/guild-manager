using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 週次決算でギルド格付けが変化したことを表す結果クラス（→ 03 §8.1）。
    /// UI側が System 層に依存させずにログ表示できるようにするための結果キャリア
    /// （GrowthEvent・DispatchResolutionと同じパターン）。
    /// </summary>
    public class GuildRankChangeEvent
    {
        public GuildRank Previous { get; }
        public GuildRank Current { get; }

        /// <summary>true なら昇格、false なら降格。</summary>
        public bool IsPromotion => Current > Previous;

        public GuildRankChangeEvent(GuildRank previous, GuildRank current)
        {
            Previous = previous;
            Current = current;
        }
    }
}
