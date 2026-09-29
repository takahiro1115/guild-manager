using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 固有武具（固定アーティファクト・伝説級、→ 03 §4.7.5、2026年9月・§0.45、ハクスラ Step 3）の入手処理。
    ///
    /// 固有武具は**1つのセーブにつき1本**：一度入手したIdは GameState.ObtainedUniqueIds に記録され、
    /// 売却・ロストで手元から消えても二度と出ない（「もう二度と出んぞ」の一点物）。
    ///  - 固定アーティファクト（紫）：鑑定で武具が出たとき、未入手の中から抽選（→ AppraisalSystem）
    ///  - 伝説級（金）：割り当てられた階層ボスの初回撃破で確定入手（→ DungeonExpeditionSystem.EngageBoss）
    ///
    /// 乱数を引くのは鑑定側だけで、ここは状態の読み書きのみ（静的メソッド）。
    /// </summary>
    public static class UniqueItemSystem
    {
        /// <summary>まだ入手していない固定アーティファクト（鑑定の抽選の母集団。CSVの行順）。</summary>
        public static IReadOnlyList<UniqueDefinition> GetAvailableArtifacts(GameState state) =>
            UniqueBalance.Artifacts.Where(d => !state.ObtainedUniqueIds.Contains(d.Id)).ToList();

        /// <summary>
        /// 固有武具を入手する：個体を作ってギルド保管庫へ入れ、入手済みとして記録する。
        /// 既に入手済みのIdなら何もせず null（二重入手を構造的に防ぐ）。
        /// </summary>
        public static EquipmentItem? Grant(GameState state, UniqueDefinition unique, string acquiredFrom, ItemRarity? rarity = null)
        {
            if (!state.ObtainedUniqueIds.Add(unique.Id))
                return null;

            var item = EquipmentItem.FromUnique(unique, state.WeekNumber, acquiredFrom, rarity);
            state.Armory.Add(item);
            return item;
        }

        /// <summary>
        /// 階層ボス撃破時の伝説級の確定入手。そのボスに伝説級が割り当てられていない・入手済みなら null。
        /// </summary>
        public static EquipmentItem? TryGrantBossDrop(GameState state, DungeonField field, FloorBoss boss)
        {
            var unique = UniqueBalance.FindBossDrop(field.Id, boss.Floor);
            return unique == null ? null : Grant(state, unique, $"{field.Name} 第{boss.Floor}層の主「{boss.Name}」を撃破");
        }
    }
}
