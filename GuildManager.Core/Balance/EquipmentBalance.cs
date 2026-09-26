using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 装備アイテム1種分の数値（→ EquipmentBalance.Get）。
    /// StatBonuses は7大能力値（STR/VIT/AGI/DEX/INT/MND/LDR）→ 補正値。0の能力値も含めて全7キーを持つ。
    /// </summary>
    public sealed record EquipmentStats(int Price, int HpBonus, IReadOnlyDictionary<string, int> StatBonuses);

    /// <summary>
    /// 装備アイテム（→ ItemCatalog）の価格・効果量・能力値補正に関するバランス値。仕様書 03 §4.2.2 参照。
    /// 値は docs/04_バランス表/equipment.csv（テーブル形式）から読み込む（→ 03 §10.1）。
    ///
    /// 列の意味：Id,Price,HpBonus,BonusStr,BonusVit,BonusAgi,BonusDex,BonusInt,BonusMnd,BonusLdr,note
    /// （HpBonus＝最大HPへの固定加算。武器は0。2026年9月・§0.37で旧 EffectValue 列（武器・一部アクセサリーでは
    /// 個人CP加算量を兼ねていた）を個人CPの撤廃に伴い HpBonus へ改名し、武具の強さは能力値補正に一本化した）。
    ///
    /// 2026年9月改訂（武具の7大能力値補正）：旧 key,value 形式（IronSword_Price 等）から
    /// テーブル形式へ移行した。アイテムごとに10個近い数値を持つようになり、key,value 形式では
    /// 行が膨れて一覧性が失われるため。アイテムの構造（Id・Name・Slot・EffectType・AllowedJobs・
    /// VisualPartId・重装区分）は引き続き ItemCatalog.cs 側に残す（→ README「CSV化していない値」）。
    ///
    /// 列はヘッダー名で引く（列順の入れ替えに強くするため）。必須列の欠損・数値のパース失敗・
    /// Idの重複・カタログが要求するIdの欠損はいずれも BalanceDataException（フォールバックしない）。
    /// </summary>
    public static class EquipmentBalance
    {
        private const string FileName = "equipment.csv";

        /// <summary>能力値名 → CSV列名。能力値名は Adventurer.GetEffectiveStat の引数と同じ表記。</summary>
        private static readonly (string Stat, string Column)[] BonusColumns =
        {
            ("STR", "BonusStr"),
            ("VIT", "BonusVit"),
            ("AGI", "BonusAgi"),
            ("DEX", "BonusDex"),
            ("INT", "BonusInt"),
            ("MND", "BonusMnd"),
            ("LDR", "BonusLdr"),
        };

        private static readonly Dictionary<string, EquipmentStats> Definitions = BuildDefinitions();

        private static Dictionary<string, EquipmentStats> BuildDefinitions()
        {
            var (header, rows) = BalanceData.GetTable(FileName);

            int idCol = RequireColumn(header, "Id");
            int priceCol = RequireColumn(header, "Price");
            int hpCol = RequireColumn(header, "HpBonus");
            var bonusCols = BonusColumns.Select(b => (b.Stat, Index: RequireColumn(header, b.Column), b.Column)).ToArray();

            var result = new Dictionary<string, EquipmentStats>();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                string id = row[idCol];
                if (result.ContainsKey(id))
                    throw new BalanceDataException($"{FileName} のId「{id}」が重複しています（{i + 2}行目）。");

                var bonuses = bonusCols.ToDictionary(b => b.Stat, b => ParseInt(row[b.Index], i, b.Column));
                result[id] = new EquipmentStats(
                    ParseInt(row[priceCol], i, "Price"),
                    ParseInt(row[hpCol], i, "HpBonus"),
                    bonuses);
            }

            return result;
        }

        private static int RequireColumn(string[] header, string name)
        {
            int index = Array.IndexOf(header, name);
            if (index < 0)
                throw new BalanceDataException($"{FileName} に必須列「{name}」がありません。");
            return index;
        }

        private static int ParseInt(string raw, int rowIndex, string columnName)
        {
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                return value;
            throw new BalanceDataException($"{FileName} の{rowIndex + 2}行目の{columnName}「{raw}」を整数として解釈できません。");
        }

        /// <summary>指定Idの数値。CSVに行が無ければ BalanceDataException（カタログとCSVの不一致は起動失敗にする）。</summary>
        public static EquipmentStats Get(string id)
        {
            if (Definitions.TryGetValue(id, out var stats))
                return stats;
            throw new BalanceDataException($"{FileName} にアイテムId「{id}」の行がありません。");
        }
    }
}
