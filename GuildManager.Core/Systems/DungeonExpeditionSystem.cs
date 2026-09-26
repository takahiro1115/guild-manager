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
    /// 出撃の流れ：
    ///  - 出撃操作（TryDispatch）では解決せず、メンバーを IsDispatched=true にして待機させる。
    ///  - 週次決算（ProcessWeeklyMissions）で全件をまとめて解決する。
    ///  - 同時出撃枠（GameState.UnlockedSquadSlots）を出撃中の件数で消費する（→ CanDispatch）。
    ///
    /// 毎回1Fリセット・複数週潜行型（2026年9月改訂、→ ExpeditionStatus）：
    ///  - 道中調査（Scouting）の部隊は毎回1階層から潜り、週ごとに進軍する（Advancing）。
    ///  - 未撃破ボスの階層に着くと自動では突入せず、扉前で指令を待つ（AwaitingBossDecision、
    ///    自動スキップも止める）。指令が無いまま週を越すと、扉前でボスの偵察（解析）を続ける。
    ///  - プレイヤーは「挑む」（→ TryEngageBoss：次週に決戦）か「撤退」（→ TryRetreat：即時帰還）を選ぶ。
    ///  - 帰還（撤退・勝利・全滅）で出撃は解除され、道中の拾得物をギルドへ格納する。次回はまた1階層から。
    ///    ボスの撃破状況・解析率はフィールド側に残るため、解析済みの区間は電撃的に抜けられる
    ///    （→ DungeonTraversalResolver.IntelSpeedMultiplier）。
    ///
    /// 調査任務は低リスク（HP下限1）だが、ボス討伐でHPが0になった冒険者は強制除籍
    /// （恒久ロスト）となり、戦死と同じ手順でロースターから外す（→ 03 §4.3.1）。
    /// </summary>
    public class DungeonExpeditionSystem
    {
        // 各resolverを省略した場合の既定シード（固定シードで再現性を保つ。→ MainDashboardの方針と同じ）。
        private const int DefaultTraversalSeed = 1719;
        private const int DefaultGatheringSeed = 1848;
        private const int DefaultGrowthSeed = 1907;
        private const int DefaultRelicSeed = 1969;

        private readonly ScoutingResolver _scoutingResolver;
        private readonly DungeonResolver _dungeonResolver;
        private readonly DungeonTraversalResolver _traversalResolver;
        private readonly GatheringResolver _gatheringResolver;
        private readonly SatisfactionSystem _satisfactionSystem;
        private readonly CompatibilitySystem _compatibilitySystem;
        private readonly GrowthSystem _growthSystem;

        /// <summary>
        /// 階層ボス撃破の遺物ドロップ用の乱数（→ 03 §4.7）。他の判定（討伐・調査・採取）と
        /// 別インスタンス・別シードにし、互いの抽選回数が結果に影響しないようにする。
        /// </summary>
        private readonly IRng _relicRng;

        public DungeonExpeditionSystem(
            ScoutingResolver scoutingResolver,
            DungeonResolver dungeonResolver,
            SatisfactionSystem satisfactionSystem,
            CompatibilitySystem compatibilitySystem,
            // 省略可能：道中進軍・探索（採取）の解決（→ DungeonTraversalResolver・GatheringResolver）。
            // 既存の呼び出し側を変更せずに接続できるよう、既定値を持たせている。
            DungeonTraversalResolver? traversalResolver = null,
            GatheringResolver? gatheringResolver = null,
            // 省略可能：出撃成長（→ GrowthSystem.ApplyExpeditionGrowth）。省略時は固定シードの既定構成。
            GrowthSystem? growthSystem = null,
            // 省略可能：階層ボス撃破の遺物ドロップ用乱数（→ 03 §4.7）。省略時は固定シード。
            IRng? relicRng = null)
        {
            _scoutingResolver = scoutingResolver;
            _dungeonResolver = dungeonResolver;
            _traversalResolver = traversalResolver ?? new DungeonTraversalResolver(new SeededRng(DefaultTraversalSeed));
            _gatheringResolver = gatheringResolver ?? new GatheringResolver(new SeededRng(DefaultGatheringSeed));
            _satisfactionSystem = satisfactionSystem;
            _compatibilitySystem = compatibilitySystem;
            _growthSystem = growthSystem ?? new GrowthSystem(new SeededRng(DefaultGrowthSeed));
            _relicRng = relicRng ?? new SeededRng(DefaultRelicSeed);
        }

        /// <summary>
        /// 大迷宮へ出撃させる。以下のいずれかに該当すれば何もせずfalseを返す（Try*系の共通パターン）：
        /// 同時出撃枠が埋まっている／部隊が空／ボスがGameState上に存在しない・撃破済み／
        /// ボスの所属フィールドが未開放／出撃不可（重傷・派遣中等）のメンバーが含まれている／
        /// ボス討伐（BossAssault）で携行アイテム（→ Party.ConsumableItemIds）の代金が足りない。
        ///
        /// フィールド未開放のチェック（→ 大迷宮フィールド選択UI仕様）はCore層で行う：
        /// UI（DungeonPanel）は未開放フィールドを選択できないようにしているが、それはUI側の
        /// 制約に過ぎない。出撃の可否そのものはCore層で自己完結して判定すべきという方針
        /// （→ CanDispatch等、既存のTry*系メソッドと同じ考え方）により、
        /// ここでも独立して検査する。
        ///
        /// 携行アイテムの代金（2026年9月新設、→ パーティ携行アイテムポーチ）：ボス討伐のみ、
        /// 出撃時点でpartyが携行している消耗品（→ Party.ConsumableItemIds）の合計代金を
        /// GameState.Goldから即座に引き落とす。調査・採取（Gathering、別メソッド）は対象外
        /// （携行品はDungeonResolverのギミック対策判定でのみ意味を持つため）。
        /// </summary>
        public bool TryDispatch(GameState state, Party party, FloorBoss boss, DungeonMissionType missionType)
        {
            if (!CanDispatch(state))
                return false;
            if (party.IsEmpty || party.Members.Any(m => !m.IsAvailable))
                return false;

            var field = state.DungeonFields.FirstOrDefault(f => f.Bosses.Contains(boss));
            if (field == null || !field.IsUnlocked || boss.IsDefeated)
                return false;

            int itemCost = missionType == DungeonMissionType.BossAssault
                ? CalculateConsumableCost(party.ConsumableItemIds)
                : 0;
            if (state.Gold < itemCost)
                return false;

            state.Gold -= itemCost;

            // 道中調査は必ず1階層から潜り始める（→ 1Fリセットルール）。
            // ボス討伐の直接出撃は、扉前から即座に決戦へ臨む扱い（UIはこの経路を使わず、
            // 道中調査→扉前到達→TryEngageBossの手順を踏む）。
            bool directAssault = missionType == DungeonMissionType.BossAssault;
            state.ActiveDungeonMissions.Add(new ActiveDungeonMission
            {
                Party = party,
                Field = field,
                Boss = boss,
                MissionType = missionType,
                Status = directAssault ? ExpeditionStatus.EngagingBoss : ExpeditionStatus.Advancing,
                CurrentFloor = directAssault ? boss.Floor : 1,
                TargetedBoss = directAssault ? boss : null,
            });

            foreach (var member in party.Members)
                member.IsDispatched = true;

            return true;
        }

        /// <summary>
        /// 同時出撃枠（→ GameState.UnlockedSquadSlots）に空きがあるか。枠は「部隊の数」であって人数ではない：
        /// 1枠の中で1〜4名を自由に割り振れる（フリーアサイン）。出撃中の部隊（→ ActiveDungeonMissions）の
        /// 件数で消費される。旧通常クエストの撤去（2026年9月）に伴い QuestDispatchSystem から移した。
        /// </summary>
        public static bool CanDispatch(GameState state) =>
            state.ActiveDungeonMissions.Count < state.UnlockedSquadSlots;

        /// <summary>携行アイテム一覧の合計代金（→ Models.ConsumableCatalog）。UIの費用表示からも使う。</summary>
        public static int CalculateConsumableCost(IEnumerable<string> itemIds) =>
            itemIds.Sum(id => ConsumableCatalog.FindById(id)?.Price ?? 0);

        /// <summary>
        /// 大迷宮へ探索（採取）に出撃させる。特定のボスではなくフィールドそのものを対象にする点が
        /// TryDispatchとの違い（→ GatheringResolver）。それ以外の失敗条件（枠・部隊・開放状態）は
        /// TryDispatchと同じ。
        /// </summary>
        public bool TryDispatchGathering(GameState state, Party party, DungeonField field)
        {
            if (!CanDispatch(state))
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
        /// 迷宮調査（→ DungeonMissionType.Survey、「🔍 迷宮調査に出撃」）に出撃させる。対象ボスを
        /// 次週の決算で1回調査し（→ ScoutingResolver、護衛の4段階判定付き）、解析率を上げて帰還する。
        /// 1階層からの潜行とは別の1週任務で、扉前まで潜る必要はない。
        /// 同時出撃枠が埋まっている／部隊が空・出撃不可のメンバーを含む／ボスが存在しない・
        /// 撃破済み・完全解析済み／所属フィールドが未開放 のいずれかならfalse。
        /// </summary>
        public bool TryDispatchSurvey(GameState state, Party party, FloorBoss boss)
        {
            if (!CanDispatch(state))
                return false;
            if (party.IsEmpty || party.Members.Any(m => !m.IsAvailable))
                return false;

            var field = state.DungeonFields.FirstOrDefault(f => f.Bosses.Contains(boss));
            if (field == null || !field.IsUnlocked || boss.IsDefeated)
                return false;
            if (ScoutingResolver.GetTier(boss.IntelRate) == IntelTier.Complete)
                return false;

            state.ActiveDungeonMissions.Add(new ActiveDungeonMission
            {
                Party = party,
                Field = field,
                Boss = boss,
                MissionType = DungeonMissionType.Survey,
                TargetedBoss = boss,
            });

            foreach (var member in party.Members)
                member.IsDispatched = true;

            return true;
        }

        /// <summary>
        /// 出撃予定を取り消す。出発前（まだ一度も週次決算を経ていない＝WeeksElapsed==0）の
        /// 出撃のみが対象で、判定は行われていないため何も失わない。潜行を始めた部隊を
        /// 呼び戻すには TryRetreat を使う。対象が存在しない・既に出発済みならfalse。
        /// 討伐待ち（EngagingBoss）なら、引き落とした携行アイテムの代金（→ TryDispatch）を全額返金する
        /// （判定が行われていないため何も失わない、という既存方針をゴールドにも適用する）。
        /// </summary>
        public bool TryCancel(GameState state, ActiveDungeonMission mission)
        {
            if (mission.WeeksElapsed > 0 || !state.ActiveDungeonMissions.Remove(mission))
                return false;

            if (mission.Status == ExpeditionStatus.EngagingBoss)
                state.Gold += CalculateConsumableCost(mission.Party.ConsumableItemIds);

            ReleaseMembers(mission.Party);
            return true;
        }

        /// <summary>
        /// 扉前で判断待ち（AwaitingBossDecision）の部隊に、ボス討伐を指令する（→ 【⚔️ ボス討伐に挑む】）。
        /// 指定した携行ポーチのアイテム（→ Party.ConsumableItemIds）を積み込み、その代金を即座に引き落とし、
        /// 状態を EngagingBoss にする。決戦判定（→ DungeonResolver）は次週の決算で行う。
        /// 判断待ちでない・ボスが既に撃破済み・代金が足りない場合は何もせずfalse。
        /// </summary>
        public bool TryEngageBoss(GameState state, ActiveDungeonMission mission, IEnumerable<string> pouchItemIds)
        {
            if (!state.ActiveDungeonMissions.Contains(mission) ||
                mission.Status != ExpeditionStatus.AwaitingBossDecision ||
                mission.TargetedBoss == null || mission.TargetedBoss.IsDefeated)
                return false;

            var previousItems = new List<string>(mission.Party.ConsumableItemIds);
            mission.Party.ConsumableItemIds.Clear();
            foreach (var itemId in pouchItemIds)
                mission.Party.TryAddConsumable(itemId);

            int itemCost = CalculateConsumableCost(mission.Party.ConsumableItemIds);
            if (state.Gold < itemCost)
            {
                mission.Party.ConsumableItemIds = previousItems;
                return false;
            }

            state.Gold -= itemCost;
            mission.Status = ExpeditionStatus.EngagingBoss;
            return true;
        }

        /// <summary>
        /// 潜行中の部隊を即時撤退・帰還させる（→ 【🏃 撤退・帰還する】）。討伐は行わず、道中で拾い集めた
        /// 素材・ゴールドをギルドへ格納し、部隊全員を待機中へ戻す（出撃は解除され、次回は1階層から）。
        /// 対象は出発済みで進軍中（Advancing）または判断待ち（AwaitingBossDecision）の出撃のみ。
        /// 帰還の内訳（→ DungeonMissionResolution.DepositedGold等）を返す。撤退できなければnull。
        /// </summary>
        public DungeonMissionResolution? TryRetreat(GameState state, ActiveDungeonMission mission)
        {
            bool retreatable = mission.MissionType == DungeonMissionType.Scouting &&
                (mission.Status == ExpeditionStatus.AwaitingBossDecision ||
                 (mission.Status == ExpeditionStatus.Advancing && mission.WeeksElapsed > 0));
            if (!retreatable || !state.ActiveDungeonMissions.Contains(mission))
                return null;

            var resolution = new DungeonMissionResolution(
                mission.Party, mission.TargetedBoss ?? mission.Boss, mission.Field, DungeonMissionType.Scouting);
            mission.Status = ExpeditionStatus.Retreating;
            ReturnHome(state, mission, resolution);
            ReleaseOrphanedDispatchFlags(state);
            return resolution;
        }

        /// <summary>
        /// 週次決算処理。出撃中の全件を1週分進める（→ ExpeditionStatus）。
        /// 解決結果の一覧を返す（UI側の週報表示用。報告すべき出来事が無かった出撃は含めない）。
        ///
        /// 帰還（勝利・撤退・全滅・採取完了）した出撃は ActiveDungeonMissions から除去され
        /// （＝同時出撃枠が即座に空く）、生還者は待機中（IsDispatched=false）へ戻る。
        /// 解決中に例外が起きた場合も、その部隊は帰還扱いにして取り残さない。
        /// 最後に、どの出撃にも属していないのに IsDispatched のまま取り残された冒険者を
        /// 待機中へ戻す（→ ReleaseOrphanedDispatchFlags）。
        /// </summary>
        public List<DungeonMissionResolution> ProcessWeeklyMissions(GameState state)
        {
            var resolutions = new List<DungeonMissionResolution>();

            foreach (var mission in state.ActiveDungeonMissions.ToList())
            {
                try
                {
                    mission.WeeksElapsed++;
                    var resolution = ResolveMission(state, mission);
                    if (resolution == null)
                        continue;

                    resolution.StatusAfter = mission.Status;
                    resolution.CurrentFloor = mission.CurrentFloor;
                    resolutions.Add(resolution);
                }
                catch
                {
                    state.ActiveDungeonMissions.Remove(mission);
                    ReleaseMembers(mission.Party);
                    throw;
                }
            }

            ReleaseOrphanedDispatchFlags(state);
            return resolutions;
        }

        /// <summary>
        /// どの出撃（→ GameState.ActiveDungeonMissions）にも属していないのに
        /// IsDispatched=true のまま残っている現役冒険者を待機中へ戻す。旧バージョンのセーブや
        /// 解決途中の例外などで生じた不整合から、出撃不能（「待機中 0名」）状態が永続するのを防ぐ。
        /// </summary>
        public static void ReleaseOrphanedDispatchFlags(GameState state)
        {
            var engagedIds = state.ActiveDungeonMissions.SelectMany(m => m.Party.Members)
                .Select(m => m.Id)
                .ToHashSet();

            foreach (var adventurer in state.Adventurers)
            {
                if (adventurer.IsDispatched && !engagedIds.Contains(adventurer.Id))
                    adventurer.IsDispatched = false;
            }
        }

        /// <summary>出撃1件を1週分進める。報告すべき出来事が無ければnull。</summary>
        private DungeonMissionResolution? ResolveMission(GameState state, ActiveDungeonMission mission)
        {
            if (mission.MissionType == DungeonMissionType.Gathering)
            {
                // 探索（採取）：特定のボスを対象にしないため、複数部隊が同じフィールドへ
                // 出ていても「先に解決した部隊が空振りになる」レース条件はそもそも存在しない
                // （毎回必ず成果が出る）。潜行型ではなく、従来どおり1週で帰還する。
                var gathering = _gatheringResolver.Resolve(mission.Party, mission.Field, state);
                state.AddMaterial(gathering.MaterialId, gathering.MaterialCount);
                state.Gold += gathering.GoldEarned;

                // 累積功績（→ AwardContribution）：素材を持ち帰れた場合のみ。
                if (!string.IsNullOrEmpty(gathering.MaterialId) && gathering.MaterialCount > 0)
                    AwardContribution(state, mission.Party, DungeonBalance.ContributionPerGathering);

                var resolution = new DungeonMissionResolution(mission.Party, mission.Field, gathering);

                // 未鑑定の古代遺物（→ 03 §4.7）：GatheringResolverが記録したドロップを
                // ここでギルドの未鑑定在庫へ移す（素材・ゴールドと同じく反映は週次解決側の責務）。
                if (gathering.UnidentifiedItemFound != null)
                {
                    state.UnidentifiedItems.Add(gathering.UnidentifiedItemFound);
                    resolution.RelicsFound.Add(gathering.UnidentifiedItemFound);
                }

                resolution.GrowthEvents = _growthSystem.ApplyExpeditionGrowth(state, mission.Party, DungeonMissionType.Gathering, isBossVictory: false);
                // 士気（→ SatisfactionSystem.ApplyExpeditionSatisfaction）：素材を持ち帰れた時のみ上がる。
                bool gatheredAny = !string.IsNullOrEmpty(gathering.MaterialId) && gathering.MaterialCount > 0;
                _satisfactionSystem.ApplyExpeditionSatisfaction(state, mission.Party, DungeonMissionType.Gathering,
                    succeeded: gatheredAny);
                // 相性（→ CompatibilitySystem.ApplyExpeditionOutcome）：素材を持ち帰れた時のみ全ペア+1。
                resolution.CompatibilityGain = _compatibilitySystem.ApplyExpeditionOutcome(
                    state, mission.Party, DungeonMissionType.Gathering, isSuccess: gatheredAny);
                ReturnHome(state, mission, resolution);
                return resolution;
            }

            if (mission.MissionType == DungeonMissionType.Survey)
                return ResolveSurvey(state, mission);

            return mission.Status switch
            {
                ExpeditionStatus.Advancing => Advance(state, mission),
                ExpeditionStatus.AwaitingBossDecision => WaitAtBossDoor(state, mission),
                ExpeditionStatus.EngagingBoss => EngageBoss(state, mission),
                _ => ReturnHomeWithoutAction(state, mission), // Retreating
            };
        }

        /// <summary>
        /// 道中進軍（Advancing）。現在階層から次の未撃破ボスへ向けて進み（→ DungeonTraversalResolver）、
        /// 拾得物を部隊に持たせる。ボス階層に着いたら進軍を止めて判断待ちにする（自動では突入しない）。
        /// 未撃破ボスが1体も残っていないフィールドを最深部まで踏破した場合は、そのまま帰還する。
        /// </summary>
        private DungeonMissionResolution Advance(GameState state, ActiveDungeonMission mission)
        {
            var field = mission.Field;
            var stopper = field.GetNextUndefeatedBossFrom(mission.CurrentFloor);
            TraversalResult traversal;

            if (stopper != null && mission.CurrentFloor >= stopper.Floor)
            {
                // 出発階層が既にボス階層（1階層のボス等）：進軍の必要が無く、そのまま扉前で待機する。
                traversal = new TraversalResult
                {
                    FloorBefore = mission.CurrentFloor,
                    FloorAfter = mission.CurrentFloor,
                    StopperTriggered = true,
                    TargetBoss = stopper,
                };
            }
            else
            {
                traversal = _traversalResolver.Resolve(mission.Party, field, mission.CurrentFloor, state);
                mission.CurrentFloor = traversal.FloorAfter;
                mission.CarriedGold += traversal.LootGold;
                foreach (var kv in traversal.LootMaterials)
                    mission.CarriedMaterials[kv.Key] = mission.CarriedMaterials.TryGetValue(kv.Key, out int n) ? n + kv.Value : kv.Value;

                // 累積功績（→ AwardContribution）：進軍した階層数に比例。
                AwardContribution(state, mission.Party,
                    (traversal.FloorAfter - traversal.FloorBefore) * DungeonBalance.ContributionPerTraversedFloor);
            }

            var boss = traversal.TargetBoss ?? mission.Boss;
            var resolution = new DungeonMissionResolution(mission.Party, boss, field, boss?.IntelRate ?? 0, traversal);
            resolution.TraitGrantEvents.AddRange(traversal.TraitGrantEvents); // 重傷生還の古傷（→ 03 §4.3）
            // 出撃成長（→ GrowthSystem.ApplyExpeditionGrowth）：実際に1階層以上進軍した週のみ。
            if (traversal.FloorAfter > traversal.FloorBefore)
            {
                resolution.GrowthEvents = _growthSystem.ApplyExpeditionGrowth(state, mission.Party, DungeonMissionType.Scouting, isBossVictory: false);
                // 士気：進軍して生還した週（道中はHP下限1のため、進軍した者は必ず生還している）。
                _satisfactionSystem.ApplyExpeditionSatisfaction(state, mission.Party, DungeonMissionType.Scouting, succeeded: true);
                // 相性：1階層以上進軍した週のみ全ペア+1（→ CompatibilitySystem.ApplyExpeditionOutcome）。
                resolution.CompatibilityGain = _compatibilitySystem.ApplyExpeditionOutcome(
                    state, mission.Party, DungeonMissionType.Scouting, isSuccess: true);
            }

            if (traversal.StopperTriggered && traversal.TargetBoss != null)
            {
                mission.Status = ExpeditionStatus.AwaitingBossDecision;
                mission.TargetedBoss = traversal.TargetBoss;
                resolution.ArrivedAtBossDoor = true;
            }
            else if (stopper == null && mission.CurrentFloor >= DungeonField.MaxFloor)
            {
                ReturnHome(state, mission, resolution);
            }

            return resolution;
        }

        /// <summary>
        /// 扉前で判断待ちのまま週を越した部隊（AwaitingBossDecision）。突入の指令を待つ間、
        /// 扉の向こうのボスを偵察して解析率を上げる（→ ScoutingResolver。完全解析済みなら何もしない）。
        /// 待機中に他の部隊がそのボスを倒していた場合は、判断待ちを解いて先へ進軍する。
        /// </summary>
        private DungeonMissionResolution? WaitAtBossDoor(GameState state, ActiveDungeonMission mission)
        {
            var boss = mission.TargetedBoss;
            if (boss == null || boss.IsDefeated)
            {
                mission.Status = ExpeditionStatus.Advancing;
                mission.TargetedBoss = null;
                return Advance(state, mission);
            }

            if (ScoutingResolver.GetTier(boss.IntelRate) == IntelTier.Complete)
                return null;

            double intelBefore = boss.IntelRate;
            var scouting = _scoutingResolver.Resolve(mission.Party, boss, state);
            return new DungeonMissionResolution(mission.Party, boss, mission.Field, intelBefore, scouting);
        }

        /// <summary>
        /// 迷宮調査（Survey）の解決：対象ボスを1回調査して解析率を加算し（上限1.0、→ ScoutingResolver）、
        /// 帰還する。出発後に対象ボスが他部隊に倒されていた場合は、調査を行わずに帰還する。
        /// </summary>
        private DungeonMissionResolution ResolveSurvey(GameState state, ActiveDungeonMission mission)
        {
            var boss = mission.TargetedBoss ?? mission.Boss;
            if (boss == null || boss.IsDefeated)
                return ReturnHomeWithoutAction(state, mission);

            double intelBefore = boss.IntelRate;
            var scouting = _scoutingResolver.Resolve(mission.Party, boss, state);
            var resolution = new DungeonMissionResolution(
                mission.Party, boss, mission.Field, intelBefore, scouting, DungeonMissionType.Survey);
            resolution.GrowthEvents = _growthSystem.ApplyExpeditionGrowth(state, mission.Party, DungeonMissionType.Survey, isBossVictory: false);
            // 士気：護衛段階に連動（余裕/十分で上昇、不足＝潰走で低下）。
            _satisfactionSystem.ApplyExpeditionSatisfaction(state, mission.Party, DungeonMissionType.Survey,
                succeeded: scouting.GuardTier != GuardTier.Deficient, surveyGuardTier: scouting.GuardTier);
            // 相性：護衛「不足」（潰走）以外で帰還した時のみ全ペア+1。
            resolution.CompatibilityGain = _compatibilitySystem.ApplyExpeditionOutcome(
                state, mission.Party, DungeonMissionType.Survey, isSuccess: scouting.GuardTier != GuardTier.Deficient);
            ReturnHome(state, mission, resolution);
            return resolution;
        }

        /// <summary>
        /// ボス討伐（EngagingBoss）の決戦判定。勝敗にかかわらず決着後は帰還する
        /// （勝利・撤退・全滅のいずれも出撃は解除され、次回は1階層から）。
        /// 討伐に向かったボスが既に他部隊に倒されていた場合は、判定を行わずに帰還し、
        /// 使われなかった携行アイテムの代金を返金する。
        /// </summary>
        private DungeonMissionResolution EngageBoss(GameState state, ActiveDungeonMission mission)
        {
            var boss = mission.TargetedBoss ?? mission.Boss;
            if (boss == null || boss.IsDefeated)
            {
                state.Gold += CalculateConsumableCost(mission.Party.ConsumableItemIds);
                return ReturnHomeWithoutAction(state, mission);
            }

            double intelBefore = boss.IntelRate;
            var assault = _dungeonResolver.Resolve(mission.Party, boss, state);
            var recoveredEquipment = ApplyForcedRetirements(state, mission.Party, assault.ForceRetiredAdventurerIds, out var traumaGrants);
            // 強制除籍（不死薬による現場からの永久離脱）へのアルベールの激怒：除籍1名につき機嫌−20（→ 03 §8.1）。
            // 0に達すれば、この週の決算で副官解雇になる（→ DefeatSystem）。
            int furyRetiredCount = assault.ForceRetiredAdventurerIds.Count;
            int furyMoodApplied = MasterMoodSystem.ApplyForcedRetirementFury(state, furyRetiredCount);

            // 撃破成功時のみ、フィールドの進行（最高到達階層・次フィールド開放・報酬・
            // 節目ボスでの出撃枠拡張）を適用する（→ 大迷宮5フィールド拡張仕様）。
            // 撤退（Retreat）時は何も進行しない。
            int? squadSlotsExpandedTo = null;
            DungeonField? fieldNewlyUnlocked = null;
            UnidentifiedItem? relic = null;
            if (assault.Outcome == DungeonOutcome.Victory)
            {
                int slotsBefore = state.UnlockedSquadSlots;
                var unlockedFieldIdsBefore = state.DungeonFields.Where(f => f.IsUnlocked).Select(f => f.Id).ToHashSet();

                ApplyFieldProgression(state, boss);

                // 累積功績（→ AwardContribution）：撃破したボスの階層に比例（深い階層ほど大きい）。
                // 強制除籍された者はロースターから外れているため加算されない。
                AwardContribution(state, mission.Party, boss.Floor * DungeonBalance.ContributionPerBossFloor);

                if (state.UnlockedSquadSlots > slotsBefore)
                    squadSlotsExpandedTo = state.UnlockedSquadSlots;
                fieldNewlyUnlocked = state.DungeonFields
                    .FirstOrDefault(f => f.IsUnlocked && !unlockedFieldIdsBefore.Contains(f.Id));

                // 未鑑定の古代遺物の確定ドロップ（→ 03 §4.7）。撃破したボスの階層が深いほど
                // 希少度ロールが伸び、さらにボス撃破ボーナスと最低保証希少度が乗る
                // （→ RelicBalance.BossRelicRollBonus・BossMinimumRarity）。
                relic = AppraisalSystem.CreateRelic(
                    _relicRng, mission.Field.Id, boss.Floor,
                    RelicBalance.BossRelicRollBonus, RelicBalance.BossMinimumRarity);
                state.UnidentifiedItems.Add(relic);
            }

            var resolution = new DungeonMissionResolution(
                mission.Party, boss, mission.Field, intelBefore, assault, squadSlotsExpandedTo, fieldNewlyUnlocked);
            if (relic != null)
                resolution.RelicsFound.Add(relic);
            resolution.RecoveredEquipment = recoveredEquipment;
            // 障害特性の開示（→ 03 §5.3.2）：撤退時の重傷生還による古傷と、仲間除籍の衝撃によるトラウマ。
            resolution.TraitGrantEvents.AddRange(assault.TraitGrantEvents);
            resolution.TraitGrantEvents.AddRange(traumaGrants);
            resolution.MasterFuryRetiredCount = furyRetiredCount;
            resolution.MasterFuryMoodApplied = furyMoodApplied;
            // 出撃成長：撃破した場合のみ（全7能力・試行回数多）。撤退・全滅では成長しない。強制除籍者は対象外。
            resolution.GrowthEvents = _growthSystem.ApplyExpeditionGrowth(
                state, mission.Party, DungeonMissionType.BossAssault, isBossVictory: assault.Outcome == DungeonOutcome.Victory);
            // 士気：撃破なら高揚、撤退・敗退なら挫折（生存者のみ。強制除籍者が出た場合の仲間ロストの余波-30は
            // ApplyForcedRetirements で既に適用済みで、これと加算で重なる）。
            _satisfactionSystem.ApplyExpeditionSatisfaction(state, mission.Party, DungeonMissionType.BossAssault,
                succeeded: assault.Outcome == DungeonOutcome.Victory);
            // 相性：撃破した時のみ、生還者（強制除籍者を除く）の全ペア+3。
            resolution.CompatibilityGain = _compatibilitySystem.ApplyExpeditionOutcome(
                state, mission.Party, DungeonMissionType.BossAssault, isSuccess: assault.Outcome == DungeonOutcome.Victory);
            ReturnHome(state, mission, resolution);
            return resolution;
        }

        private DungeonMissionResolution ReturnHomeWithoutAction(GameState state, ActiveDungeonMission mission)
        {
            var resolution = new DungeonMissionResolution(
                mission.Party, mission.TargetedBoss ?? mission.Boss, mission.Field, mission.MissionType);
            ReturnHome(state, mission, resolution);
            return resolution;
        }

        /// <summary>
        /// 累積功績（→ Adventurer.TotalContributionScore、退職金の上乗せ原資 → AgingSystem）を、
        /// 部隊の生存者（現役ロースターに残っている者）全員へ加算する。旧通常クエストの解決時に
        /// 加算していたものを、旧クエストの撤去（2026年9月）に伴い大迷宮の活動へ再配線した：
        /// 道中進軍（1階層ごと）・階層ボス撃破（ボスの階層に比例）・採取（素材獲得時）。
        /// 係数はいずれも dungeon.csv の Contribution*（→ DungeonBalance）。
        /// </summary>
        public static void AwardContribution(GameState state, Party party, int points)
        {
            if (points <= 0)
                return;

            foreach (var member in party.Members)
            {
                if (state.Adventurers.Contains(member))
                    member.TotalContributionScore += points;
            }
        }

        /// <summary>
        /// ギルドへの帰還（→ 1Fリセットルール）。出撃を解除して同時出撃枠を空け、生還者を待機中へ戻し、
        /// 道中の拾得物をギルドへ格納する（生還者が1人もいない＝全滅なら拾得物も失われる）。
        /// 累計出撃回数（→ GameState.TotalDispatchCount）は、取り消しで水増しされないよう帰還時に数える。
        /// </summary>
        private static void ReturnHome(GameState state, ActiveDungeonMission mission, DungeonMissionResolution resolution)
        {
            state.ActiveDungeonMissions.Remove(mission);
            state.TotalDispatchCount++;

            bool anySurvivor = mission.Party.Members.Any(m => state.Adventurers.Contains(m));
            if (anySurvivor)
            {
                state.Gold += mission.CarriedGold;
                foreach (var kv in mission.CarriedMaterials)
                    state.AddMaterial(kv.Key, kv.Value);

                resolution.DepositedGold = mission.CarriedGold;
                resolution.DepositedMaterials = new Dictionary<string, int>(mission.CarriedMaterials);
            }

            mission.CarriedGold = 0;
            mission.CarriedMaterials.Clear();
            ReleaseMembers(mission.Party);

            resolution.ReturnedHome = true;
            resolution.StatusAfter = mission.Status;
            resolution.CurrentFloor = mission.CurrentFloor;
        }

        /// <summary>
        /// 強制除籍の処理（→ DungeonResult.ForceRetiredAdventurerIds）。システム上は戦死と同じ
        /// 恒久ロストのため、旧通常クエストの戦死処理と同じ手順
        /// （仲間ロストの満足度低下・相性の余波・ロースターから記録への移動）を踏む。
        ///
        /// 2026年9月改訂（→ §4.2.2「離脱時の自動回収」）：記録リストへ移す**直前に**全装備を
        /// ギルド保管庫へ回収する（→ EquipmentSystem.UnequipAllToArmory）。倒れた隊員を
        /// 救い出すアルベールが、その武具も併せて持ち帰る扱い。回収した内訳
        /// （冒険者Id → 装備一覧）を返し、週報ログで報告できるようにする。
        /// </summary>
        private Dictionary<Guid, List<EquipmentItem>> ApplyForcedRetirements(
            GameState state, Party party, IEnumerable<Guid> retiredIds, out List<TraitGrantEvent> traumaGrants)
        {
            var recovered = new Dictionary<Guid, List<EquipmentItem>>();
            traumaGrants = new List<TraitGrantEvent>();
            var retiredSet = retiredIds.ToHashSet();

            foreach (var retiredId in retiredIds)
            {
                _satisfactionSystem.ApplyPartyLossPenalty(party, retiredId);
                traumaGrants.AddRange(_compatibilitySystem.ApplyDeathAftermath(state, party, retiredId, retiredSet));

                var retired = party.Members.FirstOrDefault(m => m.Id == retiredId);
                if (retired == null)
                    continue;

                var recoveredEquipment = EquipmentSystem.UnequipAllToArmory(state, retired, $"{retired.Name}（除籍）から返還");
                if (recoveredEquipment.Count > 0)
                    recovered[retiredId] = recoveredEquipment.ToList();

                retired.FellAtWeek = state.WeekNumber;
                state.TrainingAssignments.Remove(retired.Id);
                state.Adventurers.Remove(retired);
                state.FallenAdventurers.Add(retired);
            }

            return recovered;
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

            // ①撃破報酬（→ FloorBoss.RewardGold/RewardMaterialId・RewardMaterialCount。機嫌の上昇は週次決算の MasterMoodSystem が担う）。
            state.Gold += defeatedBoss.RewardGold;
            if (!string.IsNullOrEmpty(defeatedBoss.RewardMaterialId))
                state.AddMaterial(defeatedBoss.RewardMaterialId, defeatedBoss.RewardMaterialCount);

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
            // （→ 03 §8.2。旧来の「Aランク到達」経路はギルド格付けの廃止（2026年9月）で撤去し、この経路のみになった）。
            var finalField = state.DungeonFields.OrderByDescending(f => f.Order).FirstOrDefault();
            if (finalField != null && field == finalField && defeatedBoss.Floor == DungeonField.MaxFloor)
                state.FinalQuestUnlocked = true;

            // ⑥出撃枠拡張（「古代エルフの多頭通信術式」復元、→ 大迷宮ボス間隔・敗北条件改訂）。
            // 森（Order=1）の節目ボス撃破のみが対象：10F撃破で2枠、20F撃破で3枠まで引き上げる
            // （既に到達値以上ならダウングレードしない）。旧来はギルド格付けE・D相当への引き上げも
            // 伴っていたが、格付けの廃止（2026年9月）に伴い出撃枠のみになった。
            if (field.Order == 1)
            {
                if (defeatedBoss.Floor == 10)
                    state.UnlockedSquadSlots = Math.Max(state.UnlockedSquadSlots, 2);
                else if (defeatedBoss.Floor == 20)
                    state.UnlockedSquadSlots = Math.Max(state.UnlockedSquadSlots, 3);
            }
        }

        private static void ReleaseMembers(Party party)
        {
            foreach (var member in party.Members)
                member.IsDispatched = false;
        }
    }
}
