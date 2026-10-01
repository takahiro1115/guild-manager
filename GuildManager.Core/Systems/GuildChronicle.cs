using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// エンディングに出すギルドの記録（→ 03 §8.2・§0.59、2026年10月新設）。Build でゲーム状態から作る（状態は変えない）。
    /// 「在籍した冒険者」は現役・引退者・除籍者の合計（契約解除で去った者は記録が残らないので数えない）。
    /// </summary>
    public sealed class GuildChronicle
    {
        /// <summary>クリアした週（未クリアなら今の週）。</summary>
        public int ClearedAtWeek { get; init; }

        /// <summary>クリアまでにかかった年数（暦の何年目か、→ GameCalendar.YearOf）。</summary>
        public int Years { get; init; }

        public int TotalMembers { get; init; }
        public int ActiveCount { get; init; }
        public int RetiredCount { get; init; }

        /// <summary>強制除籍（ロスト）した人数。</summary>
        public int ExpelledCount { get; init; }

        /// <summary>魂魄融和の秘薬で生まれた娘の人数（→ §5.4）。</summary>
        public int DaughterCount { get; init; }

        /// <summary>最も新しい世代（初期メンバー・採用＝第1世代、その娘＝第2世代…）。</summary>
        public int MaxGeneration { get; init; }

        public int BossesDefeated { get; init; }
        public int TotalBosses { get; init; }

        /// <summary>全ボスを倒したフィールドの数。</summary>
        public int FieldsConquered { get; init; }
        public int TotalFields { get; init; }

        public int TotalDispatchCount { get; init; }

        /// <summary>深淵100Fのボスを倒した部隊の生還者の名前（記録に無い者は除く）。</summary>
        public IReadOnlyList<string> ClearingMemberNames { get; init; } = Array.Empty<string>();

        /// <summary>累積功績が最も高い冒険者（在籍者がいなければ null）。</summary>
        public string? TopContributorName { get; init; }
        public int TopContributorScore { get; init; }

        public static GuildChronicle Build(GameState state)
        {
            var everyone = state.Adventurers.Concat(state.RetiredAdventurers).Concat(state.FallenAdventurers).ToList();
            var top = everyone.OrderByDescending(a => a.TotalContributionScore).FirstOrDefault();
            int clearedAt = state.ClearedAtWeek ?? state.WeekNumber;
            var bosses = state.DungeonFields.SelectMany(f => f.Bosses).ToList();

            return new GuildChronicle
            {
                ClearedAtWeek = clearedAt,
                Years = GameCalendar.YearOf(clearedAt),
                TotalMembers = everyone.Count,
                ActiveCount = state.Adventurers.Count,
                RetiredCount = state.RetiredAdventurers.Count,
                ExpelledCount = state.FallenAdventurers.Count,
                DaughterCount = everyone.Count(a => a.ParentIds.Count > 0),
                MaxGeneration = everyone.Count == 0 ? 0 : everyone.Max(a => GenerationOf(state, a)),
                BossesDefeated = bosses.Count(b => b.IsDefeated),
                TotalBosses = bosses.Count,
                FieldsConquered = state.DungeonFields.Count(f => f.Bosses.Count > 0 && f.Bosses.All(b => b.IsDefeated)),
                TotalFields = state.DungeonFields.Count,
                TotalDispatchCount = state.TotalDispatchCount,
                ClearingMemberNames = state.ClearingMemberIds.Select(id => state.FindAdventurer(id)?.Name).OfType<string>().ToList(),
                TopContributorName = top?.Name,
                TopContributorScore = top?.TotalContributionScore ?? 0,
            };
        }

        /// <summary>
        /// 世代：親のいない者は1、魂魄融和の娘は「親の世代の大きい方＋1」。記録に無い親は第1世代として数える。
        /// </summary>
        public static int GenerationOf(GameState state, Adventurer adventurer) =>
            GenerationOf(state, adventurer, new Dictionary<Guid, int>());

        private static int GenerationOf(GameState state, Adventurer adventurer, Dictionary<Guid, int> memo)
        {
            if (memo.TryGetValue(adventurer.Id, out int known)) return known;
            memo[adventurer.Id] = 1; // 壊れたデータで親子が循環しても止まるように、先に仮の値を入れる
            int generation = 1;
            foreach (var parentId in adventurer.ParentIds)
            {
                var parent = state.FindAdventurer(parentId);
                generation = Math.Max(generation, (parent == null ? 1 : GenerationOf(state, parent, memo)) + 1);
            }
            memo[adventurer.Id] = generation;
            return generation;
        }
    }
}
