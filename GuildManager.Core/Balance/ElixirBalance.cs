using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 霊薬1種の定義（→ elixir_recipes.csv の1行）。飲ませると TargetStats の各能力の PA を PaBonus、実効値を StatBonus 上げる。
    /// 費用は RequiredGold と RequiredMaterials（素材Id→個数）。
    /// </summary>
    public sealed record ElixirRecipe(
        string Id, string Name, string Description, IReadOnlyList<string> TargetStats,
        int PaBonus, int StatBonus, IReadOnlyDictionary<string, int> RequiredMaterials, int RequiredGold);

    /// <summary>
    /// 霊薬（→ Systems.ElixirSystem、03 §4.6.2・§0.61）のバランス値。
    /// elixir.csv（キー・値）と elixir_recipes.csv（霊薬の表）から読む（→ 03 §10.1。フォールバックは持たない）。
    /// </summary>
    public static class ElixirBalance
    {
        private const string FileName = "elixir.csv";
        private const string RecipeFileName = "elixir_recipes.csv";

        private static readonly string[] ValidStats = { "STR", "VIT", "AGI", "DEX", "INT", "MND", "LDR" };

        /// <summary>1人が生涯に飲める霊薬の数。</summary>
        public static readonly int MaxPerAdventurer = BalanceData.GetInt(FileName, "MaxPerAdventurer");

        private static readonly Lazy<IReadOnlyList<ElixirRecipe>> AllRecipes = new(LoadRecipes);

        /// <summary>霊薬の一覧（CSVの行順）。</summary>
        public static IReadOnlyList<ElixirRecipe> Recipes => AllRecipes.Value;

        public static ElixirRecipe? Find(string? id) => id == null ? null : Recipes.FirstOrDefault(r => r.Id == id);

        private static IReadOnlyList<ElixirRecipe> LoadRecipes()
        {
            var (header, rows) = BalanceData.GetTable(RecipeFileName);
            int Col(string name)
            {
                int i = Array.IndexOf(header, name);
                return i >= 0 ? i : throw new BalanceDataException($"{RecipeFileName} に必須列「{name}」がありません。");
            }
            int id = Col("Id"), nameCol = Col("Name"), desc = Col("Description"), stats = Col("TargetStats"),
                pa = Col("PaBonus"), stat = Col("StatBonus"), mats = Col("RequiredMaterials"), gold = Col("RequiredGold");

            var result = new List<ElixirRecipe>();
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                int line = r + 2;
                if (result.Any(x => x.Id == row[id]))
                    throw new BalanceDataException($"{RecipeFileName} のId「{row[id]}」が重複しています（{line}行目）。");
                var targets = row[stats].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                if (targets.Count == 0 || targets.Any(t => !ValidStats.Contains(t)))
                    throw new BalanceDataException($"{RecipeFileName} の{line}行目の TargetStats「{row[stats]}」は STR/VIT/AGI/DEX/INT/MND/LDR の ; 区切りにしてください。");

                var materials = new Dictionary<string, int>();
                foreach (var entry in row[mats].Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = entry.Split(':');
                    if (parts.Length != 2)
                        throw new BalanceDataException($"{RecipeFileName} の{line}行目の RequiredMaterials「{entry}」を「素材Id:個数」として解釈できません。");
                    materials[parts[0]] = ParseInt(parts[1], line, "RequiredMaterials");
                }

                result.Add(new ElixirRecipe(row[id], row[nameCol], row[desc], targets,
                    ParseInt(row[pa], line, "PaBonus"), ParseInt(row[stat], line, "StatBonus"), materials, ParseInt(row[gold], line, "RequiredGold")));
            }
            return result;
        }

        private static int ParseInt(string raw, int line, string column) =>
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : throw new BalanceDataException($"{RecipeFileName} の{line}行目の{column}「{raw}」を整数として解釈できません。");
    }
}
