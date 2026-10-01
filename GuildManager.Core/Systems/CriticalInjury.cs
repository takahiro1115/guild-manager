using System.Linq;
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
        /// 古傷のロール。HPが hpThreshold（既定＝CriticalHp＝1）より上ならロールせず null。
        /// 当選すれば古傷を付け（5枠満杯なら通常特性を侵食、→ Adventurer.TryAddCurseTrait）、その出来事を返す。
        /// 乱数は NextInt(1, 100) を引き、round(OldWoundCriticalChance×100) 以下で当選。
        /// 2026年10月・§0.56：当選したら部位（古傷・足の古傷・腕の古傷、→ TraitCatalog.OldWoundVariantIds）を、まだ持っていない
        /// ものから等確率で選ぶ（NextInt(0, 候補数−1) をもう1回引く。候補が1つなら引かない）。3つとも持っていればロールしない。
        /// </summary>
        public static TraitGrantEvent? RollOldWound(Adventurer adventurer, IRng rng, int hpThreshold = CriticalHp)
        {
            if (adventurer.CurrentHP > hpThreshold) return null;
            var candidates = TraitCatalog.OldWoundVariantIds.Where(id => !adventurer.HasTrait(id)).ToList();
            if (candidates.Count == 0) return null;

            int threshold = (int)System.Math.Round(CombatBalance.OldWoundCriticalChance * 100);
            if (rng.NextInt(1, 100) > threshold) return null;

            string traitId = candidates.Count == 1 ? candidates[0] : candidates[rng.NextInt(0, candidates.Count - 1)];
            if (!adventurer.TryAddCurseTrait(traitId, out var eroded)) return null;
            return new TraitGrantEvent(adventurer.Id, adventurer.Name, traitId, eroded, TraitGrantCause.CriticalInjury);
        }

        // ---------------- 負傷（→ 03 §4.3、2026年10月・§0.53で大迷宮へ復活） ----------------

        /// <summary>
        /// 階層ボス戦の後の重傷判定。生き残った（HP1以上）隊員のHPが最大HP×SevereInjuryHpThresholdPct 未満なら重傷
        /// （出撃不可・全治 SevereInjuryWeeksMin〜Max 週、→ InjuryRecoverySystem）にする。軽傷中なら重傷へ上書きする。
        /// 乱数は重傷になる場合だけ NextInt(Min, Max) を1回引く。重傷にならなければ null。
        /// </summary>
        public static InjuryEvent? TryInflictSevere(Adventurer adventurer, IRng rng)
        {
            if (adventurer.CurrentHP <= 0) return null; // HP0は強制除籍（→ DungeonResolver）
            if (adventurer.CurrentHP >= adventurer.MaxHP * CombatBalance.SevereInjuryHpThresholdPct) return null;
            if (adventurer.Injury == InjurySeverity.Severe) return null;

            int weeks = rng.NextInt(CombatBalance.SevereInjuryWeeksMin, CombatBalance.SevereInjuryWeeksMax);
            adventurer.Injury = InjurySeverity.Severe;
            adventurer.InjuryWeeksRemaining = weeks;
            return new InjuryEvent(adventurer.Id, adventurer.Name, InjurySeverity.Severe, weeks);
        }

        /// <summary>
        /// 道中進軍・迷宮調査・探索（採取）でHPが下限1まで落ちた隊員の軽傷判定（呼び出し側が「今回1まで落ちた」ことを確かめて呼ぶ）。
        /// 負傷していなければ軽傷（出撃は可能だが、治るまで実効能力値が LightInjuryStatPenaltyRate だけ下がる、→ Adventurer.GetEffectiveStat）
        /// にする。既に軽傷・重傷なら重ねない（null）。乱数は軽傷になる場合だけ NextInt(Min, Max) を1回引く。
        /// </summary>
        public static InjuryEvent? TryInflictLight(Adventurer adventurer, IRng rng)
        {
            if (adventurer.Injury != InjurySeverity.None) return null;

            int weeks = rng.NextInt(CombatBalance.LightInjuryWeeksMin, CombatBalance.LightInjuryWeeksMax);
            adventurer.Injury = InjurySeverity.Light;
            adventurer.InjuryWeeksRemaining = weeks;
            return new InjuryEvent(adventurer.Id, adventurer.Name, InjurySeverity.Light, weeks);
        }
    }
}
