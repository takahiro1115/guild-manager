using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>月報の「来月への注意」から移る先の画面（§0.71）。</summary>
    public enum MonthlyNoteTarget
    {
        /// <summary>移る先の無い注意（迷宮の異変など）。</summary>
        None,
        /// <summary>大迷宮画面（方針の無い部隊・待機の理由）。</summary>
        Dungeon,
        /// <summary>施設画面（訓練施設の空き。月のはじめなので、そのまま訓練生を決められる）。</summary>
        Facility,
        /// <summary>部隊・冒険者画面（毒状態・重傷）。</summary>
        Adventurer,
    }

    /// <summary>月報の「来月への注意」1件。</summary>
    public sealed class MonthlyNote
    {
        public string Text { get; set; } = "";
        public MonthlyNoteTarget Target { get; set; }
    }

    /// <summary>月報の冒険者1人分の成長（能力名→上がった量）。</summary>
    public sealed class MonthlyGrowth
    {
        public string Name { get; set; } = "";
        public Dictionary<string, int> Gains { get; set; } = new();
    }

    /// <summary>
    /// 月報（2026年10月・§0.70：月を1ターンにする、§0.71：月報の小窓と過去12か月の保存）。
    /// 「次の月へ」（→ AutoSkipService.AdvanceMonth）で進めた週の結果をまとめ、次の月の手を考える材料にする：
    /// お金と機嫌の増減、倒したボス・決戦で退いたボス・扉前で撤退した理由、冒険者ごとの成長、来月への注意（移る先の画面つき）。
    /// 月の途中で止まったときは「途中経過」で、止まった理由（StopReasons）を持つ。
    /// セーブに直近 KeptReports か月分を残す（→ GameState.MonthlyReports・Record）。表示は UI（MonthlyReportPopup）が組み立てる。
    /// </summary>
    public sealed class MonthlyReport
    {
        /// <summary>セーブに残す月報の数（直近12か月）。</summary>
        public const int KeptReports = 12;

        /// <summary>まとめた最初と最後の週（通算）。</summary>
        public int FirstWeek { get; set; }
        public int LastWeek { get; set; }

        /// <summary>月の最後まで進んだか（false＝月の途中で止まった「途中経過」）。</summary>
        public bool MonthCompleted { get; set; }

        /// <summary>月の途中で止まった理由（MonthCompleted のときは空）。</summary>
        public List<string> StopReasons { get; set; } = new();

        public int GoldBefore { get; set; }
        public int GoldAfter { get; set; }
        public int MoodBefore { get; set; }
        public int MoodAfter { get; set; }

        /// <summary>冒険者ごとの成長。出撃と訓練の両方。自主練（→ SelfTrainingGrowth）も含む。並びは成長の多い順。</summary>
        public List<MonthlyGrowth> Growth { get; set; } = new();

        /// <summary>うち自主練による成長（§0.73）。並びは成長の多い順。</summary>
        public List<MonthlyGrowth> SelfTrainingGrowth { get; set; } = new();

        /// <summary>この月に研究の手伝いが貯まった額（§0.73。上限で切れた分は含まない）と、月末時点で貯まっている額。</summary>
        public int ResearchCreditGained { get; set; }
        public int ResearchCreditAfter { get; set; }

        /// <summary>倒したボス（「森 第10層「若き毒蜘蛛」」）。</summary>
        public List<string> BossesDefeated { get; set; } = new();

        /// <summary>決戦で撤退したボス（火力が足りなかった）。</summary>
        public List<string> BossesRepelled { get; set; } = new();

        /// <summary>扉前で撤退した理由（「森 第20層「…」：討伐火力が足りない見込みのため撤退」）。</summary>
        public List<string> DoorRetreats { get; set; } = new();

        /// <summary>この月に強制除籍された人数。</summary>
        public int ForcedRetired { get; set; }

        /// <summary>来月への注意。</summary>
        public List<MonthlyNote> Notes { get; set; } = new();

        public static MonthlyReport Build(GameState state, IReadOnlyList<AutoSkipWeek> weeks, int goldBefore)
        {
            var settlements = weeks.Select(w => w.Settlement).ToList();
            var resolutions = settlements.SelectMany(s => s.DungeonMissionResolutions).ToList();
            int lastWeek = settlements.Count > 0 ? settlements[^1].Flags.Week : state.WeekNumber;
            var report = new MonthlyReport
            {
                FirstWeek = settlements.Count > 0 ? settlements[0].Flags.Week : state.WeekNumber,
                LastWeek = lastWeek,
                MonthCompleted = settlements.Count > 0 && GameCalendar.IsLastWeekOfMonth(lastWeek),
                GoldBefore = goldBefore,
                GoldAfter = state.Gold,
                MoodBefore = settlements.Count > 0 ? settlements[0].MoodReport.MoodBefore : state.MasterMood,
                MoodAfter = state.MasterMood,
                ForcedRetired = resolutions.Sum(r => r.DungeonResult?.ForceRetiredAdventurerIds.Count ?? 0),
                ResearchCreditGained = settlements.Sum(s => s.IdleHelpEntries.Sum(e => e.Credit)),
                ResearchCreditAfter = state.ResearchCredit,
            };

            if (!report.MonthCompleted && settlements.Count > 0)
                report.StopReasons.AddRange(StopReasonsOf(settlements[^1].Flags));

            // 成長：出撃（各任務の解決）・訓練・自主練を、冒険者ごと・能力ごとに合計する（自主練だけの内訳も別に持つ）
            var selfTraining = settlements.SelectMany(s => s.SelfTrainingGrowthEvents).ToList();
            var growthEvents = resolutions.SelectMany(r => r.GrowthEvents).Concat(settlements.SelectMany(s => s.TrainingGrowthEvents)).Concat(selfTraining);
            report.Growth.AddRange(SumGrowth(growthEvents));
            report.SelfTrainingGrowth.AddRange(SumGrowth(selfTraining));

            foreach (var r in resolutions)
            {
                string where = r.Boss == null ? r.Field.Name : $"{r.Field.Name} 第{r.Boss.Floor}層「{r.Boss.Name}」";
                if (r.DungeonResult?.Outcome == DungeonOutcome.Victory)
                    report.BossesDefeated.Add(where);
                else if (r.DungeonResult?.Outcome == DungeonOutcome.Retreat)
                    report.BossesRepelled.Add(where);
                if (r.ArrivedAtBossDoor && r.DoorRetreatReason != null)
                    report.DoorRetreats.Add($"{where}：{r.DoorRetreatReason}");
            }

            AddNotes(state, report);
            return report;
        }

        /// <summary>成長を冒険者ごと・能力ごとに合計する（成長の多い順）。</summary>
        private static IEnumerable<MonthlyGrowth> SumGrowth(IEnumerable<GrowthEvent> events)
        {
            foreach (var group in events.GroupBy(e => e.Adventurer).OrderByDescending(g => g.Sum(e => e.After - e.Before)))
            {
                var gains = group.GroupBy(e => e.Stat).ToDictionary(g => g.Key, g => g.Sum(e => e.After - e.Before));
                if (gains.Values.Sum() > 0)
                    yield return new MonthlyGrowth { Name = group.Key.Name, Gains = gains };
            }
        }

        /// <summary>月の途中で止まった理由（→ WeekResult.ShouldStopMonth の各条件）。</summary>
        public static List<string> StopReasonsOf(WeekResult flags)
        {
            var reasons = new List<string>();
            if (flags.DeathOrPermanentInjuryOccurred) reasons.Add("強制除籍が出た");
            if (flags.FieldUnlocked) reasons.Add("新しいフィールドが開いた");
            if (flags.SoulFusionBirthOccurred) reasons.Add("娘が誕生した");
            if (flags.RecruitmentTrialOccurred) reasons.Add("採用試験");
            if (flags.CommissionDeadlineNear) reasons.Add("依頼の期限が近い");
            if (flags.SatisfactionWarningOccurred) reasons.Add("契約交渉の申し出");
            if (flags.CommissionsOffered) reasons.Add("依頼が届いた");
            if (flags.GameCleared) reasons.Add("深淵100Fを制覇した");
            if (flags.DefeatOccurred) reasons.Add("ゲームオーバー");
            return reasons;
        }

        /// <summary>月報をギルドの記録に足し、直近 KeptReports か月分だけ残す（古いものから捨てる）。</summary>
        public static void Record(GameState state, MonthlyReport report)
        {
            state.MonthlyReports.Add(report);
            if (state.MonthlyReports.Count > KeptReports)
                state.MonthlyReports.RemoveRange(0, state.MonthlyReports.Count - KeptReports);
        }

        /// <summary>来月への注意（移る先の画面つき）。乱数は使わない。</summary>
        private static void AddNotes(GameState state, MonthlyReport report)
        {
            if (state.Anomaly is { } anomaly)
                report.Notes.Add(new MonthlyNote { Text = DungeonAnomalySystem.DescribeStatus(state, anomaly), Target = MonthlyNoteTarget.Dungeon });

            foreach (var a in state.Adventurers.Where(a => a.IsPoisoned))
                report.Notes.Add(new MonthlyNote { Text = $"{a.Name}は毒状態（あと{a.PoisonWeeksRemaining}週、全能力−{a.PoisonStatPenalty * 100:0}%）", Target = MonthlyNoteTarget.Adventurer });
            foreach (var a in state.Adventurers.Where(a => a.Injury == InjurySeverity.Severe))
                report.Notes.Add(new MonthlyNote { Text = $"{a.Name}は重傷（あと{a.InjuryWeeksRemaining}週）", Target = MonthlyNoteTarget.Adventurer });

            foreach (var p in state.SavedParties.Where(p => p.MemberIds.Count > 0))
            {
                if (p.Order == SquadOrder.None)
                {
                    report.Notes.Add(new MonthlyNote { Text = $"「{p.Name}」は方針が無いので出撃しない", Target = MonthlyNoteTarget.Dungeon });
                    continue;
                }
                if (SquadOrderSystem.IsOut(state, p)) continue;
                if (SquadOrderSystem.GetWaitReason(state, p) is { } wait)
                    report.Notes.Add(new MonthlyNote { Text = $"「{p.Name}」は待機：{wait}", Target = MonthlyNoteTarget.Dungeon });
            }

            // 訓練施設の空き（月のはじめだけ訓練生を決められるので、その時だけ知らせる）
            if (TrainingSystem.CanChangeAssignments(state))
            {
                var training = new TrainingSystem();
                var open = System.Enum.GetValues<FacilityType>().Where(Balance.FacilityBalance.IsTrainingFacility)
                    .Where(f => training.GetSlotCapacity(state, f) > training.CountAssigned(state, f))
                    .ToList();
                bool idle = state.Adventurers.Any(a => !a.IsRetired && !a.IsDispatched && !TrainingSystem.IsTraining(state, a.Id));
                if (open.Count > 0 && idle)
                    report.Notes.Add(new MonthlyNote { Text = $"訓練施設に空きがある（{open.Count}施設）。今月の訓練生を決められる", Target = MonthlyNoteTarget.Facility });
            }
        }

    }
}
