using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 大迷宮（ダンジョン）への出撃の派遣・週次解決オーケストレーション
    /// （→ ScoutingResolver・DungeonResolver をゲーム進行へ接続する役）。
    ///
    /// QuestDispatchSystem と同じ流れに揃えている：
    ///  - 出撃操作（TryDispatch）では解決せず、メンバーを IsDispatched=true にして待機させる。
    ///  - 週次決算（ProcessWeeklyMissions）で全件をまとめて解決し、メンバーを帰還させる。
    ///  - 同時出撃枠は通常クエストの派遣と共有する（→ QuestDispatchSystem.CanDispatch）。
    ///
    /// 調査任務は低リスク（HP下限1）だが、ボス討伐でHPが0になった冒険者は強制除籍
    /// （恒久ロスト）となり、戦死と同じ手順でロースターから外す（→ 03 §4.3.1）。
    /// </summary>
    public class DungeonExpeditionSystem
    {
        private readonly ScoutingResolver _scoutingResolver;
        private readonly DungeonResolver _dungeonResolver;
        private readonly SatisfactionSystem _satisfactionSystem;
        private readonly CompatibilitySystem _compatibilitySystem;

        public DungeonExpeditionSystem(
            ScoutingResolver scoutingResolver,
            DungeonResolver dungeonResolver,
            SatisfactionSystem satisfactionSystem,
            CompatibilitySystem compatibilitySystem)
        {
            _scoutingResolver = scoutingResolver;
            _dungeonResolver = dungeonResolver;
            _satisfactionSystem = satisfactionSystem;
            _compatibilitySystem = compatibilitySystem;
        }

        /// <summary>
        /// 大迷宮へ出撃させる。以下のいずれかに該当すれば何もせずfalseを返す（Try*系の共通パターン）：
        /// 同時出撃枠が埋まっている／部隊が空／ボスがGameState上に存在しない・撃破済み／
        /// 出撃不可（重傷・派遣中等）のメンバーが含まれている。
        /// </summary>
        public bool TryDispatch(GameState state, Party party, FloorBoss boss, DungeonMissionType missionType)
        {
            if (!QuestDispatchSystem.CanDispatch(state))
                return false;
            if (party.IsEmpty || party.Members.Any(m => !m.IsAvailable))
                return false;
            if (!state.FloorBosses.Contains(boss) || boss.IsDefeated)
                return false;

            state.ActiveDungeonMissions.Add(new ActiveDungeonMission
            {
                Party = party,
                Boss = boss,
                MissionType = missionType,
            });

            foreach (var member in party.Members)
                member.IsDispatched = true;

            return true;
        }

        /// <summary>
        /// 出撃予定を取り消す（週次決算の前であれば、判定は行われていないため何も失わない）。
        /// 対象が存在しなければfalse。
        /// </summary>
        public bool TryCancel(GameState state, ActiveDungeonMission mission)
        {
            if (!state.ActiveDungeonMissions.Remove(mission))
                return false;

            foreach (var member in mission.Party.Members)
                member.IsDispatched = false;

            return true;
        }

        /// <summary>
        /// 週次決算処理。出撃中の全件を解決し、メンバーを帰還させる。
        /// 解決結果の一覧を返す（UI側の週報表示用。出撃が無ければ空リスト）。
        /// </summary>
        public List<DungeonMissionResolution> ProcessWeeklyMissions(GameState state)
        {
            var resolutions = new List<DungeonMissionResolution>();

            foreach (var mission in state.ActiveDungeonMissions.ToList())
            {
                state.ActiveDungeonMissions.Remove(mission);

                // 同じボスへ複数部隊を向けた場合、先に解決した部隊が撃破していれば後続は空振りになる。
                // 判定を行わず、そのまま帰還させる（損害も成果も無し）。
                if (mission.Boss.IsDefeated)
                {
                    ReleaseMembers(mission.Party);
                    continue;
                }

                // 累計出撃回数（→ GameState.TotalDispatchCount）。取り消しで水増しされないよう、
                // 出撃操作の時点ではなく実際に解決した時点で数える。
                state.TotalDispatchCount++;

                double intelBefore = mission.Boss.IntelRate;

                if (mission.MissionType == DungeonMissionType.Scouting)
                {
                    var scouting = _scoutingResolver.Resolve(mission.Party, mission.Boss);
                    resolutions.Add(new DungeonMissionResolution(mission.Party, mission.Boss, intelBefore, scouting));
                }
                else
                {
                    var assault = _dungeonResolver.Resolve(mission.Party, mission.Boss);
                    ApplyForcedRetirements(state, mission.Party, assault.ForceRetiredAdventurerIds);
                    resolutions.Add(new DungeonMissionResolution(mission.Party, mission.Boss, intelBefore, assault));
                }

                ReleaseMembers(mission.Party);
            }

            return resolutions;
        }

        /// <summary>
        /// 強制除籍の処理（→ DungeonResult.ForceRetiredAdventurerIds）。システム上は戦死と同じ
        /// 恒久ロストのため、QuestDispatchSystem.ProcessWeeklyDispatches の戦死処理と同じ手順
        /// （仲間ロストの満足度低下・相性の余波・ロースターから記録への移動）を踏む。
        /// </summary>
        private void ApplyForcedRetirements(GameState state, Party party, IEnumerable<Guid> retiredIds)
        {
            foreach (var retiredId in retiredIds)
            {
                _satisfactionSystem.ApplyPartyLossPenalty(party, retiredId);
                _compatibilitySystem.ApplyDeathAftermath(state, party, retiredId);

                var retired = party.Members.FirstOrDefault(m => m.Id == retiredId);
                if (retired == null)
                    continue;

                retired.FellAtWeek = state.WeekNumber;
                state.TrainingAssignments.Remove(retired.Id);
                state.Adventurers.Remove(retired);
                state.FallenAdventurers.Add(retired);
            }
        }

        private static void ReleaseMembers(Party party)
        {
            foreach (var member in party.Members)
                member.IsDispatched = false;
        }
    }
}
