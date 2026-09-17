using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 研究（→ アルベールの研究室、素材投資システム）の定義一覧。
    /// 値は docs/04_バランス表/research.csv（テーブル形式）から読み込む
    /// （→ 03 §10.1、BalanceDataの方針にならいフォールバックは持たない）。
    ///
    /// 列の意味：
    ///  - id/name/description：研究の識別子・表示名・説明文。
    ///  - effect_type：ResearchEffectTypeの名前（→ Models.ResearchEffectType）。
    ///  - effect_value：効果量（意味はeffect_typeごとに異なる）。
    ///  - required_gold：必要ゴールド。
    ///  - required_materials：必要素材。"素材Id:個数" を ";" 区切りで複数指定できる
    ///    （例："herb_moonlight:5;moss_luminous:3"）。CSVの他の列と違いカンマを含まないため
    ///    クォート不要。
    /// </summary>
    public static class ResearchBalance
    {
        private const string FileName = "research.csv";

        private static readonly List<ResearchDefinition> Definitions = BuildDefinitions();

        private static List<ResearchDefinition> BuildDefinitions()
        {
            var (_, rows) = BalanceData.GetTable(FileName);
            var result = new List<ResearchDefinition>();

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];

                if (!Enum.TryParse<ResearchEffectType>(row[3], out var effectType))
                    throw new BalanceDataException($"{FileName} の{i + 2}行目のeffect_type「{row[3]}」をResearchEffectTypeとして解釈できません。");

                result.Add(new ResearchDefinition
                {
                    Id = row[0],
                    Name = row[1],
                    Description = row[2],
                    EffectType = effectType,
                    EffectValue = ParseFloat(row[4], i, "effect_value"),
                    RequiredGold = ParseInt(row[5], i, "required_gold"),
                    RequiredMaterials = ParseMaterials(row[6], i),
                });
            }

            return result;
        }

        /// <summary>"素材Id:個数;素材Id2:個数2" 形式を辞書へ変換する。空文字は「必要素材なし」として扱う。</summary>
        private static Dictionary<string, int> ParseMaterials(string raw, int rowIndex)
        {
            var result = new Dictionary<string, int>();
            if (string.IsNullOrWhiteSpace(raw))
                return result;

            foreach (var entry in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = entry.Split(':');
                if (parts.Length != 2)
                    throw new BalanceDataException(
                        $"{FileName} の{rowIndex + 2}行目のrequired_materials「{entry}」を「素材Id:個数」として解釈できません。");

                result[parts[0]] = ParseInt(parts[1], rowIndex, "required_materials");
            }

            return result;
        }

        private static int ParseInt(string raw, int rowIndex, string columnName)
        {
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                return value;
            throw new BalanceDataException($"{FileName} の{rowIndex + 2}行目の{columnName}「{raw}」を整数として解釈できません。");
        }

        private static float ParseFloat(string raw, int rowIndex, string columnName)
        {
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                return value;
            throw new BalanceDataException($"{FileName} の{rowIndex + 2}行目の{columnName}「{raw}」を数値として解釈できません。");
        }

        /// <summary>全研究の定義一覧（→ UI側の一覧表示）。</summary>
        public static IReadOnlyList<ResearchDefinition> GetAll() => Definitions;

        /// <summary>指定Idの研究定義。未定義のIdはnull（防御的：research.csv側の設定漏れを黙って握り潰さない用途以外では、呼び出し側が null許容で扱う）。</summary>
        public static ResearchDefinition? Find(string id) => Definitions.FirstOrDefault(d => d.Id == id);
    }
}
