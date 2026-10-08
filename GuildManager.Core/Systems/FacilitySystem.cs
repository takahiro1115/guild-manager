using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 施設Lv投資（着工・工事期間・単一建設キュー）。仕様書 03 §6・§6.1・§6.2 参照。
    ///
    /// - Lvアップは即時ではなく「着工→N週間の工事期間→完成」（→ §6.1）。
    /// - 改築費は着工時に前払い。工事完了時の追加支払いは無い。
    /// - 同時着工は1件のみ（GameState.UnderConstructionが単一のnull許容フィールドのため、
    ///   型のレベルで「2件以上同時に持てない」ことが保証されている）。
    /// - 着工中も、着工前の現在Lv（と専門）の効果はそのまま維持される（このクラスはLvを下げない）。
    /// - 施設の専門（§0.76）：Lv3→4の着工で専門を選ぶ（宿舎以外）。選び直しは改装（TryStartRemodel）。
    /// </summary>
    public class FacilitySystem
    {
        /// <summary>このLvからの着工で専門を選ぶ必要があるか（宿舎以外で、今のLvが FacilityBalance.SpecialtyFromLevel）。</summary>
        public static bool NeedsSpecialtyChoice(FacilityType type, int currentLevel) =>
            FacilityBalance.HasSpecialty(type) && currentLevel == FacilityBalance.SpecialtyFromLevel;

        /// <summary>
        /// 顧問（引退者）を置くための施設か（作戦資料室＝参謀・冒険者支援室＝スカウト）。§0.78：最初の引退者が出るまで建てられない。
        /// </summary>
        public static bool RequiresAdvisorCandidate(FacilityType type) =>
            type is FacilityType.WarRoom or FacilityType.RecruitmentOffice;

        /// <summary>
        /// 建てられる施設か（§0.78、段階的な開放）：作戦資料室・冒険者支援室は、引退者（顧問候補）が1人でもいるか、すでに建っているときだけ。
        /// ほかの施設はいつでも建てられる。序盤に「建てても何も起きない施設」を並べないため。
        /// </summary>
        public static bool IsAvailable(GameState state, FacilityType type) =>
            !RequiresAdvisorCandidate(type) || state.GuildRetirees.Any() || state.GetFacilityLevel(type) >= 1;

        /// <summary>
        /// 今のLvから次のLvへの改築が、まだ開いていないLvで止められているか（§0.82。施設は大会などのご褒美で開く、→ FacilityUnlockSystem。
        /// §0.79の「倒したボスの数で全施設の上限Lv」を置き換えた）。
        /// </summary>
        public static bool IsBlockedByLevelCap(GameState state, FacilityType type) =>
            state.GetFacilityLevel(type) + 1 > FacilityUnlockSystem.GetUnlockedLevel(state, type);

        /// <summary>改築費（辺境伯杯の優勝のご褒美があれば半額、§0.82）。</summary>
        public static int GetUpgradeCost(GameState state, FacilityType type, int currentLevel) =>
            state.NextUpgradeHalfPrice ? FacilityBalance.GetUpgradeCost(type, currentLevel) / 2 : FacilityBalance.GetUpgradeCost(type, currentLevel);

        /// <summary>
        /// 対象施設の着工を試みる。他の施設が工事中、対象施設が既に最大Lv、
        /// または資金不足の場合は何もせず false を返す（Party.TryAdd等の失敗パターンに倣う）。
        /// §0.76：Lv3→4の着工では specialty（その施設の選択肢の1つ）が必要。それ以外のLvでは specialty は無視する。
        /// </summary>
        public bool TryStartConstruction(GameState state, FacilityType type, FacilitySpecialty specialty = FacilitySpecialty.None)
        {
            if (state.UnderConstruction != null)
                return false; // 同時着工は1件のみ
            if (!IsAvailable(state, type))
                return false; // 準備中（最初の引退者が出るまで、§0.78）

            var facility = FindOrCreate(state, type);
            if (facility.CurrentLevel >= FacilityBalance.MaxLevel)
                return false;
            if (IsBlockedByLevelCap(state, type))
                return false; // まだ開いていないLv（大会などのご褒美で開く、§0.82）

            bool choose = NeedsSpecialtyChoice(type, facility.CurrentLevel);
            if (choose && Array.IndexOf(FacilityBalance.GetSpecialtyOptions(type), specialty) < 0)
                return false; // 専門を選ばないとLv4へは上げられない

            int cost = GetUpgradeCost(state, type, facility.CurrentLevel);
            if (state.Gold < cost)
                return false;

            state.Gold -= cost; // 着工時に前払い
            state.NextUpgradeHalfPrice = false; // 辺境伯杯のご褒美は1回だけ
            state.UnderConstruction = new FacilityConstruction
            {
                Type = type,
                TargetLevel = facility.CurrentLevel + 1,
                WeeksRemaining = FacilityBalance.GetConstructionWeeks(type, facility.CurrentLevel),
                TargetSpecialty = choose ? specialty : FacilitySpecialty.None,
            };
            return true;
        }

        /// <summary>
        /// 改装（専門の選び直し、§0.76）を試みる。専門が付いている（Lv4以上）施設で、今と違う選択肢を指定したときだけ。
        /// 費用＝今のLv×RemodelCostPerLevel（前払い）、工期 RemodelWeeks。建設キューを1件使い、工事中は今の専門のまま。
        /// </summary>
        public bool TryStartRemodel(GameState state, FacilityType type, FacilitySpecialty specialty)
        {
            if (state.UnderConstruction != null)
                return false;

            var current = state.GetFacilitySpecialty(type);
            if (current == FacilitySpecialty.None || current == specialty
                || Array.IndexOf(FacilityBalance.GetSpecialtyOptions(type), specialty) < 0)
                return false;

            int level = state.GetFacilityLevel(type);
            int cost = FacilityBalance.GetRemodelCost(level);
            if (state.Gold < cost)
                return false;

            state.Gold -= cost;
            state.UnderConstruction = new FacilityConstruction
            {
                Type = type,
                TargetLevel = level,
                WeeksRemaining = FacilityBalance.RemodelWeeks,
                TargetSpecialty = specialty,
                IsRemodel = true,
            };
            return true;
        }

        /// <summary>
        /// 週次決算処理。工事中なら残り週数を1減らし、0に到達したらLv（と専門）を反映して完成させる。
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
            if (state.UnderConstruction.TargetSpecialty != FacilitySpecialty.None)
                facility.Specialty = state.UnderConstruction.TargetSpecialty;
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
