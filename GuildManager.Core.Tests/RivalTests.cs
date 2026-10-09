using System.Linq;
using System.Text.Json;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>ライバルギルド（§0.93、大会と育成の栄光 段3-1）：名簿・年の入れ替え・大会への出場・交流戦の相手・セーブ。</summary>
    public class RivalTests
    {
        private static GameState Visited(int week = 1)
        {
            var state = new GameState { WeekNumber = week, Gold = 1000, MasterMood = 50, DungeonFields = SampleData.CreateDefaultFields(), IsabellaVisitWeek = week };
            state.Adventurers.AddRange(SampleData.CreateStarterAdventurers());
            RivalSystem.ProcessWeek(state);
            return state;
        }

        private static RivalGuild Guild(GameState s, string id) => s.RivalGuilds.Single(g => g.Id == id);

        private static TournamentEvent Event(string definitionId, int year = 1)
        {
            var def = TournamentBalance.Find(definitionId)!;
            return new TournamentEvent { DefinitionId = def.Id, Name = def.Name, Kind = def.Kind, Discipline = def.Discipline, Grade = def.Grade, Year = year, Month = 5, Week = 2 };
        }

        [Fact]
        public void 来訪するまで名簿は無い()
        {
            var state = new GameState { WeekNumber = 1, DungeonFields = SampleData.CreateDefaultFields() };
            RivalSystem.ProcessWeek(state);
            Assert.Empty(state.RivalGuilds);
            Assert.Equal(0, state.RivalYear);
        }

        [Fact]
        public void 来訪すると3つのギルドに6人ずつ_看板の子と得意な部門()
        {
            var state = Visited();
            Assert.Equal(new[] { "whitelily", "crimson", "silvermoon" }, state.RivalGuilds.Select(g => g.Id));
            Assert.All(state.RivalGuilds, g => Assert.Equal(RivalBalance.RosterSize, g.Members.Count));
            Assert.Equal(1, state.RivalYear);

            var wl = Guild(state, "whitelily");
            var cecilia = wl.Members.Single(m => m.Name == "セシリア");
            Assert.Equal((21, 175.0, "氷華", TournamentDiscipline.Magic), (cecilia.Age, cecilia.Peak, cecilia.Epithet, cecilia.Discipline));
            Assert.Contains(wl.Members, m => m.Name == "ブリジット" && m.Discipline == TournamentDiscipline.Sword && m.Age == 18);
            Assert.Contains(wl.Members, m => m.Name == "ニナ" && m.Discipline == TournamentDiscipline.Skill && m.Age == 17);

            foreach (var g in state.RivalGuilds)
            {
                var spec = RivalBalance.Guilds.Single(d => d.Id == g.Id).Specialty;
                Assert.True(g.Members.Count(m => m.Discipline == spec) >= RivalBalance.SpecialtyCount, g.Id);
                foreach (var d in new[] { TournamentDiscipline.Sword, TournamentDiscipline.Magic, TournamentDiscipline.Skill })
                    Assert.Contains(g.Members, m => m.Discipline == d);
                Assert.All(g.Members, m => Assert.InRange(m.Age, 17, RivalBalance.StartAgeMax));
                Assert.All(g.Members, m => Assert.InRange(m.Peak, RivalBalance.PeakMin, RivalBalance.PeakMax));
            }
            var names = state.RivalGuilds.SelectMany(g => g.Members).Select(m => m.Name).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
            Assert.DoesNotContain(names, n => RivalBalance.Guilds.Any(g => g.Master == n));
        }

        [Fact]
        public void 名簿は年とギルドで決まる()
        {
            var a = Visited();
            var b = Visited();
            Assert.Equal(a.RivalGuilds.SelectMany(g => g.Members).Select(m => (m.Name, m.Age, m.Peak)),
                         b.RivalGuilds.SelectMany(g => g.Members).Select(m => (m.Name, m.Age, m.Peak)));
        }

        [Fact]
        public void 年が変わると年を取り_26歳の年度末で引退して新人が入る()
        {
            var state = Visited();
            var cecilia = Guild(state, "whitelily").Members.Single(m => m.Name == "セシリア");

            state.WeekNumber = GameCalendar.WeeksPerYear + 1; // 2年目
            RivalSystem.ProcessWeek(state);
            Assert.Equal(2, state.RivalYear);
            Assert.Equal(22, cecilia.Age);

            state.WeekNumber = GameCalendar.WeeksPerYear * 6 + 1; // 7年目：21歳から5年で26歳、その年度末で引退
            RivalSystem.ProcessWeek(state);
            Assert.Equal(7, state.RivalYear);
            Assert.DoesNotContain(cecilia, Guild(state, "whitelily").Members);
            Assert.Contains(cecilia, state.RetiredRivals);
            Assert.Equal(6, cecilia.RetiredYear);
            Assert.Equal("whitelily", state.RetiredRivalGuilds[cecilia.Id]);
            Assert.Same(Guild(state, "whitelily"), RivalSystem.GuildOf(state, cecilia));
            Assert.All(state.RivalGuilds, g => Assert.Equal(RivalBalance.RosterSize, g.Members.Count));
            Assert.All(state.RivalGuilds.SelectMany(g => g.Members), m => Assert.True(m.Age <= RivalBalance.RetirementAge));
            Assert.Contains(Guild(state, "whitelily").Members, m => m.JoinedYear == 7 && m.Age == RivalBalance.RookieAge);
        }

        [Fact]
        public void 強さは全盛と年齢の伸びで決まる()
        {
            var m = new RivalMember { Peak = 150, Age = 18 };
            Assert.Equal(150 * RivalBalance.AgeCurve(18), RivalSystem.Power(m), 6);
            m.Age = 26;
            Assert.Equal(150, RivalSystem.Power(m), 6);
            Assert.Equal("G1級", RivalSystem.PowerLabel(m));
            m.Age = 18;
            Assert.Equal("G3級", RivalSystem.PowerLabel(m));
        }

        [Fact]
        public void 大会に出るのは得意な部門で強さが幅に入る子_1ギルド2人まで()
        {
            var state = Visited();
            foreach (var m in state.RivalGuilds.SelectMany(g => g.Members))
            {
                m.Age = 26;
                m.Peak = 120; // 全員 G1 の幅（110〜）に入り、G2（85〜135）にも入る
            }

            var g1 = RivalSystem.Entrants(state, Event("classic_magic"), 8);
            Assert.All(g1, x => Assert.Equal(TournamentDiscipline.Magic, x.Member.Discipline));
            Assert.All(g1.GroupBy(x => x.Guild.Id), g => Assert.True(g.Count() <= TournamentBalance.EntrantsPerGuild));
            Assert.Equal(state.RivalGuilds.Count * TournamentBalance.EntrantsPerGuild,
                         g1.Count + state.RivalGuilds.Sum(g => System.Math.Max(0, TournamentBalance.EntrantsPerGuild - g.Members.Count(m => m.Discipline == TournamentDiscipline.Magic))));

            Assert.True(RivalSystem.Entrants(state, Event("classic_magic"), 2).Count <= 2); // 空いた枠まで

            foreach (var m in state.RivalGuilds.SelectMany(g => g.Members)) m.Peak = 175; // 175 は G2 の上限 135 を超える
            Assert.Empty(RivalSystem.Entrants(state, Event("royal_sword"), 8));
            Assert.NotEmpty(RivalSystem.Entrants(state, Event("classic_sword"), 8)); // G1 は上限なし
            Assert.Empty(RivalSystem.Entrants(state, Event("classic_party"), 8)); // 迷宮踏破杯には出ない
        }

        [Fact]
        public void 新人戦にはその年に入った新人だけが出る()
        {
            var state = Visited();
            Assert.All(RivalSystem.Entrants(state, Event("rookie", 1), 8), x => Assert.Equal((1, RivalBalance.RookieAge), (x.Member.JoinedYear, x.Member.Age))); // 名簿を作ったときは18歳の子だけが新人
            Assert.All(state.RivalGuilds.SelectMany(g => g.Members), m => Assert.Equal(1 - (m.Age - RivalBalance.RookieAge > 0 ? m.Age - RivalBalance.RookieAge : 0), m.JoinedYear));
            state.WeekNumber = GameCalendar.WeeksPerYear * 6 + 1;
            RivalSystem.ProcessWeek(state);
            var rookies = RivalSystem.Entrants(state, Event("rookie", 7), 8);
            Assert.NotEmpty(rookies);
            Assert.All(rookies, x => Assert.Equal((7, RivalBalance.RookieAge), (x.Member.JoinedYear, x.Member.Age)));
        }

        [Fact]
        public void 大会の組み合わせにライバルが入り_ギルドの名前と戦績が残る()
        {
            var state = Visited();
            foreach (var m in Guild(state, "whitelily").Members) { m.Age = 26; m.Peak = 150; }
            var ev = Event("classic_magic");
            new TournamentSystem(new SeededRng(5)).Resolve(state, ev);

            var rivalNames = Guild(state, "whitelily").Members.Where(m => m.Discipline == TournamentDiscipline.Magic).Select(m => m.Name).ToHashSet();
            var firstRound = ev.Result!.Matches.Where(m => m.Round == 1).SelectMany(m => new[] { (Name: m.NameA, Guild: m.GuildA), (Name: m.NameB, Guild: m.GuildB) }).ToList();
            Assert.Contains(firstRound, x => x.Guild == "白百合の杖" && rivalNames.Contains(x.Name));
            Assert.All(firstRound.Where(x => x.Guild.Length == 0), x => Assert.DoesNotContain(x.Name, rivalNames));
            Assert.Equal(8, firstRound.Count);

            var recorded = Guild(state, "whitelily").Members.SelectMany(m => m.Records).ToList();
            Assert.All(recorded, r => Assert.True(r.Placing <= RivalBalance.RecordMinPlacing));
            if (ev.Result.WinnerGuild == "白百合の杖")
                Assert.Contains(recorded, r => r.Placing == 1);
        }

        [Fact]
        public void 初めてG1を勝つと二つ名が付く_使われていないものから()
        {
            var state = Visited();
            var a = Guild(state, "crimson").Members.First(m => m.Discipline == TournamentDiscipline.Sword);
            var b = Guild(state, "whitelily").Members.First(m => m.Name == "ブリジット");
            RivalSystem.Record(state, a, Event("royal_sword"), 1, 10);
            Assert.Equal("", a.Epithet); // G2 では付かない
            RivalSystem.Record(state, a, Event("classic_sword"), 1, 20);
            RivalSystem.Record(state, b, Event("classic_sword", 2), 1, 70);
            Assert.Equal(RivalBalance.Epithets(TournamentDiscipline.Sword)[0], a.Epithet);
            Assert.Equal(RivalBalance.Epithets(TournamentDiscipline.Sword)[1], b.Epithet);
            Assert.Equal("「" + a.Epithet + "」" + a.Name, RivalSystem.DisplayName(a));

            RivalSystem.Record(state, a, Event("local_sword"), 8, 30);
            Assert.Equal(2, a.Records.Count); // ベスト8は残さない
        }

        [Fact]
        public void 交流戦の相手は白百合の杖のその部門でいちばん強い子()
        {
            var state = Visited();
            Assert.Equal("セシリア", IsabellaSystem.OpponentName(state, TournamentDiscipline.Magic));
            Assert.Equal("ブリジット", IsabellaSystem.OpponentName(state, TournamentDiscipline.Sword));

            state.WeekNumber = GameCalendar.WeeksPerYear * 6 + 1; // セシリアが引退
            RivalSystem.ProcessWeek(state);
            Assert.NotEqual("セシリア", IsabellaSystem.OpponentName(state, TournamentDiscipline.Magic));
            Assert.True(RivalSystem.IsRetiredRival(state, "セシリア"));
            Assert.False(RivalSystem.IsRetiredRival(state, "ブリジット"));

            var empty = new GameState { WeekNumber = 1 };
            Assert.Equal(IsabellaBalance.Opponent(TournamentDiscipline.Magic), IsabellaSystem.OpponentName(empty, TournamentDiscipline.Magic));
        }

        [Fact]
        public void 毎回の一言で引退した看板の子は話さない()
        {
            var state = Visited();
            state.WeekNumber = GameCalendar.WeeksPerYear * 9 + 1; // 10年目：ブリジット（18歳）が引退
            RivalSystem.ProcessWeek(state);
            Assert.True(RivalSystem.IsRetiredRival(state, "ブリジット"));
            for (int i = 0; i < 6; i++)
            {
                var showing = StorySystem.Replay(state, "s02_rematch_lost");
                Assert.DoesNotContain(showing.Pages.SelectMany(p => p), l => l.Kind == StoryLineKind.Speech && l.Speaker == "ブリジット");
                Assert.Single(showing.Pages.SelectMany(p => p), l => l.Kind == StoryLineKind.Speech);
                state.StoryCounters["seen:s02_rematch_lost"] = i + 1;
            }
        }

        [Fact]
        public void セーブして読み直しても名簿と戦績は残る_旧セーブは来訪済みなら作る()
        {
            var state = Visited();
            state.WeekNumber = GameCalendar.WeeksPerYear * 6 + 1;
            RivalSystem.ProcessWeek(state);
            var member = Guild(state, "crimson").Members[0];
            RivalSystem.Record(state, member, Event("classic_sword", 7), 1, state.WeekNumber);

            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(state.ToSaveData()))!);
            Assert.Equal(state.RivalYear, restored.RivalYear);
            Assert.Equal(state.RivalGuilds.SelectMany(g => g.Members).Select(m => (m.Id, m.Name, m.Age, m.Peak, m.Epithet)),
                         restored.RivalGuilds.SelectMany(g => g.Members).Select(m => (m.Id, m.Name, m.Age, m.Peak, m.Epithet)));
            Assert.Single(restored.RivalGuilds.Single(g => g.Id == "crimson").Members[0].Records);
            Assert.Equal(state.RetiredRivals.Select(m => m.Name), restored.RetiredRivals.Select(m => m.Name));
            Assert.Equal(state.RetiredRivalGuilds, restored.RetiredRivalGuilds);

            var old = state.ToSaveData();
            old.RivalGuilds = null;
            old.RetiredRivals = null;
            old.RetiredRivalGuilds = null;
            old.RivalYear = 0;
            var fromOld = GameState.FromSaveData(old);
            Assert.Empty(fromOld.RivalGuilds);
            RivalSystem.ProcessWeek(fromOld);
            Assert.Equal(3, fromOld.RivalGuilds.Count);
            Assert.Equal(7, fromOld.RivalYear);
        }
    }
}
