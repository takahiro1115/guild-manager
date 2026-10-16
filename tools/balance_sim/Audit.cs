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
    public static string? MixMain, MixSurvey, MixGather; public static bool MixHp; // 体力7割未満を部隊から外すか（既定は外さない：方針の自動出撃が静養で待つ。外すと控えで組んで弱くなる）
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
        if (opts.Contains("norulers")) NoRulerFights = true;
        if (opts.Contains("noshop")) UseShops = false;
        if (opts.Contains("prudent")) PrudentRulers = true;
        if (opts.Contains("lineage")) Lineage = true;
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
        // 編成の切り分け（ギミックに合わせた組み方の損得を見る）：mix=主力・調査・採取・HP（例 mix=boss,top,top,hp）。主力 boss|top、調査 scout|top、採取 hp|top、体力7割未満を外す hp|nohp
        if (opts.FirstOrDefault(o => o.StartsWith("mix=")) is { } mix) { var m = mix.Substring(4).Split(','); MixMain = m[0]; MixSurvey = m[1]; MixGather = m[2]; MixHp = m[3] == "hp"; SquadMode = 2; }
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
        ConfigureUnfair(opts); // 理不尽チェック（Unfair.cs）
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
            UnfairTurnStart(); // 理不尽チェック（衝撃・危機の間の手番）
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
                int wages = s.Adventurers.Where(a => !a.IsRetired && !a.IsOnLoan).Sum(a => a.WeeklyWage);
                var result = week.ProcessWeek(s);
                {
                    long side = result.SideJobIncome?.FinalGold ?? 0, shop = result.ShopSales.Sum(x => x.Gold);
                    long stip = -result.StipendsPaid, upkeep = -result.UpkeepPaid, loan = -result.LoanRepaid, wage = -wages;
                    YBook("内職", side); YBook("ギルドの店", shop); YBook("手当", stip); YBook("維持費", upkeep); YBook("借金の返済", loan); YBook("週給（見込み）", wage);
                    YBook("決算のその他（迷宮・依頼・大会・訓練・退職金など）", s.Gold - goldBefore - side - shop - stip - upkeep - loan - wage);
                    if (Trace) Console.WriteLine($"   内訳 週給{wage} 手当{stip} 維持費{upkeep} 借金{loan} 内職{side} 店{shop} その他{s.Gold - goldBefore - side - shop - stip - upkeep - loan - wage} スポンサー達成{result.Sponsors.Completed.Count}未達{result.Sponsors.Failed.Count} 大会{result.TournamentsResolved.Count} 依頼{result.Commissions}");
                }
                monthWeeks.Add(new AutoSkipWeek(orderEvents, result));
                AfterWeek(result, goldBefore);
                UnfairWeek(result);
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
                EventReportCount += rep.EventReports.Count; MaxEventReports = Math.Max(MaxEventReports, rep.EventReports.Count);
                EventLeftovers += rep.EventReports.Count(e => e.Line.Contains('{'));
                foreach (var e in rep.EventReports) EventKinds[e.Kind] = EventKinds.GetValueOrDefault(e.Kind) + 1;
                foreach (var due in StorySystem.DueScenes(s, StoryTiming.AfterReport)) StorySystem.MarkSeen(s, due.SceneId); // 画面と同じく、月報のあとの場面を見たことにする（ルミナが加わる・出来事の報告の導入が場面と重なるかを画面どおりに）
                ChoosePlans(rep);
            }
        }
    }
    public int Reports, QuietReports;
    public int EventReportCount, MaxEventReports, EventLeftovers; // 出来事の報告（§0.141）
    public Dictionary<string, int> EventKinds = new();

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
        if (f.DismissalWarningIssued) yield return "最後通告"; else if (f.MoodEnteredCrisis) yield return "機嫌の危機";
        if (f.GoldTurnedNegative) yield return "所持金のマイナス";
        if (f.BankruptcyImminent) yield return "あと1週で破産";
        if (f.GameCleared) yield return "クリア";
        if (f.DefeatOccurred) yield return "敗北";
    }

    public int WeeksTotal, WeeksMoodMax, WeeksMoodCheerful, WeeksGold10k, WeeksAfterYear3, WeeksGold10kAfterYear3, WeeksCheerfulAfterYear3;
    /// <summary>満足度の段階ごとの冒険者・週（§0.101）と、出来事（§0.102）・維持費と手当（§0.103）の合計。</summary>
    public readonly int[] StageWeeks = new int[4];
    public int GoodIncidents, BadIncidents, UpkeepTotal, StipendTotal, PoachLeft, LoanRepaidTotal, SponsorWins, SponsorLosses, RetireesLeft, TrainingInjuries;

    /// <summary>お金の台帳（年 → 項目 → 増減の合計）。手番の手は前後の所持金の差、週の決算は内訳から数える。</summary>
    public readonly Dictionary<int, Dictionary<string, long>> YearLedger = new();

    void YBook(string key, long gold)
    {
        if (gold == 0) return;
        int year = GameCalendar.YearOf(s.WeekNumber);
        if (!YearLedger.TryGetValue(year, out var row)) YearLedger[year] = row = new Dictionary<string, long>();
        row[key] = row.GetValueOrDefault(key) + gold;
    }

    void YLed(string key, Action act)
    {
        int g = s.Gold;
        act();
        YBook(key, s.Gold - g);
    }

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
        YLed("支配者戦（手番）", RulerTurn); // 支配者戦（§0.127）
        YLed("経営の判断", AnswerManagement);
        if (UseNegotiation) YLed("契約交渉", HandleNegotiations);
        YLed("依頼（手番）", HandleCommissions);
        YLed("遺物・武具・素材の売買", HandleLoot);
        YLed("店で買う", Shop);
        YLed("施設", BuildFacility);
        YLed("研究・霊薬・秘薬・教官", CampaignUpkeep);
        YLed("大会の出場", EnterTournaments);
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
            bool hpOk(Adventurer a) => !MixHp || HpRatio(a) >= 0.7; // mix=…,nohp なら体力で外さない（切り分け用）
            if (i == 0)
            {
                order = SquadOrder.Dive;
                if (SquadMode >= 2 && target != null && MixMain != "top")
                {
                    var healthy = pool.Where(hpOk).ToList();
                    pick = healthy.Count >= 3 ? PickForBoss(target.Value.Boss, healthy) : pool.OrderByDescending(StatSum).Take(4).ToList();
                    field = target.Value.Field.Id;
                }
                else if (SquadMode >= 2 && target != null)
                {
                    pick = pool.Where(hpOk).OrderByDescending(StatSum).Take(4).ToList();
                    if (pick.Count < 3) pick = pool.OrderByDescending(StatSum).Take(4).ToList();
                    field = target.Value.Field.Id;
                }
                else pick = pool.OrderByDescending(a => RandSquad ? Rnd.NextDouble() : StatSum(a)).Take(4).ToList();
            }
            else if (i == 1 && !NoMoodGather && s.MasterMood < MasterMoodBalance.TierThresholdNormal && s.UnlockedSquadSlots < 3)
            {
                // 機嫌が平常より下で採取の部隊が無いうちは、2つ目の部隊を採取に回す（人なら危機の小窓を見てそうする。§0.139）
                order = SquadOrder.Gather;
                pick = pool.Where(hpOk).OrderByDescending(HpRatio).Take(3).ToList();
            }
            else if (i == 1)
            {
                order = SquadOrder.Survey;
                if (SquadMode >= 2 && MixSurvey == "top")
                    pick = pool.Where(hpOk).OrderByDescending(StatSum).Take(3).ToList();
                else if (SquadMode >= 2)
                {
                    pick = pool.Where(hpOk).OrderByDescending(a => ScoutingResolver.GetAnalysisValue(a) + ScoutingResolver.GetGuardValue(a) + ScoutingResolver.GetStealthValue(a)).Take(3).ToList();
                    var surveyBoss = SurveyableOf(CommissionSurveyTarget()) ?? (target != null ? SurveyableOf(target.Value.Boss) : null);
                    var surveyField = surveyBoss == null ? null : s.DungeonFields.FirstOrDefault(f => f.Bosses.Contains(surveyBoss));
                    if (surveyField != null) field = surveyField.Id;
                }
                else pick = pool.OrderByDescending(StatSum).Take(3).ToList();
            }
            else
            {
                order = SquadOrder.Gather;
                pick = SquadMode >= 2 && MixGather != "top" ? pool.Where(hpOk).OrderByDescending(HpRatio).Take(3).ToList() : pool.Where(a => SquadMode < 2 || hpOk(a)).OrderByDescending(StatSum).Take(3).ToList();
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
        {
            // お金の台帳：破産した回の、破産した年の前年と前々年の平均（1年あたり）。破産しなかった回は 7〜8年目の平均
            var broke = sims.Where(x => x.Defeat?.StartsWith("Bankruptcy") == true).ToList();
            var safe = sims.Except(broke).ToList();
            var keys = sims.SelectMany(x => x.YearLedger.Values.SelectMany(r => r.Keys)).Distinct().ToList();
            string Row(List<GameSim> list, Func<GameSim, IEnumerable<int>> years)
            {
                var parts = keys.Select(k =>
                {
                    var vals = list.SelectMany(x => years(x).Select(y => x.YearLedger.TryGetValue(y, out var r) ? r.GetValueOrDefault(k) : 0L)).ToList();
                    return (k, avg: vals.Count == 0 ? 0 : vals.Average());
                }).Where(p => Math.Abs(p.avg) >= 1).OrderBy(p => p.avg);
                return string.Join("　", parts.Select(p => $"{p.k} {p.avg:+0;-0}"));
            }
            int YearOfDefeat(GameSim x) => int.Parse(new string(x.Defeat!.SkipWhile(c => c != '（').Skip(1).TakeWhile(char.IsDigit).ToArray())) / 48 + 1;
            if (broke.Count > 0)
                Console.WriteLine($"お金の台帳（破産した{broke.Count}回の、破産の前々年と前年、1年あたりG）：" + Row(broke, x => new[] { YearOfDefeat(x) - 2, YearOfDefeat(x) - 1 }));
            if (safe.Count > 0)
                Console.WriteLine($"お金の台帳（破産しなかった{safe.Count}回の、7〜8年目、1年あたりG）：" + Row(safe, x => new[] { 7, 8 }));
            Console.WriteLine("お金の台帳（全回の5〜6年目、1年あたりG）：" + Row(sims, x => new[] { 5, 6 }));
        }
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
        Console.WriteLine($"出来事の報告（§0.141）：1か月あたり {(double)sims.Sum(x => x.EventReportCount) / Math.Max(1, sims.Sum(x => x.Reports)):F1}件・いちばん多い月 {sims.Max(x => x.MaxEventReports)}件・差し込みの埋め残し {sims.Sum(x => x.EventLeftovers)}件　多い順：" +
            string.Join("・", sims.SelectMany(x => x.EventKinds).GroupBy(kv => kv.Key).Select(g => (g.Key, N: g.Sum(kv => kv.Value))).OrderByDescending(x => x.N).Take(12).Select(x => $"{x.Key} {x.N}")));
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
        PrintUnfair(sims, weeks);
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
