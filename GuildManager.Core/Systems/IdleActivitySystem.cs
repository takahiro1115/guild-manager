using System;
using System.Collections.Generic;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>今週の冒険者の過ごし方（→ IdleActivitySystem.GetWeekActivity）。画面の「今週：…」の表示にも使う。</summary>
    public enum WeekActivity
    {
        /// <summary>出撃中、または今週帰還した。</summary>
        Dispatched,
        /// <summary>訓練施設に入っている（→ GameState.TrainingAssignments）。</summary>
        Training,
        /// <summary>静養（負傷・毒・HPが条件以下）。HPの自然回復だけ。</summary>
        Resting,
        /// <summary>アルベールの研究を手伝う。</summary>
        Help,
        /// <summary>自主練。</summary>
        SelfTraining,
        /// <summary>大会に出る（その月は出撃・訓練をしない。休養か追い込み、§0.82）。</summary>
        Tournament,
        /// <summary>依頼の派遣で先方へ出ている（§0.85）。</summary>
        OnLoan,
    }

    /// <summary>
    /// 研究の手伝い1名分の記録（→ IdleActivitySystem.ProcessWeek）。Credit は実際に貯まった額（上限で切れた分は含まない）、
    /// Mood は名目上の機嫌の増分、MoodApplied は上限クランプ後に実際に動いた量。
    /// </summary>
    public record IdleHelpEntry(Guid AdventurerId, string Name, int Credit, int Mood, int MoodApplied);

    /// <summary>1週分の待機中の過ごし方の結果（→ IdleActivitySystem.ProcessWeek）。</summary>
    public sealed class IdleActivityWeek
    {
        /// <summary>研究を手伝った冒険者。</summary>
        public List<IdleHelpEntry> HelpEntries { get; } = new();

        /// <summary>自主練をした冒険者（HPは消費済み。成長のロールは GrowthSystem.ProcessSelfTraining）。</summary>
        public HashSet<Guid> SelfTrainerIds { get; } = new();
    }

    /// <summary>
    /// 待機中の過ごし方（2026年10月・§0.73。旧・待機お手伝い MasterMoodSystem.ProcessIdleHelp の置き換え）。
    /// 出撃しておらず・訓練施設にも入っておらず・HP が最大HP×IdleActivityHpRatio を超え・負傷も毒も無い冒険者は、
    /// 冒険者ごとに決めておいた過ごし方（→ Adventurer.IdleActivity）をする。それ以外は静養。
    ///  - 研究を手伝う：研究の手伝い（GameState.ResearchCredit）が IdleAdventurerHelp_ResearchCredit 貯まり（働き者×1.5・怠け者0、
    ///    上限 ResearchCreditMax）、機嫌 +IdleAdventurerHelp_Mood。
    ///  - 自主練：HP を SelfTrainingHpCost 使う。成長のロールは GrowthSystem.ProcessSelfTraining。
    /// どちらも静養の回復（→ RestRecoverySystem）は受ける（自主練は回復が SelfTrainingHpCost だけ遅くなる。回復を止めると
    /// HP が条件の少し上に張り付き、方針の出撃や構えの HP 条件に届かなくなるため）。
    /// 判定は訓練・静養でHPが動く前に行う（→ WeekProcessingSystem）。乱数は使わない。
    /// </summary>
    public static class IdleActivitySystem
    {
        /// <summary>今週この冒険者がどう過ごすか（dispatchedIds＝決算開始時点の出撃中。今週帰還した者も含む）。</summary>
        public static WeekActivity GetWeekActivity(GameState state, Adventurer a, IReadOnlySet<Guid>? dispatchedIds = null)
        {
            if (a.IsOnLoan) return WeekActivity.OnLoan;
            if (a.IsDispatched || (dispatchedIds != null && dispatchedIds.Contains(a.Id))) return WeekActivity.Dispatched;
            if (TournamentSystem.IsEntered(state, a.Id)) return WeekActivity.Tournament;
            if (state.TrainingAssignments.ContainsKey(a.Id)) return WeekActivity.Training;
            if (RestReason(a) != null) return WeekActivity.Resting;
            return a.IdleActivity == IdleActivity.SelfTraining ? WeekActivity.SelfTraining : WeekActivity.Help;
        }

        /// <summary>静養になる理由（負傷・毒・HP）。待機中の過ごし方ができるなら null。出撃・訓練は見ない。</summary>
        public static string? RestReason(Adventurer a)
        {
            if (a.Injury != InjurySeverity.None || a.InjuryWeeksRemaining != 0) return "負傷";
            if (a.IsPoisoned) return "毒状態";
            if (a.CurrentHP <= a.MaxHP * TrainingBalance.IdleActivityHpRatio)
                return $"HP{TrainingBalance.IdleActivityHpRatio * 100:0}%以下";
            return null;
        }

        /// <summary>
        /// 今週の待機中の過ごし方を行う。研究の手伝いを貯めて機嫌を上げ（report の内訳へ1行）、自主練をする者のHPを使う。
        /// </summary>
        public static IdleActivityWeek ProcessWeek(GameState state, IReadOnlySet<Guid> dispatchedIds, MasterMoodReport report)
        {
            var week = new IdleActivityWeek();
            int moodApplied = 0;

            foreach (var a in state.Adventurers)
            {
                switch (GetWeekActivity(state, a, dispatchedIds))
                {
                    case WeekActivity.Help:
                        int before = state.ResearchCredit;
                        state.ResearchCredit = Math.Min(TrainingBalance.ResearchCreditMax, state.ResearchCredit + GetHelpCredit(a));
                        int applied = MasterMoodSystem.Adjust(state, MasterMoodBalance.IdleAdventurerHelpMood);
                        moodApplied += applied;
                        week.HelpEntries.Add(new IdleHelpEntry(a.Id, a.Name, state.ResearchCredit - before, MasterMoodBalance.IdleAdventurerHelpMood, applied));
                        break;
                    case WeekActivity.SelfTraining:
                        a.CurrentHP = Math.Max(TrainingBalance.MinHp, a.CurrentHP - TrainingBalance.SelfTrainingHpCost);
                        week.SelfTrainerIds.Add(a.Id);
                        break;
                }
            }

            if (week.HelpEntries.Count > 0)
            {
                report.Entries.Add(new MasterMoodEntry(
                    $"研究の手伝い（{week.HelpEntries.Count}名×{MasterMoodBalance.IdleAdventurerHelpMood}）",
                    week.HelpEntries.Count * MasterMoodBalance.IdleAdventurerHelpMood, moodApplied));
                report.MoodAfter = state.MasterMood;
            }

            return week;
        }

        /// <summary>
        /// 研究の手伝い1名が週に貯める額＝IdleAdventurerHelp_ResearchCredit。働き者（→ TraitCatalog.Hardworker）なら
        /// HardworkerIdleHelpCreditMultiplier 倍（四捨五入）、怠け者（→ TraitCatalog.Slothful）なら0（機嫌は上がる）。
        /// </summary>
        public static int GetHelpCredit(Adventurer a) =>
            a.HasTrait(TraitCatalog.SlothfulId) ? 0
            : a.HasTrait(TraitCatalog.HardworkerId)
                ? (int)Math.Round(MasterMoodBalance.IdleAdventurerHelpResearchCredit * TraitBalance.HardworkerIdleHelpCreditMultiplier, MidpointRounding.AwayFromZero)
                : MasterMoodBalance.IdleAdventurerHelpResearchCredit;

        /// <summary>過ごし方の表示名。</summary>
        public static string Label(IdleActivity activity) => activity switch
        {
            IdleActivity.SelfTraining => "自主練",
            _ => "研究を手伝う",
        };

        /// <summary>今週の過ごし方の表示名。</summary>
        public static string Label(WeekActivity activity) => activity switch
        {
            WeekActivity.Dispatched => "出撃中",
            WeekActivity.Training => "訓練中",
            WeekActivity.Resting => "静養",
            WeekActivity.SelfTraining => "自主練",
            WeekActivity.Tournament => "大会の準備",
            WeekActivity.OnLoan => "派遣中",
            _ => "研究を手伝う",
        };
    }
}
