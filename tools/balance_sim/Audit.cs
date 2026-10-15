// audit モード：判断の効き目の計測（§0.96、→ README・docs/検証レポート/2026-10-11_単調さと判断の手応え）。
// 画面と同じ流れ（部隊の方針で自動出撃 → 「次の月へ」で月の残りを進める → 月の途中で止まったら手番）で遊ばせ、
// プレイヤーの関わり方（段階0〜3）・選び方（くじ引き）・構えごとに、クリアの速さと「手番で何をしたか」を数える。
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

partial class GameSim
{
    // ==== 設定 ====
    public static int Level = 3;
    public static int SquadMode = 2;         // 0＝最初に1回だけ組む、1＝上位から順に組み直す、2＝狙うボスに合わせて組む
    public static bool UseTrainers = true, UseResearch = true, UseFacilities = true, UseTraining = true, UseRecruit = true, UseNegotiation = true;
    public static DoorStance AuditStance = DoorStance.Standard;
    public static bool RandHire, RandTourney, RandComm, RandEquip, RandBuild, RandTrain, RandSquad;
    /// <summary>ギルドの方針と育成への投資（§0.105）：オプション hardtrain・gentletrain・prizeshare・noinvest。</summary>
    public static TrainingPolicy AuditTrainingPolicy = TrainingPolicy.Normal;
    public static PrizePolicy AuditPrizePolicy = PrizePolicy.Guild;
    public static bool UseInvestments = true;
    Random? _rnd;
    Random Rnd => _rnd ??= new Random(s.Adventurers.Count * 31 + 7 + RunIndex * 1009);
    public int RunIndex;
    static Random RandFor(int seed) => new Random(seed * 7907 + 3);
    public readonly Dictionary<string, int> RetreatReasons = new();
    public int WaitingSquadWeeks, DispatchedSquadWeeks;

    public static void ConfigureAudit(HashSet<string> opts)
    {
        Campaign = true;
        Level = int.Parse(opts.FirstOrDefault(o => o.StartsWith("L"))?.Substring(1) ?? "3");
        // 段階ごとの既定
        bool l1 = Level >= 1, l2 = Level >= 2, l3 = Level >= 3;
        SquadMode = Level == 0 ? 0 : Level == 1 ? 1 : 2;
        UseRecruit = l1; UseNegotiation = l1;
        UseLoot = l2; UseShop = l2; UseFacilities = l2; UseResearch = l2; UseTrainers = l2; UseTraining = l2;
        TourneyMode = l2 ? "all" : "off";
        UseCommissions = l3; UseElixirs = l3; UseSoulFusion = l3;
        // 個別に外す
        if (opts.Contains("notourney")) TourneyMode = "off";
        if (opts.Contains("tourney")) TourneyMode = "all";
        if (opts.Contains("nocomm")) UseCommissions = false;
        if (opts.Contains("comm")) UseCommissions = true;
        if (opts.Contains("noloot")) { UseLoot = false; UseShop = false; }
        if (opts.Contains("nofac")) UseFacilities = false;
        if (opts.Contains("fac")) UseFacilities = true;
        if (opts.Contains("notrain")) UseTraining = false;
        if (opts.Contains("train")) UseTraining = true;
        if (opts.Contains("noresearch")) UseResearch = false;
        if (opts.Contains("simple")) SquadMode = 1;
        Trace = opts.Contains("trace");
        if (opts.Contains("bold")) AuditStance = DoorStance.Bold;
        if (opts.Contains("cautious")) AuditStance = DoorStance.Cautious;
        if (opts.Contains("noelixir")) UseElixirs = false;
        if (opts.Contains("hardtrain")) AuditTrainingPolicy = TrainingPolicy.Hard;
        if (opts.Contains("gentletrain")) AuditTrainingPolicy = TrainingPolicy.Gentle;
        if (opts.Contains("prizeshare")) AuditPrizePolicy = PrizePolicy.Share;
        if (opts.Contains("noinvest")) UseInvestments = false;
        if (opts.Contains("nosf")) UseSoulFusion = false;
        if (opts.Contains("nonego")) UseNegotiation = false;
        if (opts.Contains("norecruit")) UseRecruit = false;
        if (opts.Contains("notrainer")) UseTrainers = false;
        RandHire = opts.Contains("randhire"); RandTourney = opts.Contains("randtourney"); RandComm = opts.Contains("randcomm");
        RandEquip = opts.Contains("randequip"); RandBuild = opts.Contains("randbuild"); RandTrain = opts.Contains("randtrain"); RandSquad = opts.Contains("randsquad");
        SeedFrom = int.Parse(opts.FirstOrDefault(o => o.StartsWith("from="))?.Substring(5) ?? "0");
        NoTutorial = opts.Contains("notutorial");
        DumpDir = opts.FirstOrDefault(o => o.StartsWith("dumpdir="))?.Substring(8) ?? DumpDir;
        PlanPolicy = opts.FirstOrDefault(o => o.StartsWith("plan="))?.Substring(5) ?? "continue";
        DumpSaveAtWeek = int.Parse(opts.FirstOrDefault(o => o.StartsWith("dump="))?.Substring(5) ?? "0");
    }

