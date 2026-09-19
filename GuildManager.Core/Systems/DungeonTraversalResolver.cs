using System;
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
    /// 少ない予算で抜けられ（完全解析で3倍速）、完全解析済みの区間ではHP損耗も軽減される
    /// （→ DamageTakenMultiplier）。進んだ階層に応じて素材・ゴールドを拾う（→ TraversalResult.LootGold等）。
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
        /// 被ダメージ倍率（→ DamageTakenMultiplier）も歩いた階層ごとに区間から引き、平均を適用する。
        /// </summary>
        private TraversalResult ResolveFrom(Party party, DungeonField field, int startFloor, FloorBoss? stopper, GameState? state)
        {
            if (party.Members.Count == 0)
                throw new InvalidOperationException("空のパーティは道中進軍に出せません。");

            var result = new TraversalResult { FloorBefore = startFloor };

            double score = CalculateTraversalScore(party, state);
            double requirement = FloorRequirement(startFloor);
            double ratio = requirement <= 0 ? double.MaxValue : score / requirement;

            var rank = ClassifyRatio(ratio);
            result.Rank = rank;

            var startSegmentBoss = SegmentBoss(field, startFloor, stopper);
            result.IntelSpeedMultiplier = IntelSpeedMultiplier(startSegmentBoss);

            // ストッパー：次の未撃破ボス階層を超えて進むことはない（→ 案A「一度倒したボスは素通り」の裏返し。
            // 未撃破のボスだけは必ず足止めになる）。
            int limit = stopper == null ? DungeonField.MaxFloor : Math.Min(DungeonField.MaxFloor, stopper.Floor);

            double budget = FloorsAdvanced(rank);
            int floor = startFloor;
            int walked = 0;
            double damageSum = 0;
            while (floor < limit)
            {
                var segmentBoss = SegmentBoss(field, floor, stopper);
                double cost = 1.0 / IntelSpeedMultiplier(segmentBoss);
                if (budget + BudgetEpsilon < cost)
                    break;

                budget -= cost;
                floor++;
                walked++;
                damageSum += DamageTakenMultiplier(segmentBoss);
                CollectLoot(result, field, floor);
            }

            if (stopper != null && floor >= stopper.Floor)
            {
                result.StopperTriggered = true;
                result.TargetBoss = stopper;
            }

            result.FloorAfter = floor;
            result.DamageTakenMultiplier = walked > 0 ? damageSum / walked : DamageTakenMultiplier(startSegmentBoss);

            ApplyHpLoss(result, party, rank, result.DamageTakenMultiplier);

            return result;
        }

        /// <summary>浮動小数の誤差（1/3×3 等）で1階層取りこぼさないための許容幅。</summary>
        private const double BudgetEpsilon = 1e-9;

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
        /// 走破力＝Σ(AGI+DEX)×係数 ＋ 部隊長LDR×係数（＋研究ボーナス）。空の部隊は0
        /// （stateの有無・研究の完了状況に関わらず、部隊が空ならボーナスも乗らない）。
        /// public static にしてあるのは出撃前のプレビュー（UI）とテストから同じ式を使うため
        /// （→ ScoutingResolver.CalculateStealthScoreと同じ考え方）。
        /// </summary>
        /// <param name="state">
        /// 省略可能。渡した場合、TraversalBonus種別の研究（軽量踏破靴・耐熱踏破法等）が
        /// 完了済みなら、完了分すべてのEffectValueを合計して走破力に加算する
        /// （→ Balance.ResearchBalance.GetTotalEffectValue、アルベールの研究室。同種の研究が
        /// 複数完了していても加算で重複できる）。DungeonPanel側の出撃前プレビューでも同じ式を
        /// 使うため、UIとResolve内部の両方でボーナスが一致する。
        /// </param>
        public static double CalculateTraversalScore(Party party, GameState? state = null)
        {
            if (party.IsEmpty) return 0;

            double score = party.Members.Sum(m => m.GetEffectiveStat("AGI") + m.GetEffectiveStat("DEX"))
                * DungeonTraversalBalance.StatCoefficient
                + party.Members[0].GetEffectiveStat("LDR") * DungeonTraversalBalance.LeaderCoefficient;

            if (state != null)
                score += ResearchBalance.GetTotalEffectValue(state, ResearchEffectType.TraversalBonus);

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
        /// 道中進軍のHP消費。ランクが高い（楽に進めた）ほど軽く、苦戦進軍ほど重くなる。
        /// HPは下限1で止まり、致死判定・負傷状態には一切接続しない。
        /// damageMultiplier は調査度連動の被ダメージ軽減（→ DamageTakenMultiplier）。
        /// </summary>
        private void ApplyHpLoss(TraversalResult result, Party party, TraversalRank rank, double damageMultiplier)
        {
            var (minPct, maxPct) = rank switch
            {
                TraversalRank.Lightning => (DungeonTraversalBalance.HpLossPctMinLightning, DungeonTraversalBalance.HpLossPctMaxLightning),
                TraversalRank.Swift => (DungeonTraversalBalance.HpLossPctMinSwift, DungeonTraversalBalance.HpLossPctMaxSwift),
                TraversalRank.Normal => (DungeonTraversalBalance.HpLossPctMinNormal, DungeonTraversalBalance.HpLossPctMaxNormal),
                _ => (DungeonTraversalBalance.HpLossPctMinStruggling, DungeonTraversalBalance.HpLossPctMaxStruggling),
            };

            foreach (var member in party.Members)
            {
                int lossPct = _rng.NextInt(minPct, maxPct);
                int hpLoss = (int)(member.MaxHP * lossPct / 100 * damageMultiplier);
                int newHp = Math.Max(MinHp, member.CurrentHP - hpLoss);

                result.HpLostByAdventurer[member.Id] = member.CurrentHP - newHp;
                member.CurrentHP = newHp;
            }
        }
    }
}
