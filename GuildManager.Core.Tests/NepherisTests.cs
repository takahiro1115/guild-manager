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
    /// <summary>ネフェリスの心臓の間（§0.132）のテスト。</summary>
    public class NepherisTests
    {
        private sealed class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        /// <summary>全能力が power の子。</summary>
        private static Adventurer Kid(string name, int power, int mnd = -1)
        {
            var a = new Adventurer { Name = name, JobClass = JobClass.Warrior, Age = 20, STR = power, VIT = power, MND = mnd < 0 ? power : mnd, AGI = power, DEX = power, INT = power, LDR = power };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static Adventurer[] Squad(string prefix, int power, int mnd = -1) =>
            Enumerable.Range(0, 4).Select(i => Kid($"{prefix}{i}", power, mnd)).ToArray();

        /// <summary>深淵の floorCleared 階までの主を倒した状態。defeated の支配者は決着まで倒しておく。部隊を作って出す。</summary>
        private static GameState NewState(int floorCleared, string[] defeated, params (string Name, Adventurer[] Members)[] squads)
        {
            var state = new GameState { WeekNumber = 5, DungeonFields = SampleData.CreateDefaultFields() };
            var abyss = state.DungeonFields.First(f => f.Id == "abyss");
            abyss.IsUnlocked = true;
            foreach (var boss in abyss.Bosses.Where(b => b.Floor <= floorCleared))
                boss.IsDefeated = true;
            var fieldOf = new Dictionary<string, string> { [RulerSystem.VesperaId] = "forest", [RulerSystem.MelusineId] = "cave", [RulerSystem.OrtensiaId] = "ruins", [RulerSystem.IgniaId] = "canyon" };
            foreach (var id in defeated)
                foreach (var b in state.DungeonFields.First(f => f.Id == fieldOf[id]).Bosses) b.IsDefeated = true;
            foreach (var (name, members) in squads)
            {
                state.Adventurers.AddRange(members);
                state.SavedParties.Add(new SavedParty { Name = name, MemberIds = members.Select(m => m.Id).ToList() });
            }
            RulerSystem.EnsureRulers(state);
            RulerSystem.TrySetSquads(state, Nepheris(state), state.SavedParties.Select(p => p.Id));
            return state;
        }

        private static readonly string[] All = { RulerSystem.VesperaId, RulerSystem.MelusineId, RulerSystem.OrtensiaId, RulerSystem.IgniaId };

        private static RulerState Nepheris(GameState s) => RulerSystem.Find(s, RulerSystem.NepherisId)!;

        private static NepherisBattle Run(NepherisBattle battle, bool use)
        {
            if (battle.Prompt != DuelPromptKind.None) battle.AutoAnswer(use);
            while (!battle.IsOver)
            {
                battle.Advance();
                if (battle.Prompt != DuelPromptKind.None) battle.AutoAnswer(use);
            }
            battle.Finish();
            return battle;
        }

        [Fact]
        public void Nepheris_WaitsAtTheAbyss70F_WithASongAndAHallForTheMonth()
        {
            var s = NewState(60, Array.Empty<string>());
            var n = Nepheris(s);
            Assert.True(RulerSystem.CanChallenge(s, n));
            Assert.True(RulerSystem.UsesSquads(n));
            Assert.Single(n.SongOrder);
            Assert.All(Enum.GetValues<NepherisStage>(), st => Assert.True(NepherisBattle.Shadowed(s, st))); // 誰も倒していない
            var s2 = NewState(60, All);
            Assert.All(Enum.GetValues<NepherisStage>(), st => Assert.False(NepherisBattle.Shadowed(s2, st)));
        }

        [Fact]
        public void UndefeatedRulers_AddTheirTricks()
        {
            var s = NewState(60, Array.Empty<string>(), ("主力", Squad("主", 60)));
            var n = Nepheris(s);
            var weakMind = Kid("心の弱い子", 90, mnd: 40);
            Assert.True(NepherisBattle.IsExcluded(s, n, weakMind, NepherisStage.Duel, outpost: true)); // ヴェスペラの魅了
            var s2 = NewState(60, All, ("主力", Squad("主", 60)));
            Assert.False(NepherisBattle.IsExcluded(s2, Nepheris(s2), weakMind, NepherisStage.Duel, outpost: true));

            var battle = (NepherisBattle)RulerSystem.StartBattle(s, n, new AlwaysMinRng())!;
            Assert.True(battle.Burns(NepherisStage.Duel)); // イグニアを倒していなければ、どの段も灼熱
            var battle2 = (NepherisBattle)RulerSystem.StartBattle(s2, Nepheris(s2), new AlwaysMinRng())!;
            Assert.False(battle2.Burns(NepherisStage.Duel));
            Assert.True(battle2.Burns(NepherisStage.Heat));
        }

        [Fact]
        public void AStrongGuild_ClearsAllFourStages_AndDrivesHerBack()
        {
            var s = NewState(60, All, ("主力", Squad("主", 100)));
            var battle = Run((NepherisBattle)RulerSystem.StartBattle(s, Nepheris(s), new AlwaysMinRng())!, use: false);
            Assert.All(battle.Stages, r => Assert.True(r.Won));
            Assert.True(battle.Retreated); // 3段で50を切れば、4段目を待たずに退く
            Assert.Equal("ひび割れた双子冠", battle.LegendaryFound?.Name);
            Assert.Equal(battle.Stages.Count, s.Materials.GetValueOrDefault(RulerSystem.ShardMaterialId));
        }

        [Fact]
        public void CityVoice_IsReadyAfterTheTruth_AndCancelsOneStage()
        {
            var s = NewState(60, All, ("並", Squad("並", 40)));
            var battle = (NepherisBattle)RulerSystem.StartBattle(s, Nepheris(s), new AlwaysMinRng())!;
            battle.Advance();
            Assert.NotEqual(DuelPromptKind.CityVoice, battle.Prompt); // 真相の前は聞かない

            var s2 = NewState(60, All, ("並", Squad("並", 40)));
            s2.StorySeenWeeks["s04_truth"] = 1;
            var b2 = Run((NepherisBattle)RulerSystem.StartBattle(s2, Nepheris(s2), new AlwaysMinRng())!, use: true);
            Assert.Single(b2.Stages, r => r.Voice);
            Assert.True(b2.Stages.First(r => r.Voice).Won);
            Assert.Equal(1, b2.Stages.Count(r => r.Won));
        }

        [Fact]
        public void DefeatingHerAtTheFinal_ClearsTheGame()
        {
            var s = NewState(90, All, ("主力", Squad("主", 130)));
            var abyss = s.DungeonFields.First(f => f.Id == "abyss");
            abyss.Bosses.First(b => b.Floor == 70).IsDefeated = true;
            var n = Nepheris(s);
            RulerSystem.Refresh(s, n);
            Assert.False(RulerSystem.IsOutpost(s, n));
            n.Hp = 30;
            var battle = Run((NepherisBattle)RulerSystem.StartBattle(s, n, new AlwaysMinRng())!, use: false);
            Assert.True(battle.Defeated);
            Assert.True(s.IsGameCleared);
        }

        [Fact]
        public void TheStageHall_SurvivesTheSave()
        {
            var s = NewState(60, Array.Empty<string>());
            Nepheris(s).StageHall = CorridorTarget.Illusion;
            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(s.ToSaveData()))!);
            Assert.Equal(CorridorTarget.Illusion, Nepheris(restored).StageHall);
            Assert.Single(Nepheris(restored).SongOrder);
        }
    }
}
