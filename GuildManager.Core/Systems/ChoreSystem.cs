using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>まとめて鑑定する遺物の見込み（→ ChoreSystem.PlanAppraisal）。</summary>
    /// <param name="Items">鑑定する遺物（銅・銀だけ。銀→銅、深い順）。</param>
    /// <param name="TotalCost">鑑定費用の合計。</param>
    /// <param name="Reserve">残しておく所持金（→ ChoreSystem.Reserve）。</param>
    /// <param name="OverBudget">予算に収まらず、今回は見送る銅・銀の数。</param>
    /// <param name="OneByOne">金・虹（1つずつ鑑定する）の数。</param>
    public sealed record AppraisalPlan(IReadOnlyList<UnidentifiedItem> Items, int TotalCost, int Reserve, int OverBudget, int OneByOne);

    /// <summary>おすすめの装備の入れ替え1件（→ ChoreSystem.PlanGear）。Before＝今の装備（空なら null）、After＝着せる物。</summary>
    public sealed record GearChange(Adventurer Adventurer, EquipmentSlot Slot, EquipmentItem? Before, EquipmentItem After);

    /// <summary>まとめて売る物の見込み（→ ChoreSystem.PlanSale）。Materials は素材Id→売る数。</summary>
    public sealed record SalePlan(IReadOnlyList<EquipmentItem> Equipment, IReadOnlyDictionary<string, int> Materials, int TotalGold)
    {
        public bool IsEmpty => Equipment.Count == 0 && Materials.Count == 0;
    }

    /// <summary>おすすめの大会の申し込み1件（→ ChoreSystem.RecommendEntries）。</summary>
    public sealed record TournamentPick(TournamentEvent Event, Adventurer Adventurer, TournamentDiscipline Discipline, TournamentPrep Prep, double WinChance);

    /// <summary>交渉の自動（→ ChoreSystem.ProcessAutoBonus）でボーナスを払った1件。</summary>
    public sealed record AutoBonus(Adventurer Adventurer, int Gold);

    /// <summary>
    /// いつも同じ答えになる手入れを、まとめて1回で済ませる（§0.99、改修計画の段3。検証レポートの GD-03・UI-03）。
    ///  - まとめて鑑定：銅・銀の遺物を、所持金に「週給の合計×ChoreReserveWageWeeks」を残せる範囲で鑑定する（金・虹は1つずつのまま）
    ///  - おすすめを着せる：部隊のメンバーへ、保管庫の武具を職業の重み（→ boss_power_weights.csv）で強い順に着せる
    ///  - まとめて売る：誰が着けても今の装備より良くならない武具（束で売れる物だけ）と、研究・霊薬・秘薬・受けた依頼に要る分を超えた素材
    ///  - おすすめの子で申し込む：今月の個人の大会へ、潜行の部隊以外から強い順に（潜行の部隊の子は G1・特別と、得意な部門の G2 だけ）
    ///  - 交渉の自動：システム画面で選んだとき、契約の不満が出た子へ、予算の範囲でボーナスを払う
    /// どれも乱数を使わない（鑑定の中身の抽選だけは AppraisalSystem の乱数）。画面は「見込み」を出してから「実行」を呼ぶ。
    /// </summary>
    public static class ChoreSystem
    {
        /// <summary>同じ能力の補正なら最大HPの多い方を選ぶための、HPの小さな重み（能力値の重みの比べ合いを崩さない大きさ）。</summary>
        private const double HpTieBreakWeight = 0.001;

        private const double Epsilon = 1e-9;

        // ==================== 予算 ====================

        /// <summary>まとめて鑑定・交渉の自動で残しておく所持金（現役の週給の合計×ChoreReserveWageWeeks。派遣中の子の週給は先方持ちなので数えない）。</summary>
        public static int Reserve(GameState state) =>
            state.Adventurers.Where(a => !a.IsOnLoan).Sum(a => a.WeeklyWage) * EconomyBalance.ChoreReserveWageWeeks;

        // ==================== まとめて鑑定 ====================

        /// <summary>まとめて鑑定してよい遺物か（銅・銀だけ。金・虹は中身が大きいので1つずつ）。</summary>
        public static bool IsBulkAppraisable(UnidentifiedItem item) => item.Rarity <= ItemRarity.Rare;

        /// <summary>
        /// まとめて鑑定の見込み。relicIds を渡すとその遺物の中から（月報の「今月の遺物」）、null なら倉庫の全部から選ぶ。
        /// 銀→銅、深い順に並べ、所持金−残す額に収まるものを順に足す（収まらない物は飛ばして、安い物を続けて見る）。
        /// </summary>
        public static AppraisalPlan PlanAppraisal(GameState state, IEnumerable<string>? relicIds = null)
        {
            var ids = relicIds?.ToHashSet();
            var pool = AppraisalSystem.SortForDisplay(state).Where(r => ids == null || ids.Contains(r.Id)).ToList();
            int reserve = Reserve(state);
            int budget = state.Gold - reserve;
            var picked = new List<UnidentifiedItem>();
            int cost = 0, over = 0;
            foreach (var relic in pool.Where(IsBulkAppraisable))
            {
                if (cost + relic.AppraisalCost <= budget)
                {
                    picked.Add(relic);
                    cost += relic.AppraisalCost;
                }
                else
                    over++;
            }
            return new AppraisalPlan(picked, cost, reserve, over, pool.Count(r => !IsBulkAppraisable(r)));
        }

        /// <summary>見込みどおりにまとめて鑑定し、鑑定の結果を順に返す（1つも無ければ空）。</summary>
        public static List<AppraisalResult> AppraiseBulk(GameState state, AppraisalSystem appraisal, IEnumerable<string>? relicIds = null)
        {
            var results = new List<AppraisalResult>();
            foreach (var relic in PlanAppraisal(state, relicIds).Items)
                if (appraisal.Appraise(state, relic.Id) is { } result)
                    results.Add(result);
            return results;
        }

        // ==================== おすすめを着せる ====================

        /// <summary>
        /// 武具の値打ち（その子にとって）：職業の重み（→ DungeonBalance.GetBossPowerWeights、火力と同じ重み）を掛けた能力の補正の合計。
        /// 同じなら最大HPの多い方を上にする。空の枠は0。
        /// </summary>
        public static double GearScore(Adventurer adventurer, EquipmentItem? item)
        {
            if (item == null) return 0;
            double score = 0;
            foreach (var (stat, weight) in DungeonBalance.GetBossPowerWeights(adventurer.JobClass))
                score += item.GetTotalStatBonus(stat) * weight;
            return score + item.GetTotalHpBonus() * HpTieBreakWeight;
        }

        /// <summary>武具を着けられる枠（装飾品は2枠のどちらにも）。カタログから引けない個体は空。</summary>
        private static EquipmentSlot[] SlotsFor(EquipmentItem item) => item.GetSlot() switch
        {
            null => Array.Empty<EquipmentSlot>(),
            var s when EquipmentSystem.IsAccessory(s.Value) => new[] { EquipmentSlot.Accessory1, EquipmentSlot.Accessory2 },
            var s => new[] { s.Value },
        };

        /// <summary>
        /// 部隊のメンバーへの、おすすめの装備の入れ替え。保管庫の武具だけを使い（ほかの部隊の子からは外さない）、
        /// 「いちばん値打ちの上がる1件」をくり返し選ぶ（外した物は保管庫へ戻り、ほかのメンバーが使える）。
        /// 出撃中・派遣中のメンバーは変えない。結果は枠ごとの最後の形（今の装備と違う枠だけ）。
        /// </summary>
        public static List<GearChange> PlanGear(GameState state, SavedParty party)
        {
            var members = party.MemberIds.Select(state.FindAdventurer)
                .OfType<Adventurer>().Where(a => !a.IsRetired && EquipmentSystem.CanChangeEquipment(a)).ToList();
            var worn = new Dictionary<(Guid, EquipmentSlot), EquipmentItem?>();
            foreach (var m in members)
                foreach (var slot in Adventurer.AllSlots)
                    worn[(m.Id, slot)] = m.GetEquipped(slot);
            var pool = state.Armory.Where(e => e.GetDefinition() != null).ToList();

            // 合計の値打ちが毎回きちんと上がるので止まる。念のため回数に上限を置く。
            for (int step = 0; step < 200; step++)
            {
                (EquipmentItem Item, Adventurer Who, EquipmentSlot Slot)? best = null;
                double bestGain = Epsilon;
                foreach (var item in pool)
                    foreach (var m in members.Where(m => item.IsAllowedFor(m.JobClass)))
                        foreach (var slot in SlotsFor(item))
                        {
                            double gain = GearScore(m, item) - GearScore(m, worn[(m.Id, slot)]);
                            if (gain > bestGain + Epsilon)
                            {
                                bestGain = gain;
                                best = (item, m, slot);
                            }
                        }
                if (best is not { } pick) break;
                pool.Remove(pick.Item);
                if (worn[(pick.Who.Id, pick.Slot)] is { } previous)
                    pool.Add(previous);
                worn[(pick.Who.Id, pick.Slot)] = pick.Item;
            }

            var changes = new List<GearChange>();
            foreach (var m in members)
                foreach (var slot in Adventurer.AllSlots)
                    if (worn[(m.Id, slot)] is { } after && !ReferenceEquals(after, m.GetEquipped(slot)))
                        changes.Add(new GearChange(m, slot, m.GetEquipped(slot), after));
            return changes;
        }

        /// <summary>入れ替えたときの、子ごとの能力の増減と最大HPの増減（見込みの表示用）。</summary>
        public static List<(Adventurer Adventurer, Dictionary<string, int> Stats, int Hp)> SummarizeGear(IEnumerable<GearChange> changes) =>
            changes.GroupBy(c => c.Adventurer).Select(g =>
            {
                var stats = new Dictionary<string, int>();
                foreach (var stat in AdventurerStatAccessor.AllStatNames)
                {
                    int d = g.Sum(c => c.After.GetTotalStatBonus(stat) - (c.Before?.GetTotalStatBonus(stat) ?? 0));
                    if (d != 0) stats[stat] = d;
                }
                return (g.Key, stats, g.Sum(c => c.After.GetTotalHpBonus() - (c.Before?.GetTotalHpBonus() ?? 0)));
            }).ToList();

        /// <summary>おすすめを着せる（見込みを作り直してから行う）。入れ替えた分を返す。</summary>
        public static List<GearChange> ApplyGear(GameState state, SavedParty party)
        {
            var changes = PlanGear(state, party);
            var equipment = new EquipmentSystem();
            // 先に変わる枠をすべて外してから着せる（外した物をほかのメンバーへ回す入れ替えも、この順なら保管庫を通って通る）
            foreach (var c in changes.Where(c => c.Before != null))
                equipment.TryUnequip(state, c.Adventurer, c.Slot);
            foreach (var c in changes)
                equipment.TryEquip(state, c.Adventurer, c.Slot, c.After);
            return changes;
        }

        // ==================== まとめて売る ====================

        /// <summary>
        /// 誰かが着ければ今の装備より良くなる武具か（現役の全員と比べる。出撃中・派遣中の子も帰ってくるので数える）。
        /// 装飾品は2枠のうち弱い方と比べる。
        /// </summary>
        public static bool IsUsefulToAnyone(GameState state, EquipmentItem item) =>
            state.Adventurers.Where(a => !a.IsRetired && item.IsAllowedFor(a.JobClass))
                .Any(a => SlotsFor(item).Any(slot => GearScore(a, item) > GearScore(a, a.GetEquipped(slot)) + Epsilon));

        /// <summary>まとめて売る武具：束で売れる物（アフィックス無し・銅銀・固有でない、→ EquipmentSystem.IsBulkSellable）のうち、誰の役にも立たない物。</summary>
        public static List<EquipmentItem> UnneededArmory(GameState state) =>
            state.Armory.Where(e => EquipmentSystem.IsBulkSellable(e) && EquipmentSystem.CanSell(e) && e.GetDefinition() != null && !IsUsefulToAnyone(state, e)).ToList();

        /// <summary>
        /// 素材ごとに残しておく数：まだ済んでいない研究に要る数の合計＋受けた依頼で納める数＋霊薬（解禁後）の1回分の最大＋秘薬の触媒（解禁後）の1回分。
        /// </summary>
        public static Dictionary<string, int> MaterialsToKeep(GameState state)
        {
            var keep = new Dictionary<string, int>();
            void Add(string id, int count) => keep[id] = keep.GetValueOrDefault(id) + count;

            foreach (var research in ResearchBalance.GetAll().Where(r => !state.IsResearchCompleted(r.Id)))
                foreach (var (id, count) in research.RequiredMaterials)
                    Add(id, count);
            foreach (var c in state.Commissions.Where(c => c.Accepted && c.MaterialId != null))
                Add(c.MaterialId!, c.Count);
            if (ElixirSystem.IsUnlocked(state))
                foreach (var group in ElixirBalance.Recipes.SelectMany(r => r.RequiredMaterials).GroupBy(kv => kv.Key))
                    Add(group.Key, group.Max(kv => kv.Value));
            if (SoulFusionSystem.IsUnlocked(state))
                foreach (var catalyst in SoulFusionBalance.Catalysts)
                    Add(catalyst.MaterialId, catalyst.Count);
            return keep;
        }

        /// <summary>まとめて売る物の見込み（武具は UnneededArmory、素材は残す数を超えた分）。</summary>
        public static SalePlan PlanSale(GameState state)
        {
            var equipment = UnneededArmory(state);
            var keep = MaterialsToKeep(state);
            var materials = new Dictionary<string, int>();
            int gold = equipment.Sum(EquipmentSystem.GetSellPrice);
            foreach (var (id, stock) in state.Materials.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                int surplus = stock - keep.GetValueOrDefault(id);
                if (surplus <= 0 || MaterialBalance.Find(id) is not { } definition) continue;
                materials[id] = surplus;
                gold += definition.SellPrice * surplus;
            }
            return new SalePlan(equipment, materials, gold);
        }

        /// <summary>見込みを作り直して、まとめて売る。売った物（TotalGold は実際に入った額）を返す。</summary>
        public static SalePlan ExecuteSale(GameState state, EconomySystem economy)
        {
            var plan = PlanSale(state);
            int gold = 0;
            if (plan.Equipment.Count > 0 && EquipmentSystem.TrySellEquipments(state, plan.Equipment.Select(e => e.Id.ToString()), out int fromGear))
                gold += fromGear;
            foreach (var (id, count) in plan.Materials)
                if (economy.TrySellMaterial(state, id, count, out int fromMaterial))
                    gold += fromMaterial;
            return plan with { TotalGold = gold };
        }

        // ==================== おすすめの子で申し込む ====================

        /// <summary>方針が潜行の部隊のメンバー。</summary>
        private static HashSet<Guid> DiveMembers(GameState state) =>
            state.SavedParties.Where(p => p.Order == SquadOrder.Dive).SelectMany(p => p.MemberIds).ToHashSet();

        /// <summary>
        /// 今月の個人の大会へのおすすめの申し込み（月のはじめだけ。部隊戦と交流戦は含めない）。格の高い大会から順に、空いている枠へ
        /// 出られる子を試合の強さの順に入れる。HPが RecommendMinHpRatio 未満の子と、相手の幅の中央×RecommendStrengthRatio に届かない大会（新人戦は除く）には出さない。
        /// 潜行の部隊の子は、G1・特別と、得意な部門の G2 だけに出す（迷宮を止めすぎない）。過ごし方は HP が RecommendPushHpRatio 以上なら追い込み。
        /// </summary>
        public static List<TournamentPick> RecommendEntries(GameState state)
        {
            var picks = new List<TournamentPick>();
            if (!TournamentSystem.CanChangeEntries(state)) return picks;
            var dive = DiveMembers(state);
            var planned = new HashSet<Guid>();
            var events = TournamentSystem.EventsThisMonth(state)
                .Where(e => e.Result == null && e.Kind != TournamentKind.Exchange && e.Discipline != TournamentDiscipline.Party)
                .OrderByDescending(e => TournamentSystem.GradeRank(e.Grade)).ThenBy(e => e.Week).ToList();
            foreach (var ev in events)
            {
                int open = TournamentBalance.EntrantsPerGuild - TournamentSystem.EntriesOf(state, ev).Count;
                if (open <= 0) continue;
                bool bigStage = ev.Grade is TournamentGrade.G1 or TournamentGrade.Special;
                var chosen = state.Adventurers
                    .Where(a => !planned.Contains(a.Id) && TournamentSystem.EntryBlockReason(state, ev, a) == null && HpRatio(a) >= TournamentBalance.RecommendMinHpRatio)
                    .Where(a => !dive.Contains(a.Id) || bigStage || (ev.Kind == TournamentKind.Royal && TournamentSystem.DisciplineFor(ev, a) == TournamentSystem.BestDiscipline(a)))
                    .Select(a => (A: a, D: TournamentSystem.DisciplineFor(ev, a), Str: TournamentSystem.MatchStrength(a, TournamentSystem.DisciplineFor(ev, a))))
                    .Where(x => ev.Kind == TournamentKind.Rookie || x.Str >= MidStrength(ev, x.D) * TournamentBalance.RecommendStrengthRatio)
                    .OrderByDescending(x => x.Str).ThenBy(x => x.A.Name, StringComparer.Ordinal)
                    .Take(open).ToList();
                foreach (var x in chosen)
                {
                    planned.Add(x.A.Id);
                    var prep = HpRatio(x.A) >= TournamentBalance.RecommendPushHpRatio ? TournamentPrep.Push : TournamentPrep.Rest;
                    picks.Add(new TournamentPick(ev, x.A, x.D, prep, TournamentSystem.EstimateWinChance(ev, x.D, x.Str)));
                }
            }
            return picks;
        }

        private static double HpRatio(Adventurer a) => a.MaxHP > 0 ? (double)a.CurrentHP / a.MaxHP : 0;

        private static double MidStrength(TournamentEvent ev, TournamentDiscipline d)
        {
            var (min, max) = TournamentSystem.OpponentRange(ev, d);
            return (min + max) / 2;
        }

        /// <summary>おすすめのとおりに申し込む（見込みを作り直してから）。申し込めた分を返す。</summary>
        public static List<TournamentPick> ApplyEntries(GameState state) =>
            RecommendEntries(state).Where(p => TournamentSystem.TryEnter(state, p.Event, p.Adventurer, p.Prep)).ToList();

        // ==================== 交渉の自動 ====================

        /// <summary>
        /// 交渉の自動（GameState.AutoNegotiationBonus）：契約の不満が出ている子へ、所持金に Reserve を残せるならボーナスを払う
        /// （→ SatisfactionSystem.PayBonus）。週の決算で契約交渉の判定のすぐあとに呼ぶ。払えなかった子は手で応える（今までどおり月が止まる）。
        /// </summary>
        public static List<AutoBonus> ProcessAutoBonus(GameState state, SatisfactionSystem satisfaction)
        {
            var paid = new List<AutoBonus>();
            if (!state.AutoNegotiationBonus) return paid;
            foreach (var a in state.Adventurers.Where(a => a.NeedsNegotiation && !a.IsOnLoan).ToList())
            {
                int bonus = a.WeeklyWage * SatisfactionBalance.BonusWeeksEquivalent;
                if (state.Gold - bonus < Reserve(state)) continue;
                satisfaction.PayBonus(state, a);
                paid.Add(new AutoBonus(a, bonus));
            }
            return paid;
        }
    }
}
