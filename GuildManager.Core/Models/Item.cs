using System.Collections.Generic;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// 装備アイテムの定義（カタログ）。仕様書 03 §4.2.2 参照。
    /// 個体ごとにパラメータが変わらない前提の静的定義。冒険者は装備中のアイテムIdを
    /// スロットごとに保持する（→ Adventurer.EquippedWeaponId 等）。TraitDefinitionと
    /// 同じパターン（→ TraitCatalog）。
    /// </summary>
    public class Item
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public EquipmentSlot Slot { get; set; }
        public EquipmentEffectType EffectType { get; set; }

        /// <summary>固定加算量（→ BAL: 装備）。武器・防具は常にこの値がそれぞれ個人CP・最大HPに加算される。</summary>
        public int EffectValue { get; set; }

        /// <summary>装備可能な職業の一覧。空リスト＝全職業装備可（職業制限なし）。</summary>
        public List<JobClass> AllowedJobs { get; set; } = new();

        public int Price { get; set; }

        /// <summary>
        /// 見た目切替用の識別子（→ 03 §4.2.2「見た目との連動」）。武器・防具・アクセサリー1のみ使用。
        /// 立ち絵システム（AdventurerVisualData）との連携は、本リポジトリに当該システムが
        /// まだ存在しないため未接続（→ §11。データとしてのみ保持する）。
        /// </summary>
        public string? VisualPartId { get; set; }

        /// <summary>指定した職業が装備可能かどうか（AllowedJobsが空なら常にtrue）。</summary>
        public bool IsAllowedFor(JobClass jobClass) => AllowedJobs.Count == 0 || AllowedJobs.Contains(jobClass);
    }
}