    public static int DumpSaveAtWeek;
    public static bool NoTutorial;

    // ==== 記録 ====
    public readonly Dictionary<string, int> Actions = new();
    readonly HashSet<string> turnCats = new();
    int turnActions;
    public sealed record TurnRecord(int Week, bool MonthStart, int ActionCount, string[] Categories);
    public readonly List<TurnRecord> Turns = new();
    public readonly Dictionary<string, int> StopReasons = new();
    public int MidMonthStops;
    public int DoorRetreats, DoorEngages, DoorLosses;
    public int MonthsPlayed;
    public readonly List<int> GoldByYear = new();

    void Count(string k, int n = 1)
    {
        Actions[k] = Actions.GetValueOrDefault(k) + n;
        turnActions += n;
        turnCats.Add(k);
    }

    SquadOrderSystem? _orders;
    SquadOrderSystem Orders => _orders ??= new SquadOrderSystem(expedition);
    readonly SatisfactionSystem negotiation = new();

    // ==== 1回分 ====
    public void RunUi(int weeks)
    {
        AuditMode = true;
        int start = s.WeekNumber;
        bool first = true;
        if (NoTutorial) StorySystem.DisableTutorial(s);
        while (s.WeekNumber - start < weeks)
        {
            if (s.IsGameCleared) break;
            // 手番
            bool monthStart = GameCalendar.IsFirstWeekOfMonth(s.WeekNumber);
            turnActions = 0; turnCats.Clear();
            ActUi(first);
            first = false;
            Turns.Add(new TurnRecord(s.WeekNumber, monthStart, turnActions, turnCats.ToArray()));
            if (monthStart) MonthsPlayed++;

            // 次の月へ（月の残りを進める。止まる条件で止まる）
            var monthWeeks = new List<AutoSkipWeek>();
            int goldAtTurn = s.Gold;
            for (int i = 0; i < GameCalendar.WeeksPerMonth; i++)
            {
                int wk = s.WeekNumber;
                var orderEvents = Orders.Execute(s);
                WaitingSquadWeeks += orderEvents.Count(e => e.Action == SquadOrderAction.Waiting);
                DispatchedSquadWeeks += orderEvents.Count(e => e.Action == SquadOrderAction.Dispatched);
                int goldBefore = s.Gold;
                var result = week.ProcessWeek(s);
                monthWeeks.Add(new AutoSkipWeek(orderEvents, result));
                AfterWeek(result, goldBefore);
                if (s.DefeatReason != null) { Defeat = s.DefeatReason + $"（{s.WeekNumber}週）"; return; }
                if (s.IsGameCleared) return;
                if (DumpSaveAtWeek > 0 && s.WeekNumber == DumpSaveAtWeek)
                {
                    new SaveLoadService(DumpDir).Save(s);
                    Console.Error.WriteLine($"セーブを書き出した：{DumpDir}/savegame.json（{s.WeekNumber}週）");
                }
                if (result.Flags.ShouldStopMonth)
                {
                    if (!GameCalendar.IsLastWeekOfMonth(wk)) MidMonthStops++;
                    foreach (var reason in StopNames(result.Flags)) StopReasons[reason] = StopReasons.GetValueOrDefault(reason) + 1;
                    break;
                }
                if (GameCalendar.IsLastWeekOfMonth(wk)) break;
            }
            if (monthWeeks.Count > 0)
            {
                var rep = MonthlyReport.Build(s, monthWeeks, goldAtTurn);
                Reports++;
                if (rep.Highlights.Count == 0) QuietReports++;
                ChoosePlans(rep);
            }
        }
    }
    public int Reports, QuietReports;

