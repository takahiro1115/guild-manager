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
    ///  - Id/Name/Description：研究の識別子・表示名・説明文。
    ///  - RequiredMaterials：必要素材。"素材Id:個数" を ";" 区切りで複数指定できる
    ///    （例："mat_forest_spore:3;mat_forest_wood:3"）。CSVの他の列と違いカンマを含まないため
    ///    クォート不要。
    ///  - RequiredGold：必要ゴールド。
    ///  - EffectType：ResearchEffectTypeの名前（→ Models.ResearchEffectType）。
    ///  - EffectValue：効果量（意味はEffectTypeごとに異なる）。
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

                if (!Enum.TryParse<ResearchEffectType>(row[5], out var effectType))
                    throw new BalanceDataException($"{FileName} の{i + 2}行目のEffectType「{row[5]}」をResearchEffectTypeとして解釈できません。");

                result.Add(new ResearchDefinition
                {
                    Id = row[0],
                    Name = row[1],
                    Description = row[2],
                    RequiredMaterials = ParseMaterials(row[3], i),
                    RequiredGold = ParseInt(row[4], i, "RequiredGold"),
                    EffectType = effectType,
                    EffectValue = ParseFloat(row[6], i, "EffectValue"),
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
                        $"{FileName} の{rowIndex + 2}行目のRequiredMaterials「{entry}」を「素材Id:個数」として解釈できません。");

                result[parts[0]] = ParseInt(parts[1], rowIndex, "RequiredMaterials");
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

        /// <summary>
        /// 指定した効果種別のうち、stateで完了済みの研究のEffectValue合計（2026年9月新設）。
        /// 同じ効果種別の研究が複数完了していても加算で重複できるようにする（→ 各Resolver・
        /// Systemの研究バフ参照。以前は研究Idを1つだけ固定でチェックしていたため、フィールド
        /// 拡張で同種の研究が増えても2つ目以降が反映されない不具合があった）。
        /// </summary>
        public static double GetTotalEffectValue(GameState state, ResearchEffectType effectType) =>
            Definitions.Where(d => d.EffectType == effectType && state.IsResearchCompleted(d.Id)).Sum(d => d.EffectValue);
    }
}
