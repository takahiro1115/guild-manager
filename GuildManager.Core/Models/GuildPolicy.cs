using System;

namespace GuildManager.Core.Models
{
    /// <summary>ギルドの方針：訓練の強さ（段4f、§0.105）。</summary>
    public enum TrainingPolicy
    {
        /// <summary>ふつう（今までどおり）。</summary>
        Normal,
        /// <summary>厳しく：訓練の成長が上がるが、訓練生がけがをしやすく、満足度が下がる。</summary>
        Hard,
        /// <summary>穏やかに：訓練の成長が下がるが、訓練生の満足度が上がる。</summary>
        Gentle,
    }

    /// <summary>ギルドの方針：大会の賞金の分け方（§0.105）。</summary>
    public enum PrizePolicy
    {
        /// <summary>ギルドが取る（今までどおり、賞金は全部ギルドへ）。</summary>
        Guild,
        /// <summary>本人に分ける：賞金の一部を入賞した本人へ渡し、満足度が上がる。</summary>
        Share,
    }

    /// <summary>個人特訓の契約1件（§0.105、→ GameState.Coachings）。外部の講師が1人に1季節つき、毎週の成長の判定を上乗せする。</summary>
    public class CoachingContract
    {
        public Guid AdventurerId { get; set; }
        public int StartWeek { get; set; }
        /// <summary>この週の決算まで特訓が続く。</summary>
        public int UntilWeek { get; set; }
    }
}
