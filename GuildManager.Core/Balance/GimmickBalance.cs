using System;
using System.Collections.Generic;
using System.Globalization;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 環境ギミック（→ Models.EnvironmentTag）が、統一点数計算式のどのフェーズに
    /// ペナルティ係数を適用するかの分類。仕様「フェーズ1（暗黒）、フェーズ2（霊体・隘路）、
    /// 損耗/ダウン（瘴気・巨躯）」にそのまま対応する。
    /// </summary>
    public enum GimmickPhase
    {
        /// <summary>フェーズ1（索敵・遭遇判定）のPartyScoutに乗算する。</summary>
        Scouting,

        /// <summary>フェーズ2（統一点数計算式）の点数（Score）に乗算する。</summary>
        Score,

        /// <summary>損耗（HP消費%）に乗算する。討伐・探索・護衛いずれのHP消費にも適用する。</summary>
        Attrition,
    }

    /// <summary>
    /// 環境ギミック関連のバランス値。「環境ギミック」刷新仕様参照。
    /// 値は docs/04_バランス表/gimmick.csv（タグ×パラメータのテーブル形式）から読み込む
    /// （→ 03 §10.1、BalanceDataの方針にならいフォールバックは持たない）。
    ///
    /// 各行の意味：
    ///  - stat：対策点数の算出に使う実効値（パーティ全員の合算）。
    ///  - threshold_partial/threshold_full：合算値がこの値以上で+1点/+2点（→ GimmickEvaluator）。
    ///  - phase：ペナルティ係数を適用する先（→ GimmickPhase）。
    ///  - none/partial/full_multiplier：対策達成度ごとの係数（未充足=ペナルティそのまま、
    ///    一部充足=半減、完全充足=無効化＝1.0）。
    ///  - counter_item：この点数に+1点する携行アイテムのId（→ ConsumableCatalog）。
    /// </summary>
    public static class GimmickBalance
    {
        private const string FileName = "gimmick.csv";

        private readonly record struct GimmickDefinition(
            string Stat, double ThresholdPartial, double ThresholdFull, GimmickPhase Phase,
            double NoneMultiplier, double PartialMultiplier, double FullMultiplier, string CounterItemId);

        private static readonly Dictionary<EnvironmentTag, GimmickDefinition> Definitions = BuildDefinitions();

        private static Dictionary<EnvironmentTag, GimmickDefinition> BuildDefinitions()
        {
            var (_, rows) = BalanceData.GetTable(FileName);
            var result = new Dictionary<EnvironmentTag, GimmickDefinition>();

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                string tagText = row[0];

                if (!Enum.TryParse<EnvironmentTag>(tagText, out var tag))
                    throw new BalanceDataException($"{FileName} の{i + 2}行目のtag「{tagText}」をEnvironmentTagとして解釈できません。");
                if (!Enum.TryParse<GimmickPhase>(row[4], out var phase))
                    throw new BalanceDataException($"{FileName} の{i + 2}行目のphase「{row[4]}」をGimmickPhaseとして解釈できません。");

                result[tag] = new GimmickDefinition(
                    Stat: row[1],
                    ThresholdPartial: ParseDouble(row[2], i, "threshold_partial"),
                    ThresholdFull: ParseDouble(row[3], i, "threshold_full"),
                    Phase: phase,
                    NoneMultiplier: ParseDouble(row[5], i, "none_multiplier"),
                    PartialMultiplier: ParseDouble(row[6], i, "partial_multiplier"),
                    FullMultiplier: ParseDouble(row[7], i, "full_multiplier"),
                    CounterItemId: row[8]);
            }

            return result;
        }

        private static double ParseDouble(string raw, int rowIndex, string columnName)
        {
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                return value;
            throw new BalanceDataException($"{FileName} の{rowIndex + 2}行目の{columnName}「{raw}」を数値として解釈できません。");
        }

        private static GimmickDefinition Get(EnvironmentTag tag)
        {
            if (!Definitions.TryGetValue(tag, out var def))
                throw new BalanceDataException($"{FileName} にギミック「{tag}」の行がありません。");
            return def;
        }

        /// <summary>対策点数の算出に使う実効値名（例："MND"）。</summary>
        public static string GetStat(EnvironmentTag tag) => Get(tag).Stat;

        public static double GetThresholdPartial(EnvironmentTag tag) => Get(tag).ThresholdPartial;
        public static double GetThresholdFull(EnvironmentTag tag) => Get(tag).ThresholdFull;

        /// <summary>このギミックがペナルティ係数を適用する先（→ GimmickPhase）。</summary>
        public static GimmickPhase GetPhase(EnvironmentTag tag) => Get(tag).Phase;

        /// <summary>対策達成度に応じたペナルティ係数を返す（未充足/一部/完全）。</summary>
        public static double GetMultiplier(EnvironmentTag tag, GimmickMitigationLevel level)
        {
            var def = Get(tag);
            return level switch
            {
                GimmickMitigationLevel.Full => def.FullMultiplier,
                GimmickMitigationLevel.Partial => def.PartialMultiplier,
                _ => def.NoneMultiplier,
            };
        }

        /// <summary>このギミックの対策点数を+1する携行アイテムのId（→ ConsumableCatalog）。</summary>
        public static string GetCounterItemId(EnvironmentTag tag) => Get(tag).CounterItemId;
    }
}