    // 来月の作戦（§0.98）：月報の作戦にどう答えるか。continue＝何もしない（今までどおり）、best＝見込みの勝率が最も高い手、
    // survey・other＝あればその手、rand＝くじ引き。L1以上は手番の編成（FormSquads）が方針を書き直すので、効くのは主に L0。
    public static string PlanPolicy = "continue";
    public int PlansShown, PlansChosen;
    void ChoosePlans(MonthlyReport rep)
    {
        foreach (var plan in rep.Plans.Where(p => !p.Repeated))
        {
            PlansShown++;
            var option = PlanPolicy switch
            {
                "best" => plan.Options.OrderByDescending(o => o.WinChance).First(),
                "survey" => plan.Options.FirstOrDefault(o => o.Choice == PlanChoice.Survey),
                "other" => plan.Options.FirstOrDefault(o => o.Choice == PlanChoice.OtherBoss),
                "rand" => plan.Options[Rnd.Next(plan.Options.Count)],
                _ => null,
            };
            if (option == null) continue;
            if (NextMonthPlanSystem.Apply(s, plan, option.Choice))
            {
                PlansChosen++;
                Count("来月の作戦");
            }
        }
    }

    public static string DumpDir = "save_dump";

    static IEnumerable<string> StopNames(WeekResult f)
    {
        if (f.DeathOrPermanentInjuryOccurred) yield return "強制除籍";
        if (f.FieldUnlocked) yield return "新しいフィールド";
        if (f.SoulFusionBirthOccurred) yield return "娘の誕生";
        if (f.RecruitmentTrialOccurred) yield return "採用試験";
        if (f.CommissionDeadlineNear) yield return "依頼の期限";
        if (f.SatisfactionWarningOccurred) yield return "契約交渉";
        if (f.CommissionsOffered) yield return "依頼の到着";
        if (f.SquadStalledAtBoss) yield return "来月の作戦";
        if (f.GameCleared) yield return "クリア";
        if (f.DefeatOccurred) yield return "敗北";
    }

    public int WeeksTotal, WeeksMoodMax, WeeksMoodCheerful, WeeksGold10k, WeeksAfterYear3, WeeksGold10kAfterYear3, WeeksCheerfulAfterYear3;
    /// <summary>満足度の段階ごとの冒険者・週（§0.101）と、出来事（§0.102）・維持費と手当（§0.103）の合計。</summary>
    public readonly int[] StageWeeks = new int[4];
    public int GoodIncidents, BadIncidents, UpkeepTotal, StipendTotal, PoachLeft, LoanRepaidTotal, SponsorWins, SponsorLosses, RetireesLeft, TrainingInjuries;

