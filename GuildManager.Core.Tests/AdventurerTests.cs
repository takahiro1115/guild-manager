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

        // ---------------- 生涯ピーク値（→ 03 §2.2新規・§7） ----------------

        [Fact]
        public void PeakStats_DefaultToCurrentActualValue_WhenNeverExplicitlyRecorded()
        {
            // 一度も成長していない冒険者は、Peak==現在の実効値になる
            // （SampleData・RecruitmentSystemで別途Peakを初期化する必要はない設計。→ 項目30.4）。
            var a = new Adventurer { STR = 60, VIT = 55, AGI = 35, DEX = 20, MND = 10, INT = 15, LDR = 40 };

            Assert.Equal(60, a.PeakSTR);
            Assert.Equal(55, a.PeakVIT);
            Assert.Equal(35, a.PeakAGI);
            Assert.Equal(20, a.PeakDEX);
            Assert.Equal(10, a.PeakMND);
            Assert.Equal(15, a.PeakINT);
            Assert.Equal(40, a.PeakLDR);
        }

        [Fact]
        public void PeakStat_RemembersHighestRecordedValue_EvenAfterActualValueDecreases()
        {
            var a = new Adventurer { STR = 40 };
            a.PeakSTR = 40; // 成長ロールで記録された、という想定（GrowthSystemが行う操作を模擬）

            a.STR = 20; // 衰微・古傷等で現在値のみ下がった想定（Peakには影響しない）

            Assert.Equal(20, a.STR);
            Assert.Equal(40, a.PeakSTR); // ピークは下がらない
        }

        [Fact]
        public void PeakStat_NeverGoesBelowCurrentActualValue()
        {
            // Peakのgetterは Max(記録値, 現在値) を返すため、現在値がピークの記録値より
            // 高い場合はその現在値がそのまま返る（ピークが実質的な下限を割ることは無い）。
            var a = new Adventurer { STR = 10 };
            a.PeakSTR = 10;

            a.STR = 90; // 通常はGrowthSystem経由でPeakも同時に更新されるが、ここでは実効値のみ変更

            Assert.Equal(90, a.PeakSTR);
        }
    }
}
