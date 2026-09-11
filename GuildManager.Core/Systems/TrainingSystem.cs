using System;
using System.Collections.Generic;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 訓練場配置の運用ルール。仕様書 03 §3.1〜3.4（経路2・運用ルール確定）・
    /// §3.5改（訓練週のHP処理）参照。
    ///
    /// 前回（項目7）追加した GameState.TrainingAssignments という最小限フックに、
    /// 枠数制約・週次費用・訓練週固有のHP処理を追加するもの。
    ///
    /// - 枠数：SlotCapacity（現状はLv1相当で固定1名。→ TrainingBalance）。
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
        /// <summary>訓練場の現在の枠数上限（→ TrainingBalance.SlotCapacity）。</summary>
        public int SlotCapacity => TrainingBalance.SlotCapacity;

        /// <summary>
        /// 冒険者を訓練場に配置する。既に配置済みなら何もせず成功扱い。
        /// 枠が埋まっていれば配置せず false を返す（Party.TryAdd のパターンに倣う）。
        /// </summary>
        public bool TryAssign(GameState state, Guid adventurerId)
        {
            if (state.TrainingAssignments.Contains(adventurerId))
                return true;

            if (state.TrainingAssignments.Count >= SlotCapacity)
                return false;

            state.TrainingAssignments.Add(adventurerId);
            return true;
        }

        /// <summary>訓練場配置を解除する。</summary>
        public void Unassign(GameState state, Guid adventurerId) =>
            state.TrainingAssignments.Remove(adventurerId);

        /// <summary>
        /// 週次決算処理。訓練場に配置されている冒険者から週次費用を徴収し、
        /// 今週出撃していない配置者のCurrentHPを微減させる（下限1・致死判定と非接続）。
        /// </summary>
        /// <param name="state">ゲーム状態。</param>
        /// <param name="dispatchedAdventurerIds">今週出撃したパーティのメンバーID（出撃なしの週は空集合）。</param>
        public void ProcessWeeklyTraining(GameState state, IReadOnlySet<Guid> dispatchedAdventurerIds)
        {
            if (state.TrainingAssignments.Count == 0)
                return;

            foreach (var adventurer in state.Adventurers)
            {
                if (!state.TrainingAssignments.Contains(adventurer.Id))
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
