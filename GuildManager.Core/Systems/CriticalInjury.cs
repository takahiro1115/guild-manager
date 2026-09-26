using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 重傷（HP1）で生還した隊員の古傷判定（→ 03 §4.3、2026年9月・§0.33）。
    /// 道中進軍（→ DungeonTraversalResolver）と階層ボス討伐の撤退（→ DungeonResolver）が共通で使う。
    /// 旧通常クエストの致死判定（不可逆障害域）は撤去済みで、大迷宮では HP0＝強制除籍のため、
    /// 古傷が付く経路はここだけ。
    /// </summary>
    public static class CriticalInjury
    {
        /// <summary>重傷とみなすHP（これ以下で生還した隊員がロール対象）。</summary>
        public const int CriticalHp = 1;

        /// <summary>
        /// 階層ボス討伐の撤退で古傷ロールの対象になるHPの上限＝max(1, floor(最大HP×OldWoundRetreatHpThresholdPct))
        /// （→ 03 §4.3.2、2026年9月・§0.34で緩和）。
        /// </summary>
        public static int RetreatHpThreshold(Adventurer adventurer) =>
            System.Math.Max(CriticalHp, (int)System.Math.Floor(adventurer.MaxHP * CombatBalance.OldWoundRetreatHpThresholdPct));

        /// <summary>
        /// 古傷のロール。HPが hpThreshold（既定＝CriticalHp＝1）より上、既に古傷持ち、のいずれかならロールせず null。
        /// 当選すれば古傷を付け（5枠満杯なら通常特性を侵食、→ Adventurer.TryAddCurseTrait）、その出来事を返す。
        /// 乱数は NextInt(1, 100) を1回だけ引き、round(OldWoundCriticalChance×100) 以下で当選。
        /// </summary>
        public static TraitGrantEvent? RollOldWound(Adventurer adventurer, IRng rng, int hpThreshold = CriticalHp)
        {
            if (adventurer.CurrentHP > hpThreshold) return null;
            if (adventurer.HasTrait(TraitCatalog.OldWoundId)) return null;

            int threshold = (int)System.Math.Round(CombatBalance.OldWoundCriticalChance * 100);
            if (rng.NextInt(1, 100) > threshold) return null;

            if (!adventurer.TryAddCurseTrait(TraitCatalog.OldWoundId, out var eroded)) return null;
            return new TraitGrantEvent(adventurer.Id, adventurer.Name, TraitCatalog.OldWoundId, eroded, TraitGrantCause.CriticalInjury);
        }
    }
}
