using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// パーティー編成（永続化）の管理。仕様書 03 §4.0.2 参照（v1.9改訂で新設）。
    ///
    /// - パーティーは事前に編成して保存しておける永続的な単位（→ GameState.SavedParties）。
    ///   クエストごとに毎回4人を選び直す必要はない。
    /// - 1人の冒険者は同時に1つのSavedPartyにしか所属できない（→ TryAssignMember）。
    /// - どのSavedPartyのMemberIdsにも含まれない現役冒険者は「未編成」として扱う
    ///   （専用データ構造は持たず、都度フィルタで導出する。→ GetUnassignedAdventurers）。
    /// - 派遣時は、保存された編成のうち出撃可能（Adventurer.IsAvailable）なメンバーだけで
    ///   実際の出撃用Party（既存クラス、実際のAdventurer参照を持つ実行時コンテナ）を
    ///   組み立てる（→ BuildDispatchParty）。一時的な入れ替えは、SavedParty.MemberIdsを
    ///   変更せず、呼び出し側が用意した代替メンバーId一覧をそのまま渡すことで実現する
    ///   （このクラス自身は「保存データを変更しない」という制約を、単に
    ///   SavedParty.MemberIdsへ一切書き込まないことで自然に満たす）。
    /// </summary>
    public class PartyFormationSystem
    {
        /// <summary>新しい空のパーティーを作成し、GameStateに追加して返す。</summary>
        public SavedParty CreateParty(GameState state, string name)
        {
            var party = new SavedParty { Name = name };
            state.SavedParties.Add(party);
            return party;
        }

        /// <summary>パーティー名を変更する。</summary>
        public void RenameParty(SavedParty party, string name) => party.Name = name;

        /// <summary>パーティーを削除する（進行中の派遣・戦死者記録等には一切影響しない）。</summary>
        public void DeleteParty(GameState state, SavedParty party) => state.SavedParties.Remove(party);

        /// <summary>
        /// 冒険者をパーティーに編成する。既に4名（他パーティー所属で参照が失われた
        /// メンバーは実質的な空き枠として扱う）に達していれば失敗する。
        /// 対象の冒険者が既に別のパーティーに所属していれば、まずそちらから解除してから
        /// 追加する（1人が同時に複数パーティーに所属しないようにするため）。
        /// 既に対象パーティーに所属済みなら何もせず成功扱い。
        /// </summary>
        public bool TryAssignMember(GameState state, SavedParty party, Guid adventurerId)
        {
            if (party.MemberIds.Contains(adventurerId)) return true;
            if (ActiveMemberCount(state, party) >= Party.MaxSlots) return false;

            RemoveFromAllParties(state, adventurerId);
            party.MemberIds.Add(adventurerId);
            return true;
        }

        /// <summary>パーティーから冒険者を外す（未編成に戻る）。</summary>
        public void RemoveMember(SavedParty party, Guid adventurerId) => party.MemberIds.Remove(adventurerId);

        /// <summary>
        /// どのパーティーにも属さない現役冒険者（＝「未編成」）を返す。専用データ構造は
        /// 持たず、現役ロースターをSavedParties全体のMemberIdsでフィルタして都度導出する。
        /// </summary>
        public static IEnumerable<Adventurer> GetUnassignedAdventurers(GameState state) =>
            state.Adventurers.Where(a => !state.SavedParties.Any(p => p.MemberIds.Contains(a.Id)));

        /// <summary>
        /// 指定したパーティーに実際に所属している（現役ロースターに存在する）メンバー数。
        /// 戦死・引退・契約解除等で現役ロースターから既に除外された冒険者のIdが
        /// MemberIdsに残っていても、それは実質的な空き枠として扱う（4枠の判定に使う）。
        /// </summary>
        private static int ActiveMemberCount(GameState state, SavedParty party) =>
            party.MemberIds.Count(id => state.Adventurers.Any(a => a.Id == id));

        private static void RemoveFromAllParties(GameState state, Guid adventurerId)
        {
            foreach (var party in state.SavedParties)
                party.MemberIds.Remove(adventurerId);
        }

        /// <summary>
        /// 派遣用の実行時Party（既存クラス）を組み立てる。保存された編成のうち、
        /// 出撃可能（Adventurer.IsAvailable：重傷でない・引退済みでない・他クエスト
        /// 派遣中でない）なメンバーだけを実際に含める。出撃可能な人数が0人なら
        /// 空のPartyを返す（呼び出し側はParty.IsEmptyで派遣不可を判定する）。
        /// </summary>
        /// <param name="state">現在のゲーム状態。</param>
        /// <param name="memberIds">
        /// 出撃メンバー候補のId一覧。通常はsavedParty.MemberIdsをそのまま渡すが、
        /// 派遣直前の一時的な入れ替え（→ 03 §4.0.2・§4.0.3）を行う場合は、
        /// 差し替え後のId一覧をここに渡す。SavedParty自体は一切変更しない。
        /// </param>
        public static Party BuildDispatchParty(GameState state, IEnumerable<Guid> memberIds)
        {
            var party = new Party();
            foreach (var id in memberIds)
            {
                var adventurer = state.Adventurers.FirstOrDefault(a => a.Id == id);
                if (adventurer == null || !adventurer.IsAvailable) continue;
                party.TryAdd(adventurer);
            }
            return party;
        }

        /// <summary>
        /// 保存された編成のうち、出撃不可（IsAvailable==false）なメンバーの一覧を返す
        /// （→ UI側「〇〇は重傷のため出撃できません」等の表示用）。
        /// </summary>
        public static List<Adventurer> GetUnavailableMembers(GameState state, IEnumerable<Guid> memberIds) =>
            memberIds
                .Select(id => state.Adventurers.FirstOrDefault(a => a.Id == id))
                .Where(a => a != null && !a.IsAvailable)
                .Select(a => a!)
                .ToList();

        /// <summary>
        /// 指定したメンバー群の全2人組（重複無し）について、現在の相性値を返す
        /// （→ 03 §5.3.1・CompatibilitySystem。編成画面での相性表示に使う）。
        /// 事前調査メモ：指示書は「既に実装されているGetOrInitCompatibility等を流用」と
        /// 記載していたが、実際に存在するのはCompatibilitySystem.GetCompatibility
        /// （読み取り専用・副作用なし）であり、そちらを使用する。
        /// </summary>
        public static List<PartyCompatibilityPair> GetCompatibilityPairs(GameState state, IReadOnlyList<Guid> memberIds)
        {
            var pairs = new List<PartyCompatibilityPair>();
            for (int i = 0; i < memberIds.Count; i++)
            {
                for (int j = i + 1; j < memberIds.Count; j++)
                {
                    int value = CompatibilitySystem.GetCompatibility(state, memberIds[i], memberIds[j]);
                    pairs.Add(new PartyCompatibilityPair(memberIds[i], memberIds[j], value, CompatibilitySystem.IsHostile(state, memberIds[i], memberIds[j])));
                }
            }
            return pairs;
        }
    }

    /// <summary>編成画面での相性表示用の1ペア分の結果（→ PartyFormationSystem.GetCompatibilityPairs）。</summary>
    public record PartyCompatibilityPair(Guid IdA, Guid IdB, int Value, bool IsHostile);
}
