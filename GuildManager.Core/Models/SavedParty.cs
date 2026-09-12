using System;
using System.Collections.Generic;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// 永続的なパーティー編成。仕様書 03 §4.0.2 参照（v1.9改訂で新設）。
    ///
    /// 設計メモ：既存の`Party`クラス（クエスト解決時に実際のAdventurer参照を保持する
    /// 実行時コンテナ。QuestResolver・CompatibilitySystem・GrowthSystem等で使用）とは
    /// 別のモデルとして新設した。指示書は既存`Party`自体をGuid参照ベースに拡張する案
    /// だったが、`Party.Members`（IReadOnlyList&lt;Adventurer&gt;）は本体コード14ファイル・
    /// 既存テスト6ファイルから直接参照されており、Guid参照ベースへの変更は戦闘解決系の
    /// 全ロジックとテストに連鎖する破壊的変更になる。両者の役割（「編成データの永続化」対
    /// 「解決時点の実際の出撃メンバー」）はそもそも異なるため、別モデルとして分離した
    /// （→ PartyFormationSystem.BuildDispatchPartyが、このSavedPartyから出撃可能な
    /// メンバーだけを解決し、既存の`Party`を組み立てる橋渡し役を担う）。
    ///
    /// 1人の冒険者は同時に1つのSavedPartyにしか所属できない（→ PartyFormationSystem.
    /// TryAssignMember）。どのSavedPartyのMemberIdsにも含まれない現役冒険者は
    /// 「未編成」として扱う（専用データ構造は持たず、都度フィルタで導出する）。
    /// </summary>
    public class SavedParty
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";

        /// <summary>所属する冒険者のId一覧（最大4名、→ Party.MaxSlots）。</summary>
        public List<Guid> MemberIds { get; set; } = new();
    }
}
