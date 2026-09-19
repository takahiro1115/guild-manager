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
        /// 経路1：出撃による成長。出撃したパーティの全員に成長ロールを適用する
        /// （任務の成否は問わない。§3.1〜3.4「出撃した週」）。difficulty は任務の難易度（1〜100）で、
        /// 成長確率の難易度係数（→ GrowthBalance.GetDifficultyCoefficient）を決める。
        /// 実際に成長した分だけ GrowthEvent として返す（UI側の週報表示用）。
        ///
        /// 旧通常クエストの撤去（2026年9月）以降、この難易度ベースの経路を呼び出す任務は無い。
        /// 大迷宮の任務による出撃成長は ApplyExpeditionGrowth が担う（→ 下記）。
        /// </summary>
        public List<GrowthEvent> ProcessDeploymentGrowth(Party party, int difficulty)
        {
            double difficultyCoefficient = GrowthBalance.GetDifficultyCoefficient(difficulty);
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

        // ==================== 経路1：大迷宮の任務による成長（2026年9月、旧クエスト撤去後の再配線） ====================

        private static readonly string[] TraversalGrowthStats = { "STR", "VIT", "AGI", "DEX" };
        private static readonly string[] SurveyGrowthStats = { "INT", "DEX", "LDR", "AGI" };
        private static readonly string[] GatheringGrowthStats = { "AGI", "DEX", "VIT" };

        /// <summary>
        /// 大迷宮の任務の解決時に、出撃した部隊の生存者へ成長ロールを行う（成長トリガー経路1、→ 03 §3.1〜3.4）。
        /// 任務の性質ごとに試行回数と対象能力が決まる（→ BAL: dungeon.csv GrowthRolls_*・GrowthBaseChancePercent）：
        ///  - ボス撃破（isBossVictory=true）：全7能力から、GrowthRolls_BossVictory 回
        ///  - 道中進軍（Scouting＝潜行）：STR/VIT/AGI/DEX から、GrowthRolls_Traversal 回
        ///  - 迷宮調査（Survey）：INT/DEX/LDR/AGI から、GrowthRolls_Survey 回
        ///  - 採取（Gathering）：AGI/DEX/VIT から、GrowthRolls_Gathering 回
        ///  - ボス討伐で撃破できなかった場合（BossAssault かつ isBossVictory=false）：成長なし
        /// 各試行は GrowthBaseChancePercent % で成功し、対象能力から等確率に1つを選んで +1 する。
        /// PA（潜在能力上限）は超えない（既にPA上限なら変化なし＝報告しない）。
        /// 対象は生存者のみ：現役ロースターに残っていて CurrentHP が1以上のメンバー（強制除籍者は対象外）。
        /// 実際に成長した分だけ GrowthEvent として返す（週報表示用）。
        /// </summary>
        public List<GrowthEvent> ApplyExpeditionGrowth(GameState state, Party party, DungeonMissionType missionType, bool isBossVictory)
        {
            var (rolls, stats) = ExpeditionGrowthPlan(missionType, isBossVictory);
            var events = new List<GrowthEvent>();
            if (rolls <= 0)
                return events;

            foreach (var member in party.Members)
            {
                if (member.CurrentHP <= 0 || !state.Adventurers.Contains(member))
                    continue;

                for (int i = 0; i < rolls; i++)
                {
                    if (_rng.NextInt(1, 100) > DungeonBalance.GrowthBaseChancePercent)
                        continue;

                    string stat = stats[_rng.NextInt(0, stats.Count - 1)];
                    int before = AdventurerStatAccessor.GetStat(member, stat);
                    int after = Math.Min(AdventurerStatAccessor.GetPa(member, stat), before + 1);
                    if (after == before)
                        continue; // PA上限で打ち止め

                    AdventurerStatAccessor.SetStat(member, stat, after);
                    events.Add(new GrowthEvent(member, stat, before, after));
                }
            }

            return events;
        }

        /// <summary>任務の性質ごとの成長ロール試行回数と対象能力（→ ApplyExpeditionGrowth）。</summary>
        public static (int Rolls, IReadOnlyList<string> Stats) ExpeditionGrowthPlan(DungeonMissionType missionType, bool isBossVictory)
        {
            if (isBossVictory)
                return (DungeonBalance.GrowthRollsBossVictory, AdventurerStatAccessor.AllStatNames);

            return missionType switch
            {
                DungeonMissionType.Scouting => (DungeonBalance.GrowthRollsTraversal, TraversalGrowthStats),
                DungeonMissionType.Survey => (DungeonBalance.GrowthRollsSurvey, SurveyGrowthStats),
                DungeonMissionType.Gathering => (DungeonBalance.GrowthRollsGathering, GatheringGrowthStats),
                _ => (0, Array.Empty<string>()), // ボス討伐で撃破できなかった場合は成長なし
            };
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
