// バランス調整用のシミュレーター（→ README.md）。GuildManager.Core の計算・週次決算をそのまま使う。
//   dotnet run --project tools/balance_sim -- static          全能力Sの4人部隊で倒せる最深ボス・1F→100Fの週数
//   dotnet run --project tools/balance_sim -- door            目標のボスの扉前まで潜る週数
//   dotnet run --project tools/balance_sim -- ideal           新人1名を理想的に育てた場合の伸び
//   dotnet run --project tools/balance_sim -- game 10 384     通しのシミュレーション（回数・最大週数）
//   dotnet run --project tools/balance_sim -- game 10 384 free   同・お金の制約を外す
//   dotnet run --project tools/balance_sim -- game 10 384 frugal 同・装備と施設を買わない
//   dotnet run --project tools/balance_sim -- anchor          要求値と目安部隊の値
//   dotnet run --project tools/balance_sim -- game 1 60 trace    同・1週ごとの経過を出す
// バランス値は実行ファイルの隣の 04_バランス表（docs/04_バランス表 のコピー）から読む。
using System.Text;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;

Console.OutputEncoding = Encoding.UTF8;
string mode = args.Length > 0 ? args[0] : "all";

if (mode is "all" or "ideal")
    IdealGrowth.Run();
if (mode is "anchor")
    AnchorTable.Run();
if (mode is "door")
    DoorTable.Run();
if (mode is "all" or "static")
    StaticTables.Run();
if (mode is "all" or "game")
{
    int runs = args.Length > 1 ? int.Parse(args[1]) : 10;
    GameSim.Trace = args.Length > 3 && args[3] == "trace";
    GameSim.FreeMoney = args.Length > 3 && args[3] == "free";
    GameSim.Frugal = args.Length > 3 && args[3] == "frugal";
    int weeks = args.Length > 2 ? int.Parse(args[2]) : 48 * 8;
    GameSim.RunMany(runs, weeks);
}
if (mode is "campaign")
{
    // 5フィールドを進めて深淵100F（クリア、→ 03 §8.2）まで回す。教官・研究・宿舎・秘薬も使う。
    int runs = args.Length > 1 ? int.Parse(args[1]) : 10;
    int weeks = args.Length > 2 ? int.Parse(args[2]) : 48 * 30;
    GameSim.Campaign = true;
    var opts = args.Skip(3).ToHashSet();
    GameSim.UseSoulFusion = !opts.Contains("nosf");
    GameSim.Trace = opts.Contains("trace");
    GameSim.UseElixirs = !opts.Contains("noelixir");
    // 待機中の過ごし方（§0.73）：selftrain＝機嫌が60以上の週は全員が自主練、下回ったら研究を手伝う（既定は全員が研究を手伝う）
    GameSim.SelfTrainAll = opts.Contains("selftrain") || opts.Contains("selftrainall");
    GameSim.SelfTrainMinMood = opts.Contains("selftrainall") ? 0 : 60; // selftrainall＝機嫌にかかわらず全員が自主練
    // 施設の専門（§0.76）：rivalry＝訓練施設を切磋琢磨にする（既定は精鋭）
    GameSim.UseRivalry = opts.Contains("rivalry");
    // 大会（§0.82）：nomain＝主力4名は個人の大会に出さない、notourney＝大会に出ない
    GameSim.TourneyMode = opts.Contains("notourney") ? "off" : opts.Contains("nomain") ? "nomain" : "all";
    // 依頼（§0.64）：nocomm＝依頼を受けない、old＝依頼も迷宮の異変も無し（§0.63までと同じ条件で比べる）
    GameSim.UseCommissions = !opts.Contains("nocomm") && !opts.Contains("old");
    GameSim.NoAnomaly = opts.Contains("old");
    GameSim.SteerSurvey = !opts.Contains("nosteer");
    GameSim.SeedFrom = int.Parse(opts.FirstOrDefault(o => o.StartsWith("from="))?.Substring(5) ?? "0");
    GameSim.AcceptTypes = opts.FirstOrDefault(o => o.StartsWith("types="))?.Substring(6);
    GameSim.RunCampaign(runs, weeks);
}

// ============================================================================================
// 1. 静的な表：全能力S・4人部隊（装備・特性なし）で、どこまで倒せるか・何週で潜れるか
// ============================================================================================
static class StaticTables
{
    public static Adventurer Make(JobClass job, int s)
    {
        var a = new Adventurer
        {
            Name = job.ToString(), JobClass = job, Age = 20,
            STR = s, AGI = s, VIT = s, MND = s, DEX = s, LDR = s, INT = s,
            PA_STR = 100, PA_AGI = 100, PA_VIT = 100, PA_MND = 100, PA_DEX = 100, PA_LDR = 100, PA_INT = 100,
        };
        a.CurrentHP = a.MaxHP;
        return a;
    }

    public static Party Squad(int s, int n = 4)
    {
        var p = new Party();
        var jobs = new[] { JobClass.Warrior, JobClass.Mage, JobClass.Cleric, JobClass.Ranger };
        for (int i = 0; i < n; i++) p.TryAdd(Make(jobs[i], s));
        return p;
    }

    public static void Run()
    {
        Console.WriteLine("## A. 全能力S・4人部隊（装備・特性・研究なし、HP満タン）");
        Console.WriteLine();
        Console.WriteLine("| S | 討伐火力 | 倒せる最深ボス（解析なし） | 同（完全解析+20%） | 走破力 | 1F→100F の週数（区間すべて未解析） | 同（すべて完全解析） | 同（解析50%） |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|");
        var field = SampleData.CreateDefaultFields()[0];
        foreach (int s in new[] { 25, 30, 40, 50, 60, 70, 80, 90, 100 })
        {
            var party = Squad(s);
            var boss10 = field.Bosses[0];
            double power = DungeonPowerCalculator.PartyPower(party.Members);
            int maxNoIntel = 0, maxFull = 0;
            foreach (var b in field.Bosses)
            {
                double req = DungeonResolver.RequiredPower(b);
                if (power >= req) maxNoIntel = b.Floor;
                if (power * (1 + DungeonBalance.FullIntelDamageBonus) >= req) maxFull = b.Floor;
            }
            double trav = DungeonTraversalResolver.CalculateTraversalScore(party);
            Console.WriteLine($"| {s} | {power:F0} | {maxNoIntel}F | {maxFull}F | {trav:F0} | {DiveWeeks(party, 0)} | {DiveWeeks(party, 1.0)} | {DiveWeeks(party, 0.5)} |");
        }
        Console.WriteLine();
        Console.WriteLine("要求火力：" + string.Join("・", field.Bosses.Select(b => $"{b.Floor}F={DungeonResolver.RequiredPower(b):F0}")));
        Console.WriteLine();

        Console.WriteLine("## B. 1週ごとの進軍（全能力S、全ボス撃破済み・区間未解析）：出発階層 → 到達階層");
        foreach (int s in new[] { 25, 50, 90 })
        {
            var party = Squad(s);
            var f = FieldAllDefeated(0);
            var steps = new List<string>();
            int floor = 1;
            for (int w = 0; w < 60 && floor < 100; w++)
            {
                int next = DungeonTraversalResolver.PredictFloorAfter(f, floor, DungeonTraversalResolver.CalculateTraversalScore(party));
                steps.Add($"{next}");
                floor = next;
            }
            Console.WriteLine($"- S={s}: 1→" + string.Join("→", steps));
        }
        Console.WriteLine();
    }

    static DungeonField FieldAllDefeated(double intel)
    {
        var f = SampleData.CreateDefaultFields()[0];
        foreach (var b in f.Bosses) { b.IsDefeated = true; b.IntelRate = intel; }
        return f;
    }

    /// <summary>全ボス撃破済みの森を1Fから100Fまで潜るのに要る週数（1週＝進軍1回）。</summary>
    public static string DiveWeeks(Party party, double intel)
    {
        var f = FieldAllDefeated(intel);
        double score = DungeonTraversalResolver.CalculateTraversalScore(party);
        int floor = 1;
        for (int w = 1; w <= 500; w++)
        {
            floor = DungeonTraversalResolver.PredictFloorAfter(f, floor, score);
            if (floor >= 100) return w.ToString();
        }
        return ">500";
    }
}

// ============================================================================================
// 2. 通しのシミュレーション：Core の週次決算をそのまま回し、機械的なプレイヤーの方針で操作する
// ============================================================================================
record BossKill(int Week, int Floor, double SquadAvgStat, double SquadWeightedStat, double Margin, double Intel, int DivesForBoss);

