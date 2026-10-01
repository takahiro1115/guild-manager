using System;
using System.Collections.Generic;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 負傷から回復するまでの週数を消化する処理。仕様書 03 §3.6 参照。
    ///
    /// 毎週の決算時、遠征に出したかどうかに関わらず必ず1回呼び出す想定
    /// （「休んで治す」という選択を成立させるため。仕様書 §5.1の
    /// 「4週連続で遠征なし」ペナルティは、休養そのものを罰するのではなく
    /// 出場機会の観点から満足度を下げるものであり、休養自体は正当な選択）。
    ///
    /// 医務室（Infirmary）の現在Lvに連動して回復速度が上がる
    /// （→ FacilityBalance.GetInfirmaryInjuryRecoverySpeed。Lv1＝週1減＝施設システム
    /// 導入前と同じ速度）。
    ///
    /// 重傷は治ったときにHPを満タンにして復帰させる。軽傷（§0.53）は治ってもHPはそのまま（静養で回復する）。
    /// その週の任務で負傷したばかりの隊員（justInjured）は、その週の回復を進めない（負傷した週に1週分が消化されないように）。
    /// </summary>
    public class InjuryRecoverySystem
    {
        public void ProcessWeeklyRecovery(GameState state, IReadOnlySet<Guid>? justInjured = null)
        {
            int recoverySpeed = FacilityBalance.GetInfirmaryInjuryRecoverySpeed(state.GetFacilityLevel(FacilityType.Infirmary));

            foreach (var adventurer in state.Adventurers)
            {
                if (adventurer.Injury == InjurySeverity.None)
                    continue;
                if (justInjured != null && justInjured.Contains(adventurer.Id))
                    continue;

                if (adventurer.InjuryWeeksRemaining > 0)
                {
                    // 治りが早い（§0.55）は医務室の回復速度に加算する。
                    int speed = recoverySpeed + (adventurer.HasTrait(TraitCatalog.QuickHealerId) ? TraitBalance.QuickHealerInjuryRecoveryBonus : 0);
                    adventurer.InjuryWeeksRemaining = Math.Max(0, adventurer.InjuryWeeksRemaining - speed);
                }

                if (adventurer.InjuryWeeksRemaining <= 0)
                {
                    if (adventurer.Injury == InjurySeverity.Severe)
                        adventurer.CurrentHP = adventurer.MaxHP;
                    adventurer.Injury = InjurySeverity.None;
                }
            }
        }
    }
}
