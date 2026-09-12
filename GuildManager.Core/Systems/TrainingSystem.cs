using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 訓練施設配置の運用ルール。仕様書 03 §3.1〜3.4（経路2・運用ルール確定）・
    /// §3.5改（訓練週のHP処理）・§6（v1.3改訂：訓練場・道場の4分割）参照。
    ///
    /// v1.3改訂：旧・単一の訓練場・道場（TrainingGround）を、戦士訓練所（WarriorHall）・
    /// 教会（Church）・魔法研究所（MageLab）・斥候所（ScoutPost）の4施設に分割した。
    /// GameState.TrainingAssignments が「どの施設に配置されているか」まで持つ
    /// Dictionary&lt;Guid, FacilityType&gt; になったことに伴い、枠数判定・配置操作を
    /// 施設ごとに独立させている。
    ///
    /// - 枠数：GetSlotCapacity(state, facility)。各訓練施設のLvに連動する
    ///   （→ FacilityBalance.GetTrainingSlotCapacity。Lv1＝1名は分割前と同じ値）。
    ///   満杯時の追加配置は Party.TryAdd と同様に失敗（false）で表現する。
    /// - 費用：配置されている限り毎週発生する都度払い（宿舎枠のような無料保有ではない）。
    ///   週給引落しと同じタイミングで ProcessWeeklyTraining を呼ぶ想定。
    /// - HP：今週出撃していない配置者のみ、CurrentHPを微減させる（下限1）。
    ///   InjurySeverityやフェーズ3の致死判定（§4.3）には一切接続しない
    ///   （訓練は「回復しない・地味に削れる」だけの低リスクな経路）。
    ///   出撃した週・単純待機の週とは排他（それぞれ戦闘のHP処理・RestRecoverySystemが担当）。
    /// </summary>
    public class TrainingSystem
    {
        /// <summary>指定した訓練施設の現在Lvに連動する枠数上限。</summary>
        public int GetSlotCapacity(GameState state, FacilityType facility) =>
            FacilityBalance.GetTrainingSlotCapacity(state.GetFacilityLevel(facility));

        /// <summary>
        /// 冒険者を指定した訓練施設に配置する。既にその施設に配置済みなら何もせず成功扱い。
        /// 既に「別の」施設に配置済みの場合は、一旦解除してから新しい施設に付け替える
        /// （1人が同時に複数の訓練施設へ重複配置されることは無い）。
        /// 対象施設の枠が埋まっていれば配置せず false を返す（Party.TryAdd のパターンに倣う）。
        /// </summary>
        public bool TryAssign(GameState state, Guid adventurerId, FacilityType facility)
        {
            if (state.TrainingAssignments.TryGetValue(adventurerId, out var current))
            {
                if (current == facility)
                    return true; // 既に同じ施設に配置済み

                Unassign(state, adventurerId); // 別施設への付け替え：まず現在の枠を解放する
            }

            if (CountAssigned(state, facility) >= GetSlotCapacity(state, facility))
                return false;

            state.TrainingAssignments[adventurerId] = facility;
            return true;
        }

        /// <summary>訓練施設への配置を解除する（配置先の施設を問わない）。</summary>
        public void Unassign(GameState state, Guid adventurerId) =>
            state.TrainingAssignments.Remove(adventurerId);

        /// <summary>指定した訓練施設に現在配置されている人数。</summary>
        public int CountAssigned(GameState state, FacilityType facility) =>
            state.TrainingAssignments.Values.Count(f => f == facility);

        /// <summary>
        /// 週次決算処理。訓練施設に配置されている冒険者から週次費用を徴収し、
        /// 今週出撃していない配置者のCurrentHPを微減させる（下限1・致死判定と非接続）。
        /// どの施設に配置されているかは対象外（全施設共通の処理。→ 03 §6「訓練週のHP微減・
        /// 回復なし・下限1は全施設共通で維持する」）。
        /// </summary>
        /// <param name="state">ゲーム状態。</param>
        /// <param name="dispatchedAdventurerIds">今週出撃したパーティのメンバーID（出撃なしの週は空集合）。</param>
        public void ProcessWeeklyTraining(GameState state, IReadOnlySet<Guid> dispatchedAdventurerIds)
        {
            if (state.TrainingAssignments.Count == 0)
                return;

            foreach (var adventurer in state.Adventurers)
            {
                if (!state.TrainingAssignments.ContainsKey(adventurer.Id))
                    continue;

                // 配置されている限り、費用は都度払い（出撃の有無に関わらず発生する）。
                state.Gold -= TrainingBalance.WeeklyCost;

                if (dispatchedAdventurerIds.Contains(adventurer.Id))
                    continue; // 今週は出撃扱い。訓練固有のHP処理は行わない（§3.5改：排他）。

                adventurer.CurrentHP = Math.Max(TrainingBalance.MinHp, adventurer.CurrentHP - TrainingBalance.WeeklyHpCost);
            }
        }
    }
}