class GameSim
{
    readonly GameState s;
    readonly RecruitmentSystem recruitment;
    readonly WeekProcessingSystem week;
    readonly DungeonExpeditionSystem expedition;
    readonly TrainingSystem training;
    readonly FacilitySystem facility;
    readonly AppraisalSystem appraisal;
    readonly EconomySystem economy = new();
    readonly EquipmentSystem equipment = new();
    public static bool UseLoot = true;
    public static bool Grind = true;
    public static bool FreeMoney;
    // 依頼（§0.64）：受けるか（campaign の既定は受ける）と、迷宮の異変を消して比べるか
    public static bool UseCommissions;
    public static bool NoAnomaly;
    public static bool SteerSurvey = true;
    /// <summary>campaign の最初のシード（from=N。1回だけ詳しく見るとき用）。</summary>
    public static int SeedFrom;
    /// <summary>受ける依頼の種類を絞る（例：types=Defeat,Survey）。null なら全種類。</summary>
    public static string? AcceptTypes;
    readonly CommissionSystem commissions;
    public int CommDone, CommFailed, CommGold, Loans, PatronUniques, Anomalies;
    public readonly List<BossKill> Kills = new();
    public readonly List<string> Yearly = new();
    public readonly List<int> RosterByYear = new();
    public int ForcedRetired;
    public int SevereInjuries, LightInjuries;
    public string? Defeat;
    public int DivesTotal;
    public static bool Trace;
    readonly Dictionary<int, int> divesPerBoss = new();
    public readonly List<(int Week, int Floor, int Weeks)> DoorArrivals = new();

    public GameSim(int seed)
    {
        int k = seed * 7919;
        var satisfaction = new SatisfactionSystem();
        var compat = new CompatibilitySystem(new SeededRng(2525 + k));
        var growth = new GrowthSystem(new SeededRng(7 + k));
        training = new TrainingSystem(new SeededRng(831 + k));
        recruitment = new RecruitmentSystem(new SeededRng(2024 + k));
        facility = new FacilitySystem();
        appraisal = new AppraisalSystem(new SeededRng(4649 + k));
        expedition = new DungeonExpeditionSystem(
            new ScoutingResolver(new SeededRng(1453 + k)), new DungeonResolver(new SeededRng(1588 + k)),
            satisfaction, compat,
            new DungeonTraversalResolver(new SeededRng(1719 + k)), new GatheringResolver(new SeededRng(1848 + k)),
            new GrowthSystem(new SeededRng(1907 + k)), new SeededRng(1969 + k));
        commissions = new CommissionSystem(new SeededRng(2718 + k));
        week = new WeekProcessingSystem(new MasterMoodSystem(), new EconomySystem(), training, new InjuryRecoverySystem(),
            new RestRecoverySystem(), growth, satisfaction, new AgingSystem(new SeededRng(99 + k)), facility,
            new DefeatSystem(), recruitment, expedition, commissions, isabellaSystem: new IsabellaSystem(new SeededRng(4049 + k)));

        s = new GameState { Adventurers = SampleData.CreateStarterAdventurers(), DungeonFields = SampleData.CreateDefaultFields() };
        var draft = recruitment.StartInitialDraft(s);
        for (int guard = 0; guard < 10 && !draft.IsComplete; guard++)
        {
            var best = draft.Offers.OrderByDescending(o => TotalPa(o.Candidate)).First();
            recruitment.TryDraftHire(s, draft, best);
        }
    }

    static int TotalPa(Adventurer a) => Acc.All.Sum(n => Acc.Pa(a, n));
    static double StatSum(Adventurer a) => DungeonBalance.GetBossPowerWeights(a.JobClass).Sum(w => a.GetEffectiveStat(w.Stat) * w.Weight); // 職業ごとの重み（§0.72）
    static double WeightSum => DungeonBalance.BossPowerWeights.Sum(w => w.Weight);
    static double HpRatio(Adventurer a) => (double)a.CurrentHP / a.MaxHP;

    /// <summary>
    /// 狙うボスに合わせて主力4名を選ぶ（§0.68：ギミックへの備えは編成で決まる）。火力上位8名から4名の組を総当たりし、
    /// 討伐火力÷要求火力（ギミック込み）が最も高い組を選ぶ。即死級の備えが半分未満の組は避け、備えの平均を少しだけ加点する。
    /// </summary>
    List<Adventurer> PickForBoss(FloorBoss boss, List<Adventurer> pool)
    {
        var top = pool.OrderByDescending(StatSum).Take(8).ToList();
        int k = Math.Min(4, top.Count);
        List<Adventurer> best = top.Take(k).ToList();
        double bestScore = double.MinValue;
        foreach (var combo in Combinations(top, k))
        {
            var party = new Party();
            foreach (var a in combo) party.TryAdd(a);
            double score = DungeonResolver.CalculateBossPower(party, boss) / DungeonResolver.RequiredPower(boss, s, party);
            if (boss.Gimmicks.Any(g => g.Type == BossGimmickType.InstantKill && DungeonResolver.Readiness(g, combo) < 0.5)) score -= 10;
            if (boss.Gimmicks.Count > 0) score += 0.05 * boss.Gimmicks.Average(g => DungeonResolver.Readiness(g, combo));
            if (score > bestScore) { bestScore = score; best = combo; }
        }
        return best;
    }

    static IEnumerable<List<Adventurer>> Combinations(List<Adventurer> items, int k, int start = 0)
    {
        if (k == 0) { yield return new List<Adventurer>(); yield break; }
        for (int i = start; i <= items.Count - k; i++)
            foreach (var rest in Combinations(items, k - 1, i + 1))
            {
                rest.Insert(0, items[i]);
                yield return rest;
            }
    }
    DungeonField Forest => s.DungeonFields.First(f => f.Order == 1);
    IEnumerable<Adventurer> Active => s.Adventurers.Where(a => !a.IsRetired && !a.IsOnLoan); // 派遣中（§0.85）は先方にいる

    // ==== campaign モード（5フィールド→深淵100F） ====
    public static bool Campaign;
    public static bool UseSoulFusion = true;
    public static bool UseElixirs = true;
    public static bool SelfTrainAll;
    public static bool UseRivalry;
    public static int SelfTrainMinMood = 60;
    public int ResearchCreditUsed;
    public int ElixirsGiven;

    /// <summary>
    /// campaign で買い物・研究・霊薬・秘薬に使わずに残す額：今年度末に満期を迎える者（年度末に26歳になる25歳。年の後半から数える）の退職金の見込み＋週給4週分＋3000G。
    /// 残しておかないと、年度末の退職金が重なって破産する。
    /// </summary>
    int Reserve => Active.Where(a => a.Age >= 26 || (a.Age == 25 && GameCalendar.WeekOfYear(s.WeekNumber) >= 30)).Sum(AgingSystem.CalculateSeverancePay) + Active.Sum(a => a.WeeklyWage) * 4 + 3000;
    readonly AdvisorSystem advisors = new();
    SoulFusionSystem? soulFusion;
    public readonly List<(int Order, int Floor, int Week)> FieldKills = new();
    public int DaughtersBorn;
    public int TrainerWeeks;

    /// <summary>
    /// 次に狙うボス。森だけのモードでは森の次のボス。campaign では、開いているフィールドの次のボスのうち
    /// 要求火力が最も低いもの（深淵を開くための各フィールド20Fも自然にこの順で倒す）。
    /// </summary>
    (DungeonField Field, FloorBoss Boss)? PickTarget()
    {
        if (!Campaign)
            return Forest.GetNextActiveBoss() is { } b ? (Forest, b) : null;
        return s.DungeonFields.Where(f => f.IsUnlocked)
            .Select(f => (Field: f, Boss: f.GetNextActiveBoss()))
            .Where(x => x.Boss != null)
            .OrderBy(x => DungeonResolver.RequiredPower(x.Boss!))
            .Select(x => ((DungeonField, FloorBoss)?)(x.Field, x.Boss!))
            .FirstOrDefault();
    }

    public void Run(int weeks)
    {
        for (int w = 0; w < weeks; w++)
        {
            if (Campaign ? s.IsGameCleared : Forest.Bosses.All(b => b.IsDefeated))
                break;
            int actBefore = s.Gold;
            Act();
            Book("その他の行動", s.Gold - actBefore - actBooked);
            actBooked = 0;
            if (FreeMoney) s.Gold = Math.Max(s.Gold, 100000);
            int goldBefore = s.Gold;
            int wages = s.Adventurers.Sum(a => a.WeeklyWage);
            int trainees = s.TrainingAssignments.Count;
            if (NoAnomaly) s.Anomaly = null;
            var result = week.ProcessWeek(s);
            CommDone += result.Commissions.Completed.Count;
            CommFailed += result.Commissions.Failed.Count;
            CommGold += result.Commissions.Completed.Sum(c => c.Gold);
            PatronUniques += result.Commissions.Completed.Count(c => c.PatronUnique != null);
            if (result.Arrivals.AnnouncedAnomaly != null && !NoAnomaly) Anomalies++;
            BookSettlement(result, s.Gold - goldBefore, wages, trainees);
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
            TrainerWeeks += s.AssignedTrainers.Count(kv => kv.Value != null);
            if (Trace)
                Console.WriteLine($"w{result.Flags.Week}: G {goldBefore}→{s.Gold} 機嫌{s.MasterMood} 現役{Active.Count()} 週給計{Active.Sum(a => a.WeeklyWage)} 訓練{s.TrainingAssignments.Count} 工事{s.UnderConstruction?.Type} " +
                    string.Join(" / ", result.DungeonMissionResolutions.Select(r => $"{r.MissionType}@{r.CurrentFloor}{(r.DungeonResult != null ? ":" + r.DungeonResult.Outcome : "")}{(r.ReturnedHome ? "帰還" : "")}+{r.DepositedGold}G")) +
                    $" 内職{result.SideJobIncome} HP[{string.Join(",", Active.Select(a => $"{a.CurrentHP}/{a.MaxHP}"))}]");
            foreach (var r in result.DungeonMissionResolutions)
            {
                foreach (var inj in r.InjuryEvents)
                    if (inj.Severity == InjurySeverity.Severe) SevereInjuries++; else LightInjuries++;
                if (r.DungeonResult != null)
                {
                    ForcedRetired += r.DungeonResult.ForceRetiredAdventurerIds.Count;
                    RecordAssault(result.Flags.Week, r);
                }
                if (Trace && r.ArrivedAtBossDoor)
                    Console.WriteLine($"  扉前{r.CurrentFloor}F: {r.DoorRetreatReason ?? "挑んだ"}");
                if (r.ArrivedAtBossDoor && r.Field.Order == 1)
                {
                    var m = lastMain;
                    DoorArrivals.Add((result.Flags.Week, r.CurrentFloor, m?.WeeksElapsed ?? 0));
                }
            }
            if (result.Flags.RecruitmentTrialOccurred)
            {
                int g = s.Gold;
                HoldTrial();
                Book("採用の契約金", s.Gold - g);
            }
            if (s.WeekNumber % 48 == 1)
                Snapshot();
            if (s.DefeatReason != null) { Defeat = s.DefeatReason.ToString() + $"（{s.WeekNumber}週）"; break; }
        }
    }

