using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 潜行の方針の部隊が主に勝てずに戻った週の記録（§0.98）：扉前で撤退した（勝率・備え・HPのどれかが構えに届かない）、または決戦に負けた。
    /// Power・RequiredPower・HpPercent は扉前に着いたときの討伐火力・要求火力・部隊のHP（道中で減ったHP込み）。来月の作戦の見込みはこれを元にする。
    /// RetreatReason は扉前の撤退の理由（決戦に負けたときは null）。
    /// First は、その部隊がこの主で初めて知らせるもの（→ SavedParty.StallNoticedBossId。月を止めるのは First のときだけ）。
    /// </summary>
    public sealed record SquadStall(SavedParty Party, FloorBoss Boss, DungeonField Field, double Power, double RequiredPower, double WinChance,
        int HpPercent, string? RetreatReason, bool First)
    {
        public bool Lost => RetreatReason == null;
    }

    /// <summary>来月の作戦の選び方（§0.98）。</summary>
    public enum PlanChoice
    {
        /// <summary>このまま潜り続ける（方針は変えない）。</summary>
        Continue,
        /// <summary>そのフィールドの調査に切り替える。調べが済んだら潜行に戻る（→ SavedParty.ResumeDiveAfterSurvey）。</summary>
        Survey,
        /// <summary>場所を「おまかせ」にして、今いちばん勝てそうな別の主を狙う。</summary>
        OtherBoss,
    }

    /// <summary>来月の作戦の選択肢1つ（月報のボタン1つ）。</summary>
    public sealed class PlanOption
    {
        public PlanChoice Choice { get; set; }
        /// <summary>ボタンの文字（「調査に切り替える」など）。</summary>
        public string Label { get; set; } = "";
        /// <summary>見込み（「今の解析45%。完全解析で火力+20%、勝率42%→71%。調べが済んだら…の潜行に戻る」など）。</summary>
        public string Detail { get; set; } = "";
        /// <summary>この手を選んだときの決戦の勝率の見込み（調査は完全解析のあと、別の主はその主、続けるは今の勝率）。</summary>
        public double WinChance { get; set; }
    }

    /// <summary>
    /// 月報の先頭に出す「来月の作戦」1件（§0.98）：主に勝てずに戻った潜行の部隊と、次の手の選択肢。
    /// Repeated（同じ部隊・同じ主で2回目から）のときは1行にたたむ。
    /// </summary>
    public sealed class MonthlyPlan
    {
        public Guid PartyId { get; set; }
        public string PartyName { get; set; } = "";
        public string FieldId { get; set; } = "";
        public Guid BossId { get; set; }
        /// <summary>「翠緑の原生林 第40層「…」」。</summary>
        public string BossLabel { get; set; } = "";
        /// <summary>何があったか（「扉前で引き返した（勝率42%・構え「標準」は6割から挑む）」「決戦に負けた（勝率55%）」）。</summary>
        public string Situation { get; set; } = "";
        /// <summary>どの手でも構えの下限に届かないときの助言（無ければ空）。</summary>
        public string Advice { get; set; } = "";
        public bool Repeated { get; set; }
        public List<PlanOption> Options { get; set; } = new();
    }

    /// <summary>
    /// 来月の作戦（§0.98、検証レポート GD-02）。潜行の方針の部隊が、扉前で撤退した・決戦に負けたとき、
    /// 黙って1階から潜り直させずに月を止め（→ WeekResult.SquadStalledAtBoss）、月報の先頭で次の手を選んでもらう：
    /// このまま潜り続ける／調査に切り替える（完全解析のあとの勝率）／別の主を狙う（おまかせの候補と勝率）。
    /// 同じ部隊・同じ主では最初の1回だけ止め、2回目からは月報に1行だけ出す。乱数は使わない。
    /// </summary>
    public static class NextMonthPlanSystem
    {
        /// <summary>週の決算のあと：潜行の部隊が主に勝てずに戻ったかを調べ、result.SquadStalls と止める印に入れる。WeekProcessingSystem が呼ぶ。</summary>
        public static void ProcessWeek(GameState state, WeeklySettlementResult result)
        {
            foreach (var r in result.DungeonMissionResolutions)
            {
                if (r.Boss == null) continue;
                var saved = SquadOf(state, r);
                if (saved == null) continue;

                if (r.DungeonResult?.Outcome == DungeonOutcome.Victory)
                {
                    if (saved.StallNoticedBossId == r.Boss.Id) saved.StallNoticedBossId = null;
                    continue;
                }

                bool lost = r.DungeonResult?.Outcome == DungeonOutcome.Retreat;
                bool retreated = r.ArrivedAtBossDoor && r.DoorRetreatReason != null;
                if (!lost && !retreated) continue;
                if (saved.Order != SquadOrder.Dive) continue;

                bool first = saved.StallNoticedBossId != r.Boss.Id;
                saved.StallNoticedBossId = r.Boss.Id;
                // 決戦に負けた週は、同じ週の扉前の記録（同じ出撃）から着いたときのHPを取る
                int hp = lost
                    ? result.DungeonMissionResolutions.FirstOrDefault(d => d.ArrivedAtBossDoor && d.Party == r.Party)?.DoorHpPercent ?? 100
                    : r.DoorHpPercent;
                var stall = lost
                    ? new SquadStall(saved, r.Boss, r.Field, r.DungeonResult!.PartyPower, r.DungeonResult.RequiredPower, r.DungeonResult.WinChance, hp, null, first)
                    : new SquadStall(saved, r.Boss, r.Field, r.DoorPower, r.DoorRequiredPower, r.DoorWinChance ?? 0, hp, r.DoorRetreatReason, first);
                result.SquadStalls.Add(stall);
                if (first) result.Flags.SquadStalledAtBoss = true;
            }
        }

        /// <summary>出撃の顔ぶれから、その出撃を出した保存部隊を探す（月報の部隊ごとと同じ考え方）。</summary>
        private static SavedParty? SquadOf(GameState state, DungeonMissionResolution r) =>
            state.SavedParties.FirstOrDefault(p => r.Party.Members.Any(m => p.MemberIds.Contains(m.Id)));

        /// <summary>
        /// 月報に出す来月の作戦（部隊ごとに、その月の最後に勝てずに戻った主について1件）。主がもう倒されていれば出さない。
        /// 見込みは、勝てなかったときの火力（扉前に着いたときの、道中で減ったHP込みの火力）を元にする：
        ///  - 続ける：そのときの勝率
        ///  - 調査：その火力に完全解析の上乗せを掛けた勝率（道中の損耗が減る分は見込まない）
        ///  - 別の主：今の顔ぶれでの火力の比（別の主÷この主）をそのときの火力に掛けた勝率（道中の減り方は同じと見る）
        /// </summary>
        public static List<MonthlyPlan> BuildPlans(GameState state, IEnumerable<SquadStall> stalls)
        {
            var plans = new List<MonthlyPlan>();
            foreach (var group in stalls.GroupBy(s => s.Party.Id))
            {
                var last = group.Last();
                if (last.Boss.IsDefeated || !state.SavedParties.Contains(last.Party)) continue;
                var plan = BuildPlan(state, last);
                plan.Repeated = !group.Any(s => s.First);
                plans.Add(plan);
            }
            return plans;
        }

        private static MonthlyPlan BuildPlan(GameState state, SquadStall stall)
        {
            var saved = stall.Party;
            var boss = stall.Boss;
            var party = Forecast(state, saved);
            var rule = Balance.SquadOrderBalance.For(saved.Stance);
            double now = stall.WinChance;
            string stance = SquadOrderSystem.StanceLabel(saved.Stance);

            var plan = new MonthlyPlan
            {
                PartyId = saved.Id,
                PartyName = saved.Name,
                FieldId = stall.Field.Id,
                BossId = boss.Id,
                BossLabel = $"{stall.Field.Name} 第{boss.Floor}層「{boss.Name}」",
                Situation = (stall.Lost ? "決戦に負けた" : $"扉前で引き返した（{stall.RetreatReason}）")
                    + $"。着いたときのHP{stall.HpPercent}%・火力{stall.Power:F0}／要求{stall.RequiredPower:F0}"
                    + (stall.RetreatReason?.Contains("勝率") == true ? "" : $"・勝率{Pct(now)}"),
            };

            if (ScoutingResolver.GetTier(boss.IntelRate) != IntelTier.Complete)
            {
                double after = DungeonResolver.WinChance(stall.Power * (1.0 + Balance.DungeonBalance.FullIntelDamageBonus), stall.RequiredPower);
                plan.Options.Add(new PlanOption
                {
                    Choice = PlanChoice.Survey,
                    Label = "調査に切り替える",
                    WinChance = after,
                    Detail = $"{stall.Field.Name}を調べる（今の解析{Pct(boss.IntelRate)}）。完全解析で火力+{Pct(Balance.DungeonBalance.FullIntelDamageBonus)}・勝率{Pct(now)}→{Pct(after)}、道中の損耗も減る。調べが済んだら{stall.Field.Name}の潜行に戻る",
                });
            }

            if (OtherTarget(state, saved, boss, party) is { } other)
            {
                double here = DungeonResolver.CalculateBossPower(party, boss);
                double power = here > 0 ? stall.Power * DungeonResolver.CalculateBossPower(party, other.Boss) / here : 0;
                double chance = DungeonResolver.WinChance(power, DungeonResolver.RequiredPower(other.Boss, state, party));
                if (chance > now)
                    plan.Options.Add(new PlanOption
                    {
                        Choice = PlanChoice.OtherBoss,
                        Label = "別の主を狙う（おまかせ）",
                        WinChance = chance,
                        Detail = $"今なら{other.Field.Name} 第{other.Boss.Floor}層「{other.Boss.Name}」・勝率{Pct(chance)}の見込み",
                    });
            }

            plan.Options.Add(new PlanOption
            {
                Choice = PlanChoice.Continue,
                Label = "このまま潜り続ける",
                WinChance = now,
                Detail = $"構え「{stance}」（勝率{Pct(rule.MinWinChance)}から挑む）。HPと解析が同じなら、また勝率{Pct(now)}",
            });

            plan.Advice = AdviceFor(stall, plan, rule.MinWinChance, saved.Stance);
            return plan;
        }

        /// <summary>
        /// どの手でも足りないときの助言（無ければ空）。道中でHPが削られている・備えが足りない・火力が足りない、の順に見る。
        /// HPが低いほど火力は下がる（→ DungeonPowerCalculator.MemberPower）ので、HPの減りを先に知らせる。
        /// </summary>
        private static string AdviceFor(SquadStall stall, MonthlyPlan plan, double minChance, DoorStance stance)
        {
            if (stall.HpPercent < LowDoorHpPercent)
                return $"扉前に着いたときHPが{stall.HpPercent}%しか残っていない（HPの割合が火力に掛かる）。調査で区間を解析する・走破力（VIT・MND）を上げる・神官を入れると、道中の損耗が減る";
            if (stall.RetreatReason?.Contains("備え") == true)
                return "ギミックへの備えが足りない。効く職業・伝説級の装備の子を部隊に入れるか、構えを変える（ボスの欄の特殊能力を参照）";
            if (plan.Options.Max(o => o.WinChance) < minChance)
                return "どの手でも構えの下限に届かない見込み。訓練・装備・編成で火力を上げる" + (stance == DoorStance.Bold ? "" : "か、構えを強気にする");
            return "";
        }

        /// <summary>扉前に着いたときのHPがこれ未満なら、道中の損耗を先に知らせる（%）。</summary>
        public const int LowDoorHpPercent = 50;

        /// <summary>
        /// 「おまかせ」で潜行したときに狙う、ほかのフィールドの次の主と勝率（→ SquadOrderSystem.ResolveField の潜行と同じ選び方）。
        /// 今の主のフィールドは除く。候補が無ければ null。
        /// </summary>
        public static (DungeonField Field, FloorBoss Boss, double Chance)? OtherTarget(GameState state, SavedParty saved, FloorBoss current, Party party)
        {
            if (party.IsEmpty) return null;
            var best = state.DungeonFields.Where(f => f.IsUnlocked && !f.Bosses.Contains(current)).OrderBy(f => f.Order)
                .Select(f => (Field: f, Boss: f.GetNextActiveBoss()))
                .Where(x => x.Boss != null)
                .Select(x => (x.Field, Boss: x.Boss!, Ratio: DungeonResolver.CalculateBossPower(party, x.Boss!) / Math.Max(1.0, DungeonResolver.RequiredPower(x.Boss!, state, party))))
                .OrderByDescending(x => x.Ratio)
                .FirstOrDefault();
            if (best.Boss == null) return null;
            return (best.Field, best.Boss, DungeonResolver.WinChance(party, best.Boss, state));
        }

        /// <summary>
        /// 来月の作戦を選ぶ：部隊の方針を書き換える。方針が変わったら true（出撃中なら、画面が呼び戻す → SquadOrderSystem.Recall）。
        /// 部隊が見つからない・選択肢が今は選べないなら false で、何も変えない。
        /// </summary>
        public static bool Apply(GameState state, MonthlyPlan plan, PlanChoice choice)
        {
            var saved = state.SavedParties.FirstOrDefault(p => p.Id == plan.PartyId);
            if (saved == null) return false;
            switch (choice)
            {
                case PlanChoice.Survey:
                    if (state.DungeonFields.All(f => f.Id != plan.FieldId)) return false;
                    saved.Order = SquadOrder.Survey;
                    saved.OrderFieldId = plan.FieldId;
                    saved.ResumeDiveAfterSurvey = true;
                    return true;
                case PlanChoice.OtherBoss:
                    saved.Order = SquadOrder.Dive;
                    saved.OrderFieldId = SavedParty.AutoFieldId;
                    saved.ResumeDiveAfterSurvey = false;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>見込みの計算に使う顔ぶれ：部隊の現役の全員（重傷・訓練中・大会の者も含める。来月には戻る見込みとして）。</summary>
        private static Party Forecast(GameState state, SavedParty saved)
        {
            var party = new Party();
            foreach (var id in saved.MemberIds)
                if (state.Adventurers.FirstOrDefault(a => a.Id == id) is { IsRetired: false } a)
                    party.TryAdd(a);
            return party;
        }

        private static string Pct(double value) => $"{value * 100:F0}%";
    }
}
