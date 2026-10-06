using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 2026年10月・§0.58のテスト：魂魄融和の秘薬（相性100のペアから娘を培養する。→ 03 §5.4）。
    /// 処方の条件・費用、子の能力（PA）と能力限界突破、特性の継承、百合相性、培養と誕生、セーブ互換。
    /// 実行方法: `dotnet test GuildManager.Core.Tests --filter FullyQualifiedName~SoulFusion`
    /// </summary>
    public class SoulFusionTests
    {
        /// <summary>決まった順に値を返す乱数（尽きたら最後の値を繰り返す）。範囲外は [min, max] に収める。</summary>
        private class SequenceRng : IRng
        {
            private readonly Queue<int> _values;
            private int _last;
            public SequenceRng(params int[] values) { _values = new Queue<int>(values); _last = values.LastOrDefault(); }
            public int NextInt(int min, int max)
            {
                if (_values.Count > 0) _last = _values.Dequeue();
                return Math.Clamp(_last, min, max);
            }
        }

        private class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        private static readonly string[] AllStats = { "STR", "AGI", "VIT", "MND", "DEX", "LDR", "INT" };

        private static Adventurer Make(string name, JobClass job, int pa = 80, params string[] traits)
        {
            var a = new Adventurer
            {
                Name = name, JobClass = job,
                PA_STR = pa, PA_AGI = pa, PA_VIT = pa, PA_MND = pa, PA_DEX = pa, PA_LDR = pa, PA_INT = pa,
                STR = 30, AGI = 30, VIT = 30, MND = 30, DEX = 30, LDR = 30, INT = 30,
            };
            foreach (var id in traits) Assert.True(a.TryAddTrait(id), id);
            return a;
        }

        /// <summary>研究済み・所持金十分・2人が相性100の状態。</summary>
        private static (GameState State, Adventurer A, Adventurer B) Setup(JobClass jobA = JobClass.Warrior, JobClass jobB = JobClass.Cleric)
        {
            var a = Make("アリス", jobA);
            var b = Make("セリア", jobB);
            var state = new GameState { Gold = 10000, Adventurers = new List<Adventurer> { a, b } };
            state.CompletedResearchIds.Add(ResearchIds.SoulFusion);
            state.Compatibility[CompatibilitySystem.NormalizeKey(a.Id, b.Id)] = 100;
            return (state, a, b);
        }

        // ---------------- バランス値 ----------------

        [Fact]
        public void CsvValues_AreLoaded()
        {
            Assert.Equal(100, SoulFusionBalance.RequiredCompatibility);
            Assert.Equal(2500, SoulFusionBalance.PrescriptionGold);
            Assert.Equal(12, SoulFusionBalance.CultureWeeks);
            Assert.Equal(1, SoulFusionBalance.CultureTankCount);
            Assert.Equal(-5, SoulFusionBalance.PaVarianceMin);
            Assert.Equal(10, SoulFusionBalance.PaVarianceMax);
            Assert.Equal(30, SoulFusionBalance.BreakthroughChanceDestined);
            Assert.Equal(15, SoulFusionBalance.BreakthroughChanceComplementary);
            Assert.Equal(5, SoulFusionBalance.BreakthroughChanceOrdinary);
            Assert.Equal(120, SoulFusionBalance.BreakthroughPaCap);
            Assert.Equal(5, SoulFusionBalance.Catalysts.Count);
            Assert.All(SoulFusionBalance.Catalysts, c => Assert.NotEqual(c.MaterialId, MaterialBalance.GetName(c.MaterialId)));

            var research = ResearchBalance.Find(ResearchIds.SoulFusion)!;
            Assert.Equal(ResearchEffectType.SoulFusionUnlock, research.EffectType);
            Assert.Equal("魂魄融和の秘薬", research.Name);
        }

        // ---------------- 百合相性（ニックス） ----------------

        [Theory]
        [InlineData(JobClass.Warrior, JobClass.Cleric, SoulFusionNyxTier.Destined)]
        [InlineData(JobClass.Cleric, JobClass.Knight, SoulFusionNyxTier.Destined)]
        [InlineData(JobClass.Ranger, JobClass.Mage, SoulFusionNyxTier.Complementary)]
        [InlineData(JobClass.Scholar, JobClass.Thief, SoulFusionNyxTier.Complementary)]
        [InlineData(JobClass.Warrior, JobClass.Knight, SoulFusionNyxTier.Ordinary)]
        [InlineData(JobClass.Cleric, JobClass.Mage, SoulFusionNyxTier.Ordinary)]
        [InlineData(JobClass.Cleric, JobClass.Cleric, SoulFusionNyxTier.Ordinary)]
        public void NyxTier_ByJobPair(JobClass a, JobClass b, SoulFusionNyxTier expected)
        {
            Assert.Equal(expected, SoulFusionSystem.GetNyxTier(a, b));
        }

        [Fact]
        public void BreakthroughChance_ByTier_PlusCanyonGemCatalyst()
        {
            Assert.Equal(30, SoulFusionSystem.GetBreakthroughChance(SoulFusionNyxTier.Destined, null));
            Assert.Equal(15, SoulFusionSystem.GetBreakthroughChance(SoulFusionNyxTier.Complementary, null));
            Assert.Equal(5, SoulFusionSystem.GetBreakthroughChance(SoulFusionNyxTier.Ordinary, null));
            Assert.Equal(45, SoulFusionSystem.GetBreakthroughChance(SoulFusionNyxTier.Destined, "mat_canyon_gem"));
            Assert.Equal(5, SoulFusionSystem.GetBreakthroughChance(SoulFusionNyxTier.Ordinary, "mat_forest_herb")); // 別の効果の触媒は効かない
        }

        // ---------------- 処方の条件 ----------------

        [Fact]
        public void Check_Ok_WhenAllConditionsMet()
        {
            var (state, a, b) = Setup();
            Assert.Equal(SoulFusionCheck.Ok, SoulFusionSystem.CheckPrescription(state, a, b, JobClass.Cleric, null));
        }

        [Fact]
        public void Check_NotUnlocked_WithoutResearch()
        {
            var (state, a, b) = Setup();
            state.CompletedResearchIds.Clear();
            Assert.Equal(SoulFusionCheck.NotUnlocked, SoulFusionSystem.CheckPrescription(state, a, b, JobClass.Cleric, null));
        }

        [Fact]
        public void Check_NotEligible_WhenCompatibilityBelowMax_OrSamePerson_OrAlreadyParent()
        {
            var (state, a, b) = Setup();
            state.Compatibility[CompatibilitySystem.NormalizeKey(a.Id, b.Id)] = 99;
            Assert.Equal(SoulFusionCheck.NotEligiblePair, SoulFusionSystem.CheckPrescription(state, a, b, JobClass.Cleric, null));

            state.Compatibility[CompatibilitySystem.NormalizeKey(a.Id, b.Id)] = 100;
            Assert.Equal(SoulFusionCheck.NotEligiblePair, SoulFusionSystem.CheckPrescription(state, a, a, JobClass.Warrior, null));

            b.HasUsedSoulFusion = true;
            Assert.Equal(SoulFusionCheck.NotEligiblePair, SoulFusionSystem.CheckPrescription(state, a, b, JobClass.Cleric, null));
        }

        [Fact]
        public void Check_RetiredParent_IsAllowed_ButExpelledIsNot()
        {
            var (state, a, b) = Setup();
            state.Adventurers.Remove(b);
            b.IsRetired = true;
            state.RetiredAdventurers.Add(b);
            Assert.Equal(SoulFusionCheck.Ok, SoulFusionSystem.CheckPrescription(state, a, b, JobClass.Cleric, null));
            Assert.Single(SoulFusionSystem.GetEligiblePairs(state));

            state.RetiredAdventurers.Remove(b);
            state.FallenAdventurers.Add(b);
            Assert.Equal(SoulFusionCheck.NotEligiblePair, SoulFusionSystem.CheckPrescription(state, a, b, JobClass.Cleric, null));
            Assert.Empty(SoulFusionSystem.GetEligiblePairs(state));
        }

        [Fact]
        public void Check_ChildJobMustBeOneOfParents()
        {
            var (state, a, b) = Setup();
            Assert.Equal(SoulFusionCheck.InvalidJob, SoulFusionSystem.CheckPrescription(state, a, b, JobClass.Mage, null));
            Assert.Equal(SoulFusionCheck.Ok, SoulFusionSystem.CheckPrescription(state, a, b, JobClass.Warrior, null));
        }

        [Fact]
        public void Check_GoldAndCatalyst()
        {
            var (state, a, b) = Setup();
            Assert.Equal(SoulFusionCheck.UnknownCatalyst, SoulFusionSystem.CheckPrescription(state, a, b, JobClass.Cleric, "mat_forest_wood"));
            Assert.Equal(SoulFusionCheck.NotEnoughCatalyst, SoulFusionSystem.CheckPrescription(state, a, b, JobClass.Cleric, "mat_canyon_gem"));
            state.AddMaterial("mat_canyon_gem", 3);
            Assert.Equal(SoulFusionCheck.Ok, SoulFusionSystem.CheckPrescription(state, a, b, JobClass.Cleric, "mat_canyon_gem"));

            state.Gold = SoulFusionBalance.PrescriptionGold - 1;
            Assert.Equal(SoulFusionCheck.NotEnoughGold, SoulFusionSystem.CheckPrescription(state, a, b, JobClass.Cleric, null));
        }

        [Fact]
        public void Check_TankBusy_WhileCulturing()
        {
            var (state, a, b) = Setup();
            var c = Make("ミレイ", JobClass.Mage);
            var d = Make("イネス", JobClass.Ranger);
            state.Adventurers.AddRange(new[] { c, d });
            state.Compatibility[CompatibilitySystem.NormalizeKey(c.Id, d.Id)] = 100;

            Assert.NotNull(new SoulFusionSystem(new AlwaysMaxRng()).TryPrescribe(state, a, b, JobClass.Cleric));
            Assert.Equal(SoulFusionCheck.TankBusy, SoulFusionSystem.CheckPrescription(state, c, d, JobClass.Mage, null));
        }

        // ---------------- 処方 ----------------

        [Fact]
        public void Prescribe_PaysCost_MarksParents_AndStartsCulture()
        {
            var (state, a, b) = Setup();
            state.AddMaterial("mat_forest_herb", 7);

            var culture = new SoulFusionSystem(new AlwaysMaxRng()).TryPrescribe(state, a, b, JobClass.Cleric, "mat_forest_herb");

            Assert.NotNull(culture);
            Assert.Equal(10000 - SoulFusionBalance.PrescriptionGold, state.Gold);
            Assert.Equal(2, state.Materials["mat_forest_herb"]);
            Assert.True(a.HasUsedSoulFusion);
            Assert.True(b.HasUsedSoulFusion);
            Assert.Same(culture, Assert.Single(state.SoulFusionCultures));
            Assert.Equal(12, culture!.WeeksRemaining);
            Assert.Equal("mat_forest_herb", culture.CatalystMaterialId);
            Assert.Equal(SoulFusionNyxTier.Destined, culture.NyxTier);

            var child = culture.Child;
            Assert.DoesNotContain(child, state.Adventurers); // 生まれるまではロースターにいない
            Assert.Equal(new[] { a.Id, b.Id }, child.ParentIds);
            Assert.Equal(JobClass.Cleric, child.JobClass);
            Assert.Equal(Placement.Back, child.Placement);
            Assert.Equal(18, child.Age);
            Assert.Equal(Gender.Female, child.Gender);
            Assert.False(child.HasUsedSoulFusion); // 子もいずれ親になれる
            Assert.Equal(child.MaxHP, child.CurrentHP);
            // 週給は副官の見立ての総合PAで決まる（§0.74）
            Assert.Equal(Math.Max(1, (int)(PotentialEstimateSystem.EstimateTotalPa(state, child) * EconomyBalance.WeeklyWageCoefficient)), child.WeeklyWage);

            // 同じ2人はもう処方できない
            Assert.Null(new SoulFusionSystem(new AlwaysMaxRng()).TryPrescribe(state, a, b, JobClass.Cleric));
        }

        [Fact]
        public void Prescribe_Fails_WithoutChangingState()
        {
            var (state, a, b) = Setup();
            state.Gold = 100;
            Assert.Null(new SoulFusionSystem(new AlwaysMaxRng()).TryPrescribe(state, a, b, JobClass.Cleric));
            Assert.Equal(100, state.Gold);
            Assert.False(a.HasUsedSoulFusion);
            Assert.Empty(state.SoulFusionCultures);
        }

        [Fact]
        public void CreateChild_RecordsOrigin_OnTheChild_ForTheLabHistory()
        {
            var (state, a, b) = Setup(JobClass.Warrior, JobClass.Cleric); // 運命の一対
            string catalyst = SoulFusionBalance.Catalysts.First().MaterialId;

            var culture = new SoulFusionSystem(new AlwaysMaxRng()).CreateChild(state, a, b, JobClass.Warrior, catalyst);

            Assert.Equal(catalyst, culture.Child.FusionCatalystId);
            Assert.Equal(SoulFusionNyxTier.Destined, culture.Child.FusionNyxTier);
            Assert.Equal(culture.BreakthroughStats, culture.Child.FusionBreakthroughStats);
            Assert.Equal(new[] { a.Id, b.Id }, culture.Child.ParentIds);
        }

        [Fact]
        public void Origin_SurvivesSaveAndLoad_AndOldDaughtersReadAsUnknown()
        {
            var (state, a, b) = Setup(JobClass.Warrior, JobClass.Cleric);
            var child = new SoulFusionSystem(new AlwaysMaxRng()).CreateChild(state, a, b, JobClass.Warrior, null).Child;
            child.FusionBreakthroughStats = new List<string> { "STR" };
            state.Adventurers.Add(child);

            var loaded = GameState.FromSaveData(state.ToSaveData()).Adventurers.First(x => x.Id == child.Id);
            Assert.Equal(SoulFusionNyxTier.Destined, loaded.FusionNyxTier);
            Assert.Equal(new[] { "STR" }, loaded.FusionBreakthroughStats);
            Assert.Null(loaded.FusionCatalystId);

            // 旧セーブの娘（生まれの記録なし）は既定値で読まれ、壊れない
            var old = new Adventurer { ParentIds = new List<Guid> { a.Id, b.Id } };
            Assert.Null(old.FusionCatalystId);
            Assert.Equal(SoulFusionNyxTier.Ordinary, old.FusionNyxTier);
            Assert.Empty(old.FusionBreakthroughStats);
        }

        // ---------------- 子の能力（PA）と能力限界突破 ----------------

        [Fact]
        public void ChildPa_LeansToHigherParent_PlusVariance_CappedAt100()
        {
            var (state, a, b) = Setup(JobClass.Warrior, JobClass.Knight); // ふつう（突破5%）
            foreach (var stat in AllStats) { SetPa(a, stat, 80); SetPa(b, stat, 61); } // 80×0.75＋61×0.25＝75.25→75（§0.60）

            // 乱数が常に最大：ばらつき+10、突破の判定(100)は外れ、特性の判定も外れる。
            var child = new SoulFusionSystem(new AlwaysMaxRng()).CreateChild(state, a, b, JobClass.Knight, null).Child;
            foreach (var stat in AllStats)
            {
                Assert.Equal(85, GetPa(child, stat));
                Assert.Equal((int)(85 * RecruitmentBalance.YoungestGrowthRatio), GetStat(child, stat));
            }

            foreach (var stat in AllStats) { SetPa(a, stat, 100); SetPa(b, stat, 100); }
            var capped = new SoulFusionSystem(new AlwaysMaxRng()).CreateChild(state, a, b, JobClass.Knight, null);
            Assert.Empty(capped.BreakthroughStats);
            Assert.All(AllStats, stat => Assert.Equal(100, GetPa(capped.Child, stat)));
        }

        [Fact]
        public void HerbCatalyst_RaisesVarianceFloor()
        {
            var (state, a, b) = Setup();
            // 文化圏・名前 → ばらつきは下限（−100を下限に収める）×7 → 突破の判定100（外れ）→ 以降100
            var plain = new SoulFusionSystem(new SequenceRng(100, 0, -100, -100, -100, -100, -100, -100, -100, 100)).CreateChild(state, a, b, JobClass.Warrior, null).Child;
            var herb = new SoulFusionSystem(new SequenceRng(100, 0, -100, -100, -100, -100, -100, -100, -100, 100)).CreateChild(state, a, b, JobClass.Warrior, "mat_forest_herb").Child;
            Assert.All(AllStats, stat => Assert.Equal(75, GetPa(plain, stat))); // 80−5
            Assert.All(AllStats, stat => Assert.Equal(80, GetPa(herb, stat)));  // 80+0（両親の平均を下回らない）
        }

        [Fact]
        public void Breakthrough_LiftsBestStat_Above100()
        {
            var (state, a, b) = Setup(); // 運命の一対
            foreach (var stat in AllStats) { SetPa(a, stat, 80); SetPa(b, stat, 80); }
            a.PA_STR = 100; b.PA_STR = 100;

            // 文化圏・名前 → ばらつき+10×7 → 突破の判定1（当たり）→ 上乗せ15 → 以降100（特性は付かない）
            var culture = new SoulFusionSystem(new SequenceRng(100, 0, 10, 10, 10, 10, 10, 10, 10, 1, 15, 100))
                .CreateChild(state, a, b, JobClass.Warrior, null);

            Assert.Equal(new[] { "STR" }, culture.BreakthroughStats);
            Assert.Equal(120, culture.Child.PA_STR); // 110+15 → 上限120
            Assert.Equal(90, culture.Child.PA_AGI);
            Assert.Equal((int)(120 * RecruitmentBalance.YoungestGrowthRatio), culture.Child.STR);
        }

        // ---------------- 特性の継承 ----------------

        [Fact]
        public void InheritChance_ByKind_AndCatalyst()
        {
            Assert.Equal(30, SoulFusionSystem.InheritChance(TraitCatalog.BraveId, null));
            Assert.Equal(15, SoulFusionSystem.InheritChance(TraitCatalog.GeniusId, null));
            Assert.Equal(20, SoulFusionSystem.InheritChance(TraitCatalog.CowardId, null));
            Assert.Equal(0, SoulFusionSystem.InheritChance(TraitCatalog.OldWoundId, null));
            Assert.Equal(0, SoulFusionSystem.InheritChance(TraitCatalog.TraumaId, null));
            Assert.Equal(0, SoulFusionSystem.InheritChance("NoSuchTrait", null));

            Assert.Equal(50, SoulFusionSystem.InheritChance(TraitCatalog.BraveId, "mat_cave_ore"));
            Assert.Equal(0, SoulFusionSystem.InheritChance(TraitCatalog.CowardId, "mat_ruins_rune"));
            Assert.Equal(50, SoulFusionSystem.InheritChance(TraitCatalog.GeniusId, "mat_abyss_crystal"));
            Assert.Equal(30, SoulFusionSystem.InheritChance(TraitCatalog.BraveId, "mat_abyss_crystal"));
        }

        [Fact]
        public void Traits_AreInherited_ExceptInjuries_AndOvercomeFlaws()
        {
            var a = Make("アリス", JobClass.Warrior, 80, TraitCatalog.BraveId, TraitCatalog.GeniusId);
            var b = Make("セリア", JobClass.Cleric, 80, TraitCatalog.OldWoundId, TraitCatalog.CowardId, TraitCatalog.SicklyId);
            var state = new GameState { Gold = 10000, Adventurers = new List<Adventurer> { a, b } };

            // 文化圏・名前 → ばらつき×7 → 突破の判定100（外れ）→ 継承の判定：豪胆1・天才1・臆病1・病弱1（古傷は判定しない）→ 以降100
            var child = new SoulFusionSystem(new SequenceRng(100, 0, 0, 0, 0, 0, 0, 0, 0, 100, 1, 1, 1, 1, 100))
                .CreateChild(state, a, b, JobClass.Warrior, null).Child;

            Assert.Contains(TraitCatalog.BraveId, child.TraitIds);
            Assert.Contains(TraitCatalog.GeniusId, child.TraitIds);
            Assert.Contains(TraitCatalog.SicklyId, child.TraitIds);
            Assert.DoesNotContain(TraitCatalog.CowardId, child.TraitIds);  // 豪胆を受け継いだので臆病は付かない
            Assert.DoesNotContain(TraitCatalog.OldWoundId, child.TraitIds); // 障害は受け継がない
            Assert.Equal(child.MaxHP, child.CurrentHP);
        }

        // ---------------- 顔グラフィック ----------------

        [Fact]
        public void Portrait_IsUnused_AndPrefersParentColors()
        {
            var (state, a, b) = Setup();
            var pool = PortraitBalance.All;
            if (pool.Count < 2) return;
            a.PortraitId = pool[0].Id;

            var child = new SoulFusionSystem(new AlwaysMaxRng(), new AlwaysMaxRng()).CreateChild(state, a, b, JobClass.Cleric, null).Child;

            Assert.NotNull(child.PortraitId);
            Assert.NotEqual(a.PortraitId, child.PortraitId);
            var picked = PortraitBalance.FindById(child.PortraitId)!;
            bool anyInheriting = pool.Skip(1).Any(p => p.HairColor == pool[0].HairColor || p.EyeColor == pool[0].EyeColor);
            if (anyInheriting)
                Assert.True(picked.HairColor == pool[0].HairColor || picked.EyeColor == pool[0].EyeColor);
        }

        // ---------------- 培養と誕生 ----------------

        [Fact]
        public void Culture_CountsDown_ThenChildJoinsWithStarterGear()
        {
            var (state, a, b) = Setup();
            var culture = new SoulFusionSystem(new AlwaysMaxRng()).TryPrescribe(state, a, b, JobClass.Cleric)!;

            for (int week = 1; week < SoulFusionBalance.CultureWeeks; week++)
                Assert.Empty(SoulFusionSystem.ProcessWeeklyCultures(state));
            Assert.Equal(1, culture.WeeksRemaining);

            var born = SoulFusionSystem.ProcessWeeklyCultures(state);

            Assert.Same(culture, Assert.Single(born));
            Assert.Contains(culture.Child, state.Adventurers);
            Assert.Empty(state.SoulFusionCultures);
            Assert.NotNull(culture.Child.EquippedWeapon);
            Assert.NotNull(culture.Child.EquippedArmor);
            Assert.Equal(culture.Child.MaxHP, culture.Child.CurrentHP);
        }

        [Fact]
        public void Culture_WaitsInTank_WhileDormitoryIsFull()
        {
            var (state, a, b) = Setup();
            var culture = new SoulFusionSystem(new AlwaysMaxRng()).TryPrescribe(state, a, b, JobClass.Cleric)!;
            culture.WeeksRemaining = 1;
            int cap = FacilityBalance.GetDormitoryCapacity(state.GetFacilityLevel(FacilityType.Dormitory));
            while (state.Adventurers.Count < cap)
                state.Adventurers.Add(Make($"団員{state.Adventurers.Count}", JobClass.Ranger));

            Assert.Empty(SoulFusionSystem.ProcessWeeklyCultures(state));
            Assert.Equal(0, culture.WeeksRemaining);
            Assert.True(SoulFusionSystem.IsWaitingForRoom(culture));

            state.Adventurers.RemoveAt(state.Adventurers.Count - 1);
            Assert.Single(SoulFusionSystem.ProcessWeeklyCultures(state));
            Assert.Contains(culture.Child, state.Adventurers);
        }

        [Fact]
        public void Birth_StopsAutoSkip()
        {
            Assert.True(new WeekResult { SoulFusionBirthOccurred = true }.ShouldStopAutoSkip);
        }

        // ---------------- セーブ互換 ----------------

        [Fact]
        public void SaveRoundTrip_PreservesCultureAndLineage()
        {
            var (state, a, b) = Setup();
            var culture = new SoulFusionSystem(new SequenceRng(100, 0, 10, 10, 10, 10, 10, 10, 10, 1, 15, 100))
                .TryPrescribe(state, a, b, JobClass.Warrior)!;

            string json = JsonSerializer.Serialize(state.ToSaveData());
            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(json)!);

            var rc = Assert.Single(restored.SoulFusionCultures);
            Assert.Equal(a.Id, rc.ParentAId);
            Assert.Equal(b.Id, rc.ParentBId);
            Assert.Equal(culture.Child.Id, rc.Child.Id);
            Assert.Equal(culture.Child.Name, rc.Child.Name);
            Assert.Equal(culture.Child.PA_STR, rc.Child.PA_STR);
            Assert.Equal(new[] { a.Id, b.Id }, rc.Child.ParentIds);
            Assert.Equal(culture.BreakthroughStats, rc.BreakthroughStats);
            Assert.Equal(SoulFusionNyxTier.Destined, rc.NyxTier);
            Assert.Equal(12, rc.WeeksRemaining);
            Assert.All(restored.Adventurers, x => Assert.True(x.HasUsedSoulFusion));
        }

        [Fact]
        public void OldSave_WithoutSoulFusionKeys_LoadsWithDefaults()
        {
            var (state, _, _) = Setup();
            string json = JsonSerializer.Serialize(state.ToSaveData());
            string oldJson = System.Text.RegularExpressions.Regex.Replace(json, ",\"(SoulFusionCultures|ParentIds|HasUsedSoulFusion)\":(\\[\\]|false)", "");
            Assert.DoesNotContain("SoulFusion", oldJson);
            Assert.DoesNotContain("ParentIds", oldJson);

            var restored = GameState.FromSaveData(JsonSerializer.Deserialize<SaveData>(oldJson)!);

            Assert.Empty(restored.SoulFusionCultures);
            Assert.All(restored.Adventurers, x => { Assert.Empty(x.ParentIds); Assert.False(x.HasUsedSoulFusion); });
        }

        [Fact]
        public void Save_WithNullCultures_LoadsAsEmpty()
        {
            var data = new GameState().ToSaveData();
            data.SoulFusionCultures = null;
            Assert.Empty(GameState.FromSaveData(data).SoulFusionCultures);

            data.SoulFusionCultures = new List<SoulFusionCulture> { new() { Child = null! } };
            Assert.Empty(GameState.FromSaveData(data).SoulFusionCultures); // 子の欠けた記録は捨てる
        }

        [Fact]
        public void FindAdventurer_SearchesActiveRetiredAndFallen()
        {
            var active = Make("A", JobClass.Warrior);
            var retired = Make("R", JobClass.Cleric);
            var fallen = Make("F", JobClass.Mage);
            var state = new GameState
            {
                Adventurers = new() { active }, RetiredAdventurers = new() { retired }, FallenAdventurers = new() { fallen },
            };
            Assert.Same(active, state.FindAdventurer(active.Id));
            Assert.Same(retired, state.FindAdventurer(retired.Id));
            Assert.Same(fallen, state.FindAdventurer(fallen.Id));
            Assert.Null(state.FindAdventurer(Guid.NewGuid()));
        }

        // ---------------- ヘルパー ----------------

        private static int GetPa(Adventurer a, string stat) => stat switch
        {
            "STR" => a.PA_STR, "AGI" => a.PA_AGI, "VIT" => a.PA_VIT, "MND" => a.PA_MND,
            "DEX" => a.PA_DEX, "LDR" => a.PA_LDR, _ => a.PA_INT,
        };

        private static int GetStat(Adventurer a, string stat) => stat switch
        {
            "STR" => a.STR, "AGI" => a.AGI, "VIT" => a.VIT, "MND" => a.MND,
            "DEX" => a.DEX, "LDR" => a.LDR, _ => a.INT,
        };

        private static void SetPa(Adventurer a, string stat, int value)
        {
            switch (stat)
            {
                case "STR": a.PA_STR = value; break;
                case "AGI": a.PA_AGI = value; break;
                case "VIT": a.PA_VIT = value; break;
                case "MND": a.PA_MND = value; break;
                case "DEX": a.PA_DEX = value; break;
                case "LDR": a.PA_LDR = value; break;
                default: a.PA_INT = value; break;
            }
        }
    }
}
