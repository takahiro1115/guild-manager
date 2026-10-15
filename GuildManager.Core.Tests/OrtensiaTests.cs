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
    /// <summary>オルテンシアの玉座への回廊（§0.130）と古王の寝所のテスト。</summary>
    public class OrtensiaTests
    {
        private sealed class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private static Adventurer Kid(string name, int str = 20, int vit = 20, int intel = 20, int mnd = 20, int ldr = 20, int dex = 20)
        {
            var a = new Adventurer { Name = name, JobClass = JobClass.Warrior, Age = 20, STR = str, VIT = vit, MND = mnd, AGI = 20, DEX = dex, INT = intel, LDR = ldr };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        /// <summary>前衛4人（STR・VIT が high）。</summary>
        private static Adventurer[] Vanguard(string prefix, int high) =>
            Enumerable.Range(0, 4).Select(i => Kid($"{prefix}{i}", str: high, vit: high, ldr: high)).ToArray();

        /// <summary>廃墟の floorCleared 階までの主を倒した状態。部隊を作る。週は月のはじめ。</summary>
        private static GameState NewState(int floorCleared, params (string Name, Adventurer[] Members)[] squads)
        {
            var state = new GameState { WeekNumber = 5, DungeonFields = SampleData.CreateDefaultFields() };
            var ruins = state.DungeonFields.First(f => f.Id == "ruins");
            ruins.IsUnlocked = true;
            foreach (var boss in ruins.Bosses.Where(b => b.Floor <= floorCleared))
                boss.IsDefeated = true;
            foreach (var (name, members) in squads)
            {
                state.Adventurers.AddRange(members);
                state.SavedParties.Add(new SavedParty { Name = name, MemberIds = members.Select(m => m.Id).ToList() });
            }
            RulerSystem.EnsureRulers(state);
            return state;
        }

        private static RulerState Ortensia(GameState s) => RulerSystem.Find(s, RulerSystem.OrtensiaId)!;
        private static FloorBoss RuinsBoss(GameState s, int floor) => s.DungeonFields.First(f => f.Id == "ruins").Bosses.First(b => b.Floor == floor);

        [Fact]
        public void Ortensia_WaitsAtTheRuins70F_WithThreeFullSeals()
        {
            var s = NewState(60);
            var o = Ortensia(s);
            Assert.True(RulerSystem.CanChallenge(s, o));
            Assert.True(RulerSystem.UsesSquads(o));
            Assert.Equal("支配者オルテンシア", RuinsBoss(s, 70).Name);
            Assert.Equal(Enumerable.Repeat(RulerBalance.OrtensiaSealMax, 3), o.SealHp);
            Assert.False(OrtensiaBattle.ThroneOpen(o));
        }

        [Fact]
        public void Targets_AreSetWithTheSquads()
        {
            var s = NewState(60, ("前衛", Vanguard("前", 60)), ("騎士", new[] { Kid("騎士", str: 90, ldr: 90) }));
            var o = Ortensia(s);
            Assert.True(RulerSystem.TrySetSquads(s, o, s.SavedParties.Select(p => p.Id), new[] { CorridorTarget.Skeleton, CorridorTarget.KnightKing }));
            Assert.Equal(CorridorTarget.KnightKing, RulerSystem.TargetOf(o, s.SavedParties[1].Id));
            Assert.True(RulerSystem.IsRepresentative(s, s.SavedParties[1].MemberIds[0]));
        }

        [Fact]
        public void SquadFit_DiffersByHall()
        {
            var s = NewState(60);
            var four = Vanguard("前", 60);
            Assert.Equal(1.0, OrtensiaBattle.SquadFit(s, four, CorridorTarget.Skeleton, outpost: true), 3); // 合計÷（60×4）
            Assert.Equal(0.25, OrtensiaBattle.SquadFit(s, four.Take(1).ToList(), CorridorTarget.Skeleton, outpost: true), 3); // 1人では足りない
            var ace = Kid("騎士", str: 90, ldr: 90);
            Assert.Equal(1.5, OrtensiaBattle.SquadFit(s, new[] { ace, Kid("並") }, CorridorTarget.KnightKing, outpost: true), 3); // いちばん強い1人
            var sage = Kid("賢者", intel: 72, mnd: 72);
            Assert.True(OrtensiaBattle.IsExcluded(s, ace, CorridorTarget.Illusion, outpost: true)); // （20+20）÷2 < 51
            Assert.Equal(1.2, OrtensiaBattle.SquadFit(s, new[] { ace, sage }, CorridorTarget.Illusion, outpost: true), 3); // 惑わされた子は平均から外す
        }

        [Fact]
        public void Seals_PersistAcrossBattles_AndBreakingAllOpensTheThroneTheSameMonth()
        {
            var s = NewState(60,
                ("前衛", Vanguard("前", 60)),
                ("騎士", new[] { Kid("騎士", str: 102, ldr: 102) }),
                ("玉座", Vanguard("玉", 70)));
            var o = Ortensia(s);
            o.SealHp[(int)CorridorTarget.Illusion] = 0; // 幻惑の間はもう破れている
            RulerSystem.TrySetSquads(s, o, s.SavedParties.Select(p => p.Id), new[] { CorridorTarget.Skeleton, CorridorTarget.KnightKing, CorridorTarget.Throne });

            var first = (OrtensiaBattle)RulerSystem.ResolveAuto(s, o, new AlwaysMinRng(), useInterventions: false)!;
            Assert.Equal(40, o.SealHp[(int)CorridorTarget.Skeleton]); // 60×1.0 を削り、残る
            Assert.Equal(0, o.SealHp[(int)CorridorTarget.KnightKing]); // 60×1.7＝102 で1か月で破れる
            Assert.True(first.Assaults.Single(a => a.Target == CorridorTarget.Throne).Idle); // 玉座はまだ閉じている

            foreach (var a in s.Adventurers) a.CurrentHP = a.MaxHP;
            var second = (OrtensiaBattle)RulerSystem.ResolveAuto(s, o, new AlwaysMinRng(), useInterventions: false)!;
            Assert.True(second.Assaults.First().ThroneOpened);
            var throne = second.Assaults.Single(a => a.Target == CorridorTarget.Throne);
            Assert.False(throne.Idle);
            Assert.True(throne.Won);
            Assert.True(o.Hp < RulerBalance.MaxHp);
            Assert.True(s.Materials.GetValueOrDefault(RulerSystem.SealMaterialId) >= 3); // 破った封印2つと玉座1回
        }

        [Fact]
        public void Awe_FreezesLowLdr_ButNotEpithetHolders_AndFameWeakensIt()
        {
            var s = NewState(60);
            var meek = Kid("おとなしい子", ldr: 40);
            Assert.Equal(60, OrtensiaBattle.AweThreshold(s, outpost: true));
            Assert.True(OrtensiaBattle.IsExcluded(s, meek, CorridorTarget.Throne, outpost: true)); // 40 < 51

            meek.TournamentRecords.Add(new TournamentRecord { Kind = TournamentKind.Rookie, Grade = TournamentGrade.G3, Placing = 1 }); // 新人王（誉）
            Assert.True(OrtensiaBattle.HasEpithet(s, meek));
            Assert.False(OrtensiaBattle.IsExcluded(s, meek, CorridorTarget.Throne, outpost: true));

            var champ = Kid("覇者");
            for (int i = 0; i < 20; i++)
                champ.TournamentRecords.Add(new TournamentRecord { Kind = TournamentKind.Classic, Grade = TournamentGrade.G1, Placing = 1, Year = i + 1, DefinitionId = "x" });
            s.RetiredAdventurers.Add(champ); // 引退した子の優勝も数える
            Assert.Equal(60 - RulerBalance.OrtensiaAweFameMax, OrtensiaBattle.AweThreshold(s, outpost: true));
        }

        [Fact]
        public void Reseal_HappensEveryThreeMonths_WithoutABattle()
        {
            var s = NewState(60);
            var o = Ortensia(s);
            o.SealHp[0] = 0;
            for (int m = 0; m < RulerBalance.OrtensiaResealMonths; m++)
            {
                RulerSystem.ProcessNewWeek(s);
                s.WeekNumber += 4;
            }
            Assert.Equal(RulerBalance.OrtensiaSealMax, o.SealHp[0]);
            Assert.False(o.ResealPending);
        }

        [Fact]
        public void Reinforce_StopsTheReseal_AndTheGuardDoesNotAttack()
        {
            var s = NewState(60, ("盾", Vanguard("盾", 70)), ("騎士", new[] { Kid("騎士", str: 90, ldr: 90) }));
            var o = Ortensia(s);
            o.SealHp[(int)CorridorTarget.Skeleton] = 0;
            o.ResealPending = true;
            RulerSystem.TrySetSquads(s, o, s.SavedParties.Select(p => p.Id), new[] { CorridorTarget.Illusion, CorridorTarget.KnightKing });

            var battle = (OrtensiaBattle)RulerSystem.StartBattle(s, o, new AlwaysMinRng())!;
            battle.Advance();
            Assert.Equal(DuelPromptKind.Reinforce, battle.Prompt);
            battle.AutoAnswer(useInterventions: true);
            while (!battle.IsOver) { battle.Advance(); if (battle.Prompt != DuelPromptKind.None) battle.AutoAnswer(true); }
            battle.Finish();

            Assert.Equal("盾", battle.GuardSquadName);
            Assert.Equal(0, o.SealHp[(int)CorridorTarget.Skeleton]); // 張り直されなかった
            Assert.DoesNotContain(battle.Assaults, a => a.SquadName == "盾");
            Assert.False(o.ResealPending);

            // 援軍を出さなければ張り直される
            var s2 = NewState(60, ("盾", Vanguard("盾", 70)));
            var o2 = Ortensia(s2);
            o2.SealHp[(int)CorridorTarget.Skeleton] = 0;
            o2.ResealPending = true;
            RulerSystem.TrySetSquads(s2, o2, s2.SavedParties.Select(p => p.Id), new[] { CorridorTarget.KnightKing });
            var b2 = (OrtensiaBattle)RulerSystem.ResolveAuto(s2, o2, new AlwaysMinRng(), useInterventions: false)!;
            Assert.Equal(CorridorTarget.Skeleton, b2.Resealed);
            Assert.Equal(RulerBalance.OrtensiaSealMax, o2.SealHp[(int)CorridorTarget.Skeleton]);
        }

        [Fact]
        public void Redirect_SendsTheSquadThatBrokeASeal_ToAnotherHall()
        {
            var ace = Kid("騎士", str: 110, vit: 110, ldr: 110, intel: 110, mnd: 110);
            var s = NewState(60, ("騎士", new[] { ace }));
            var o = Ortensia(s);
            RulerSystem.TrySetSquads(s, o, s.SavedParties.Select(p => p.Id), new[] { CorridorTarget.KnightKing });

            var battle = (OrtensiaBattle)RulerSystem.StartBattle(s, o, new AlwaysMinRng())!;
            battle.Advance();
            Assert.Equal(DuelPromptKind.Redirect, battle.Prompt);
            Assert.NotEmpty(battle.PromptChoices());
            battle.Answer(true, OrtensiaBattle.TargetChoiceId(CorridorTarget.KnightKing)); // 破れた間は選べない
            Assert.Equal(1, battle.InterventionsLeft);
            Assert.Single(battle.Assaults);

            var s2 = NewState(60, ("騎士", new[] { Kid("騎士", str: 110, vit: 110, ldr: 110), Kid("賢者", intel: 110, mnd: 110) }));
            var o2 = Ortensia(s2);
            RulerSystem.TrySetSquads(s2, o2, s2.SavedParties.Select(p => p.Id), new[] { CorridorTarget.KnightKing });
            var b2 = (OrtensiaBattle)RulerSystem.ResolveAuto(s2, o2, new AlwaysMinRng(), useInterventions: true)!;
            Assert.Equal(2, b2.Assaults.Count);
            Assert.True(b2.Assaults[1].Redirected);
            Assert.Equal(CorridorTarget.Illusion, b2.Assaults[1].Target); // 合い具合の届く間へ
        }

        [Fact]
        public void DrivingHerBack_GrantsTheShroud()
        {
            var s = NewState(60, ("玉座", Vanguard("玉", 90)));
            var o = Ortensia(s);
            for (int i = 0; i < 3; i++) o.SealHp[i] = 0;
            o.Hp = 60;
            RulerSystem.TrySetSquads(s, o, s.SavedParties.Select(p => p.Id), new[] { CorridorTarget.Throne });

            var battle = RulerSystem.ResolveAuto(s, o, new AlwaysMinRng(), useInterventions: false)!;

            Assert.True(battle.Retreated);
            Assert.Equal("女王の聖骸布", battle.LegendaryFound?.Name);
            Assert.True(battle.LegendaryFound!.CountersGimmick(BossGimmickType.Swarm));
            Assert.Equal(Enumerable.Repeat(RulerBalance.OrtensiaSealMax, 3), o.SealHp); // 決着の階は封印を張り直したて
        }

        [Fact]
        public void NobleQuarters_AddANobleDaughterToTheRecruitment()
        {
            var s = NewState(60);
            int before = new RecruitmentSystem(new SeededRng(1)).GenerateCandidates(s).Count;
            foreach (var b in s.DungeonFields.First(f => f.Id == "ruins").Bosses) b.IsDefeated = true;
            Assert.True(RulerSystem.IsNobleQuartersOpen(s));
            var offers = new RecruitmentSystem(new SeededRng(1)).GenerateCandidates(s);
            Assert.Equal(before + 1, offers.Count);
            Assert.True(offers.Last().IsNobleDaughter);
            Assert.Equal(before, offers.Count(o => !o.IsNobleDaughter));
        }

        [Fact]
        public void SealsAndTargets_SurviveTheSave_AndOldSavesGetFullSeals()
        {
            var s = NewState(60, ("騎士", new[] { Kid("騎士", str: 90, ldr: 90) }));
            var o = Ortensia(s);
            RulerSystem.TrySetSquads(s, o, s.SavedParties.Select(p => p.Id), new[] { CorridorTarget.Throne });
            o.SealHp = new List<int> { 0, 40, 100 };
            o.ResealCounter = 2;
            o.ResealPending = true;
            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(s.ToSaveData()))!);
            var ro = Ortensia(restored);
            Assert.Equal(o.SealHp, ro.SealHp);
            Assert.Equal(o.SquadTargets, ro.SquadTargets);
            Assert.Equal(2, ro.ResealCounter);
            Assert.True(ro.ResealPending);

            var data = s.ToSaveData();
            data.Rulers!.RemoveAll(r => r.RulerId == RulerSystem.OrtensiaId); // §0.130 より前のセーブ
            foreach (var r in data.Rulers) { r.SealHp = null!; r.SquadTargets = null!; }
            var old = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(data))!);
            Assert.Equal(Enumerable.Repeat(RulerBalance.OrtensiaSealMax, 3), Ortensia(old).SealHp);
        }
    }
}
