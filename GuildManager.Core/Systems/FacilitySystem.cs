using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 施設Lv投資（着工・工事期間・単一建設キュー）。仕様書 03 §6・§6.1 参照。
    ///
    /// - Lvアップは即時ではなく「着工→N週間の工事期間→完成」（→ §6.1）。
    /// - 改築費は着工時に前払い。工事完了時の追加支払いは無い。
    /// - 同時着工は1件のみ（GameState.UnderConstructionが単一のnull許容フィールドのため、
    ///   型のレベルで「2件以上同時に持てない」ことが保証されている）。
    /// - 着工中も、着工前の現在Lvの効果はそのまま維持される（このクラスはLvを下げない）。
    /// </summary>
    public class FacilitySystem
    {
        /// <summary>
        /// 対象施設の着工を試みる。他の施設が工事中、対象施設が既に最大Lv、
        /// または資金不足の場合は何もせず false を返す（Party.TryAdd等の失敗パターンに倣う）。
        /// </summary>
        public bool TryStartConstruction(GameState state, FacilityType type)
        {
            if (state.UnderConstruction != null)
                return false; // 同時着工は1件のみ

            var facility = FindOrCreate(state, type);
            if (facility.CurrentLevel >= FacilityBalance.MaxLevel)
                return false;

            int cost = FacilityBalance.GetUpgradeCost(type, facility.CurrentLevel);
            if (state.Gold < cost)
                return false;

            state.Gold -= cost; // 着工時に前払い
            state.UnderConstruction = new FacilityConstruction
            {
                Type = type,
                TargetLevel = facility.CurrentLevel + 1,
                WeeksRemaining = FacilityBalance.GetConstructionWeeks(type, facility.CurrentLevel),
            };
            return true;
        }

        /// <summary>
        /// 週次決算処理。工事中なら残り週数を1減らし、0に到達したらLvを+1して完成させる。
        /// 完成した施設を返す（工事中でない、またはまだ完成していない週は null）。
        /// </summary>
        public Facility? ProcessWeeklyConstruction(GameState state)
        {
            if (state.UnderConstruction == null)
                return null;

            state.UnderConstruction.WeeksRemaining--;
            if (state.UnderConstruction.WeeksRemaining > 0)
                return null;

            var facility = FindOrCreate(state, state.UnderConstruction.Type);
            facility.CurrentLevel = state.UnderConstruction.TargetLevel;
            state.UnderConstruction = null;
            return facility;
        }

        private static Facility FindOrCreate(GameState state, FacilityType type)
        {
            foreach (var facility in state.Facilities)
                if (facility.Type == type) return facility;

            var created = new Facility { Type = type, CurrentLevel = 1 };
            state.Facilities.Add(created);
            return created;
        }
    }
}