    /// <summary>
    /// 大会の出場（§0.82）：月のはじめに、今月の大会ごとに資格のある者のうち「部門の強さ×HPの補正」が高い2人を出す
    /// （勝ち目の薄い大会には出さない）。迷宮踏破杯は主力4名の部隊で出る。過ごし方は HP8割未満なら休養、ほかは追い込み。
    /// オプション nomain＝主力4名は個人の大会に出さない、notourney＝大会に出ない。
    /// 交流戦（§0.84）：季節に1回、勝てる見込みが ExchangeApplyChance 以上なら申し込み、部門ごとに「部門の強さ÷相手の強さ」が高い子を出す（休養）。
    /// 最初の交流戦は Core が月のはじめに自動で入れる。notourney なら申し込まない（最初の1回だけ行う）。
    /// </summary>
    void EnterTournaments()
    {
        TournamentSystem.EnsureSchedule(s);
        if (TourneyMode == "off" || !TournamentSystem.CanChangeEntries(s)) return;
        EnterExchange();
        var main = Active.Where(a => a.Injury != InjurySeverity.Severe).OrderByDescending(StatSum).Take(4).Select(a => a.Id).ToList();
        foreach (var ev in TournamentSystem.EventsThisMonth(s).Where(e => e.Result == null).OrderByDescending(e => TournamentSystem.GradeRank(e.Grade)))
        {
            if (ev.Discipline == TournamentDiscipline.Party)
            {
                var squad = s.SavedParties.FirstOrDefault(p => p.Name == "踏破杯の部隊");
                if (squad == null) { squad = new SavedParty { Name = "踏破杯の部隊" }; s.SavedParties.Add(squad); }
                squad.MemberIds = main.ToList();
                if (TournamentSystem.PartyEntryBlockReason(s, ev, squad) == null)
                {
                    var (pmin, pmax) = TournamentSystem.OpponentRange(ev, TournamentDiscipline.Party);
                    if (TournamentSystem.PartyStrength(s, TournamentSystem.PartyMembers(s, squad)) >= (pmin + pmax) / 2 * 0.85)
                        TournamentSystem.TryEnterParty(s, ev, squad, TournamentPrep.Rest);
                }
                continue;
            }
            // 主力4名は G1・最強決定戦・招待と、得意な部門のG2（G1の資格を取るため）にだけ出す（迷宮を止めすぎない）。nomain なら個人の大会には出さない
            bool bigStage = ev.Grade is TournamentGrade.G1 or TournamentGrade.Special;
            var picks = Active
                .Where(a => !(main.Contains(a.Id) && (TourneyMode == "nomain" || !(bigStage || (ev.Kind == TournamentKind.Royal && TournamentSystem.DisciplineFor(ev, a) == TournamentSystem.BestDiscipline(a))))) && HpRatio(a) >= 0.6 && TournamentSystem.EntryBlockReason(s, ev, a) == null)
                .Select(a => (A: a, Str: TournamentSystem.MatchStrength(a, TournamentSystem.DisciplineFor(ev, a)), Range: TournamentSystem.OpponentRange(ev, TournamentSystem.DisciplineFor(ev, a))))
                .Where(x => ev.Kind == TournamentKind.Rookie || x.Str >= (x.Range.Min + x.Range.Max) / 2 * 0.85)
                .OrderByDescending(x => x.Str).Take(TournamentBalance.EntrantsPerGuild).ToList();
            foreach (var p in picks)
                TournamentSystem.TryEnter(s, ev, p.A, HpRatio(p.A) < 0.8 ? TournamentPrep.Rest : TournamentPrep.Push);
        }
    }

    /// <summary>交流戦（§0.84）：季節に1回、勝てる見込みがこの値以上なら申し込む。</summary>
    const double ExchangeApplyChance = 0.35;

    void EnterExchange()
    {
        if (IsabellaSystem.ApplyBlockReason(s) == null)
        {
            var preview = PickExchange();
            if (preview.Count == 3 && IsabellaSystem.MatchWinChance(preview.Select(p => p.Chance).ToList()) >= ExchangeApplyChance)
                IsabellaSystem.TryApply(s);
        }
        if (IsabellaSystem.ExchangeThisMonth(s) is not { Result: null } ev || !IsabellaSystem.TournamentsOpen(s)) return; // 最初の1回は Core が入れる
        foreach (var p in PickExchange())
            if (IsabellaSystem.SlotEntry(s, ev, p.D) == null)
                IsabellaSystem.TryEnter(s, ev, p.A, p.D, TournamentPrep.Rest);
    }

    /// <summary>交流戦の出場者の組（部門ごとに「部門の強さ÷相手の強さ」が高い順に、出られる子を重ならないように選ぶ）。</summary>
    List<(TournamentDiscipline D, Adventurer A, double Chance)> PickExchange()
    {
        var picks = new List<(TournamentDiscipline, Adventurer, double)>();
        var used = new HashSet<Guid>();
        var open = IsabellaSystem.ExchangeDisciplines.ToList();
        while (open.Count > 0)
        {
            var best = open.SelectMany(d => Active.Where(a => !used.Contains(a.Id) && !a.IsDispatched && a.Injury == InjurySeverity.None && !a.IsPoisoned
                        && HpRatio(a) >= 0.6 && !TournamentSystem.IsEntered(s, a.Id))
                    .Select(a => (d, a, chance: IsabellaSystem.BoutWinChance(TournamentSystem.MatchStrength(a, d), IsabellaSystem.OpponentStrength(s, d)))))
                .OrderByDescending(x => x.chance).FirstOrDefault();
            if (best.a == null) break;
            picks.Add((best.d, best.a, best.chance));
            used.Add(best.a.Id);
            open.Remove(best.d);
        }
        return picks;
    }

    public int? VisitWeek, FirstExchangeWinWeek, GuestArrivalWeek, CommissionsOpenWeek;
    public int ExchangePlayed, ExchangeWon;

    public readonly Dictionary<(FacilityType, int), int> UnlockWeek = new();
    public readonly Dictionary<TournamentDiscipline, int> FirstG1Week = new();
    public int TournamentEntries, TournamentWins;
    public static string TourneyMode = "all";

    ActiveDungeonMission? lastMain;
    readonly HashSet<Guid> mainIds = new();

    void Act() => ActInner();

    /// <summary>決戦の決着を記録（§0.69：扉前に着いた週の決算のうちに決戦するので、その週の解決から拾う）。</summary>
    void RecordAssault(int week, DungeonMissionResolution r)
    {
        var boss = r.Boss;
        if (boss == null || r.DungeonResult!.Outcome != DungeonOutcome.Victory) return;
        Kills.Add(new BossKill(week, boss.Floor, r.Party.Members.Average(a => Acc.All.Average(n => a.GetEffectiveStat(n))),
            r.Party.Members.Average(a => StatSum(a) / WeightSum), r.DungeonResult.PartyPower / r.DungeonResult.RequiredPower, r.IntelRateBefore,
            divesPerBoss.GetValueOrDefault(boss.Floor)));
        FieldKills.Add((r.Field.Order, boss.Floor, week));
    }

