using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 大迷宮「道中進軍」の解決エンジン（→ 03 §4.5.2、大迷宮5フィールド拡張仕様）。
    ///
    /// 調査任務は解決時の状況で2つに分岐する：
    ///  - 分岐A（本クラス）：現在の到達階層（DungeonField.ReachedFloor）が次の未撃破ボスより
    ///    浅い場合。部隊の走破力に応じて複数階層を一気に進軍する（一度倒したボス階層は
    ///    素通りできる＝既に IsDefeated 済みのボスは再び足止めにならない）。
    ///  - 分岐B（→ ScoutingResolver）：既にボス階層に到達している場合。従来どおりの
    ///    隠密・解析判定でIntelRateを上げる。
    ///
    /// 低リスク経路として設計する：ScoutingResolverと同じくHP下限1で止まり、
    /// 致死判定には一切接続しない（→ ScoutingResolver.MinHpと同じ考え方）。
    ///
    /// 毎回1Fリセット・複数週潜行型（2026年9月改訂）：潜行中の部隊は Resolve(party, field, currentFloor)
    /// で現在階層から進む。1歩の重さはその階層の要求値で決まり、深く潜るほど1週に進める階層が減る（→ StepCost、§0.49）。
    /// 調査度連動の走破加速（→ IntelSpeedMultiplier）により、解析済みの区間ほど
    /// 少ない予算で抜けられ（完全解析で3倍速）、完全解析済みの区間を歩いた階層ではHP損耗も軽減される
    /// （→ DamageTakenMultiplier。損耗は階層ごとに積み上げる、→ ApplyHpLoss）。進んだ階層に応じて素材・ゴールドを拾う（→ TraversalResult.LootGold等）。
    /// </summary>
    public class DungeonTraversalResolver
    {
        /// <summary>道中進軍でのHP下限。致死判定に接続しないため0にはしない。</summary>
        private const int MinHp = 1;

        private readonly IRng _rng;

        public DungeonTraversalResolver(IRng rng)
        {
            _rng = rng;
        }

        /// <summary>
        /// 部隊を道中へ差し向け、到達階層を進める。field.ReachedFloor は本メソッドが直接書き換える。
        /// nextBossの階層を超えて進むことはない（ストッパー、→ TraversalResult.StopperTriggered）。
        /// </summary>
        /// <param name="state">
        /// 省略可能。CalculateTraversalScoreへそのまま渡す（→ 研究「軽装の踏破術」のボーナス）。
        /// nullなら通常どおりボーナス無しで解決する（既存の呼び出し側・テストとの互換用）。
        /// </param>
        public TraversalResult Resolve(Party party, DungeonField field, FloorBoss nextBoss, GameState? state = null)
        {
            var result = ResolveFrom(party, field, field.ReachedFloor, nextBoss, state);
            field.ReachedFloor = result.FloorAfter;
            return result;
        }

        /// <summary>
        /// 潜行中の部隊を currentFloor から進軍させる（→ 毎回1Fリセット・複数週潜行型、2026年9月新設）。
        /// 足止め先は currentFloor 以降で最初の未撃破ボス（→ DungeonField.GetNextUndefeatedBossFrom）。
        /// field.ReachedFloor は「最高到達階層の記録」として、より深く潜れた場合のみ更新する。
        /// 部隊の現在階層（ActiveDungeonMission.CurrentFloor）の更新は呼び出し側が result.FloorAfter で行う。
        /// </summary>
        public TraversalResult Resolve(Party party, DungeonField field, int currentFloor, GameState? state = null)
        {
            var stopper = field.GetNextUndefeatedBossFrom(currentFloor);
            var result = ResolveFrom(party, field, currentFloor, stopper, state);
            field.ReachedFloor = Math.Max(field.ReachedFloor, result.FloorAfter);
            return result;
        }

        /// <summary>
        /// 進軍の本体（2026年9月・§0.49で階層ごとの要求値へ改訂）。1週の移動予算1を、1階層ずつ消費して進む：
        /// 1歩の消費＝その階層の要求値 ÷（走破力×FloorsPerRatio）÷ 走破倍率（→ StepCost）。
        /// 深い階層ほど要求値が大きく1歩が重くなり、解析済みの区間ほど軽くなる（完全解析で1/3）。
        /// 最初の1歩だけは予算が足りなくても必ず進む（完全な足止めにはしない）。
        /// 進軍ランク・比率は、その週に最後に踏み出した階層（＝先頭）の要求値で決める（→ PlanWeek）。
        /// HP損耗は歩いた階層ごとに「その階層の基礎損耗率×その区間の被ダメージ倍率」を積み上げる
        /// （→ ApplyHpLoss。2026年9月改訂：旧来の「進軍全体の基礎率×平均倍率」を廃止）。
        /// </summary>
        private TraversalResult ResolveFrom(Party party, DungeonField field, int startFloor, FloorBoss? stopper, GameState? state)
        {
            if (party.Members.Count == 0)
                throw new InvalidOperationException("空のパーティは道中進軍に出せません。");

            var result = new TraversalResult { FloorBefore = startFloor };
            if (state != null)
            {
                result.AdvisorTraversalBonus = AdvisorSystem.GetAdvisorTraversalPowerBonus(state);
                result.AdvisorName = result.AdvisorTraversalBonus > 0 ? AdvisorSystem.GetAssignedAdvisor(state)?.Name : null;
            }

            double score = CalculateTraversalScore(party, state);
            var plan = Walk(field, startFloor, stopper, score);

            var rank = plan.Rank;
            result.Rank = rank;
            result.BaseFloors = plan.BaseFloors;
            result.TraversalScore = score;
            result.Requirement = plan.Requirement;
            result.Ratio = plan.Ratio;
            result.FrontFloor = plan.FrontFloor;

            var startSegmentBoss = SegmentBoss(field, startFloor, stopper);
            // 迷宮の異変「瘴気」（→ DungeonAnomalySystem、§0.64）：その週の損耗を区間の被ダメージ倍率に上乗せする。
            double miasma = DungeonAnomalySystem.DamageMultiplier(state, field.Id);

            int exploredRecord = field.ReachedFloor; // 進軍前の最高到達階層（これより深い階層が未踏破）
            var steps = new List<TraversalStep>();
            for (int from = startFloor; from < plan.FloorAfter; from++)
            {
                // from→from+1 の1歩は、出発する階層 from の区間担当ボスが受け持つ
                // （→ GetSegmentBoss。9→10Fは10Fボス、10→11Fは20Fボスの区間）。
                var segmentBoss = SegmentBoss(field, from, stopper);
                int floor = from + 1;
                bool unexplored = floor > exploredRecord;
                if (unexplored)
                    result.EnteredUnexplored = true;
                steps.Add(new TraversalStep(floor, segmentBoss, unexplored, IntelSpeedMultiplier(segmentBoss), DamageTakenMultiplier(segmentBoss) * miasma));
                CollectLoot(result, field, floor);
            }

            if (stopper != null && plan.FloorAfter >= stopper.Floor)
            {
                result.StopperTriggered = true;
                result.TargetBoss = stopper;
            }

            result.FloorAfter = plan.FloorAfter;
            result.UnexploredFloorsAdvanced = steps.Count(s => s.Unexplored);

            // 進軍全体の実効倍率（階層数で重み付けした平均）。1階層も進めなかった場合は出発区間の値。
            result.IntelSpeedMultiplier = steps.Count > 0 ? steps.Average(s => s.Speed) : IntelSpeedMultiplier(startSegmentBoss);
            result.DamageTakenMultiplier = steps.Count > 0 ? steps.Average(s => s.Damage) : DamageTakenMultiplier(startSegmentBoss) * miasma;

            ApplyHpLoss(result, party, rank, steps, startSegmentBoss, miasma);

            return result;
        }

        /// <summary>進軍の1歩（→ ResolveFrom）。Floor は踏み入れた階層。</summary>
        private readonly record struct TraversalStep(int Floor, FloorBoss? Boss, bool Unexplored, double Speed, double Damage);

        /// <summary>浮動小数の誤差（1/3×3 等）で1階層取りこぼさないための許容幅。</summary>
        private const double BudgetEpsilon = 1e-9;

        /// <summary>
        /// 1週ぶんの進軍の見通し（→ PlanWeek・Walk）。FrontFloor＝その週に最後に踏み出した階層（1歩も進めなければ出発階層）。
        /// Requirement・Ratio・BaseFloors・Rank はいずれも先頭の階層の要求値で求めた値で、
        /// BaseFloors は「その深さで比率のまま1週に進める階層数」（＝ CalculateBaseFloors(Ratio)）。
        /// </summary>
        public readonly record struct TraversalPlan(int FloorAfter, int FrontFloor, double Requirement, double Ratio, int BaseFloors, TraversalRank Rank);

        /// <summary>
        /// 1歩（floor→floor+1）の予算の消費（2026年9月・§0.49）＝ floor の要求値 ÷（走破力×FloorsPerRatio）÷ 区間の走破倍率。
        /// 1週の予算は1。走破力が0なら無限大（最初の1歩だけ進む）。
        /// </summary>
        public static double StepCost(DungeonField field, int floor, double score, FloorBoss? stopper = null)
        {
            if (score <= 0)
                return double.PositiveInfinity;
            return FloorRequirement(field, floor) / (score * DungeonTraversalBalance.FloorsPerRatio)
                / IntelSpeedMultiplier(SegmentBoss(field, floor, stopper));
        }

        /// <summary>
        /// 1週の進軍を数える本体（Resolve と出撃前プレビューで共有する。乱数・状態の変更は無い）。
        /// 予算1から StepCost を引きながら進み、stopper の階層・最深部で必ず止まる（越境しない）。
        /// 最初の1歩は予算が足りなくても進む。
        /// </summary>
        private static TraversalPlan Walk(DungeonField field, int startFloor, FloorBoss? stopper, double score)
        {
            int limit = stopper == null ? DungeonField.MaxFloor : Math.Min(DungeonField.MaxFloor, stopper.Floor);
            double budget = WeeklyBudget;
            int floor = startFloor;
            while (floor < limit)
            {
                double cost = StepCost(field, floor, score, stopper);
                if (floor > startFloor && budget + BudgetEpsilon < cost)
                    break;
                budget -= cost;
                floor++;
            }

            int front = floor > startFloor ? floor - 1 : startFloor;
            double requirement = FloorRequirement(field, front);
            double ratio = requirement <= 0 ? double.MaxValue : score / requirement;
            int baseFloors = CalculateBaseFloors(ratio);
            return new TraversalPlan(floor, front, requirement, ratio, baseFloors, ClassifyRatio(ratio));
        }

        /// <summary>1週の移動予算（§0.49）。1歩の消費は StepCost。</summary>
        private const double WeeklyBudget = 1.0;

        /// <summary>
        /// 出撃前プレビュー用：startFloor から1週で進んだ場合の見通し。Resolve と同じ規則で数え、
        /// startFloor 以降で最初の未撃破ボスの階層で止まる。
        /// </summary>
        public static TraversalPlan PlanWeek(DungeonField field, int startFloor, double score) =>
            Walk(field, startFloor, field.GetNextUndefeatedBossFrom(startFloor), score);

        /// <summary>出撃前プレビュー用：startFloor から1週で進んだ場合の到達階層の予測（→ PlanWeek）。</summary>
        public static int PredictFloorAfter(DungeonField field, int startFloor, double score) =>
            PlanWeek(field, startFloor, score).FloorAfter;

        /// <summary>
        /// 出撃前プレビュー用：startFloor から targetFloor（または途中の未撃破ボスの階層・最深部）に着くまでの週数。
        /// 解析率・走破力は出発時のまま変わらないものとして数える。maxWeeks を超える場合は maxWeeks＋1 を返す。
        /// </summary>
        public static int PredictWeeksToFloor(DungeonField field, int startFloor, int targetFloor, double score, int maxWeeks = 99)
        {
            var stopper = field.GetNextUndefeatedBossFrom(startFloor);
            int goal = Math.Min(targetFloor, stopper == null ? DungeonField.MaxFloor : Math.Min(DungeonField.MaxFloor, stopper.Floor));
            int floor = startFloor;
            for (int week = 1; week <= maxWeeks; week++)
            {
                floor = Walk(field, floor, stopper, score).FloorAfter;
                if (floor >= goal)
                    return week;
            }
            return maxWeeks + 1;
        }

        /// <summary>
        /// 出撃前プレビュー用：fromFloor から toFloor まで進むとした場合の区間内訳
        /// （区間担当ボスと未踏破かどうかが変わるごとに1区間）。損耗率は含まない（BaseLossPct=0）。
        /// exploredRecord より深い階層を未踏破として扱う。
        /// </summary>
        public static List<TraversalSegmentDetail> DescribeSegments(DungeonField field, int fromFloor, int toFloor, int exploredRecord)
        {
            var steps = new List<TraversalStep>();
            for (int f = fromFloor; f < Math.Min(toFloor, DungeonField.MaxFloor); f++)
            {
                var boss = field.GetSegmentBoss(f);
                steps.Add(new TraversalStep(f + 1, boss, f + 1 > exploredRecord, IntelSpeedMultiplier(boss), DamageTakenMultiplier(boss)));
            }
            return BuildSegments(steps, fromFloor, _ => 0);
        }

        /// <summary>
        /// 連続する歩みを「区間担当ボス」と「未踏破かどうか」が同じものごとにまとめる。
        /// basePctOf は区間の基礎損耗率（部隊平均）を返す。
        /// </summary>
        private static List<TraversalSegmentDetail> BuildSegments(List<TraversalStep> steps, int startFloor, Func<bool, double> basePctOf)
        {
            var segments = new List<TraversalSegmentDetail>();
            int from = startFloor;
            foreach (var step in steps)
            {
                var last = segments.Count > 0 ? segments[^1] : null;
                if (last != null && ReferenceEquals(last.Boss, step.Boss) && last.IsUnexplored == step.Unexplored)
                {
                    last.ToFloor = step.Floor;
                    last.Floors++;
                }
                else
                {
                    double basePct = basePctOf(step.Unexplored);
                    segments.Add(new TraversalSegmentDetail
                    {
                        FromFloor = from,
                        ToFloor = step.Floor,
                        Floors = 1,
                        Boss = step.Boss,
                        IntelRate = step.Boss?.IntelRate ?? 0.0,
                        IsUnexplored = step.Unexplored,
                        SpeedMultiplier = step.Speed,
                        DamageMultiplier = step.Damage,
                        BaseLossPct = basePct,
                    });
                }
                from = step.Floor;
            }
            return segments;
        }

        /// <summary>
        /// floor→floor+1 の区間を担当するボス。フィールドのボス一覧に加え、明示的に渡された
        /// 足止め先（旧シグネチャの nextBoss はフィールドに属さない場合がある）も候補にする。
        /// </summary>
        private static FloorBoss? SegmentBoss(DungeonField field, int floor, FloorBoss? stopper)
        {
            var segment = field.GetSegmentBoss(floor);
            if (stopper != null && stopper.Floor > floor && (segment == null || stopper.Floor < segment.Floor))
                return stopper;
            return segment;
        }

        /// <summary>
        /// 調査度連動の走破倍率＝1.0＋解析率×IntelSpeedBonusPerIntel（未調査1.0倍〜完全解析3.0倍）。
        /// 区間担当ボスがいない（最深部より先）場合は1.0倍。
        /// </summary>
        public static double IntelSpeedMultiplier(FloorBoss? segmentBoss)
        {
            double intel = Math.Clamp(segmentBoss?.IntelRate ?? 0.0, 0.0, 1.0);
            return 1.0 + intel * DungeonTraversalBalance.IntelSpeedBonusPerIntel;
        }

        /// <summary>区間担当ボスが完全解析済みならHP損耗を軽減する（→ FullIntelDamageMultiplier）。それ以外は1.0倍。</summary>
        public static double DamageTakenMultiplier(FloorBoss? segmentBoss) =>
            segmentBoss != null && ScoutingResolver.GetTier(segmentBoss.IntelRate) == IntelTier.Complete
                ? DungeonTraversalBalance.FullIntelDamageMultiplier
                : 1.0;

        /// <summary>
        /// 1階層進むたびの拾得物：ゴールドを LootGoldPerFloor、LootFloorsPerMaterial 階層ごとに
        /// その階層で採れる素材を1個。素材の選択は乱数を消費しない決定的な巡回（階層番号で回す）
        /// にしてある（HP消費の乱数列をずらさないため）。
        /// </summary>
        private static void CollectLoot(TraversalResult result, DungeonField field, int floor)
        {
            result.LootGold += DungeonTraversalBalance.LootGoldPerFloor;

            int per = DungeonTraversalBalance.LootFloorsPerMaterial;
            if (per <= 0 || floor % per != 0)
                return;

            var eligible = MaterialBalance.GetEligibleMaterials(field.Id, floor);
            if (eligible.Count == 0)
                return;

            string materialId = eligible[(floor / per) % eligible.Count].Id;
            result.LootMaterials[materialId] = result.LootMaterials.TryGetValue(materialId, out int n) ? n + 1 : 1;
        }

        /// <summary>
        /// 走破力＝Σ(VIT×WeightVit ＋ MND×WeightMnd) ＋ 部隊長LDR×WeightLdr
        /// （＋研究ボーナス＋参謀のルート指導ボーナス）。空の部隊は0
        /// （stateの有無・研究の完了状況に関わらず、部隊が空ならボーナスも乗らない）。
        ///
        /// 2026年9月改訂（→ 03 §4.5.3）：旧モデルは AGI+DEX 合算で、隠密適性
        /// （→ ScoutingResolver.CalculateStealthScore）とまったく同じ式だったため、UIの
        /// 「走破力予測」と「隠密適性」が常に同値になっていた。走破力は**悪路を踏み越える体力（VIT）と、
        /// 長い潜行に耐える気力（MND）**の指標へ切り分けてある。
        ///
        /// public static にしてあるのは出撃前のプレビュー（UI）とテストから同じ式を使うため
        /// （→ ScoutingResolver.CalculateStealthScoreと同じ考え方）。
        /// </summary>
        /// <param name="state">
        /// 省略可能。渡した場合、TraversalBonus種別の研究（軽量踏破靴・耐熱踏破法等）が
        /// 完了済みなら、完了分すべてのEffectValueを合計して走破力に加算する
        /// （→ Balance.ResearchBalance.GetTotalEffectValue、アルベールの研究室。同種の研究が
        /// 複数完了していても加算で重複できる）。DungeonPanel側の出撃前プレビューでも同じ式を
        /// 使うため、UIとResolve内部の両方でボーナスが一致する。参謀が任命されていれば、
        /// 参謀の7能力実効値平均×Advisor_TraversalPowerBonusCoeff も加算する（→ AdvisorSystem）。
        /// </param>
        public static double CalculateTraversalScore(Party party, GameState? state = null)
        {
            if (party.IsEmpty) return 0;

            double score = party.Members.Sum(GetMemberTraversalValue)
                + party.Members[0].GetEffectiveStat("LDR") * DungeonTraversalBalance.WeightLdr;

            if (state != null)
            {
                score += ResearchBalance.GetTotalEffectValue(state, ResearchEffectType.TraversalBonus);
                score += FacilityBalance.GetPathfindingTraversalBonus(state.GetFacilityLevel(FacilityType.WarRoom), state.GetFacilitySpecialty(FacilityType.WarRoom)); // 作戦資料室の踏破（§0.76）
                // 参謀のルート指導（→ AdvisorSystem.GetAdvisorTraversalPowerBonus、2026年9月再配線）。
                score += AdvisorSystem.GetAdvisorTraversalPowerBonus(state);
            }

            return score;
        }

        /// <summary>
        /// 隊員1名の走破力への寄与＝VIT×WeightVit ＋ MND×WeightMnd（部隊長LDR・研究・参謀の加算は含まない）。
        /// CalculateTraversalScore が合計し、編成画面の「走破貢献」列（→ PartyFormationPanel）も同じ値を使う。
        /// </summary>
        public static double GetMemberTraversalValue(Adventurer member) =>
            (member.GetEffectiveStat("VIT") * DungeonTraversalBalance.WeightVit
             + member.GetEffectiveStat("MND") * DungeonTraversalBalance.WeightMnd)
            * (1.0
               + (member.HasTrait(TraitCatalog.PathfinderId) ? TraitBalance.PathfinderTraversalBonus : 0)    // 健脚（§0.55）
               + (member.HasTrait(TraitCatalog.MapReaderId) ? TraitBalance.MapReaderTraversalBonus : 0)     // 地図読み（§0.56）
               + (member.HasTrait(TraitCatalog.PoorDirectionId) ? TraitBalance.PoorDirectionTraversalPenalty : 0)); // 方向音痴（§0.56）

        /// <summary>道中進軍の要求値＝現在到達階層×係数×フィールド倍率（深く潜るほど道中も険しくなる）。</summary>
        public static double CurrentFloorRequirement(DungeonField field) => FloorRequirement(field, field.ReachedFloor);

        /// <summary>
        /// その階層から1階層進む際の要求値＝階層×係数×フィールド倍率。1歩ごとにこの値で消費を決める（→ StepCost、§0.49）。
        /// 基礎値は0（→ DungeonTraversalBalance.RequirementPerFloor の注記。§0.47）。
        /// </summary>
        public static double FloorRequirement(DungeonField field, int floor) =>
            DungeonBalance.ScaleRequirement(0, DungeonTraversalBalance.RequirementPerFloor, floor, field.Order);

        /// <summary>
        /// 比率 ratio のまま1週に進める階層数＝max(1, floor(Ratio×FloorsPerRatio))。週報・見立ての
        /// 表示に使う（進軍ランクは ClassifyRatio で別に決める）。§0.49以降、実際の進軍は階層ごとの要求値で1歩ずつ数える（→ StepCost）ため、この値そのものを予算にはしない。
        /// 要求値0（Ratio＝∞）でも最深部の階層数で頭打ちにして整数へ変換する（オーバーフロー防止）。
        /// </summary>
        public static int CalculateBaseFloors(double ratio)
        {
            double raw = Math.Floor(ratio * DungeonTraversalBalance.FloorsPerRatio);
            if (double.IsNaN(raw)) return 1;
            return (int)Math.Clamp(raw, 1, DungeonField.MaxFloor);
        }

        /// <summary>
        /// 進軍ランクの段（→ ClassifyRatio）からの逆引き：1＝苦戦／2＝通常／3＝迅速／4＝電撃／5＝疾風／6以上＝神速。
        /// </summary>
        public static TraversalRank RankFromFloors(int baseFloors) => baseFloors switch
        {
            <= 1 => TraversalRank.Struggling,
            2 => TraversalRank.Normal,
            3 => TraversalRank.Swift,
            4 => TraversalRank.Lightning,
            5 => TraversalRank.Gale,
            _ => TraversalRank.Godspeed,
        };

        /// <summary>
        /// 走破力Ratioから進軍ランクを求める＝RankFromFloors(max(1, floor(Ratio×RankRatioScale)))。
        /// 進む速さ（FloorsPerRatio）とは切り離してあり、速さを変えても既踏の損耗率は変わらない（§0.49）。
        /// </summary>
        public static TraversalRank ClassifyRatio(double ratio)
        {
            double raw = Math.Floor(ratio * DungeonTraversalBalance.RankRatioScale);
            int step = double.IsNaN(raw) ? 1 : (int)Math.Clamp(raw, 1, DungeonField.MaxFloor);
            return RankFromFloors(step);
        }

        /// <summary>
        /// 道中進軍のHP消費（2026年9月改訂：階層ごとの個別積み上げ）。
        ///
        /// 各員について、基礎損耗率を2種類（必要なものだけ）乱数で決める：
        ///  - 既踏階層用：進軍ランクごとの率（→ DungeonTraversalBalance.HpLossPct*。楽に進めたほど軽い）
        ///  - 未踏破階層用：重損耗の率（→ DungeonBalance.UnexploredHpLossPct*、30〜50%）
        /// この率を歩いた階層数で均等配分し、1階層ごとに「その階層の基礎率÷歩いた階層数
        /// ×その区間の被ダメージ倍率（→ DamageTakenMultiplier、完全解析区間は0.3倍）」を合算する。
        /// したがって解析済み区間の軽減は解析済み区間の階層にしか効かず、未解析区間の重損耗を薄めない。
        ///
        /// 失うHP＝floor(最大HP×合計損耗率÷100)（浮動小数で計算し、途中の整数割り算による切り捨てはしない）。
        /// HPは下限1で止まり、致死判定・負傷状態には一切接続しない。
        /// 1階層も進めなかった場合は従来どおり、進軍ランクの率×出発区間の被ダメージ倍率を1回分受ける。
        /// </summary>
        private void ApplyHpLoss(TraversalResult result, Party party, TraversalRank rank, List<TraversalStep> steps, FloorBoss? startSegmentBoss, double miasma)
        {
            var (rankMin, rankMax) = RankHpLossRange(rank);
            bool needRank = steps.Count == 0 || steps.Any(s => !s.Unexplored);
            bool needUnexplored = steps.Any(s => s.Unexplored);

            // 夜目（→ 03 §4.5.3・§5.3.2）：部隊に保有者が1人でもいれば、未踏破階層の基礎損耗率を一律で軽減する。
            double unexploredMultiplier = UnexploredLossMultiplier(party);
            result.NightVisionApplied = needUnexplored && unexploredMultiplier < 1.0;

            // 神官の加護（§0.72）：部隊に神官がいれば、損耗率を%ポイントで下げる。
            double blessing = ClericBlessing.LossReductionPct(party.Members);
            result.ClericBlessingPct = blessing;

            double rankPctSum = 0, unexploredPctSum = 0;
            foreach (var member in party.Members)
            {
                // 必要な種類だけ乱数を引く（既踏のみ・未踏破のみの進軍は従来どおり1人1回）。
                int rankPct = needRank ? _rng.NextInt(rankMin, rankMax) : 0;
                double unexploredPct = needUnexplored
                    ? _rng.NextInt(DungeonBalance.UnexploredHpLossPctMin, DungeonBalance.UnexploredHpLossPctMax) * unexploredMultiplier
                    : 0;
                rankPctSum += rankPct;
                unexploredPctSum += unexploredPct;

                double effectivePct = steps.Count == 0
                    ? rankPct * DamageTakenMultiplier(startSegmentBoss) * miasma
                    : steps.Sum(s => (s.Unexplored ? unexploredPct : rankPct) * s.Damage) / steps.Count;
                effectivePct = Math.Max(0, effectivePct - blessing);

                int hpLoss = (int)Math.Floor(member.MaxHP * effectivePct / 100.0 + LossEpsilon);
                int newHp = Math.Max(MinHp, member.CurrentHP - hpLoss);

                result.EffectiveLossPctByAdventurer[member.Id] = effectivePct;
                result.HpLostByAdventurer[member.Id] = member.CurrentHP - newHp;
                bool fellToCritical = member.CurrentHP > CriticalInjury.CriticalHp && newHp <= CriticalInjury.CriticalHp;
                member.CurrentHP = newHp;

                // 重傷生還の古傷（→ 03 §4.3）：今回の進軍でHPが下限1まで落ちた隊員だけロールする
                // （既にHP1のまま潜行を続けている隊員を毎週ロールし直さない）。
                if (fellToCritical && CriticalInjury.RollOldWound(member, _rng) is { } grant)
                    result.TraitGrantEvents.Add(grant);
                // 軽傷（§0.53）：今回HPが下限1まで落ちた隊員は軽傷になる（既に負傷中なら重ねない）。
                if (fellToCritical && CriticalInjury.TryInflictLight(member, _rng) is { } injury)
                    result.InjuryEvents.Add(injury);
            }

            int n = party.Members.Count;
            result.RankLossPct = needRank ? rankPctSum / n : 0;
            result.UnexploredLossPct = needUnexplored ? unexploredPctSum / n : 0;
            result.EffectiveLossPct = result.EffectiveLossPctByAdventurer.Values.Average();
            result.Segments = BuildSegments(steps, result.FloorBefore,
                unexplored => unexplored ? result.UnexploredLossPct : result.RankLossPct);
        }

        /// <summary>
        /// 未踏破階層の基礎損耗率に掛かる倍率。部隊に夜目（→ TraitCatalog.NightVision）の保有者がいれば
        /// 1 − NightVisionUnexploredDamageReductionRate、いなければ1.0（出撃前プレビューも同じ値を使う）。
        /// </summary>
        public static double UnexploredLossMultiplier(Party party) =>
            party.Members.Any(m => m.HasTrait(TraitCatalog.NightVisionId))
                ? 1.0 - DungeonTraversalBalance.NightVisionUnexploredDamageReductionRate
                : 1.0;

        /// <summary>損耗率の合算で 15.0 が 14.999… になって1減る事故を防ぐ許容幅。</summary>
        private const double LossEpsilon = 1e-9;

        /// <summary>
        /// 進軍ランクごとの既踏階層のHP消費率（下限・上限、%）。疾風（5階層）・神速（6階層以上）は
        /// 電撃（4階層）と同じ率で底打ちする（2026年9月、リニア進軍モデル：速く進めても消耗はそれ以上軽くならない）。
        /// </summary>
        public static (int Min, int Max) RankHpLossRange(TraversalRank rank) => rank switch
        {
            TraversalRank.Lightning or TraversalRank.Gale or TraversalRank.Godspeed =>
                (DungeonTraversalBalance.HpLossPctMinLightning, DungeonTraversalBalance.HpLossPctMaxLightning),
            TraversalRank.Swift => (DungeonTraversalBalance.HpLossPctMinSwift, DungeonTraversalBalance.HpLossPctMaxSwift),
            TraversalRank.Normal => (DungeonTraversalBalance.HpLossPctMinNormal, DungeonTraversalBalance.HpLossPctMaxNormal),
            _ => (DungeonTraversalBalance.HpLossPctMinStruggling, DungeonTraversalBalance.HpLossPctMaxStruggling),
        };
    }
}
