using System;
using System.Collections.Generic;
using System.Globalization;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 成長トリガー（GrowthSystem）関連のバランス値。仕様書 03 §3.1〜3.4 参照。
    /// 値は docs/04_バランス表/aging.csv（key,value形式）・growth_job_weights.csv
    /// （job別の重みテーブル）から読み込む（→ 03 §10.1、項目58）。
    /// </summary>
    public static class GrowthBalance
    {
        private const string AgingFileName = "aging.csv";
        private const string JobWeightsFileName = "growth_job_weights.csv";

        // ---- 年齢帯別 成長ロール基礎確率（→ BAL: 加齢/年齢帯別基礎確率） ----
        // 成長期は基準の1.5倍、以降の年齢帯で逓減する設計（仕様書 03 §3.0）。CSV側では
        // 既に係数を織り込んだ最終値（例：成長期0.30＝基準0.20×1.5）として持つ。
        private static readonly double GrowthPeriodProbability = BalanceData.GetDouble(AgingFileName, "GrowthProbability_GrowthPeriod");
        private static readonly double PrimePeriodProbability = BalanceData.GetDouble(AgingFileName, "GrowthProbability_PrimePeriod");
        private static readonly double MaturePeriodProbability = BalanceData.GetDouble(AgingFileName, "GrowthProbability_MaturePeriod");
        private static readonly double LimitPeriodProbability = BalanceData.GetDouble(AgingFileName, "GrowthProbability_LimitPeriod");

        /// <summary>年齢帯別の成長ロール基礎確率（0.0〜1.0）。</summary>
        public static double GetBaseProbability(AgeBand band) => band switch
        {
            AgeBand.GrowthPeriod => GrowthPeriodProbability,
            AgeBand.PrimePeriod => PrimePeriodProbability,
            AgeBand.MaturePeriod => MaturePeriodProbability,
            AgeBand.LimitPeriod => LimitPeriodProbability,
            _ => 0.0
        };

        // ---- 経路1（出撃）：クエスト難易度による成長係数。→ BAL: 加齢/難易度成長係数 ----
        // 難易度0でBase倍・難易度100でBase+Per100倍の線形補間（難しいクエストほど成長機会が増える）。
        private static readonly double DifficultyCoefficientBase = BalanceData.GetDouble(AgingFileName, "DifficultyCoefficientBase");
        private static readonly double DifficultyCoefficientPer100 = BalanceData.GetDouble(AgingFileName, "DifficultyCoefficientPer100");

        public static double GetDifficultyCoefficient(int questDifficulty) =>
            DifficultyCoefficientBase + questDifficulty / 100.0 * DifficultyCoefficientPer100;

        // TODO(→ 03 §6 施設・インフラ拡張): 訓練場Lvに連動させる。現状は常に固定倍率。
        public static readonly double TrainingFacilityMultiplier = BalanceData.GetDouble(AgingFileName, "TrainingFacilityMultiplier");

        // ---- 成長量（実効値の上昇幅）。→ BAL: 加齢/成長量 ----
        public static readonly int MinGrowthAmount = BalanceData.GetInt(AgingFileName, "MinGrowthAmount");
        public static readonly int MaxGrowthAmount = BalanceData.GetInt(AgingFileName, "MaxGrowthAmount");

        // ---- 経路1（出撃）のステータス抽選：職業ごとの重み付け（→ BAL: 加齢/職業別成長重み） ----
        // §4.2の列システム（前衛/後衛）は未実装のため、現時点では職業のみに基づく重み
        // （仕様書 03 §2.1 の各職業の特性説明を反映）。前衛(Warrior)はSTR/AGI/VIT寄り、
        // 斥候(Ranger)はAGI/DEX寄り、魔導士(Mage)はMND/INT寄り、神官(Cleric)はMND/LDR寄り。
        // v1.2改訂：INTを予約フィールドから活性化。魔導士のみ成長対象プールにINTを持つ
        // （他職はCSV上のINT重みが0＝経路1では成長しない。→ 03 §3.1〜3.4）。
        private static readonly Dictionary<JobClass, (string Stat, int Weight)[]> JobStatWeights = BuildJobStatWeights();

        private static Dictionary<JobClass, (string Stat, int Weight)[]> BuildJobStatWeights()
        {
            var (header, rows) = BalanceData.GetTable(JobWeightsFileName);
            // header[0]="job"、header[1..]がステータス名（STR,AGI,VIT,MND,DEX,LDR,INT）。
            var result = new Dictionary<JobClass, (string Stat, int Weight)[]>();

            foreach (var row in rows)
            {
                if (!Enum.TryParse<JobClass>(row[0], out var jobClass))
                    throw new BalanceDataException($"{JobWeightsFileName} のjob列「{row[0]}」をJobClassとして解釈できません。");

                var weights = new (string Stat, int Weight)[header.Length - 1];
                for (int col = 1; col < header.Length; col++)
                {
                    if (!int.TryParse(row[col], NumberStyles.Integer, CultureInfo.InvariantCulture, out int weight))
                        throw new BalanceDataException($"{JobWeightsFileName} の{row[0]}行・{header[col]}列の値「{row[col]}」を整数として解釈できません。");
                    weights[col - 1] = (header[col], weight);
                }
                result[jobClass] = weights;
            }

            return result;
        }

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
