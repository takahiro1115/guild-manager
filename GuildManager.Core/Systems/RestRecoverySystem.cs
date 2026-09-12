using System;
using System.Collections.Generic;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 静養・HP自然回復。仕様書 03 §3.5改 参照（疲労Fatigueを廃止し、回復をHPに一本化した後の新規実装）。
    ///
    /// 毎週の決算処理で ProcessWeeklyRest を1回呼び出す想定
    /// （InjuryRecoverySystem.ProcessWeeklyRecovery と同様の使い方。§3.6の重傷回復とは別管理）。
    ///
    /// - 対象：今週このパーティに選ばれず出撃しなかった、かつ訓練場にも配置されていない
    ///   （＝単純待機の）、重傷以外（無傷・軽傷）の冒険者。
    ///   出撃した者は同じ週の戦闘でHPが増減するため、ここでの回復対象からは外す。
    ///   訓練場配置中の者は TrainingSystem が別のHP処理（微減・回復なし）を行うため対象外
    ///   （出撃／訓練場配置／単純待機は互いに排他。→ 03 §3.5改）。
    /// - 重傷（Severe）はこの処理の対象外。§3.6 の InjuryRecoverySystem が別管理する
    ///   （全治週数のカウントダウン → 全快）。
    /// - 回復量は暫定定数（→ BAL: 静養/待機回復。現状は仮値）に、医務室（Infirmary）の
    ///   現在Lvに連動する倍率を掛ける（→ FacilityBalance.GetInfirmaryHpRecoveryMultiplier）。
    ///   上限は MaxHP。
    /// </summary>
    public class RestRecoverySystem
    {
        // → BAL: 静養/待機回復。現状は仮値（MaxHPの15%を毎週回復）。
        private const double RestRecoveryRatio = 0.15;

        /// <summary>
        /// 静養によるHP自然回復を処理する。
        /// </summary>
        /// <param name="state">ゲーム状態。</param>
        /// <param name="dispatchedAdventurerIds">今週出撃したパーティのメンバーID（出撃なしの週は空集合）。</param>
        public void ProcessWeeklyRest(GameState state, IReadOnlySet<Guid> dispatchedAdventurerIds)
        {
            foreach (var adventurer in state.Adventurers)
            {
                if (dispatchedAdventurerIds.Contains(adventurer.Id))
                    continue; // 今週出撃した者は静養扱いにしない

                if (state.TrainingAssignments.ContainsKey(adventurer.Id))
                    continue; // 訓練場配置中はTrainingSystemが別途処理する（§3.5改：排他）

                if (adventurer.Injury == InjurySeverity.Severe)
                    continue; // 重傷は§3.6（InjuryRecoverySystem）が別管理

                if (adventurer.CurrentHP >= adventurer.MaxHP)
                    continue;

                double facilityMultiplier = FacilityBalance.GetInfirmaryHpRecoveryMultiplier(state.GetFacilityLevel(FacilityType.Infirmary));
                int recovery = (int)(adventurer.MaxHP * RestRecoveryRatio * facilityMultiplier);
                adventurer.CurrentHP = Math.Min(adventurer.MaxHP, adventurer.CurrentHP + recovery);
            }
        }
    }
}
