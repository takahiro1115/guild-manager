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

        public QuestDispatchSystem(
            QuestResolver questResolver,
            GrowthSystem growthSystem,
            EconomySystem economySystem,
            SatisfactionSystem satisfactionSystem)
        {
            _questResolver = questResolver;
            _growthSystem = growthSystem;
            _economySystem = economySystem;
            _satisfactionSystem = satisfactionSystem;
        }

        /// <summary>
        /// パーティを派遣する。全メンバーを IsDispatched=true にし、ActiveDispatches へ追加する。
        /// この時点では解決しない（満了週まで待つ。1週クエストは同じ週の決算で解決される）。
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

                var result = _questResolver.Resolve(dispatch.Party, dispatch.Quest);
                _economySystem.ApplyReward(state, result.RewardGold);
                var growthEvents = _growthSystem.ProcessDeploymentGrowth(dispatch.Party, dispatch.Quest); // 経路1：満了週にのみ1回
                _satisfactionSystem.ApplyQuestAchievementBonus(dispatch.Party, dispatch.Quest, result.QuestAchieved);

                foreach (var member in dispatch.Party.Members)
                    member.IsDispatched = false;

                state.ActiveDispatches.Remove(dispatch);
                resolutions.Add(new DispatchResolution(dispatch.Party, dispatch.Quest, result, growthEvents));
            }

            return resolutions;
        }
    }
}
