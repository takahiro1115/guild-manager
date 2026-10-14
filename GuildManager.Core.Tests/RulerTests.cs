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
    /// <summary>大迷宮の支配者とヴェスペラの一騎打ち（§0.127）のテスト。</summary>
    public class RulerTests
    {
        private sealed class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private sealed class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        private static Adventurer Make(string name, int stat, JobClass job = JobClass.Warrior)
        {
            var a = new Adventurer
            {
                Name = name, JobClass = job, Age = 20,
                STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat,
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        /// <summary>森の floorCleared 階までの主を倒した状態（森は開いている）。週は月のはじめ。</summary>
        private static GameState NewState(int floorCleared, params Adventurer[] adventurers)
        {
            var state = new GameState { WeekNumber = 5, DungeonFields = SampleData.CreateDefaultFields() };
            var forest = state.DungeonFields.First(f => f.Id == "forest");
            forest.IsUnlocked = true;
            foreach (var boss in forest.Bosses.Where(b => b.Floor <= floorCleared))
                boss.IsDefeated = true;
            state.Adventurers.AddRange(adventurers);
            RulerSystem.EnsureRulers(state);
            return state;
        }

        private static RulerState Vespera(GameState s) => RulerSystem.Find(s, RulerSystem.VesperaId)!;
        private static FloorBoss ForestBoss(GameState s, int floor) => s.DungeonFields.First(f => f.Id == "forest").Bosses.First(b => b.Floor == floor);

        // ---------------- 状態と挑める時期 ----------------

        [Fact]
        public void Vespera_WaitsAtThe70F_OnlyAfterThe60FIsDefeated()
        {
            var early = NewState(50);
            Assert.Equal(RulerBalance.MaxHp, Vespera(early).Hp);
            Assert.False(RulerSystem.CanChallenge(early, Vespera(early)));
            Assert.Same(Vespera(early), RulerSystem.RulerAt(early, ForestBoss(early, 70))); // 主の決戦では倒せない
            Assert.Equal("支配者ヴェスペラ", ForestBoss(early, 70).Name); // 目盛りや目標の表示も支配者の名前
            Assert.Equal("支配者ヴェスペラ", ForestBoss(early, 100).Name);
            Assert.Null(RulerSystem.RulerAt(early, ForestBoss(early, 60)));

            var ready = NewState(60);
            Assert.True(RulerSystem.CanChallenge(ready, Vespera(ready)));
            Assert.True(RulerSystem.IsOutpost(ready, Vespera(ready)));
            Assert.Equal(70, Vespera(ready).HpFloor);
        }

        [Fact]
        public void AfterThe70F_SheWaitsAtThe100F_WithFullHp_AndIsGoneAfterThe100F()
        {
            var s = NewState(90);
            Assert.True(RulerSystem.CanChallenge(s, Vespera(s)));
            Assert.False(RulerSystem.IsOutpost(s, Vespera(s)));
            Assert.Equal(100, Vespera(s).HpFloor);
            Assert.Equal(RulerBalance.MaxHp, Vespera(s).Hp);

            var done = NewState(100);
            Assert.Null(RulerSystem.BossOf(done, Vespera(done)));
            Assert.False(RulerSystem.CanChallenge(done, Vespera(done)));
        }

        [Fact]
        public void Hp_RegeneratesAtTheStartOfEachMonth_UpToTheMax()
        {
            var s = NewState(60);
            Vespera(s).Hp = 60;
            s.WeekNumber = 6; // 月の途中
            RulerSystem.ProcessNewWeek(s);
            Assert.Equal(60, Vespera(s).Hp);
            s.WeekNumber = 9; // 月のはじめ
            RulerSystem.ProcessNewWeek(s);
            Assert.Equal(60 + RulerBalance.RegenPerMonth, Vespera(s).Hp);
            Vespera(s).Hp = RulerBalance.MaxHp - 1;
            s.WeekNumber = 13;
            RulerSystem.ProcessNewWeek(s);
            Assert.Equal(RulerBalance.MaxHp, Vespera(s).Hp);
        }

        // ---------------- 代表 ----------------

        [Fact]
        public void Representatives_AreSetAtTheMonthStart_UpToFive_AndSkipTheOnesWhoCannotGo()
        {
            var team = Enumerable.Range(0, 7).Select(i => Make($"子{i}", 60)).ToArray();
            team[1].Injury = InjurySeverity.Severe;
            var s = NewState(60, team);

            Assert.True(RulerSystem.TrySetRepresentatives(s, Vespera(s), team.Select(a => a.Id)));
            Assert.Equal(RulerBalance.VesperaMaxRepresentatives, Vespera(s).RepresentativeIds.Count);
            Assert.DoesNotContain(team[1].Id, Vespera(s).RepresentativeIds);
            Assert.Equal(team[0].Id, Vespera(s).RepresentativeIds[0]); // 順番どおり

            s.WeekNumber = 6;
            Assert.False(RulerSystem.TrySetRepresentatives(s, Vespera(s), new[] { team[6].Id }));
        }

        [Fact]
        public void Representatives_DoNotTrainOrSortieThatMonth_OnlyWhileSheCanBeChallenged()
        {
            var rep = Make("代表", 60);
            var other = Make("ほかの子", 60);
            var s = NewState(60, rep, other);
            s.Facilities.First(f => f.Type == FacilityType.DrillHall).CurrentLevel = 1; // 鍛錬所を建てておく
            RulerSystem.TrySetRepresentatives(s, Vespera(s), new[] { rep.Id });

            Assert.True(RulerSystem.IsRepresentative(s, rep.Id));
            Assert.Equal(WeekActivity.RulerDuel, IdleActivitySystem.GetWeekActivity(s, rep));
            Assert.False(new TrainingSystem().TryAssign(s, rep.Id, FacilityType.DrillHall));
            Assert.True(new TrainingSystem().TryAssign(s, other.Id, FacilityType.DrillHall)); // 代表でない子は入れる

            // 挑めないあいだ（70Fを倒した・まだ60Fを倒していない）は、代表に入っていても縛られない
            ForestBoss(s, 70).IsDefeated = true;
            Assert.False(RulerSystem.IsRepresentative(s, rep.Id));
        }

        // ---------------- 魅了 ----------------

        [Fact]
        public void Charm_UsesMnd_TheLonelyAreEasierToCharm()
        {
            var steady = Make("揺るがない", 60);
            var lonely = Make("ひとり", 60);
            var s = NewState(60, steady, lonely, Make("相棒", 50));
            s.Compatibility[CompatibilitySystem.NormalizeKey(steady.Id, s.Adventurers[2].Id)] = PersonalityBalance.BuddyThreshold;

            Assert.Equal(RulerBalance.VesperaCharmThresholdOutpost, RulerSystem.CharmThreshold(s, steady, outpost: true));
            Assert.Equal(DuelCharm.Resisted, RulerSystem.JudgeCharm(s, steady, outpost: true));
            Assert.Equal(RulerBalance.VesperaCharmThresholdOutpost + RulerBalance.VesperaLonelyThresholdBonus, RulerSystem.CharmThreshold(s, lonely, outpost: true));
            Assert.Equal(DuelCharm.Wavered, RulerSystem.JudgeCharm(s, lonely, outpost: true)); // 60 ≥ 70×0.85
            Assert.Equal(DuelCharm.Charmed, RulerSystem.JudgeCharm(s, Make("弱い", 40), outpost: true));
            Assert.Equal(RulerBalance.VesperaCharmThresholdFinal + RulerBalance.VesperaLonelyThresholdBonus, RulerSystem.CharmThreshold(s, lonely, outpost: false));
        }

        // ---------------- 決闘 ----------------

        [Fact]
        public void StrongRepresentatives_DriveHerBackAtThe70F_AndTheFieldMovesOn()
        {
            var aces = Enumerable.Range(0, 3).Select(i => Make($"エース{i}", 100)).ToArray();
            var s = NewState(60, aces);
            var forest = s.DungeonFields.First(f => f.Id == "forest");
            RulerSystem.TrySetRepresentatives(s, Vespera(s), aces.Select(a => a.Id));
            Assert.False(RulerSystem.IsDuelDue(s, Vespera(s))); // 月のはじめに選んだだけでは決闘にならない
            s.WeekNumber = 9;
            RulerSystem.ProcessNewWeek(s); // 代表が月を過ごし終えた
            Assert.True(RulerSystem.IsDuelDue(s, Vespera(s)));

            var duel = (RulerDuel)RulerSystem.ResolveAuto(s, Vespera(s), new AlwaysMinRng(), useInterventions: false)!;

            Assert.True(duel.Retreated);
            Assert.True(ForestBoss(s, 70).IsDefeated);
            Assert.True(forest.ReachedFloor >= 70);
            Assert.All(duel.Bouts, b => Assert.True(b.Won));
            Assert.Equal(aces[0].Id, duel.Bouts[0].AdventurerId);
            Assert.Equal(aces[0].Id, duel.Bouts[1].AdventurerId); // 勝った代表は残る
            Assert.True(duel.HpBefore - duel.Bouts.Sum(b => b.Damage) < RulerBalance.OutpostRetreatHp);
            // 退いたあと、100Fの決着では満タンから
            Assert.Equal(100, Vespera(s).HpFloor);
            Assert.Equal(RulerBalance.MaxHp, Vespera(s).Hp);
            Assert.False(RulerSystem.IsDuelDue(s, Vespera(s))); // 1か月に1回
        }

        [Fact]
        public void WeakRepresentatives_AreCharmed_DealNoDamage_AndStepDown()
        {
            var weak = Enumerable.Range(0, 2).Select(i => Make($"新人{i}", 20)).ToArray();
            var s = NewState(60, weak);
            RulerSystem.TrySetRepresentatives(s, Vespera(s), weak.Select(a => a.Id));

            var duel = (RulerDuel)RulerSystem.ResolveAuto(s, Vespera(s), new AlwaysMaxRng(), useInterventions: false)!;

            Assert.Equal(2, duel.Bouts.Count); // 2人とも負けて終わり
            Assert.All(duel.Bouts, b => { Assert.Equal(DuelCharm.Charmed, b.Charm); Assert.False(b.Won); Assert.Equal(0, b.Damage); });
            Assert.Equal(RulerBalance.MaxHp, Vespera(s).Hp);
            Assert.False(duel.Retreated);
            Assert.False(ForestBoss(s, 70).IsDefeated);
        }

        [Fact]
        public void TheWeaponOrder_ContinuesIntoTheNextMonth()
        {
            var weak = Make("新人", 20);
            var s = NewState(60, weak);
            RulerSystem.TrySetRepresentatives(s, Vespera(s), new[] { weak.Id });
            var first = (RulerDuel)RulerSystem.ResolveAuto(s, Vespera(s), new AlwaysMaxRng(), useInterventions: false)!;
            Assert.Equal(TournamentDiscipline.Sword, first.Bouts[0].Discipline);
            Assert.Equal(1, Vespera(s).WeaponIndex);

            weak.CurrentHP = weak.MaxHP;
            weak.Injury = InjurySeverity.None;
            s.WeekNumber = 9;
            var second = (RulerDuel)RulerSystem.ResolveAuto(s, Vespera(s), new AlwaysMaxRng(), useInterventions: false)!;
            Assert.Equal(TournamentDiscipline.Magic, second.Bouts[0].Discipline);
        }

        [Fact]
        public void HpZero_IsRarelyFatal_TheFallenLeaveTheGuildAndTheRepresentatives()
        {
            // AlwaysMinRng：強制除籍の判定（1 ≤ 10%）は当たる。HPが1なら負けで0になる
            var doomed = Make("倒れる子", 20);
            doomed.CurrentHP = 1;
            var s = NewState(60, doomed);
            RulerSystem.TrySetRepresentatives(s, Vespera(s), new[] { doomed.Id });
            int moodBefore = s.MasterMood;

            var duel = (RulerDuel)RulerSystem.ResolveAuto(s, Vespera(s), new AlwaysMinRng(), useInterventions: false)!;

            Assert.Contains(doomed.Id, duel.FallenIds);
            Assert.True(duel.Bouts[0].Fallen);
            Assert.DoesNotContain(doomed, s.Adventurers);
            Assert.Contains(doomed, s.FallenAdventurers);
            Assert.Empty(Vespera(s).RepresentativeIds);
            Assert.True(s.MasterMood < moodBefore);
        }

        [Fact]
        public void HpZero_OtherwiseSurvivesAtOneHp_SeverelyInjured()
        {
            var lucky = Make("助かる子", 20);
            lucky.CurrentHP = 1;
            var s = NewState(60, lucky);
            RulerSystem.TrySetRepresentatives(s, Vespera(s), new[] { lucky.Id });

            var duel = (RulerDuel)RulerSystem.ResolveAuto(s, Vespera(s), new AlwaysMaxRng(), useInterventions: false)!; // 判定 100 > 10%

            Assert.Contains(lucky.Id, duel.NearDeathIds);
            Assert.Equal(1, lucky.CurrentHP);
            Assert.Equal(InjurySeverity.Severe, lucky.Injury);
            Assert.Contains(lucky, s.Adventurers);
        }

        // ---------------- 介入 ----------------

        [Fact]
        public void Foresight_IsAskedWhenTheNextWeaponIsKnown_AndSwapsInAStrongerRepresentative()
        {
            var mage = Make("魔の子", 40, JobClass.Mage);
            var swordsman = Make("剣の子", 100);
            var s = NewState(60, mage, swordsman);
            RulerSystem.TrySetRepresentatives(s, Vespera(s), new[] { mage.Id, swordsman.Id });
            ForestBoss(s, 70).IntelRate = RulerBalance.VesperaIntelWeaponReveal; // 次の武器（剣）が分かる

            var duel = RulerSystem.StartDuel(s, Vespera(s), new AlwaysMinRng())!;
            duel.Advance();
            Assert.Equal(DuelPromptKind.Foresight, duel.Prompt);
            Assert.Equal(TournamentDiscipline.Sword, duel.PendingDiscipline);
            Assert.Equal(swordsman.Id, duel.BestForesightCandidate());

            duel.AnswerForesight(swordsman.Id);
            Assert.Equal(0, duel.InterventionsLeft);
            duel.Advance();
            duel.Finish();
            Assert.Equal(swordsman.Id, duel.Bouts[0].AdventurerId);
            Assert.True(duel.Bouts[0].Won);
        }

        [Fact]
        public void Foresight_IsNotAskedWithoutIntel()
        {
            var mage = Make("魔の子", 40, JobClass.Mage);
            var swordsman = Make("剣の子", 100);
            var s = NewState(60, mage, swordsman);
            RulerSystem.TrySetRepresentatives(s, Vespera(s), new[] { mage.Id, swordsman.Id });

            var duel = RulerSystem.StartDuel(s, Vespera(s), new AlwaysMinRng())!;
            duel.Advance();
            Assert.NotEqual(DuelPromptKind.Foresight, duel.Prompt);
        }

        [Fact]
        public void CallName_IsAskedWhenCharmed_AndWhenItReaches_SheFightsWavered()
        {
            var charmed = Make("魅入られる子", 100);
            charmed.MND = 30;
            var s = NewState(60, charmed);
            RulerSystem.TrySetRepresentatives(s, Vespera(s), new[] { charmed.Id });

            var duel = RulerSystem.StartDuel(s, Vespera(s), new AlwaysMinRng())!;
            duel.Advance();
            Assert.Equal(DuelPromptKind.CallName, duel.Prompt);
            duel.AnswerCallName(true); // AlwaysMinRng の 1 は届く
            Assert.True(duel.Bouts[0].CalledName);
            Assert.True(duel.Bouts[0].CallNameReached);
            Assert.Equal(DuelCharm.Wavered, duel.Bouts[0].Charm);
            Assert.True(duel.Bouts[0].Damage > 0);
        }

        [Fact]
        public void CallName_Chance_GrowsWithCompatibilityAndMorale_UpToTheCap()
        {
            var a = Make("子", 50);
            var b = Make("仲良し", 50);
            var s = NewState(60, a, b);
            a.Satisfaction = 0;
            Assert.Equal(RulerBalance.CallNameBaseChance + Math.Max(0, CompatibilitySystem.GetCompatibility(s, a.Id, b.Id)) / RulerBalance.CallNameCompatibilityDivisor, RulerSystem.CallNameChance(s, a));
            a.Satisfaction = 100;
            s.Compatibility[CompatibilitySystem.NormalizeKey(a.Id, b.Id)] = 100;
            Assert.Equal(RulerBalance.CallNameMaxChance, RulerSystem.CallNameChance(s, a));
        }

        // ---------------- 見返り（§0.128） ----------------

        [Fact]
        public void EachWin_LeavesAThorn_AndDrivingHerBack_GrantsHerBlades()
        {
            var aces = Enumerable.Range(0, 3).Select(i => Make($"エース{i}", 100)).ToArray();
            var s = NewState(60, aces);
            RulerSystem.TrySetRepresentatives(s, Vespera(s), aces.Select(a => a.Id));

            var duel = (RulerDuel)RulerSystem.ResolveAuto(s, Vespera(s), new AlwaysMinRng(), useInterventions: false)!;

            int wins = duel.Bouts.Count(b => b.Won);
            Assert.True(wins > 0);
            Assert.Equal(wins, duel.ThornsGained);
            Assert.Equal(wins, s.Materials.GetValueOrDefault(RulerSystem.ThornMaterialId));
            Assert.True(duel.Retreated);
            Assert.NotNull(duel.LegendaryFound);
            Assert.Equal("ヴェスペラの双剣", duel.LegendaryFound!.Name);
            Assert.True(duel.LegendaryFound.CountersGimmick(BossGimmickType.Charm));
            Assert.Contains(RulerSystem.DuelClosingLines(duel), l => l.Text.Contains("ヴェスペラの双剣"));
        }

        [Fact]
        public void TheThorn_IsNeverGatheredOrAppraised()
        {
            Assert.DoesNotContain(MaterialBalance.GetEligibleMaterials("forest", 100), m => m.Id == RulerSystem.ThornMaterialId);
            Assert.Equal("茨の欠片", MaterialBalance.GetName(RulerSystem.ThornMaterialId));
        }

        [Fact]
        public void TheAmuletResearch_AddsInterventions()
        {
            var s = NewState(60);
            Assert.Equal(RulerBalance.InterventionsBase, RulerSystem.InterventionsPerBattle(s));
            s.CompletedResearchIds.Add("res_link_amulet_1");
            Assert.Equal(RulerBalance.InterventionsBase + 1, RulerSystem.InterventionsPerBattle(s));
            s.CompletedResearchIds.Add("res_link_amulet_2");
            Assert.Equal(RulerBalance.InterventionsBase + 2, RulerSystem.InterventionsPerBattle(s));
            var research = ResearchBalance.GetAll().First(r => r.Id == "res_link_amulet_1");
            Assert.True(research.RequiredMaterials.ContainsKey(RulerSystem.ThornMaterialId));
        }

        [Fact]
        public void DefeatingHerAtThe100F_OpensTheTrainingHall_TwoCoachingsAtOnce()
        {
            var s = NewState(90);
            Assert.False(RulerSystem.IsTrainingHallOpen(s));
            Assert.Equal(PolicyBalance.CoachingSlots, GuildPolicySystem.CoachingSlots(s));
            Vespera(s).Hp = 5; // 最後のひと押し
            var ace = Make("エース", 100);
            s.Adventurers.Add(ace);
            RulerSystem.TrySetRepresentatives(s, Vespera(s), new[] { ace.Id });

            var duel = (RulerDuel)RulerSystem.ResolveAuto(s, Vespera(s), new AlwaysMinRng(), useInterventions: false)!;

            Assert.True(duel.Defeated);
            Assert.True(ForestBoss(s, 100).IsDefeated);
            Assert.True(RulerSystem.IsTrainingHallOpen(s));
            Assert.Equal(PolicyBalance.CoachingSlots + 1, GuildPolicySystem.CoachingSlots(s));
        }

        // ---------------- 主の決戦では戦えない ----------------

        [Fact]
        public void TheBossAssault_RetreatsAtHerDoor()
        {
            var s = NewState(60, Make("隊員", 100));
            var party = new Party();
            party.TryAdd(s.Adventurers[0]);
            var mission = new ActiveDungeonMission { Party = party, TargetedBoss = ForestBoss(s, 70) };
            var (go, reason) = SquadOrderSystem.JudgeEngage(s, mission, DoorStance.Bold);
            Assert.False(go);
            Assert.Contains(RulerSystem.VesperaName, reason);
        }

        // ---------------- セーブ ----------------

        [Fact]
        public void TheRulerState_SurvivesTheSave_AndAnOldSaveGetsHer()
        {
            var rep = Make("代表", 60);
            var s = NewState(60, rep);
            RulerSystem.TrySetRepresentatives(s, Vespera(s), new[] { rep.Id });
            Vespera(s).Hp = 42;
            Vespera(s).WeaponIndex = 2;
            Vespera(s).DuelCount = 3;
            Vespera(s).PendingDuelWeek = 9;

            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(s.ToSaveData()))!);
            var v = Vespera(restored);
            Assert.Equal(42, v.Hp);
            Assert.Equal(2, v.WeaponIndex);
            Assert.Equal(3, v.DuelCount);
            Assert.Equal(9, v.PendingDuelWeek);
            Assert.Equal(new[] { rep.Id }, v.RepresentativeIds);

            // §0.127 より前のセーブ（支配者の項目が無い）。森の70Fまで倒していれば、100Fで待つ
            ForestBoss(s, 70).IsDefeated = true;
            var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(s.ToSaveData()))!;
            node.AsObject().Remove("Rulers");
            var old = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(node.ToJsonString())!);
            var ov = Vespera(old);
            Assert.Equal(RulerBalance.MaxHp, ov.Hp);
            Assert.Equal(100, ov.HpFloor);
            Assert.Empty(ov.RepresentativeIds);
        }
    }
}
