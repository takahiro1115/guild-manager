using System.Linq;
using GuildManager.Core.Models;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// Adventurerモデル単体のテスト（仕様書 03 §2.2）。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class AdventurerTests
    {
        [Fact]
        public void TotalPA_AveragesAllSevenPaFields_IncludingInt()
        {
            // v1.2改訂：INT活性化に伴い、総合PAは6値平均から7値平均に変わった（→ 03 §2.2）。
            var a = new Adventurer
            {
                PA_STR = 10, PA_AGI = 20, PA_VIT = 30, PA_MND = 40,
                PA_DEX = 50, PA_LDR = 60, PA_INT = 70,
            };

            // (10+20+30+40+50+60+70)/7 = 280/7 = 40
            Assert.Equal(40.0, a.TotalPA);
        }

        [Fact]
        public void TotalPA_DefaultsTo100_WhenNoPaFieldsAreSet()
        {
            // 全PAフィールドのデフォルトは100（実効値の成長上限なし相当）。
            var a = new Adventurer();

            Assert.Equal(100.0, a.TotalPA);
        }

        // ---------------- 特性（Trait）エンジン（→ 03 §4.3・§5.3） ----------------

        [Fact]
        public void TryAddTrait_Succeeds_WhenNotAlreadyHeld()
        {
            var a = new Adventurer();

            bool result = a.TryAddTrait(TraitCatalog.OldWoundId);

            Assert.True(result);
            Assert.True(a.HasTrait(TraitCatalog.OldWoundId));
        }

        [Fact]
        public void TryAddTrait_Fails_WhenAlreadyHeld_DuplicatesNotAllowed()
        {
            var a = new Adventurer();
            a.TryAddTrait(TraitCatalog.OldWoundId);

            bool result = a.TryAddTrait(TraitCatalog.OldWoundId);

            Assert.False(result);
            Assert.Single(a.TraitIds); // 重複して追加されていない
        }

        // ---------------- 特性スロット上限（→ 特性伝授・スロット上限刷新仕様） ----------------

        [Fact]
        public void Adventurer_CannotExceed_MaxFiveTraits()
        {
            var a = new Adventurer();
            var traitIds = new[] { "Trait1", "Trait2", "Trait3", "Trait4", "Trait5", "Trait6" };

            var results = traitIds.Select(id => a.TryAddTrait(id)).ToList();

            // 先頭5件はスロット上限(5)以内なので成功、6件目は満杯のため失敗する。
            Assert.Equal(new[] { true, true, true, true, true, false }, results);
            Assert.Equal(5, a.TraitIds.Count);
            Assert.DoesNotContain("Trait6", a.TraitIds);
        }

        [Fact]
        public void GetEffectiveStat_ReturnsBaseValue_WhenNoTraitsHeld()
        {
            var a = new Adventurer { STR = 50 };

            Assert.Equal(50, a.GetEffectiveStat("STR"));
        }

        [Fact]
        public void GetEffectiveStat_AppliesOldWoundReduction_ToStrVitAgiDex()
        {
            var a = new Adventurer { STR = 100, VIT = 100, AGI = 100, DEX = 100, MND = 100, INT = 100, LDR = 100 };
            a.TryAddTrait(TraitCatalog.OldWoundId);

            // 古傷はSTR/VIT/AGI/DEXを15%（暫定値）低下させる。
            Assert.Equal(85.0, a.GetEffectiveStat("STR"));
            Assert.Equal(85.0, a.GetEffectiveStat("VIT"));
            Assert.Equal(85.0, a.GetEffectiveStat("AGI"));
            Assert.Equal(85.0, a.GetEffectiveStat("DEX"));

            // MND/INT/LDRは対象外（古傷は「精神系」を低下させない）。
            Assert.Equal(100.0, a.GetEffectiveStat("MND"));
            Assert.Equal(100.0, a.GetEffectiveStat("INT"));
            Assert.Equal(100.0, a.GetEffectiveStat("LDR"));
        }

        [Fact]
        public void MaxHP_ReflectsOldWoundReduction_ViaGetEffectiveStat()
        {
            var a = new Adventurer { VIT = 100 };
            int maxHpBefore = a.MaxHP; // 100*2+50=250

            a.TryAddTrait(TraitCatalog.OldWoundId);
            int maxHpAfter = a.MaxHP; // 実効VIT=85 → 85*2+50=220

            Assert.Equal(250, maxHpBefore);
            Assert.Equal(220, maxHpAfter);
        }

        // ---------------- 相性/特性拡充：新特性4種とSumTraitEffect（→ 03 §5.3、v1.4改訂） ----------------

        [Fact]
        public void GetEffectiveStat_AppliesTraumaReduction_ToMndOnly()
        {
            var a = new Adventurer { STR = 100, VIT = 100, AGI = 100, DEX = 100, MND = 100, INT = 100, LDR = 100 };
            a.TryAddTrait(TraitCatalog.TraumaId);

            Assert.Equal(85.0, a.GetEffectiveStat("MND"));
            Assert.Equal(100.0, a.GetEffectiveStat("STR")); // MND以外は対象外
        }

        [Fact]
        public void SumTraitEffect_ReturnsZero_WhenNoMatchingTraitHeld()
        {
            var a = new Adventurer();
            a.TryAddTrait(TraitCatalog.OldWoundId);

            Assert.Equal(0, a.SumTraitEffect(TraitEffectType.SurvivalThresholdModifier));
        }

        [Fact]
        public void SumTraitEffect_ReturnsBraveBonus_ForSurvivalThresholdModifier()
        {
            var a = new Adventurer();
            a.TryAddTrait(TraitCatalog.BraveId);

            Assert.Equal(5, a.SumTraitEffect(TraitEffectType.SurvivalThresholdModifier));
        }

        [Fact]
        public void SumTraitEffect_ReturnsAttentiveBonus_ForScoutingModifier_OnDexOnly()
        {
            var a = new Adventurer();
            a.TryAddTrait(TraitCatalog.AttentiveId);

            Assert.Equal(0.10, a.SumTraitEffect(TraitEffectType.ScoutingModifier, "DEX"));
            Assert.Equal(0, a.SumTraitEffect(TraitEffectType.ScoutingModifier, "STR")); // 対象ステータス違いは合算しない
        }

        [Fact]
        public void SumTraitEffect_TargetStatNull_SumsAcrossAllTargetStats()
        {
            // TargetStatをnullにすると対象ステータスを問わず合算する（SurvivalThresholdModifier等向け）。
            var a = new Adventurer();
            a.TryAddTrait(TraitCatalog.OldWoundId); // STR/VIT/AGI/DEXの4件、各-0.15

            Assert.Equal(-0.6, a.SumTraitEffect(TraitEffectType.StatPercentReduction), precision: 10);
        }
    }
}
