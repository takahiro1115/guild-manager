using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// アルベールの研究室（素材投資システム）の実行エンジン。採取任務（→ GatheringResolver）で
    /// 集めた素材（→ GameState.Materials）とゴールドを投じて、恒久的なインフラバフ
    /// （→ ResearchEffectType）を獲得する。
    ///
    /// 研究には期間がある（2026年10月・§0.114）：研究室で進められるのは同時に1件（→ GameState.ResearchInProgress）。
    /// 始めたときに研究費と素材を払い、research.csv の Weeks 週たった週の決算で完了する（→ ProcessWeek）。
    /// 研究の手伝い（→ GameState.ResearchCredit、§0.73）は、貯まった分を使って研究を1週ぶん余計に進める（研究費の割り引きは §0.114 でやめた）。
    ///
    /// 乱数もパーティも扱わない純粋な判定・状態変更のため、PlacementRules・
    /// QuestScoreCalculatorと同じくstaticクラスにしている。
    /// </summary>
    public static class ResearchSystem
    {
        /// <summary>
        /// 研究を始められるか。以下のいずれかに該当すればfalse：
        /// 既に完了済み／前提が済んでいない／ほかの研究を進めている／ゴールドが足りない／必要素材のいずれかが足りない。
        /// </summary>
        public static bool CanStartResearch(GameState state, ResearchDefinition research)
        {
            if (state.IsResearchCompleted(research.Id))
                return false;
            if (!IsPrerequisiteMet(state, research))
                return false;
            if (state.ResearchInProgress != null)
                return false;
            if (state.Gold < research.RequiredGold)
                return false;

            foreach (var (materialId, requiredCount) in research.RequiredMaterials)
            {
                state.Materials.TryGetValue(materialId, out int have);
                if (have < requiredCount)
                    return false;
            }

            return true;
        }

        /// <summary>進めている研究の定義（研究していなければ null）。</summary>
        public static ResearchDefinition? InProgress(GameState state) =>
            state.ResearchInProgress is { } p ? ResearchBalance.Find(p.ResearchId) : null;

        /// <summary>その研究を進めている最中か。</summary>
        public static bool IsInProgress(GameState state, string researchId) => state.ResearchInProgress?.ResearchId == researchId;

        /// <summary>研究の手伝いで縮められる週の上限＝研究の週×ResearchMaxShortenRate（切り捨て）。</summary>
        public static int MaxShortenWeeks(ResearchDefinition research) =>
            (int)(research.Weeks * TrainingBalance.ResearchMaxShortenRate);

        /// <summary>
        /// 研究を始める：研究費と素材を払い、研究室で進める研究にする（→ GameState.ResearchInProgress）。
        /// CanStartResearch が false なら何もせず false。
        /// </summary>
        public static bool StartResearch(GameState state, ResearchDefinition research)
        {
            if (!CanStartResearch(state, research))
                return false;

            state.Gold -= research.RequiredGold;
            foreach (var (materialId, requiredCount) in research.RequiredMaterials)
                state.Materials[materialId] -= requiredCount;

            state.ResearchInProgress = new ResearchProgress { ResearchId = research.Id, WeeksRemaining = research.Weeks };
            return true;
        }

        /// <summary>進めている研究をやめる（払った研究費と素材は戻らない）。研究していなければ false。</summary>
        public static bool CancelResearch(GameState state)
        {
            if (state.ResearchInProgress == null)
                return false;
            state.ResearchInProgress = null;
            return true;
        }

        /// <summary>
        /// 週の決算（研究の手伝いが貯まったあと）：進めている研究を1週進める。研究の手伝いが ResearchCreditPerWeek 以上あり、
        /// 縮めた週が MaxShortenWeeks に届いていなければ、それを使ってもう1週進める。残り週が0になったら完了し、その研究を返す。
        /// 完了したら研究の手伝いは0に戻る（§0.151。次の研究へ持ち越さない）。
        /// </summary>
        public static ResearchDefinition? ProcessWeek(GameState state)
        {
            if (state.ResearchInProgress is not { } progress)
                return null;
            var research = ResearchBalance.Find(progress.ResearchId);
            if (research == null)
            {
                state.ResearchInProgress = null; // 定義から消えた研究（FromSaveData で捨てるが念のため）
                return null;
            }

            progress.WeeksRemaining--;
            if (progress.WeeksRemaining > 0 && state.ResearchCredit >= TrainingBalance.ResearchCreditPerWeek
                && progress.WeeksShortened < MaxShortenWeeks(research))
            {
                state.ResearchCredit -= TrainingBalance.ResearchCreditPerWeek;
                progress.WeeksShortened++;
                progress.WeeksRemaining--;
            }
            if (progress.WeeksRemaining > 0)
                return null;

            state.CompletedResearchIds.Add(research.Id);
            state.ResearchInProgress = null;
            state.ResearchCredit = 0; // 研究の手伝いは、その研究のためのもの。済んだら0から貯め直す（§0.151）
            return research;
        }

        // ---- 研究室のツリー（§0.77）：列＝必要な素材のフィールド、まだ入っていないフィールドの研究は「？」で見せる ----

        /// <summary>
        /// 研究が並ぶ列：必要な素材のフィールドの並び順（DungeonField.Order、森＝1〜深淵＝5）の最大。素材が無ければ1。
        /// </summary>
        public static int GetFieldOrder(GameState state, ResearchDefinition research)
        {
            int order = 1;
            foreach (var materialId in research.RequiredMaterials.Keys)
                if (FindMaterialField(state, materialId) is { } field)
                    order = Math.Max(order, field.Order);
            return order;
        }

        /// <summary>
        /// 研究の名前と効果を見せるか：済んでいる、または必要な素材のフィールドがすべて開放済み。
        /// まだ入っていないフィールドの素材が要る研究は、ツリーに札と線だけ出し「？」にする。
        /// </summary>
        public static bool IsRevealed(GameState state, ResearchDefinition research) =>
            state.IsResearchCompleted(research.Id)
            || research.RequiredMaterials.Keys.All(id => FindMaterialField(state, id)?.IsUnlocked ?? true);

        private static DungeonField? FindMaterialField(GameState state, string materialId) =>
            MaterialBalance.Find(materialId) is { } material
                ? state.DungeonFields.FirstOrDefault(f => f.Id == material.FieldId)
                : null;

        /// <summary>前提の研究（→ ResearchDefinition.PrerequisiteId、§0.60）が無いか、済んでいるか。</summary>
        public static bool IsPrerequisiteMet(GameState state, ResearchDefinition research) =>
            research.PrerequisiteId == null || state.IsResearchCompleted(research.PrerequisiteId);
    }
}
