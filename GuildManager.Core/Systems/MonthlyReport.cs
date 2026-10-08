using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
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

    /// <summary>月報の1行の色合い（§0.81）。</summary>
    public enum MonthlyTone
    {
        Normal,
        Good,
        Warning,
        Bad,
    }

    /// <summary>月報の1行（主な出来事・部隊の週ごとの行動・冒険者の状態。§0.81）。</summary>
    public sealed class MonthlyLine
    {
        public string Text { get; set; } = "";
        public MonthlyTone Tone { get; set; }
        /// <summary>部隊の週ごとの行動のときの、月の中の週（1〜4）。それ以外は0。</summary>
        public int WeekOfMonth { get; set; }
    }

    /// <summary>月報の部隊1つ分（§0.81）：方針・メンバー・週ごとの行動・月の結果・今のHP。</summary>
    public sealed class MonthlySquad
    {
        public string Name { get; set; } = "";
        public SquadOrder Order { get; set; }
        public string FieldName { get; set; } = "";
        public List<string> Members { get; set; } = new();
        public List<MonthlyLine> Weeks { get; set; } = new();
        public string Result { get; set; } = "";
        /// <summary>メンバーの今のHPの合計÷最大HPの合計（%）。</summary>
        public int HpPercent { get; set; }
    }

    /// <summary>月報の冒険者1人分（§0.81）：所属する部隊・この月の過ごし方・成長・今の状態。</summary>
    public sealed class MonthlyAdventurer
    {
        public string Name { get; set; } = "";
        public JobClass Job { get; set; }
        public int Age { get; set; }
        public string Squad { get; set; } = "";
        /// <summary>過ごし方の内訳（「出撃3週・静養1週」など）。</summary>
        public string Activities { get; set; } = "";
        public Dictionary<string, int> Gains { get; set; } = new();
        public List<MonthlyLine> Status { get; set; } = new();
        public int HpPercent { get; set; }
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

        /// <summary>主な出来事（撃破・新しいフィールド・負傷・誕生・引退・施設など。§0.81）。</summary>
        public List<MonthlyLine> Highlights { get; set; } = new();

        /// <summary>部隊ごと（メンバーのいる部隊。§0.81）。</summary>
        public List<MonthlySquad> Squads { get; set; } = new();

        /// <summary>冒険者ごと（この月に在籍した現役。§0.81）。</summary>
        public List<MonthlyAdventurer> Adventurers { get; set; } = new();

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
            AddHighlights(state, weeks, report);
            AddSquads(state, weeks, report);
            AddAdventurers(state, weeks, report);
            return report;
        }

        // ==================== 主な出来事・部隊ごと・冒険者ごと（§0.81） ====================

        private static string Where(DungeonMissionResolution r) =>
            r.Boss == null ? r.Field.Name : $"{r.Field.Name} 第{r.Boss.Floor}層「{r.Boss.Name}」";

        private static string NameOf(GameState state, Guid id) => state.FindAdventurer(id)?.Name ?? "？";

        /// <summary>主な出来事：撃破・新しいフィールド・出撃枠・重傷・ギルドを去った者・誕生・引退・退団・依頼・施設。</summary>
        private static void AddHighlights(GameState state, IReadOnlyList<AutoSkipWeek> weeks, MonthlyReport report)
        {
            void Add(string text, MonthlyTone tone) => report.Highlights.Add(new MonthlyLine { Text = text, Tone = tone });

            foreach (var s in weeks.Select(w => w.Settlement))
            {
                foreach (var r in s.DungeonMissionResolutions)
                {
                    if (r.DungeonResult?.Outcome == DungeonOutcome.Victory)
                        Add($"{Where(r)}を撃破", MonthlyTone.Good);
                    if (r.FieldNewlyUnlocked != null)
                        Add($"新しいフィールド「{r.FieldNewlyUnlocked.Name}」が開いた", MonthlyTone.Good);
                    if (r.SquadSlotsExpandedTo is int slots)
                        Add($"同時に出撃できる部隊が{slots}つになった", MonthlyTone.Good);
                    foreach (var injury in r.InjuryEvents.Where(e => e.Severity == InjurySeverity.Severe))
                        Add($"{injury.Name}が重傷（全治{injury.Weeks}週）", MonthlyTone.Bad);
                    foreach (var id in r.DungeonResult?.ForceRetiredAdventurerIds ?? new HashSet<Guid>())
                        Add($"{NameOf(state, id)}が致命傷を負い、ギルドを去った", MonthlyTone.Bad);
                }
                foreach (var birth in s.SoulFusionBirths)
                    Add($"{birth.Child.Name}が生まれた", MonthlyTone.Good);
                foreach (var left in s.NegotiationTerminated)
                    Add($"{left.Name}が契約交渉の末に退団した", MonthlyTone.Bad);
                foreach (var done in s.Commissions.Completed)
                    Add(done.LoanedAdventurerName != null
                        ? $"依頼を果たした（{done.ClientName}）：{done.LoanedAdventurerName}をひと季節派遣した"
                        : $"依頼を果たした（{done.ClientName}）", MonthlyTone.Good);
                foreach (var back in s.LoanReturns)
                    Add($"{back.Adventurer.Name}が{back.ClientName}から帰ってきた" +
                        (back.Growth.Count > 0 ? "（" + string.Join("・", back.Growth.GroupBy(g => g.Stat).Select(g => $"{g.Key}+{g.Sum(e => e.After - e.Before)}")) + "）" : "") +
                        (back.TraitId != null ? $"。特性「{TraitCatalog.FindById(back.TraitId)?.DisplayName ?? back.TraitId}」が付いた" : ""), MonthlyTone.Good);
                foreach (var failed in s.Commissions.Failed)
                    Add($"依頼に失敗した（{failed.ClientName}・{failed.Reason}）", MonthlyTone.Warning);
                if (s.CompletedFacility != null)
                    Add($"施設がLv{s.CompletedFacility.CurrentLevel}に完成した（{FacilityName(s.CompletedFacility.Type)}）", MonthlyTone.Good);
                // 大会（§0.82）：ギルドの出場者の順位（ベスト4以上）。出場しなかった大会は優勝者だけ。
                foreach (var ev in s.TournamentsResolved)
                {
                    var placings = ev.Result?.Placings ?? new List<TournamentPlacing>();
                    foreach (var p in placings.Where(p => p.Placing <= 4))
                        Add($"{ev.Name}（{TournamentSystem.GradeLabel(ev.Grade)}）で{p.Name}が{TournamentSystem.PlacingLabel(p.Placing)}" + (p.Prize > 0 ? $"（賞金{p.Prize}G）" : ""), p.Placing == 1 ? MonthlyTone.Good : MonthlyTone.Normal);
                    foreach (var p in placings.Where(p => p.Placing > 4))
                        Add($"{ev.Name}で{p.Name}は{TournamentSystem.PlacingLabel(p.Placing)}", MonthlyTone.Normal);
                }
                // イザベラの来訪と交流戦（§0.84）
                if (s.IsabellaVisited)
                    Add("白百合の杖のイザベラが来訪し、交流戦を申し込んできた（来月の第4週・剣・魔・技の3本勝負。🏆 大会のタブが開いた）", MonthlyTone.Good);
                foreach (var m in s.ExchangeMatches)
                {
                    string bouts = string.Join("・", m.Bouts.Select(b => $"{TournamentSystem.DisciplineLabel(b.Discipline)} {b.OurName}{(b.Won ? "○" : "×")}{b.OpponentName}"));
                    string reward = m.FirstWin ? "。ご褒美に教官のマルグリットが派遣される" : m.Prize > 0 ? $"（賞金{m.Prize}G・機嫌+{m.Mood}）" : "";
                    Add($"交流戦に{(m.Won ? "勝った" : "負けた")}（{m.Wins}勝{m.Bouts.Count - m.Wins}敗：{bouts}）{reward}", m.Won ? MonthlyTone.Good : MonthlyTone.Normal);
                    if (m.First)
                        Add("来月から王都の大会に出られる（🏆 大会の暦）", MonthlyTone.Good);
                }
                if (s.GuestTrainer.Arrived is { } guest)
                    Add($"派遣の教官{guest.Name}が着任した（{IsabellaBalance.GuestTrainerWeeks}週" + (s.GuestTrainer.AssignedTo is FacilityType t ? $"・{FacilityName(t)}の教官" : "・施設画面で教官に任命できる") + "）", MonthlyTone.Good);
                if (s.GuestTrainer.Left is { } guestLeft)
                    Add($"派遣の教官{guestLeft.Name}が白百合の杖へ帰った", MonthlyTone.Normal);
                if (s.CommissionsUnlocked && state.CommissionsFromWeek is int from)
                    Add($"初めての入賞で王都に名が知られた。{GameCalendar.FormatMonth(from)}から依頼が届く", MonthlyTone.Good);
                foreach (var invite in s.TournamentInvitations)
                    Add($"招待が届いた：{invite.Name}（{GameCalendar.FormatMonth(GameCalendar.WeekNumberOf(invite.Year, invite.Month, invite.Week))} 第{invite.Week}週）", MonthlyTone.Good);
                foreach (var unlock in s.FacilityUnlocks)
                    Add($"{FacilityUnlockSystem.DescribeUnlock(unlock)}：{unlock.Line}", MonthlyTone.Good);
                // 栄誉（大会と育成の栄光 段2）：新しい二つ名・殿堂入り・母娘が同じ部隊で出撃
                foreach (var notice in s.HonorNotices)
                    Add(notice.Text, MonthlyTone.Good);
            }

            // 引退：この月のはじめに現役で、今は引退している者（満期・早期のどちらも）
            var firstWeekIds = weeks.Count > 0 ? weeks[0].Settlement.Activities.Keys : Enumerable.Empty<Guid>();
            foreach (var id in firstWeekIds)
                if (state.RetiredAdventurers.FirstOrDefault(a => a.Id == id) is { } retired)
                    Add($"{retired.Name}が引退した", MonthlyTone.Normal);
        }

        /// <summary>部隊ごと：方針・メンバー・週ごとの行動（出撃の結果／待機の理由）・月の結果・今のHP。</summary>
        private static void AddSquads(GameState state, IReadOnlyList<AutoSkipWeek> weeks, MonthlyReport report)
        {
            foreach (var party in state.SavedParties.Where(p => p.MemberIds.Count > 0))
            {
                var members = party.MemberIds.Select(id => state.Adventurers.FirstOrDefault(a => a.Id == id)).Where(a => a != null).Select(a => a!).ToList();
                var squad = new MonthlySquad
                {
                    Name = party.Name,
                    Order = party.Order,
                    FieldName = SquadOrderSystem.ResolveField(state, party)?.Name ?? "",
                    Members = members.Select(a => a.Name).ToList(),
                    HpPercent = members.Sum(a => a.MaxHP) > 0 ? members.Sum(a => a.CurrentHP) * 100 / members.Sum(a => a.MaxHP) : 0,
                };

                int bosses = 0, gold = 0, relics = 0;
                var materials = new Dictionary<string, int>();
                var reached = new Dictionary<string, int>();
                foreach (var week in weeks)
                {
                    int weekOfMonth = GameCalendar.WeekOfMonth(week.Settlement.Flags.Week);
                    var resolutions = week.Settlement.DungeonMissionResolutions
                        .Where(r => r.Party.Members.Any(m => party.MemberIds.Contains(m.Id))).ToList();
                    foreach (var r in resolutions)
                    {
                        var (text, tone) = DescribeWeek(r);
                        squad.Weeks.Add(new MonthlyLine { Text = text, Tone = tone, WeekOfMonth = weekOfMonth });
                        if (r.DungeonResult?.Outcome == DungeonOutcome.Victory) bosses++;
                        gold += r.DepositedGold + (r.GatheringResult?.GoldEarned ?? 0);
                        foreach (var kv in r.DepositedMaterials)
                            materials[kv.Key] = materials.GetValueOrDefault(kv.Key) + kv.Value;
                        if (r.GatheringResult is { MaterialCount: > 0 } g)
                            materials[g.MaterialId] = materials.GetValueOrDefault(g.MaterialId) + g.MaterialCount;
                        relics += r.RelicsFound.Count;
                        if (r.TraversalResult is { } t)
                            reached[r.Field.Name] = Math.Max(reached.GetValueOrDefault(r.Field.Name), t.FloorAfter);
                    }
                    if (resolutions.Count == 0)
                    {
                        var order = week.Orders.FirstOrDefault(e => e.Party.Id == party.Id);
                        if (order?.Action == SquadOrderAction.Waiting)
                            squad.Weeks.Add(new MonthlyLine { Text = $"待機：{order.Detail}", Tone = MonthlyTone.Warning, WeekOfMonth = weekOfMonth });
                        else if (week.Settlement.Activities.Any(kv => party.MemberIds.Contains(kv.Key) && kv.Value == WeekActivity.Dispatched))
                            squad.Weeks.Add(new MonthlyLine { Text = "潜行中", WeekOfMonth = weekOfMonth });
                        else
                            squad.Weeks.Add(new MonthlyLine { Text = party.Order == SquadOrder.None ? "方針が無いので出撃しない" : "出撃しなかった", Tone = MonthlyTone.Warning, WeekOfMonth = weekOfMonth });
                    }
                }

                var parts = new List<string>();
                if (bosses > 0) parts.Add($"撃破{bosses}体");
                parts.AddRange(reached.Select(kv => $"{kv.Key} {kv.Value}Fまで"));
                if (gold > 0) parts.Add($"{gold}G");
                parts.AddRange(materials.Where(kv => kv.Value > 0).Select(kv => $"{Balance.MaterialBalance.GetName(kv.Key)}×{kv.Value}"));
                if (relics > 0) parts.Add($"未鑑定の遺物×{relics}");
                squad.Result = parts.Count > 0 ? string.Join("・", parts) : "成果なし";
                report.Squads.Add(squad);
            }
        }

        /// <summary>出撃の結果を1行に（どこで何をして、どうなったか）。計算の内訳は週ごとの記録に任せる。</summary>
        private static (string Text, MonthlyTone Tone) DescribeWeek(DungeonMissionResolution r)
        {
            var parts = new List<string>();
            var tone = MonthlyTone.Normal;
            if (r.TraversalResult is { } t)
                parts.Add(t.FloorAfter > t.FloorBefore ? $"{r.Field.Name} {t.FloorBefore}F → {t.FloorAfter}F" : $"{r.Field.Name} {t.FloorAfter}Fで足止め");
            if (r.ScoutingResult is { } s)
            {
                parts.Add($"{Where(r)}を調査：解析 {r.IntelRateBefore * 100:0}% → {s.IntelRateAfter * 100:0}%" + (s.StealthSucceeded ? "" : "（見つかった）"));
                if (!s.StealthSucceeded) tone = MonthlyTone.Warning;
            }
            if (r.GatheringResult is { } g)
                parts.Add(g.MaterialCount > 0
                    ? $"{r.Field.Name}で採取：{Balance.MaterialBalance.GetName(g.MaterialId)}×{g.MaterialCount}" + (g.GoldEarned > 0 ? $"・{g.GoldEarned}G" : "")
                    : $"{r.Field.Name}で採取：何も見つからなかった");
            if (r.ArrivedAtBossDoor && r.DoorRetreatReason != null)
            {
                parts.Add($"扉前で撤退：{r.DoorRetreatReason}");
                tone = MonthlyTone.Warning;
            }
            if (r.DungeonResult is { } d)
            {
                if (d.Outcome == DungeonOutcome.Victory)
                {
                    parts.Add($"{Where(r)}を撃破（火力 {d.PartyPower:F0} ／ 要求 {d.RequiredPower:F0}）");
                    tone = MonthlyTone.Good;
                }
                else
                {
                    parts.Add($"{Where(r)}との決戦で退いた（火力 {d.PartyPower:F0} ／ 要求 {d.RequiredPower:F0}）");
                    tone = MonthlyTone.Bad;
                }
            }
            if (r.InjuryEvents.Any())
            {
                parts.Add("負傷：" + string.Join("、", r.InjuryEvents.Select(e => $"{e.Name}（{(e.Severity == InjurySeverity.Severe ? "重傷" : "軽傷")}）")));
                if (tone == MonthlyTone.Normal) tone = MonthlyTone.Warning;
            }
            if (r.ReturnedHome && r.TraversalResult != null)
                parts.Add("帰還");
            return (parts.Count > 0 ? string.Join("・", parts) : $"{r.Field.Name}へ出撃", tone);
        }

        /// <summary>冒険者ごと：所属する部隊・過ごし方の内訳・成長・今の状態（HP・負傷・毒・特性・満足度）。</summary>
        private static void AddAdventurers(GameState state, IReadOnlyList<AutoSkipWeek> weeks, MonthlyReport report)
        {
            var settlements = weeks.Select(w => w.Settlement).ToList();
            var growth = settlements.SelectMany(s => s.DungeonMissionResolutions).SelectMany(r => r.GrowthEvents)
                .Concat(settlements.SelectMany(s => s.TrainingGrowthEvents))
                .Concat(settlements.SelectMany(s => s.SelfTrainingGrowthEvents))
                .Concat(settlements.SelectMany(s => s.LoanReturns).SelectMany(r => r.Growth)) // 派遣から帰ってきたときの成長（§0.85）
                .ToList();
            var traitGrants = settlements.SelectMany(s => s.DungeonMissionResolutions).SelectMany(r => r.TraitGrantEvents)
                .Concat(settlements.SelectMany(s => s.TraitGrantEvents)).ToList();
            var transmissions = settlements.SelectMany(s => s.TraitTransmissionEvents).ToList();

            foreach (var a in state.Adventurers.Where(a => !a.IsRetired))
            {
                var counts = settlements
                    .Select(s => s.Activities.TryGetValue(a.Id, out var act) ? act : (WeekActivity?)null)
                    .Where(act => act != null)
                    .GroupBy(act => act!.Value)
                    .ToDictionary(g => g.Key, g => g.Count());
                var entry = new MonthlyAdventurer
                {
                    Name = a.Name,
                    Job = a.JobClass,
                    Age = a.Age,
                    Squad = state.SavedParties.FirstOrDefault(p => p.MemberIds.Contains(a.Id))?.Name ?? "",
                    Activities = string.Join("・", new[] { WeekActivity.Dispatched, WeekActivity.OnLoan, WeekActivity.Tournament, WeekActivity.Training, WeekActivity.SelfTraining, WeekActivity.Help, WeekActivity.Resting }
                        .Where(counts.ContainsKey)
                        .Select(act => $"{ActivityLabel(state, a, act)}{counts[act]}週")),
                    Gains = growth.Where(e => e.Adventurer.Id == a.Id).GroupBy(e => e.Stat)
                        .ToDictionary(g => g.Key, g => g.Sum(e => e.After - e.Before)),
                    HpPercent = a.MaxHP > 0 ? a.CurrentHP * 100 / a.MaxHP : 0,
                };
                if (entry.Activities.Length == 0)
                    entry.Activities = "この月に加わった";

                if (a.Injury != InjurySeverity.None)
                    entry.Status.Add(new MonthlyLine { Text = $"{(a.Injury == InjurySeverity.Severe ? "重傷" : "軽傷")}（あと{a.InjuryWeeksRemaining}週）", Tone = a.Injury == InjurySeverity.Severe ? MonthlyTone.Bad : MonthlyTone.Warning });
                if (a.IsPoisoned)
                    entry.Status.Add(new MonthlyLine { Text = $"毒（あと{a.PoisonWeeksRemaining}週）", Tone = MonthlyTone.Warning });
                foreach (var grant in traitGrants.Where(e => e.AdventurerId == a.Id))
                    entry.Status.Add(new MonthlyLine { Text = $"特性「{TraitCatalog.FindById(grant.TraitId)?.DisplayName ?? grant.TraitId}」が付いた", Tone = grant.Cause == TraitGrantCause.Awakening ? MonthlyTone.Good : MonthlyTone.Bad });
                foreach (var t in transmissions.Where(e => e.Student.Id == a.Id))
                    entry.Status.Add(new MonthlyLine { Text = $"教官{t.Trainer.Name}から特性「{TraitCatalog.FindById(t.TraitId)?.DisplayName ?? t.TraitId}」を受け継いだ", Tone = MonthlyTone.Good });
                foreach (var ev in settlements.SelectMany(s => s.TournamentsResolved))
                    foreach (var p in (ev.Result?.Placings ?? new List<TournamentPlacing>()).Where(p => p.AdventurerId == a.Id || (p.PartyId is Guid pid && state.SavedParties.FirstOrDefault(sp => sp.Id == pid)?.MemberIds.Contains(a.Id) == true)))
                        entry.Status.Add(new MonthlyLine { Text = $"{ev.Name}で{TournamentSystem.PlacingLabel(p.Placing)}", Tone = p.Placing == 1 ? MonthlyTone.Good : p.Placing <= 4 ? MonthlyTone.Normal : MonthlyTone.Warning });
                if (a.IsOnLoan)
                    entry.Status.Add(new MonthlyLine { Text = $"{CommissionBalance.FindClient(a.LoanClientId)?.Name ?? a.LoanClientId}へ派遣中（あと{a.LoanUntilWeek!.Value - state.WeekNumber + 1}週）", Tone = MonthlyTone.Normal });
                if (a.NeedsNegotiation)
                    entry.Status.Add(new MonthlyLine { Text = "満足度が低い（契約交渉が必要）", Tone = MonthlyTone.Bad });
                report.Adventurers.Add(entry);
            }
        }

        private static string ActivityLabel(GameState state, Adventurer a, WeekActivity activity) => activity switch
        {
            WeekActivity.Dispatched => "出撃",
            WeekActivity.OnLoan => "派遣",
            WeekActivity.Training => state.TrainingAssignments.TryGetValue(a.Id, out var f) ? $"訓練（{FacilityName(f)}）" : "訓練",
            WeekActivity.SelfTraining => a.SelfTrainingStat != null ? $"自主練（{a.SelfTrainingStat}）" : "自主練",
            WeekActivity.Help => "研究の手伝い",
            WeekActivity.Tournament => TournamentSystem.EntryOf(state, a.Id)?.Prep == TournamentPrep.Push ? "大会（追い込み）" : "大会（休養）",
            _ => "静養",
        };

        private static string FacilityName(FacilityType type) => type switch
        {
            FacilityType.Dormitory => "宿舎",
            FacilityType.Infirmary => "医務室",
            FacilityType.Tavern => "ギルド酒場",
            FacilityType.WarRoom => "作戦資料室",
            FacilityType.DrillHall => "鍛錬所",
            FacilityType.Academy => "学問所",
            FacilityType.SkillHall => "技巧所",
            FacilityType.RecruitmentOffice => "冒険者支援室",
            _ => type.ToString(),
        };

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
