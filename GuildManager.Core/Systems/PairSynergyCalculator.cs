using System;
using System.Collections.Generic;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// ペア特性シナジーの計算（→ 03 §4.2.3、項目64で新設）。統一点数計算式における
    /// 「パーティ全体への加算項」を求める。定義データは PairSynergyBalance
    /// （pair_synergy.csv）が持つ。
    ///
    /// ルール：
    ///  - パーティ内の**異なる2名**が、定義された特性の組み合わせをそれぞれ満たすと1ペア成立。
    ///  - 同じ組み合わせが複数ペア成立する場合は合算する
    ///    （例：豪胆2名・注意深い1名なら2ペア成立＝2倍）。
    ///  - **1名が両方の特性を兼ねている場合は成立しない**（「異なる2名」の条件を満たさないため）。
    ///  - 合計がマイナスの場合のみ、隊長（先頭メンバー。索敵・致死判定と同じ扱い）のLDRに
    ///    応じて絶対値を圧縮する。プラスの合計には一切影響しない。
    ///
    /// Systemクラスだが乱数もGameStateも使わない純粋な計算のため、他のSystemと違い
    /// staticクラスにしている（→ QuestResolverから直接呼ぶ）。
    /// </summary>
    public static class PairSynergyCalculator
    {
        /// <summary>
        /// パーティ全体スコアへ加算するペア特性シナジーの合計（隊長LDRによる緩和適用後）。
        /// </summary>
        public static double Calculate(IReadOnlyList<Adventurer> members, QuestType questType)
        {
            double raw = CalculateRawTotal(members, questType);
            if (members.Count == 0)
                return raw;

            double leaderLdr = members[0].GetEffectiveStat("LDR"); // MVP: 先頭メンバーを隊長とみなす
            return ApplyLeaderMitigation(raw, leaderLdr);
        }

        /// <summary>
        /// 緩和前のペア特性シナジー合計。成立ペア数×定義値をすべて足し合わせる。
        /// public にしてあるのはユニットテストから緩和前後を切り分けて確認できるようにするため。
        /// </summary>
        public static double CalculateRawTotal(IReadOnlyList<Adventurer> members, QuestType questType)
        {
            double total = 0;

            foreach (var definition in PairSynergyBalance.GetDefinitions())
            {
                if (!definition.AppliesTo(questType))
                    continue;

                total += definition.Value * CountPairs(members, definition.TraitAId, definition.TraitBId);
            }

            return total;
        }

        /// <summary>
        /// 隊長LDRによる負のシナジーの緩和（→ 03 §4.2.3）。
        /// 合計がマイナスの場合のみ、絶対値を 1−clamp(隊長LDR×係数, 0, 1) 倍に圧縮する。
        /// プラス（および0）はそのまま返す。
        /// public にしてあるのはユニットテストから直接呼べるようにするため。
        /// </summary>
        public static double ApplyLeaderMitigation(double rawTotal, double leaderLdr)
        {
            if (rawTotal >= 0)
                return rawTotal;

            double mitigationRate = Math.Clamp(leaderLdr * PairSynergyBalance.LdrMitigationCoefficient, 0, 1);
            return rawTotal * (1 - mitigationRate);
        }

        /// <summary>
        /// パーティ内で「異なる2名」により成立するペアの数を数える。
        /// メンバーの全組み合わせ（i &lt; j）を走査し、片方がtraitA・もう片方がtraitBを
        /// 持っていれば1ペアとする（どちらの向きでも成立）。1名が両方を兼ねていても、
        /// 相手側がもう一方の特性を持たない限りペアにはならない。
        /// </summary>
        private static int CountPairs(IReadOnlyList<Adventurer> members, string traitAId, string traitBId)
        {
            int count = 0;

            for (int i = 0; i < members.Count; i++)
            {
                for (int j = i + 1; j < members.Count; j++)
                {
                    bool forward = members[i].HasTrait(traitAId) && members[j].HasTrait(traitBId);
                    bool backward = members[i].HasTrait(traitBId) && members[j].HasTrait(traitAId);

                    if (forward || backward)
                        count++;
                }
            }

            return count;
        }
    }
}
