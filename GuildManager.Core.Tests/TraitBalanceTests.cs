using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// TraitBalance（→ docs/04_バランス表/trait.csv、項目58フォローアップ）のテスト。
    /// CSV由来の値が旧・直書き値（-0.15等）と一致すること、およびTraitCatalog側の
    /// TraitDefinitionへ正しく反映されていることを確認する。
    /// </summary>
    public class TraitBalanceTests
    {
        [Fact]
        public void Values_MatchFormerHardcodedConstants()
        {
            Assert.Equal(-0.15, TraitBalance.OldWoundStatReduction, precision: 10);
            Assert.Equal(-0.15, TraitBalance.TraumaMndReduction, precision: 10);
            Assert.Equal(5, TraitBalance.BraveSurvivalThresholdBonus, precision: 10);
            Assert.Equal(0.10, TraitBalance.AttentiveScoutingBonus, precision: 10);
            Assert.Equal(1.5, TraitBalance.BeautifulCompatibilityGainMultiplier, precision: 10);
        }

        [Fact]
        public void OldWound_EffectsUseTraitBalanceValue_ForAllFourStats()
        {
            foreach (var effect in TraitCatalog.OldWound.Effects)
            {
                Assert.Equal(TraitEffectType.StatPercentReduction, effect.EffectType);
                Assert.Equal(TraitBalance.OldWoundStatReduction, effect.Value, precision: 10);
            }
        }

        [Fact]
        public void Trauma_EffectUsesTraitBalanceValue()
        {
            var effect = Assert.Single(TraitCatalog.Trauma.Effects);
            Assert.Equal("MND", effect.TargetStat);
            Assert.Equal(TraitBalance.TraumaMndReduction, effect.Value, precision: 10);
        }

        [Fact]
        public void Brave_EffectUsesTraitBalanceValue()
        {
            var effect = Assert.Single(TraitCatalog.Brave.Effects);
            Assert.Equal(TraitEffectType.SurvivalThresholdModifier, effect.EffectType);
            Assert.Equal(TraitBalance.BraveSurvivalThresholdBonus, effect.Value, precision: 10);
        }

        [Fact]
        public void Attentive_EffectUsesTraitBalanceValue()
        {
            var effect = Assert.Single(TraitCatalog.Attentive.Effects);
            Assert.Equal(TraitEffectType.ScoutingModifier, effect.EffectType);
            Assert.Equal(TraitBalance.AttentiveScoutingBonus, effect.Value, precision: 10);
        }

        [Fact]
        public void Beautiful_EffectUsesTraitBalanceValue()
        {
            var effect = Assert.Single(TraitCatalog.Beautiful.Effects);
            Assert.Equal(TraitEffectType.CompatibilityGainMultiplier, effect.EffectType);
            Assert.Equal(TraitBalance.BeautifulCompatibilityGainMultiplier, effect.Value, precision: 10);
        }
    }
}
