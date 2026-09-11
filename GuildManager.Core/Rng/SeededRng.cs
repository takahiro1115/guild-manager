using System;

namespace GuildManager.Core.Rng
{
    /// <summary>
    /// シードを保持する決定論的な乱数実装。
    /// セーブデータにSeedを保存しておけば、同じ乱数列を再現できる（→ 05 技術メモ §5）。
    /// </summary>
    public class SeededRng : IRng
    {
        private readonly Random _random;
        public int Seed { get; }

        public SeededRng(int seed)
        {
            Seed = seed;
            _random = new Random(seed);
        }

        public int NextInt(int min, int max) => _random.Next(min, max + 1);
    }
}
