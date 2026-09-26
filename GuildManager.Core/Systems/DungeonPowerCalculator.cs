using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 階層ボス討伐の部隊火力（→ DungeonResolver）の計算。旧通常クエストの点数計算
    /// （QuestScoreCalculator.MemberScore の「討伐」種別）を、旧クエスト撤去（2026年9月）に伴い
    /// 大迷宮側へ同じ式・同じ数値のまま移設したもの。
    ///
    /// 隊員1名分の火力＝(Σ 実効ステータス×重み) × 現在HP/最大HP（巨獣狩りの上乗せは下記）。
    /// 実効ステータスは素の値×特性補正＋装備の能力値補正（→ Adventurer.GetEffectiveStat）なので、武具は能力値補正を
    /// 通してのみ火力に効く。部隊火力＝全員の火力の単純合算（完全解析なら DungeonResolver が+20%を上乗せする）。
    /// 2026年9月・§0.37：旧「個人CP」の中間概念と、武具の「個人CPボーナス」（重みを経ずに火力へ直接足していた固定値）を撤廃した。
    /// 2026年9月・§0.23：職業×配置の補正（前衛職の前衛1.2倍等）を撤廃した。配置は職業で一意に決まるため、
    /// 実質「職業ごとの固定倍率」になっていた（→ 03 §4.2・§4.5.4・§0.23）。前衛／後衛は火力に影響しない。
    /// 重みは dungeon.csv の BossPowerWeight_*（→ DungeonBalance.BossPowerWeights）。
    /// public static にしてあるのは、出撃前のプレビュー（UI）とテストから同じ式を使うため。
    /// </summary>
    public static class DungeonPowerCalculator
    {
        /// <summary>
        /// メンバー1名分の火力。負傷（HP減少）に比例して効率が落ちる。
        /// boss を渡し、そのボスが「重装甲」ギミックを持つ場合、巨獣狩り（→ TraitCatalog.GiantHunter）の保有者は
        /// 火力が GiantHunterDamageBonusRate だけ上乗せされる（→ 03 §4.5.4・§5.3.2。HP比率の前に掛ける）。
        /// boss を省略した場合（ボスを特定しない試算）は上乗せしない。
        /// </summary>
        public static double MemberPower(Adventurer a, FloorBoss? boss = null)
        {
            double hpRatio = (double)a.CurrentHP / a.MaxHP;

            double statSum = 0;
            foreach (var (stat, weight) in DungeonBalance.BossPowerWeights)
                statSum += a.GetEffectiveStat(stat) * weight;

            if (GiantHunterApplies(a, boss))
                statSum *= 1.0 + CombatBalance.GiantHunterDamageBonusRate;
            return statSum * hpRatio;
        }

        /// <summary>巨獣狩りの上乗せが効くか：本人が保有し、かつボスが「重装甲」ギミックを持つ。</summary>
        public static bool GiantHunterApplies(Adventurer a, FloorBoss? boss) =>
            boss != null
            && a.HasTrait(TraitCatalog.GiantHunterId)
            && boss.Gimmicks.Any(g => g.Type == BossGimmickType.HeavyArmor);

        /// <summary>
        /// 部隊全員分の火力合計（完全解析ボーナスは含まない。→ DungeonResolver が上乗せする）。
        /// boss を渡すと巨獣狩りの上乗せ（→ MemberPower）が効く。
        /// </summary>
        public static double PartyPower(IEnumerable<Adventurer> members, FloorBoss? boss = null) =>
            members.Sum(m => MemberPower(m, boss));
    }
}
