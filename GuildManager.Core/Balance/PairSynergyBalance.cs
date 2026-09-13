using System;
using System.Collections.Generic;
using System.Globalization;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// ペア特性シナジー（→ 03 §4.2.3、項目64で新設）の定義データとパラメータ。
    ///
    /// パーティ内の「異なる2名」がそれぞれ指定の特性を持つ時、指定クエスト種別の
    /// パーティ全体スコアに符号付きで加算される（計算本体は
    /// Systems.PairSynergyCalculator）。定義は docs/04_バランス表/pair_synergy.csv、
    /// 隊長LDRによる緩和係数は trait.csv（特性まわりの数値をまとめている既存ファイル。
    /// → BAL: 特性/ペアシナジー）から読み込む。
    /// </summary>
    public static class PairSynergyBalance
    {
        private const string FileName = "pair_synergy.csv";
        private const string TraitFileName = "trait.csv";

        /// <summary>quest_type列がこの値の行は、全クエスト種別に適用される。</summary>
        private const string AllQuestTypesKeyword = "All";

        /// <summary>ペア特性シナジー1件分の定義。</summary>
        /// <param name="TraitAId">組み合わせの一方の特性ID（→ TraitCatalog）。</param>
        /// <param name="TraitBId">組み合わせのもう一方の特性ID。</param>
        /// <param name="QuestType">対象クエスト種別。nullなら全種別が対象。</param>
        /// <param name="Value">成立1ペアあたりの加算量（符号あり。マイナスは仲違い）。</param>
        public readonly record struct PairSynergyDefinition(string TraitAId, string TraitBId, QuestType? QuestType, double Value)
        {
            /// <summary>この定義が指定クエスト種別に適用されるか。</summary>
            public bool AppliesTo(QuestType questType) => QuestType == null || QuestType == questType;
        }

        private static readonly PairSynergyDefinition[] Definitions = BuildDefinitions();

        private static PairSynergyDefinition[] BuildDefinitions()
        {
            var (_, rows) = BalanceData.GetTable(FileName);
            var result = new List<PairSynergyDefinition>();

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                string traitA = row[0];
                string traitB = row[1];
                string questTypeText = row[2];

                QuestType? questType = null;
                if (!string.Equals(questTypeText, AllQuestTypesKeyword, StringComparison.Ordinal))
                {
                    if (!Enum.TryParse<QuestType>(questTypeText, out var parsed))
                        throw new BalanceDataException(
                            $"{FileName} の{i + 2}行目のquest_type「{questTypeText}」をQuestTypeとして解釈できません" +
                            $"（全種別対象にする場合は「{AllQuestTypesKeyword}」と書いてください）。");
                    questType = parsed;
                }

                if (!double.TryParse(row[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    throw new BalanceDataException($"{FileName} の{i + 2}行目のvalue「{row[3]}」を数値として解釈できません。");

                result.Add(new PairSynergyDefinition(traitA, traitB, questType, value));
            }

            return result.ToArray();
        }

        /// <summary>定義済みのペア特性シナジー一覧（CSVの記載順）。</summary>
        public static IReadOnlyList<PairSynergyDefinition> GetDefinitions() => Definitions;

        /// <summary>
        /// 隊長LDRによる負のシナジーの緩和係数（→ 03 §4.2.3）。
        /// 緩和率＝clamp(隊長LDR×この係数, 0, 1)。合計がマイナスの時のみ働き、
        /// プラスの合計には一切影響しない。
        /// </summary>
        public static readonly double LdrMitigationCoefficient = BalanceData.GetDouble(TraitFileName, "PairSynergyLdrMitigationCoefficient");
    }
}
