using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// ステータス成長トリガー。仕様書 03 §3.1〜3.4（成長トリガーの新規定義部分）・
    /// §2.2新規（生涯ピーク値）・§6（v1.3改訂：訓練場・道場の4分割）・§7（教官ボーナス）参照。
    ///
    /// 成長は「出撃した週」（経路1）と「訓練施設に配置されている週」（経路2）の
    /// 2経路のみで発生する。単純待機（どちらにも該当しない）では実効値は変化しない
    /// （HPの自然回復・待機回復は §3.5改 が別途担当。ステータス成長とは独立した軸）。
    /// EXP・Lvのような中間概念は導入せず、実効値が週ごとの成長ロールで直接増減する。
    ///
    /// v1.3改訂：
    ///  - 経路2の対象ステータスは、配置先の訓練施設が扱うステータスに限定される
    ///    （戦士訓練所→STR・VIT、教会→MND、魔法研究所→INT、斥候所→AGI・DEX）。
    ///  - 配置先施設に教官が配置されていれば、その教官の対象ステータス実効値に
    ///    比例したボーナスが成長ロールの確率倍率に加算される（→ AdvisorSystem.GetTrainerBonus）。
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
        /// 経路2：訓練施設配置による成長。今週出撃しておらず、かつ訓練施設に配置されている者のみ対象
        /// （出撃した週は経路1側で処理済みのため二重成長させない）。
        /// v1.3改訂：対象ステータスは配置先施設が扱うものに限定し、配置先施設の教官がいれば
        /// その生涯ピーク値に比例したボーナスを成長ロール確率に加算する（→ 03 §7.1）。
        /// 実際に成長した分だけ GrowthEvent として返す（UI側の週報表示用）。
        /// </summary>
        public List<GrowthEvent> ProcessTrainingGrowth(GameState state, IReadOnlySet<Guid> dispatchedAdventurerIds)
        {
            var events = new List<GrowthEvent>();

            foreach (var adventurer in state.Adventurers)
            {
                if (dispatchedAdventurerIds.Contains(adventurer.Id))
                    continue; // 出撃者は経路1側で処理済み・二重成長なし

                if (!state.TrainingAssignments.TryGetValue(adventurer.Id, out var facility))
                    continue; // 訓練施設未配置＝単純待機（成長ロールなし）

                var targetStats = FacilityBalance.GetTrainingTargetStats(facility);
                double multiplier = GrowthBalance.TrainingFacilityMultiplier + GetTrainerBonus(state, facility);

                var growthEvent = TryGrowOne(adventurer, multiplier, _ => targetStats[_rng.NextInt(0, targetStats.Length - 1)]);
                if (growthEvent != null)
                    events.Add(growthEvent);
            }

            return events;
        }

        /// <summary>配置先施設に教官がいれば、その教官の対象ステータス生涯ピーク値に比例したボーナスを返す（→ 03 §7.1）。</summary>
        private static double GetTrainerBonus(GameState state, FacilityType facility)
        {
            if (!state.AssignedTrainers.TryGetValue(facility, out var trainerId) || trainerId == null)
                return 0;

            var trainer = state.RetiredAdventurers.FirstOrDefault(a => a.Id == trainerId.Value);
            return trainer == null ? 0 : AdvisorSystem.GetTrainerBonus(trainer, facility);
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
    }
}
