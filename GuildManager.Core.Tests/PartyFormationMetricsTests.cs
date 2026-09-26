using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 武具の能力値補正が部隊指標（→ PartyFormationSystem.CalculateMetrics、03 §4.5.3）と
    /// 討伐CP（→ DungeonPowerCalculator）へ波及することのテスト（2026年9月、武具の7大能力値補正）。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~PartyFormationMetrics`
    /// </summary>
    public class PartyFormationMetricsTests
    {
        private static Adventurer MakeAdventurer(JobClass job)
        {
            var a = new Adventurer
            {
                JobClass = job,
                STR = 40, VIT = 40, AGI = 40, DEX = 40, INT = 40, MND = 40, LDR = 40,
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

        [Fact]
        public void Dagger_RaisesStealthScore()
        {
            var thief = MakeAdventurer(JobClass.Thief);
            var party = PartyOf(thief);
            var before = PartyFormationSystem.CalculateMetrics(party);

            thief.SetEquippedId(EquipmentSlot.Weapon, ItemCatalog.DaggerId); // DEX+5, AGI+2
            var after = PartyFormationSystem.CalculateMetrics(party);

            Assert.True(after.BaseStealthScore > before.BaseStealthScore);
            Assert.True(after.StealthScore > before.StealthScore);
        }

        [Theory]
        [InlineData(ItemCatalog.MaceId)]  // MND+5
        [InlineData(ItemCatalog.SpearId)] // VIT+3
        public void MndOrVitWeapon_RaisesTraversalPower(string weaponId)
        {
            var knight = MakeAdventurer(JobClass.Knight);
            var party = PartyOf(knight);
            double before = PartyFormationSystem.CalculateMetrics(party).TraversalPower;

            knight.SetEquippedId(EquipmentSlot.Weapon, weaponId);

            Assert.True(PartyFormationSystem.CalculateMetrics(party).TraversalPower > before);
        }

        [Fact]
        public void Grimoire_RaisesAnalysisScore()
        {
            var mage = MakeAdventurer(JobClass.Mage);
            var party = PartyOf(mage);
            double before = PartyFormationSystem.CalculateMetrics(party).AnalysisScore;

            mage.SetEquippedId(EquipmentSlot.Weapon, ItemCatalog.GrimoireId); // INT+4

            Assert.True(PartyFormationSystem.CalculateMetrics(party).AnalysisScore > before);
        }

        [Fact]
        public void GreatSword_RaisesGuardPower()
        {
            var warrior = MakeAdventurer(JobClass.Warrior);
            var party = PartyOf(warrior);

            warrior.SetEquippedId(EquipmentSlot.Weapon, ItemCatalog.GreatSwordId); // STR+6

            Assert.Equal(46, PartyFormationSystem.CalculateMetrics(party).GuardPower);
        }

        [Fact]
        public void StatBonus_RaisesBossPower_ByWeightedBonus()
        {
            // §0.37：討伐火力＝Σ実効ステータス×重み×HP比率（個人CPの固定加算は撤廃）。
            // 大剣（STR+6・VIT+2）の寄与はちょうど 6×STR重み＋2×VIT重み。
            var warrior = MakeAdventurer(JobClass.Warrior);
            double before = DungeonPowerCalculator.MemberPower(warrior);

            warrior.SetEquippedId(EquipmentSlot.Weapon, ItemCatalog.GreatSwordId);
            warrior.CurrentHP = warrior.MaxHP; // VIT補正で最大HPが伸びた分を満タンに戻す

            double W(string stat) => DungeonBalance.BossPowerWeights.First(w => w.Stat == stat).Weight;
            double expectedGain = 6 * W("STR") + 2 * W("VIT");
            Assert.Equal(before + expectedGain, DungeonPowerCalculator.MemberPower(warrior), precision: 6);
        }

        [Theory]
        [InlineData(ItemCatalog.ChainmailId, 0)]
        [InlineData(ItemCatalog.HeavyArmorId, 1)]
        [InlineData(ItemCatalog.PlateArmorId, 1)]
        public void HeavyArmorPenalty_AppliesToHeavyAndPlate_ButNotChainmail(string armorId, int expectedHeavy)
        {
            // 職業では重装扱いにならない神官で、防具だけの判定を見る（全身板金鎧は本来神官不可だが、
            // SetEquippedId は職業制限を見ないため、重装区分の判定だけを切り出して検証できる）。
            var cleric = MakeAdventurer(JobClass.Cleric);
            var party = PartyOf(cleric);
            cleric.SetEquippedId(EquipmentSlot.Armor, armorId);

            var metrics = PartyFormationSystem.CalculateMetrics(party);

            Assert.Equal(expectedHeavy, metrics.HeavyMemberCount);
            Assert.Equal(expectedHeavy * ScoutingBalance.StealthHeavyArmorPenalty,
                ScoutingResolver.CalculateHeavyArmorPenalty(party), precision: 6);
        }
    }
}
