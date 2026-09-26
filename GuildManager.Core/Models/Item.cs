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

        /// <summary>
        /// 最大HPへの固定加算（→ BAL: equipment.csv `HpBonus`、Adventurer.MaxHP）。防具・HP系アクセサリーのみ正、武器は0。
        /// 2026年9月・§0.37：旧 EffectType（個人CP／最大HPの二択）・EffectValue は個人CPの撤廃に伴い廃止し、
        /// 武具の強さは最大HP加算と能力値補正（StatBonuses）の2つだけになった。
        /// </summary>
        public int MaxHpBonus { get; set; }

        /// <summary>装備可能な職業の一覧。空リスト＝全職業装備可（職業制限なし）。</summary>
        public List<JobClass> AllowedJobs { get; set; } = new();

        public int Price { get; set; }

        /// <summary>
        /// 7大能力値への固定加算（→ 03 §4.2.2、2026年9月新設）。キーは "STR","VIT","AGI","DEX","INT","MND","LDR"
        /// （→ Adventurer.GetEffectiveStat）。値は equipment.csv の Bonus* 列由来（→ EquipmentBalance）。
        /// 空＝補正なし。
        /// </summary>
        public IReadOnlyDictionary<string, int> StatBonuses { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// 重装防具か（→ ScoutingResolver.CountHeavyMembers：隠密の重装ペナルティ対象）。
        /// 重装鎧・全身板金鎧のみtrue。鎖帷子はfalse（中装扱い）。
        /// </summary>
        public bool IsHeavyArmor { get; set; }

        /// <summary>指定した能力値への補正値（未設定なら0）。</summary>
        public int GetStatBonus(string statName) => StatBonuses.TryGetValue(statName, out int v) ? v : 0;

        /// <summary>能力値補正を表示する順序（→ DescribeEffects）。</summary>
        private static readonly string[] StatDisplayOrder = { "STR", "VIT", "AGI", "DEX", "INT", "MND", "LDR" };

        /// <summary>
        /// 効果の短い説明（UIの装備欄・保管庫・装備ダイアログ共通）。例：「最大HP+15・AGI+2」「STR+6・VIT+2」。
        /// 補正が1つも無ければ「効果なし」。
        /// </summary>
        public string DescribeEffects()
        {
            var parts = new List<string>();
            if (MaxHpBonus != 0) parts.Add($"最大HP{(MaxHpBonus > 0 ? "+" : "")}{MaxHpBonus}");
            foreach (var stat in StatDisplayOrder)
            {
                int bonus = GetStatBonus(stat);
                if (bonus != 0) parts.Add($"{stat}{(bonus > 0 ? "+" : "")}{bonus}");
            }
            return parts.Count == 0 ? "効果なし" : string.Join("・", parts);
        }

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
