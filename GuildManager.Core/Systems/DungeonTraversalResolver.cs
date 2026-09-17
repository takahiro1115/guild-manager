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
            if (party.Members.Count == 0)
                throw new InvalidOperationException("空のパーティは道中進軍に出せません。");

            var result = new TraversalResult { FloorBefore = field.ReachedFloor };

            double score = CalculateTraversalScore(party, state);
            double requirement = CurrentFloorRequirement(field);
            double ratio = requirement <= 0 ? double.MaxValue : score / requirement;

            var rank = ClassifyRatio(ratio);
            result.Rank = rank;

            int advanced = FloorsAdvanced(rank);
            int target = Math.Min(DungeonField.MaxFloor, field.ReachedFloor + advanced);

            // ストッパー：次の未撃破ボス階層を超えて進むことはない（→ 案A「一度倒したボスは素通り」の裏返し。
            // 未撃破のボスだけは必ず足止めになる）。
            if (target >= nextBoss.Floor)
            {
                target = nextBoss.Floor;
                result.StopperTriggered = true;
                result.TargetBoss = nextBoss;
            }

            field.ReachedFloor = target;
            result.FloorAfter = target;

            ApplyHpLoss(result, party, rank);

            return result;
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
        public static double CurrentFloorRequirement(DungeonField field) =>
            field.ReachedFloor * DungeonTraversalBalance.RequirementPerFloor;

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
        /// </summary>
        private void ApplyHpLoss(TraversalResult result, Party party, TraversalRank rank)
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
                int hpLoss = member.MaxHP * lossPct / 100;
                int newHp = Math.Max(MinHp, member.CurrentHP - hpLoss);

                result.HpLostByAdventurer[member.Id] = member.CurrentHP - newHp;
                member.CurrentHP = newHp;
            }
        }
    }
}
