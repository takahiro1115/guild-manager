using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// パーティ携行アイテム（消耗品）の静的カタログ。「パーティ携行アイテム」刷新仕様参照。
    /// ポーチは2枠・使い切りで、出撃解決時に一括消費される（→ Party.ConsumableItemIds、
    /// QuestResolver.Resolve）。Price・EffectValueは docs/04_バランス表/consumables.csv 由来
    /// （→ ConsumableBalance）。
    /// </summary>
    public static class ConsumableCatalog
    {
        // ---- ギミック相殺（→ GimmickBalance.GetCounterItemId・GimmickEvaluator） ----
        public const string AntidoteId = "Antidote";
        public const string HolyWaterId = "HolyWater";
        public const string TorchId = "Torch";
        public const string ClimbingGearId = "ClimbingGear";
        public const string CharmId = "Charm";

        // ---- 効果アイテム3種 ----
        public const string SmokeBombId = "SmokeBomb";
        public const string QualityHealingSalveId = "QualityHealingSalve";
        public const string TravelRationsId = "TravelRations";

        public static readonly ConsumableItem Antidote = new ConsumableItem
        {
            Id = AntidoteId, Name = "解毒薬", EffectType = ConsumableEffectType.GimmickCounter,
            CounterTag = EnvironmentTag.Miasma, Price = ConsumableBalance.AntidotePrice,
        };

        public static readonly ConsumableItem HolyWater = new ConsumableItem
        {
            Id = HolyWaterId, Name = "聖水", EffectType = ConsumableEffectType.GimmickCounter,
            CounterTag = EnvironmentTag.Undead, Price = ConsumableBalance.HolyWaterPrice,
        };

        public static readonly ConsumableItem Torch = new ConsumableItem
        {
            Id = TorchId, Name = "松明", EffectType = ConsumableEffectType.GimmickCounter,
            CounterTag = EnvironmentTag.Darkness, Price = ConsumableBalance.TorchPrice,
        };

        public static readonly ConsumableItem ClimbingGear = new ConsumableItem
        {
            Id = ClimbingGearId, Name = "登攀具", EffectType = ConsumableEffectType.GimmickCounter,
            CounterTag = EnvironmentTag.NarrowPath, Price = ConsumableBalance.ClimbingGearPrice,
        };

        public static readonly ConsumableItem Charm = new ConsumableItem
        {
            Id = CharmId, Name = "護符", EffectType = ConsumableEffectType.GimmickCounter,
            CounterTag = EnvironmentTag.Colossal, Price = ConsumableBalance.CharmPrice,
        };

        public static readonly ConsumableItem SmokeBomb = new ConsumableItem
        {
            Id = SmokeBombId, Name = "煙幕弾", EffectType = ConsumableEffectType.DownRateHalving,
            EffectValue = ConsumableBalance.SmokeBombDownRateMultiplier, Price = ConsumableBalance.SmokeBombPrice,
        };

        public static readonly ConsumableItem QualityHealingSalve = new ConsumableItem
        {
            Id = QualityHealingSalveId, Name = "高品質傷薬", EffectType = ConsumableEffectType.DamageReduction,
            EffectValue = ConsumableBalance.QualityHealingSalveDamageReductionPct, Price = ConsumableBalance.QualityHealingSalvePrice,
        };

        public static readonly ConsumableItem TravelRations = new ConsumableItem
        {
            Id = TravelRationsId, Name = "携帯糧食", EffectType = ConsumableEffectType.SatisfactionBonus,
            EffectValue = ConsumableBalance.TravelRationsSatisfactionBonus, Price = ConsumableBalance.TravelRationsPrice,
        };

        private static readonly ConsumableItem[] All =
        {
            Antidote, HolyWater, Torch, ClimbingGear, Charm,
            SmokeBomb, QualityHealingSalve, TravelRations,
        };

        public static ConsumableItem? FindById(string? id) => id == null ? null : All.FirstOrDefault(i => i.Id == id);

        /// <summary>カタログ全件（購入UIの一覧表示用）。</summary>
        public static IReadOnlyList<ConsumableItem> GetAll() => All;
    }
}
