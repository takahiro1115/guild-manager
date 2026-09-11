using System;
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
    /// 不可逆障害（Permanent）は対象外（そもそも完治しない想定。現状のMVPでは未実装）。
    /// </summary>
    public class InjuryRecoverySystem
    {
        public void ProcessWeeklyRecovery(GameState state)
        {
            int recoverySpeed = FacilityBalance.GetInfirmaryInjuryRecoverySpeed(state.GetFacilityLevel(FacilityType.Infirmary));

            foreach (var adventurer in state.Adventurers)
            {
                if (adventurer.Injury == InjurySeverity.None)
                    continue;

                if (adventurer.InjuryWeeksRemaining > 0)
                {
                    adventurer.InjuryWeeksRemaining = Math.Max(0, adventurer.InjuryWeeksRemaining - recoverySpeed);
                }

                if (adventurer.InjuryWeeksRemaining <= 0)
                {
                    adventurer.Injury = InjurySeverity.None;
                    adventurer.CurrentHP = adventurer.MaxHP;
                }
            }
        }
    }
}
