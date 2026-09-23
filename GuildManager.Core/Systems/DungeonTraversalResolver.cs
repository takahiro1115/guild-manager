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
    /// で現在階層から進む。調査度連動の走破加速（→ IntelSpeedMultiplier）により、解析済みの区間ほど
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
        /// 進軍の本体。1階層ずつ「移動予算」を消費して進む：進軍ランクの階層数（→ FloorsAdvanced）を
        /// 予算とし、1階層進むごとに 1÷走破倍率（→ IntelSpeedMultiplier、その階層の区間担当ボスの
        /// 解析率で決まる）を消費する。完全解析済みの区間は1/3の予算で抜けられる＝同じ予算で3倍進む。
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
            double requirement = FloorRequirement(startFloor);
            double ratio = requirement <= 0 ? double.MaxValue : score / requirement;

            var rank = ClassifyRatio(ratio);
            result.Rank = rank;
            result.TraversalScore = score;
            result.Requirement = requirement;
            result.Ratio = ratio;

            var startSegmentBoss = SegmentBoss(field, startFloor, stopper);

            // ストッパー：次の未撃破ボス階層を超えて進むことはない（→ 案A「一度倒したボスは素通り」の裏返し。
            // 未撃破のボスだけは必ず足止めになる）。
            int limit = stopper == null ? DungeonField.MaxFloor : Math.Min(DungeonField.MaxFloor, stopper.Floor);

            double budget = FloorsAdvanced(rank);
            int floor = startFloor;
            int exploredRecord = field.ReachedFloor; // 進軍前の最高到達階層（これより深い階層が未踏破）
            var steps = new List<TraversalStep>();
            while (floor < limit)
            {
                // floor→floor+1 の1歩は、出発する階層 floor の区間担当ボスが受け持つ
                // （→ GetSegmentBoss。9→10Fは10Fボス、10→11Fは20Fボスの区間）。
                var segmentBoss = SegmentBoss(field, floor, stopper);
                double speed = IntelSpeedMultiplier(segmentBoss);
                double cost = 1.0 / speed;
                if (budget + BudgetEpsilon < cost)
                    break;

                budget -= cost;
                floor++;
                bool unexplored = floor > exploredRecord;
                if (unexplored)
                    result.EnteredUnexplored = true;
                steps.Add(new TraversalStep(floor, segmentBoss, unexplored, speed, DamageTakenMultiplier(segmentBoss)));
                CollectLoot(result, field, floor);
            }

            if (stopper != null && floor >= stopper.Floor)
            {
                result.StopperTriggered = true;
                result.TargetBoss = stopper;
            }

            result.FloorAfter = floor;
            result.UnexploredFloorsAdvanced = steps.Count(s => s.Unexplored);

            // 進軍全体の実効倍率（階層数で重み付けした平均）。1階層も進めなかった場合は出発区間の値。
            result.IntelSpeedMultiplier = steps.Count > 0 ? steps.Average(s => s.Speed) : IntelSpeedMultiplier(startSegmentBoss);
            result.DamageTakenMultiplier = steps.Count > 0 ? steps.Average(s => s.Damage) : DamageTakenMultiplier(startSegmentBoss);

            ApplyHpLoss(result, party, rank, steps, startSegmentBoss);

            return result;
        }

        /// <summary>進軍の1歩（→ ResolveFrom）。Floor は踏み入れた階層。</summary>
        private readonly record struct TraversalStep(int Floor, FloorBoss? Boss, bool Unexplored, double Speed, double Damage);

        /// <summary>浮動小数の誤差（1/3×3 等）で1階層取りこぼさないための許容幅。</summary>
        private const double BudgetEpsilon = 1e-9;

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

            double score = party.Members.Sum(m =>
                    m.GetEffectiveStat("VIT") * DungeonTraversalBalance.WeightVit
                    + m.GetEffectiveStat("MND") * DungeonTraversalBalance.WeightMnd)
                + party.Members[0].GetEffectiveStat("LDR") * DungeonTraversalBalance.WeightLdr;

            if (state != null)
            {
                score += ResearchBalance.GetTotalEffectValue(state, ResearchEffectType.TraversalBonus);
                // 参謀のルート指導（→ AdvisorSystem.GetAdvisorTraversalPowerBonus、2026年9月再配線）。
                score += AdvisorSystem.GetAdvisorTraversalPowerBonus(state);
            }

            return score;
        }

        /// <summary>道中進軍の要求値＝現在到達階層×係数（深く潜るほど道中も険しくなる）。</summary>
        public static double CurrentFloorRequirement(DungeonField field) => FloorRequirement(field.ReachedFloor);

        /// <summary>指定階層から進軍する際の要求値＝階層×係数（→ 潜行中の部隊は ActiveDungeonMission.CurrentFloor を渡す）。</summary>
        public static double FloorRequirement(int floor) => floor * DungeonTraversalBalance.RequirementPerFloor;

        /// <summary>走破力Ratioから進軍ランクの4区分を求める。</summary>
        public static TraversalRank ClassifyRatio(double ratio)
        {
            if (ratio >= DungeonTraversalBalance.RatioThresholdLightning) return TraversalRank.Lightning;
            if (ratio >= DungeonTraversalBalance.RatioThresholdSwift) return TraversalRank.Swift;
            if (ratio >= DungeonTraversalBalance.RatioThresholdNormal) return TraversalRank.Normal;
            return TraversalRank.Struggling;
        }

        /// <summary>進軍ランクごとの進軍階層数（→ DungeonTraversalBalance）。</summary>
        public static int FloorsAdvanced(TraversalRank rank) => rank switch
        {
            TraversalRank.Lightning => DungeonTraversalBalance.FloorsAdvancedLightning,
            TraversalRank.Swift => DungeonTraversalBalance.FloorsAdvancedSwift,
            TraversalRank.Normal => DungeonTraversalBalance.FloorsAdvancedNormal,
            _ => DungeonTraversalBalance.FloorsAdvancedStruggling,
        };

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
        private void ApplyHpLoss(TraversalResult result, Party party, TraversalRank rank, List<TraversalStep> steps, FloorBoss? startSegmentBoss)
        {
            var (rankMin, rankMax) = RankHpLossRange(rank);
            bool needRank = steps.Count == 0 || steps.Any(s => !s.Unexplored);
            bool needUnexplored = steps.Any(s => s.Unexplored);

            double rankPctSum = 0, unexploredPctSum = 0;
            foreach (var member in party.Members)
            {
                // 必要な種類だけ乱数を引く（既踏のみ・未踏破のみの進軍は従来どおり1人1回）。
                int rankPct = needRank ? _rng.NextInt(rankMin, rankMax) : 0;
                int unexploredPct = needUnexplored
                    ? _rng.NextInt(DungeonBalance.UnexploredHpLossPctMin, DungeonBalance.UnexploredHpLossPctMax)
                    : 0;
                rankPctSum += rankPct;
                unexploredPctSum += unexploredPct;

                double effectivePct = steps.Count == 0
                    ? rankPct * DamageTakenMultiplier(startSegmentBoss)
                    : steps.Sum(s => (s.Unexplored ? unexploredPct : rankPct) * s.Damage) / steps.Count;

                int hpLoss = (int)Math.Floor(member.MaxHP * effectivePct / 100.0 + LossEpsilon);
                int newHp = Math.Max(MinHp, member.CurrentHP - hpLoss);

                result.EffectiveLossPctByAdventurer[member.Id] = effectivePct;
                result.HpLostByAdventurer[member.Id] = member.CurrentHP - newHp;
                member.CurrentHP = newHp;
            }

            int n = party.Members.Count;
            result.RankLossPct = needRank ? rankPctSum / n : 0;
            result.UnexploredLossPct = needUnexplored ? unexploredPctSum / n : 0;
            result.EffectiveLossPct = result.EffectiveLossPctByAdventurer.Values.Average();
            result.Segments = BuildSegments(steps, result.FloorBefore,
                unexplored => unexplored ? result.UnexploredLossPct : result.RankLossPct);
        }

        /// <summary>損耗率の合算で 15.0 が 14.999… になって1減る事故を防ぐ許容幅。</summary>
        private const double LossEpsilon = 1e-9;

        /// <summary>進軍ランクごとの既踏階層のHP消費率（下限・上限、%）。</summary>
        public static (int Min, int Max) RankHpLossRange(TraversalRank rank) => rank switch
        {
            TraversalRank.Lightning => (DungeonTraversalBalance.HpLossPctMinLightning, DungeonTraversalBalance.HpLossPctMaxLightning),
            TraversalRank.Swift => (DungeonTraversalBalance.HpLossPctMinSwift, DungeonTraversalBalance.HpLossPctMaxSwift),
            TraversalRank.Normal => (DungeonTraversalBalance.HpLossPctMinNormal, DungeonTraversalBalance.HpLossPctMaxNormal),
            _ => (DungeonTraversalBalance.HpLossPctMinStruggling, DungeonTraversalBalance.HpLossPctMaxStruggling),
        };
    }
}
