using System;
using System.Collections.Generic;
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
    /// <summary>メルジーネの調べの読み合い（§0.129）と瞑想のテスト。</summary>
    public class MelusineTests
    {
        private sealed class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private sealed class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        /// <summary>その調べに合う能力だけが高い子（ほかは低い）。</summary>
        private static Adventurer Specialist(string name, SongMode mode, int high, int low = 20)
        {
            var a = new Adventurer { Name = name, JobClass = JobClass.Warrior, Age = 20, STR = low, VIT = low, MND = low, AGI = low, DEX = low, INT = low, LDR = low };
            switch (mode)
            {
                case SongMode.Fierce: a.STR = high; a.VIT = high; break;
                case SongMode.Calm: a.MND = high; break;
                case SongMode.Swift: a.AGI = high; a.DEX = high; break;
                default: a.INT = high; break;
            }
            a.CurrentHP = a.MaxHP;
            return a;
        }

        /// <summary>洞窟の floorCleared 階までの主を倒した状態。部隊を作る。週は月のはじめ。</summary>
        private static GameState NewState(int floorCleared, params (string Name, Adventurer[] Members)[] squads)
        {
            var state = new GameState { WeekNumber = 5, DungeonFields = SampleData.CreateDefaultFields() };
            var cave = state.DungeonFields.First(f => f.Id == "cave");
            cave.IsUnlocked = true;
            foreach (var boss in cave.Bosses.Where(b => b.Floor <= floorCleared))
                boss.IsDefeated = true;
            foreach (var (name, members) in squads)
            {
                state.Adventurers.AddRange(members);
                state.SavedParties.Add(new SavedParty { Name = name, MemberIds = members.Select(m => m.Id).ToList() });
            }
            RulerSystem.EnsureRulers(state);
            return state;
        }

        private static RulerState Melusine(GameState s) => RulerSystem.Find(s, RulerSystem.MelusineId)!;
        private static FloorBoss CaveBoss(GameState s, int floor) => s.DungeonFields.First(f => f.Id == "cave").Bosses.First(b => b.Floor == floor);

        [Fact]
        public void Melusine_WaitsAtTheCave70F_AndUsesSquads()
        {
            var s = NewState(60);
            Assert.True(RulerSystem.CanChallenge(s, Melusine(s)));
            Assert.True(RulerSystem.UsesSquads(Melusine(s)));
            Assert.Equal("支配者メルジーネ", CaveBoss(s, 70).Name);
            Assert.Equal(MelusineBattle.SongCount, Melusine(s).SongOrder.Count);
            Assert.Equal(Enum.GetValues<SongMode>().OrderBy(m => m), Melusine(s).SongOrder.OrderBy(m => m)); // 4つの調べが1回ずつ
        }

        [Fact]
        public void Squads_AreSetAtTheMonthStart_UpToThree_AndTheirMembersAreBusy()
        {
            var squads = Enumerable.Range(0, 4).Select(i => ($"部隊{i}", new[] { Specialist($"子{i}", SongMode.Fierce, 70) })).ToArray();
            var s = NewState(60, squads);
            var ids = s.SavedParties.Select(p => p.Id).ToList();
            Assert.True(RulerSystem.TrySetSquads(s, Melusine(s), ids));
            Assert.Equal(RulerBalance.MelusineMaxSquads, Melusine(s).SquadIds.Count);
            Assert.True(RulerSystem.IsRepresentative(s, s.SavedParties[0].MemberIds[0]));
            Assert.False(RulerSystem.IsRepresentative(s, s.SavedParties[3].MemberIds[0]));
            s.SavedParties[0].Order = SquadOrder.Gather;
            s.SavedParties[0].OrderFieldId = SavedParty.AutoFieldId;
            Assert.Contains("代表", SquadOrderSystem.GetWaitReason(s, s.SavedParties[0]) ?? ""); // 出す部隊はその月、出撃しない
        }

        [Fact]
        public void SquadFit_IsTheAverageOfTheMatchingStats_AndSweetCharmsTheLowInt()
        {
            var brute = Specialist("力持ち", SongMode.Fierce, 72);
            Assert.Equal(72.0 / 60, MelusineBattle.SquadFit(new[] { brute }, SongMode.Fierce, outpost: true), 3);
            Assert.True(MelusineBattle.IsCharmedBySweet(brute, outpost: true)); // INT 20 < 60×0.85
            var sage = Specialist("賢者", SongMode.Sweet, 90);
            Assert.Equal(90.0 / 60, MelusineBattle.SquadFit(new[] { brute, sage }, SongMode.Sweet, outpost: true), 3); // 魅了された子は平均から外す
            Assert.Equal(0, MelusineBattle.SquadFit(new[] { brute }, SongMode.Sweet, outpost: true));
        }

        [Fact]
        public void KnownSongs_SendTheBestSquad_UnknownSongs_FollowTheOrder()
        {
            var s = NewState(60,
                ("前衛", new[] { Specialist("前衛", SongMode.Fierce, 90) }),
                ("祈り手", new[] { Specialist("祈り手", SongMode.Calm, 90) }));
            var m = Melusine(s);
            m.SongOrder = new List<SongMode> { SongMode.Calm, SongMode.Fierce, SongMode.Calm, SongMode.Fierce };
            RulerSystem.TrySetSquads(s, m, s.SavedParties.Select(p => p.Id));

            // 解析なし：1曲目は1番目の部隊（前衛）が静かな調べに出て呑まれる
            var blind = (MelusineBattle)RulerSystem.ResolveAuto(s, m, new AlwaysMinRng(), useInterventions: false)!;
            Assert.Equal("前衛", blind.Songs[0].SquadName);
            Assert.False(blind.Songs[0].Won);
            Assert.Equal("祈り手", blind.Songs[1].SquadName); // 2曲目は2番目
            Assert.False(blind.Songs[1].Won);

            // 全曲分かっている：いちばん合う部隊が出て、全部打ち破る
            var s2 = NewState(60,
                ("前衛", new[] { Specialist("前衛", SongMode.Fierce, 90) }),
                ("祈り手", new[] { Specialist("祈り手", SongMode.Calm, 90) }));
            var m2 = Melusine(s2);
            m2.SongOrder = new List<SongMode> { SongMode.Calm, SongMode.Fierce, SongMode.Calm, SongMode.Fierce };
            CaveBoss(s2, 70).IntelRate = 1.0;
            RulerSystem.TrySetSquads(s2, m2, s2.SavedParties.Select(p => p.Id));
            var seen = (MelusineBattle)RulerSystem.ResolveAuto(s2, m2, new AlwaysMinRng(), useInterventions: false)!;
            Assert.Equal(new[] { "祈り手", "前衛" }, seen.Songs.Take(2).Select(x => x.SquadName));
            Assert.True(seen.Songs[0].Won);
            Assert.Equal(seen.Songs.Count(x => x.Won), s2.Materials.GetValueOrDefault(RulerSystem.PearlMaterialId));
        }

        [Fact]
        public void Swap_IsAskedWhenTheSquadIsBeingSwallowed()
        {
            var s = NewState(60,
                ("前衛", new[] { Specialist("前衛", SongMode.Fierce, 90) }),
                ("祈り手", new[] { Specialist("祈り手", SongMode.Calm, 90) }));
            var m = Melusine(s);
            m.SongOrder = new List<SongMode> { SongMode.Calm, SongMode.Fierce, SongMode.Swift, SongMode.Sweet };
            RulerSystem.TrySetSquads(s, m, s.SavedParties.Select(p => p.Id));

            var battle = (MelusineBattle)RulerSystem.StartBattle(s, m, new AlwaysMinRng())!;
            battle.Advance();
            Assert.Equal(DuelPromptKind.Swap, battle.Prompt);
            Assert.Equal("前衛", battle.PendingSquad!.Name);
            var choice = Assert.Single(battle.PromptChoices());
            battle.Answer(true, choice.Id);
            battle.Advance();
            Assert.Equal("祈り手", battle.Songs[0].SquadName);
            Assert.Equal("前衛", battle.Songs[0].SwappedFrom);
            Assert.True(battle.Songs[0].Won);
        }

        [Fact]
        public void Lumina_CanListen_OnlyAfterTheSecondOmen()
        {
            var s = NewState(60,
                ("前衛", new[] { Specialist("前衛", SongMode.Fierce, 90) }),
                ("祈り手", new[] { Specialist("祈り手", SongMode.Calm, 90) }));
            var m = Melusine(s);
            m.SongOrder = new List<SongMode> { SongMode.Calm, SongMode.Fierce, SongMode.Swift, SongMode.Sweet };
            RulerSystem.TrySetSquads(s, m, s.SavedParties.Select(p => p.Id));
            var before = RulerSystem.StartBattle(s, m, new AlwaysMinRng())!;
            before.Advance();
            Assert.NotEqual(DuelPromptKind.LuminaListen, before.Prompt);

            s.StorySeenWeeks["s04_omen2"] = 1;
            var after = (MelusineBattle)RulerSystem.StartBattle(s, m, new AlwaysMinRng())!;
            after.Advance();
            Assert.Equal(DuelPromptKind.LuminaListen, after.Prompt);
            after.Answer(true, null);
            after.Advance();
            Assert.True(after.Songs[0].LuminaHeard);
            Assert.Equal("祈り手", after.Songs[0].SquadName);
        }

        [Fact]
        public void DrivingHerBack_GrantsTheEarring_AndTheSongOrderChanges()
        {
            var squads = new[] { SongMode.Fierce, SongMode.Calm, SongMode.Swift }
                .Select(mode => (mode.ToString(), new[] { Specialist(mode.ToString(), mode, 100) })).ToArray();
            var s = NewState(60, squads);
            var m = Melusine(s);
            CaveBoss(s, 70).IntelRate = 1.0;
            RulerSystem.TrySetSquads(s, m, s.SavedParties.Select(p => p.Id));
            var before = m.SongOrder.ToList();

            var battle = RulerSystem.ResolveAuto(s, m, new AlwaysMinRng(), useInterventions: false)!;

            Assert.True(battle.Retreated);
            Assert.Equal("歌姫の耳飾り", battle.LegendaryFound?.Name);
            Assert.True(battle.LegendaryFound!.CountersGimmick(BossGimmickType.Regeneration));
            Assert.True(CaveBoss(s, 70).IsDefeated);
            Assert.Equal(MelusineBattle.SongCount, m.SongOrder.Count);
        }

        [Fact]
        public void Meditation_IsOpenAfterTheFinal_AndGrowsOnlyMnd()
        {
            var a = Specialist("瞑想する子", SongMode.Fierce, 50);
            a.IdleActivity = IdleActivity.Meditation;
            var s = NewState(90, ("部隊", new[] { a }));
            Assert.False(RulerSystem.IsMeditationOpen(s));
            Assert.Equal(WeekActivity.Help, IdleActivitySystem.GetWeekActivity(s, a)); // 泉が無ければ研究を手伝う

            CaveBoss(s, 100).IsDefeated = true;
            Assert.True(RulerSystem.IsMeditationOpen(s));
            Assert.Equal(WeekActivity.Meditation, IdleActivitySystem.GetWeekActivity(s, a));
            int mnd = a.MND;
            var events = new GrowthSystem(new AlwaysMinRng()).ProcessMeditation(s, new HashSet<Guid> { a.Id });
            Assert.All(events, e => Assert.Equal("MND", e.Stat));
            Assert.True(a.MND >= mnd);
        }

        [Fact]
        public void TheSquadsAndSongOrder_SurviveTheSave()
        {
            var s = NewState(60, ("前衛", new[] { Specialist("前衛", SongMode.Fierce, 90) }));
            var m = Melusine(s);
            RulerSystem.TrySetSquads(s, m, s.SavedParties.Select(p => p.Id));
            m.SongOrder = new List<SongMode> { SongMode.Sweet, SongMode.Swift, SongMode.Calm, SongMode.Fierce };
            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(s.ToSaveData()))!);
            var rm = Melusine(restored);
            Assert.Equal(m.SquadIds, rm.SquadIds);
            Assert.Equal(m.SongOrder, rm.SongOrder);
        }
    }
}
