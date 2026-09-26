using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Balance
{
    /// <summary>アフィックスの位置（→ affixes.csv `Type`）。接頭辞は名前の前、接尾辞は名前の後ろに付く。</summary>
    public enum AffixType
    {
        Prefix,
        Suffix,
    }

    /// <summary>
    /// アフィックス1種の定義（→ affixes.csv の1行）。
    /// TargetStat は7大能力値（"STR","VIT","AGI","DEX","INT","MND","LDR"＝Adventurer.GetEffectiveStat の表記）
    /// または最大HPを表す <see cref="AffixBalance.HpTarget"/>（"HP"）。
    /// AllowedSlots は付与できる装備枠（Accessory は装飾1・装飾2の両方）。
    /// </summary>
    public sealed record AffixDefinition(
        string Id,
        AffixType Type,
        string Name,
        string TargetStat,
        int MinValue,
        int MaxValue,
        int Tier,
        IReadOnlyList<EquipmentSlot> AllowedSlots,
        int Weight)
    {
        /// <summary>最大HPへの補正か（そうでなければ7大能力値のいずれか）。</summary>
        public bool IsHpBonus => TargetStat == AffixBalance.HpTarget;

        /// <summary>
        /// 効果説明の見出しに使う短い名前（→ EquipmentItem.DescribeEffects）。
        /// 接頭辞は末尾の「の」「な」を、接尾辞は［］を外す（例：剛力の → 剛力、頑強な → 頑強、［巨躯］ → 巨躯）。
        /// </summary>
        public string ShortLabel => Type == AffixType.Prefix
            ? Name.TrimEnd('の', 'な')
            : Name.Trim('［', '］');

        /// <summary>補正の短い表記（例：STR+3、最大HP+15）。</summary>
        public string DescribeValue(int value) =>
            $"{(IsHpBonus ? "最大HP" : TargetStat)}{(value >= 0 ? "+" : "")}{value}";
    }

    /// <summary>
    /// 希少度ごとのアフィックス付与ルール（→ relic.csv の Affix*）。鑑定で武具が出たとき、
    /// 接頭辞・接尾辞をそれぞれ独立に付与判定し、当選した枠は Tier が MinTier〜MaxTier のアフィックスから抽選する。
    /// </summary>
    public sealed record AffixRarityRule(ItemRarity Rarity, int PrefixRate, int SuffixRate, int MinTier, int MaxTier)
    {
        public int RateFor(AffixType type) => type == AffixType.Prefix ? PrefixRate : SuffixRate;
    }

    /// <summary>
    /// 鑑定品のランダムアフィックス（→ 03 §4.7・§4.2.2、2026年9月・§0.39）のバランス値。
    ///
    ///  - アフィックスの一覧：docs/04_バランス表/affixes.csv（テーブル形式、
    ///    列 `Id,Type,Name,TargetStat,MinValue,MaxValue,Tier,AllowedSlots,Weight`）
    ///  - 希少度ごとの付与率・Tier範囲：relic.csv の `AffixPrefixRate*`・`AffixSuffixRate*`・`AffixMinTier*`・`AffixMaxTier*`
    ///    （希少度ごとの鑑定の数値はすべて relic.csv に集める方針のため。→ RelicBalance）
    ///
    /// 読み込み時に以下を検出し、BalanceDataException で起動失敗にする（フォールバックしない）：
    /// 必須列の欠損・Idの重複・Type／TargetStat／AllowedSlots の不正値・数値のパース失敗・
    /// Min＞Max・Tier が1〜3の外・Weight が0以下・付与率が0〜100の外・Tier範囲の逆転、
    /// および「付与率が0より大きいのに、ある装備枠で抽選できるアフィックスが1件も無い」組み合わせ
    /// （鑑定のたびに抽選が空振りするのを起動時に潰すため）。
    ///
    /// 列はヘッダー名で引く（列順の入れ替えに強くするため。→ EquipmentBalance と同じ流儀）。
    /// 解析処理は <see cref="Parse"/> に分けてあり、テストから不正な表を直接渡して例外を検証できる。
    /// </summary>
    public static class AffixBalance
    {
        public const string FileName = "affixes.csv";
        private const string RelicFileName = "relic.csv";

        /// <summary>最大HPへの補正を表す TargetStat の値。</summary>
        public const string HpTarget = "HP";

        public const int MinTier = 1;
        public const int MaxTier = 3;

        /// <summary>TargetStat に書ける値（7大能力値＋HP）。</summary>
        private static readonly string[] ValidTargets = { "STR", "VIT", "AGI", "DEX", "INT", "MND", "LDR", HpTarget };

        private static readonly string[] RequiredColumns =
            { "Id", "Type", "Name", "TargetStat", "MinValue", "MaxValue", "Tier", "AllowedSlots", "Weight" };

        private static readonly Lazy<IReadOnlyList<AffixDefinition>> AllDefinitions = new(LoadDefinitions);
        private static readonly Lazy<Dictionary<string, AffixDefinition>> DefinitionsById =
            new(() => All.ToDictionary(d => d.Id));
        private static readonly Lazy<Dictionary<ItemRarity, AffixRarityRule>> RarityRules = new(LoadRarityRules);

        /// <summary>affixes.csv の全アフィックス（CSVの行順）。</summary>
        public static IReadOnlyList<AffixDefinition> All => AllDefinitions.Value;

        /// <summary>指定Idのアフィックス。存在しなければnull（CSVから削除された旧セーブの個体など）。</summary>
        public static AffixDefinition? FindById(string? id) =>
            id != null && DefinitionsById.Value.TryGetValue(id, out var def) ? def : null;

        /// <summary>希少度ごとの付与ルール。</summary>
        public static AffixRarityRule GetRule(ItemRarity rarity) => RarityRules.Value[rarity];

        /// <summary>
        /// 指定の位置・装備枠・Tier範囲に合致するアフィックス（抽選の母集団）。CSVの行順を保つ。
        /// </summary>
        public static IReadOnlyList<AffixDefinition> GetCandidates(AffixType type, EquipmentSlot slot, int minTier, int maxTier) =>
            All.Where(d => d.Type == type && d.Tier >= minTier && d.Tier <= maxTier && d.AllowedSlots.Contains(slot))
               .ToList();

        /// <summary>
        /// 母集団から Weight に比例して1件を抽選する（乱数は1回：1〜重み合計）。母集団が空ならnull。
        /// </summary>
        public static AffixDefinition? PickWeighted(IReadOnlyList<AffixDefinition> candidates, IRng rng)
        {
            if (candidates.Count == 0) return null;

            int total = candidates.Sum(d => d.Weight);
            int roll = rng.NextInt(1, total);
            foreach (var def in candidates)
            {
                roll -= def.Weight;
                if (roll <= 0) return def;
            }
            return candidates[^1]; // 到達しない（roll ≤ total のため）
        }

        // ==================== 読み込み ====================

        private static IReadOnlyList<AffixDefinition> LoadDefinitions()
        {
            var (header, rows) = BalanceData.GetTable(FileName);
            var definitions = Parse(header, rows);
            ValidatePoolsAgainstRules(definitions, LoadRarityRules());
            return definitions;
        }

        /// <summary>
        /// affixes.csv のヘッダーと行を定義の一覧へ変換する。書式違反は BalanceDataException。
        /// </summary>
        public static IReadOnlyList<AffixDefinition> Parse(string[] header, IReadOnlyList<string[]> rows)
        {
            var col = RequiredColumns.ToDictionary(name => name, name => RequireColumn(header, name));

            var result = new List<AffixDefinition>();
            var ids = new HashSet<string>();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                int line = i + 2;
                if (row.Length != header.Length)
                    throw new BalanceDataException($"{FileName} の{line}行目の列数（{row.Length}）がヘッダー（{header.Length}列）と一致しません。");

                string id = row[col["Id"]].Trim();
                if (id.Length == 0)
                    throw new BalanceDataException($"{FileName} の{line}行目の Id が空です。");
                if (!ids.Add(id))
                    throw new BalanceDataException($"{FileName} のId「{id}」が重複しています（{line}行目）。");

                string rawType = row[col["Type"]].Trim();
                if (!Enum.TryParse<AffixType>(rawType, ignoreCase: false, out var type) || !Enum.IsDefined(type))
                    throw new BalanceDataException($"{FileName} の{line}行目の Type「{rawType}」は Prefix / Suffix のいずれかにしてください。");

                string name = row[col["Name"]].Trim();
                if (name.Length == 0)
                    throw new BalanceDataException($"{FileName} の{line}行目の Name が空です。");

                string target = row[col["TargetStat"]].Trim();
                if (!ValidTargets.Contains(target))
                    throw new BalanceDataException(
                        $"{FileName} の{line}行目の TargetStat「{target}」は {string.Join("/", ValidTargets)} のいずれかにしてください。");

                int min = ParseInt(row[col["MinValue"]], line, "MinValue");
                int max = ParseInt(row[col["MaxValue"]], line, "MaxValue");
                if (min > max)
                    throw new BalanceDataException($"{FileName} の{line}行目の MinValue（{min}）が MaxValue（{max}）を超えています。");

                int tier = ParseInt(row[col["Tier"]], line, "Tier");
                if (tier < MinTier || tier > MaxTier)
                    throw new BalanceDataException($"{FileName} の{line}行目の Tier（{tier}）は{MinTier}〜{MaxTier}にしてください。");

                var slots = ParseSlots(row[col["AllowedSlots"]], line);

                int weight = ParseInt(row[col["Weight"]], line, "Weight");
                if (weight <= 0)
                    throw new BalanceDataException($"{FileName} の{line}行目の Weight（{weight}）は1以上にしてください。");

                result.Add(new AffixDefinition(id, type, name, target, min, max, tier, slots, weight));
            }

            if (result.Count == 0)
                throw new BalanceDataException($"{FileName} にアフィックスが1件もありません。");

            return result;
        }

        /// <summary>
        /// AllowedSlots の解釈。All＝全4枠、Weapon・Armor、Accessory＝装飾1・装飾2。
        /// 「Weapon|Armor」のように | 区切りで複数指定できる。
        /// </summary>
        private static IReadOnlyList<EquipmentSlot> ParseSlots(string raw, int line)
        {
            var slots = new List<EquipmentSlot>();
            foreach (var token in raw.Split('|').Select(t => t.Trim()))
            {
                switch (token)
                {
                    case "All":
                        slots.AddRange(new[] { EquipmentSlot.Weapon, EquipmentSlot.Armor, EquipmentSlot.Accessory1, EquipmentSlot.Accessory2 });
                        break;
                    case "Weapon":
                        slots.Add(EquipmentSlot.Weapon);
                        break;
                    case "Armor":
                        slots.Add(EquipmentSlot.Armor);
                        break;
                    case "Accessory":
                        slots.Add(EquipmentSlot.Accessory1);
                        slots.Add(EquipmentSlot.Accessory2);
                        break;
                    default:
                        throw new BalanceDataException(
                            $"{FileName} の{line}行目の AllowedSlots「{raw}」は All / Weapon / Armor / Accessory（| 区切りで複数可）にしてください。");
                }
            }
            return slots.Distinct().ToList();
        }

        private static Dictionary<ItemRarity, AffixRarityRule> LoadRarityRules()
        {
            var rules = new Dictionary<ItemRarity, AffixRarityRule>();
            foreach (var rarity in Enum.GetValues<ItemRarity>())
            {
                var rule = new AffixRarityRule(
                    rarity,
                    BalanceData.GetInt(RelicFileName, $"AffixPrefixRate{rarity}"),
                    BalanceData.GetInt(RelicFileName, $"AffixSuffixRate{rarity}"),
                    BalanceData.GetInt(RelicFileName, $"AffixMinTier{rarity}"),
                    BalanceData.GetInt(RelicFileName, $"AffixMaxTier{rarity}"));

                if (rule.PrefixRate is < 0 or > 100 || rule.SuffixRate is < 0 or > 100)
                    throw new BalanceDataException($"{RelicFileName} の {rarity} のアフィックス付与率は0〜100にしてください。");
                if (rule.MinTier < MinTier || rule.MaxTier > MaxTier || rule.MinTier > rule.MaxTier)
                    throw new BalanceDataException(
                        $"{RelicFileName} の {rarity} のアフィックスTier範囲（{rule.MinTier}〜{rule.MaxTier}）が不正です（{MinTier}〜{MaxTier}の範囲で Min ≤ Max）。");

                rules[rarity] = rule;
            }
            return rules;
        }

        /// <summary>
        /// 付与率が0より大きい（希少度×位置）について、全装備枠で抽選の母集団が空でないことを検証する。
        /// </summary>
        private static void ValidatePoolsAgainstRules(IReadOnlyList<AffixDefinition> definitions, Dictionary<ItemRarity, AffixRarityRule> rules)
        {
            foreach (var rule in rules.Values)
            foreach (var type in Enum.GetValues<AffixType>())
            {
                if (rule.RateFor(type) == 0) continue;
                foreach (var slot in Enum.GetValues<EquipmentSlot>())
                {
                    bool any = definitions.Any(d =>
                        d.Type == type && d.Tier >= rule.MinTier && d.Tier <= rule.MaxTier && d.AllowedSlots.Contains(slot));
                    if (!any)
                        throw new BalanceDataException(
                            $"{FileName} に、{rule.Rarity}（Tier {rule.MinTier}〜{rule.MaxTier}）の {slot} へ付けられる {type} が1件もありません。");
                }
            }
        }

        private static int RequireColumn(string[] header, string name)
        {
            int index = Array.IndexOf(header, name);
            if (index < 0)
                throw new BalanceDataException($"{FileName} に必須列「{name}」がありません。");
            return index;
        }

        private static int ParseInt(string raw, int line, string columnName)
        {
            if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                return value;
            throw new BalanceDataException($"{FileName} の{line}行目の{columnName}「{raw}」を整数として解釈できません。");
        }
    }
}
