using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 複数週クエストの派遣・解決オーケストレーション。仕様書 03 §4.0.1 参照。
    ///
    /// 出撃時は即座に解決せず、拘束期間（Quest.DurationWeeks）分の「派遣中」状態を経て、
    /// 満了週にまとめて1回だけ索敵〜治安の解決（QuestResolver）と成長ロール経路1（出撃による
    /// 成長、GrowthSystem.ProcessDeploymentGrowth）を実行する。1週クエスト（Scale=Small、
    /// 既存の全クエストのデフォルト）は「WeeksRemaining=1で開始し同じ週の決算で0に到達する」
    /// ことで、これまでどおり派遣した週のうちに結果が出る（挙動に変化なし）。
    ///
    /// - 派遣中は何も起きない：HP自然回復（§3.5）・訓練場配置による成長（経路2）は
    ///   Adventurer.IsDispatched を見て除外される（RestRecoverySystem・GrowthSystem側で対応済み）。
    /// - 週給は派遣中も毎週通常どおり発生する（EconomySystem側は変更しない）。
    /// - 派遣中のメンバーは Adventurer.IsAvailable が false になり、他クエストへの再編成・
    ///   訓練場への配置ができない。
    /// - 中断（呼び戻し）は実装しない。結果が出るまで待つ以外の選択肢は無い。
    /// </summary>
    public class QuestDispatchSystem
    {
        private readonly QuestResolver _questResolver;
        private readonly GrowthSystem _growthSystem;
        private readonly EconomySystem _economySystem;
        private readonly SatisfactionSystem _satisfactionSystem;
        private readonly CompatibilitySystem _compatibilitySystem;

        public QuestDispatchSystem(
            QuestResolver questResolver,
            GrowthSystem growthSystem,
            EconomySystem economySystem,
            SatisfactionSystem satisfactionSystem,
            CompatibilitySystem compatibilitySystem)
        {
            _questResolver = questResolver;
            _growthSystem = growthSystem;
            _economySystem = economySystem;
            _satisfactionSystem = satisfactionSystem;
            _compatibilitySystem = compatibilitySystem;
        }

        /// <summary>
        /// 同時出撃枠（→ GameState.UnlockedSquadSlots、コアシステム刷新仕様「4. 進行管理」）に
        /// 空きがあるか。枠は「部隊の数」であって人数ではない：1枠の中で1〜4名を自由に
        /// 割り振れる（フリーアサイン）。
        /// </summary>
        public static bool CanDispatch(GameState state) =>
            state.ActiveDispatches.Count < state.UnlockedSquadSlots;

        /// <summary>
        /// 同時出撃枠を確認した上でパーティを派遣する。枠が埋まっていれば何もせずfalseを返す
        /// （Party.TryAdd等と同じTry*系のパターン）。UI側はこちらを使うこと。
        /// </summary>
        public bool TryDispatch(GameState state, Party party, Quest quest)
        {
            if (!CanDispatch(state))
                return false;

            Dispatch(state, party, quest);
            return true;
        }

        /// <summary>
        /// パーティを派遣する。全メンバーを IsDispatched=true にし、ActiveDispatches へ追加する。
        /// この時点では解決しない（満了週まで待つ。1週クエストは同じ週の決算で解決される）。
        ///
        /// 注意：このメソッド自体は同時出撃枠を検査しない（既存の呼び出し・テストとの互換のため）。
        /// 枠の制限を効かせたい通常の導線からは TryDispatch を使うこと。
        /// </summary>
        public void Dispatch(GameState state, Party party, Quest quest)
        {
            var dispatch = new ActiveDispatch
            {
                Party = party,
                Quest = quest,
                WeeksRemaining = quest.DurationWeeks,
            };
            state.ActiveDispatches.Add(dispatch);

            // 受注済みになったクエストは受注可能一覧から外す（→ 03 §4.0・§4.4）。
            // 外しておかないと、派遣中（拘束期間中）もQuestBoardSystemの期限切れ判定の対象に
            // なり続けてしまい、受注済みのクエストが誤って「放置」扱いされてしまう。
            state.AvailableQuests.Remove(quest);

            // 累計出撃回数（→ GameState.TotalDispatchCount）。昇格試験の提示条件
            // （→ GuildProgressionSystem）に使うため、クエストの成否に関わらず出撃した時点で数える。
            state.TotalDispatchCount++;

            foreach (var member in party.Members)
                member.IsDispatched = true;
        }

        /// <summary>
        /// 週次決算処理。全ての派遣中案件の残り週数を1減らし、0に到達したものだけを解決する。
        /// 満了した案件の結果一覧を返す（UI側の週報表示用。何も満了しなければ空リスト）。
        /// </summary>
        public List<DispatchResolution> ProcessWeeklyDispatches(GameState state)
        {
            var resolutions = new List<DispatchResolution>();

            // 満了した案件をActiveDispatchesから取り除くため、スナップショットで走査する
            // （AgingSystem.ProcessWeeklyAgingと同じ理由）。
            foreach (var dispatch in state.ActiveDispatches.ToList())
            {
                dispatch.WeeksRemaining--;
                if (dispatch.WeeksRemaining > 0)
                    continue; // まだ派遣中（移動中）：何もしない

                double advisorBonus = GetAdvisorBonus(state); // → 03 §7.2：参謀ボーナス（PartyScout・SurvivalThresholdの両方に加算）
                var result = _questResolver.Resolve(dispatch.Party, dispatch.Quest, advisorBonus);
                _economySystem.ApplyReward(state, result.RewardGold);

                // 経路1：満了週にのみ1回。戦死した者は成長ロールの結果を報告しない
                // （直後に「〇〇が戦死しました」と報告するのに、その直前に成長報告が出るのは
                // 不自然なため → 03 §4.3）。
                var growthEvents = _growthSystem.ProcessDeploymentGrowth(dispatch.Party, dispatch.Quest)
                    .Where(e => !result.FallenAdventurerIds.Contains(e.Adventurer.Id))
                    .ToList();

                _satisfactionSystem.ApplyQuestAchievementBonus(dispatch.Party, dispatch.Quest, result.QuestAchieved);

                // 累積功績（→ Adventurer.TotalContributionScore、8年稼働・満期引退モデル）。
                // 退職金の上乗せ分の算定根拠になる：危険な任務を多く引き受けた冒険者ほど
                // 手厚く送り出される（→ AgingSystem.CalculateSeverancePay）。
                // 達成できなかった任務でも、危険に身を晒したこと自体は功績として記録する
                // （失敗を理由に退職金を削らない＝ギルドマスターの方針）。
                ApplyContributionScore(dispatch.Party, dispatch.Quest, result.QuestAchieved);

                // 相性（→ 03 §5.3.1）：同パーティで出撃した全ペアの相性を達成/失敗に応じて増減。
                _compatibilitySystem.ApplyQuestOutcome(state, dispatch.Party, result.QuestAchieved);

                // 戦死処理（→ 03 §4.3.1）：ロースターから除外し戦死者記録へ移す。
                // 仲間ロストの余波（§5.1）として、生存メンバー全員の満足度を-30する。
                // あわせて、居合わせた生存者同士の相性を大きく下降させ、確率でトラウマを付与する
                // （→ 03 §5.3.1）。
                foreach (var fallenId in result.FallenAdventurerIds)
                {
                    _satisfactionSystem.ApplyPartyLossPenalty(dispatch.Party, fallenId);
                    _compatibilitySystem.ApplyDeathAftermath(state, dispatch.Party, fallenId);

                    var fallen = dispatch.Party.Members.FirstOrDefault(m => m.Id == fallenId);
                    if (fallen == null)
                        continue;

                    fallen.FellAtWeek = state.WeekNumber;
                    state.TrainingAssignments.Remove(fallen.Id); // 訓練場配置からも外れる（枠を解放。AgingSystem.Retireと同じ防御的処理）
                    state.Adventurers.Remove(fallen);
                    state.FallenAdventurers.Add(fallen);
                }

                foreach (var member in dispatch.Party.Members)
                    member.IsDispatched = false;

                state.ActiveDispatches.Remove(dispatch);
                resolutions.Add(new DispatchResolution(dispatch.Party, dispatch.Quest, result, growthEvents));
            }

            return resolutions;
        }

        /// <summary>
        /// 出撃した全メンバーへ累積功績を加算する（→ Adventurer.TotalContributionScore）。
        /// 功績量はクエストの難易度に比例させ、達成した場合はさらに上乗せする
        /// （難しい任務を完遂した者ほど、引退時の退職金が厚くなる）。
        /// 戦死者への加算は行わない（退職金を受け取る主体が居ないため）。
        /// </summary>
        private static void ApplyContributionScore(Party party, Quest quest, bool questAchieved)
        {
            int score = Math.Max(1, quest.Difficulty / ContributionDifficultyDivisor);
            if (questAchieved)
                score *= ContributionAchievementMultiplier;

            foreach (var member in party.Members)
                member.TotalContributionScore += score;
        }

        /// <summary>
        /// 功績量の算出パラメータ。難易度スケール（1〜100）を功績ポイントへ落とし込むための
        /// 構造的な除数・倍率であり、調整対象の「バランス値」ではないためCSV化していない
        /// （実際の退職金額は EconomyBalance.SeveranceContributionCoefficient 側で調整する）。
        /// </summary>
        private const int ContributionDifficultyDivisor = 10;
        private const int ContributionAchievementMultiplier = 2;

        /// <summary>作戦資料室に配置されている参謀がいれば、そのボーナスを返す（未配置なら0。→ 03 §7.2）。</summary>
        private static double GetAdvisorBonus(GameState state)
        {
            if (state.AssignedAdvisor == null) return 0;

            var advisor = state.RetiredAdventurers.FirstOrDefault(a => a.Id == state.AssignedAdvisor.Value);
            return advisor == null ? 0 : AdvisorSystem.GetAdvisorBonus(advisor);
        }
    }
}
