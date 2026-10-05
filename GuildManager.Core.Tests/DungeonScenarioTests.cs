using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// ダンジョン攻略の通しシナリオテスト（→ 実装指示 Step 5）。
    ///
    /// 「採用 → 調査で解析率を上げる → 判明したギミックへ対策を組む → 討伐」という
    /// 本システムの導線が、個々のエンジンを跨いで実際に繋がっていることを検証する。
    /// あわせて「無調査・無対策での突撃は壊滅する」という設計上の約束も確認する。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class DungeonScenarioTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        /// <summary>調査・討伐の両方に耐える水準の冒険者（斥候は配置補正1.0で火力計算が読みやすい）。</summary>
        private static Adventurer MakeAdventurer(JobClass job, int stat, Placement placement = Placement.Front)
        {
            var a = new Adventurer
            {
                Age = 18, JobClass = job, Placement = placement,
                STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat,
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            return party;
        }


        /// <summary>猛毒（神官＋MND）と即死級（騎士＋LDR）を持つ階層ボス（§0.68：備えは職業0.5＋能力の合計÷300）。</summary>
        private static FloorBoss MakeGuardian(int floor = 2) => new FloorBoss
        {
            Name = "階層の守護者",
            Floor = floor,
            MaxHp = 800,
            CurrentHp = 800,
            Gimmicks =
            {
                new BossGimmick
                {
                    Type = BossGimmickType.Poison,
                    CounterRoles = { JobClass.Cleric },
                    RequiredCounterStat = "MND", RequiredCounterStatThreshold = 300,
                    DangerLevel = 2,
                },
                new BossGimmick
                {
                    Type = BossGimmickType.InstantKill,
                    CounterRoles = { JobClass.Knight },
                    RequiredCounterStat = "LDR", RequiredCounterStatThreshold = 300,
                    DangerLevel = 5,
                },
            },
        };

        [Fact]
        public void Scenario_ScoutThenFormCounterPartyThenDefeatTheFloorBoss()
        {
            var scout = new ScoutingResolver(new AlwaysMinRng());
            var dungeon = new DungeonResolver(new AlwaysMinRng());
            var boss = MakeGuardian(floor: 2);

            // ---- 第1段階：調査隊（AGI/DEXとINTを兼ね備えた斥候2名）で情報を集める ----
            Assert.Equal(IntelTier.Unknown, ScoutingResolver.GetTier(boss.IntelRate));

            var scoutParty = PartyOf(MakeAdventurer(JobClass.Ranger, 60), MakeAdventurer(JobClass.Ranger, 60));

            var firstScout = scout.Resolve(scoutParty, boss);
            Assert.True(firstScout.StealthSucceeded, "十分なAGI/DEXがあれば見つからずに帰れるはず");
            Assert.True(firstScout.IntelGained > 0);

            // 2回調査すれば完全解析（1.0）まで到達し、討伐時の与ダメージ補正が付く。
            scout.Resolve(scoutParty, boss);
            Assert.Equal(IntelTier.Complete, ScoutingResolver.GetTier(boss.IntelRate));
            Assert.Equal(1.0, boss.IntelRate, precision: 10);

            // ---- 第2段階：判明したギミックに備える部隊を編成する ----
            // 猛毒 → 神官（0.5）＋MND合計180÷300（0.6）／即死級 → 騎士（0.5）＋LDR合計180÷300（0.6）。どちらも万全。
            var strikeParty = PartyOf(
                MakeAdventurer(JobClass.Knight, 60),
                MakeAdventurer(JobClass.Ranger, 60),
                MakeAdventurer(JobClass.Cleric, 60, Placement.Back));

            var result = dungeon.Resolve(strikeParty, boss);

            // ---- 検証：備えが揃っていれば撃破でき、全員が生還する ----
            Assert.Equal(DungeonOutcome.Victory, result.Outcome);
            Assert.True(boss.IsDefeated);

            Assert.Contains(BossGimmickType.Poison, result.CounteredGimmicks);
            Assert.Contains(BossGimmickType.InstantKill, result.CounteredGimmicks);
            Assert.Empty(result.UncounteredGimmicks);
            Assert.Equal(1.0, result.DamageMultiplier, precision: 10);

            Assert.True(result.FullIntelBonusApplied, "完全解析の与ダメージ補正が乗るはず");
            Assert.Empty(result.ForceRetiredAdventurerIds);
            Assert.All(strikeParty.Members, m => Assert.True(m.CurrentHP > 0));
            Assert.All(strikeParty.Members, m => Assert.False(m.IsPoisoned));
        }

        [Fact]
        public void Scenario_RecklessAssaultWithoutAnyReadiness_WipesOutTheParty()
        {
            // 同じボスへ、騎士も指揮（LDR）も無い部隊で突撃した場合。即死級への備えが0になる。
            var dungeon = new DungeonResolver(new AlwaysMinRng());
            var boss = MakeGuardian(floor: 2);

            var warrior = MakeAdventurer(JobClass.Warrior, 60);
            var ranger = MakeAdventurer(JobClass.Ranger, 60);
            warrior.LDR = 0;
            ranger.LDR = 0;
            var recklessParty = PartyOf(warrior, ranger);

            var result = dungeon.Resolve(recklessParty, boss);

            // 備えの無い即死級を踏んで全滅し、全員が強制除籍（恒久ロスト）になる。
            Assert.Contains(BossGimmickType.InstantKill, result.UncounteredGimmicks);
            Assert.Contains(BossGimmickType.Poison, result.UncounteredGimmicks);
            Assert.Equal(recklessParty.Members.Count, result.ForceRetiredAdventurerIds.Count);
            Assert.All(recklessParty.Members, m => Assert.Equal(0, m.CurrentHP));
        }


        [Fact]
        public void Scenario_EightYearCareer_EndsInMaturityRetirementWithContributionPay()
        {
            // ---- 採用 → 8年稼働 → 26歳満期引退 → 功績に応じた退職金、までの通し ----
            var recruitment = new RecruitmentSystem(new AlwaysMinRng());
            var aging = new AgingSystem(new AlwaysMinRng());

            var state = new GameState { Gold = 100_000, WeekNumber = 1 };

            var offer = recruitment.GenerateCandidates(state)[0];
            Assert.Equal(18, offer.Candidate.Age); // 加入は18歳に統一されている

            Assert.True(recruitment.TryHire(state, offer));
            var rookie = offer.Candidate;

            // 危険な任務をこなして功績を積んだ想定（→ QuestDispatchSystemが加算する値を直接与える）。
            rookie.TotalContributionScore = 120;

            // 8年分（48週×8）の週次決算を回す。年度末のたびに加齢し、8回目の年度末で満期を迎える。
            for (int week = 1; week <= AgingSystem.MaxActiveWeeks; week++)
            {
                state.WeekNumber = week;
                aging.ProcessWeeklyAging(state);
            }

            // ---- 検証：衰えではなく「満期」でギルドを去る ----
            Assert.True(rookie.IsRetired);
            Assert.Equal(26, rookie.RetiredAtAge);
            Assert.DoesNotContain(rookie, state.Adventurers);
            Assert.Contains(rookie, state.RetiredAdventurers);

            // 8年間の稼働がすべて記録されている（引退した週まで加算される）。
            Assert.Equal(AgingSystem.MaxActiveWeeks, rookie.ActiveWeeks);

            // 退職金には功績分が上乗せされている。
            int expectedPay = rookie.WeeklyWage * EconomyBalance.SeveranceWeeks
                + (int)Math.Round(120 * EconomyBalance.SeveranceContributionCoefficient);
            Assert.Equal(expectedPay, AgingSystem.CalculateSeverancePay(rookie));
            Assert.True(rookie.SeverancePaid);
        }

        [Fact]
        public void Scenario_NoStatDecayOverTheWholeCareer()
        {
            // 8年間を通してステータスが一切下がらないこと（衰微の廃止）。
            var aging = new AgingSystem(new AlwaysMinRng());
            var veteran = MakeAdventurer(JobClass.Warrior, 50);
            var state = new GameState { Gold = 100_000, Adventurers = { veteran } };

            var before = (veteran.STR, veteran.AGI, veteran.VIT, veteran.DEX, veteran.MND, veteran.LDR, veteran.INT);

            for (int week = 1; week <= AgingSystem.MaxActiveWeeks; week++)
            {
                state.WeekNumber = week;
                aging.ProcessWeeklyAging(state);
            }

            Assert.Equal(before, (veteran.STR, veteran.AGI, veteran.VIT, veteran.DEX, veteran.MND, veteran.LDR, veteran.INT));
        }
    }
}
