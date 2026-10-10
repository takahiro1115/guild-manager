using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>ギルドのホール（§0.95）：hall.csv の読み込み、押せる場所が使えるか、椅子に座る子。</summary>
    public class HallTests
    {
        private static readonly string[] Header = { "Id", "Kind", "Target", "Label", "X", "Y", "W", "H", "Squad", "Seat", "note" };

        private static GameState NewGame()
        {
            var state = new GameState { WeekNumber = 1, Gold = 1000, MasterMood = 50, DungeonFields = SampleData.CreateDefaultFields() };
            state.Adventurers.AddRange(SampleData.CreateStarterAdventurers());
            return state;
        }

        [Fact]
        public void テーブルは4つ_椅子は部隊ごとに4脚_行き先はすべてある()
        {
            var tables = HallBalance.Areas.Where(a => a.Target == HallTarget.Squad).ToList();
            Assert.Equal(new[] { 1, 2, 3, 4 }, tables.Select(t => t.Squad).OrderBy(s => s));
            Assert.Equal(16, HallBalance.Seats.Count);
            Assert.All(Enumerable.Range(1, 4), sq => Assert.Equal(new[] { 1, 2, 3, 4 }, HallBalance.Seats.Where(s => s.Squad == sq).Select(s => s.Seat)));
            Assert.All(Enum.GetValues<HallTarget>(), t => Assert.Contains(HallBalance.Areas, a => a.Target == t));
        }

        [Fact]
        public void 椅子はその部隊のテーブルの範囲の近くにある()
        {
            foreach (var seat in HallBalance.Seats)
            {
                var table = HallBalance.Areas.Single(a => a.Target == HallTarget.Squad && a.Squad == seat.Squad);
                Assert.InRange(seat.X, table.X - 0.03, table.X + table.W + 0.03);
                Assert.InRange(seat.Y, table.Y - 0.03, table.Y + table.H + 0.03);
            }
        }

        [Theory]
        [InlineData("area", "nowhere", "0.1", "0.1", "0.1", "0.1", "0", "0")]   // 行き先の誤り
        [InlineData("area", "shop", "0.95", "0.1", "0.1", "0.1", "0", "0")]     // 絵の外にはみ出す
        [InlineData("area", "shop", "0.1", "0.1", "0", "0.1", "0", "0")]        // 大きさ0
        [InlineData("area", "squad", "0.1", "0.1", "0.1", "0.1", "0", "0")]     // テーブルなのに部隊の番号が無い
        [InlineData("area", "shop", "0.1", "0.1", "0.1", "0.1", "2", "0")]      // テーブルでないのに部隊の番号
        [InlineData("seat", "", "0.1", "0.1", "0.03", "0", "0", "1")]           // 椅子に部隊の番号が無い
        [InlineData("seat", "", "1.5", "0.1", "0.03", "0", "1", "1")]           // 割合が1を超える
        [InlineData("chair", "", "0.1", "0.1", "0.03", "0", "1", "1")]          // 種類の誤り
        public void 書式の誤りは起動失敗にする(string kind, string target, string x, string y, string w, string h, string squad, string seat)
        {
            var rows = new List<string[]> { new[] { "r1", kind, target, "", x, y, w, h, squad, seat, "" } };
            Assert.Throws<BalanceDataException>(() => HallBalance.Parse(Header, rows));
        }

        [Fact]
        public void Idと席の重複は起動失敗にする()
        {
            Assert.Throws<BalanceDataException>(() => HallBalance.Parse(Header, new List<string[]>
            {
                new[] { "a", "area", "shop", "", "0.1", "0.1", "0.1", "0.1", "0", "0", "" },
                new[] { "a", "area", "dorm", "", "0.2", "0.1", "0.1", "0.1", "0", "0", "" },
            }));
            Assert.Throws<BalanceDataException>(() => HallBalance.Parse(Header, new List<string[]>
            {
                new[] { "s1", "seat", "", "", "0.1", "0.1", "0.03", "0", "1", "1", "" },
                new[] { "s2", "seat", "", "", "0.2", "0.1", "0.03", "0", "1", "1", "" },
            }));
        }

        [Fact]
        public void 施設が建っていなければ暗くする_建てば使える()
        {
            var state = NewGame();
            foreach (var type in new[] { FacilityType.Infirmary, FacilityType.WarRoom, FacilityType.Tavern, FacilityType.RecruitmentOffice, FacilityType.DrillHall, FacilityType.Academy, FacilityType.SkillHall })
                state.Facilities.Single(f => f.Type == type).CurrentLevel = 0;

            Assert.False(HallSystem.Availability(state, HallTarget.Infirmary).Open);
            Assert.False(HallSystem.Availability(state, HallTarget.Training).Open);
            Assert.NotEmpty(HallSystem.Availability(state, HallTarget.Tavern).Reason);
            Assert.True(HallSystem.Availability(state, HallTarget.Dungeon).Open);
            Assert.True(HallSystem.Availability(state, HallTarget.Squad).Open);
            Assert.True(HallSystem.Availability(state, HallTarget.Dorm).Open);

            state.Facilities.Single(f => f.Type == FacilityType.Infirmary).CurrentLevel = 1;
            state.Facilities.Single(f => f.Type == FacilityType.SkillHall).CurrentLevel = 1; // 訓練所はどれか1つあればよい
            Assert.True(HallSystem.Availability(state, HallTarget.Infirmary).Open);
            Assert.True(HallSystem.Availability(state, HallTarget.Training).Open);
        }

        [Fact]
        public void 依頼とタブは開いてから使える()
        {
            var state = NewGame();
            Assert.False(HallSystem.Availability(state, HallTarget.Commission).Open);
            Assert.False(HallSystem.Availability(state, HallTarget.Research).Open); // 手ほどきありの新しいゲームでは研究室は物語で教わるまで隠れている
            state.CommissionsFromWeek = 1;
            StorySystem.DisableTutorial(state);
            Assert.True(HallSystem.Availability(state, HallTarget.Commission).Open);
            Assert.True(HallSystem.Availability(state, HallTarget.Research).Open);
            Assert.True(HallSystem.Availability(state, HallTarget.Shop).Open);
            Assert.True(HallSystem.Availability(state, HallTarget.Ledger).Open);
        }

        [Fact]
        public void 椅子には部隊のメンバーが順に座り_空いた席と引退した子は空ける()
        {
            var state = NewGame();
            var party = new SavedParty { Name = "第1部隊" };
            party.MemberIds.AddRange(state.Adventurers.Take(3).Select(a => a.Id));
            state.SavedParties.Add(party);
            state.Adventurers[1].IsRetired = true;

            var seated = HallSystem.SeatedMembers(state, 1);
            Assert.Equal(4, seated.Count);
            Assert.Equal(new[] { 1, 2, 3, 4 }, seated.Select(s => s.Seat.Seat));
            Assert.Equal(new Guid?[] { state.Adventurers[0].Id, state.Adventurers[2].Id, null, null }, seated.Select(s => s.Adventurer?.Id));

            Assert.All(HallSystem.SeatedMembers(state, 4), s => Assert.Null(s.Adventurer)); // まだ無い部隊
            Assert.Null(HallSystem.Squad(state, 0));
        }
    }
}
