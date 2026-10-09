using System.Linq;
using System.Text.Json;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>年末のギルドの順位表（§0.94、大会と育成の栄光 段3-2）：栄誉点・並び・ご褒美・迷宮の撃破・セーブ。</summary>
    public class GuildStandingTests
    {
        private static GameState Visited()
        {
            var state = new GameState { WeekNumber = 1, Gold = 1000, MasterMood = 50, DungeonFields = SampleData.CreateDefaultFields(), IsabellaVisitWeek = 1 };
            state.Adventurers.AddRange(SampleData.CreateStarterAdventurers());
            RivalSystem.ProcessWeek(state);
            return state;
        }

        private static TournamentEvent Event(string definitionId, int year, params (string Name, int Placing, int Prize)[] ours)
        {
            var def = TournamentBalance.Find(definitionId)!;
            var ev = new TournamentEvent { DefinitionId = def.Id, Name = def.Name, Kind = def.Kind, Discipline = def.Discipline, Grade = def.Grade, Year = year, Month = 5, Week = 2, Result = new TournamentResult() };
            foreach (var (name, placing, prize) in ours)
                ev.Result.Placings.Add(new TournamentPlacing { Name = name, Placing = placing, Prize = prize });
            return ev;
        }

        private static RivalGuild Guild(GameState s, string id) => s.RivalGuilds.Single(g => g.Id == id);

        [Theory]
        [InlineData(TournamentKind.Final, TournamentGrade.G1, 1, 15)]
        [InlineData(TournamentKind.Classic, TournamentGrade.G1, 1, 10)]
        [InlineData(TournamentKind.Classic, TournamentGrade.G1, 2, 5)]
        [InlineData(TournamentKind.Classic, TournamentGrade.G1, 4, 3)]   // 10×0.25＝2.5 → 切り上げ
        [InlineData(TournamentKind.Classic, TournamentGrade.G1, 8, 0)]
        [InlineData(TournamentKind.Royal, TournamentGrade.G2, 1, 4)]
        [InlineData(TournamentKind.Royal, TournamentGrade.G2, 4, 1)]
        [InlineData(TournamentKind.Invite, TournamentGrade.Special, 1, 4)]
        [InlineData(TournamentKind.Local, TournamentGrade.G3, 1, 2)]
        [InlineData(TournamentKind.Rookie, TournamentGrade.G3, 2, 1)]
        [InlineData(TournamentKind.Local, TournamentGrade.G3, 4, 1)]     // 2×0.25＝0.5 → 切り上げ
        public void 栄誉点は格と順位で決まる(TournamentKind kind, TournamentGrade grade, int placing, int points)
        {
            Assert.Equal(points, GuildStandingSystem.PlacingPoints(kind, grade, placing));
        }

        [Fact]
        public void 自分のギルドは大会の結果と迷宮の撃破から_ライバルは戦績から数える()
        {
            var state = Visited();
            state.TournamentEvents.Add(Event("classic_sword", 1, ("ルカ", 1, 3000)));
            state.TournamentEvents.Add(Event("local_magic", 1, ("ミア", 4, 80), ("ノア", 8, 0)));
            state.TournamentEvents.Add(Event("classic_magic", 2, ("ルカ", 1, 3000))); // 別の年は数えない
            var exchange = Event("classic_skill", 1, ("ルカ", 1, 0));
            exchange.Kind = TournamentKind.Exchange; // 交流戦は数えない
            state.TournamentEvents.Add(exchange);
            state.BossKillsByYear[1] = 3;

            var wl = Guild(state, "whitelily");
            RivalSystem.Record(state, wl.Members[0], Event("classic_magic", 1), 1, 30); // G1優勝 10点・3000G
            RivalSystem.Record(state, wl.Members[1], Event("royal_sword", 1), 2, 20);   // G2準優勝 2点・500G

            var standing = GuildStandingSystem.Compute(state, 1);
            var ours = standing.Rows.Single(r => r.Ours);
            Assert.Equal((10 + 1 + 3, 1, 1, 3080, 3), (ours.Points, ours.G1Wins, ours.Wins, ours.Prize, ours.BossKills));
            var lily = standing.Rows.Single(r => r.GuildId == "whitelily");
            Assert.Equal((12, 1, 1, 3500, 0), (lily.Points, lily.G1Wins, lily.Wins, lily.Prize, lily.BossKills));
            Assert.Equal(4, standing.Rows.Count);
            Assert.Equal(new[] { 1, 2, 3, 4 }, standing.Rows.Select(r => r.Rank));
            Assert.Equal(new[] { "当ギルド", "白百合の杖" }, standing.Rows.Take(2).Select(r => r.Name));
        }

        [Fact]
        public void 同点ならG1の勝ち数_賞金_自分のギルドの順()
        {
            var state = Visited();
            var crimson = Guild(state, "crimson");
            RivalSystem.Record(state, crimson.Members[0], Event("royal_sword", 1), 1, 10);  // 4点・1000G
            state.BossKillsByYear[1] = 4;                                                  // 4点・0G
            var standing = GuildStandingSystem.Compute(state, 1);
            Assert.Equal("紅蓮の牙", standing.Rows[0].Name); // 同点で賞金が多い

            state.BossKillsByYear[1] = 0;
            var empty = GuildStandingSystem.Compute(new GameState { RivalGuilds = { new RivalGuild { Id = "a", Name = "A" } } }, 1);
            Assert.True(empty.Rows[0].Ours); // 全部0なら自分のギルドが上
        }

        [Fact]
        public void 引退したライバルの戦績もその年のギルドに数える()
        {
            var state = Visited();
            var cecilia = Guild(state, "whitelily").Members.Single(m => m.Name == "セシリア");
            RivalSystem.Record(state, cecilia, Event("classic_magic", 6), 1, 6 * 48);
            state.WeekNumber = GameCalendar.WeeksPerYear * 6 + 1;
            RivalSystem.ProcessWeek(state); // セシリアが引退
            Assert.Contains(cecilia, state.RetiredRivals);
            Assert.Equal(10, GuildStandingSystem.Compute(state, 6).Rows.Single(r => r.GuildId == "whitelily").Points);
        }

        [Fact]
        public void 年の最後の週に確定し_1位と2位にご褒美_二度は確定しない()
        {
            var state = Visited();
            state.TournamentEvents.Add(Event("final", 1, ("ルカ", 1, 5000)));
            state.WeekNumber = GameCalendar.WeeksPerYear - 1;
            Assert.Null(GuildStandingSystem.ProcessWeek(state)); // 年の最後の週ではない

            state.WeekNumber = GameCalendar.WeeksPerYear;
            int gold = state.Gold, mood = state.MasterMood;
            var standing = GuildStandingSystem.ProcessWeek(state)!;
            var ours = standing.Rows.Single(r => r.Ours);
            var (rewardGold, rewardMood) = RivalBalance.StandingReward(1);
            Assert.Equal((1, rewardGold, rewardMood), (ours.Rank, ours.RewardGold, ours.RewardMood));
            Assert.Equal(gold + rewardGold, state.Gold);
            Assert.Equal(System.Math.Min(100, mood + rewardMood), state.MasterMood);
            Assert.Single(state.GuildStandings);
            Assert.Null(GuildStandingSystem.ProcessWeek(state));

            Assert.Equal((2000, 5), RivalBalance.StandingReward(2));
            Assert.Equal((0, 0), RivalBalance.StandingReward(3));
        }

        [Fact]
        public void ライバルの名簿が無ければ順位表は作らない()
        {
            var state = new GameState { WeekNumber = GameCalendar.WeeksPerYear, DungeonFields = SampleData.CreateDefaultFields() };
            Assert.Null(GuildStandingSystem.ProcessWeek(state));
            Assert.Empty(state.GuildStandings);
        }

        [Fact]
        public void ボスを倒すとその年の撃破に数える()
        {
            var state = Visited();
            state.WeekNumber = GameCalendar.WeeksPerYear + 5; // 2年目
            var boss = state.DungeonFields.Single(f => f.Id == "forest").Bosses.First();
            boss.IsDefeated = true;
            DungeonExpeditionSystem.ApplyFieldProgression(state, boss);
            Assert.Equal(1, state.BossKillsByYear[2]);
            Assert.False(state.BossKillsByYear.ContainsKey(1));
        }

        [Fact]
        public void セーブして読み直しても順位表と撃破数は残る_旧セーブは空()
        {
            var state = Visited();
            state.BossKillsByYear[1] = 7;
            state.WeekNumber = GameCalendar.WeeksPerYear;
            GuildStandingSystem.ProcessWeek(state);

            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(state.ToSaveData()))!);
            Assert.Equal(7, restored.BossKillsByYear[1]);
            var s = Assert.Single(restored.GuildStandings);
            Assert.Equal(state.GuildStandings[0].Rows.Select(r => (r.Name, r.Rank, r.Points, r.RewardGold)), s.Rows.Select(r => (r.Name, r.Rank, r.Points, r.RewardGold)));

            var old = state.ToSaveData();
            old.GuildStandings = null;
            old.BossKillsByYear = null;
            var fromOld = GameState.FromSaveData(old);
            Assert.Empty(fromOld.GuildStandings);
            Assert.Empty(fromOld.BossKillsByYear);
        }
    }
}
