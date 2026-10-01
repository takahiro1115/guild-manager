using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GuildManager.Core.Balance
{
    /// <summary>触媒の素材が効く先（→ soul_fusion_catalysts.csv EffectType、SoulFusionSystem）。</summary>
    public enum SoulFusionCatalystEffect
    {
        /// <summary>子のPAのばらつきの下限に足す（pt）。</summary>
        PaVarianceMinBonus,

        /// <summary>親の長所を受け継ぐ確率に足す（%ポイント）。</summary>
        TraitInheritBonus,

        /// <summary>親の欠点を受け継ぐ確率に足す（%ポイント。負の値で下げる）。</summary>
        FlawInheritBonus,

        /// <summary>能力限界突破の確率に足す（%ポイント）。</summary>
        BreakthroughChanceBonus,

        /// <summary>親のレア特性を受け継ぐ確率に足す（%ポイント）。</summary>
        RareTraitInheritBonus,
    }

    /// <summary>触媒1種の定義（→ soul_fusion_catalysts.csv の1行）。処方1回につき1種類まで、Count 個を消費する。</summary>
    public sealed record SoulFusionCatalyst(string MaterialId, int Count, SoulFusionCatalystEffect EffectType, int EffectValue, string Note);

    /// <summary>
    /// 魂魄融和の秘薬（→ Systems.SoulFusionSystem、03 §5.4・§0.58）のバランス値。
    /// 値は docs/04_バランス表/soul_fusion.csv（キー・値）と soul_fusion_catalysts.csv（触媒の表）から読む
    /// （→ 03 §10.1。フォールバックは持たない）。通常の能力のPA上限（100）は構造値のためコードに置く。
    /// </summary>
    public static class SoulFusionBalance
    {
        private const string FileName = "soul_fusion.csv";
        private const string CatalystFileName = "soul_fusion_catalysts.csv";

        /// <summary>限界突破しない能力のPA上限（→ Adventurer の PA 1〜100）。</summary>
        public const int NormalPaCap = 100;

        public static readonly int RequiredCompatibility = BalanceData.GetInt(FileName, "RequiredCompatibility");
        public static readonly int PrescriptionGold = BalanceData.GetInt(FileName, "PrescriptionGold");
        public static readonly int CultureWeeks = BalanceData.GetInt(FileName, "CultureWeeks");
        public static readonly int CultureTankCount = BalanceData.GetInt(FileName, "CultureTankCount");

        /// <summary>娘のPAの基準で、両親のうち高い方に掛ける重み（残りを低い方に掛ける。0.5＝平均、1.0＝高い方そのもの。§0.60）。</summary>
        public static readonly double HigherParentWeight = BalanceData.GetDouble(FileName, "HigherParentWeight");

        public static readonly int PaVarianceMin = BalanceData.GetInt(FileName, "PaVarianceMin");
        public static readonly int PaVarianceMax = BalanceData.GetInt(FileName, "PaVarianceMax");

        public static readonly int BreakthroughChanceDestined = BalanceData.GetInt(FileName, "BreakthroughChance_Destined");
        public static readonly int BreakthroughChanceComplementary = BalanceData.GetInt(FileName, "BreakthroughChance_Complementary");
        public static readonly int BreakthroughChanceOrdinary = BalanceData.GetInt(FileName, "BreakthroughChance_Ordinary");
        public static readonly int BreakthroughBonusMin = BalanceData.GetInt(FileName, "BreakthroughBonusMin");
        public static readonly int BreakthroughBonusMax = BalanceData.GetInt(FileName, "BreakthroughBonusMax");
        public static readonly int BreakthroughPaCap = BalanceData.GetInt(FileName, "BreakthroughPaCap");

        public static readonly int TraitInheritChancePercent = BalanceData.GetInt(FileName, "TraitInheritChancePercent");
        public static readonly int RareTraitInheritChancePercent = BalanceData.GetInt(FileName, "RareTraitInheritChancePercent");
        public static readonly int FlawInheritChancePercent = BalanceData.GetInt(FileName, "FlawInheritChancePercent");

        /// <summary>娘が「魂魄の申し子」（→ TraitCatalog.SoulChild、§0.62）を持つ確率（%）。</summary>
        public static readonly int SoulChildChancePercent = BalanceData.GetInt(FileName, "SoulChildChancePercent");

        private static readonly Lazy<IReadOnlyList<SoulFusionCatalyst>> AllCatalysts = new(LoadCatalysts);

        /// <summary>触媒の一覧（CSVの行順）。</summary>
        public static IReadOnlyList<SoulFusionCatalyst> Catalysts => AllCatalysts.Value;

        /// <summary>指定した素材の触媒定義。触媒にならない素材・null は null。</summary>
        public static SoulFusionCatalyst? FindCatalyst(string? materialId) =>
            materialId == null ? null : Catalysts.FirstOrDefault(c => c.MaterialId == materialId);

        private static IReadOnlyList<SoulFusionCatalyst> LoadCatalysts()
        {
            var (_, rows) = BalanceData.GetTable(CatalystFileName);
            var result = new List<SoulFusionCatalyst>();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                int line = i + 2;
                if (row.Length < 5)
                    throw new BalanceDataException($"{CatalystFileName} の{line}行目の列数が足りません（MaterialId,Count,EffectType,EffectValue,Note）。");
                if (!Enum.TryParse<SoulFusionCatalystEffect>(row[2], out var effect))
                    throw new BalanceDataException($"{CatalystFileName} の{line}行目のEffectType「{row[2]}」を解釈できません。");
                if (result.Any(c => c.MaterialId == row[0]))
                    throw new BalanceDataException($"{CatalystFileName} の素材「{row[0]}」が重複しています（{line}行目）。");
                result.Add(new SoulFusionCatalyst(row[0], ParseInt(row[1], line, "Count"), effect, ParseInt(row[3], line, "EffectValue"), row[4]));
            }
            return result;
        }

        private static int ParseInt(string raw, int line, string column) =>
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : throw new BalanceDataException($"{CatalystFileName} の{line}行目の{column}「{raw}」を整数として解釈できません。");
    }
}