    void ActInner()
    {
        int g0 = s.Gold;
        HandleCommissions();
        HandleLoot();
        BookAct("遺物・素材の売却（鑑定代差引）", s.Gold - g0);
        g0 = s.Gold;
        Shop();
        BookAct("装備の購入", s.Gold - g0);
        g0 = s.Gold;
        BuildFacility();
        BookAct("施設の建設", s.Gold - g0);

        // 扉前の判断は、着いた週の決算のうちに Core が構え「標準」（方針の無い出撃）で行う（§0.69）。

        if (Campaign)
            CampaignUpkeep();
        EnterTournaments(); // 大会の出場（§0.82。訓練の割り振りと同じく月のはじめ）

        // 訓練は月の約束（§0.70）：割り振りを変えられるのは月のはじめだけ。月の途中は訓練生を出撃に回さない。
        bool monthStart = TrainingSystem.CanChangeAssignments(s);
        bool Free(Adventurer a) => (monthStart || !TrainingSystem.IsTraining(s, a.Id)) && !TournamentSystem.IsEntered(s, a.Id);

        // 主力：次のボスへ潜行（森だけのモードでは森、campaign では要求火力の最も低いボス）
        var target = PickTarget()?.Boss;
        bool mainOut = s.ActiveDungeonMissions.Any(m => m.MissionType == DungeonMissionType.Scouting);
        var reserved = Active.Where(a => !a.IsDispatched && a.Injury != InjurySeverity.Severe)
            .OrderByDescending(StatSum).Take(4).Select(a => a.Id).ToHashSet();
        if (!mainOut && target != null && DungeonExpeditionSystem.CanDispatch(s))
        {
            var ready = PickForBoss(target, Active.Where(a => a.IsAvailable && Free(a) && HpRatio(a) >= 0.8).ToList());
            if (ready.Count == 4 || (ready.Count >= 3 && Active.Count(a => a.IsAvailable) < 4))
            {
                var leader = ready.OrderByDescending(a => a.GetEffectiveStat("LDR")).First();
                ready.Remove(leader); ready.Insert(0, leader);
                var party = new Party();
                foreach (var a in ready) { training.Unassign(s, a.Id); party.TryAdd(a); }
                bool hopeless = ready.Sum(StatSum) * (1 + DungeonBalance.FullIntelDamageBonus) * 0.85 < DungeonResolver.RequiredPower(target);
                // 扉前まで潜ったことがあり、今の解析では火力が届かないなら、潜り直す前に迷宮調査で解析を進める
                // （§0.69：扉前で待って偵察することは無くなった。完全解析の+20%で届くかを調べる）
                var targetField = s.DungeonFields.First(f => f.Bosses.Contains(target));
                bool shortNow = DungeonResolver.CalculateBossPower(party, target) < DungeonResolver.RequiredPower(target, s, party);
                if (!hopeless && shortNow && targetField.ReachedFloor >= target.Floor
                    && ScoutingResolver.GetTier(target.IntelRate) != IntelTier.Complete && SurveyableOf(target) is { } analyse)
                    expedition.TryDispatchSurvey(s, party, analyse);
                else if (Grind && hopeless && ScoutingResolver.GetTier(target.IntelRate) == IntelTier.Complete)
                    expedition.TryDispatchGathering(s, party, Forest); // 勝ち目が無い間は採取で稼ぎ、機嫌と成長を保つ
                else if (Grind && hopeless)
                {
                    // 調査は解析が完全でない最も浅いボスで、潜行の済んだ階層だけ（§0.67）。無理なら採取で稼ぐ
                    if (SurveyableOf(target) is { } surveyBoss) expedition.TryDispatchSurvey(s, party, surveyBoss);
                    else expedition.TryDispatchGathering(s, party, Forest);
                }
                else if (expedition.TryDispatch(s, party, target, DungeonMissionType.Scouting))
                {
                    lastMain = s.ActiveDungeonMissions.Last();
                    DivesTotal++;
                    divesPerBoss[target.Floor] = divesPerBoss.GetValueOrDefault(target.Floor) + 1;
                }
            }
        }

        // 2・3枠目：調査 → 採取（主力の4名は使わない）
        while (DungeonExpeditionSystem.CanDispatch(s))
        {
            var pool = Active.Where(a => a.IsAvailable && Free(a) && !reserved.Contains(a.Id) && HpRatio(a) >= 0.6).ToList();
            if (pool.Count < 1) break;
            bool surveyOut = s.ActiveDungeonMissions.Any(m => m.MissionType == DungeonMissionType.Survey);
            var party = new Party();
            // 受けた完全解析の依頼があれば、その対象を先に調べる（§0.64）
            var surveyTarget = SurveyableOf(CommissionSurveyTarget())
                ?? (target != null ? SurveyableOf(target) : null);
            if (!surveyOut && pool.Count >= 2 && surveyTarget != null)
            {
                foreach (var a in pool.OrderByDescending(a => ScoutingResolver.GetAnalysisValue(a) + ScoutingResolver.GetGuardValue(a) + ScoutingResolver.GetStealthValue(a)).Take(3))
                { training.Unassign(s, a.Id); party.TryAdd(a); }
                if (!expedition.TryDispatchSurvey(s, party, surveyTarget)) break;
            }
            else
            {
                foreach (var a in pool.OrderByDescending(HpRatio).Take(3)) { training.Unassign(s, a.Id); party.TryAdd(a); }
                if (!expedition.TryDispatchGathering(s, party, Forest)) break;
            }
        }

        // 訓練：主力以外の待機者を、伸びしろの大きい施設へ（月のはじめだけ。§0.70）
        if (!monthStart) return;
        foreach (var a in Active.Where(a => !a.IsDispatched))
        {
            if (reserved.Contains(a.Id) || HpRatio(a) < 0.5 || a.Injury == InjurySeverity.Severe || TournamentSystem.IsEntered(s, a.Id))
            {
                training.Unassign(s, a.Id);
                continue;
            }
            var best = TrainingFacilities
                .OrderByDescending(f => FacilityBalance.GetTrainingTargetStats(f).Sum(n => Acc.Pa(a, n) - Acc.Stat(a, n)))
                .Where(f => training.GetSlotCapacity(s, f) > 0);
            foreach (var f in best)
                if (training.TryAssign(s, a.Id, f)) break;
        }
    }

    /// <summary>
    /// 依頼（§0.64）の機械的な方針：
    ///  - 撃破：主力が狙っているボス（要求火力の最も低い次のボス）なら受ける
    ///  - 完全解析：受ける（2・3枠目の調査がその対象へ向かう）
    ///  - 納品：受ける（その素材は売らずに残し、揃ったら納める）
    ///  - 献上：主力の上位4名以外に条件を満たす者がいれば受け、その中で最も弱い者を譲る
    /// </summary>
    void HandleCommissions()
    {
        if (!UseCommissions) return;
        var top4 = Active.OrderByDescending(StatSum).Take(4).Select(a => a.Id).ToHashSet();
        var target = PickTarget()?.Boss;
        foreach (var c in s.Commissions.Where(c => !c.Accepted).ToList())
        {
            bool want = c.Type switch
            {
                CommissionType.Defeat => c.BossId == target?.Id,
                CommissionType.Survey => true,
                // 納品は在庫が揃っているときだけ受けてすぐ納める（素材を売らずに溜めると序盤の資金繰りが詰まる）
                CommissionType.Deliver => c.MaterialId != null && s.Materials.GetValueOrDefault(c.MaterialId) >= c.Count,
                // 派遣は現役が8名以上いるときだけ（少ない人数で出すと2・3枠目が出せず、退屈で機嫌が尽きる。§0.85）
                _ => Active.Count() >= 8 && CommissionSystem.GetLoanCandidates(s, c).Any(a => !top4.Contains(a.Id)),
            };
            if (AcceptTypes != null && !AcceptTypes.Split(',').Contains(c.Type.ToString())) want = false;
            if (want) CommissionSystem.TryAccept(s, c);
        }
        foreach (var c in s.Commissions.Where(c => c.Accepted).ToList())
        {
            CommissionCompletion? done = null;
            if (c.Type == CommissionType.Deliver)
                done = commissions.TryDeliver(s, c);
            else if (c.Type == CommissionType.Loan && Active.Count() >= 8
                     && CommissionSystem.GetLoanCandidates(s, c).Where(a => !top4.Contains(a.Id)).OrderBy(StatSum).FirstOrDefault() is { } who)
            {
                done = commissions.TryLoan(s, c, who);
                if (done != null) Loans++;
            }
            if (done == null) continue;
            CommDone++;
            CommGold += done.Gold;
            if (done.PatronUnique != null) PatronUniques++;
        }
    }

    /// <summary>そのボスのフィールドで今調べられるボス（解析が完全でない最も浅いボスで、潜行の済んだ階層。→ ScoutingResolver.FindSurveyableBoss、§0.67）。無ければ null。</summary>
    FloorBoss? SurveyableOf(FloorBoss? boss)
    {
        if (boss == null) return null;
        var field = s.DungeonFields.FirstOrDefault(f => f.Bosses.Contains(boss));
        return field == null ? null : ScoutingResolver.FindSurveyableBoss(field); // 低層に未解析の階が残っていれば、そちらを調べる
    }

    /// <summary>受けた完全解析の依頼の対象（まだ解析が済んでいない・倒していない）。無ければnull。</summary>
    FloorBoss? CommissionSurveyTarget() => !UseCommissions || !SteerSurvey ? null : s.Commissions
        .Where(c => c.Accepted && c.Type == CommissionType.Survey)
        .Select(c => CommissionSystem.FindBoss(s, c))
        .FirstOrDefault(b => b != null && !b.IsDefeated && ScoutingResolver.GetTier(b.IntelRate) != IntelTier.Complete);

    static readonly FacilityType[] TrainingFacilities ={ FacilityType.DrillHall, FacilityType.Academy, FacilityType.SkillHall };
    static readonly FacilityType[] BuildOrder =
    {
        FacilityType.DrillHall, FacilityType.SkillHall, FacilityType.Academy,
        FacilityType.DrillHall, FacilityType.SkillHall, FacilityType.Academy,
        FacilityType.Infirmary, FacilityType.Infirmary,
        FacilityType.DrillHall, FacilityType.SkillHall, FacilityType.Academy,
    };


