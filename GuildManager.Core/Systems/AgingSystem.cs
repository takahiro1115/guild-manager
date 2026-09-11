using System;
using System.Collections.Generic;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 加齢・衰微モデル。仕様書 03 §3 参照。
    ///
    /// 毎週の決算処理で ProcessWeeklyAging を1回呼び出す想定
    /// （InjuryRecoverySystem.ProcessWeeklyRecovery と同様の使い方）。
    ///
    /// 「伸びる」側（実効値の成長）は GrowthSystem が別途担当する（→ 03 §3.1〜3.4）。
    /// このクラスは「衰える」「歳を取る」側のみを扱う：
    /// - 円熟期（28〜34歳）：年1回（年度末＝48週目）、STR/AGI/ENDから1〜2項目が恒久低下する。
    /// - 限界期（35〜40歳）：年2回、円熟期より大きい低下量で恒久低下する。
    /// - 低下時はPA自体も同じ量だけ引き下げる（§2.2「加齢でPA自体が低下していく」）。
    ///   GrowthSystemによる成長分も区別なく減算対象になる（レベルアップ等の特別扱いはしない）。
    /// - 年度末（48週目）にAgeを+1する。40歳の年度末に達していた場合は+1せず
    ///   IsRetired=true とする（§3.7）。顧問への転用は別タスク「顧問制度」で扱う。
    ///
    /// 衰微の実行週・低下量は本来 → BAL: 加齢 に集約する数値だが、
    /// 04_バランス表.xlsx はまだコードから読み込めない（Phase 4で外部化予定）ため、
    /// QuestResolver と同様に現状は仮値を定数として直書きする。
    /// </summary>
    public class AgingSystem
    {
        private const int WeeksPerYear = 48; // 仕様書 03 §1.2

        // ---- 衰微の対象ステータス。仕様書 03 §3.0「フィジカル衰微」＝STR/AGI/ENDのみ ----
        private static readonly string[] DeclineTargetStats = { "STR", "AGI", "END" };

        // ---- 円熟期：年1回・年度末（48週目）。低下量は→ BAL: 加齢/衰微量。現状は仮値 ----
        private const int MatureDeclineWeek = WeeksPerYear;
        private const int MatureDeclineAmountMin = 1;
        private const int MatureDeclineAmountMax = 3;

        // ---- 限界期：年2回、低下量は円熟期より大きい（仮値） ----
        private static readonly int[] LimitDeclineWeeks = { WeeksPerYear / 2, WeeksPerYear };
        private const int LimitDeclineAmountMin = 3;
        private const int LimitDeclineAmountMax = 6;

        private const int RetirementAge = 40; // 仕様書 03 §3.7
        private const int MinStatValue = 1;

        private readonly IRng _rng;

        public AgingSystem(IRng rng)
        {
            _rng = rng;
        }

        public void ProcessWeeklyAging(GameState state)
        {
            int weekOfYear = ((state.WeekNumber - 1) % WeeksPerYear) + 1;
            bool isYearEnd = weekOfYear == WeeksPerYear;

            foreach (var adventurer in state.Adventurers)
            {
                if (adventurer.IsRetired)
                    continue;

                switch (adventurer.AgeBand)
                {
                    case AgeBand.MaturePeriod:
                        if (weekOfYear == MatureDeclineWeek)
                            ApplyDecline(adventurer, MatureDeclineAmountMin, MatureDeclineAmountMax);
                        break;

                    case AgeBand.LimitPeriod:
                        if (Array.IndexOf(LimitDeclineWeeks, weekOfYear) >= 0)
                            ApplyDecline(adventurer, LimitDeclineAmountMin, LimitDeclineAmountMax);
                        break;
                }

                if (isYearEnd)
                    AdvanceYear(adventurer);
            }
        }

        private void AdvanceYear(Adventurer adventurer)
        {
            if (adventurer.Age >= RetirementAge)
                adventurer.IsRetired = true;
            else
                adventurer.Age++;
        }

        private void ApplyDecline(Adventurer adventurer, int amountMin, int amountMax)
        {
            int statCount = _rng.NextInt(1, 2); // 「STR/AGI/ENDからランダムに1〜2項目」

            foreach (var stat in PickRandomDistinct(DeclineTargetStats, statCount))
            {
                int amount = _rng.NextInt(amountMin, amountMax);
                int newActual = Math.Max(MinStatValue, AdventurerStatAccessor.GetStat(adventurer, stat) - amount);
                int newPa = Math.Max(MinStatValue, AdventurerStatAccessor.GetPa(adventurer, stat) - amount);
                AdventurerStatAccessor.SetStat(adventurer, stat, newActual);
                AdventurerStatAccessor.SetPa(adventurer, stat, newPa);
            }
        }

        private List<string> PickRandomDistinct(IReadOnlyList<string> source, int count)
        {
            var pool = new List<string>(source);
            var picked = new List<string>();
            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                int index = _rng.NextInt(0, pool.Count - 1);
                picked.Add(pool[index]);
                pool.RemoveAt(index);
            }
            return picked;
        }
    }
}
