// 理不尽チェック（2026-10-15、→ README の「理不尽チェック」・docs/06）。
// 「誰でもクリアできる」ではなく「理不尽な敗北や躓きがない」ことを見る。audit の回ごとに次を数える。
//  1. 前触れと手番：機嫌の危機（20未満）・所持金のマイナスが始まってから負けるまでに、プレイヤーの手番が何回あったか。
//     手番0回の負け（前触れなしの負け）は1件でも直す候補。ひと月で機嫌が大きく下がった回・危なかったが立て直した回も数える。
//  2. 人間らしい遊び方：spendthrift（蓄えを残さない）・reckless（支配者に無理に挑む・扉前は強気）・overhire（雇えるだけ雇う）・
//     noloan（借金を知らない）・naive（spendthrift＋noloan）。
//  3. 詰んでからの立て直し：shock=週:種類（broke＝所持金0・debt＝所持金−3000・mood＝機嫌20・ace＝出撃していない上位2人を失う。
//     + でつなぐ。例 shock=200:ace+mood）。その週の手番のはじめに起こし、そのあと負けたか・立て直すまでの週を数える。
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

partial class GameSim
{
    // ==== 設定 ====
    public static bool Spendthrift, Reckless, Overhire, NoLoan, NoMoodGather;
    public static int ShockWeek;
    public static string[] ShockKinds = Array.Empty<string>();

    public static void ConfigureUnfair(HashSet<string> opts)
    {
        bool naive = opts.Contains("naive");
        Spendthrift = naive || opts.Contains("spendthrift");
        NoLoan = naive || opts.Contains("noloan");
        Reckless = opts.Contains("reckless");
        Overhire = opts.Contains("overhire");
        NoMoodGather = naive || opts.Contains("nomoodgather"); // 機嫌が下がっても調査の部隊を採取に回さない（§0.139）
        if (Reckless) AuditStance = DoorStance.Bold;
        var shock = opts.FirstOrDefault(o => o.StartsWith("shock="))?.Substring(6);
        if (shock != null)
        {
            var parts = shock.Split(':');
            ShockWeek = int.Parse(parts[0]);
            ShockKinds = parts.Length > 1 ? parts[1].Split('+') : new[] { "ace" };
        }
    }

    static string PersonaLabel()
    {
        var p = new List<string>();
        if (Spendthrift) p.Add("蓄えを残さない");
        if (NoLoan) p.Add("借金を知らない");
        if (Reckless) p.Add("無理に挑む");
        if (Overhire) p.Add("雇いすぎ");
        if (NoMoodGather && !(Spendthrift && NoLoan)) p.Add("機嫌を見ない");
        if (ShockWeek > 0) p.Add($"衝撃 {ShockWeek}週:{string.Join("+", ShockKinds)}");
        return p.Count == 0 ? "ふつう" : string.Join("・", p);
    }

    // ==== 記録 ====
    public sealed record DefeatInfo(string Reason, int Week, int TurnsInDanger, int WeeksInDanger, int LastTurnMood, int LastTurnGold,
        int WeeksSinceLastTurn, int WeeksSinceKill, int Kills);
    public DefeatInfo? DefeatDetail;

    int? moodDangerStart, goldDangerStart;
    int moodDangerTurns, goldDangerTurns;
    int lastTurnWeek = 1, lastTurnMood, lastTurnGold, lastKillWeek = 1, lastKills, monthLowMood = 100;
    public int SuddenMoodDrops;           // 手番から次の手番までに機嫌が30以上下がった回
    public int MoodRecoveries;            // 機嫌が10未満まで下がってから20以上に戻った回
    public int GoldRecoveries;            // 所持金のマイナスが3週以上続いてから戻った回
    public int MoodDangerSpells, GoldDangerSpells; // 危機に入った回数
    public readonly List<int> MoodDangerTurnCounts = new(), GoldDangerTurnCounts = new(); // 立て直した危機の、危機の間の手番の数
    int moodDangerMin = 100, goldDangerWeeks;

