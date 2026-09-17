using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

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
        // 各resolverを省略した場合の既定シード（固定シードで再現性を保つ。→ MainDashboardの方針と同じ）。
        private const int DefaultTraversalSeed = 1719;
        private const int DefaultGatheringSeed = 1848;

        private readonly ScoutingResolver _scoutingResolver;
        private readonly DungeonResolver _dungeonResolver;
        private readonly DungeonTraversalResolver _traversalResolver;
        private readonly GatheringResolver _gatheringResolver;
        private readonly SatisfactionSystem _satisfactionSystem;
        private readonly CompatibilitySystem _compatibilitySystem;

        public DungeonExpeditionSystem(
            ScoutingResolver scoutingResolver,
            DungeonResolver dungeonResolver,
            SatisfactionSystem satisfactionSystem,
            CompatibilitySystem compatibilitySystem,
            // 省略可能：道中進軍・探索（採取）の解決（→ DungeonTraversalResolver・GatheringResolver）。
            // 既存の呼び出し側を変更せずに接続できるよう、既定値を持たせている。
            DungeonTraversalResolver? traversalResolver = null,
            GatheringResolver? gatheringResolver = null)
        {
            _scoutingResolver = scoutingResolver;
            _dungeonResolver = dungeonResolver;
            _traversalResolver = traversalResolver ?? new DungeonTraversalResolver(new SeededRng(DefaultTraversalSeed));
            _gatheringResolver = gatheringResolver ?? new GatheringResolver(new SeededRng(DefaultGatheringSeed));
            _satisfactionSystem = satisfactionSystem;
            _compatibilitySystem = compatibilitySystem;
        }

        /// <summary>
        /// 大迷宮へ出撃させる。以下のいずれかに該当すれば何もせずfalseを返す（Try*系の共通パターン）：
        /// 同時出撃枠が埋まっている／部隊が空／ボスがGameState上に存在しない・撃破済み／
        /// ボスの所属フィールドが未開放／出撃不可（重傷・派遣中等）のメンバーが含まれている。
        ///
        /// フィールド未開放のチェック（→ 大迷宮フィールド選択UI仕様）はCore層で行う：
        /// UI（DungeonPanel）は未開放フィールドを選択できないようにしているが、それはUI側の
        /// 制約に過ぎない。出撃の可否そのものはCore層で自己完結して判定すべきという方針
        /// （→ QuestDispatchSystem.CanDispatch等、既存のTry*系メソッドと同じ考え方）により、
        /// ここでも独立して検査する。
        /// </summary>
        public bool TryDispatch(GameState state, Party party, FloorBoss boss, DungeonMissionType missionType)
        {
            if (!QuestDispatchSystem.CanDispatch(state))
                return false;
            if (party.IsEmpty || party.Members.Any(m => !m.IsAvailable))
                return false;

            var field = state.DungeonFields.FirstOrDefault(f => f.Bosses.Contains(boss));
            if (field == null || !field.IsUnlocked || boss.IsDefeated)
                return false;

            state.ActiveDungeonMissions.Add(new ActiveDungeonMission
            {
                Party = party,
                Field = field,
                Boss = boss,
                MissionType = missionType,
            });

            foreach (var member in party.Members)
                member.IsDispatched = true;

            return true;
        }

        /// <summary>
        /// 大迷宮へ探索（採取）に出撃させる。特定のボスではなくフィールドそのものを対象にする点が
        /// TryDispatchとの違い（→ GatheringResolver）。それ以外の失敗条件（枠・部隊・開放状態）は
        /// TryDispatchと同じ。
        /// </summary>
        public bool TryDispatchGathering(GameState state, Party party, DungeonField field)
        {
            if (!QuestDispatchSystem.CanDispatch(state))
                return false;
            if (party.IsEmpty || party.Members.Any(m => !m.IsAvailable))
                return false;
            if (!state.DungeonFields.Contains(field) || !field.IsUnlocked)
                return false;

            state.ActiveDungeonMissions.Add(new ActiveDungeonMission
            {
                Party = party,
                Field = field,
                Boss = null,
                MissionType = DungeonMissionType.Gathering,
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

                if (mission.MissionType == DungeonMissionType.Gathering)
                {
                    // 探索（採取）：特定のボスを対象にしないため、複数部隊が同じフィールドへ
                    // 出ていても「先に解決した部隊が空振りになる」レース条件はそもそも存在しない
                    // （毎回必ず成果が出る）。
                    state.TotalDispatchCount++;

                    var gathering = _gatheringResolver.Resolve(mission.Party, mission.Field, state);
                    state.AddMaterial(gathering.MaterialId, gathering.MaterialCount);
                    state.Gold += gathering.GoldEarned;

                    resolutions.Add(new DungeonMissionResolution(mission.Party, mission.Field, gathering));
                    ReleaseMembers(mission.Party);
                    continue;
                }

                // 調査・討伐はボスを対象にする。TryDispatch/TryDispatchGathering側の構築規約により
                // ここでは必ず非nullのはずだが、防御的にnullなら（構築不整合として）何もせず帰還させる。
                var boss = mission.Boss;
                if (boss == null)
                {
                    ReleaseMembers(mission.Party);
                    continue;
                }

                // 同じボスへ複数部隊を向けた場合、先に解決した部隊が撃破していれば後続は空振りになる。
                // 判定を行わず、そのまま帰還させる（損害も成果も無し）。
                if (boss.IsDefeated)
                {
                    ReleaseMembers(mission.Party);
                    continue;
                }

                // 累計出撃回数（→ GameState.TotalDispatchCount）。取り消しで水増しされないよう、
                // 出撃操作の時点ではなく実際に解決した時点で数える。
                state.TotalDispatchCount++;

                double intelBefore = boss.IntelRate;

                if (mission.MissionType == DungeonMissionType.Scouting)
                {
                    if (mission.Field.ReachedFloor < boss.Floor)
                    {
                        // 分岐A：道中進軍。まだボス階層に到達していない＝素通りで一気に進める。
                        var traversal = _traversalResolver.Resolve(mission.Party, mission.Field, boss, state);
                        resolutions.Add(new DungeonMissionResolution(mission.Party, boss, mission.Field, intelBefore, traversal));
                    }
                    else
                    {
                        // 分岐B：ボス解析。既にボス階層に到達しているため、従来どおりIntelRateを上げる。
                        var scouting = _scoutingResolver.Resolve(mission.Party, boss, state);
                        resolutions.Add(new DungeonMissionResolution(mission.Party, boss, mission.Field, intelBefore, scouting));
                    }
                }
                else
                {
                    var assault = _dungeonResolver.Resolve(mission.Party, boss);
                    ApplyForcedRetirements(state, mission.Party, assault.ForceRetiredAdventurerIds);

                    // 撃破成功時のみ、フィールドの進行（最高到達階層・次フィールド開放・報酬・
                    // 節目ボスでの出撃枠拡張）を適用する（→ 大迷宮5フィールド拡張仕様）。
                    // 撤退（Retreat）時は何も進行しない。
                    int? squadSlotsExpandedTo = null;
                    if (assault.Outcome == DungeonOutcome.Victory)
                    {
                        int slotsBefore = state.UnlockedSquadSlots;
                        ApplyFieldProgression(state, boss);
                        if (state.UnlockedSquadSlots > slotsBefore)
                            squadSlotsExpandedTo = state.UnlockedSquadSlots;
                    }

                    resolutions.Add(new DungeonMissionResolution(
                        mission.Party, boss, mission.Field, intelBefore, assault, squadSlotsExpandedTo));
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

        /// <summary>
        /// ボス撃破に伴うフィールド進行（→ 大迷宮5フィールド拡張仕様）。ProcessWeeklyMissionsが
        /// 撃破（Victory）時にのみ呼ぶ。public static にしてあるのは、フィールド単体では完結しない
        /// （他フィールドの状態を横断して見る必要がある）ロジックをテストから直接検証できるようにするため
        /// （→ DungeonResolver.IsCounteredと同じ考え方）。
        ///
        /// 順序：①撃破報酬の付与 → ②最高到達階層の更新 → ③次フィールドの開放判定
        /// （10Fボス撃破→Order+1を開放。ただし開放先はOrder 2〜4に限る＝深淵はこの経路では開かない）
        /// → ④深淵（Order 5）の開放判定（Order 1〜4すべてで20Fボスが撃破済みになった時点） →
        /// ⑤最終フィールド（Orderが最大＝深淵）の100Fボス撃破でFinalQuestUnlockedを立てる →
        /// ⑥森（Order=1）の節目ボス撃破による出撃枠拡張（「古代エルフ通信技術の復元」）。
        /// </summary>
        public static void ApplyFieldProgression(GameState state, FloorBoss defeatedBoss)
        {
            var field = state.DungeonFields.FirstOrDefault(f => f.Bosses.Contains(defeatedBoss));
            if (field == null)
                return; // フィールドに属さないボス（旧セーブ・テスト等）は対象外。防御的に何もしない。

            // ①撃破報酬（→ FloorBoss.RewardGold/RewardReputation）。
            state.Gold += defeatedBoss.RewardGold;
            state.Reputation += defeatedBoss.RewardReputation;

            // ②最高到達階層の更新（現在値と「撃破階層+1」の大きい方、上限MaxFloor）。
            field.ReachedFloor = Math.Min(DungeonField.MaxFloor, Math.Max(field.ReachedFloor, defeatedBoss.Floor + 1));

            // ③通常開放：10Fボス撃破で次順（Order+1）のフィールドを開放する。
            // 開放先はOrder 2〜4のみ（Order 4の10F撃破でOrder 5=深淵が開いてしまわないようガードする。
            // 深淵は④の特別条件でのみ開放される）。
            if (defeatedBoss.Floor == 10 && field.Order + 1 <= 4)
            {
                var next = state.DungeonFields.FirstOrDefault(f => f.Order == field.Order + 1);
                if (next != null)
                    next.IsUnlocked = true;
            }

            // ④深淵開放：Order 1〜4の全フィールドで20Fボスが撃破済みになった時点で、
            // 第5フィールド（深淵）を開放する。
            bool allShallowFieldsClearedFloor20 = state.DungeonFields
                .Where(f => f.Order is >= 1 and <= 4)
                .All(f => f.Bosses.Any(b => b.Floor == 20 && b.IsDefeated));
            if (allShallowFieldsClearedFloor20)
            {
                var abyss = state.DungeonFields.FirstOrDefault(f => f.Order == 5);
                if (abyss != null)
                    abyss.IsUnlocked = true;
            }

            // ⑤最深部（最終フィールドの100Fボス）撃破：最終討伐クエストの解禁フラグを立てる
            // （→ 03 §8.2。Aランク到達時と同じフラグを共有する＝どちらも「終盤コンテンツが
            // 解禁された」ことを示す合図として扱う）。
            var finalField = state.DungeonFields.OrderByDescending(f => f.Order).FirstOrDefault();
            if (finalField != null && field == finalField && defeatedBoss.Floor == DungeonField.MaxFloor)
                state.FinalQuestUnlocked = true;

            // ⑥出撃枠拡張（「古代エルフの多頭通信術式」復元、→ 大迷宮ボス間隔・敗北条件改訂）。
            // 森（Order=1）の節目ボス撃破のみが対象：10F撃破で2枠・Rank E相当、
            // 20F撃破で3枠・Rank D相当まで引き上げる（既に到達値以上ならダウングレードしない）。
            if (field.Order == 1)
            {
                if (defeatedBoss.Floor == 10)
                    SyncSquadSlotsAndRank(state, targetSlots: 2, targetRank: GuildRank.E);
                else if (defeatedBoss.Floor == 20)
                    SyncSquadSlotsAndRank(state, targetSlots: 3, targetRank: GuildRank.D);
            }
        }

        /// <summary>
        /// 出撃枠とギルド格付け・名声を目標値まで引き上げる（下げない）。
        /// GuildProgressionSystem.ApplyPromotionIfExamClearedと同じパターン：ランクは
        /// 「名声から導出される」値であり、週次決算の降格判定が毎週走るため、ランクだけを
        /// 書き換えると次の決算で名声不足と判定され引き戻されてしまう。名声自体を目標ランクの
        /// 昇格ラインまで引き上げることで、単一の真実（名声→ランク）を保ったまま昇格を成立させる。
        /// </summary>
        private static void SyncSquadSlotsAndRank(GameState state, int targetSlots, GuildRank targetRank)
        {
            state.UnlockedSquadSlots = Math.Max(state.UnlockedSquadSlots, targetSlots);

            if (state.GuildRank < targetRank)
                state.GuildRank = targetRank;
            state.Reputation = Math.Max(state.Reputation, GuildRankBalance.GetThreshold(targetRank).PromoteAt);
        }

        private static void ReleaseMembers(Party party)
        {
            foreach (var member in party.Members)
                member.IsDispatched = false;
        }
    }
}
