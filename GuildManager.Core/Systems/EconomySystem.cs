using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 週次の資金処理（週給引き落とし・クエスト報酬加算）と、在庫の換金（→ 03 §4.8 売却）。
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

        /// <summary>
        /// アルベールの市販薬・内職売上（→ 03 §8.1、旧・月次助成金の後継。2026年9月）。
        /// SideJobIntervalWeeks週に1回（週番号がその倍数の週）、基本額×マスターの機嫌の売上倍率
        /// （→ MasterMoodSystem.GetSideJobMultiplier：上機嫌1.5／平常1.0／不機嫌0.5／危機0.0）を入金する。
        /// 倍率は決算時点の機嫌で決める（呼び出し側は機嫌の週次変動を済ませてから呼ぶ）。
        /// 入金週でなければ null。
        /// </summary>
        public SideJobIncome? ProcessWeeklySideJobIncome(GameState state)
        {
            if (state.WeekNumber % EconomyBalance.SideJobIntervalWeeks != 0)
                return null;

            int mood = state.MasterMood;
            var tier = MasterMoodSystem.GetTier(mood);
            double multiplier = MasterMoodSystem.GetSideJobMultiplier(tier);
            int finalGold = (int)Math.Round(EconomyBalance.SideJobBaseAmount * multiplier);
            state.Gold += finalGold;
            return new SideJobIncome(EconomyBalance.SideJobBaseAmount, mood, tier, multiplier, finalGold);
        }

        /// <summary>クエスト報酬を所持金に加算する。</summary>
        public void ApplyReward(GameState state, int rewardGold)
        {
            state.Gold += rewardGold;
        }

        // ==================== 在庫の換金（→ 03 §4.8 売却） ====================

        /// <summary>
        /// 採取素材を売却して所持金へ換える（→ 03 §4.8）。単価は materials.csv の SellPrice
        /// （→ Balance.MaterialBalance.GetSellPrice）。
        ///
        /// 以下のいずれかに該当する場合は**在庫・所持金を一切動かさず** false を返す
        /// （Try*系の共通パターン）：
        ///  - count が0以下
        ///  - その素材の在庫が無い、または在庫が count に足りない
        ///  - materials.csv に定義が無い素材Id（単価が決まらないため売れない）
        ///
        /// 在庫がちょうど0になった素材は辞書からキーごと取り除く（→ GameState.AddMaterial が
        /// 「未所持の素材はキー自体が存在しない」を前提にしているため、その不変条件に合わせる）。
        /// </summary>
        public bool TrySellMaterial(GameState state, string materialId, int count, out int totalGold)
        {
            totalGold = 0;

            if (count <= 0)
                return false;
            if (!state.Materials.TryGetValue(materialId, out int stock) || stock < count)
                return false;

            var definition = MaterialBalance.Find(materialId);
            if (definition == null)
                return false;

            int remaining = stock - count;
            if (remaining > 0)
                state.Materials[materialId] = remaining;
            else
                state.Materials.Remove(materialId);

            totalGold = definition.SellPrice * count;
            state.Gold += totalGold;
            return true;
        }

        /// <summary>
        /// 指定した素材の在庫を全数売却する（→ UI の【全売却】）。在庫が無ければ false。
        /// </summary>
        public bool TrySellAllOfMaterial(GameState state, string materialId, out int totalGold)
        {
            totalGold = 0;
            return state.Materials.TryGetValue(materialId, out int stock)
                && TrySellMaterial(state, materialId, stock, out totalGold);
        }
    }
}
