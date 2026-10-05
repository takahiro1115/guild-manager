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

        // ---- 部隊の方針（自動出撃、2026年10月・§0.63、→ Systems.SquadOrderSystem） ----

        /// <summary>部隊の方針。None 以外なら、週送りの前に空いていれば自動で出撃する。旧セーブには無く None で読まれる。</summary>
        public SquadOrder Order { get; set; } = SquadOrder.None;

        /// <summary>方針の対象フィールドのId（→ DungeonField.Id）。方針が None なら使わない。</summary>
        public string? OrderFieldId { get; set; }

        /// <summary>
        /// <see cref="OrderFieldId"/> にこの値を入れると「おまかせ」：出撃のたびに、方針に合うダンジョンを自動で選ぶ
        /// （→ SquadOrderSystem.ResolveField）。旧セーブの具体的なフィールドIdはそのまま読まれる。
        /// </summary>
        public const string AutoFieldId = "auto";

        /// <summary>
        /// 潜行の方針で扉前に着いたときの構え（§0.68）。構えの条件（→ SquadOrderBalance.For）を満たせば、着いた週のうちに挑み、
        /// 満たさなければ撤退する（→ SquadOrderSystem.JudgeEngage、§0.69）。
        /// </summary>
        public DoorStance Stance { get; set; } = DoorStance.Standard;
    }

    /// <summary>扉前の構え（→ SavedParty.Stance、Systems.SquadOrderSystem.JudgeEngage、03 §4.0.3・§0.68・§0.69）。</summary>
    public enum DoorStance
    {
        /// <summary>慎重：火力に余裕があり、すべてのギミックに万全の備えがあるときだけ挑む。</summary>
        Cautious,

        /// <summary>標準：撃破の見込みがあり、即死級に半分以上の備えがあれば挑む。</summary>
        Standard,

        /// <summary>強気：火力が少し足りなくても、備えが無くても挑む。</summary>
        Bold,
    }

    /// <summary>部隊の方針（→ SavedParty.Order、Systems.SquadOrderSystem、03 §4.0.3・§0.63）。</summary>
    public enum SquadOrder
    {
        /// <summary>方針なし（手動で出撃させる）。</summary>
        None,

        /// <summary>潜行を続ける：対象フィールドの次の未撃破ボスへ1階層から潜る。</summary>
        Dive,

        /// <summary>調査を続ける：対象フィールドの次のボスを調査する。完全解析なら、その週は同じフィールドで採取する。</summary>
        Survey,

        /// <summary>採取を続ける：対象フィールドで採取する。</summary>
        Gather,
    }
}
