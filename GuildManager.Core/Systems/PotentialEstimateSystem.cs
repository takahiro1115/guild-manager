using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 副官の見立て（目利き、2026年10月・§0.74）。PA（伸びしろの上限）は画面に出さず、副官が見立てた段階（S〜D）で見せる。
    ///  - 見立てのPA＝本当のPA＋ずれ×幅。ずれ（−1〜+1）は冒険者・能力ごとに一度だけ決めてセーブに持つ（→ Adventurer.PaEstimateOffsets）。
    ///  - 幅＝BaseWidth×(1−目利き)×(1−在籍の月数×MonthlyNarrowRate)×(魂魄融和の娘なら SoulFusionWidthRate)。
    ///  - 目利き＝スカウトの顧問＋段階研究（RecruitPaBonus）＋在籍の学者（上限 MaxEye）。
    ///  - 今の能力値より下には見立てない。能力が PA に届いた（伸び止まった）能力と、幅が ConfirmWidth 以下の能力は「確か」。
    /// 給与を決めるのは副官なので、採用時の週給・契約金はこの見立ての総合PAで決まる（→ RecruitmentSystem）。
    /// 満足度の「適正な週給」は本当の PA で決まるため、低く見立てて安く雇った子は賃金に不満を持ちやすい。
    /// ゲームの計算（成長・戦闘）は本当の PA のまま。乱数は ずれを決めるとき（AssignOffsets）だけ使う。
    /// </summary>
    public static class PotentialEstimateSystem
    {
        /// <summary>冒険者・能力ごとの見立てのずれを決める（−100〜+100、百分率）。採用の候補・魂魄融和の娘が生まれたときに1回。</summary>
        public static void AssignOffsets(Adventurer a, IRng rng)
        {
            foreach (var stat in AdventurerStatAccessor.AllStatNames)
                a.PaEstimateOffsets[stat] = rng.NextInt(-100, 100);
        }

        /// <summary>ギルドの目利き（0〜MaxEye）。</summary>
        public static double GetEye(GameState state)
        {
            double eye = 0;
            if (state.AssignedScoutMaster is { } scoutId && state.RetiredAdventurers.FirstOrDefault(r => r.Id == scoutId) is { } scout)
                eye += (scout.LDR + scout.DEX) / 2.0 * PotentialEstimateBalance.ScoutMasterEyeCoeff;
            eye += ResearchBalance.GetAll().Count(r => r.EffectType == ResearchEffectType.RecruitPaBonus && state.IsResearchCompleted(r.Id))
                * PotentialEstimateBalance.ResearchEyePerStep;
            var scholars = state.Adventurers.Where(a => !a.IsRetired && a.JobClass == JobClass.Scholar).ToList();
            if (scholars.Count > 0)
                eye += scholars.Max(s => s.INT) * PotentialEstimateBalance.ScholarEyeCoeff;
            return Math.Clamp(eye, 0, PotentialEstimateBalance.MaxEye);
        }

        /// <summary>この冒険者の見立ての幅（pt）。</summary>
        public static double GetWidth(GameState state, Adventurer a)
        {
            int months = a.ActiveWeeks / GameCalendar.WeeksPerMonth;
            double width = PotentialEstimateBalance.BaseWidth * (1 - GetEye(state))
                * Math.Max(0, 1 - months * PotentialEstimateBalance.MonthlyNarrowRate);
            if (a.ParentIds.Count == 2)
                width *= PotentialEstimateBalance.SoulFusionWidthRate;
            return width;
        }

        /// <summary>能力1つの見立てのPA。</summary>
        public static int EstimatePa(GameState state, Adventurer a, string stat) => EstimatePa(a, stat, GetWidth(state, a));

        private static int EstimatePa(Adventurer a, string stat, double width)
        {
            int pa = AdventurerStatAccessor.GetPa(a, stat);
            int current = AdventurerStatAccessor.GetStat(a, stat);
            if (current >= pa) return pa; // 伸び止まった＝PAが分かる
            int offset = a.PaEstimateOffsets.TryGetValue(stat, out int o) ? o : 0;
            int estimate = (int)Math.Round(pa + offset / 100.0 * width, MidpointRounding.AwayFromZero);
            return Math.Max(Math.Max(1, current), estimate);
        }

        /// <summary>能力1つの見立てが確かか（伸び止まった、または幅が ConfirmWidth 以下）。</summary>
        public static bool IsConfirmed(GameState state, Adventurer a, string stat) =>
            AdventurerStatAccessor.GetStat(a, stat) >= AdventurerStatAccessor.GetPa(a, stat) || GetWidth(state, a) <= PotentialEstimateBalance.ConfirmWidth;

        /// <summary>見立ての総合PA（7能力の見立ての平均）。採用時の週給・契約金はこの値で決める。</summary>
        public static double EstimateTotalPa(GameState state, Adventurer a)
        {
            double width = GetWidth(state, a);
            return AdventurerStatAccessor.AllStatNames.Average(stat => EstimatePa(a, stat, width));
        }

        /// <summary>見立ての総合が確か（全能力が確か）か。</summary>
        public static bool IsTotalConfirmed(GameState state, Adventurer a) =>
            AdventurerStatAccessor.AllStatNames.All(stat => IsConfirmed(state, a, stat));

        /// <summary>PA の値の段階（S〜D）。</summary>
        public static string Rank(double pa) =>
            pa >= PotentialEstimateBalance.RankS ? "S"
            : pa >= PotentialEstimateBalance.RankA ? "A"
            : pa >= PotentialEstimateBalance.RankB ? "B"
            : pa >= PotentialEstimateBalance.RankC ? "C"
            : "D";

        /// <summary>能力1つの見立ての表示（「B」「B?」）。</summary>
        public static string RankLabel(GameState state, Adventurer a, string stat) =>
            Rank(EstimatePa(state, a, stat)) + (IsConfirmed(state, a, stat) ? "" : "?");

        /// <summary>総合の見立ての表示（「B」「B?」）。</summary>
        public static string TotalRankLabel(GameState state, Adventurer a) =>
            Rank(EstimateTotalPa(state, a)) + (IsTotalConfirmed(state, a) ? "" : "?");
    }
}
