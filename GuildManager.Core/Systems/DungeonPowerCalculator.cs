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
    /// メンバー1名分の火力（個人CP）＝(Σ 実効ステータス×重み ＋ 装備の個人CPボーナス) × 現在HP/最大HP。
    /// 部隊火力＝全員の個人CPの単純合算（完全解析なら DungeonResolver が+20%を上乗せする）。
    /// 2026年9月：職業×配置の個人CP補正（前衛職の前衛1.2倍等）を撤廃した。配置は職業で一意に決まるため、
    /// 実質「職業ごとの固定倍率」になっていた（→ 03 §4.2・§4.5.4・§0.23）。前衛／後衛は火力に影響しない。
    /// 重みは dungeon.csv の BossPowerWeight_*（→ DungeonBalance.BossPowerWeights）。
    /// public static にしてあるのは、出撃前のプレビュー（UI）とテストから同じ式を使うため。
    /// </summary>
    public static class DungeonPowerCalculator
    {
        /// <summary>
        /// メンバー1名分の火力。負傷（HP減少）に比例して効率が落ちる。
        /// boss を渡し、そのボスが「重装甲」ギミックを持つ場合、巨獣狩り（→ TraitCatalog.GiantHunter）の保有者は
        /// 個人CPが GiantHunterDamageBonusRate だけ上乗せされる（→ 03 §4.5.4・§5.3.2。HP比率の前に掛ける）。
        /// boss を省略した場合（ボスを特定しない試算）は上乗せしない。
        /// </summary>
        public static double MemberPower(Adventurer a, FloorBoss? boss = null)
        {
            double hpRatio = (double)a.CurrentHP / a.MaxHP;

            double statSum = 0;
            foreach (var (stat, weight) in DungeonBalance.BossPowerWeights)
                statSum += a.GetEffectiveStat(stat) * weight;

            double baseScore = statSum + a.GetEquipmentBonus(EquipmentEffectType.PersonalCpBonus);
            if (GiantHunterApplies(a, boss))
                baseScore *= 1.0 + CombatBalance.GiantHunterDamageBonusRate;
            return baseScore * hpRatio;
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