    // 衝撃
    public bool ShockApplied;
    public int ShockAppliedWeek;
    public string ShockNote = "";
    public int? MoodBackWeek, GoldBackWeek, PowerBackWeek;
    double powerBeforeShock;

    /// <summary>手番のはじめ（ActUi の前）。衝撃を起こし、危機の間の手番を数える。</summary>
    void UnfairTurnStart()
    {
        if (ShockWeek > 0 && !ShockApplied && s.WeekNumber >= ShockWeek) ApplyShock();
        if (moodDangerStart != null) moodDangerTurns++;
        if (goldDangerStart != null) goldDangerTurns++;
        if (Turns.Count > 0 && lastTurnMood - monthLowMood >= 30) SuddenMoodDrops++; // 前の手番から、いちばん下がったところまで
        lastTurnWeek = s.WeekNumber; lastTurnMood = s.MasterMood; lastTurnGold = s.Gold;
        monthLowMood = s.MasterMood;
    }

    /// <summary>週の決算のあと。</summary>
    /// <summary>アルベールのへそくりの直前の所持金（§0.140。へそくりの額を決めるために測る）。</summary>
    public int? GoldBeforeBailout;

    void UnfairWeek(WeeklySettlementResult result)
    {
        WallWeek(result);
        if (result.DeficitRelief is { BailoutGold: > 0 } relief) GoldBeforeBailout = s.Gold - relief.BailoutGold;
        int kills = s.DungeonFields.Sum(f => f.Bosses.Count(b => b.IsDefeated));
        if (kills > lastKills) { lastKills = kills; lastKillWeek = s.WeekNumber; }
        monthLowMood = Math.Min(monthLowMood, s.MasterMood);
        if (s.DefeatReason != null && DefeatDetail == null && lastTurnMood - monthLowMood >= 30) SuddenMoodDrops++; // 次の手番が来ない

        bool moodDanger = s.MasterMood < MasterMoodBalance.TierThresholdGrumpy;
        if (moodDanger && moodDangerStart == null) { moodDangerStart = s.WeekNumber; moodDangerTurns = 0; moodDangerMin = 100; MoodDangerSpells++; }
        if (moodDanger) moodDangerMin = Math.Min(moodDangerMin, s.MasterMood);
        if (!moodDanger && moodDangerStart != null)
        {
            if (moodDangerMin < 10) MoodRecoveries++;
            MoodDangerTurnCounts.Add(moodDangerTurns);
            moodDangerStart = null;
        }
        bool goldDanger = s.Gold < 0;
        if (goldDanger && goldDangerStart == null) { goldDangerStart = s.WeekNumber; goldDangerTurns = 0; goldDangerWeeks = 0; GoldDangerSpells++; }
        if (goldDanger) goldDangerWeeks++;
        if (!goldDanger && goldDangerStart != null)
        {
            if (goldDangerWeeks >= 3) GoldRecoveries++;
            GoldDangerTurnCounts.Add(goldDangerTurns);
            goldDangerStart = null;
        }

        if (ShockApplied)
        {
            if (MoodBackWeek == null && s.MasterMood >= MasterMoodBalance.TierThresholdNormal) MoodBackWeek = s.WeekNumber;
            if (GoldBackWeek == null && s.Gold >= 5000) GoldBackWeek = s.WeekNumber;
            if (PowerBackWeek == null && TopPower() >= powerBeforeShock) PowerBackWeek = s.WeekNumber;
        }

        if (s.DefeatReason != null && DefeatDetail == null)
        {
            bool mood = s.DefeatReason == DefeatReason.DismissedByMaster;
            int? start = mood ? moodDangerStart : goldDangerStart;
            DefeatDetail = new DefeatInfo(mood ? "副官解雇" : "破産", s.WeekNumber,
                mood ? moodDangerTurns : goldDangerTurns, start == null ? 0 : s.WeekNumber - start.Value + 1,
                lastTurnMood, lastTurnGold, s.WeekNumber - lastTurnWeek, s.WeekNumber - lastKillWeek, kills);
        }
    }