    static readonly EquipmentSlot[] Slots = { EquipmentSlot.Weapon, EquipmentSlot.Armor, EquipmentSlot.Accessory1, EquipmentSlot.Accessory2 };
    static double Worth(EquipmentItem? e) => e == null ? 0 : DungeonBalance.BossPowerWeights.Sum(w => e.GetTotalStatBonus(w.Stat) * w.Weight) + e.GetTotalHpBonus() * 0.05;

    /// <summary>遺物を鑑定し、今より良い装備は上位の冒険者へ着せ、残りと素材は売る。</summary>
    void HandleLoot()
    {
        if (!UseLoot) return;
        foreach (var relic in s.UnidentifiedItems.ToList())
            // campaign では週給4週分＋500Gを残せるときだけ鑑定する（依頼の報酬の金・虹の遺物は鑑定代が高く、序盤に一度に鑑定すると破産する）
            if (!Campaign || s.Gold - relic.AppraisalCost >= Active.Sum(a => a.WeeklyWage) * 4 + 500)
                appraisal.Appraise(s, relic.Id);
        foreach (var item in s.Armory.ToList())
        {
            var slot = item.GetSlot();
            if (slot == null) continue;
            var slots = slot is EquipmentSlot.Accessory1 or EquipmentSlot.Accessory2 ? new[] { EquipmentSlot.Accessory1, EquipmentSlot.Accessory2 } : new[] { slot.Value };
            var bestGain = 0.0; Adventurer? who = null; EquipmentSlot at = slot.Value;
            foreach (var a in Active.Where(a => !a.IsDispatched && item.IsAllowedFor(a.JobClass)).OrderByDescending(StatSum).Take(8))
                foreach (var sl in slots)
                {
                    double gain = Worth(item) - Worth(a.GetEquipped(sl));
                    if (gain > bestGain + 0.01) { bestGain = gain; who = a; at = sl; }
                }
            if (who != null)
            {
                equipment.TryEquip(s, who, at, item); // 装飾品は1と2のどちらの枠にも着けられる
            }
        }
        var sellable = s.Armory.Where(EquipmentSystem.CanSell).Select(e => e.Id.ToString()).ToList();
        if (sellable.Count > 0) EquipmentSystem.TrySellEquipments(s, sellable, out _);
        // campaign では、まだ済んでいない研究に要る素材と、秘薬の触媒（紅玉鉱石）は売らずに残す。
        var keep = Campaign
            ? ResearchBalance.GetAll().Where(r => !s.IsResearchCompleted(r.Id)).SelectMany(r => r.RequiredMaterials.Keys)
                .Concat(UseElixirs ? ElixirBalance.Recipes.SelectMany(r => r.RequiredMaterials.Keys) : Enumerable.Empty<string>())
                .Append("mat_canyon_gem")
                .Concat(s.Commissions.Where(c => c.Accepted && c.MaterialId != null).Select(c => c.MaterialId!)).ToHashSet()
            : new HashSet<string>();
        foreach (var id in s.Materials.Keys.Where(id => !keep.Contains(id)).ToList())
            economy.TrySellAllOfMaterial(s, id, out _);
    }

    static readonly FacilityType[] CampaignBuildOrder =
    {
        FacilityType.DrillHall, FacilityType.SkillHall, FacilityType.Academy,
        FacilityType.Dormitory,
        FacilityType.DrillHall, FacilityType.SkillHall, FacilityType.Academy,
        FacilityType.Infirmary, FacilityType.Dormitory, FacilityType.Infirmary,
        FacilityType.DrillHall, FacilityType.SkillHall, FacilityType.Academy,
        FacilityType.DrillHall, FacilityType.SkillHall, FacilityType.Academy,
        FacilityType.DrillHall, FacilityType.SkillHall, FacilityType.Academy,
    };

    /// <summary>
    /// campaign の毎週の手入れ：引退者を空いた訓練施設の教官に就ける、余裕があれば研究を済ませる、
    /// 秘薬を処方する（相性100のペアのうち両親の総合PAの合計が最大の組。娘の職業は強い方の親、紅玉鉱石があれば触媒に）。
    /// </summary>
    void CampaignUpkeep()
    {
        foreach (var a in Active)
            a.IdleActivity = SelfTrainAll && s.MasterMood >= SelfTrainMinMood ? IdleActivity.SelfTraining : IdleActivity.Help;

        var posted = s.AssignedTrainers.Values.OfType<Guid>().ToHashSet();
        foreach (var f in TrainingFacilities.Where(f => s.GetFacilityLevel(f) >= 1))
        {
            if (s.AssignedTrainers.TryGetValue(f, out var cur) && cur != null) continue;
            var best = s.RetiredAdventurers.Where(r => !posted.Contains(r.Id))
                .OrderByDescending(r => FacilityBalance.GetTrainingTargetStats(f).Sum(n => Acc.Stat(r, n))).FirstOrDefault();
            if (best != null && advisors.TryAssignTrainer(s, f, best.Id))
                posted.Add(best.Id);
        }

        foreach (var r in ResearchBalance.GetAll().Where(r => !s.IsResearchCompleted(r.Id)))
            if (s.Gold - ResearchSystem.GetGoldToPay(s, r) >= Reserve)
            {
                int discount = ResearchSystem.GetDiscount(s, r);
                if (ResearchSystem.CompleteResearch(s, r)) ResearchCreditUsed += discount;
            }

        // 霊薬（§0.61）：若い上位の冒険者に、火力の重みが最も伸びる霊薬を1週1本
        if (UseElixirs && ElixirSystem.IsUnlocked(s))
        {
            var weight = DungeonBalance.BossPowerWeights.ToDictionary(w => w.Stat, w => w.Weight);
            var pick = Active.Where(a => !a.IsDispatched && a.Age <= 24 && a.ElixirsTaken < ElixirBalance.MaxPerAdventurer)
                .OrderByDescending(StatSum).Take(6)
                .SelectMany(a => ElixirBalance.Recipes.Select(r => (A: a, R: r, Gain: r.TargetStats.Sum(n => DungeonBalance.GetBossPowerWeights(a.JobClass).First(w => w.Stat == n).Weight) * (r.PaBonus + r.StatBonus))))
                .Where(x => s.Gold - x.R.RequiredGold >= Reserve && ElixirSystem.Check(s, x.A, x.R.Id) == ElixirCheck.Ok)
                .OrderByDescending(x => x.Gain).FirstOrDefault();
            if (pick.A != null)
            {
                ElixirSystem.TryGive(s, pick.A, pick.R.Id);
                ElixirsGiven++;
            }
        }

        if (!UseSoulFusion || !SoulFusionSystem.IsUnlocked(s) || !SoulFusionSystem.HasFreeTank(s)
            || s.Gold < SoulFusionBalance.PrescriptionGold + Reserve)
            return;
        var pair = SoulFusionSystem.GetEligiblePairs(s).OrderByDescending(p => p.A.TotalPA + p.B.TotalPA).FirstOrDefault();
        if (pair.A == null) return;
        var job = (StatSum(pair.A) >= StatSum(pair.B) ? pair.A : pair.B).JobClass;
        string? catalyst = s.Materials.GetValueOrDefault("mat_canyon_gem") >= 3 ? "mat_canyon_gem" : null;
        soulFusion ??= new SoulFusionSystem(new SeededRng(s.WeekNumber * 13 + 5));
        soulFusion.TryPrescribe(s, pair.A, pair.B, job, catalyst);
    }

    static double ItemWorth(Item i) => DungeonBalance.BossPowerWeights.Sum(w => i.GetStatBonus(w.Stat) * w.Weight) + i.MaxHpBonus * 0.05;
    public static bool UseShop = true;

    /// <summary>所持金に余裕があれば、主力候補（上位4名）の装備を店で良いものへ買い替える（1週1点）。</summary>
    void Shop()
    {
        if (Frugal) return;
        if (!UseShop) return;
        var best = (Gain: 0.0, Who: (Adventurer?)null, Item: (Item?)null);
        foreach (var a in Active.Where(a => !a.IsDispatched).OrderByDescending(StatSum).Take(Campaign ? 6 : 4))
            foreach (var item in ItemCatalog.GetAll().Where(i => i.IsAllowedFor(a.JobClass) && EquipmentSystem.IsInShop(s, i)))
            {
                if (s.Gold - item.Price < (Campaign ? Reserve : 800)) continue;
                double gain = ItemWorth(item) - Worth(a.GetEquipped(item.Slot));
                if (gain <= 0.5) continue;
                // campaign ではお金が余るので、1G あたりではなく伸びの大きさで選ぶ（上位装備を買い進める）
                double score = Campaign ? gain : gain / item.Price;
                if (best.Who == null || score > best.Gain) best = (score, a, item);
            }
        if (best.Who != null)
            equipment.TryPurchaseAndEquip(s, best.Who, best.Item!.Id);
    }

    // ==== 収支の内訳（→ RunMany の「お金の出入り」表） ====
    public static int LedgerUntil = 48;
    public static bool Frugal;
    /// <summary>区間（1〜12週／13〜24週／25〜48週）×項目ごとの合計。</summary>
    public readonly Dictionary<(int Phase, string Item), int> Ledger = new();
    public readonly int[] PhaseWeeks = new int[3];
    int actBooked;

    static int PhaseOf(int week) => week <= 12 ? 0 : week <= 24 ? 1 : 2;

