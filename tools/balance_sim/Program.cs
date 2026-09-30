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
    public readonly List<BossKill> Kills = new();
    public readonly List<string> Yearly = new();
    public readonly List<int> RosterByYear = new();
    public int ForcedRetired;
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
        week = new WeekProcessingSystem(new MasterMoodSystem(), new EconomySystem(), training, new InjuryRecoverySystem(),
            new RestRecoverySystem(), growth, satisfaction, new AgingSystem(new SeededRng(99 + k)), facility,
            new DefeatSystem(), recruitment, expedition);

        s = new GameState { Adventurers = SampleData.CreateStarterAdventurers(), DungeonFields = SampleData.CreateDefaultFields() };
        var draft = recruitment.StartInitialDraft(s);
        for (int guard = 0; guard < 10 && !draft.IsComplete; guard++)
        {
            var best = draft.Offers.OrderByDescending(o => TotalPa(o.Candidate)).First();
            recruitment.TryDraftHire(s, draft, best);
        }
    }

    static int TotalPa(Adventurer a) => Acc.All.Sum(n => Acc.Pa(a, n));
    static double StatSum(Adventurer a) => DungeonBalance.BossPowerWeights.Sum(w => a.GetEffectiveStat(w.Stat) * w.Weight);
    static double WeightSum => DungeonBalance.BossPowerWeights.Sum(w => w.Weight);
    static double HpRatio(Adventurer a) => (double)a.CurrentHP / a.MaxHP;
    DungeonField Forest => s.DungeonFields.First(f => f.Order == 1);
    IEnumerable<Adventurer> Active => s.Adventurers.Where(a => !a.IsRetired);

    public void Run(int weeks)
    {
        for (int w = 0; w < weeks; w++)
        {
            if (Forest.Bosses.All(b => b.IsDefeated)) break;
            int actBefore = s.Gold;
            Act();
            Book("その他の行動", s.Gold - actBefore - actBooked);
            actBooked = 0;
            if (FreeMoney) s.Gold = Math.Max(s.Gold, 100000);
            int goldBefore = s.Gold;
            int wages = s.Adventurers.Sum(a => a.WeeklyWage);
            int trainees = s.TrainingAssignments.Count;
            var result = week.ProcessWeek(s);
            BookSettlement(result, s.Gold - goldBefore, wages, trainees);
            if (Trace)
                Console.WriteLine($"w{result.Flags.Week}: G {goldBefore}→{s.Gold} 機嫌{s.MasterMood} 現役{Active.Count()} 週給計{Active.Sum(a => a.WeeklyWage)} 訓練{s.TrainingAssignments.Count} 工事{s.UnderConstruction?.Type} " +
                    string.Join(" / ", result.DungeonMissionResolutions.Select(r => $"{r.MissionType}@{r.CurrentFloor}{(r.DungeonResult != null ? ":" + r.DungeonResult.Outcome : "")}{(r.ReturnedHome ? "帰還" : "")}+{r.DepositedGold}G")) +
                    $" 内職{result.SideJobIncome} HP[{string.Join(",", Active.Select(a => $"{a.CurrentHP}/{a.MaxHP}"))}]");
            foreach (var r in result.DungeonMissionResolutions)
            {
                if (r.DungeonResult != null)
                    ForcedRetired += r.DungeonResult.ForceRetiredAdventurerIds.Count;
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

    ActiveDungeonMission? lastMain;
    readonly HashSet<Guid> mainIds = new();
    readonly Dictionary<ActiveDungeonMission, (double Margin, double Avg, double W, double Intel)> pendingAssault = new();

    void Act()
    {
        // 前週に討伐指令を出した部隊の決着を記録（ProcessWeek の後で IsDefeated を見る）
        foreach (var kv in pendingAssault.ToList())
        {
            if (!s.ActiveDungeonMissions.Contains(kv.Key))
            {
                var boss = kv.Key.TargetedBoss!;
                if (boss.IsDefeated)
                    Kills.Add(new BossKill(s.WeekNumber - 1, boss.Floor, kv.Value.Avg, kv.Value.W, kv.Value.Margin, kv.Value.Intel,
                        divesPerBoss.GetValueOrDefault(boss.Floor)));
                pendingAssault.Remove(kv.Key);
            }
        }

        int g0 = s.Gold;
        HandleLoot();
        BookAct("遺物・素材の売却（鑑定代差引）", s.Gold - g0);
        g0 = s.Gold;
        Shop();
        BookAct("装備の購入", s.Gold - g0);
        g0 = s.Gold;
        BuildFacility();
        BookAct("施設の建設", s.Gold - g0);

        // 扉前の判断
        foreach (var m in s.ActiveDungeonMissions.Where(m => m.Status == ExpeditionStatus.AwaitingBossDecision).ToList())
        {
            var boss = m.TargetedBoss!;
            m.Party.ConsumableItemIds.Clear();
            var items = boss.Gimmicks.Where(g => !DungeonResolver.IsCountered(g, m.Party) && g.RequiredItemId != null)
                .Select(g => g.RequiredItemId!).Take(2).ToList();
            double power = DungeonResolver.CalculateBossPower(m.Party, boss);
            double req = DungeonResolver.RequiredPower(boss);
            double minHp = m.Party.Members.Min(HpRatio);
            bool safe = minHp > DungeonBalance.BaseHpLossPctMax / 100.0 + 0.02;
            if (Trace) Console.WriteLine($"  扉前{boss.Floor}F: 火力{power:F0}/要求{req:F0} 満タン時{m.Party.Members.Sum(StatSum):F0} 解析{boss.IntelRate:P0} 最低HP{minHp:P0} 装備[{string.Join(",", m.Party.Members.Select(a => $"{a.EquippedWeapon?.ItemId}/{a.EquippedArmor?.ItemId}/{a.EquippedAccessory1?.ItemId}"))}] G{s.Gold}");
            if (power >= req && safe && s.Gold >= DungeonExpeditionSystem.CalculateConsumableCost(items))
            {
                int gp = s.Gold;
                expedition.TryEngageBoss(s, m, items);
                BookAct("携行品", s.Gold - gp);
                pendingAssault[m] = (power / req, m.Party.Members.Average(a => Acc.All.Average(n => a.GetEffectiveStat(n))),
                    m.Party.Members.Average(a => StatSum(a) / WeightSum), boss.IntelRate);
            }
            else if (ScoutingResolver.GetTier(boss.IntelRate) != IntelTier.Complete && minHp > 0.4
                     && m.Party.Members.Sum(StatSum) * (1 + DungeonBalance.FullIntelDamageBonus) >= req)
            {
                // 扉前で偵察を続ける（何もしない）
            }
            else
                expedition.TryRetreat(s, m);
        }

        // 主力：森の次のボスへ潜行
        var target = Forest.GetNextActiveBoss();
        bool mainOut = s.ActiveDungeonMissions.Any(m => m.MissionType == DungeonMissionType.Scouting);
        var reserved = Active.Where(a => !a.IsDispatched && a.Injury != InjurySeverity.Severe)
            .OrderByDescending(StatSum).Take(4).Select(a => a.Id).ToHashSet();
        if (!mainOut && target != null && DungeonExpeditionSystem.CanDispatch(s))
        {
            var ready = Active.Where(a => a.IsAvailable && HpRatio(a) >= 0.8).OrderByDescending(StatSum).Take(4).ToList();
            if (ready.Count == 4 || (ready.Count >= 3 && Active.Count(a => a.IsAvailable) < 4))
            {
                var leader = ready.OrderByDescending(a => a.GetEffectiveStat("LDR")).First();
                ready.Remove(leader); ready.Insert(0, leader);
                var party = new Party();
                foreach (var a in ready) { training.Unassign(s, a.Id); party.TryAdd(a); }
                bool hopeless = ready.Sum(StatSum) * (1 + DungeonBalance.FullIntelDamageBonus) * 0.85 < DungeonResolver.RequiredPower(target);
                if (Grind && hopeless && ScoutingResolver.GetTier(target.IntelRate) == IntelTier.Complete)
                    expedition.TryDispatchGathering(s, party, Forest); // 勝ち目が無い間は採取で稼ぎ、機嫌と成長を保つ
                else if (Grind && hopeless)
                    expedition.TryDispatchSurvey(s, party, target);
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
            var pool = Active.Where(a => a.IsAvailable && !reserved.Contains(a.Id) && HpRatio(a) >= 0.6).ToList();
            if (pool.Count < 1) break;
            bool surveyOut = s.ActiveDungeonMissions.Any(m => m.MissionType == DungeonMissionType.Survey);
            var party = new Party();
            if (!surveyOut && pool.Count >= 2 && target != null && ScoutingResolver.GetTier(target.IntelRate) != IntelTier.Complete)
            {
                foreach (var a in pool.OrderByDescending(a => ScoutingResolver.GetAnalysisValue(a) + ScoutingResolver.GetGuardValue(a) + ScoutingResolver.GetStealthValue(a)).Take(3))
                { training.Unassign(s, a.Id); party.TryAdd(a); }
                if (!expedition.TryDispatchSurvey(s, party, target)) break;
            }
            else
            {
                foreach (var a in pool.OrderByDescending(HpRatio).Take(3)) { training.Unassign(s, a.Id); party.TryAdd(a); }
                if (!expedition.TryDispatchGathering(s, party, Forest)) break;
            }
        }

        // 訓練：主力以外の待機者を、伸びしろの大きい施設へ
        foreach (var a in Active.Where(a => !a.IsDispatched))
        {
            if (reserved.Contains(a.Id) || HpRatio(a) < 0.5 || a.Injury == InjurySeverity.Severe)
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

    static readonly FacilityType[] TrainingFacilities = { FacilityType.WarriorHall, FacilityType.Church, FacilityType.MageLab, FacilityType.ScoutPost };
    static readonly FacilityType[] BuildOrder =
    {
        FacilityType.WarriorHall, FacilityType.ScoutPost, FacilityType.Church, FacilityType.MageLab,
        FacilityType.WarriorHall, FacilityType.ScoutPost, FacilityType.Church, FacilityType.MageLab,
        FacilityType.Infirmary, FacilityType.Infirmary,
        FacilityType.WarriorHall, FacilityType.ScoutPost, FacilityType.Church, FacilityType.MageLab,
    };


    static readonly EquipmentSlot[] Slots = { EquipmentSlot.Weapon, EquipmentSlot.Armor, EquipmentSlot.Accessory1, EquipmentSlot.Accessory2 };
    static double Worth(EquipmentItem? e) => e == null ? 0 : DungeonBalance.BossPowerWeights.Sum(w => e.GetTotalStatBonus(w.Stat) * w.Weight) + e.GetTotalHpBonus() * 0.05;

    /// <summary>遺物を鑑定し、今より良い装備は上位の冒険者へ着せ、残りと素材は売る。</summary>
    void HandleLoot()
    {
        if (!UseLoot) return;
        foreach (var relic in s.UnidentifiedItems.ToList())
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
                var def = item.GetDefinition();
                if (def != null && def.Slot != at && slot is EquipmentSlot.Accessory1 or EquipmentSlot.Accessory2)
                {
                    // アクセサリの定義スロットに合わせる（TryEquip はスロット一致を要求）
                    at = def.Slot;
                }
                equipment.TryEquip(s, who, at, item);
            }
        }
        var sellable = s.Armory.Where(EquipmentSystem.CanSell).Select(e => e.Id.ToString()).ToList();
        if (sellable.Count > 0) EquipmentSystem.TrySellEquipments(s, sellable, out _);
        foreach (var id in s.Materials.Keys.ToList())
            economy.TrySellAllOfMaterial(s, id, out _);
    }

    static double ItemWorth(Item i) => DungeonBalance.BossPowerWeights.Sum(w => i.GetStatBonus(w.Stat) * w.Weight) + i.MaxHpBonus * 0.05;
    public static bool UseShop = true;

    /// <summary>所持金に余裕があれば、主力候補（上位4名）の装備を店で良いものへ買い替える（1週1点）。</summary>
    void Shop()
    {
        if (Frugal) return;
        if (!UseShop) return;
        var best = (Gain: 0.0, Who: (Adventurer?)null, Item: (Item?)null);
        foreach (var a in Active.Where(a => !a.IsDispatched).OrderByDescending(StatSum).Take(4))
            foreach (var item in ItemCatalog.GetAll().Where(i => i.IsAllowedFor(a.JobClass)))
            {
                if (s.Gold - item.Price < 800) continue;
                double gain = ItemWorth(item) - Worth(a.GetEquipped(item.Slot));
                if (gain <= 0.5) continue;
                double perGold = gain / item.Price;
                if (best.Who == null || perGold > best.Gain) best = (perGold, a, item);
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
        int idle = r.IdleHelpEntries.Sum(e => e.Gold);
        int gather = r.DungeonMissionResolutions.Sum(x => x.GatheringResult?.GoldEarned ?? 0);
        int loot = r.DungeonMissionResolutions.Sum(x => x.DepositedGold);
        int boss = r.DungeonMissionResolutions.Where(x => x.DungeonResult?.Outcome == DungeonOutcome.Victory).Sum(x => x.Boss?.RewardGold ?? 0);
        int training = -trainees * 20;
        Add("週給", -wages);
        Add("訓練費", training);
        Add("内職", side);
        Add("待機お手伝い", idle);
        Add("採取の報酬", gather);
        Add("潜行の拾得ゴールド", loot);
        Add("ボスの撃破報酬", boss);
        Add("その他の決算", total - (-wages + training + side + idle + gather + loot + boss));
    }
    void BuildFacility()
    {
        if (Frugal) return;
        if (s.UnderConstruction != null) return;
        var counts = new Dictionary<FacilityType, int>();
        foreach (var f in BuildOrder)
        {
            counts[f] = counts.GetValueOrDefault(f) + 1;
            if (s.GetFacilityLevel(f) >= counts[f]) continue;
            if (s.Gold - FacilityBalance.GetUpgradeCost(f, s.GetFacilityLevel(f)) >= 2500)
                facility.TryStartConstruction(s, f);
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
                   $" 森の最高到達 {Forest.ReachedFloor}F・撃破 {Forest.Bosses.Count(b => b.IsDefeated)}体・所持金 {s.Gold}G・機嫌 {s.MasterMood}・現役 {Active.Count()}名");
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

    static double W(Adventurer a) => DungeonBalance.BossPowerWeights.Sum(w => a.GetEffectiveStat(w.Stat) * w.Weight) / DungeonBalance.BossPowerWeights.Sum(w => w.Weight);

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
        foreach (var f in new[] { FacilityType.WarriorHall, FacilityType.Church, FacilityType.MageLab, FacilityType.ScoutPost })
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
                var best = new[] { FacilityType.WarriorHall, FacilityType.Church, FacilityType.MageLab, FacilityType.ScoutPost }
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