    // ==== 踊り場の壁（年のはじめの狙うボスと、扉前で撤退したときの勝率） ====
    public readonly List<string> YearTargets = new();
    public readonly Dictionary<int, List<int>> YearRetreatWin = new();
    public readonly Dictionary<int, int> YearKills = new();
    public readonly Dictionary<int, List<int>> YearDoorHp = new();
    void WallWeek(WeeklySettlementResult result)
    {
        int year = GameCalendar.YearOf(s.WeekNumber);
        foreach (var r in result.DungeonMissionResolutions.Where(r => r.ArrivedAtBossDoor && r.DoorRetreatReason != null))
        {
            var m = System.Text.RegularExpressions.Regex.Match(r.DoorRetreatReason!, @"勝率(\d+)%");
            if (m.Success) { if (!YearRetreatWin.TryGetValue(year, out var l)) YearRetreatWin[year] = l = new List<int>(); l.Add(int.Parse(m.Groups[1].Value)); }
            if (!YearDoorHp.TryGetValue(year, out var h)) YearDoorHp[year] = h = new List<int>();
            h.Add((int)(100 * r.Party.Members.Average(a => (double)a.CurrentHP / a.MaxHP)));
        }
        YearKills[year] = s.DungeonFields.Sum(f => f.Bosses.Count(b => b.IsDefeated));
        if (s.WeekNumber % 48 == 1)
        {
            var t = PickTarget();
            { if (t == null) YearTargets.Add("―"); else { var best = Active.OrderByDescending(a => DungeonPowerCalculator.MemberPower(a, t.Value.Boss)).Take(4).ToList(); var pb = PickForBoss(t.Value.Boss, Active.ToList()); var pp = new Party(); foreach (var a in pb) pp.TryAdd(a); YearTargets.Add($"{FieldShort(t.Value.Field.Order)}{t.Value.Boss.Floor}F {DungeonPowerCalculator.PartyPower(best, t.Value.Boss, s) / DungeonResolver.RequiredPower(t.Value.Boss, s):F2}/{DungeonResolver.CalculateBossPower(pp, t.Value.Boss, s) / DungeonResolver.RequiredPower(t.Value.Boss, s, pp):F2} " + string.Join("/", s.DungeonFields.OrderBy(f => f.Order).Select(f => f.IsUnlocked ? $"{FieldShort(f.Order)}{f.GetNextActiveBoss()?.Floor}{(f.GetNextActiveBoss() is { } nb && RulerSystem.RulerAt(s, nb) != null ? "主" : "")}" : ""))); } }
        }
    }

    double TopPower() => Active.OrderByDescending(StatSum).Take(4).Sum(StatSum);

    void ApplyShock()
    {
        ShockApplied = true;
        ShockAppliedWeek = s.WeekNumber;
        powerBeforeShock = TopPower();
        var notes = new List<string>();
        foreach (var kind in ShockKinds)
        {
            switch (kind)
            {
                case "broke": s.Gold = Math.Min(s.Gold, 0); notes.Add("所持金0"); break;
                case "debt": s.Gold = Math.Min(s.Gold, -3000); notes.Add("所持金−3000"); break;
                case "mood": s.MasterMood = Math.Min(s.MasterMood, MasterMoodBalance.TierThresholdGrumpy); notes.Add($"機嫌{s.MasterMood}"); break;
                case "ace":
                    var away = s.ActiveDungeonMissions.SelectMany(m => m.Party.Members).Select(a => a.Id).ToHashSet();
                    var victims = Active.Where(a => !away.Contains(a.Id) && !a.NeverRetires && !a.IsDispatched).OrderByDescending(StatSum).Take(2).ToList();
                    foreach (var v in victims)
                    {
                        EquipmentSystem.UnequipAllToArmory(s, v, $"{v.Name}（除籍）から返還");
                        v.FellAtWeek = s.WeekNumber;
                        s.TrainingAssignments.Remove(v.Id);
                        s.Adventurers.Remove(v);
                        s.FallenAdventurers.Add(v);
                        foreach (var p in s.SavedParties) p.MemberIds.Remove(v.Id);
                        foreach (var r in s.Rulers) r.RepresentativeIds.Remove(v.Id);
                    }
                    notes.Add($"上位{victims.Count}人を失う");
                    break;
            }
        }
        ShockNote = string.Join("・", notes);
    }

