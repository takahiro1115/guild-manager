using System.Collections.Generic;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 成長トリガー（GrowthSystem）関連の暫定バランス値。仕様書 03 §3.1〜3.4 参照。
    ///
    /// 05技術メモ§3の方針（数値をコードに直書きしない）に沿い、GrowthSystem本体へ
    /// 定数を散らさずここへ集約した。ただし 04_バランス表.xlsx からの読み込みへの
    /// 置き換え（Phase 4での外部化）はまだ行っておらず、現状はすべて仮値の定数。
    /// </summary>
    public static class GrowthBalance
    {
        // ---- 年齢帯別 成長ロール基礎確率（→ BAL: 加齢/年齢帯別基礎確率。現状は仮値） ----
        // 成長期は基準の1.5倍、以降の年齢帯で逓減する設計（仕様書 03 §3.0）。
        private const double BaselineProbability = 0.20;
        private const double GrowthPeriodMultiplier = 1.5;
        private const double PrimePeriodProbability = 0.12;
        private const double MaturePeriodProbability = 0.05;
        private const double LimitPeriodProbability = 0.02;

        /// <summary>年齢帯別の成長ロール基礎確率（0.0〜1.0）。</summary>
        public static double GetBaseProbability(AgeBand band) => band switch
        {
            AgeBand.GrowthPeriod => BaselineProbability * GrowthPeriodMultiplier,
            AgeBand.PrimePeriod => PrimePeriodProbability,
            AgeBand.MaturePeriod => MaturePeriodProbability,
            AgeBand.LimitPeriod => LimitPeriodProbability,
            _ => 0.0
        };

        // ---- 経路1（出撃）：クエスト難易度による成長係数。→ BAL: 加齢/難易度成長係数。現状は仮値 ----
        // 難易度0で1.0倍・難易度100で2.0倍の線形補間（難しいクエストほど成長機会が増える）。
        public static double GetDifficultyCoefficient(int questDifficulty) => 1.0 + questDifficulty / 100.0;

        // TODO(→ 03 §6 施設・インフラ拡張): 訓練場Lvに連動させる。現状は常に1.0倍固定。
        public const double TrainingFacilityMultiplier = 1.0;

        // ---- 成長量（実効値の上昇幅）。→ BAL: 加齢/成長量。現状は仮値 ----
        public const int MinGrowthAmount = 1;
        public const int MaxGrowthAmount = 3;

        // ---- 経路1（出撃）のステータス抽選：職業ごとの簡易重み付け。
        // §4.2の列システム（前衛/後衛）は未実装のため、現時点では職業のみに基づく簡易重み
        // （仕様書 03 §2.1 の各職業の特性説明を反映した仮値。→ BAL: 加齢/職業別成長重み）。
        // 前衛(Warrior)はSTR/AGI/VIT寄り、斥候(Ranger)はAGI/DEX寄り、魔導士(Mage)はMND/INT寄り、
        // 神官(Cleric)はMND/LDR寄り。
        // v1.2改訂：INTを予約フィールドから活性化。魔導士のみ成長対象プールにINTを追加する
        // （他職はINTを重み付け対象に含めない＝経路1では成長しない。→ 03 §3.1〜3.4）。
        private static readonly Dictionary<JobClass, (string Stat, int Weight)[]> JobStatWeights = new()
        {
            [JobClass.Warrior] = new[] { ("STR", 3), ("AGI", 1), ("VIT", 3), ("MND", 0), ("DEX", 1), ("LDR", 1) },
            [JobClass.Ranger] = new[] { ("STR", 1), ("AGI", 3), ("VIT", 1), ("MND", 0), ("DEX", 3), ("LDR", 1) },
            [JobClass.Mage] = new[] { ("STR", 0), ("AGI", 1), ("VIT", 1), ("MND", 3), ("DEX", 1), ("LDR", 1), ("INT", 2) },
            [JobClass.Cleric] = new[] { ("STR", 0), ("AGI", 1), ("VIT", 1), ("MND", 2), ("DEX", 1), ("LDR", 2) },
        };

        /// <summary>職業別の重みに従って、成長対象ステータスを1つ抽選する。</summary>
        public static string PickJobWeightedStat(JobClass jobClass, IRng rng)
        {
            var weights = JobStatWeights[jobClass];

            int total = 0;
            foreach (var (_, weight) in weights)
                total += weight;

            int roll = rng.NextInt(1, total);
            int cumulative = 0;
            foreach (var (stat, weight) in weights)
            {
                cumulative += weight;
                if (roll <= cumulative)
                    return stat;
            }

            return weights[^1].Stat; // 理論上到達しないフォールバック
        }
    }
}