    void AfterWeek(WeeklySettlementResult result, int goldBefore)
    {
        WeeksTotal++;
        foreach (var a in Active) StageWeeks[(int)a.MoraleStage]++;
        GoodIncidents += result.Incidents.Count(i => i.Good);
        BadIncidents += result.Incidents.Count(i => !i.Good);
        PoachLeft += result.Incidents.Count(i => i.Kind == IncidentKind.PoachLeft);
        UpkeepTotal += result.UpkeepPaid;
        StipendTotal += result.StipendsPaid;
        LoanRepaidTotal += result.LoanRepaid; // 資金の調達（§0.104）
        SponsorWins += result.Sponsors.Completed.Count;
        SponsorLosses += result.Sponsors.Failed.Count;
        RetireesLeft += result.RetireesLeft.Count; // 方針と進路（§0.105）
        TrainingInjuries += result.TrainingInjuries.Count;
        if (s.MasterMood >= 100) WeeksMoodMax++;
        if (s.MasterMood >= 80) WeeksMoodCheerful++;
        if (s.Gold >= 10000) WeeksGold10k++;
        if (s.WeekNumber > 48 * 3)
        {
            WeeksAfterYear3++;
            if (s.Gold >= 10000) WeeksGold10kAfterYear3++;
            if (s.MasterMood >= 80) WeeksCheerfulAfterYear3++;
        }
        CommDone += result.Commissions.Completed.Count;
        CommFailed += result.Commissions.Failed.Count;
        CommGold += result.Commissions.Completed.Sum(c => c.Gold);
        PatronUniques += result.Commissions.Completed.Count(c => c.PatronUnique != null);
        if (result.Arrivals.AnnouncedAnomaly != null) Anomalies++;
        DaughtersBorn += result.SoulFusionBirths.Count;
        foreach (var n in result.FacilityUnlocks) UnlockWeek.TryAdd((n.Facility, n.Level), result.Flags.Week);
        if (result.IsabellaVisited) VisitWeek = result.Flags.Week;
        foreach (var m in result.ExchangeMatches)
        {
            ExchangePlayed++;
            if (m.Won) { ExchangeWon++; if (m.FirstWin) FirstExchangeWinWeek = result.Flags.Week; }
        }
        if (result.GuestTrainer.Arrived != null) GuestArrivalWeek = result.Flags.Week;
        if (result.CommissionsUnlocked) CommissionsOpenWeek = s.CommissionsFromWeek;
        foreach (var ev in result.TournamentsResolved)
        {
            TournamentEntries += ev.Result?.Placings.Count ?? 0;
            if (ev.Result?.WinnerIsOurs == true)
            {
                TournamentWins++;
                if (ev.Grade == TournamentGrade.G1) FirstG1Week.TryAdd(ev.Result.Placings.First(p => p.Placing == 1).Discipline, result.Flags.Week);
            }
        }
        foreach (var r in result.DungeonMissionResolutions)
        {
            foreach (var inj in r.InjuryEvents)
                if (inj.Severity == InjurySeverity.Severe) SevereInjuries++; else LightInjuries++;
            if (r.DungeonResult != null)
            {
                ForcedRetired += r.DungeonResult.ForceRetiredAdventurerIds.Count;
                DoorEngages++;
                if (r.DungeonResult.Outcome != DungeonOutcome.Victory) DoorLosses++;
                RecordAssault(result.Flags.Week, r);
            }
            if (r.ArrivedAtBossDoor && r.DoorRetreatReason != null)
            {
                DoorRetreats++;
                string k = r.DoorRetreatReason.Contains("備え") ? "備え不足" : r.DoorRetreatReason.Contains("火力") ? "火力不足" : r.DoorRetreatReason.Contains("HP") ? "HP不足" : "その他";
                RetreatReasons[k] = RetreatReasons.GetValueOrDefault(k) + 1;
            }
        }
        if (result.Flags.RecruitmentTrialOccurred && UseRecruit)
            HoldTrial();
        if (s.WeekNumber % 48 == 1)
        {
            Snapshot();
            GoldByYear.Add(s.Gold);
        }
        if (Trace)
            Console.WriteLine($"w{result.Flags.Week}: G {goldBefore}→{s.Gold} 機嫌{s.MasterMood} 現役{Active.Count()} " +
                string.Join(" / ", result.DungeonMissionResolutions.Select(r => $"{r.MissionType}@{r.CurrentFloor}{(r.DungeonResult != null ? ":" + r.DungeonResult.Outcome : "")}{(r.ArrivedAtBossDoor ? "扉前" + (r.DoorRetreatReason ?? "挑む") : "")}{(r.ReturnedHome ? "帰還" : "")}")));
    }

    // ==== 手番 ====
    void ActUi(bool first)
    {
        // 物語の場面は、手番ごとに見たことにする（画面で小窓を閉じたのと同じ）
        foreach (var timing in new[] { StoryTiming.Interactive, StoryTiming.BeforeReport, StoryTiming.AfterReport })
            foreach (var sh in StorySystem.DueScenes(s, timing))
                StorySystem.MarkSeen(s, sh.SceneId);
        if (Level == 0)
        {
            if (first) SetupOnce();
            return;
        }
        RulerTurn(); // 支配者戦（§0.127）
        AnswerManagement();
        if (UseNegotiation) HandleNegotiations();
        HandleCommissions();
        HandleLoot();
        Shop();
        BuildFacility();
        CampaignUpkeep();
        EnterTournaments();
        FormSquads();
        if (UseTraining) AssignTraining();
    }

    void SetupOnce()
    {
        var a = Squad("第1部隊");
        var ids = Active.OrderByDescending(StatSum).Take(4).Select(x => x.Id).ToList();
        SetSquad(a, ids, SquadOrder.Dive, SavedParty.AutoFieldId);
    }

    SavedParty Squad(string name)
    {
        var p = s.SavedParties.FirstOrDefault(x => x.Name == name);
        if (p == null) { p = new SavedParty { Name = name }; s.SavedParties.Add(p); }
        return p;
    }

