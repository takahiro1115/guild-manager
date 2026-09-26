using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using GuildManager.Core.Balance;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// ギルド保管庫（<see cref="GameState.Armory"/>）に積まれている装備品の「個体」。
    ///
    /// 既存の <see cref="Item"/> は「カタログ定義」（Id・効果・価格が固定の静的データ、→ ItemCatalog）
    /// であり、同じ鉄の剣を2本持っている状態を表現できない。鑑定（→ Systems.AppraisalSystem）で
    /// 武具が出るようになったことで、ギルドが「まだ誰にも装備させていない現物」を複数抱える状態が
    /// 生まれるため、カタログIdを指す個体としてこのクラスを新設した。
    ///
    /// 2026年9月改訂（保管庫からの着脱、→ §4.2.2）以降は、冒険者の装備枠が持つのも本クラスの
    /// 個体になった（→ Adventurer.EquippedWeapon 等）。「保管庫にある＝誰も装備していない」
    /// という不変条件を EquipmentSystem が保ち、同じ個体が保管庫と冒険者の両方に現れることはない。
    /// セーブはプリミティブのみで構成されているためそのままJSON化できる（→ SaveData.Armory）。
    /// </summary>
    public class EquipmentItem
    {
        /// <summary>個体ごとの識別子（保管庫内で一意）。同じカタログIdの武具を複数持てるようにするため。</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>カタログ上のアイテムId（→ ItemCatalog.FindById）。</summary>
        public string ItemId { get; set; } = "";

        /// <summary>
        /// 表示名。カタログ由来のスナップショットを保持する（カタログから引けなくなった
        /// 旧セーブの装備でも、名前だけは一覧に出せるようにするための防御的な二重持ち）。
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 入手した週（→ GameState.WeekNumber）。0＝不明（旧セーブ）。保管庫一覧の並べ替え・
        /// 「いつ掘り出した物か」の表示に使う。
        /// </summary>
        public int AcquiredAtWeek { get; set; }

        /// <summary>入手経路の短い説明（例：「森 第12層の遺物を鑑定」）。演出・履歴表示用。</summary>
        public string AcquiredFrom { get; set; } = "";

        /// <summary>
        /// 鑑定で出土した個体の希少度（→ Systems.AppraisalSystem、03 §4.7）。
        /// **null＝カタログ品（無銘の汎用武具）**＝カタログから購入した個体、および
        /// 本フィールド追加前の旧セーブの個体。
        ///
        /// 売却額の系統（→ 03 §4.8、EquipmentSystem.GetSellPrice）と、保管庫UIでの
        /// 扱い（汎用武具はまとめ売り、金・虹は誤売却防止のため1行ずつ表示）を分ける鍵になる。
        /// 装備効果そのものには影響しない（効果はカタログ定義が持つ。→ GetDefinition）。
        /// </summary>
        public ItemRarity? Rarity { get; set; }

        // ---- ランダムアフィックス（→ 03 §4.7・§4.2.2、2026年9月・§0.39） ----
        // 鑑定で出土した個体にだけ付く（カタログ品・旧セーブの個体はすべて null / 0 / 空のまま）。
        // 補正の正本は AffixStatBonuses・AffixHpBonus（付与した時点の値を個体に焼き付ける）。
        // affixes.csv の TargetStat や範囲を後から直しても、既に掘り出した個体の強さは変わらない。
        // Id と PrefixValue／SuffixValue は名前と効果説明の表示用（→ DisplayName・DescribeEffects）。
        // 旧セーブにはキー自体が無いが、System.Text.Json は初期化子の値のまま復元する（→ 03 §12）。

        /// <summary>接頭辞のアフィックスId（→ affixes.csv）。null＝接頭辞なし。</summary>
        public string? PrefixId { get; set; }

        /// <summary>接頭辞でロールされた補正値（表示用。効果は AffixStatBonuses／AffixHpBonus に合算済み）。</summary>
        public int PrefixValue { get; set; }

        /// <summary>接尾辞のアフィックスId（→ affixes.csv）。null＝接尾辞なし。</summary>
        public string? SuffixId { get; set; }

        /// <summary>接尾辞でロールされた補正値（表示用）。</summary>
        public int SuffixValue { get; set; }

        /// <summary>
        /// アフィックスによる7大能力値への補正の合計（キーは "STR"〜"LDR"、→ Item.StatBonuses と同じ表記）。
        /// 接頭辞と接尾辞が同じ能力値なら合算される。Adventurer.GetEquipmentStatBonus が加算する。
        /// </summary>
        public Dictionary<string, int> AffixStatBonuses { get; set; } = new();

        /// <summary>アフィックスによる最大HPへの加算の合計（→ Adventurer.GetEquipmentHpBonus）。</summary>
        public int AffixHpBonus { get; set; }

        /// <summary>接頭辞・接尾辞のいずれかを持つか。</summary>
        [JsonIgnore]
        public bool HasAffix => !string.IsNullOrEmpty(PrefixId) || !string.IsNullOrEmpty(SuffixId);

        /// <summary>
        /// アフィックス込みの表示名：「{接頭辞}{カタログ名}{接尾辞}」（例：剛力の鉄の剣［巨躯］・剛力の鉄の剣・鉄の剣［巨躯］）。
        /// アフィックスが無ければカタログ名（カタログから引けない旧データはスナップショットの Name）。
        /// affixes.csv から消えたアフィックスは名前を出さない（効果は個体に焼き付いたまま）。
        /// </summary>
        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                string baseName = GetDefinition()?.Name ?? Name;
                if (!HasAffix) return baseName;
                return $"{AffixBalance.FindById(PrefixId)?.Name}{baseName}{AffixBalance.FindById(SuffixId)?.Name}";
            }
        }

        /// <summary>指定した能力値へのアフィックス補正（無ければ0）。</summary>
        public int GetAffixStatBonus(string statName) => AffixStatBonuses.TryGetValue(statName, out int v) ? v : 0;

        /// <summary>
        /// アフィックスを1つ付ける（鑑定の抽選 → AppraisalSystem）。位置（接頭辞／接尾辞）の Id と値を記録し、
        /// 補正を AffixStatBonuses／AffixHpBonus へ加算する。同じ位置に既に付いていれば例外（上書きで合計が狂うのを防ぐ）。
        /// </summary>
        public void ApplyAffix(AffixDefinition affix, int value)
        {
            if (affix.Type == AffixType.Prefix)
            {
                if (PrefixId != null) throw new InvalidOperationException($"接頭辞は既に付いています: {PrefixId}");
                PrefixId = affix.Id;
                PrefixValue = value;
            }
            else
            {
                if (SuffixId != null) throw new InvalidOperationException($"接尾辞は既に付いています: {SuffixId}");
                SuffixId = affix.Id;
                SuffixValue = value;
            }

            if (affix.IsHpBonus)
                AffixHpBonus += value;
            else
                AffixStatBonuses[affix.TargetStat] = GetAffixStatBonus(affix.TargetStat) + value;
        }

        /// <summary>
        /// 効果の短い説明：カタログの基本性能（→ Item.DescribeEffects）に、アフィックスの補正を位置ごとに添える。
        /// 例：「STR+2・VIT+1 [剛力: STR+3] [巨躯: 最大HP+15]」。アフィックスが無ければカタログの説明のまま。
        /// カタログから引けない個体の基本性能は「（不明）」。
        /// </summary>
        public string DescribeEffects()
        {
            string text = GetDefinition()?.DescribeEffects() ?? "（不明）";
            text += DescribeAffix(PrefixId, PrefixValue);
            text += DescribeAffix(SuffixId, SuffixValue);
            return text;
        }

        private static string DescribeAffix(string? id, int value)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var def = AffixBalance.FindById(id);
            return def == null ? $" [{id}: {value:+0;-0}]" : $" [{def.ShortLabel}: {def.DescribeValue(value)}]";
        }

        /// <summary>
        /// カタログ定義を引く。未知のId（カタログから削除された等）ならnull。
        /// プロパティではなくメソッドにしてあるのは、System.Text.Jsonが読み取り専用プロパティを
        /// セーブデータへ書き出してしまうのを避けるため（→ SaveData）。
        /// </summary>
        public Item? GetDefinition() => ItemCatalog.FindById(ItemId);

        /// <summary>
        /// この武具が入る装備枠（→ Item.Slot）。カタログから引けない個体（旧データ）はnull。
        /// GetDefinition()と同じ理由でメソッドにしてある。
        /// </summary>
        public EquipmentSlot? GetSlot() => GetDefinition()?.Slot;

        /// <summary>
        /// 指定した職業が装備できるか（→ Item.IsAllowedFor）。カタログから引けない個体は
        /// 装備不可として扱う（防御的：正体不明の物を着せない）。
        /// </summary>
        public bool IsAllowedFor(JobClass jobClass) => GetDefinition()?.IsAllowedFor(jobClass) ?? false;

        /// <summary>
        /// カタログ定義から保管庫の個体を作る。rarity を渡すと「鑑定で出土した個体」として扱われ、
        /// 売却額が希少度基準になる（→ Rarity）。省略時はカタログ品（無銘）。
        /// </summary>
        public static EquipmentItem FromCatalog(
            Item item, int acquiredAtWeek = 0, string acquiredFrom = "", ItemRarity? rarity = null) => new()
        {
            ItemId = item.Id,
            Name = item.Name,
            AcquiredAtWeek = acquiredAtWeek,
            AcquiredFrom = acquiredFrom,
            Rarity = rarity,
        };
    }
}
