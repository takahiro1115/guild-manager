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
    /// メンバー1名分の火力＝(Σ 実効ステータス×重み ＋ 装備の個人CPボーナス) × 配置補正 × 現在HP/最大HP。
    /// 重みは dungeon.csv の BossPowerWeight_*（→ DungeonBalance.BossPowerWeights）。
    /// public static にしてあるのは、出撃前のプレビュー（UI）とテストから同じ式を使うため。
    /// </summary>
    public static class DungeonPowerCalculator
    {
        /// <summary>メンバー1名分の火力。負傷（HP減少）に比例して効率が落ちる。</summary>
        public static double MemberPower(Adventurer a)
        {
            double hpRatio = (double)a.CurrentHP / a.MaxHP;

            double statSum = 0;
            foreach (var (stat, weight) in DungeonBalance.BossPowerWeights)
                statSum += a.GetEffectiveStat(stat) * weight;

            double baseScore = statSum + a.GetEquipmentBonus(EquipmentEffectType.PersonalCpBonus);
            return baseScore * PlacementBalance.GetPersonalCpCorrection(a.JobClass, a.Placement) * hpRatio;
        }

        /// <summary>部隊全員分の火力合計（完全解析ボーナスは含まない。→ DungeonResolver が上乗せする）。</summary>
        public static double PartyPower(IEnumerable<Adventurer> members) => members.Sum(MemberPower);
    }
}
