using System;
using System.Collections.Generic;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// ステータス成長トリガー。仕様書 03 §3.1〜3.4（成長トリガーの新規定義部分）参照。
    ///
    /// 成長は「出撃した週」（経路1）と「訓練場に配置されている週」（経路2）の
    /// 2経路のみで発生する。単純待機（どちらにも該当しない）では実効値は変化しない
    /// （HPの自然回復・待機回復は §3.5改 が別途担当。ステータス成長とは独立した軸）。
    /// EXP・Lvのような中間概念は導入せず、実効値が週ごとの成長ロールで直接増減する。
    ///
    /// 加齢による恒久的な衰微・年齢進行・強制引退は引き続き AgingSystem が担当する
    /// （このクラスは「伸びる」側のみを扱う。§3.1〜3.4 参照）。
    /// </summary>
    public class GrowthSystem
    {
        private readonly IRng _rng;

        public GrowthSystem(IRng rng)
        {
            _rng = rng;
        }

        /// <summary>
        /// 経路1：出撃による成長。今週派遣されたパーティの全員に成長ロールを適用する
        /// （クエストの成否は問わない。§3.1〜3.4「出撃した週」）。
        /// 実際に成長した分だけ GrowthEvent として返す（UI側の週報表示用）。
        /// </summary>
        public List<GrowthEvent> ProcessDeploymentGrowth(Party party, Quest quest)
        {
            double difficultyCoefficient = GrowthBalance.GetDifficultyCoefficient(quest.Difficulty);
            var events = new List<GrowthEvent>();

            foreach (var member in party.Members)
            {
                var growthEvent = TryGrowOne(member, difficultyCoefficient, JobWeightedStat);
                if (growthEvent != null)
                    events.Add(growthEvent);
            }

            return events;
        }

        /// <summary>
        /// 経路2：訓練場配置による成長。今週出撃しておらず、かつ訓練場に配置されている者のみ対象
        /// （出撃した週は経路1側で処理済みのため二重成長させない）。
        /// 実際に成長した分だけ GrowthEvent として返す（UI側の週報表示用）。
        /// </summary>
        public List<GrowthEvent> ProcessTrainingGrowth(GameState state, IReadOnlySet<Guid> dispatchedAdventurerIds)
        {
            var events = new List<GrowthEvent>();

            foreach (var adventurer in state.Adventurers)
            {
                if (dispatchedAdventurerIds.Contains(adventurer.Id))
                    continue; // 出撃者は経路1側で処理済み・二重成長なし

                if (!state.TrainingAssignments.Contains(adventurer.Id))
                    continue; // 訓練場未配置＝単純待機（成長ロールなし）

                var growthEvent = TryGrowOne(adventurer, GrowthBalance.TrainingFacilityMultiplier, UniformStat);
                if (growthEvent != null)
                    events.Add(growthEvent);
            }

            return events;
        }

        private GrowthEvent? TryGrowOne(Adventurer adventurer, double multiplier, Func<Adventurer, string> pickStat)
        {
            double probability = GrowthBalance.GetBaseProbability(adventurer.AgeBand) * multiplier;
            int roll = _rng.NextInt(1, 100);
            if (roll > (int)Math.Round(probability * 100))
                return null;

            string stat = pickStat(adventurer);
            int amount = _rng.NextInt(GrowthBalance.MinGrowthAmount, GrowthBalance.MaxGrowthAmount);

            int before = AdventurerStatAccessor.GetStat(adventurer, stat);
            int pa = AdventurerStatAccessor.GetPa(adventurer, stat);
            int after = Math.Min(pa, before + amount); // PAクランプ＝成長打ち止め（§3.1〜3.4）
            AdventurerStatAccessor.SetStat(adventurer, stat, after);

            if (after == before)
                return null; // 既にPA上限で実質変化なし＝報告しない

            return new GrowthEvent(adventurer, stat, before, after);
        }

        private string JobWeightedStat(Adventurer adventurer) =>
            GrowthBalance.PickJobWeightedStat(adventurer.JobClass, _rng);

        // 経路2は全ステータスから均等抽選。教官（Trainer、§7）配置時の加重補正は未実装のため
        // TODO(→ 03 §7 顧問制度): 教官の得意ステータスへの当選率加重を実装する。
        private string UniformStat(Adventurer adventurer) =>
            AdventurerStatAccessor.AllStatNames[_rng.NextInt(0, AdventurerStatAccessor.AllStatNames.Length - 1)];
    }
}
