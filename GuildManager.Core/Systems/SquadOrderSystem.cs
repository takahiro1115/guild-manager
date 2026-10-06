using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>方針による自動の行動1件の種類（→ SquadOrderEvent）。</summary>
    public enum SquadOrderAction
    {
        /// <summary>方針どおりに出撃した。</summary>
        Dispatched,
        /// <summary>条件を満たさず、この週は待機した（Detail に理由）。</summary>
        Waiting,
    }

    /// <summary>方針による自動の行動1件（週報用）。</summary>
    public sealed record SquadOrderEvent(SavedParty Party, SquadOrderAction Action, string Detail);

    /// <summary>
    /// 部隊の方針と自動出撃（→ 03 §4.0.3・§0.63、2026年10月新設）。作業感を減らすため、保存した部隊（→ SavedParty）に
    /// 方針（潜行・調査・採取を続ける）を持たせ、週送りの前に Execute を呼ぶと、出撃していない方針つきの部隊を、
    /// 条件（全員が出撃でき、HPが AutoDispatchMinHpPercent 以上）を満たせば方針どおりに出撃させる。
    /// §0.69で出撃はすべて方針から出す形にした（手動の出撃・扉前の指令は撤去）。扉前の判断は、着いた週の決算のうちに
    /// 構え（→ JudgeEngage）で DungeonExpeditionSystem が行う。
    /// 呼び出すのは UI の「次週へ」（決算の前）と自動スキップ（毎週の決算の前、→ AutoSkipService）。乱数は使わない。
    /// </summary>
    public class SquadOrderSystem
    {
        private readonly DungeonExpeditionSystem _expedition;

        public SquadOrderSystem(DungeonExpeditionSystem expedition)
        {
            _expedition = expedition;
        }

        /// <summary>
        /// この部隊の出撃を呼び戻す（§0.69、→ DungeonExpeditionSystem.TryRecall）。方針の解除・変更のときと、
        /// 大迷宮画面の【呼び戻す】で使う。呼び戻せる出撃が無ければ false。
        /// </summary>
        public bool Recall(GameState state, SavedParty saved)
        {
            var mission = state.ActiveDungeonMissions.FirstOrDefault(m => m.SavedPartyId == saved.Id)
                ?? state.ActiveDungeonMissions.FirstOrDefault(m => m.Party.Members.Any(a => saved.MemberIds.Contains(a.Id)));
            return mission != null && _expedition.TryRecall(state, mission);
        }

        public List<SquadOrderEvent> Execute(GameState state)
        {
            var events = new List<SquadOrderEvent>();
            if (state.DefeatReason != null)
                return events;

            // 方針つきの部隊の自動出撃
            foreach (var saved in state.SavedParties.Where(p => p.Order != SquadOrder.None))
            {
                if (IsOut(state, saved))
                    continue;

                string? wait = GetWaitReason(state, saved);
                if (wait != null)
                {
                    events.Add(new SquadOrderEvent(saved, SquadOrderAction.Waiting, wait));
                    continue;
                }

                var party = PartyFormationSystem.BuildDispatchParty(state, saved.MemberIds);
                var field = ResolveField(state, saved)!; // 待機理由が無い＝対象が決まっている（→ GetWaitReason）
                var (ok, detail) = DispatchByOrder(state, saved, field, party);
                if (!ok)
                {
                    events.Add(new SquadOrderEvent(saved, SquadOrderAction.Waiting, detail));
                    continue;
                }
                state.ActiveDungeonMissions[^1].SavedPartyId = saved.Id;
                events.Add(new SquadOrderEvent(saved, SquadOrderAction.Dispatched, detail));
            }

            return events;
        }

        private (bool Ok, string Detail) DispatchByOrder(GameState state, SavedParty saved, DungeonField field, Party party)
        {
            var boss = field.GetNextActiveBoss();
            switch (saved.Order)
            {
                case SquadOrder.Dive:
                    if (boss == null) return (false, $"{field.Name}は制覇済みで、潜る先のボスがいない");
                    return _expedition.TryDispatch(state, party, boss, DungeonMissionType.Scouting)
                        ? (true, $"{field.Name}の第1層から潜行を始める（目標：第{boss.Floor}層の扉前）")
                        : (false, "出撃できなかった");
                case SquadOrder.Survey:
                    // 解析が完全でない最も浅いボスを調べる（潜行の済んだ階層のみ。§0.67）。
                    // 調べるボスがいない（すべて完全解析済み・まだ潜行が進んでいない）なら、その週は同じフィールドで採取して稼ぐ。
                    if (ScoutingResolver.FindSurveyableBoss(field) is { } surveyBoss)
                        return _expedition.TryDispatchSurvey(state, party, surveyBoss)
                            ? (true, $"{field.Name}の{ScoutingResolver.SegmentLabel(field, surveyBoss)}の区間の迷宮調査へ出発する")
                            : (false, "出撃できなかった");
                    return _expedition.TryDispatchGathering(state, party, field)
                        ? (true, ScoutingResolver.FindSurveyTarget(field) == null
                            ? $"調べるボスが完全解析済みのため、{field.Name}で採取する"
                            : $"調べる階層まで潜行が進んでいないため、{field.Name}で採取する")
                        : (false, "出撃できなかった");
                case SquadOrder.Gather:
                    return _expedition.TryDispatchGathering(state, party, field)
                        ? (true, $"{field.Name}へ採取に出発する")
                        : (false, "出撃できなかった");
                default:
                    return (false, "方針がない");
            }
        }

        /// <summary>
        /// 方針の対象ダンジョン。具体的なフィールドIdならそれ、「おまかせ」（<see cref="SavedParty.AutoFieldId"/>）なら方針に合うものを選ぶ。
        /// 選べるフィールドが無ければ null。乱数は使わない。
        ///  - 潜行：次のボスに対する討伐火力÷要求火力が最も高いダンジョン（勝てそうなところから）
        ///  - 調査：今調べられるボス（解析が完全でない最も浅いボスで、潜行の済んだ階層。→ ScoutingResolver.FindSurveyableBoss）のうち、解析率が最も低いダンジョン（無ければ最も深いダンジョン＝その週は採取）
        ///  - 採取：護衛の段階が最も良く（同じなら）最高到達階層が深いダンジョン
        /// 同点は、フィールドの並び順が先のもの。
        /// </summary>
        public static DungeonField? ResolveField(GameState state, SavedParty saved)
        {
            if (saved.OrderFieldId != SavedParty.AutoFieldId)
                return state.DungeonFields.FirstOrDefault(f => f.Id == saved.OrderFieldId);

            var unlocked = state.DungeonFields.Where(f => f.IsUnlocked).OrderBy(f => f.Order).ToList();
            if (unlocked.Count == 0) return null;

            var party = PartyFormationSystem.BuildDispatchParty(state, saved.MemberIds);
            if (party.IsEmpty)
            {
                // 出撃できる人がいない間は、表示のために最も深いダンジョンを仮の対象にする（実際の出撃は待機理由で止まる）
                return unlocked.OrderByDescending(f => f.ReachedFloor).First();
            }

            switch (saved.Order)
            {
                case SquadOrder.Dive:
                {
                    var candidates = unlocked.Select(f => (Field: f, Boss: f.GetNextActiveBoss())).Where(x => x.Boss != null).ToList();
                    if (candidates.Count == 0) return null;
                    return candidates
                        .OrderByDescending(x => DungeonResolver.CalculateBossPower(party, x.Boss!) / Math.Max(1.0, DungeonResolver.RequiredPower(x.Boss!, state, party)))
                        .First().Field;
                }
                case SquadOrder.Survey:
                {
                    var open = unlocked.Select(f => (Field: f, Boss: ScoutingResolver.FindSurveyableBoss(f)))
                        .Where(x => x.Boss != null)
                        .OrderBy(x => x.Boss!.IntelRate)
                        .ToList();
                    return open.Count > 0 ? open[0].Field : unlocked.OrderByDescending(f => f.ReachedFloor).First();
                }
                default:
                    return unlocked
                        .OrderByDescending(f => GuardRank(GatheringResolver.PreviewGuardTier(party, f)))
                        .ThenByDescending(f => f.ReachedFloor)
                        .First();
            }
        }

        private static int GuardRank(GuardTier tier) => tier switch
        {
            GuardTier.Abundant => 3,
            GuardTier.Sufficient => 2,
            GuardTier.Marginal => 1,
            _ => 0,
        };

        /// <summary>その部隊が出撃中か：方針で出た出撃が残っているか、メンバーの誰かが出撃中。</summary>
        public static bool IsOut(GameState state, SavedParty saved) =>
            state.ActiveDungeonMissions.Any(m => m.SavedPartyId == saved.Id)
            || saved.MemberIds.Any(id => state.Adventurers.FirstOrDefault(a => a.Id == id)?.IsDispatched == true);

        /// <summary>
        /// 自動出撃できない理由（出撃できるなら null）：方針の対象フィールドが無い・未開放、同時出撃枠が埋まっている、
        /// ロースターにいるメンバーがいない、重傷者・毒状態の者がいる、HPが AutoDispatchMinHpPercent 未満の者がいる。
        /// 出撃中の部隊はこの判定の対象外（→ IsOut）。
        /// </summary>
        public static string? GetWaitReason(GameState state, SavedParty saved)
        {
            var field = ResolveField(state, saved);
            if (field == null) return saved.OrderFieldId == SavedParty.AutoFieldId ? "おまかせで選べるダンジョンがない" : "方針の対象フィールドが見つからない";
            if (!field.IsUnlocked) return $"{field.Name}がまだ開放されていない";
            var members = saved.MemberIds.Select(id => state.Adventurers.FirstOrDefault(a => a.Id == id)).OfType<Adventurer>().ToList();
            if (members.Count == 0) return "部隊にメンバーがいない";
            // 訓練中の冒険者はその月は出撃せず、ほかの隊員だけで出る（§0.70）。全員が訓練中なら待機。
            members = members.Where(m => !TrainingSystem.IsTraining(state, m.Id)).ToList();
            if (members.Count == 0) return "全員が訓練中（今月は出撃しない）";
            var severe = members.FirstOrDefault(m => m.Injury == InjurySeverity.Severe);
            if (severe != null) return $"{severe.Name}が重傷のため待機";
            var poisoned = members.Where(m => m.IsPoisoned).ToList();
            if (poisoned.Count > 0) return $"{string.Join("・", poisoned.Select(m => m.Name))}が毒状態のため静養";
            var tired = members.Where(m => m.CurrentHP * 100 < m.MaxHP * SquadOrderBalance.AutoDispatchMinHpPercent).ToList();
            if (tired.Count > 0) return $"{string.Join("・", tired.Select(m => m.Name))}のHPが{SquadOrderBalance.AutoDispatchMinHpPercent}%未満のため静養";
            if (!DungeonExpeditionSystem.CanDispatch(state)) return "同時出撃枠が埋まっている";
            return null;
        }

        /// <summary>出撃が方針で出た部隊のものなら、その部隊（手動の出撃・部隊が消えていれば null）。</summary>
        public static SavedParty? FindOrderedParty(GameState state, ActiveDungeonMission mission) =>
            mission.SavedPartyId == null ? null : state.SavedParties.FirstOrDefault(p => p.Id == mission.SavedPartyId);

        /// <summary>
        /// 扉前で挑むかの見込み（→ 大迷宮画面の見立てと同じ考え方、§0.68）。構えの条件（→ SquadOrderBalance.For）を
        /// 次の順に確かめ、すべて満たせば挑む：
        ///  - すべてのギミックの備え（→ DungeonResolver.Readiness）が MinReadiness 以上、即死級は MinInstantKillReadiness 以上
        ///  - 討伐火力（ギミック込み、→ DungeonResolver.CalculateBossPower）が要求火力（再生・異変込み）×PowerMargin 以上
        ///  - 全員のHPが MinHpPercent 以上
        /// 見込みがあれば Go、無ければ撤退の理由を返す。扉前に着いた週の決算で DungeonExpeditionSystem が呼ぶ（§0.69）。
        /// </summary>
        public static (bool Go, string? Reason) JudgeEngage(GameState state, ActiveDungeonMission mission, DoorStance stance)
        {
            var boss = mission.TargetedBoss;
            if (boss == null || boss.IsDefeated) return (false, "挑むボスがいない");
            var rule = SquadOrderBalance.For(stance);

            var party = mission.Party;
            foreach (var gimmick in boss.Gimmicks)
            {
                double readiness = DungeonResolver.Readiness(gimmick, party.Members);
                double needed = gimmick.Type == BossGimmickType.InstantKill
                    ? Math.Max(rule.MinReadiness, rule.MinInstantKillReadiness)
                    : rule.MinReadiness;
                if (readiness < needed)
                    return (false, $"{BossGimmickInfo.Label(gimmick.Type)}への備えが足りないため撤退");
            }

            double power = DungeonResolver.CalculateBossPower(party, boss);
            double required = DungeonResolver.RequiredPower(boss, state, party); // 再生・迷宮の異変（主の衰え・猛り、§0.64）込み
            if (power < required * rule.PowerMargin)
                return (false, "討伐火力が足りない見込みのため撤退");

            var tired = party.Members.Where(m => m.CurrentHP * 100 < m.MaxHP * rule.MinHpPercent).ToList();
            if (tired.Count > 0)
                return (false, $"{string.Join("・", tired.Select(m => m.Name))}のHPが{rule.MinHpPercent}%未満のため撤退");

            return (true, null);
        }

        /// <summary>扉前の構えの表示名。</summary>
        public static string StanceLabel(DoorStance stance) => stance switch
        {
            DoorStance.Cautious => "慎重",
            DoorStance.Standard => "標準",
            _ => "強気",
        };
    }
}
