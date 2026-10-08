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
    /// <summary>
    /// 2026年10月・§0.64のテスト：依頼（撃破・完全解析・納品・献上、依頼人の固有武具）と迷宮の異変。
    /// 実行方法: `dotnet test GuildManager.Core.Tests --filter FullyQualifiedName~Commission`
    /// </summary>
    public class CommissionTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private static Adventurer Make(string name, int stat)
        {
            var a = new Adventurer
            {
                Name = name, JobClass = JobClass.Warrior,
                STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat,
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        /// <summary>森だけ開いた既定の5フィールドと、3人の冒険者。</summary>
        private static GameState NewState(int week = 13)
        {
            var fields = SampleData.CreateDefaultFields();
            return new GameState
            {
                WeekNumber = week,
                Gold = 1000,
                MasterMood = 50,
                DungeonFields = fields,
                Adventurers = { Make("アリス", 30), Make("セリア", 40), Make("ミナ", 50) },
                CommissionsFromWeek = 13, // 依頼は開いている（初めての入賞のあと、§0.84）
            };
        }

        private static FloorBoss ForestBoss(GameState state) => state.DungeonFields.First(f => f.Id == "forest").GetNextActiveBoss()!;

        private static GuildCommission Accepted(GameState state, CommissionType type, FloorBoss? boss = null, int deadlineWeek = 30)
        {
            var c = new GuildCommission
            {
                ClientId = type switch
                {
                    CommissionType.Defeat => "Knights",
                    CommissionType.Survey => "Academy",
                    CommissionType.Deliver => "Merchants",
                    _ => "Noble",
                },
                Type = type,
                FieldId = "forest",
                BossId = boss?.Id,
                OfferedWeek = state.WeekNumber,
                DeadlineWeek = deadlineWeek,
                RewardGold = 500,
                RewardRelicFloor = 10,
                Accepted = true,
            };
            state.Commissions.Add(c);
            return c;
        }

        // ---------------- CSV ----------------

        [Fact]
        public void CsvValues_AreLoaded()
        {
            Assert.Equal(13, CommissionBalance.FirstAnomalyWeek);
            Assert.Equal(3, CommissionBalance.OffersPerSeason);
            Assert.Equal(2, CommissionBalance.MaxAccepted);
            Assert.Equal(24, CommissionBalance.GetDeadlineWeeks(CommissionType.Defeat));
            Assert.Equal(3.0, CommissionBalance.GetRewardMultiplier(CommissionType.Tribute), precision: 6);
            Assert.Equal(ItemRarity.Epic, CommissionBalance.RewardRelicMinRarity);
            Assert.Equal(5, CommissionBalance.PatronUniqueCompletions);
            Assert.Equal(5, CommissionBalance.Clients.Count);
        }

        [Fact]
        public void EveryClient_HasTextsForItsTypes_AndAPatronUnique()
        {
            foreach (var client in CommissionBalance.Clients)
            {
                foreach (var type in client.Types)
                    Assert.NotEmpty(CommissionBalance.GetTexts(client.Id, type));
                var unique = UniqueBalance.FindPatronReward(client.Id);
                Assert.NotNull(unique);
                Assert.Equal(UniqueGrade.Patron, unique!.Grade);
                Assert.Equal(0, unique.SellPrice);
            }
            Assert.Contains(CommissionBalance.Clients, c => c.Types.Count == 4); // エルフの里は何でも頼む
            foreach (var type in Enum.GetValues<DungeonAnomalyType>())
                Assert.False(string.IsNullOrEmpty(CommissionBalance.GetAnomaly(type).Name));
        }

        [Fact]
        public void PatronUnique_CannotBeSold()
        {
            var item = EquipmentItem.FromUnique(UniqueBalance.FindPatronReward("Knights")!);
            Assert.False(EquipmentSystem.CanSell(item));
        }

        [Fact]
        public void UniqueParse_RejectsPatronWithoutClient_AndPatronIdOnOtherGrades()
        {
            string[] header =
            {
                "Id", "Grade", "Name", "BaseItemId", "HpBonus",
                "BonusStr", "BonusVit", "BonusAgi", "BonusDex", "BonusInt", "BonusMnd", "BonusLdr",
                "CounterGimmick", "DropFieldId", "DropFloor", "SellPrice", "PatronId",
            };
            string[] Row(string grade, string patron, string sell = "0") =>
                new[] { "X", grade, "試し", "IronSword", "0", "1", "0", "0", "0", "0", "0", "0", "", "", "0", sell, patron };

            Assert.Single(UniqueBalance.Parse(header, new[] { Row("Patron", "Knights") }));
            Assert.Throws<BalanceDataException>(() => UniqueBalance.Parse(header, new[] { Row("Patron", "") }));
            Assert.Throws<BalanceDataException>(() => UniqueBalance.Parse(header, new[] { Row("Patron", "Knights", "100") }));
            Assert.Throws<BalanceDataException>(() => UniqueBalance.Parse(header, new[] { Row("Artifact", "Knights", "100") }));
        }

        // ---------------- 届き方 ----------------

        [Theory]
        [InlineData(1, false)]
        [InlineData(12, false)]
        [InlineData(13, true)]
        [InlineData(14, false)]
        [InlineData(25, true)]
        [InlineData(49, true)]
        public void OfferWeek_IsFirstWeekOfSeason_FromCommissionsFromWeek(int week, bool expected) =>
            Assert.Equal(expected, CommissionSystem.IsOfferWeek(new GameState { CommissionsFromWeek = 13 }, week));

        [Fact]
        public void OfferWeek_NeverBeforeCommissionsOpen()
        {
            var state = new GameState();
            Assert.False(CommissionSystem.IsOfferWeek(state, 13));
            Assert.False(CommissionSystem.IsOfferWeek(state, 49));
        }

        [Fact]
        public void NewSeason_OffersThreeCommissions_FromDifferentClients()
        {
            var state = NewState(13);
            var arrivals = new CommissionSystem(new SeededRng(7)).ProcessNewWeek(state);

            Assert.Equal(3, arrivals.Offered.Count);
            Assert.Equal(3, arrivals.Offered.Select(c => c.ClientId).Distinct().Count());
            Assert.All(arrivals.Offered, c =>
            {
                Assert.False(c.Accepted);
                Assert.Equal(13 + CommissionBalance.GetDeadlineWeeks(c.Type) - 1, c.DeadlineWeek);
                Assert.True(c.RewardGold > 0);
                Assert.False(string.IsNullOrEmpty(c.Text));
                Assert.DoesNotContain("{", c.Text);
            });
            Assert.Equal(arrivals.Offered, state.Commissions);
        }

        [Fact]
        public void NoOffers_BeforeFirstOfferWeek_OrMidSeason()
        {
            Assert.Empty(new CommissionSystem(new SeededRng(1)).ProcessNewWeek(NewState(1)).Offered);
            Assert.Empty(new CommissionSystem(new SeededRng(1)).ProcessNewWeek(NewState(14)).Offered);
        }

        [Fact]
        public void NewSeason_DropsUnacceptedOffers_KeepsAccepted()
        {
            var state = NewState(25);
            var kept = Accepted(state, CommissionType.Defeat, ForestBoss(state), deadlineWeek: 36);
            state.Commissions.Add(new GuildCommission { ClientId = "Merchants", Type = CommissionType.Deliver, DeadlineWeek = 30 });

            new CommissionSystem(new SeededRng(3)).ProcessNewWeek(state);

            Assert.Contains(kept, state.Commissions);
            Assert.Single(state.Commissions, c => c.Accepted);
            Assert.All(state.Commissions.Where(c => !c.Accepted), c => Assert.Equal(25, c.OfferedWeek));
        }

        [Fact]
        public void DefeatOffer_TargetsFrontierBoss_WithRewardFromBoss()
        {
            var state = NewState(13);
            var c = Enumerable.Range(0, 30)
                .Select(seed => { var s = NewState(13); return (s, new CommissionSystem(new SeededRng(seed)).GenerateOffers(s)); })
                .SelectMany(x => x.Item2.Select(o => (x.s, o)))
                .First(x => x.o.Type == CommissionType.Defeat);

            var boss = CommissionSystem.FindBoss(c.s, c.o)!;
            Assert.Equal(ForestBoss(c.s).Id, boss.Id); // 森だけ開いているので最前線は森の次のボス
            Assert.Equal((int)Math.Round(boss.RewardGold * CommissionBalance.RewardMultiplierDefeat), c.o.RewardGold);
            Assert.Contains(boss.Name, c.o.Text);
        }

        [Fact]
        public void TributeOffer_UsesThirdHighestStat()
        {
            var state = NewState(13);
            state.Adventurers.Add(Make("レナ", 60)); // 60・50・40・30 → 3番目は40
            var c = Enumerable.Range(0, 60)
                .Select(seed => { var s = NewState(13); s.Adventurers.Add(Make("レナ", 60)); return new CommissionSystem(new SeededRng(seed)).GenerateOffers(s); })
                .SelectMany(x => x)
                .First(x => x.Type == CommissionType.Tribute);

            Assert.Equal(40, c.StatThreshold);
            Assert.Contains(c.StatName!, new[] { "STR", "AGI", "VIT", "MND", "DEX", "LDR", "INT" });
        }

        [Fact]
        public void NoOffers_WhenEveryFieldIsCleared()
        {
            var state = NewState(13);
            foreach (var b in state.DungeonFields.SelectMany(f => f.Bosses)) b.IsDefeated = true;
            Assert.Empty(new CommissionSystem(new SeededRng(1)).GenerateOffers(state));
        }

        // ---------------- 受ける・断る ----------------

        [Fact]
        public void Accept_UpToTwo_Decline_OnlyOffered()
        {
            var state = NewState(13);
            var offers = new CommissionSystem(new SeededRng(5)).GenerateOffers(state);

            Assert.True(CommissionSystem.TryAccept(state, offers[0]));
            Assert.True(CommissionSystem.TryAccept(state, offers[1]));
            Assert.False(CommissionSystem.TryAccept(state, offers[2])); // 上限2件
            Assert.False(CommissionSystem.Decline(state, offers[0])); // 受けた依頼は断れない
            Assert.True(CommissionSystem.Decline(state, offers[2]));
            Assert.Equal(2, state.Commissions.Count);
        }

        // ---------------- 週次決算での判定 ----------------

        [Fact]
        public void DefeatCommission_Completes_WhenBossIsDefeated_WithRewards()
        {
            var state = NewState(20);
            var boss = ForestBoss(state);
            var c = Accepted(state, CommissionType.Defeat, boss);
            boss.IsDefeated = true;

            var report = new CommissionSystem(new AlwaysMinRng()).ProcessSettlement(state);

            var done = Assert.Single(report.Completed);
            Assert.Same(c, done.Commission);
            Assert.Empty(state.Commissions);
            Assert.Equal(1500, state.Gold);
            Assert.Equal(60, state.MasterMood);
            Assert.True(Assert.Single(state.UnidentifiedItems).Rarity >= ItemRarity.Epic);
            Assert.Equal(1, state.CommissionCompletions["Knights"]);
            Assert.Null(done.BonusMaterialId); // 騎士団はおまけなし
        }

        [Fact]
        public void SurveyCommission_Completes_OnFullIntel_FailsIfBossDefeatedFirst()
        {
            var state = NewState(20);
            var boss = ForestBoss(state);
            Accepted(state, CommissionType.Survey, boss);
            boss.IntelRate = 1.0;
            var done = Assert.Single(new CommissionSystem(new AlwaysMinRng()).ProcessSettlement(state).Completed);
            Assert.Equal("mat_ruins_rune", done.BonusMaterialId);
            Assert.Equal(3, state.Materials["mat_ruins_rune"]);

            var state2 = NewState(20);
            var boss2 = ForestBoss(state2);
            Accepted(state2, CommissionType.Survey, boss2);
            boss2.IsDefeated = true;
            var failed = Assert.Single(new CommissionSystem(new AlwaysMinRng()).ProcessSettlement(state2).Failed);
            Assert.Equal(-10, failed.MoodApplied);
            Assert.Equal(40, state2.MasterMood);
            Assert.Empty(state2.Commissions);
        }

        [Fact]
        public void AcceptedCommission_FailsAtDeadline_OfferedOneIsDroppedWithoutPenalty()
        {
            var state = NewState(30);
            Accepted(state, CommissionType.Defeat, ForestBoss(state), deadlineWeek: 30);
            state.Commissions.Add(new GuildCommission { ClientId = "Merchants", Type = CommissionType.Deliver, DeadlineWeek = 30, MaterialId = "mat_forest_herb", Count = 6 });

            var report = new CommissionSystem(new AlwaysMinRng()).ProcessSettlement(state);

            Assert.Equal("期限切れ", Assert.Single(report.Failed).Reason);
            Assert.Empty(state.Commissions);
            Assert.Equal(40, state.MasterMood); // 罰は受けた1件分だけ
        }

        [Fact]
        public void BeforeDeadline_NothingHappens()
        {
            var state = NewState(29);
            Accepted(state, CommissionType.Defeat, ForestBoss(state), deadlineWeek: 30);
            var report = new CommissionSystem(new AlwaysMinRng()).ProcessSettlement(state);
            Assert.Empty(report.Failed);
            Assert.Empty(report.Completed);
            Assert.Single(state.Commissions);
        }

        [Fact]
        public void DeadlineNear_IsReported_WhenTwoWeeksLeft()
        {
            var state = NewState(29);
            var c = Accepted(state, CommissionType.Defeat, ForestBoss(state), deadlineWeek: 30);
            var arrivals = new CommissionSystem(new AlwaysMinRng()).ProcessNewWeek(state);
            Assert.Same(c, Assert.Single(arrivals.DeadlineNear));

            state.WeekNumber = 28;
            Assert.Empty(new CommissionSystem(new AlwaysMinRng()).ProcessNewWeek(state).DeadlineNear);
        }

        // ---------------- 納品・献上 ----------------

        [Fact]
        public void Deliver_ConsumesMaterials_AndCompletes()
        {
            var state = NewState(20);
            var c = Accepted(state, CommissionType.Deliver);
            c.MaterialId = "mat_forest_herb";
            c.Count = 6;
            var system = new CommissionSystem(new AlwaysMinRng());

            state.Materials["mat_forest_herb"] = 5;
            Assert.Null(system.TryDeliver(state, c));

            state.Materials["mat_forest_herb"] = 8;
            var done = system.TryDeliver(state, c)!;
            Assert.Equal(2, state.Materials["mat_forest_herb"]);
            Assert.Equal("mat_canyon_gem", done.BonusMaterialId);
            Assert.Empty(state.Commissions);
        }

        [Fact]
        public void Tribute_RemovesAdventurer_ReturnsGear_AndCompletes()
        {
            var state = NewState(20);
            var c = Accepted(state, CommissionType.Tribute);
            c.StatName = "STR";
            c.StatThreshold = 40;
            var mina = state.Adventurers.First(a => a.Name == "ミナ");
            var celia = state.Adventurers.First(a => a.Name == "セリア");
            celia.IsDispatched = true;
            mina.EquippedWeapon = EquipmentItem.FromCatalog(ItemCatalog.FindById("IronSword")!);
            var saved = new SavedParty { Name = "第一部隊", MemberIds = { mina.Id, celia.Id } };
            state.SavedParties.Add(saved);
            state.TrainingAssignments[mina.Id] = FacilityType.DrillHall;

            var candidates = CommissionSystem.GetTributeCandidates(state, c);
            Assert.Equal(new[] { mina }, candidates); // セリアは出撃中、アリスは基準未満

            var system = new CommissionSystem(new AlwaysMinRng());
            Assert.Null(system.TryTribute(state, c, state.Adventurers.First(a => a.Name == "アリス")));
            var done = system.TryTribute(state, c, mina)!;

            Assert.Equal("ミナ", done.TributedAdventurerName);
            Assert.DoesNotContain(mina, state.Adventurers);
            Assert.DoesNotContain(mina.Id, saved.MemberIds);
            Assert.False(state.TrainingAssignments.ContainsKey(mina.Id));
            Assert.Single(state.Armory);
            Assert.Empty(state.Commissions);
        }

        [Fact]
        public void PatronUnique_ArrivesOnFifthCompletion_OnlyOnce()
        {
            var state = NewState(20);
            state.CommissionCompletions["Knights"] = 4;
            var boss = ForestBoss(state);
            Accepted(state, CommissionType.Defeat, boss);
            boss.IsDefeated = true;

            var done = Assert.Single(new CommissionSystem(new AlwaysMinRng()).ProcessSettlement(state).Completed);
            Assert.Equal("PatronKnightsBlade", done.PatronUnique!.UniqueId);
            Assert.Contains(state.Armory, i => i.UniqueId == "PatronKnightsBlade");

            var boss2 = state.DungeonFields.First(f => f.Id == "forest").GetNextActiveBoss()!;
            Accepted(state, CommissionType.Defeat, boss2);
            boss2.IsDefeated = true;
            var again = Assert.Single(new CommissionSystem(new AlwaysMinRng()).ProcessSettlement(state).Completed);
            Assert.Null(again.PatronUnique);
            Assert.Equal(6, state.CommissionCompletions["Knights"]);
        }

        // ---------------- 迷宮の異変 ----------------

        [Fact]
        public void Anomaly_IsAnnouncedOnWeekFour_ActiveForFourWeeks_ThenCleared()
        {
            var state = NewState(16);
            var system = new CommissionSystem(new SeededRng(2));
            var a = system.ProcessNewWeek(state).AnnouncedAnomaly!;

            Assert.Equal("forest", a.FieldId); // 開いているのは森だけ
            Assert.Equal(17, a.StartWeek);
            Assert.Equal(20, a.EndWeek);
            Assert.True(a.IsUpcoming(16));
            Assert.True(a.IsActive(17));
            Assert.True(a.IsActive(20));
            Assert.False(a.IsActive(21));
            Assert.DoesNotContain("{", DungeonAnomalySystem.Describe(state, a));

            state.WeekNumber = 21;
            system.ProcessNewWeek(state);
            Assert.Null(state.Anomaly);
        }

        private static GameState WithAnomaly(DungeonAnomalyType type, int week = 18)
        {
            var state = NewState(week);
            state.Anomaly = new DungeonAnomaly
            {
                Type = type, FieldId = "forest",
                BossId = type is DungeonAnomalyType.WeakenedLord or DungeonAnomalyType.EnragedLord ? ForestBoss(state).Id : null,
                AnnouncedWeek = 16, StartWeek = 17, EndWeek = 20,
            };
            return state;
        }

        [Fact]
        public void LordAnomalies_ChangeRequirement_AndEnragedDoublesReward()
        {
            var weak = WithAnomaly(DungeonAnomalyType.WeakenedLord);
            var boss = ForestBoss(weak);
            Assert.Equal(DungeonResolver.RequiredPower(boss) * 0.85, DungeonResolver.RequiredPower(boss, weak), precision: 6);

            var enraged = WithAnomaly(DungeonAnomalyType.EnragedLord);
            var eBoss = ForestBoss(enraged);
            Assert.Equal(DungeonResolver.RequiredPower(eBoss) * 1.2, DungeonResolver.RequiredPower(eBoss, enraged), precision: 6);
            int before = enraged.Gold;
            DungeonExpeditionSystem.ApplyFieldProgression(enraged, eBoss);
            Assert.Equal(before + eBoss.RewardGold * 2, enraged.Gold);

            // 予告中（まだ始まっていない）なら効かない
            var upcoming = WithAnomaly(DungeonAnomalyType.WeakenedLord, week: 16);
            Assert.Equal(DungeonResolver.RequiredPower(ForestBoss(upcoming)), DungeonResolver.RequiredPower(ForestBoss(upcoming), upcoming), precision: 6);
        }

        [Fact]
        public void Miasma_DoublesMaterials_AndIncreasesGatheringLoss()
        {
            var party = new Party();
            party.TryAdd(Make("アリス", 40));
            var normal = NewState(18);
            var r0 = new GatheringResolver(new AlwaysMinRng()).Resolve(party, normal.DungeonFields[0], normal);
            party.Members[0].CurrentHP = party.Members[0].MaxHP;

            var miasma = WithAnomaly(DungeonAnomalyType.Miasma);
            var r1 = new GatheringResolver(new AlwaysMinRng()).Resolve(party, miasma.DungeonFields[0], miasma);

            Assert.Equal(r0.MaterialCount * 2, r1.MaterialCount);
            Assert.True(r1.HpLostByAdventurer.Values.Single() > r0.HpLostByAdventurer.Values.Single());
        }

        [Fact]
        public void RelicVein_TriplesDropChance_ClearMist_DoublesIntel()
        {
            var party = new Party();
            party.TryAdd(Make("アリス", 40));
            var normal = NewState(18);
            int p0 = new GatheringResolver(new AlwaysMinRng()).Resolve(party, normal.DungeonFields[0], normal).RelicDropPercent;
            var vein = WithAnomaly(DungeonAnomalyType.RelicVein);
            int p1 = new GatheringResolver(new AlwaysMinRng()).Resolve(party, vein.DungeonFields[0], vein).RelicDropPercent;
            Assert.Equal(Math.Min(100, p0 * 3), p1);

            party.Members[0].CurrentHP = party.Members[0].MaxHP;
            var s0 = new ScoutingResolver(new AlwaysMinRng()).Resolve(party, ForestBoss(normal), normal);
            var mist = WithAnomaly(DungeonAnomalyType.ClearMist);
            var s1 = new ScoutingResolver(new AlwaysMinRng()).Resolve(party, ForestBoss(mist), mist);
            Assert.True(s0.IntelGained > 0);
            Assert.Equal(Math.Min(1.0, s0.IntelGained * 2), s1.IntelGained, precision: 6);
        }

        [Fact]
        public void Anomaly_OnOtherField_HasNoEffect()
        {
            var state = WithAnomaly(DungeonAnomalyType.Miasma);
            state.Anomaly!.FieldId = "cave";
            Assert.Equal(1.0, DungeonAnomalySystem.MaterialMultiplier(state, "forest"));
            Assert.Equal(2.0, DungeonAnomalySystem.MaterialMultiplier(state, "cave"));
            Assert.Equal(1.0, DungeonAnomalySystem.MaterialMultiplier(null, "cave"));
        }

        // ---------------- 週次決算・自動スキップ ----------------

        [Fact]
        public void WeekProcessing_StopsAutoSkip_WhenOffersArrive_AndLogsMood()
        {
            var state = NewState(12);
            var week = new WeekProcessingSystem(new MasterMoodSystem(), new EconomySystem(), new TrainingSystem(), new InjuryRecoverySystem(),
                new RestRecoverySystem(), new GrowthSystem(new AlwaysMinRng()), new SatisfactionSystem(), new AgingSystem(new AlwaysMinRng()),
                new FacilitySystem(), new DefeatSystem(), new RecruitmentSystem(new AlwaysMinRng()),
                commissionSystem: new CommissionSystem(new SeededRng(9)));

            var result = week.ProcessWeek(state);

            Assert.Equal(13, state.WeekNumber);
            Assert.True(result.Flags.CommissionsOffered);
            Assert.True(result.Flags.ShouldStopAutoSkip);
            Assert.Equal(3, result.Arrivals.Offered.Count);

            // 受けた撃破依頼が決算で達成されると、機嫌の内訳に載る
            var boss = ForestBoss(state);
            Accepted(state, CommissionType.Defeat, boss);
            boss.IsDefeated = true;
            var next = week.ProcessWeek(state);
            Assert.Single(next.Commissions.Completed);
            Assert.Contains(next.MoodReport.Entries, e => e.Reason.StartsWith("依頼の達成"));
        }

        [Fact]
        public void NewStopConditions()
        {
            Assert.True(new WeekResult { CommissionsOffered = true }.ShouldStopAutoSkip);
            Assert.True(new WeekResult { AnomalyAnnounced = true }.ShouldStopAutoSkip);
            Assert.True(new WeekResult { CommissionDeadlineNear = true }.ShouldStopAutoSkip);
        }

        // ---------------- セーブ ----------------

        [Fact]
        public void SaveRoundTrip_PreservesCommissionsAndAnomaly()
        {
            var state = WithAnomaly(DungeonAnomalyType.EnragedLord);
            var c = Accepted(state, CommissionType.Survey, ForestBoss(state));
            c.Text = "試しの依頼";
            state.CommissionCompletions["Academy"] = 3;

            string json = JsonSerializer.Serialize(state.ToSaveData());
            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!);

            var rc = Assert.Single(restored.Commissions);
            Assert.Equal(c.Id, rc.Id);
            Assert.Equal(CommissionType.Survey, rc.Type);
            Assert.Equal(c.BossId, rc.BossId);
            Assert.True(rc.Accepted);
            Assert.Equal("試しの依頼", rc.Text);
            Assert.NotNull(CommissionSystem.FindBoss(restored, rc));
            Assert.Equal(3, restored.CommissionCompletions["Academy"]);
            Assert.Equal(DungeonAnomalyType.EnragedLord, restored.Anomaly!.Type);
            Assert.Equal(state.Anomaly!.BossId, restored.Anomaly.BossId);
            Assert.Equal(20, restored.Anomaly.EndWeek);
        }

        [Fact]
        public void OldSave_WithoutCommissionKeys_LoadsEmpty()
        {
            var state = NewState(20);
            string json = JsonSerializer.Serialize(state.ToSaveData());
            string oldJson = System.Text.RegularExpressions.Regex.Replace(json, ",\"(Commissions|CommissionCompletions|CommissionsFromWeek|Anomaly)\":(\\[\\]|\\{\\}|null|\\d+)", "");
            Assert.DoesNotContain("Commission", oldJson);
            Assert.DoesNotContain("Anomaly", oldJson);

            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(oldJson)!);
            Assert.Empty(restored.Commissions);
            Assert.Empty(restored.CommissionCompletions);
            Assert.Null(restored.Anomaly);

            // null が書かれていても空で始める
            var nulls = GameState.FromSaveData(new SaveData { Commissions = null, CommissionCompletions = null });
            Assert.Empty(nulls.Commissions);
            Assert.Empty(nulls.CommissionCompletions);
        }
    }
}
