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
    /// <summary>作業を減らす（§0.99）：まとめて鑑定・おすすめを着せる・まとめて売る・おすすめの申し込み・交渉の自動・今月の遺物のテスト。</summary>
    public class ChoreTests
    {
        private static Adventurer Make(string name, JobClass job = JobClass.Warrior, int stat = 40, int wage = 100, int joinedYear = 0)
        {
            var a = new Adventurer
            {
                Name = name, JobClass = job, WeeklyWage = wage, JoinedYear = joinedYear,
                STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat,
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static UnidentifiedItem Relic(ItemRarity rarity, int floor = 5) => new()
        {
            Name = $"遺物{rarity}{floor}", Rarity = rarity, OriginFieldId = "forest", OriginFloor = floor,
            AppraisalCost = RelicBalance.GetAppraisalCost(rarity),
        };

        private static EquipmentItem Gear(string itemId) => EquipmentItem.FromCatalog(ItemCatalog.FindById(itemId)!);

        /// <summary>戦士にとっての値打ちの順に並べた、戦士が使える武器（弱い→強い）。</summary>
        private static List<string> WarriorWeaponsByScore()
        {
            var probe = Make("見本");
            return ItemCatalog.GetAll().Where(i => i.Slot == EquipmentSlot.Weapon && i.IsAllowedFor(JobClass.Warrior))
                .Select(i => i.Id).OrderBy(id => ChoreSystem.GearScore(probe, Gear(id))).ToList();
        }

        // ---------------- まとめて鑑定 ----------------

        [Fact]
        public void Reserve_IsWagesTimesWeeks_WithoutLoans()
        {
            var a = Make("A", wage: 100);
            var b = Make("B", wage: 50);
            var c = Make("C", wage: 999);
            c.LoanUntilWeek = 99; // 派遣中の週給は先方持ち
            var state = new GameState { WeekNumber = 1 };
            state.Adventurers.AddRange(new[] { a, b, c });

            Assert.True(c.IsOnLoan);
            Assert.Equal(150 * EconomyBalance.ChoreReserveWageWeeks, ChoreSystem.Reserve(state));
        }

        [Fact]
        public void PlanAppraisal_PicksCopperAndSilverWithinBudget_LeavesGoldAndRainbow()
        {
            var state = new GameState();
            state.Adventurers.Add(Make("A", wage: 100));
            int reserve = ChoreSystem.Reserve(state);
            var silver = Relic(ItemRarity.Rare);
            var copper1 = Relic(ItemRarity.Common, 3);
            var copper2 = Relic(ItemRarity.Common, 2);
            var gold = Relic(ItemRarity.Epic);
            state.UnidentifiedItems.AddRange(new[] { copper2, gold, copper1, silver });
            // 銀と銅1つ分だけ払える
            state.Gold = reserve + silver.AppraisalCost + copper1.AppraisalCost;

            var plan = ChoreSystem.PlanAppraisal(state);

            Assert.Equal(new[] { silver, copper1 }, plan.Items); // 銀→銅、深い順
            Assert.Equal(silver.AppraisalCost + copper1.AppraisalCost, plan.TotalCost);
            Assert.Equal(1, plan.OverBudget);
            Assert.Equal(1, plan.OneByOne);
            Assert.Equal(reserve, plan.Reserve);
        }

        [Fact]
        public void PlanAppraisal_SkipsExpensiveAndKeepsLookingForCheaper()
        {
            var state = new GameState();
            var silver = Relic(ItemRarity.Rare);
            var copper = Relic(ItemRarity.Common);
            state.UnidentifiedItems.AddRange(new[] { silver, copper });
            state.Gold = copper.AppraisalCost; // 週給0なので残す額も0

            var plan = ChoreSystem.PlanAppraisal(state);

            Assert.Equal(new[] { copper }, plan.Items);
            Assert.Equal(1, plan.OverBudget);
        }

        [Fact]
        public void PlanAppraisal_WithIds_OnlyLooksAtThoseRelics()
        {
            var state = new GameState { Gold = 100000 };
            var mine = Relic(ItemRarity.Common);
            var other = Relic(ItemRarity.Common);
            state.UnidentifiedItems.AddRange(new[] { mine, other });

            var plan = ChoreSystem.PlanAppraisal(state, new[] { mine.Id, "消えた遺物" });

            Assert.Equal(new[] { mine }, plan.Items);
        }

        [Fact]
        public void AppraiseBulk_AppraisesThePlannedRelics_AndPaysTheirCost()
        {
            var state = new GameState { Gold = 100000, DungeonFields = SampleData.CreateDefaultFields() };
            var a = Relic(ItemRarity.Common);
            var b = Relic(ItemRarity.Rare);
            var gold = Relic(ItemRarity.Legendary);
            state.UnidentifiedItems.AddRange(new[] { a, b, gold });

            var results = ChoreSystem.AppraiseBulk(state, new AppraisalSystem(new SeededRng(7)));

            Assert.Equal(2, results.Count);
            Assert.Equal(a.AppraisalCost + b.AppraisalCost, results.Sum(r => r.AppraisalCost));
            Assert.Equal(new[] { gold }, state.UnidentifiedItems);
        }

        // ---------------- おすすめを着せる ----------------

        [Fact]
        public void PlanGear_GivesTheStrongestWeaponToAMember_AndApplyMovesTheOldOneToTheArmory()
        {
            var weapons = WarriorWeaponsByScore();
            var a = Make("A");
            var state = new GameState();
            state.Adventurers.Add(a);
            var equipment = new EquipmentSystem();
            var weak = Gear(weapons[0]);
            var strong = Gear(weapons[^1]);
            state.Armory.AddRange(new[] { weak, strong });
            Assert.True(equipment.TryEquip(state, a, EquipmentSlot.Weapon, weak));
            var party = new SavedParty { Name = "主力", MemberIds = { a.Id } };

            var plan = ChoreSystem.PlanGear(state, party);

            var change = Assert.Single(plan, c => c.Slot == EquipmentSlot.Weapon);
            Assert.Same(weak, change.Before);
            Assert.Same(strong, change.After);
            var summary = Assert.Single(ChoreSystem.SummarizeGear(plan));
            Assert.Same(a, summary.Adventurer);

            ChoreSystem.ApplyGear(state, party);

            Assert.Same(strong, a.EquippedWeapon);
            Assert.Contains(weak, state.Armory);
            Assert.DoesNotContain(strong, state.Armory);
            Assert.Empty(ChoreSystem.PlanGear(state, party)); // もう良くならない
        }

        [Fact]
        public void PlanGear_PassesTheReplacedWeaponToAnotherMember()
        {
            var weapons = WarriorWeaponsByScore();
            var a = Make("A");
            var b = Make("B");
            var state = new GameState();
            state.Adventurers.AddRange(new[] { a, b });
            var middle = Gear(weapons[0]);
            var strong = Gear(weapons[^1]);
            state.Armory.AddRange(new[] { middle, strong });
            new EquipmentSystem().TryEquip(state, a, EquipmentSlot.Weapon, middle); // A は弱い方を持ち、B は素手
            var party = new SavedParty { Name = "主力", MemberIds = { a.Id, b.Id } };

            ChoreSystem.ApplyGear(state, party);

            // どちらも武器を持つ（強い方と、外れた方が1本ずつ）
            Assert.NotNull(a.EquippedWeapon);
            Assert.NotNull(b.EquippedWeapon);
            Assert.Equal(new[] { middle, strong }.OrderBy(e => e.Id), new[] { a.EquippedWeapon!, b.EquippedWeapon! }.OrderBy(e => e.Id));
            Assert.Empty(state.Armory);
        }

        [Fact]
        public void PlanGear_SkipsDispatchedMembers_AndOtherSquads()
        {
            var weapons = WarriorWeaponsByScore();
            var away = Make("出撃中");
            away.IsDispatched = true;
            var outsider = Make("ほかの部隊");
            var state = new GameState();
            state.Adventurers.AddRange(new[] { away, outsider });
            state.Armory.Add(Gear(weapons[^1]));
            var party = new SavedParty { Name = "主力", MemberIds = { away.Id } };

            Assert.Empty(ChoreSystem.PlanGear(state, party));
        }

        [Fact]
        public void PlanGear_RespectsJobRestrictions()
        {
            var mage = Make("魔導士", JobClass.Mage);
            var state = new GameState();
            state.Adventurers.Add(mage);
            var notForMage = ItemCatalog.GetAll().First(i => i.Slot == EquipmentSlot.Weapon && !i.IsAllowedFor(JobClass.Mage));
            state.Armory.Add(Gear(notForMage.Id));

            Assert.Empty(ChoreSystem.PlanGear(state, new SavedParty { MemberIds = { mage.Id } }));
        }

        // ---------------- まとめて売る ----------------

        [Fact]
        public void UnneededArmory_SellsOnlyWhatNobodyWouldWear()
        {
            var weapons = WarriorWeaponsByScore();
            var a = Make("A");
            var state = new GameState();
            state.Adventurers.Add(a);
            var strong = Gear(weapons[^1]);
            state.Armory.Add(strong);
            new EquipmentSystem().TryEquip(state, a, EquipmentSlot.Weapon, strong);
            var weak = Gear(weapons[0]);
            var armor = Gear(ItemCatalog.GetAll().First(i => i.Slot == EquipmentSlot.Armor && i.IsAllowedFor(JobClass.Warrior)).Id); // A は防具が空
            var gold = EquipmentItem.FromCatalog(ItemCatalog.FindById(weapons[0])!, rarity: ItemRarity.Epic); // 金は1つずつ売る
            state.Armory.AddRange(new[] { weak, armor, gold });

            var unneeded = ChoreSystem.UnneededArmory(state);

            Assert.Equal(new[] { weak }, unneeded);
        }

        [Fact]
        public void MaterialsToKeep_CountsUnfinishedResearch_AndAcceptedCommissions()
        {
            var state = new GameState();
            var research = ResearchBalance.GetAll().First(r => r.RequiredMaterials.Count > 0);
            var (materialId, need) = research.RequiredMaterials.First();
            state.Commissions.Add(new GuildCommission { Accepted = true, MaterialId = materialId, Count = 2 });
            state.Commissions.Add(new GuildCommission { Accepted = false, MaterialId = materialId, Count = 50 }); // 受けていない依頼は数えない

            var keep = ChoreSystem.MaterialsToKeep(state);

            int fromResearch = ResearchBalance.GetAll().Where(r => !state.IsResearchCompleted(r.Id)).Sum(r => r.RequiredMaterials.GetValueOrDefault(materialId));
            Assert.True(fromResearch >= need);
            Assert.Equal(fromResearch + 2, keep[materialId]);
        }

        [Fact]
        public void PlanSale_SellsOnlyTheSurplus_AndExecuteSaleAddsTheGold()
        {
            var state = new GameState { Gold = 0 };
            var keep = ChoreSystem.MaterialsToKeep(state);
            var used = MaterialBalance.GetAll().First(m => keep.GetValueOrDefault(m.Id) > 0);
            var unused = MaterialBalance.GetAll().FirstOrDefault(m => keep.GetValueOrDefault(m.Id) == 0);
            state.AddMaterial(used.Id, keep[used.Id] + 3);
            if (unused != null) state.AddMaterial(unused.Id, 4);

            var plan = ChoreSystem.PlanSale(state);

            Assert.Equal(3, plan.Materials[used.Id]);
            if (unused != null) Assert.Equal(4, plan.Materials[unused.Id]);
            var sold = ChoreSystem.ExecuteSale(state, new EconomySystem());
            Assert.Equal(plan.TotalGold, sold.TotalGold);
            Assert.Equal(plan.TotalGold, state.Gold);
            Assert.Equal(keep[used.Id], state.Materials[used.Id]);
            Assert.True(ChoreSystem.PlanSale(state).IsEmpty);
        }

        // ---------------- おすすめの子で申し込む ----------------

        /// <summary>1年目 春1の月 第1週（新人戦の月のはじめ）。</summary>
        private static GameState TournamentGame(params Adventurer[] adventurers)
        {
            var state = new GameState { WeekNumber = 1, Gold = 1000, DungeonFields = SampleData.CreateDefaultFields(), TournamentCalendarFromWeek = 1 };
            state.Adventurers.AddRange(adventurers);
            TournamentSystem.EnsureSchedule(state);
            return state;
        }

        [Fact]
        public void RecommendEntries_PicksEligibleRookiesByStrength_AndSkipsTheDiveSquad()
        {
            var strong = Make("強い新人", stat: 60, joinedYear: 1);
            var weak = Make("弱い新人", stat: 30, joinedYear: 1);
            var diver = Make("潜行の新人", stat: 90, joinedYear: 1);
            var veteran = Make("古株", stat: 90, joinedYear: 0); // 新人戦の資格が無い
            var state = TournamentGame(strong, weak, diver, veteran);
            state.SavedParties.Add(new SavedParty { Name = "主力", Order = SquadOrder.Dive, MemberIds = { diver.Id } });

            var picks = ChoreSystem.RecommendEntries(state);

            var rookie = state.TournamentEvents.Single(e => e.Kind == TournamentKind.Rookie && e.Year == 1);
            Assert.All(picks, p => Assert.Same(rookie, p.Event));
            Assert.Equal(new[] { strong, weak }.Take(TournamentBalance.EntrantsPerGuild), picks.Select(p => p.Adventurer));
            Assert.All(picks, p => Assert.Equal(TournamentPrep.Push, p.Prep)); // HP満タンなら追い込み

            var applied = ChoreSystem.ApplyEntries(state);
            Assert.Equal(picks.Count, applied.Count);
            Assert.True(TournamentSystem.IsEntered(state, strong.Id));
            Assert.False(TournamentSystem.IsEntered(state, diver.Id));
            Assert.DoesNotContain(ChoreSystem.RecommendEntries(state), p => p.Adventurer == strong); // もう出る子は選ばない
        }

        [Fact]
        public void RecommendEntries_LowHpRests_AndTooTiredIsSkipped()
        {
            var tired = Make("疲れ気味", stat: 60, joinedYear: 1);
            tired.CurrentHP = (int)Math.Ceiling(tired.MaxHP * (TournamentBalance.RecommendMinHpRatio + TournamentBalance.RecommendPushHpRatio) / 2);
            var exhausted = Make("へとへと", stat: 90, joinedYear: 1);
            exhausted.CurrentHP = (int)(exhausted.MaxHP * TournamentBalance.RecommendMinHpRatio) - 1;
            var state = TournamentGame(tired, exhausted);

            var pick = Assert.Single(ChoreSystem.RecommendEntries(state));

            Assert.Same(tired, pick.Adventurer);
            Assert.Equal(TournamentPrep.Rest, pick.Prep);
        }

        [Fact]
        public void RecommendEntries_IsEmptyOutsideTheFirstWeekOfTheMonth()
        {
            var state = TournamentGame(Make("新人", stat: 60, joinedYear: 1));
            state.WeekNumber = 2;

            Assert.Empty(ChoreSystem.RecommendEntries(state));
        }

        // ---------------- 交渉の自動 ----------------

        [Fact]
        public void ProcessAutoBonus_DoesNothingUnlessTurnedOn()
        {
            var a = Make("不満", wage: 10);
            a.NeedsNegotiation = true;
            var state = new GameState { Gold = 100000 };
            state.Adventurers.Add(a);

            Assert.Empty(ChoreSystem.ProcessAutoBonus(state, new SatisfactionSystem()));
            Assert.True(a.NeedsNegotiation);
        }

        [Fact]
        public void ProcessAutoBonus_PaysWithinTheReserve_AndLeavesTheRestToThePlayer()
        {
            var rich = Make("先に払える", wage: 10);
            var costly = Make("高い", wage: 1000);
            rich.NeedsNegotiation = true;
            costly.NeedsNegotiation = true;
            var state = new GameState { AutoNegotiationBonus = true };
            state.Adventurers.AddRange(new[] { rich, costly });
            int bonus = 10 * SatisfactionBalance.BonusWeeksEquivalent;
            state.Gold = ChoreSystem.Reserve(state) + bonus;

            var paid = ChoreSystem.ProcessAutoBonus(state, new SatisfactionSystem());

            var only = Assert.Single(paid);
            Assert.Same(rich, only.Adventurer);
            Assert.Equal(bonus, only.Gold);
            Assert.False(rich.NeedsNegotiation);
            Assert.True(costly.NeedsNegotiation);
            Assert.Equal(ChoreSystem.Reserve(state), state.Gold);
        }

        [Fact]
        public void WeekProcessing_AutoBonusAnswersANewComplaint_WithoutStoppingTheMonth()
        {
            var a = Make("不満", wage: 10);
            a.Satisfaction = SatisfactionBalance.NegotiationThreshold - 15;
            var state = new GameState { WeekNumber = 2, Gold = 100000, AutoNegotiationBonus = true, DungeonFields = SampleData.CreateDefaultFields() };
            state.Adventurers.Add(a);

            var result = new WeekProcessingSystem(new MasterMoodSystem(), new EconomySystem(), new TrainingSystem(), new InjuryRecoverySystem(),
                new RestRecoverySystem(), new GrowthSystem(new SeededRng(1)), new SatisfactionSystem(), new AgingSystem(new SeededRng(2)),
                new FacilitySystem(), new DefeatSystem(), new RecruitmentSystem(new SeededRng(3))).ProcessWeek(state);

            Assert.Single(result.AutoBonuses);
            Assert.False(a.NeedsNegotiation);
            Assert.False(result.Flags.SatisfactionWarningOccurred);
        }

        [Fact]
        public void AutoNegotiationBonus_RoundTrips_AndOldSavesReadOff()
        {
            var state = new GameState { AutoNegotiationBonus = true };
            var json = JsonSerializer.Serialize(state.ToSaveData());
            Assert.True(GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!).AutoNegotiationBonus);

            var oldJson = json.Replace("\"AutoNegotiationBonus\":true,", "").Replace(",\"AutoNegotiationBonus\":true", "");
            Assert.DoesNotContain("AutoNegotiationBonus", oldJson);
            Assert.False(GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(oldJson)!).AutoNegotiationBonus);
        }

        // ---------------- 今月の遺物 ----------------

        [Fact]
        public void MonthlyReport_RecordsTheRelicsFoundThisMonth()
        {
            var state = new GameState { WeekNumber = 5, DungeonFields = SampleData.CreateDefaultFields() };
            var field = state.DungeonFields[0];
            var found = Relic(ItemRarity.Common);
            var old = Relic(ItemRarity.Rare);
            state.UnidentifiedItems.AddRange(new[] { old, found });
            var settlement = new WeeklySettlementResult();
            settlement.Flags.Week = 4;
            var resolution = new DungeonMissionResolution(new Party(), null, field, DungeonMissionType.Survey);
            resolution.RelicsFound.Add(found);
            settlement.DungeonMissionResolutions.Add(resolution);

            var report = MonthlyReport.Build(state, new List<AutoSkipWeek> { new(new List<SquadOrderEvent>(), settlement) }, state.Gold);

            Assert.Equal(new[] { found.Id }, report.RelicIds);
        }

        [Fact]
        public void MonthlyReport_OldSaveWithoutRelicIds_ReadsEmpty()
        {
            var state = new GameState();
            state.MonthlyReports.Add(new MonthlyReport { FirstWeek = 1, LastWeek = 4, MonthCompleted = true });
            var json = JsonSerializer.Serialize(state.ToSaveData()).Replace("\"RelicIds\":[],", "");
            Assert.DoesNotContain("RelicIds", json);

            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!);

            Assert.Empty(Assert.Single(restored.MonthlyReports).RelicIds);
        }
    }
}
