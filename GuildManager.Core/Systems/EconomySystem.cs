using System.Linq;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 週次の資金処理（週給引き落とし・クエスト報酬加算）。
    /// 仕様書 03 §1.2 / §5.2、04 バランス表「経済」参照。
    /// </summary>
    public class EconomySystem
    {
        /// <summary>全冒険者の週給を所持金から引き落とす。</summary>
        public void ApplyWeeklyWages(GameState state)
        {
            int totalWages = state.Adventurers.Sum(a => a.WeeklyWage);
            state.Gold -= totalWages;
        }

        /// <summary>クエスト報酬を所持金に加算する。</summary>
        public void ApplyReward(GameState state, int rewardGold)
        {
            state.Gold += rewardGold;
        }
    }
}