    void SetSquad(SavedParty sq, List<Guid> ids, SquadOrder order, string fieldId)
    {
        bool changed = !sq.MemberIds.SequenceEqual(ids) || sq.Order != order || sq.OrderFieldId != fieldId || sq.Stance != AuditStance;
        if (!changed) return;
        sq.MemberIds = ids;
        sq.Order = ids.Count == 0 ? SquadOrder.None : order;
        sq.OrderFieldId = fieldId;
        sq.Stance = AuditStance;
        Count("部隊の編成・方針");
    }

    void HandleNegotiations()
    {
        foreach (var a in Active.Where(a => a.NeedsNegotiation).ToList())
        {
            if (s.Gold - a.WeeklyWage * SatisfactionBalance.BonusWeeksEquivalent >= 1500)
                negotiation.PayBonus(s, a);
            else
                negotiation.RaiseWage(a, SatisfactionBalance.MinRaiseMultiplier);
            Count("契約交渉");
        }
    }

    void FormSquads()
    {
        bool monthStart = TrainingSystem.CanChangeAssignments(s);
        var names = new[] { "主力", "調査", "採取", "採取2" };
        var used = new HashSet<Guid>();
        // 出撃中の部隊の隊員はそのまま
        foreach (var n in names)
        {
            var sq = s.SavedParties.FirstOrDefault(x => x.Name == n);
            if (sq != null && SquadOrderSystem.IsOut(s, sq))
                foreach (var id in sq.MemberIds) used.Add(id);
        }
        bool Avail(Adventurer a) => !used.Contains(a.Id) && !a.IsDispatched && a.Injury != InjurySeverity.Severe
            && !TournamentSystem.IsEntered(s, a.Id) && (monthStart || !TrainingSystem.IsTraining(s, a.Id));

        var target = PickTarget();
        for (int i = 0; i < names.Length; i++)
        {
            var sq = Squad(names[i]);
            if (SquadOrderSystem.IsOut(s, sq)) continue;
            if (i >= s.UnlockedSquadSlots) { SetSquad(sq, new List<Guid>(), SquadOrder.None, SavedParty.AutoFieldId); continue; }
            var pool = Active.Where(Avail).ToList();
            List<Adventurer> pick;
            SquadOrder order;
            string field = SavedParty.AutoFieldId;
            if (i == 0)
            {
                order = SquadOrder.Dive;
                if (SquadMode >= 2 && target != null)
                {
                    var healthy = pool.Where(a => HpRatio(a) >= 0.7).ToList();
                    pick = healthy.Count >= 3 ? PickForBoss(target.Value.Boss, healthy) : pool.OrderByDescending(StatSum).Take(4).ToList();
                    field = target.Value.Field.Id;
                }
                else pick = pool.OrderByDescending(a => RandSquad ? Rnd.NextDouble() : StatSum(a)).Take(4).ToList();
            }
            else if (i == 1)
            {
                order = SquadOrder.Survey;
                if (SquadMode >= 2)
                {
                    pick = pool.Where(a => HpRatio(a) >= 0.7).OrderByDescending(a => ScoutingResolver.GetAnalysisValue(a) + ScoutingResolver.GetGuardValue(a) + ScoutingResolver.GetStealthValue(a)).Take(3).ToList();
                    var surveyBoss = SurveyableOf(CommissionSurveyTarget()) ?? (target != null ? SurveyableOf(target.Value.Boss) : null);
                    var surveyField = surveyBoss == null ? null : s.DungeonFields.FirstOrDefault(f => f.Bosses.Contains(surveyBoss));
                    if (surveyField != null) field = surveyField.Id;
                }
                else pick = pool.OrderByDescending(StatSum).Take(3).ToList();
            }
            else
            {
                order = SquadOrder.Gather;
                pick = SquadMode >= 2 ? pool.Where(a => HpRatio(a) >= 0.7).OrderByDescending(HpRatio).Take(3).ToList() : pool.OrderByDescending(StatSum).Take(3).ToList();
            }
            if (pick.Count == 0) continue;
            if (i == 0)
            {
                var leader = pick.OrderByDescending(a => a.GetEffectiveStat("LDR")).First();
                pick.Remove(leader); pick.Insert(0, leader);
            }
            foreach (var a in pick)
            {
                used.Add(a.Id);
                if (monthStart && TrainingSystem.IsTraining(s, a.Id)) training.Unassign(s, a.Id);
            }
            SetSquad(sq, pick.Select(a => a.Id).ToList(), order, field);
        }
    }

