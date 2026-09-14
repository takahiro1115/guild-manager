using System.Collections.Generic;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// ランク昇格試験の突破によって発生した変化のまとめ（→ コアシステム刷新仕様 Phase 4）。
    /// UI側（Godot）はこれを見て「昇格演出・第2部隊枠の開放告知・新人の紹介」を行う想定。
    /// </summary>
    public class PromotionExamResult
    {
        /// <summary>昇格後のギルドランク。</summary>
        public GuildRank NewRank { get; init; }

        /// <summary>拡張後の同時出撃枠（→ GameState.UnlockedSquadSlots）。</summary>
        public int UnlockedSquadSlots { get; init; }

        /// <summary>支給された昇格報奨金（クエスト報酬とは別枠）。</summary>
        public int RewardGold { get; init; }

        /// <summary>
        /// 昇格に伴い酒場へ補充された新規冒険者の応募一覧（→ RecruitmentSystem.GenerateCandidates）。
        /// 実際に雇うかどうかはプレイヤーが選ぶ（→ RecruitmentSystem.TryHire）。
        /// </summary>
        public IReadOnlyList<RecruitmentOffer> NewHireOffers { get; init; } = new List<RecruitmentOffer>();
    }
}
