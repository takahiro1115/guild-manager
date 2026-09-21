using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// パーティ携行アイテム（消耗品）の静的カタログ。「パーティ携行アイテム」刷新仕様参照。
    /// ポーチは2枠・使い切りで、階層ボス討伐への出撃時に代金を徴収し、解決時に消費される
    /// （→ Party.ConsumableItemIds、DungeonExpeditionSystem.TryDispatch）。
    /// Priceは docs/04_バランス表/consumables.csv 由来（→ ConsumableBalance）。
    ///
    /// **大迷宮のボスギミック4種に1対1で対応する4アイテムのみ**を持つ（→ 03 §0.13・§4.5.4）。
    /// 旧・通常クエストの環境ギミック相殺アイテム（聖水・松明・登攀具）と効果アイテム
    /// （煙幕弾・高品質傷薬・携帯糧食）は、参照元を失っていたため2026年9月に撤去した。
    /// </summary>
    public static class ConsumableCatalog
    {
        public const string AntidoteId = "Antidote";
        public const string AcidFlaskId = "AcidFlask";
        public const string NetId = "Net";
        public const string CharmId = "Charm";

        /// <summary>解毒薬：猛毒(Poison)ギミックの対策アイテム。</summary>
        public static readonly ConsumableItem Antidote = new ConsumableItem
        {
            Id = AntidoteId, Name = "解毒薬", EffectType = ConsumableEffectType.GimmickCounter,
            TargetGimmick = BossGimmickType.Poison, Price = ConsumableBalance.AntidotePrice,
        };

        /// <summary>溶解液：重装甲(HeavyArmor)ギミックの対策アイテム（2026年9月新設）。</summary>
        public static readonly ConsumableItem AcidFlask = new ConsumableItem
        {
            Id = AcidFlaskId, Name = "溶解液", EffectType = ConsumableEffectType.GimmickCounter,
            TargetGimmick = BossGimmickType.HeavyArmor, Price = ConsumableBalance.AcidFlaskPrice,
        };

        /// <summary>捕縛網：飛行(Flying)ギミックの対策アイテム（2026年9月新設）。</summary>
        public static readonly ConsumableItem Net = new ConsumableItem
        {
            Id = NetId, Name = "捕縛網", EffectType = ConsumableEffectType.GimmickCounter,
            TargetGimmick = BossGimmickType.Flying, Price = ConsumableBalance.NetPrice,
        };

        /// <summary>身代わりの護符：即死級(InstantKill)ギミックの対策アイテム。</summary>
        public static readonly ConsumableItem Charm = new ConsumableItem
        {
            Id = CharmId, Name = "身代わりの護符", EffectType = ConsumableEffectType.GimmickCounter,
            TargetGimmick = BossGimmickType.InstantKill, Price = ConsumableBalance.CharmPrice,
        };

        private static readonly ConsumableItem[] All = { Antidote, AcidFlask, Net, Charm };

        public static ConsumableItem? FindById(string? id) => id == null ? null : All.FirstOrDefault(i => i.Id == id);

        /// <summary>カタログ全件（購入UI・ポーチ選択の一覧表示用）。</summary>
        public static IReadOnlyList<ConsumableItem> GetAll() => All;
    }
}
