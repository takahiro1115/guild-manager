using System;
using System.Collections.Generic;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 負傷の大迷宮への復活と、豪胆・注意深い・知識人の効果の付け直し（→ 03 §0.53）のテスト。
    /// </summary>
    public class InjuryRevivalTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private static Adventurer Make(int stat, JobClass job = JobClass.Ranger)
        {
            var a = new Adventurer { Name = job.ToString(), JobClass = job, STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            return party;
        }

        // ---------------- 重傷・軽傷の判定（CriticalInjury） ----------------

        [Fact]
        public void TryInflictSevere_BelowThreshold_MakesSevereInjury()
        {
            var a = Make(20);
            a.CurrentHP = (int)(a.MaxHP * CombatBalance.SevereInjuryHpThresholdPct) - 1;

            var injury = CriticalInjury.TryInflictSevere(a, new AlwaysMinRng());

            Assert.NotNull(injury);
            Assert.Equal(InjurySeverity.Severe, a.Injury);
            Assert.Equal(CombatBalance.SevereInjuryWeeksMin, a.InjuryWeeksRemaining);
            Assert.False(a.IsAvailable); // 重傷は出撃できない
        }

        [Fact]
        public void TryInflictSevere_AtOrAboveThreshold_OrAtZeroHp_DoesNothing()
        {
            var healthy = Make(20);
            healthy.CurrentHP = (int)Math.Ceiling(healthy.MaxHP * CombatBalance.SevereInjuryHpThresholdPct);
            var fallen = Make(20);
            fallen.CurrentHP = 0; // HP0は強制除籍の扱い（→ DungeonResolver）

            Assert.Null(CriticalInjury.TryInflictSevere(healthy, new AlwaysMinRng()));
            Assert.Null(CriticalInjury.TryInflictSevere(fallen, new AlwaysMinRng()));
            Assert.Equal(InjurySeverity.None, healthy.Injury);
            Assert.Equal(InjurySeverity.None, fallen.Injury);
        }

        [Fact]
        public void TryInflictLight_OnlyWhenUninjured()
        {
            var fresh = Make(20);
            var severe = Make(20);
            severe.Injury = InjurySeverity.Severe;
            severe.InjuryWeeksRemaining = 5;

            Assert.NotNull(CriticalInjury.TryInflictLight(fresh, new AlwaysMinRng()));
            Assert.Equal(InjurySeverity.Light, fresh.Injury);
            Assert.Equal(CombatBalance.LightInjuryWeeksMin, fresh.InjuryWeeksRemaining);
            Assert.True(fresh.IsAvailable); // 軽傷は出撃できる

            Assert.Null(CriticalInjury.TryInflictLight(severe, new AlwaysMinRng()));
            Assert.Equal(InjurySeverity.Severe, severe.Injury); // 重傷を軽傷で上書きしない
            Assert.Equal(5, severe.InjuryWeeksRemaining);
        }

        [Fact]
        public void LightInjury_LowersEffectiveStats_ButNotMaxHp()
        {
            var a = Make(50);
            int maxHpBefore = a.MaxHP;
            double strBefore = a.GetEffectiveStat("STR");

            a.Injury = InjurySeverity.Light;

            Assert.Equal(strBefore * (1 - CombatBalance.LightInjuryStatPenaltyRate), a.GetEffectiveStat("STR"), precision: 6);
            Assert.Equal(maxHpBefore, a.MaxHP);
        }

        // ---------------- 回復（InjuryRecoverySystem） ----------------

        [Fact]
        public void Recovery_SkipsJustInjured_AndLightHealsWithoutRestoringHp()
        {
            var justHurt = Make(20);
            justHurt.Injury = InjurySeverity.Light;
            justHurt.InjuryWeeksRemaining = 1;
            var healing = Make(20);
            healing.Injury = InjurySeverity.Light;
            healing.InjuryWeeksRemaining = 1;
            healing.CurrentHP = 5;
            var state = new GameState { Adventurers = { justHurt, healing } };

            new InjuryRecoverySystem().ProcessWeeklyRecovery(state, new HashSet<Guid> { justHurt.Id });

            Assert.Equal(InjurySeverity.Light, justHurt.Injury); // 負傷した週は回復を進めない
            Assert.Equal(1, justHurt.InjuryWeeksRemaining);
            Assert.Equal(InjurySeverity.None, healing.Injury);    // 軽傷は治る
            Assert.Equal(5, healing.CurrentHP);                   // が、HPは満タンにしない（静養で回復する）
        }

        // ---------------- 階層ボス戦（DungeonResolver） ----------------

        [Fact]
        public void BossFight_SurvivorBelowThreshold_BecomesSeverelyInjured()
        {
            // 1Fボス（要求352.5）。全能力90の隊員（火力378）で撃破し、HP30%で挑んだ隊員は撃破時の損耗12%（AlwaysMin）で18%に落ちて重傷。
            var strong = Make(90);
            var worn = Make(10);
            worn.CurrentHP = (int)(worn.MaxHP * 0.30);
            var boss = new FloorBoss { Name = "1Fの主", Floor = 1, FieldOrder = 1, MaxHp = 1, CurrentHp = 1 };

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(strong, worn), boss);

            Assert.Equal(DungeonOutcome.Victory, result.Outcome);
            Assert.Equal(InjurySeverity.Severe, worn.Injury);
            Assert.Equal(InjurySeverity.None, strong.Injury);
            var injury = Assert.Single(result.InjuryEvents);
            Assert.Equal(worn.Id, injury.AdventurerId);
            Assert.Equal(InjurySeverity.Severe, injury.Severity);
        }

        [Fact]
        public void Brave_ReducesOwnBossFightHpLoss()
        {
            var brave = Make(90);
            brave.TryAddTrait(TraitCatalog.BraveId);
            var plain = Make(90);
            var boss = new FloorBoss { Name = "1Fの主", Floor = 1, FieldOrder = 1, MaxHp = 1, CurrentHp = 1 };

            var result = new DungeonResolver(new AlwaysMinRng()).Resolve(PartyOf(brave, plain), boss);

            int basePct = DungeonBalance.BaseHpLossPctMin; // 撃破・AlwaysMin
            Assert.Equal(plain.MaxHP * basePct / 100, result.HpLostByAdventurer[plain.Id]);
            Assert.Equal(brave.MaxHP * (basePct - (int)TraitBalance.BraveSurvivalThresholdBonus) / 100, result.HpLostByAdventurer[brave.Id]);
        }

        // ---------------- 注意深い・知識人（ScoutingResolver） ----------------

        [Fact]
        public void Attentive_RaisesOwnStealthValue()
        {
            var attentive = Make(40);
            attentive.TryAddTrait(TraitCatalog.AttentiveId);
            var plain = Make(40);

            Assert.Equal(ScoutingResolver.GetStealthValue(plain) * (1 + TraitBalance.AttentiveScoutingBonus),
                ScoutingResolver.GetStealthValue(attentive), precision: 6);
        }

        [Fact]
        public void Scholar_RaisesOwnAnalysisValue()
        {
            var scholar = Make(40);
            scholar.TryAddTrait(TraitCatalog.ScholarId);
            var plain = Make(40);

            Assert.Equal(0.20, TraitBalance.ScholarAnalysisBonus, precision: 6);
            Assert.Equal(ScoutingResolver.GetAnalysisValue(plain) * (1 + TraitBalance.ScholarAnalysisBonus),
                ScoutingResolver.GetAnalysisValue(scholar), precision: 6);
        }
    }
}