    void Book(string item, int gold)
    {
        if (FreeMoney || s.WeekNumber > LedgerUntil || gold == 0) return;
        var key = (PhaseOf(s.WeekNumber), item);
        Ledger[key] = Ledger.GetValueOrDefault(key) + gold;
    }

    void BookAct(string item, int gold) { Book(item, gold); actBooked += gold; }

    void BookSettlement(WeeklySettlementResult r, int total, int wages, int trainees)
    {
        if (FreeMoney || r.Flags.Week > LedgerUntil) return;
        int week = r.Flags.Week;
        PhaseWeeks[PhaseOf(week)]++;
        void Add(string item, int gold) { if (gold != 0) { var k = (PhaseOf(week), item); Ledger[k] = Ledger.GetValueOrDefault(k) + gold; } }
        int side = r.SideJobIncome?.FinalGold ?? 0;
        int gather = r.DungeonMissionResolutions.Sum(x => x.GatheringResult?.GoldEarned ?? 0);
        int loot = r.DungeonMissionResolutions.Sum(x => x.DepositedGold);
        int boss = r.DungeonMissionResolutions.Where(x => x.DungeonResult?.Outcome == DungeonOutcome.Victory).Sum(x => x.Boss?.RewardGold ?? 0);
        int training = -trainees * 20;
        Add("週給", -wages);
        Add("訓練費", training);
        Add("内職", side);
        Add("採取の報酬", gather);
        Add("潜行の拾得ゴールド", loot);
        Add("ボスの撃破報酬", boss);
        Add("その他の決算", total - (-wages + training + side + gather + loot + boss));
    }
    void BuildFacility()
    {
        if (Frugal) return;
        if (s.UnderConstruction != null) return;
        var counts = new Dictionary<FacilityType, int>();
        foreach (var f in Campaign ? CampaignBuildOrder : BuildOrder)
        {
            counts[f] = counts.GetValueOrDefault(f) + 1;
            if (s.GetFacilityLevel(f) >= counts[f]) continue;
            if (FacilitySystem.IsBlockedByLevelCap(s, f)) continue; // 施設の上限Lv（§0.79）：倒したボスの数で開く
            if (s.Gold - FacilityBalance.GetUpgradeCost(f, s.GetFacilityLevel(f)) >= 2500)
                facility.TryStartConstruction(s, f, !FacilitySystem.NeedsSpecialtyChoice(f, s.GetFacilityLevel(f)) ? FacilitySpecialty.None
                    : UseRivalry && FacilityBalance.IsTrainingFacility(f) ? FacilitySpecialty.Rivalry : FacilityBalance.GetDefaultSpecialty(f)); // Lv3→4は専門を選ぶ（§0.76）
            return;
        }
    }

    void HoldTrial()
    {
        var offers = recruitment.GenerateCandidates(s);
        foreach (var o in offers.OrderByDescending(o => TotalPa(o.Candidate)))
        {
            if (Active.Count() >= 12 || s.Gold - o.SigningBonus < 1500) break;
            recruitment.TryHire(s, o);
        }
    }

    void Snapshot()
    {
        RosterByYear.Add(Active.Count());
        var top = Active.OrderByDescending(StatSum).Take(4).ToList();
        double w = top.Count == 0 ? 0 : top.Average(a => StatSum(a) / WeightSum);
        Yearly.Add($"{GameCalendar.Format(s.WeekNumber)}: 上位4名の加重平均 {w:F1}（{string.Join("/", top.Select(a => $"{a.JobClass}{a.Age}歳{StatSum(a) / WeightSum:F0}"))}）" +
                   (Campaign
                       ? $" 撃破 {string.Join("/", s.DungeonFields.OrderBy(f => f.Order).Select(f => $"{FieldShort(f.Order)}{f.Bosses.Count(b => b.IsDefeated) * 10}F"))}・所持金 {s.Gold}G・現役 {Active.Count()}名・娘 {DaughtersBorn}名"
                       : $" 森の最高到達 {Forest.ReachedFloor}F・撃破 {Forest.Bosses.Count(b => b.IsDefeated)}体・所持金 {s.Gold}G・機嫌 {s.MasterMood}・現役 {Active.Count()}名"));
        if (Campaign) YearlyKills.Add(s.DungeonFields.Sum(f => f.Bosses.Count(b => b.IsDefeated)));
    }

    public readonly List<int> YearlyKills = new();
    static string FieldShort(int order) => order switch { 1 => "森", 2 => "洞", 3 => "廃", 4 => "峡", _ => "深" };

    public static void RunCampaign(int runs, int weeks)
    {
        Console.WriteLine($"## E. 深淵100F（クリア）までの通しのシミュレーション（{runs}回、最大{weeks}週＝{weeks / 48}年。教官・研究・宿舎{(UseSoulFusion ? "・秘薬" : "")}を使う）");
        var sims = new List<GameSim>();
        for (int i = 0; i < runs; i++)
        {
            var sim = new GameSim(i + SeedFrom);
            sim.Run(weeks);
            sims.Add(sim);
        }

        Console.WriteLine();
        Console.WriteLine("### フィールドごとの節目ボスを初めて倒した年（中央値〔最小〜最大〕・倒せた回数）");
        Console.WriteLine("| フィールド | 10F | 20F | 50F | 100F |");
        Console.WriteLine("|---|---|---|---|---|");
        foreach (int order in new[] { 1, 2, 3, 4, 5 })
        {
            var cells = new[] { 10, 20, 50, 100 }.Select(floor =>
            {
                var ws = sims.Select(x => x.FieldKills.Where(k => k.Order == order && k.Floor == floor).Select(k => (int?)k.Week).FirstOrDefault())
                    .OfType<int>().Select(w => (double)w / 48 + 1).ToList();
                return ws.Count == 0 ? $"―（0/{runs}）" : $"{Median(ws):F1}年目〔{ws.Min():F1}〜{ws.Max():F1}〕（{ws.Count}/{runs}）";
            });
            Console.WriteLine($"| {FieldShort(order)} | {string.Join(" | ", cells)} |");
        }
        Console.WriteLine();
        var cleared = sims.Where(x => x.s.IsGameCleared).Select(x => (double)GameCalendar.YearOf(x.s.ClearedAtWeek!.Value)).ToList();
        Console.WriteLine(cleared.Count == 0
            ? $"クリア：0/{runs}回（{weeks / 48}年以内に深淵100Fに届かない）"
            : $"クリア：{cleared.Count}/{runs}回、{Median(cleared):F0}年目〔{cleared.Min():F0}〜{cleared.Max():F0}〕");
        Console.WriteLine();
        Console.WriteLine("### イザベラの来訪と交流戦（§0.84）");
        string WeekStat(Func<GameSim, int?> pick)
        {
            var ws = sims.Select(pick).OfType<int>().Select(w => (double)w / 48 + 1).ToList();
            return ws.Count == 0 ? $"―（0/{runs}）" : $"{Median(ws):F1}年目〔{ws.Min():F1}〜{ws.Max():F1}〕（{ws.Count}/{runs}）";
        }
        Console.WriteLine($"来訪（森の{IsabellaBalance.VisitFloor}F）：{WeekStat(x => x.VisitWeek)}　交流戦の初勝利：{WeekStat(x => x.FirstExchangeWinWeek)}　派遣の教官が来た：{WeekStat(x => x.GuestArrivalWeek)}");
        Console.WriteLine($"依頼が届き始めた（初めての入賞の次の季節）：{WeekStat(x => x.CommissionsOpenWeek)}");
        Console.WriteLine($"交流戦（行った／勝った）：{string.Join(", ", sims.Select(x => $"{x.ExchangePlayed}/{x.ExchangeWon}"))}");
        Console.WriteLine();
        Console.WriteLine($"### 大会（§0.82、出場の方針：{TourneyMode}）");
        Console.WriteLine($"出場：{string.Join(", ", sims.Select(x => x.TournamentEntries))}　優勝：{string.Join(", ", sims.Select(x => x.TournamentWins))}");
        foreach (var d in new[] { TournamentDiscipline.Sword, TournamentDiscipline.Magic, TournamentDiscipline.Skill, TournamentDiscipline.Party })
        {
            var ys = sims.Where(x => x.FirstG1Week.ContainsKey(d)).Select(x => (double)GameCalendar.YearOf(x.FirstG1Week[d])).ToList();
            Console.WriteLine($"最初のG1優勝（{TournamentSystem.DisciplineLabel(d)}）：" + (ys.Count == 0 ? $"―（0/{runs}）" : $"{Median(ys):F0}年目〔{ys.Min():F0}〜{ys.Max():F0}〕（{ys.Count}/{runs}）"));
        }
        Console.WriteLine("| 施設 | 新設 | Lv2 | Lv3 | Lv4 | Lv5 |");
        Console.WriteLine("|---|---|---|---|---|---|");
        foreach (var t in Enum.GetValues<FacilityType>())
        {
            var cells = Enumerable.Range(1, 5).Select(lv =>
            {
                if (lv <= FacilityUnlockSystem.InitialLevel(t)) return "初め";
                var ys = sims.Where(x => x.UnlockWeek.ContainsKey((t, lv))).Select(x => (double)GameCalendar.YearOf(x.UnlockWeek[(t, lv)])).ToList();
                return ys.Count == 0 ? $"―（0/{runs}）" : $"{Median(ys):F0}年目（{ys.Count}/{runs}）";
            });
            Console.WriteLine($"| {FacilityUnlockSystem.FacilityName(t)} | {string.Join(" | ", cells)} |");
        }
        Console.WriteLine($"撃破したボスの数（50体中、最終）：{string.Join(", ", sims.Select(x => x.s.DungeonFields.Sum(f => f.Bosses.Count(b => b.IsDefeated))))}");
        Console.WriteLine("撃破数の推移（各年のはじめ、中央値）：" + string.Join("・", Enumerable.Range(0, weeks / 48).Select(y =>
        {
            var l = sims.Where(x => x.YearlyKills.Count > y).Select(x => (double)x.YearlyKills[y]).ToList();
            return l.Count == 0 ? null : $"{y + 2}年目 {Median(l):F0}";
        }).OfType<string>()));
        Console.WriteLine($"秘薬で生まれた娘：{string.Join(", ", sims.Select(x => x.DaughtersBorn))}");
        Console.WriteLine($"飲ませた霊薬：{string.Join(", ", sims.Select(x => x.ElixirsGiven))}　上位装備の段（最終）：{string.Join(", ", sims.Select(x => EquipmentSystem.GetUnlockedShopTier(x.s)))}");
        Console.WriteLine($"研究の手伝いで割り引いた額（G）：{string.Join(", ", sims.Select(x => x.ResearchCreditUsed))}");
        Console.WriteLine($"強制除籍：{string.Join(", ", sims.Select(x => x.ForcedRetired))}　敗北：{string.Join(", ", sims.Select(x => x.Defeat ?? "なし"))}");
        Console.WriteLine($"依頼（§0.64、{(UseCommissions ? "受ける" : "受けない")}・異変{(NoAnomaly ? "なし" : "あり")}）：達成 {string.Join(", ", sims.Select(x => x.CommDone))}　失敗 {string.Join(", ", sims.Select(x => x.CommFailed))}　" +
            $"報酬G（千） {string.Join(", ", sims.Select(x => x.CommGold / 1000))}　派遣 {string.Join(", ", sims.Select(x => x.Loans))}　依頼人の固有武具 {string.Join(", ", sims.Select(x => x.PatronUniques))}　異変 {string.Join(", ", sims.Select(x => x.Anomalies))}");
        Console.WriteLine();
        Console.WriteLine("### 1回目の最後の状態：各フィールドの次のボスと、上位4名の部隊の値");
        sims[0].Diagnose();
        Console.WriteLine();
        Console.WriteLine("### 1回目の年ごとの様子");
        foreach (var y in sims[0].Yearly) Console.WriteLine("- " + y);
    }

