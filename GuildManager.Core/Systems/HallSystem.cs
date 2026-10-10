using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// ギルドのホール（2026年10月・§0.95、→ HallBalance）。押せる場所が今使えるか（施設が建っているか・タブが開いているか）と、
    /// 部隊のテーブルの椅子に座る子を決める。画面（GuildHallPanel）は使えない場所を暗くし、押しても移らない。
    /// </summary>
    public static class HallSystem
    {
        /// <summary>押せる場所が今使えるか。使えなければ理由（画面のヒントに出す）。</summary>
        public static (bool Open, string Reason) Availability(GameState state, HallTarget target) => target switch
        {
            HallTarget.Commission => IsabellaSystem.CommissionsOpen(state) ? (true, "") : (false, "まだ依頼は届いていない（大会で初めて入賞すると届き始める）"),
            HallTarget.Research => Tab(state, GuideTab.Research),
            HallTarget.Shop => Tab(state, GuideTab.Shop),
            HallTarget.Ledger => Tab(state, GuideTab.Facility),
            HallTarget.WarRoom => Built(state, FacilityType.WarRoom),
            HallTarget.Infirmary => Built(state, FacilityType.Infirmary),
            HallTarget.Tavern => Built(state, FacilityType.Tavern),
            HallTarget.Support => Built(state, FacilityType.RecruitmentOffice),
            HallTarget.Training => new[] { FacilityType.DrillHall, FacilityType.Academy, FacilityType.SkillHall }.Any(t => state.GetFacilityLevel(t) > 0)
                ? (true, "") : (false, "まだ訓練所が無い（施設管理で建てる）"),
            _ => (true, ""),
        };

        private static (bool, string) Tab(GameState state, GuideTab tab) =>
            GuideSystem.IsTabOpen(state, tab) ? (true, "") : (false, "まだ使えない（物語で教わると開く）");

        private static (bool, string) Built(GameState state, FacilityType type) =>
            state.GetFacilityLevel(type) > 0 ? (true, "") : (false, "まだ建っていない（施設管理で建てる）");

        /// <summary>その部隊（1〜）。無ければ null。</summary>
        public static SavedParty? Squad(GameState state, int squadNo) =>
            squadNo >= 1 && squadNo <= state.SavedParties.Count ? state.SavedParties[squadNo - 1] : null;

        /// <summary>
        /// 部隊（1〜）のテーブルの椅子に座る子（席の順＝部隊のメンバーの順。いない席は null）。席の数は hall.csv のその部隊の椅子の数。
        /// 引退した子・名簿にいない子は座らせない。
        /// </summary>
        public static List<(HallSeat Seat, Adventurer? Adventurer)> SeatedMembers(GameState state, int squadNo)
        {
            var members = Squad(state, squadNo)?.MemberIds
                .Select(id => state.Adventurers.FirstOrDefault(a => a.Id == id && !a.IsRetired))
                .OfType<Adventurer>().ToList() ?? new List<Adventurer>();
            return HallBalance.Seats.Where(s => s.Squad == squadNo)
                .Select((seat, i) => (seat, i < members.Count ? members[i] : null))
                .ToList();
        }
    }
}