    // ==== まとめ ====
    static void PrintUnfair(List<GameSim> sims, int weeks)
    {
        Console.WriteLine($"### 理不尽チェック（遊び方：{PersonaLabel()}）");
        var defeats = sims.Where(x => x.DefeatDetail != null).Select(x => (Sim: x, D: x.DefeatDetail!)).ToList();
        int noWarning = defeats.Count(d => d.D.TurnsInDanger == 0);
        int oneTurn = defeats.Count(d => d.D.TurnsInDanger == 1);
        Console.WriteLine($"負け：{defeats.Count}/{sims.Count}回（前触れなし＝危機の間に手番0回 {noWarning}・手番1回 {oneTurn}・手番2回以上 {defeats.Count - noWarning - oneTurn}）");
        foreach (var (sim, d) in defeats)
            Console.WriteLine($"  回{sim.RunIndex}：{d.Reason} {GameCalendar.YearOf(d.Week)}年目（{d.Week}週）　危機の間の手番 {d.TurnsInDanger}回・{d.WeeksInDanger}週　" +
                $"最後の手番は{d.WeeksSinceLastTurn}週前（機嫌{d.LastTurnMood}・所持金{d.LastTurnGold}G）　最後のボス撃破から{d.WeeksSinceKill}週（撃破{d.Kills}体）");
        double years = Math.Max(1, sims.Sum(x => (x.s.WeekNumber - 1) / 48.0));
        Console.WriteLine($"機嫌の危機（20未満）に入った：{sims.Sum(x => x.MoodDangerSpells)}回（{sims.Count(x => x.MoodDangerSpells > 0)}回のうち）・10未満から立て直した {sims.Sum(x => x.MoodRecoveries)}　" +
            $"ひと月で機嫌が30以上下がった：{sims.Sum(x => x.SuddenMoodDrops)}回");
        Console.WriteLine($"所持金のマイナス：{sims.Sum(x => x.GoldDangerSpells)}回（{sims.Count(x => x.GoldDangerSpells > 0)}回のうち）・3週以上続いて立て直した {sims.Sum(x => x.GoldRecoveries)}　" +
            $"立て直した危機の間の手番（機嫌／所持金）：{Spread(sims.SelectMany(x => x.MoodDangerTurnCounts))}／{Spread(sims.SelectMany(x => x.GoldDangerTurnCounts))}");
        // 序盤の流れ（§0.140）：アルベールのへそくり・イザベラの早めの来訪・来訪と資金の画面が開いた年・へそくりの直前の所持金
        static string Yr(IEnumerable<int> weeksList) { var l = weeksList.Select(w => 1 + (w - 1) / 48.0).OrderBy(v => v).ToList(); return l.Count == 0 ? "―" : $"{l[l.Count / 2]:F1}年目〔{l[0]:F1}〜{l[^1]:F1}〕"; }
        Console.WriteLine($"序盤の流れ（§0.140）：へそくり {sims.Count(x => x.s.AlbertBailoutWeek != null)}/{sims.Count}回（{Yr(sims.Where(x => x.s.AlbertBailoutWeek != null).Select(x => x.s.AlbertBailoutWeek!.Value))}）・" +
            $"直前の所持金 {Spread(sims.Where(x => x.GoldBeforeBailout != null).Select(x => x.GoldBeforeBailout!.Value))}G　早めの来訪 {sims.Count(x => x.s.IsabellaEarlyVisit)}回　" +
            $"来訪 {Yr(sims.Where(x => x.s.IsabellaVisitWeek != null).Select(x => x.s.IsabellaVisitWeek!.Value))}（{sims.Count(x => x.s.IsabellaVisitWeek != null)}回）　" +
            $"資金の画面 {Yr(sims.Where(x => x.s.FundingOpenedWeek > 1).Select(x => x.s.FundingOpenedWeek))}（{sims.Count(x => x.s.FundingOpenedWeek > 1)}回）");
        // 踊り場の壁：年のはじめの狙うボスと火力の割合（上位4人の火力÷要求火力、ギミック込み）、その年の扉前の撤退の勝率、撃破数（中央値）
        Console.WriteLine("年ごとの壁（中央値。狙うボス・火力÷要求・扉前で撤退したときの勝率・年末の撃破数）：");
        for (int y = 1; y <= Math.Min(12, weeks / 48); y++)
        {
            var targets = sims.Where(x => x.YearTargets.Count >= y).Select(x => x.YearTargets[y - 1]).ToList();
            if (targets.Count == 0) break;
            var common = targets.Select(t => t.Split(' ')[0]).GroupBy(t => t).OrderByDescending(g => g.Count()).First().Key;
            var ratios = targets.Select(t => t.Split(' ')).Where(p => p.Length > 1).Select(p => double.Parse(p[1].Split('/')[0])).OrderBy(v => v).ToList();
            var wins = sims.SelectMany(x => x.YearRetreatWin.GetValueOrDefault(y) ?? new List<int>()).OrderBy(v => v).ToList();
            var kills = sims.Where(x => x.YearKills.ContainsKey(y)).Select(x => x.YearKills[y]).OrderBy(v => v).ToList();
            var hps = sims.SelectMany(x => x.YearDoorHp.GetValueOrDefault(y) ?? new List<int>()).OrderBy(v => v).ToList();
            var gim = targets.Select(t => t.Split(' ')).Where(p => p.Length > 1 && p[1].Contains('/')).Select(p => double.Parse(p[1].Split('/')[1])).OrderBy(v => v).ToList();
            var fieldsNext = targets.Select(t => t.Split(' ')).Where(p => p.Length > 2).GroupBy(p => p[2]).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? "";
            Console.WriteLine($"  {y}年目：{common}（{targets.Count(t => t.StartsWith(common + ' '))}/{targets.Count}）　火力÷要求 {(ratios.Count == 0 ? "―" : ratios[ratios.Count / 2].ToString("F2"))}（ギミック込み {(gim.Count == 0 ? "―" : gim[gim.Count / 2].ToString("F2"))}）　撤退 {wins.Count}回・勝率 {(wins.Count == 0 ? "―" : wins[wins.Count / 2] + "%")}・HP {(hps.Count == 0 ? "―" : hps[hps.Count / 2] + "%")}　撃破 {(kills.Count == 0 ? "―" : kills[kills.Count / 2].ToString())}　次のボス（多い形）{fieldsNext}");
        }
        if (ShockWeek > 0)
        {
            var shocked = sims.Where(x => x.ShockApplied).ToList();
            string Weeks(Func<GameSim, int?> f) => Spread(shocked.Select(x => f(x) is int w ? w - x.ShockAppliedWeek : -1).Where(w => w >= 0)) +
                $"（戻らなかった {shocked.Count(x => f(x) == null)}）";
            Console.WriteLine($"衝撃：{shocked.Count}回に起こした（{shocked.FirstOrDefault()?.ShockNote}）　そのあと負けた {shocked.Count(x => x.DefeatDetail != null)}・" +
                $"クリア {shocked.Count(x => x.s.IsGameCleared)}　機嫌50に戻るまで {Weeks(x => x.MoodBackWeek)}週　所持金5000Gに戻るまで {Weeks(x => x.GoldBackWeek)}週　" +
                $"上位4人の強さが戻るまで {Weeks(x => x.PowerBackWeek)}週");
        }
        Console.WriteLine();
    }

    static string Spread(IEnumerable<int> values)
    {
        var l = values.OrderBy(v => v).ToList();
        if (l.Count == 0) return "―";
        return $"中央値{l[l.Count / 2]}〔{l[0]}〜{l[^1]}〕";
    }
}