    void AssignTraining()
    {
        if (!TrainingSystem.CanChangeAssignments(s)) return;
        var inSquads = s.SavedParties.Where(p => p.Order != SquadOrder.None).SelectMany(p => p.MemberIds).ToHashSet();
        foreach (var a in Active.Where(a => !a.IsDispatched))
        {
            var before = s.TrainingAssignments.TryGetValue(a.Id, out var f0) ? (FacilityType?)f0 : null;
            if (inSquads.Contains(a.Id) || HpRatio(a) < 0.5 || a.Injury == InjurySeverity.Severe || TournamentSystem.IsEntered(s, a.Id))
            {
                if (before != null) { training.Unassign(s, a.Id); Count("訓練の割り振り"); }
                continue;
            }
            var best = TrainingFacilities
                .Where(f => training.GetSlotCapacity(s, f) > 0)
                .OrderByDescending(f => RandTrain ? Rnd.NextDouble() : FacilityBalance.GetTrainingTargetStats(f).Sum(n => Acc.Pa(a, n) - Acc.Stat(a, n)))
                .ToList();
            if (RandTrain && before != null) continue; // くじで決めたら、その子はそのまま
            if (best.Count == 0) continue;
            if (before != null && before == best[0]) continue; // そのまま
            if (before != null) training.Unassign(s, a.Id);
            foreach (var f in best)
                if (training.TryAssign(s, a.Id, f)) { if (before != f) Count("訓練の割り振り"); break; }
        }
    }

