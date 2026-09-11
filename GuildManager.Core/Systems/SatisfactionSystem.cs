using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 満足度（Satisfaction）変動と契約交渉・退団フロー。仕様書 03 §5.1・§5.2 参照。
    ///
    /// 自然回復の酒場Lv連動（→ §6）は施設Lv投資システムの実装により接続済み。
    /// 依存先システムが依然として未実装のため、今回のスコープに含めない項目：
    ///  - 人間関係：相性「険悪」による減点（→ §5.3 相性・特性システム、未実装）。
    ///  - 仲間ロストの余波（→ §4.3 不可逆障害・戦死＝ロスト、未実装）。ApplyPartyLossPenalty
    ///    メソッドとしては用意するが、死亡イベント自体が無いため現状どこからも呼び出されない
    ///    （§4.3実装時にQuestResolver側から接続する想定）。
    /// </summary>
    public class SatisfactionSystem
    {
        /// <summary>
        /// 毎週の決算処理。出場機会・賃金妥当性・自然回復による満足度の増減と、
        /// 出撃履歴（連続不出撃週数）の更新を行う。
        /// </summary>
        /// <param name="state">ゲーム状態。</param>
        /// <param name="dispatchedAdventurerIds">今週出撃したパーティのメンバーID（出撃なしの週は空集合）。</param>
        public void ProcessWeeklySatisfaction(GameState state, IReadOnlySet<Guid> dispatchedAdventurerIds)
        {
            foreach (var adventurer in state.Adventurers)
            {
                // 各要因の増減は単純に加算し、まとめて1回だけ0〜100にクランプする
                // （個別にクランプすると、例えば「大きく減点された直後の自然回復+1」だけが
                // 下限0から救われてしまうなど、適用順序で結果が変わってしまうため）。
                int delta = 0;

                if (dispatchedAdventurerIds.Contains(adventurer.Id))
                {
                    adventurer.WeeksSinceLastDeployment = 0;
                }
                else
                {
                    adventurer.WeeksSinceLastDeployment++;

                    // 出場機会：22〜27歳が「4週連続で遠征なし」なら毎週-5（→ 03 §5.1）。
                    if (IsPrimeAgeForOuting(adventurer.Age) &&
                        adventurer.WeeksSinceLastDeployment >= SatisfactionBalance.NoDeploymentWeeksThreshold)
                    {
                        delta -= SatisfactionBalance.NoDeploymentPenalty;
                    }
                }

                // 賃金妥当性：週給が適正値の80%未満なら毎週-8（→ 03 §5.1）。
                if (adventurer.WeeklyWage < GetAppropriateWage(adventurer) * SatisfactionBalance.WageAdequacyRatio)
                    delta -= SatisfactionBalance.UnderpaidPenalty;

                // 自然回復（→ 03 §5.1・§6）。ギルド酒場（Tavern）の現在Lvに連動する。
                delta += FacilityBalance.GetTavernSatisfactionRecovery(state.GetFacilityLevel(FacilityType.Tavern));

                Adjust(adventurer, delta);
            }
        }

        /// <summary>勝利・功績ボーナス：Bランク以上のクエスト達成でパーティ全員+10（→ 03 §5.1）。</summary>
        public void ApplyQuestAchievementBonus(Party party, Quest quest, bool questAchieved)
        {
            if (!questAchieved || quest.Rank < QuestRank.B)
                return;

            foreach (var member in party.Members)
                Adjust(member, SatisfactionBalance.VictoryBonus);
        }

        /// <summary>
        /// 仲間ロストの余波：同パーティの死亡で一律-30（→ 03 §4.3・§5.1）。
        /// §4.3の致死判定が未実装のため、現状どこからも呼び出されない
        /// （実装済みのメソッドとして用意するのみ）。
        /// </summary>
        public void ApplyPartyLossPenalty(Party party, Guid lostAdventurerId)
        {
            foreach (var member in party.Members)
            {
                if (member.Id == lostAdventurerId)
                    continue;

                Adjust(member, -SatisfactionBalance.PartyLossPenalty);
            }
        }

        /// <summary>
        /// 契約交渉の週次判定（→ 03 §5.2）。満足度20未満で警告フラグを立て、
        /// 2週を超えて未対応（昇給・ボーナスの実行が無い）なら契約解除しロースターから除外する。
        /// 契約解除された冒険者を返す（除去後も参照できるよう Adventurer 自体を返す。
        /// 週報ログでの氏名表示などに使う）。
        /// </summary>
        public List<Adventurer> ProcessWeeklyNegotiation(GameState state)
        {
            var terminated = new List<Adventurer>();

            // 決算中にstate.Adventurersから除去することがあるため、スナップショットで走査する
            // （AgingSystem.ProcessWeeklyAgingと同じ理由）。
            foreach (var adventurer in state.Adventurers.ToList())
            {
                if (adventurer.NeedsNegotiation)
                {
                    if (adventurer.NegotiationWeeksElapsed >= SatisfactionBalance.NegotiationGraceWeeks)
                    {
                        Terminate(state, adventurer);
                        terminated.Add(adventurer);
                        continue;
                    }

                    adventurer.NegotiationWeeksElapsed++;
                }
                else if (adventurer.Satisfaction < SatisfactionBalance.NegotiationThreshold)
                {
                    adventurer.NeedsNegotiation = true;
                    adventurer.NegotiationWeeksElapsed = 0;
                }
            }

            return terminated;
        }

        /// <summary>
        /// 「昇給する」対応（→ 03 §5.2）。週給を multiplier 倍に引き上げ、警告状態を解除する。
        /// multiplier は仕様の範囲（1.5〜2.0倍）にクランプする。
        /// </summary>
        public void RaiseWage(Adventurer adventurer, double multiplier)
        {
            double clamped = Math.Clamp(multiplier, SatisfactionBalance.MinRaiseMultiplier, SatisfactionBalance.MaxRaiseMultiplier);
            adventurer.WeeklyWage = (int)Math.Round(adventurer.WeeklyWage * clamped);
            ResolveNegotiation(adventurer);
        }

        /// <summary>
        /// 「ボーナスを払う」対応（→ 03 §5.2）。一時金（週給×仮値の倍数）を支給し、警告状態を解除する。
        /// </summary>
        public void PayBonus(GameState state, Adventurer adventurer)
        {
            state.Gold -= adventurer.WeeklyWage * SatisfactionBalance.BonusWeeksEquivalent;
            ResolveNegotiation(adventurer);
        }

        private static void ResolveNegotiation(Adventurer adventurer)
        {
            adventurer.NeedsNegotiation = false;
            adventurer.NegotiationWeeksElapsed = 0;
        }

        /// <summary>
        /// 自発的な契約解除（→ 03 §5.2「他都市へ移籍（消滅）」）。§4.3の戦死や§3.7の引退とは異なり、
        /// 顧問候補として保持する必要が無いため、単純にロースターから取り除く（削除のみ）。
        /// </summary>
        private static void Terminate(GameState state, Adventurer adventurer)
        {
            state.TrainingAssignments.Remove(adventurer.Id); // 訓練場配置からも外れる（枠を解放）
            state.Adventurers.Remove(adventurer);
        }

        private static bool IsPrimeAgeForOuting(int age) => age >= 22 && age <= 27; // §5.1「22〜27歳」

        private static double GetAppropriateWage(Adventurer adventurer) =>
            adventurer.TotalPA * SatisfactionBalance.AppropriateWageCoefficient;

        private static void Adjust(Adventurer adventurer, int delta) =>
            adventurer.Satisfaction = Math.Clamp(adventurer.Satisfaction + delta, SatisfactionBalance.Min, SatisfactionBalance.Max);
    }
}