    /// <summary>止まっている理由を見る：各フィールドの次のボスの要求火力・ギミックと、上位4名の火力（完全解析）・走破力。</summary>
    void Diagnose()
    {
        var top = Active.OrderByDescending(StatSum).Take(4).ToList();
        var party = new Party();
        foreach (var a in top) party.TryAdd(a);
        double trav = DungeonTraversalResolver.CalculateTraversalScore(party);
        foreach (var f in s.DungeonFields.OrderBy(f => f.Order))
        {
            var boss = f.GetNextActiveBoss();
            if (boss == null) { Console.WriteLine($"- {FieldShort(f.Order)}：制覇"); continue; }
            double power = DungeonResolver.CalculateBossPower(party, boss);
            double req = DungeonResolver.RequiredPower(boss, s, party);
            string gimmicks = string.Join("・", boss.Gimmicks.Select(g => $"{g.Type}(備え{DungeonResolver.Readiness(g, party.Members):F1}・{string.Join("/", g.CounterRoles)})"));
            Console.WriteLine($"- {FieldShort(f.Order)} {boss.Floor}F：要求火力 {req:F0}／上位4名の火力 {power:F0}（解析{boss.IntelRate:P0}）・ギミック {gimmicks}・" +
                $"走破力 {trav:F0}／{boss.Floor}Fの走破要求 {DungeonTraversalResolver.FloorRequirement(f, boss.Floor):F1}・最高到達 {f.ReachedFloor}F");
        }
        Console.WriteLine($"  上位4名：{string.Join("／", top.Select(a => $"{a.JobClass}{a.Age}歳 加重{StatSum(a) / WeightSum:F0} 霊薬{a.ElixirsTaken} 装備[{a.EquippedWeapon?.ItemId}/{a.EquippedArmor?.ItemId}/{a.EquippedAccessory1?.ItemId}/{a.EquippedAccessory2?.ItemId}]"))}");
    }

    public static void RunMany(int runs, int weeks)
    {
        Console.WriteLine($"## C. 通しのシミュレーション（{runs}回、最大{weeks}週）");
        var sims = new List<GameSim>();
        for (int i = 0; i < runs; i++)
        {
            var sim = new GameSim(i);
            sim.Run(weeks);
            sims.Add(sim);
        }

        Console.WriteLine();
        Console.WriteLine("### 森のボスを撃破した週（中央値〔最小〜最大〕、撃破できた回数）");
        Console.WriteLine("| ボス | 撃破週 | 撃破時の部隊の7能力平均 | 同・火力の加重平均 | 火力/要求 | 解析率 | その階層ボスまでの出撃回数 |");
        Console.WriteLine("|---|---|---|---|---|---|---|");
        for (int f = 10; f <= 100; f += 10)
        {
            var ks = sims.Select(x => x.Kills.FirstOrDefault(k => k.Floor == f)).OfType<BossKill>().ToList();
            if (ks.Count == 0) { Console.WriteLine($"| {f}F | 未撃破（0/{runs}） | | | | | |"); continue; }
            string Med(Func<BossKill, double> sel, string fmt) => $"{Median(ks.Select(sel)).ToString(fmt)}";
            Console.WriteLine($"| {f}F | {Med(k => k!.Week, "F0")}〔{ks.Min(k => k!.Week)}〜{ks.Max(k => k!.Week)}〕（{ks.Count}/{runs}） | {Med(k => k!.SquadAvgStat, "F1")} | {Med(k => k!.SquadWeightedStat, "F1")} | {Med(k => k!.Margin, "F2")} | {Med(k => k!.Intel * 100, "F0")}% | {Med(k => k!.DivesForBoss, "F0")} |");
        }
        Console.WriteLine();
        Console.WriteLine($"強制除籍（計）：{string.Join(", ", sims.Select(x => x.ForcedRetired))}");
        Console.WriteLine($"負傷（計）：重傷 {string.Join(", ", sims.Select(x => x.SevereInjuries))}／軽傷 {string.Join(", ", sims.Select(x => x.LightInjuries))}");
        Console.WriteLine($"敗北：{string.Join(", ", sims.Select(x => x.Defeat ?? "なし"))}");
        Console.WriteLine("現役人数（各年のはじめ、中央値）：" + string.Join("・", Enumerable.Range(0, 8).Select(y => { var l = sims.Where(x => x.RosterByYear.Count > y).Select(x => (double)x.RosterByYear[y]).ToList(); return l.Count == 0 ? "-" : $"{y + 2}年目 {Median(l):F0}名（{l.Count}回）"; })));
        if (!FreeMoney)
        {
            Console.WriteLine();
            Console.WriteLine($"### お金の出入り（1週あたりの平均G、全{runs}回。破産した回はその週まで）");
            string[] phases = { "1〜12週", "13〜24週", "25〜48週" };
            Console.WriteLine("| 項目 | " + string.Join(" | ", phases) + " |");
            Console.WriteLine("|---|---|---|---|");
            var items = sims.SelectMany(x => x.Ledger.Keys.Select(k => k.Item)).Distinct()
                .OrderByDescending(i => sims.Sum(x => x.Ledger.Where(kv => kv.Key.Item == i).Sum(kv => kv.Value)));
            var phaseWeeks = Enumerable.Range(0, 3).Select(p => Math.Max(1, sims.Sum(x => x.PhaseWeeks[p]))).ToArray();
            foreach (var item in items)
                Console.WriteLine($"| {item} | " + string.Join(" | ", Enumerable.Range(0, 3).Select(p => (sims.Sum(x => x.Ledger.GetValueOrDefault((p, item))) / (double)phaseWeeks[p]).ToString("+0;-0;0"))) + " |");
            Console.WriteLine("| **収支** | " + string.Join(" | ", Enumerable.Range(0, 3).Select(p => (sims.Sum(x => x.Ledger.Where(kv => kv.Key.Phase == p).Sum(kv => kv.Value)) / (double)phaseWeeks[p]).ToString("+0;-0;0"))) + " |");
        }
        Console.WriteLine();
        Console.WriteLine("### 1回目の年ごとの様子");
        foreach (var y in (sims.FirstOrDefault(x => x.Defeat == null) ?? sims[0]).Yearly) Console.WriteLine("- " + y);
        Console.WriteLine();
        Console.WriteLine("### 1回目の扉前到達（週：階層・出発からの週数）");
        Console.WriteLine(string.Join("、", sims[0].DoorArrivals.Select(d => $"{d.Week}週:{d.Floor}F({d.Weeks}週)")));
    }

    static double Median(IEnumerable<double> xs)
    {
        var l = xs.OrderBy(x => x).ToList();
        return l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2;
    }
}

