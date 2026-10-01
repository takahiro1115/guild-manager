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
        /// <summary>扉前で見込みがあり、ボスに挑んだ（決戦は次の決算）。</summary>
        Engaged,
        /// <summary>扉前で見込みが無く、撤退・帰還した（Detail に理由）。</summary>
        Retreated,
    }

    /// <summary>方針による自動の行動1件（週報用）。Retreat には帰還の内訳（Resolution）が付く。</summary>
    public sealed record SquadOrderEvent(SavedParty Party, SquadOrderAction Action, string Detail, DungeonMissionResolution? Resolution = null);

    /// <summary>
    /// 部隊の方針と自動出撃（→ 03 §4.0.3・§0.63、2026年10月新設）。作業感を減らすため、保存した部隊（→ SavedParty）に
    /// 方針（潜行・調査・採取を続ける）を持たせ、週送りの前に Execute を呼ぶと：
    ///  1. 扉前で判断待ちの、「見込みがあれば挑む」（AutoEngage）を付けた方針の部隊を、見込みで挑ませるか撤退させる（→ JudgeEngage）
    ///  2. 出撃していない方針つきの部隊を、条件（全員が出撃でき、HPが AutoDispatchMinHpPercent 以上）を満たせば方針どおりに出撃させる
    /// 呼び出すのは UI の「次週へ」（決算の前）と自動スキップ（毎週の決算の前、→ AutoSkipService）。乱数は使わない。
    /// </summary>
    public class SquadOrderSystem
    {
        private readonly DungeonExpeditionSystem _expedition;

        public SquadOrderSystem(DungeonExpeditionSystem expedition)
        {
            _expedition = expedition;
        }

        public List<SquadOrderEvent> Execute(GameState state)
        {
            var events = new List<SquadOrderEvent>();
            if (state.DefeatReason != null)
                return events;

            // 1. 扉前の自動判断
            foreach (var mission in state.ActiveDungeonMissions.Where(m => m.Status == ExpeditionStatus.AwaitingBossDecision).ToList())
            {
                var saved = FindOrderedParty(state, mission);
                if (saved == null || saved.Order != SquadOrder.Dive || !saved.AutoEngage)
                    continue;

                var judge = JudgeEngage(state, mission);
                if (judge.Go && _expedition.TryEngageBoss(state, mission, judge.Items))
                {
                    events.Add(new SquadOrderEvent(saved, SquadOrderAction.Engaged,
                        $"第{mission.TargetedBoss!.Floor}層「{mission.TargetedBoss.Name}」に挑む" +
                        (judge.Items.Count > 0 ? $"（携行品：{string.Join("・", judge.Items.Select(id => ConsumableCatalog.FindById(id)?.Name ?? id))}）" : "")));
                }
                else
                {
                    var resolution = _expedition.TryRetreat(state, mission);
                    events.Add(new SquadOrderEvent(saved, SquadOrderAction.Retreated, judge.Reason ?? "所持金が携行品の代金に足りない", resolution));
                }
            }

            // 2. 方針つきの部隊の自動出撃
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

                var field = state.DungeonFields.First(f => f.Id == saved.OrderFieldId);
                var party = PartyFormationSystem.BuildDispatchParty(state, saved.MemberIds);
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
                    // 完全解析済み（またはボスがいない）なら、その週は同じフィールドで採取して稼ぐ。
                    if (boss != null && ScoutingResolver.GetTier(boss.IntelRate) != IntelTier.Complete)
                        return _expedition.TryDispatchSurvey(state, party, boss)
                            ? (true, $"{field.Name}第{boss.Floor}層「{boss.Name}」の迷宮調査へ出発する")
                            : (false, "出撃できなかった");
                    return _expedition.TryDispatchGathering(state, party, field)
                        ? (true, $"調べるボスが完全解析済みのため、{field.Name}で採取する")
                        : (false, "出撃できなかった");
                case SquadOrder.Gather:
                    return _expedition.TryDispatchGathering(state, party, field)
                        ? (true, $"{field.Name}へ採取に出発する")
                        : (false, "出撃できなかった");
                default:
                    return (false, "方針がない");
            }
        }

        /// <summary>その部隊が出撃中か：方針で出た出撃が残っているか、メンバーの誰かが出撃中。</summary>
        public static bool IsOut(GameState state, SavedParty saved) =>
            state.ActiveDungeonMissions.Any(m => m.SavedPartyId == saved.Id)
            || saved.MemberIds.Any(id => state.Adventurers.FirstOrDefault(a => a.Id == id)?.IsDispatched == true);

        /// <summary>
        /// 自動出撃できない理由（出撃できるなら null）：方針の対象フィールドが無い・未開放、同時出撃枠が埋まっている、
        /// ロースターにいるメンバーがいない、重傷者がいる、HPが AutoDispatchMinHpPercent 未満の者がいる。
        /// 出撃中の部隊はこの判定の対象外（→ IsOut）。
        /// </summary>
        public static string? GetWaitReason(GameState state, SavedParty saved)
        {
            var field = state.DungeonFields.FirstOrDefault(f => f.Id == saved.OrderFieldId);
            if (field == null) return "方針の対象フィールドが見つからない";
            if (!field.IsUnlocked) return $"{field.Name}がまだ開放されていない";
            var members = saved.MemberIds.Select(id => state.Adventurers.FirstOrDefault(a => a.Id == id)).OfType<Adventurer>().ToList();
            if (members.Count == 0) return "部隊にメンバーがいない";
            var severe = members.FirstOrDefault(m => m.Injury == InjurySeverity.Severe);
            if (severe != null) return $"{severe.Name}が重傷のため待機";
            var tired = members.Where(m => m.CurrentHP * 100 < m.MaxHP * SquadOrderBalance.AutoDispatchMinHpPercent).ToList();
            if (tired.Count > 0) return $"{string.Join("・", tired.Select(m => m.Name))}のHPが{SquadOrderBalance.AutoDispatchMinHpPercent}%未満のため静養";
            if (!DungeonExpeditionSystem.CanDispatch(state)) return "同時出撃枠が埋まっている";
            return null;
        }

        /// <summary>出撃が方針で出た部隊のものなら、その部隊（手動の出撃・部隊が消えていれば null）。</summary>
        public static SavedParty? FindOrderedParty(GameState state, ActiveDungeonMission mission) =>
            mission.SavedPartyId == null ? null : state.SavedParties.FirstOrDefault(p => p.Id == mission.SavedPartyId);

        /// <summary>
        /// 扉前に着いたこの部隊を、プレイヤーに聞かずに方針で判断するか（＝潜行の方針で AutoEngage）。
        /// 週次決算は、これが true の部隊の扉前到達では自動スキップを止めない（→ WeekProcessingSystem）。
        /// </summary>
        public static bool DecidesAtDoor(GameState state, Party party)
        {
            var mission = state.ActiveDungeonMissions.FirstOrDefault(m => m.Party == party);
            var saved = mission == null ? null : FindOrderedParty(state, mission);
            return saved is { Order: SquadOrder.Dive, AutoEngage: true };
        }

        /// <summary>
        /// 扉前で挑むかの見込み（→ 大迷宮画面の見立てと同じ考え方）：
        ///  - ギミックをすべて対策できる（部隊の職業・能力・装備で足りなければ、対策の携行品を最大2つまで積む）
        ///  - 討伐火力（携行品込み、→ DungeonResolver.CalculateBossPower）が要求火力×AutoEngagePowerMargin 以上
        ///  - 全員のHPが AutoEngageMinHpPercent 以上
        /// 見込みがあれば Go と積む携行品、無ければ理由を返す。所持金の確認は TryEngageBoss に任せる。
        /// </summary>
        public static (bool Go, List<string> Items, string? Reason) JudgeEngage(GameState state, ActiveDungeonMission mission)
        {
            var boss = mission.TargetedBoss;
            if (boss == null || boss.IsDefeated) return (false, new List<string>(), "挑むボスがいない");

            var trial = new Party();
            foreach (var m in mission.Party.Members) trial.TryAdd(m);
            var items = new List<string>();
            foreach (var gimmick in boss.Gimmicks.Where(g => !DungeonResolver.IsCountered(g, trial)))
            {
                if (gimmick.RequiredItemId == null || !trial.TryAddConsumable(gimmick.RequiredItemId))
                    return (false, items, $"対策できないギミック（{BossGimmickLabel(gimmick.Type)}）があるため撤退");
                items.Add(gimmick.RequiredItemId);
            }

            double power = DungeonResolver.CalculateBossPower(trial, boss);
            double required = DungeonResolver.RequiredPower(boss);
            if (power < required * SquadOrderBalance.AutoEngagePowerMargin)
                return (false, items, $"討伐火力が足りない見込み（{power:F0}／要求{required:F0}）のため撤退");

            var tired = mission.Party.Members.Where(m => m.CurrentHP * 100 < m.MaxHP * SquadOrderBalance.AutoEngageMinHpPercent).ToList();
            if (tired.Count > 0)
                return (false, items, $"{string.Join("・", tired.Select(m => m.Name))}のHPが{SquadOrderBalance.AutoEngageMinHpPercent}%未満のため撤退");

            return (true, items, null);
        }

        private static string BossGimmickLabel(BossGimmickType type) => type switch
        {
            BossGimmickType.Poison => "猛毒",
            BossGimmickType.HeavyArmor => "重装甲",
            BossGimmickType.Flying => "飛行",
            BossGimmickType.InstantKill => "即死級",
            _ => type.ToString(),
        };
    }
}
