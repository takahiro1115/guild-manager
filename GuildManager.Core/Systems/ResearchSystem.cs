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
    /// 乱数もパーティも扱わない純粋な判定・状態変更のため、PlacementRules・
    /// QuestScoreCalculatorと同じくstaticクラスにしている。
    /// </summary>
    public static class ResearchSystem
    {
        /// <summary>
        /// 研究を開始（実行）できるか。以下のいずれかに該当すればfalse：
        /// 既に完了済み／ゴールドが足りない／必要素材のいずれかが足りない。
        /// </summary>
        public static bool CanStartResearch(GameState state, ResearchDefinition research)
        {
            if (state.IsResearchCompleted(research.Id))
                return false;
            if (!IsPrerequisiteMet(state, research))
                return false;
            if (state.Gold < GetGoldToPay(state, research))
                return false;

            foreach (var (materialId, requiredCount) in research.RequiredMaterials)
            {
                state.Materials.TryGetValue(materialId, out int have);
                if (have < requiredCount)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 研究の手伝い（→ GameState.ResearchCredit、§0.73）で割り引ける額＝min(貯まった分, 研究費×ResearchCreditMaxDiscountRate（切り捨て）)。
        /// </summary>
        public static int GetDiscount(GameState state, ResearchDefinition research) =>
            Math.Max(0, Math.Min(state.ResearchCredit, (int)(research.RequiredGold * TrainingBalance.ResearchCreditMaxDiscountRate)));

        /// <summary>研究の手伝いで割り引いたあとに払う研究費。</summary>
        public static int GetGoldToPay(GameState state, ResearchDefinition research) =>
            research.RequiredGold - GetDiscount(state, research);

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

        /// <summary>
        /// 研究を完了させる。素材・ゴールドを減算し、CompletedResearchIdsへ登録する。
        /// CanStartResearchがfalseを返す状況（未充足・完了済み）では何もせずfalseを返す
        /// （Try*系の共通パターンと同じ「呼び出し前に必ずしもCanXxxを経由しなくても安全」という設計）。
        /// </summary>
        public static bool CompleteResearch(GameState state, ResearchDefinition research)
        {
            if (!CanStartResearch(state, research))
                return false;

            int discount = GetDiscount(state, research);
            state.Gold -= research.RequiredGold - discount;
            state.ResearchCredit -= discount; // 研究の手伝い（§0.73）は使った分だけ減る
            foreach (var (materialId, requiredCount) in research.RequiredMaterials)
                state.Materials[materialId] -= requiredCount;

            state.CompletedResearchIds.Add(research.Id);
            return true;
        }
    }
}