static class Acc
{
    public static readonly string[] All = { "STR", "AGI", "VIT", "MND", "DEX", "LDR", "INT" };
    public static int Stat(Adventurer a, string n) => n switch
    {
        "STR" => a.STR, "AGI" => a.AGI, "VIT" => a.VIT, "MND" => a.MND, "DEX" => a.DEX, "LDR" => a.LDR, _ => a.INT,
    };
    public static int Pa(Adventurer a, string n) => n switch
    {
        "STR" => a.PA_STR, "AGI" => a.PA_AGI, "VIT" => a.PA_VIT, "MND" => a.PA_MND, "DEX" => a.PA_DEX, "LDR" => a.PA_LDR, _ => a.PA_INT,
    };
}

// ============================================================================================
// 3. 理想の育成：PA が全能力 pa の新人1名を18歳から、Lv5 施設＋教官（能力60）の訓練、または潜行で育てる
// ============================================================================================
static class IdealGrowth
{
    public static void Run()
    {
        Console.WriteLine("## D. 理想の育成（1名、18歳加入・初期値＝PA×0.3、200人の平均）");
        Console.WriteLine();
        Console.WriteLine("| PA | 育て方 | 20歳 | 22歳 | 24歳 | 26歳（引退直前） |");
        Console.WriteLine("|---|---|---|---|---|---|");
        foreach (int pa in new[] { 60, 80, 100 })
            foreach (var plan in new[] { "訓練のみ（教官なし）", "訓練のみ（教官あり）", "潜行のみ", "22歳まで訓練（教官あり）→潜行" })
            {
                var sums = new double[4];
                for (int i = 0; i < 200; i++)
                {
                    var r = One(pa, plan, i);
                    for (int k = 0; k < 4; k++) sums[k] += r[k];
                }
                Console.WriteLine($"| {pa} | {plan} | " + string.Join(" | ", sums.Select(x => (x / 200).ToString("F1"))) + " |");
            }
        Console.WriteLine();
        Console.WriteLine("値は火力の加重平均（STR0.8・AGI0.5・VIT0.6・MND0.8・DEX0.4・LDR0.4・INT0.7 で重み付けした能力の平均）。装備なし。");
    }

    static double W(Adventurer a) => DungeonBalance.GetBossPowerWeights(a.JobClass).Sum(w => a.GetEffectiveStat(w.Stat) * w.Weight) / DungeonBalance.BossPowerWeights.Sum(w => w.Weight);

    static double[] One(int pa, string plan, int seed)
    {
        int init = (int)(pa * 0.3);
        var a = new Adventurer
        {
            Name = "新人", JobClass = JobClass.Warrior, Age = 18,
            STR = init, AGI = init, VIT = init, MND = init, DEX = init, LDR = init, INT = init,
            PA_STR = pa, PA_AGI = pa, PA_VIT = pa, PA_MND = pa, PA_DEX = pa, PA_LDR = pa, PA_INT = pa,
        };
        a.CurrentHP = a.MaxHP;
        var s = new GameState { Adventurers = new List<Adventurer> { a }, DungeonFields = SampleData.CreateDefaultFields() };
        foreach (var f in new[] { FacilityType.DrillHall, FacilityType.Academy, FacilityType.SkillHall })
        {
            var fac = s.Facilities.FirstOrDefault(x => x.Type == f);
            if (fac == null) s.Facilities.Add(new Facility { Type = f, CurrentLevel = 5 }); else fac.CurrentLevel = 5;
            if (plan.Contains("教官あり"))
            {
                var t = new Adventurer { Name = "教官", Age = 26, IsRetired = true, STR = 60, AGI = 60, VIT = 60, MND = 60, DEX = 60, LDR = 60, INT = 60 };
                s.RetiredAdventurers.Add(t);
                s.AssignedTrainers[f] = t.Id;
            }
        }
        var rng = new SeededRng(seed * 31 + pa);
        var growth = new GrowthSystem(rng);
        var training = new TrainingSystem(new SeededRng(seed));
        var aging = new AgingSystem(new SeededRng(seed + 5));
        var result = new double[4];
        var weightOf = DungeonBalance.BossPowerWeights.ToDictionary(w => w.Stat, w => w.Weight);
        for (int week = 1; week <= 48 * 8; week++)
        {
            s.WeekNumber = week;
            bool dive = plan == "潜行のみ" || (plan.StartsWith("22歳まで") && a.Age > 22);
            if (dive)
            {
                var p = new Party(); p.TryAdd(a);
                growth.ApplyExpeditionGrowth(s, p, DungeonMissionType.Scouting, isBossVictory: false);
            }
            else
            {
                var best = new[] { FacilityType.DrillHall, FacilityType.Academy, FacilityType.SkillHall }
                    .OrderByDescending(f => FacilityBalance.GetTrainingTargetStats(f).Average(n => (Acc.Pa(a, n) - Acc.Stat(a, n)) * weightOf[n])).First();
                training.TryAssign(s, a.Id, best);
                growth.ProcessTrainingGrowth(s, new HashSet<Guid>());
            }
            if (week % 48 == 0)
            {
                a.Age++;
                int idx = a.Age switch { 20 => 0, 22 => 1, 24 => 2, 26 => 3, _ => -1 };
                if (idx >= 0) result[idx] = W(a);
            }
        }
        return result;
    }
}

static class DoorTable
{
    public static void Run()
    {
        Console.WriteLine("| 目標ボス | 部隊の全能力 | 扉前までの週数（最後の区間 未解析） | 同（最後の区間も完全解析） | 扉前の比率 |");
        Console.WriteLine("|---|---|---|---|---|");
        foreach (var (boss, s) in new[] { (10, 25), (20, 30), (30, 40), (40, 50), (60, 60), (80, 80), (100, 90) })
        {
            string W(double lastIntel)
            {
                var f = SampleData.CreateDefaultFields()[0];
                foreach (var b in f.Bosses)
                {
                    if (b.Floor < boss) { b.IsDefeated = true; b.IntelRate = 1.0; }
                    else if (b.Floor == boss) b.IntelRate = lastIntel;
                }
                double score = DungeonTraversalResolver.CalculateTraversalScore(StaticTables.Squad(s));
                return DungeonTraversalResolver.PredictWeeksToFloor(f, 1, boss, score).ToString();
            }
            double sc = DungeonTraversalResolver.CalculateTraversalScore(StaticTables.Squad(s));
            Console.WriteLine($"| {boss}F | {s} | {W(0)} | {W(1.0)} | {sc / (7.5 * (boss - 1)):F2} |");
        }
    }
}

// ============================================================================================
// 4. 要求値の目安：§0.47 の目安部隊（4人の討伐隊・3人の調査隊、全能力S・装備なし）の値と、今の要求値を並べる
// ============================================================================================
static class AnchorTable
{
    static Adventurer Make(JobClass job, int s)
    {
        var a = new Adventurer { JobClass = job, STR = s, AGI = s, VIT = s, MND = s, DEX = s, LDR = s, INT = s };
        a.CurrentHP = a.MaxHP;
        return a;
    }

    static Party Of(params Adventurer[] m) { var p = new Party(); foreach (var a in m) p.TryAdd(a); return p; }
    static Party Assault(int s) => Of(Make(JobClass.Knight, s), Make(JobClass.Ranger, s), Make(JobClass.Mage, s), Make(JobClass.Cleric, s));
    static Party Survey(int s) => Of(Make(JobClass.Ranger, s), Make(JobClass.Mage, s), Make(JobClass.Scholar, s));

    public static void Run()
    {
        Console.WriteLine("## 要求値の目安（森＝フィールド倍率1.0）");
        Console.WriteLine("| 全能力 | 討伐火力（完全解析） | 護衛力 | 隠密 | 解析 | 走破力 |");
        Console.WriteLine("|---|---|---|---|---|---|");
        foreach (int s in new[] { 25, 40, 55, 70, 90 })
        {
            var boss = new FloorBoss { Floor = 10, FieldOrder = 1, MaxHp = 1, IntelRate = 1.0 };
            Console.WriteLine($"| {s} | {DungeonResolver.CalculateBossPower(Assault(s), boss):F0} | {ScoutingResolver.CalculateGuardPower(Survey(s)):F1} | " +
                $"{ScoutingResolver.CalculateStealthScore(Survey(s)):F1} | {ScoutingResolver.CalculateAnalysisScore(Survey(s)):F1} | {DungeonTraversalResolver.CalculateTraversalScore(Assault(s)):F0} |");
        }
        Console.WriteLine();
        Console.WriteLine("| 階層 | 討伐 | 護衛 | 隠密 | 解析 | 走破 |");
        Console.WriteLine("|---|---|---|---|---|---|");
        var field = new DungeonField { Id = "forest", Order = 1 };
        foreach (int f in new[] { 10, 20, 30, 40, 50, 100 })
        {
            var b = new FloorBoss { Floor = f, FieldOrder = 1, MaxHp = 1 };
            Console.WriteLine($"| {f}F | {DungeonResolver.RequiredPower(b):F0} | {ScoutingResolver.RequiredGuardPower(b):F1} | {ScoutingResolver.StealthRequirement(b):F1} | " +
                $"{ScoutingResolver.AnalysisRequirement(b):F1} | {DungeonTraversalResolver.FloorRequirement(field, f):F1} |");
        }
    }
}
