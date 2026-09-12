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
    /// 仲間ロストの余波（ApplyPartyLossPenalty）は、致死判定の本実装（→ §4.3）に伴い
    /// QuestDispatchSystem.ProcessWeeklyDispatches から接続済み（戦死判定時に呼ばれる）。
    /// 人間関係：相性「険悪」による減点（→ §5.3.1）は、相性システムの実装（v1.4改訂）に
    /// 伴い接続済み（v1.5からの保留を解消）。
    /// </summary>
    public class SatisfactionSystem
    {
        /// <summary>
        /// 毎週の決算処理。出場機会・賃金妥当性・自然回復・相性険悪ペアによる満足度の増減と、
        /// 出撃履歴（連続不出撃週数）の更新を行う。
        /// </summary>
        /// <param name="state">ゲーム状態。</param>
        /// <param name="dispatchedAdventurerIds">今週出撃したパーティのメンバーID（出撃なしの週は空集合）。</param>
        public void ProcessWeeklySatisfaction(GameState state, IReadOnlySet<Guid> dispatchedAdventurerIds)
        {
            var hostilePairPenalty = ComputeHostilePairPenalty(state);

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

                // 人間関係：相性「険悪」のペアと同パーティで出撃している週、毎週-10（→ 03 §5.1・§5.3.1）。
                // 複数の険悪ペアに同時に該当する場合はその件数分だけ加算される。
                if (hostilePairPenalty.TryGetValue(adventurer.Id, out var penalty))
                    delta -= penalty;

                // 自然回復（→ 03 §5.1・§6）。ギルド酒場（Tavern）の現在Lvに連動する。
                delta += FacilityBalance.GetTavernSatisfactionRecovery(state.GetFacilityLevel(FacilityType.Tavern));

                Adjust(adventurer, delta);
            }
        }

        /// <summary>
        /// 現在進行中（派遣中）の全パーティについて、相性が険悪（30未満）なペアを洗い出し、
        /// 該当する各冒険者が今週受けるペナルティ合計を返す（→ 03 §5.1・§5.3.1）。
        /// 派遣中は毎週「同じパーティで出撃している」状態が続くため、拘束期間中は
        /// 満了週に限らず毎週この判定を行う。
        /// </summary>
        private static Dictionary<Guid, int> ComputeHostilePairPenalty(GameState state)
        {
            var penalty = new Dictionary<Guid, int>();

            foreach (var dispatch in state.ActiveDispatches)
            {
                var members = dispatch.Party.Members;
                for (int i = 0; i < members.Count; i++)
                {
                    for (int j = i + 1; j < members.Count; j++)
                    {
                        if (!CompatibilitySystem.IsHostile(state, members[i].Id, members[j].Id))
                            continue;

                        penalty[members[i].Id] = penalty.GetValueOrDefault(members[i].Id) + SatisfactionBalance.HostilePairPenalty;
                        penalty[members[j].Id] = penalty.GetValueOrDefault(members[j].Id) + SatisfactionBalance.HostilePairPenalty;
                    }
                }
            }

            return penalty;
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
        /// QuestDispatchSystem.ProcessWeeklyDispatchesが戦死判定時に呼び出す。
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
