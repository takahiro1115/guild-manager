using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// ギルドの方針・引退後の進路・育成への投資（段4f、2026年10月・§0.105。→ docs/検討中_経営と育成.md 第1部 A2・A3・D）。
    ///  - 方針（システム画面で選ぶ）：訓練の強さ（厳しく／ふつう／穏やかに）、大会の賞金の分け方（ギルドが取る／本人に分ける）。
    ///  - 引退後の進路：顧問・教官の役職についた引退者だけがギルドに残る。引退した（または役職を外れた）週から
    ///    RetireeRoleGraceWeeks のうちに役職につかなければ、ギルドを去る（顧問・教官の候補から外れる。記録には残る）。
    ///  - 個人特訓：外部の講師を1人に1季節つけ、毎週の成長の判定を上乗せする（前払い）。
    ///  - スカウト網の予算：来年の新春の採用試験に向けて払うと、志願者が増え、有望新人が出やすくなる。
    /// </summary>
    public static class GuildPolicySystem
    {
        // ==================== 方針：訓練の強さ ====================

        /// <summary>訓練施設の成長の確率に掛ける倍率（→ GrowthSystem.ProcessTrainingGrowth）。</summary>
        public static double TrainingGrowthMultiplier(GameState state) => state.TrainingPolicy switch
        {
            TrainingPolicy.Hard => PolicyBalance.TrainingHardGrowthMultiplier,
            TrainingPolicy.Gentle => PolicyBalance.TrainingGentleGrowthMultiplier,
            _ => 1.0,
        };

        /// <summary>
        /// 訓練の方針の週の効き目（訓練の成長のあとに呼ぶ）。今週訓練した子（訓練施設に配置され、出撃・さぼり・ふさぎ込みで外れていない子）について、
        /// 厳しく＝軽傷の判定と満足度の低下（志向が訓練の子は下がらない）、穏やかに＝満足度の上昇。けがをした子を返す。
        /// </summary>
        public static List<InjuryEvent> ProcessTrainingPolicy(GameState state, IReadOnlySet<Guid> excludedIds, IRng rng)
        {
            var injuries = new List<InjuryEvent>();
            if (state.TrainingPolicy == TrainingPolicy.Normal) return injuries;
            foreach (var a in state.Adventurers.Where(a => !a.IsRetired && state.TrainingAssignments.ContainsKey(a.Id) && !excludedIds.Contains(a.Id)).ToList())
            {
                if (state.TrainingPolicy == TrainingPolicy.Gentle)
                {
                    SatisfactionSystem.Adjust(a, PolicyBalance.TrainingGentleSatisfaction);
                    continue;
                }
                if (!LovesTraining(a))
                    SatisfactionSystem.Adjust(a, PolicyBalance.TrainingHardSatisfaction);
                if (a.Injury == InjurySeverity.None && rng.NextInt(1, 1000) <= (int)Math.Round(PolicyBalance.TrainingHardInjuryChance * 1000)
                    && CriticalInjury.TryInflictLight(a, rng) is { } injury)
                    injuries.Add(injury);
            }
            return injuries;
        }

        private static bool LovesTraining(Adventurer a) => a.Personality is { } p && (p.Main == Drive.Training || p.Sub == Drive.Training);

        public static string TrainingPolicyLabel(TrainingPolicy policy) => policy switch
        {
            TrainingPolicy.Hard => "厳しく",
            TrainingPolicy.Gentle => "穏やかに",
            _ => "ふつう",
        };

        // ==================== 方針：大会の賞金の分け方 ====================

        /// <summary>賞金のうちギルドに入る額（本人に分けるなら PrizeShareRate を本人へ渡した残り）。</summary>
        public static int GuildShareOfPrize(GameState state, int prize) =>
            state.PrizePolicy == PrizePolicy.Share ? prize - (int)Math.Round(prize * PolicyBalance.PrizeShareRate, MidpointRounding.AwayFromZero) : prize;

        /// <summary>本人に分けるとき、賞金を受け取った子の満足度を上げる（志向が大会・お金の子はさらに）。分けないときは何もしない。</summary>
        public static void RewardPrizeShare(GameState state, Adventurer a, int prize)
        {
            if (state.PrizePolicy != PrizePolicy.Share || prize <= 0) return;
            bool likes = a.Personality is { } p && (p.Main is Drive.Tournament or Drive.Money || p.Sub is Drive.Tournament or Drive.Money);
            SatisfactionSystem.Adjust(a, PolicyBalance.PrizeShareSatisfaction + (likes ? PolicyBalance.PrizeShareDriveBonus : 0));
        }

        public static string PrizePolicyLabel(PrizePolicy policy) => policy == PrizePolicy.Share ? "本人に分ける" : "ギルドが取る";

        // ==================== 引退後の進路 ====================

        /// <summary>顧問・教官の役職についている引退者の Id（派遣の教官を含む）。</summary>
        public static HashSet<Guid> AppointedIds(GameState state)
        {
            var ids = state.AssignedTrainers.Values.OfType<Guid>().ToHashSet();
            if (state.AssignedAdvisor is Guid advisor) ids.Add(advisor);
            if (state.AssignedScoutMaster is Guid scout) ids.Add(scout);
            return ids;
        }

        /// <summary>まだギルドにいて、役職についていない引退者（派遣の教官を除く）。</summary>
        public static List<Adventurer> WaitingRetirees(GameState state)
        {
            var appointed = AppointedIds(state);
            return state.RetiredAdventurers.Where(r => !r.IsGuest && !r.LeftGuild && !appointed.Contains(r.Id)).ToList();
        }

        /// <summary>役職につかなければギルドを去る週（この週の決算で去る）。役職についていれば null。</summary>
        public static int? LeaveWeek(GameState state, Adventurer retiree) =>
            WaitingRetirees(state).Any(r => r.Id == retiree.Id)
                ? (retiree.UnassignedSinceWeek ?? state.WeekNumber) + PolicyBalance.RetireeRoleGraceWeeks - 1
                : null;

        /// <summary>
        /// 週の決算（加齢・引退のあと）：役職についた引退者の数えを消し、役職についていない引退者は数え始め、
        /// RetireeRoleGraceWeeks がたったらギルドを去らせる。去った子を返す。
        /// </summary>
        public static List<Adventurer> ProcessRetirees(GameState state, int week)
        {
            var left = new List<Adventurer>();
            var appointed = AppointedIds(state);
            foreach (var r in state.RetiredAdventurers.Where(r => !r.IsGuest && !r.LeftGuild))
            {
                if (appointed.Contains(r.Id))
                {
                    r.UnassignedSinceWeek = null;
                    continue;
                }
                r.UnassignedSinceWeek ??= week; // 引退した週は AgingSystem.Retire が入れる。役職を外れた子はこの週から数える
                if (week >= r.UnassignedSinceWeek.Value + PolicyBalance.RetireeRoleGraceWeeks - 1)
                {
                    r.LeftGuild = true;
                    left.Add(r);
                }
            }
            return left;
        }

        // ==================== 個人特訓 ====================

        public static CoachingContract? CoachingOf(GameState state, Guid adventurerId) => state.Coachings.FirstOrDefault(c => c.AdventurerId == adventurerId);

        /// <summary>個人特訓をつけられない理由（つけられるなら null）。</summary>
        public static string? CoachingBlockReason(GameState state, Adventurer a)
        {
            if (a.IsRetired || !state.Adventurers.Contains(a)) return "現役の冒険者だけ";
            if (CoachingOf(state, a.Id) != null) return "もう特訓中";
            if (state.Coachings.Count >= PolicyBalance.CoachingSlots) return $"同時に特訓できるのは{PolicyBalance.CoachingSlots}人まで";
            if (state.Gold < PolicyBalance.CoachingCost) return $"お金が足りない（{PolicyBalance.CoachingCost}G）";
            return null;
        }

        /// <summary>個人特訓をつける（前払い）。つけられなければ false。</summary>
        public static bool StartCoaching(GameState state, Adventurer a)
        {
            if (CoachingBlockReason(state, a) != null) return false;
            state.Gold -= PolicyBalance.CoachingCost;
            state.Coachings.Add(new CoachingContract { AdventurerId = a.Id, StartWeek = state.WeekNumber, UntilWeek = state.WeekNumber + PolicyBalance.CoachingWeeks - 1 });
            return true;
        }

        /// <summary>
        /// 週の決算（訓練の成長のあと）：特訓中の子に CoachingRollsPerWeek 回の成長の判定（重傷・派遣中は無し）。
        /// 期間の終わった特訓・いなくなった子の特訓を外す。成長を返す。
        /// </summary>
        public static List<GrowthEvent> ProcessCoaching(GameState state, GrowthSystem growth, int week)
        {
            var events = new List<GrowthEvent>();
            foreach (var c in state.Coachings.ToList())
            {
                var a = state.Adventurers.FirstOrDefault(x => x.Id == c.AdventurerId && !x.IsRetired);
                if (a != null && a.Injury != InjurySeverity.Severe && !a.IsOnLoan)
                    events.AddRange(growth.ProcessCoachingGrowth(state, a, PolicyBalance.CoachingRollsPerWeek, PolicyBalance.CoachingGrowthMultiplier));
                if (a == null || week >= c.UntilWeek)
                    state.Coachings.Remove(c);
            }
            return events;
        }

        // ==================== スカウト網の予算 ====================

        /// <summary>予算を払う新春の年（今の年の次の年）。</summary>
        public static int NextScoutYear(GameState state) => GameCalendar.YearOf(state.WeekNumber) + 1;

        public static string? ScoutBudgetBlockReason(GameState state)
        {
            if (state.ScoutBudgetYear >= NextScoutYear(state)) return "来春の分はもう払った";
            if (state.Gold < PolicyBalance.ScoutBudgetCost) return $"お金が足りない（{PolicyBalance.ScoutBudgetCost}G）";
            return null;
        }

        /// <summary>来年の新春に向けてスカウト網の予算を払う。払えなければ false。</summary>
        public static bool PayScoutBudget(GameState state)
        {
            if (ScoutBudgetBlockReason(state) != null) return false;
            state.Gold -= PolicyBalance.ScoutBudgetCost;
            state.ScoutBudgetYear = NextScoutYear(state);
            return true;
        }

        /// <summary>今の年の採用試験にスカウト網の予算が効くか。</summary>
        public static bool ScoutBudgetActive(GameState state) => state.ScoutBudgetYear == GameCalendar.YearOf(state.WeekNumber);
    }
}
