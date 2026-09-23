using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 素材（→ 第3の任務「探索（採取）」）の定義一覧。値は docs/04_バランス表/materials.csv
    /// （テーブル形式）から読み込む（→ 03 §10.1、BalanceDataの方針にならいフォールバックは持たない）。
    ///
    /// 列の意味：Id,Name,Description,FieldId,MinFloor,MaxFloor,BaseYield,SellPrice
    /// （→ Models.MaterialDefinition。SellPriceは2026年9月追加、→ 03 §4.8 売却）。
    ///
    /// 現時点（候補A）ではforestフィールド分のみを定義する。他フィールド（cave/ruins/canyon/abyss）は
    /// 未定義のため、それらのフィールドでの採取任務は素材が抽選されない
    /// （→ Systems.GatheringResolver.RollMaterialが空文字を返す）。
    /// </summary>
    public static class MaterialBalance
    {
        private const string FileName = "materials.csv";

        private static readonly List<MaterialDefinition> Definitions = BuildDefinitions();

        private static List<MaterialDefinition> BuildDefinitions()
        {
            var (_, rows) = BalanceData.GetTable(FileName);
            var result = new List<MaterialDefinition>();

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];

                result.Add(new MaterialDefinition
                {
                    Id = row[0],
                    Name = row[1],
                    Description = row[2],
                    FieldId = row[3],
                    MinFloor = ParseInt(row[4], i, "MinFloor"),
                    MaxFloor = ParseInt(row[5], i, "MaxFloor"),
                    BaseYield = ParseInt(row[6], i, "BaseYield"),
                    SellPrice = ParseInt(row[7], i, "SellPrice"),
                });
            }

            return result;
        }

        private static int ParseInt(string raw, int rowIndex, string columnName)
        {
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                return value;
            throw new BalanceDataException($"{FileName} の{rowIndex + 2}行目の{columnName}「{raw}」を整数として解釈できません。");
        }

        /// <summary>全素材の定義一覧。</summary>
        public static IReadOnlyList<MaterialDefinition> GetAll() => Definitions;

        /// <summary>指定Idの素材定義。未定義のIdはnull。</summary>
        public static MaterialDefinition? Find(string id) => Definitions.FirstOrDefault(d => d.Id == id);

        /// <summary>素材Idの表示名。カタログに無いIdはそのまま表示する（防御的フォールバック）。</summary>
        public static string GetName(string id) => Find(id)?.Name ?? id;

        /// <summary>
        /// 素材1個あたりの売却額（→ Systems.EconomySystem.TrySellMaterial）。
        /// materials.csvに定義の無いIdは0（＝売れない）として扱う（防御的フォールバック）。
        /// </summary>
        public static int GetSellPrice(string id) => Find(id)?.SellPrice ?? 0;

        /// <summary>指定フィールドに定義されている素材の一覧（到達階層に関わらず全件、UIのプレビュー表示用）。</summary>
        public static IReadOnlyList<MaterialDefinition> GetFieldMaterials(string fieldId) =>
            Definitions.Where(d => d.FieldId == fieldId).ToList();

        /// <summary>
        /// 指定フィールド・到達階層で実際に抽選対象になる素材の一覧（→ Systems.GatheringResolver）。
        /// MinFloor以上に到達していれば対象（MaxFloorは抽選の上限としては使わない、→ MaterialDefinition）。
        /// </summary>
        public static IReadOnlyList<MaterialDefinition> GetEligibleMaterials(string fieldId, int reachedFloor) =>
            Definitions.Where(d => d.FieldId == fieldId && reachedFloor >= d.MinFloor).ToList();
    }
}
