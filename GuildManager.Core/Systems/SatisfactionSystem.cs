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
    /// DungeonExpeditionSystem（ボス討伐の強制除籍時）から接続済み。
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

                    // 出場機会：全盛期（23〜26歳）が「4週連続で遠征なし」なら毎週-5（→ 03 §5.1）。
                    if (IsPeakAgeForOuting(adventurer.Age) &&
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
        /// 大迷宮へ出撃中（→ GameState.ActiveDungeonMissions）の全部隊について、相性が険悪（30未満）なペアを洗い出し、
        /// 該当する各冒険者が今週受けるペナルティ合計を返す（→ 03 §5.1・§5.3.1）。
        /// 出撃中は毎週「同じ部隊で出撃している」状態が続くため、複数週の潜行中は
        /// 帰還週に限らず毎週この判定を行う（旧通常クエストの派遣一覧から、旧クエスト撤去時に参照先を移した）。
        /// </summary>
        private static Dictionary<Guid, int> ComputeHostilePairPenalty(GameState state)
        {
            var penalty = new Dictionary<Guid, int>();

            foreach (var dispatch in state.ActiveDungeonMissions)
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

        /// <summary>
        /// 大迷宮の任務成果による士気の変動（→ 03 §5.1「勝利・功績」、BAL: satisfaction.csv Satisfaction_*）。
        /// 旧通常クエストの「勝利・功績ボーナス」を、大迷宮の任務成果へ再配線したもの：
        ///  - ボス討伐（BossAssault）：撃破（succeeded）なら +BossVictory、撤退・敗退なら +BossDefeat（負の値）
        ///  - 迷宮調査（Survey）：護衛段階（surveyGuardTier）で 余裕 +2／十分 +1／充足 0／不足 -5
        ///  - 道中進軍（Scouting＝潜行）：進軍して生還した（succeeded）週に +TraversalSuccess
        ///  - 採取（Gathering）：素材を持ち帰れた（succeeded）時に +GatheringSuccess（取れなければ変動なし）
        /// 対象は部隊の生存者のみ（現役ロースターに残り、HPが1以上の者。強制除籍者は対象外）。
        /// 満足度は0〜100にクランプする。適用した変動量（対象者1人あたり）を返す。
        /// 強制除籍が出た場合の「仲間ロストの余波」（-30、→ ApplyPartyLossPenalty）は別途
        /// DungeonExpeditionSystem が除籍処理の中で適用する（本メソッドとは加算で重なる）。
        /// </summary>
        public int ApplyExpeditionSatisfaction(
            GameState state, Party party, DungeonMissionType missionType, bool succeeded, GuardTier? surveyGuardTier = null)
        {
            int delta = ExpeditionSatisfactionDelta(missionType, succeeded, surveyGuardTier);
            if (delta == 0)
                return 0;

            foreach (var member in party.Members)
            {
                if (member.CurrentHP <= 0 || !state.Adventurers.Contains(member))
                    continue;
                Adjust(member, delta);
            }

            return delta;
        }

        /// <summary>任務成果ごとの士気の変動量（→ ApplyExpeditionSatisfaction）。</summary>
        public static int ExpeditionSatisfactionDelta(DungeonMissionType missionType, bool succeeded, GuardTier? surveyGuardTier = null) =>
            missionType switch
            {
                DungeonMissionType.BossAssault => succeeded
                    ? SatisfactionBalance.ExpeditionBossVictory
                    : SatisfactionBalance.ExpeditionBossDefeat,
                DungeonMissionType.Survey => surveyGuardTier switch
                {
                    GuardTier.Abundant => SatisfactionBalance.ExpeditionSurveyAbundant,
                    GuardTier.Sufficient => SatisfactionBalance.ExpeditionSurveySufficient,
                    GuardTier.Deficient => SatisfactionBalance.ExpeditionSurveyDeficient,
                    _ => 0, // 充足・判定なし
                },
                DungeonMissionType.Scouting => succeeded ? SatisfactionBalance.ExpeditionTraversalSuccess : 0,
                DungeonMissionType.Gathering => succeeded ? SatisfactionBalance.ExpeditionGatheringSuccess : 0,
                _ => 0,
            };

        /// <summary>
        /// 仲間ロストの余波：同パーティの死亡で一律-30（→ 03 §4.3・§5.1）。
        /// DungeonExpeditionSystemがボス討伐の強制除籍時に呼び出す。
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
        ///
        /// 2026年9月改訂（→ §4.2.2「離脱時の自動回収」）：ロースターから外す**直前に**全装備を
        /// ギルド保管庫へ回収する（→ EquipmentSystem.UnequipAllToArmory）。移籍する本人は
        /// 記録としても残らないため、装備したまま消えると武具も一緒に失われてしまう
        /// （ギルドの備品は置いていってもらう、という扱い）。
        /// </summary>
        private static void Terminate(GameState state, Adventurer adventurer)
        {
            EquipmentSystem.UnequipAllToArmory(state, adventurer, $"{adventurer.Name}（退団）から返還");

            state.TrainingAssignments.Remove(adventurer.Id); // 訓練場配置からも外れる（枠を解放）
            state.Adventurers.Remove(adventurer);
        }

        /// <summary>
        /// 出場機会ペナルティの対象年齢か（→ 03 §5.1）。年齢帯の3区分化（→ 03 §0.11）に伴い、
        /// 対象を新・全盛期（23〜26歳、→ BAL: satisfaction.csv）へ整合させた。22歳（成長期）は
        /// 育成猶予として対象外（→ 03 §0.12）。
        /// </summary>
        private static bool IsPeakAgeForOuting(int age) =>
            age >= SatisfactionBalance.OpportunityPenaltyMinAge && age <= SatisfactionBalance.OpportunityPenaltyMaxAge;

        private static double GetAppropriateWage(Adventurer adventurer) =>
            adventurer.TotalPA * SatisfactionBalance.AppropriateWageCoefficient;

        private static void Adjust(Adventurer adventurer, int delta) =>
            adventurer.Satisfaction = Math.Clamp(adventurer.Satisfaction + delta, SatisfactionBalance.Min, SatisfactionBalance.Max);
    }
}
