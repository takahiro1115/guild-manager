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

        /// <summary>
        /// 研究を完了させる。素材・ゴールドを減算し、CompletedResearchIdsへ登録する。
        /// CanStartResearchがfalseを返す状況（未充足・完了済み）では何もせずfalseを返す
        /// （Try*系の共通パターンと同じ「呼び出し前に必ずしもCanXxxを経由しなくても安全」という設計）。
        /// </summary>
        public static bool CompleteResearch(GameState state, ResearchDefinition research)
        {
            if (!CanStartResearch(state, research))
                return false;

            state.Gold -= research.RequiredGold;
            foreach (var (materialId, requiredCount) in research.RequiredMaterials)
                state.Materials[materialId] -= requiredCount;

            state.CompletedResearchIds.Add(research.Id);
            return true;
        }
    }
}
