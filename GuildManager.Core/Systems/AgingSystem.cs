using System;
using System.Collections.Generic;
using System.Linq;
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
    /// - 円熟期（28〜34歳）：年1回（年度末＝48週目）、STR/AGI/VITから1〜2項目が恒久低下する。
    /// - 限界期（35〜40歳）：年2回、円熟期より大きい低下量で恒久低下する。
    ///   精神系（MND/DEX/LDR）はどちらの年齢帯でも対象外（§3.0「円熟期は精神系維持可」）。
    /// - 低下はPAには影響しない（PA＝成長上限はそのまま。実効値のみ下限0でクランプ）。
    /// - 年度末（48週目）にAgeを+1する。40歳の年度末に達していた場合は+1せず強制引退させる
    ///   （§3.7）：退職金（週給×12週）を支給し、Adventurers から RetiredAdventurers へ移す。
    ///   顧問としての実際の効果（教官伝授・スカウト補正・参謀補正）は§7が未実装のため付与しない。
    ///
    /// 衰微の実行週・低下量は本来 → BAL: 加齢 に集約する数値だが、
    /// 04_バランス表.xlsx はまだコードから読み込めない（Phase 4で外部化予定）ため、
    /// QuestResolver と同様に現状は仮値を定数として直書きする。
    /// </summary>
    public class AgingSystem
    {
        private const int WeeksPerYear = 48; // 仕様書 03 §1.2

        // ---- 衰微の対象ステータス。仕様書 03 §3.0「フィジカル衰微」＝STR/AGI/VITのみ。
        // 精神系（MND/DEX/LDR）は円熟期・限界期のいずれでも対象外とする
        // （§3.0「円熟期は精神系維持可」の記述どおり。限界期に含めるかは仕様上明記が無いため今回は含めない）。 ----
        private static readonly string[] DeclineTargetStats = { "STR", "AGI", "VIT" };

        // ---- 円熟期：年1回・年度末（48週目）。低下量は→ BAL: 加齢/衰微量。現状は仮値 ----
        private const int MatureDeclineWeek = WeeksPerYear;
        private const int MatureDeclineAmountMin = 1;
        private const int MatureDeclineAmountMax = 3;

        // ---- 限界期：年2回、低下量は円熟期より大きい（仮値） ----
        private static readonly int[] LimitDeclineWeeks = { WeeksPerYear / 2, WeeksPerYear };
        private const int LimitDeclineAmountMin = 3;
        private const int LimitDeclineAmountMax = 6;

        private const int MinStatValue = 0; // 仕様書：実効値は0未満にならない

        private const int RetirementAge = 40; // 仕様書 03 §3.7
        private const int SeveranceWeeks = 12; // 退職金＝週給×12週（仕様書 03 §7）

        private readonly IRng _rng;

        public AgingSystem(IRng rng)
        {
            _rng = rng;
        }

        public void ProcessWeeklyAging(GameState state)
        {
            int weekOfYear = ((state.WeekNumber - 1) % WeeksPerYear) + 1;
            bool isYearEnd = weekOfYear == WeeksPerYear;

            // ToList()でスナップショットを取る：年度末の引退処理でstate.Adventurersから
            // 要素を取り除く（RetiredAdventurersへ移す）ため、foreach対象の生のリストを
            // 直接回すとコレクション変更例外になる。
            foreach (var adventurer in state.Adventurers.ToList())
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
                    AdvanceYear(state, adventurer);
            }
        }

        private void AdvanceYear(GameState state, Adventurer adventurer)
        {
            if (adventurer.Age >= RetirementAge)
                Retire(state, adventurer);
            else
                adventurer.Age++;
        }

        /// <summary>
        /// 早期引退（仕様書 03 §7「引退の経路」）。プレイヤーが任意のタイミングで
        /// 40歳未満の現役冒険者を引退させ、顧問候補にする。退職金支給等の処理は
        /// 40歳強制引退（Retire）と共通のものを使う。既に引退済みなら何もしない。
        /// </summary>
        public void RetireVoluntarily(GameState state, Adventurer adventurer)
        {
            if (adventurer.IsRetired)
                return;

            Retire(state, adventurer);
        }

        /// <summary>
        /// 引退処理の共通部分（仕様書 03 §3.7・§7）。退職金を支給し、現役ロースターから
        /// 引退済み一覧へ移す。訓練場に配置中だった場合はその枠も解放する。
        /// 40歳強制引退（AdvanceYear経由）と早期引退（RetireVoluntarily）の両方から呼ばれる。
        /// </summary>
        private void Retire(GameState state, Adventurer adventurer)
        {
            adventurer.IsRetired = true;
            adventurer.RetiredAtAge = adventurer.Age;
            adventurer.RetiredAtWeek = state.WeekNumber;

            state.Gold -= adventurer.WeeklyWage * SeveranceWeeks;
            adventurer.SeverancePaid = true;

            state.TrainingAssignments.Remove(adventurer.Id); // 訓練場配置からも外れる（枠を解放）
            state.Adventurers.Remove(adventurer);
            state.RetiredAdventurers.Add(adventurer);
        }

        private void ApplyDecline(Adventurer adventurer, int amountMin, int amountMax)
        {
            int statCount = _rng.NextInt(1, 2); // 「STR/AGI/VITからランダムに1〜2項目」

            foreach (var stat in PickRandomDistinct(DeclineTargetStats, statCount))
            {
                int amount = _rng.NextInt(amountMin, amountMax);
                int newActual = Math.Max(MinStatValue, AdventurerStatAccessor.GetStat(adventurer, stat) - amount);
                AdventurerStatAccessor.SetStat(adventurer, stat, newActual);
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