    // ==== まとめ ====
    public static void RunAudit(int runs, int weeks)
    {
        Console.WriteLine($"## 段階{Level}（編成{SquadMode}・構え{SquadOrderSystem.StanceLabel(AuditStance)}・採用{On(UseRecruit)}・交渉{On(UseNegotiation)}・遺物と店{On(UseLoot)}・施設{On(UseFacilities)}・研究{On(UseResearch)}・教官{On(UseTrainers)}・訓練{On(UseTraining)}・大会{(TourneyMode == "off" ? "×" : "○")}・依頼{On(UseCommissions)}・霊薬{On(UseElixirs)}・秘薬{On(UseSoulFusion)}）、{runs}回、最大{weeks / 48}年");
        var sims = new List<GameSim>();
        for (int i = 0; i < runs; i++)
        {
            var sim = new GameSim(i + SeedFrom) { RunIndex = i + SeedFrom };
            sim.RunUi(weeks);
            sims.Add(sim);
        }
        var cleared = sims.Where(x => x.s.IsGameCleared).Select(x => (double)GameCalendar.YearOf(x.s.ClearedAtWeek!.Value)).ToList();
        Console.WriteLine(cleared.Count == 0 ? $"クリア：0/{runs}回" : $"クリア：{cleared.Count}/{runs}回、{Median(cleared):F0}年目〔{cleared.Min():F0}〜{cleared.Max():F0}〕");
        Console.WriteLine($"敗北：{string.Join(", ", sims.Select(x => x.Defeat ?? "なし"))}");
        string YearOfWeek(int? w) => w is int v ? $"{GameCalendar.YearOf(v)}" : "―";
        Console.WriteLine($"ヴェスペラ（§0.127）：退かせた年 {string.Join(", ", sims.Select(x => YearOfWeek(x.VesperaRetreatWeek)))}　倒した年 {string.Join(", ", sims.Select(x => YearOfWeek(x.VesperaDefeatWeek)))}　決闘 {sims.Sum(x => x.VesperaDuels)}回・強制除籍 {sims.Sum(x => x.VesperaFallen)}・一命を取り留めた {sims.Sum(x => x.VesperaNearDeath)}");
        Console.WriteLine($"メルジーネ（§0.129）：退かせた年 {string.Join(", ", sims.Select(x => YearOfWeek(x.MelusineRetreatWeek)))}　倒した年 {string.Join(", ", sims.Select(x => YearOfWeek(x.MelusineDefeatWeek)))}　戦い {sims.Sum(x => x.MelusineBattles)}回");
        Console.WriteLine($"ネフェリス（§0.132）：退かせた年 {string.Join(", ", sims.Select(x => YearOfWeek(x.NepherisRetreatWeek)))}　倒した年 {string.Join(", ", sims.Select(x => YearOfWeek(x.NepherisDefeatWeek)))}　戦い {sims.Sum(x => x.NepherisBattles)}回");
        Console.WriteLine($"イグニア（§0.131）：退かせた年 {string.Join(", ", sims.Select(x => YearOfWeek(x.IgniaRetreatWeek)))}　倒した年 {string.Join(", ", sims.Select(x => YearOfWeek(x.IgniaDefeatWeek)))}　戦い {sims.Sum(x => x.IgniaBattles)}回");
        Console.WriteLine($"オルテンシア（§0.130）：退かせた年 {string.Join(", ", sims.Select(x => YearOfWeek(x.OrtensiaRetreatWeek)))}　倒した年 {string.Join(", ", sims.Select(x => YearOfWeek(x.OrtensiaDefeatWeek)))}　戦い {sims.Sum(x => x.OrtensiaBattles)}回");
        Console.WriteLine($"撃破（最終、50体中）：{string.Join(", ", sims.Select(x => x.s.DungeonFields.Sum(f => f.Bosses.Count(b => b.IsDefeated))))}");
        Console.WriteLine("撃破数の推移（各年のはじめ、中央値）：" + string.Join("・", Enumerable.Range(0, weeks / 48).Select(y =>
        {
            var l = sims.Where(x => x.YearlyKills.Count > y).Select(x => (double)x.YearlyKills[y]).ToList();
            return l.Count == 0 ? null : $"{y + 2}年目 {Median(l):F0}";
        }).OfType<string>()));
        double years = sims.Sum(x => (x.s.WeekNumber - 1) / 48.0);
        int turns = sims.Sum(x => x.Turns.Count);
        int idle = sims.Sum(x => x.Turns.Count(t => t.ActionCount == 0));
        int idleMonth = sims.Sum(x => x.Turns.Count(t => t.MonthStart && t.ActionCount == 0));
        int monthTurns = sims.Sum(x => x.Turns.Count(t => t.MonthStart));
        Console.WriteLine($"手番：{turns}回（1年あたり {turns / years:F1}回、うち月のはじめ {monthTurns / years:F1}回・月の途中で止まった {sims.Sum(x => x.MidMonthStops) / years:F1}回）");
        Console.WriteLine($"何もしなかった手番：{idle}/{turns}（{100.0 * idle / Math.Max(1, turns):F0}%）、月のはじめに限ると {idleMonth}/{monthTurns}（{100.0 * idleMonth / Math.Max(1, monthTurns):F0}%）");
        Console.WriteLine($"機嫌：上機嫌（80以上）の週 {100.0 * sims.Sum(x => x.WeeksMoodCheerful) / Math.Max(1, sims.Sum(x => x.WeeksTotal)):F0}%・上限100の週 {100.0 * sims.Sum(x => x.WeeksMoodMax) / Math.Max(1, sims.Sum(x => x.WeeksTotal)):F0}%（4年目以降の上機嫌 {100.0 * sims.Sum(x => x.WeeksCheerfulAfterYear3) / Math.Max(1, sims.Sum(x => x.WeeksAfterYear3)):F0}%）　所持金1万G以上の週 {100.0 * sims.Sum(x => x.WeeksGold10k) / Math.Max(1, sims.Sum(x => x.WeeksTotal)):F0}%（4年目以降 {100.0 * sims.Sum(x => x.WeeksGold10kAfterYear3) / Math.Max(1, sims.Sum(x => x.WeeksAfterYear3)):F0}%）");
        { int stTotal = Math.Max(1, sims.Sum(x => x.StageWeeks.Sum())); string P(int i) => $"{100.0 * sims.Sum(x => x.StageWeeks[i]) / stTotal:F0}%";
          Console.WriteLine($"満足度の段階（冒険者・週）：上機嫌 {P(1)}・ふつう {P(0)}・不満 {P(2)}・限界 {P(3)}　出来事（1年あたり）：プラス {sims.Sum(x => x.GoodIncidents) / years:F1}・マイナス {sims.Sum(x => x.BadIncidents) / years:F1}　引き抜きで移籍 {sims.Sum(x => x.PoachLeft)}　維持費 {sims.Sum(x => x.UpkeepTotal) / years:F0}G/年・手当 {sims.Sum(x => x.StipendTotal) / years:F0}G/年"); Console.WriteLine($"資金の調達（§0.104）：借金の返済 {sims.Sum(x => x.LoanRepaidTotal) / years:F0}G/年　スポンサー契約 達成 {sims.Sum(x => x.SponsorWins)}・未達 {sims.Sum(x => x.SponsorLosses)}（全回の合計）"); Console.WriteLine($"方針と進路（§0.105）：役職につかずに去った引退者 {sims.Sum(x => x.RetireesLeft) / years:F1}人/年　厳しい訓練のけが {sims.Sum(x => x.TrainingInjuries) / years:F1}回/年"); }
        Console.WriteLine($"月報の「主な出来事」が空だった回：{sims.Sum(x => x.QuietReports)}/{sims.Sum(x => x.Reports)}（{100.0 * sims.Sum(x => x.QuietReports) / Math.Max(1, sims.Sum(x => x.Reports)):F0}%）");
        Console.WriteLine($"1手番あたりの操作：平均 {sims.Sum(x => x.Turns.Sum(t => t.ActionCount)) / (double)Math.Max(1, turns):F1}回");
        var cats = sims.SelectMany(x => x.Actions).GroupBy(kv => kv.Key).Select(g => (g.Key, Sum: g.Sum(kv => kv.Value))).OrderByDescending(x => x.Sum).ToList();
        Console.WriteLine("操作の内訳（1年あたり）：" + string.Join("・", cats.Select(c => $"{c.Key} {c.Sum / years:F1}")));
        // 操作のある手番のうち、操作の種類が「鑑定・売却・着せ替え・購入」だけの手番（決まった答えの作業）
        var chores = new HashSet<string> { "鑑定", "武具の売却", "素材の売却", "装備の着せ替え", "装備の購入" };
        int choreOnly = sims.Sum(x => x.Turns.Count(t => t.ActionCount > 0 && t.Categories.All(chores.Contains)));
        Console.WriteLine($"作業（鑑定・売却・着せ替え・購入）だけの手番：{choreOnly}/{turns}（{100.0 * choreOnly / Math.Max(1, turns):F0}%）");
        var stops = sims.SelectMany(x => x.StopReasons).GroupBy(kv => kv.Key).Select(g => $"{g.Key} {g.Sum(kv => kv.Value) / years:F1}");
        Console.WriteLine("止まった理由（1年あたり）：" + string.Join("・", stops));
        Console.WriteLine("扉前の撤退の理由：" + string.Join("・", sims.SelectMany(x => x.RetreatReasons).GroupBy(kv => kv.Key).Select(g => $"{g.Key} {g.Sum(kv => kv.Value)}")));
        Console.WriteLine($"部隊の週：出撃 {sims.Sum(x => x.DispatchedSquadWeeks) / years:F1}回/年・待機（静養など） {sims.Sum(x => x.WaitingSquadWeeks) / years:F1}回/年");
        Console.WriteLine($"来月の作戦（{PlanPolicy}）：出た {sims.Sum(x => x.PlansShown)}・選んで方針を変えた {sims.Sum(x => x.PlansChosen)}");
        Console.WriteLine($"扉前：挑んだ {sims.Sum(x => x.DoorEngages)}（うち負け {sims.Sum(x => x.DoorLosses)}）・撤退 {sims.Sum(x => x.DoorRetreats)}　強制除籍 {sims.Sum(x => x.ForcedRetired)}・重傷 {sims.Sum(x => x.SevereInjuries)}");
        Console.WriteLine($"所持金（各年のはじめ、中央値）：" + string.Join("・", Enumerable.Range(0, Math.Min(15, weeks / 48)).Select(y =>
        {
            var l = sims.Where(x => x.GoldByYear.Count > y).Select(x => (double)x.GoldByYear[y]).ToList();
            return l.Count == 0 ? null : $"{y + 2}年目 {Median(l):F0}G";
        }).OfType<string>()));
        Console.WriteLine();
    }

    static string On(bool b) => b ? "○" : "×";

    void BuildRandom()
    {
        if (s.UnderConstruction != null) return;
        var options = Enum.GetValues<FacilityType>()
            .Where(f => !FacilitySystem.IsBlockedByLevelCap(s, f) && s.GetFacilityLevel(f) < 5
                && CanAffordUpgrade(f))
            .ToList();
        if (options.Count == 0) return;
        var f = options[Rnd.Next(options.Count)];
        var spec = !FacilitySystem.NeedsSpecialtyChoice(f, s.GetFacilityLevel(f)) ? FacilitySpecialty.None : FacilityBalance.GetDefaultSpecialty(f);
        if (facility.TryStartConstruction(s, f, spec)) Count("施設の着工");
    }
}
