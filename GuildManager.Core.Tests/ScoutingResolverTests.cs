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
    /// 調査任務（スカウティング）と解析率ロジック（→ ダンジョン攻略システム）のテスト。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class ScoutingResolverTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        /// <summary>全能力を指定値で揃えたHP満タンの冒険者。</summary>
        private static Adventurer MakeAdventurer(int stat)
        {
            var a = new Adventurer { STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        /// <summary>AGI/DEX（隠密）とINT（解析）を個別に指定できる冒険者。</summary>
        private static Adventurer MakeSpecialist(int agiDex, int intel, int ldr = 0)
        {
            var a = new Adventurer { STR = 10, AGI = agiDex, VIT = 30, MND = 10, DEX = agiDex, LDR = ldr, INT = intel };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            return party;
        }

        private static FloorBoss MakeBoss(int floor = 1, double intelRate = 0.0) => new FloorBoss
        {
            Name = "階層の主", Floor = floor, MaxHp = 500, CurrentHp = 500, IntelRate = intelRate,
        };

        /// <summary>
        /// 護衛「余裕」時の解析成果倍率。以下の既存テストの部隊（VIT30以上）と浅い階層のボスでは
        /// 護衛比率が常に1.4以上になるため、期待値にこの倍率を掛けて比較する。
        /// </summary>
        private static readonly double AbundantGuard = ScoutingBalance.GuardIntelMultiplierAbundant;

        // ---------------- 解析率の段階（→ IntelTier） ----------------

        [Theory]
        [InlineData(0.00, IntelTier.Unknown)]
        [InlineData(0.24, IntelTier.Unknown)]
        [InlineData(0.25, IntelTier.Basic)]           // 閾値ちょうど
        [InlineData(0.49, IntelTier.Basic)]
        [InlineData(0.50, IntelTier.Hazards)]
        [InlineData(0.74, IntelTier.Hazards)]
        [InlineData(0.75, IntelTier.Countermeasures)]
        [InlineData(0.99, IntelTier.Countermeasures)]
        [InlineData(1.00, IntelTier.Complete)]
        public void GetTier_MapsIntelRateToDisclosureStage(double intelRate, IntelTier expected)
        {
            Assert.Equal(expected, ScoutingResolver.GetTier(intelRate));
        }

        // ---------------- 解析判定（INT） ----------------

        [Fact]
        public void Resolve_HighIntParty_GreatlyAdvancesIntelRate()
        {
            // 解析要求値＝階層1×14。INT合算が十分高ければ大成功となり、一度で0.50上昇する。
            var party = PartyOf(MakeSpecialist(agiDex: 60, intel: 60), MakeSpecialist(agiDex: 60, intel: 60));
            var boss = MakeBoss(floor: 1);

            var result = new ScoutingResolver(new AlwaysMinRng()).Resolve(party, boss);

            Assert.True(result.StealthSucceeded);
            Assert.Equal(SurveyOutcome.GreatSuccess, result.AnalysisOutcome);
            Assert.Equal(ScoutingBalance.IntelGainGreatSuccess * AbundantGuard, result.IntelGained, precision: 10);
            Assert.Equal(ScoutingBalance.IntelGainGreatSuccess * AbundantGuard, boss.IntelRate, precision: 10);
        }

        [Fact]
        public void Resolve_LowIntParty_StillBringsBackPartialIntel()
        {
            // 解析に失敗しても断片情報は持ち帰れる（調査が完全な無駄骨にはならない）。
            var party = PartyOf(MakeSpecialist(agiDex: 80, intel: 1));
            var boss = MakeBoss(floor: 3);

            var result = new ScoutingResolver(new AlwaysMinRng()).Resolve(party, boss);

            Assert.Equal(SurveyOutcome.Failure, result.AnalysisOutcome);
            Assert.Equal(ScoutingBalance.IntelGainPartial * AbundantGuard, result.IntelGained, precision: 10);
        }

        [Fact]
        public void Resolve_IntelRateIsCappedAtComplete()
        {
            var party = PartyOf(MakeSpecialist(agiDex: 90, intel: 90), MakeSpecialist(agiDex: 90, intel: 90));
            var boss = MakeBoss(floor: 1, intelRate: 0.9);

            var result = new ScoutingResolver(new AlwaysMinRng()).Resolve(party, boss);

            Assert.Equal(1.0, boss.IntelRate, precision: 10);
            Assert.Equal(IntelTier.Complete, result.TierAfter);
            Assert.Equal(0.1, result.IntelGained, precision: 10); // クランプ後の実際の増分だけを報告する
        }

        [Fact]
        public void Resolve_ReportsTierAdvancement()
        {
            var party = PartyOf(MakeSpecialist(agiDex: 60, intel: 60), MakeSpecialist(agiDex: 60, intel: 60));
            var boss = MakeBoss(floor: 1);

            var first = new ScoutingResolver(new AlwaysMinRng()).Resolve(party, boss);

            Assert.True(first.TierAdvanced); // Unknown → Hazards（0.50）
            Assert.Equal(IntelTier.Hazards, first.TierAfter);
        }

        // ---------------- 隠密判定（AGI+DEX＋部隊長LDR） ----------------

        [Fact]
        public void Resolve_LowAgiDexParty_IsDiscovered()
        {
            // 深い階層（要求値＝5×18=90）に対してAGI+DEXが足りないと見つかる。
            var party = PartyOf(MakeSpecialist(agiDex: 5, intel: 90));
            var boss = MakeBoss(floor: 5);

            var result = new ScoutingResolver(new AlwaysMinRng()).Resolve(party, boss);

            Assert.False(result.StealthSucceeded);
        }

        [Fact]
        public void Resolve_LeaderLdr_HelpsStealthSucceed()
        {
            // 同じAGI/DEXでも、部隊長のLDR（パニック・事故防止）が高いと隠密に成功しうる。
            var boss1 = MakeBoss(floor: 2);
            var boss2 = MakeBoss(floor: 2);
            var resolver = new ScoutingResolver(new AlwaysMinRng());

            // 要求値＝2×7=14。単独の平均素点(12+12)×1.10＝26.4→26、重戦士（既定の職）の重装ペナルティ−15で11と届かないが、
            // 部隊長LDR60×0.25=15 を足せば26で超える（§0.41の値）。
            var withoutLeader = resolver.Resolve(PartyOf(MakeSpecialist(agiDex: 12, intel: 50, ldr: 0)), boss1);
            var withLeader = resolver.Resolve(PartyOf(MakeSpecialist(agiDex: 12, intel: 50, ldr: 60)), boss2);

            Assert.False(withoutLeader.StealthSucceeded);
            Assert.True(withLeader.StealthSucceeded);
        }

        [Fact]
        public void Resolve_DiscoveredParty_GetsDowngradedAnalysisResult()
        {
            // 見つかると落ち着いて観察できず、解析成果が1段階下がる
            //（AGI/DEX型とINT型の両方を編成する動機を作るための連動）。
            var boss = MakeBoss(floor: 5);
            // INTは大成功域（要求70に対し合算150）だが、AGI+DEXは要求90に対し合算10で確実に見つかる。
            var party = PartyOf(MakeSpecialist(agiDex: 5, intel: 150));

            var result = new ScoutingResolver(new AlwaysMinRng()).Resolve(party, boss);

            Assert.False(result.StealthSucceeded);
            Assert.Equal(SurveyOutcome.Success, result.AnalysisOutcome); // 大成功→成功へ格下げ
            Assert.Equal(ScoutingBalance.IntelGainSuccess * AbundantGuard, result.IntelGained, precision: 10);
        }

        // ---------------- HP消費（低リスク経路：致死判定に接続しない） ----------------

        [Fact]
        public void Resolve_HpLoss_IsDecidedByGuardTier_NotByStealth()
        {
            // 2026年9月改訂：HP消費は隠密の成否ではなく護衛段階だけで決まる。
            // 見つかっても護衛が「余裕」ならHPは減らない。
            var discovered = MakeSpecialist(agiDex: 1, intel: 50);

            var result = new ScoutingResolver(new AlwaysMaxRng()).Resolve(PartyOf(discovered), MakeBoss(floor: 5));

            Assert.False(result.StealthSucceeded);
            Assert.Equal(GuardTier.Abundant, result.GuardTier);
            Assert.Equal(0, result.HpLostByAdventurer[discovered.Id]);
        }

        // ---------------- 護衛判定（4段階、2026年9月新設） ----------------

        /// <summary>STR/VIT/INTを個別に指定できる冒険者（その他の能力は低め）。</summary>
        private static Adventurer MakeGuard(int str, int vit, int intel)
        {
            var a = new Adventurer { STR = str, AGI = 10, VIT = vit, MND = 10, DEX = 10, LDR = 0, INT = intel };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        [Fact]
        public void Scouting_GuardPower_IsCarrierPlusSupport()
        {
            // §0.41：護衛力＝主護衛の max(STR,VIT,INT) ＋ 他の隊員の max(STR,VIT,INT) の合計×支援係数0.3。
            // 各員の護衛値は 40（STR）・55（INT）・48（VIT）→ 主護衛55 ＋ (40+48)×0.3＝26.4 ＝ 81.4
            var party = PartyOf(MakeGuard(str: 40, vit: 20, intel: 10), MakeGuard(str: 5, vit: 30, intel: 55), MakeGuard(str: 12, vit: 48, intel: 3));

            Assert.Equal(0.3, ScoutingBalance.GuardSupportRatio, precision: 6);
            Assert.Equal(81.4, ScoutingResolver.CalculateGuardPower(party), precision: 6);
            Assert.Equal(26.4, ScoutingResolver.CalculateGuardSupportPower(party), precision: 6);
            // 単独行は主護衛の値そのもの（旧式と同じ）
            Assert.Equal(40, ScoutingResolver.CalculateGuardPower(PartyOf(MakeGuard(str: 40, vit: 20, intel: 10))), precision: 6);
            Assert.Equal(0, ScoutingResolver.CalculateGuardSupportPower(PartyOf(MakeGuard(str: 40, vit: 20, intel: 10))), precision: 6);
            Assert.Equal(0, ScoutingResolver.CalculateGuardPower(new Party()), precision: 6);
        }

        [Fact]
        public void Scouting_GuardPower_GrowsWithPartySize_WhileStealthShrinks()
        {
            // 同じ能力の隊員を足していくと、護衛力は上がり隠密は下がる（§0.41：調査編成の駆け引き）。
            var members = Enumerable.Range(0, 4).Select(_ => MakeSpecialist(agiDex: 40, intel: 40)).ToArray();
            var guards = new List<double>();
            var stealths = new List<double>();
            for (int n = 1; n <= 4; n++)
            {
                var party = PartyOf(members.Take(n).ToArray());
                guards.Add(ScoutingResolver.CalculateGuardPower(party));
                stealths.Add(ScoutingResolver.CalculateStealthScore(party));
            }

            for (int i = 1; i < 4; i++)
            {
                Assert.True(guards[i] > guards[i - 1], $"護衛力が{i + 1}名で上がっていない: {string.Join(",", guards)}");
                Assert.True(stealths[i] < stealths[i - 1], $"隠密が{i + 1}名で下がっていない: {string.Join(",", stealths)}");
            }
        }

        [Fact]
        public void Resolve_RecordsGuardCarrierValueAndSupport()
        {
            var party = PartyOf(MakeGuard(str: 40, vit: 20, intel: 10), MakeGuard(str: 5, vit: 30, intel: 55));

            var result = new ScoutingResolver(new AlwaysMinRng()).Resolve(party, MakeBoss(floor: 10));

            Assert.Equal(55, result.GuardCarrierValue, precision: 6);
            Assert.Equal("INT", result.GuardCarrierStat);
            Assert.Equal(12, result.GuardSupportPower, precision: 6);
            Assert.Equal(67, result.GuardPower, precision: 6);
        }

        [Theory]
        [InlineData(1.40, GuardTier.Abundant)]
        [InlineData(1.39, GuardTier.Sufficient)]
        [InlineData(1.00, GuardTier.Sufficient)]
        [InlineData(0.99, GuardTier.Marginal)]
        [InlineData(0.70, GuardTier.Marginal)]
        [InlineData(0.69, GuardTier.Deficient)]
        public void ClassifyGuard_UsesCsvThresholds(double ratio, GuardTier expected) =>
            Assert.Equal(expected, ScoutingResolver.ClassifyGuard(ratio));

        [Fact]
        public void RequiredGuardPower_ScalesWithBossFloor()
        {
            Assert.Equal(ScoutingBalance.BaseRequiredGuardPower, ScoutingResolver.RequiredGuardPower(MakeBoss(floor: 10)), precision: 6);
            Assert.Equal(ScoutingBalance.BaseRequiredGuardPower * 3, ScoutingResolver.RequiredGuardPower(MakeBoss(floor: 30)), precision: 6);
        }

        [Fact]
        public void Scouting_GuardTier_Abundant_ProvidesBonusAndZeroDamage()
        {
            // 10Fボス：要求護衛値35。護衛力60（STR）→ 比率1.71（余裕）。INTは大成功域。
            var member = MakeGuard(str: 60, vit: 10, intel: 10);
            var analyst = MakeSpecialist(agiDex: 200, intel: 300);
            var boss = MakeBoss(floor: 10);

            var result = new ScoutingResolver(new AlwaysMaxRng()).Resolve(PartyOf(member, analyst), boss);

            Assert.Equal(GuardTier.Abundant, result.GuardTier);
            Assert.True(result.GuardRatio >= 1.4);
            Assert.Equal(1.25, ScoutingBalance.GuardIntelMultiplierAbundant, precision: 6);
            Assert.Equal(SurveyOutcome.GreatSuccess, result.AnalysisOutcome);
            Assert.Equal(ScoutingBalance.IntelGainGreatSuccess * 1.25, result.IntelGained, precision: 10);
            Assert.Equal(0, result.HpLostByAdventurer[member.Id]);
            Assert.Equal(0, result.HpLostByAdventurer[analyst.Id]);
            Assert.Equal(member.MaxHP, member.CurrentHP);
        }

        [Fact]
        public void Scouting_GuardTier_Deficient_AbortsIntelAndInflictsDamageWithoutDeath()
        {
            // 100Fボス：要求護衛値350。護衛力は最大でも50 → 比率0.14（不足）。
            // INT・隠密がいくら高くても解析成果は0、各員は最大HPの40%を失うが、HP下限1で生存する。
            var healthy = MakeGuard(str: 50, vit: 50, intel: 50);
            var exhausted = MakeGuard(str: 10, vit: 10, intel: 10);
            exhausted.CurrentHP = 3; // 40%の損耗で0以下になる状態
            var boss = MakeBoss(floor: 100, intelRate: 0.3);

            var result = new ScoutingResolver(new AlwaysMinRng()).Resolve(PartyOf(healthy, exhausted), boss);

            Assert.Equal(GuardTier.Deficient, result.GuardTier);
            Assert.True(result.GuardRatio < 0.7);
            Assert.Equal(0.0, result.IntelGained, precision: 10);
            Assert.Equal(0.3, boss.IntelRate, precision: 10);
            Assert.Equal(healthy.MaxHP * ScoutingBalance.GuardHpLossPercentDeficient / 100, result.HpLostByAdventurer[healthy.Id]);
            Assert.Equal(healthy.MaxHP - healthy.MaxHP * 40 / 100, healthy.CurrentHP);
            Assert.Equal(1, exhausted.CurrentHP);                       // HP下限1で生存
            Assert.Equal(InjurySeverity.None, exhausted.Injury);        // 負傷・除籍判定には接続しない
            Assert.False(exhausted.IsRetired);
        }

        [Theory]
        [InlineData(GuardTier.Sufficient, 1.0, 8)]
        [InlineData(GuardTier.Marginal, 0.75, 20)]
        public void GuardEffects_MatchCsv_ForMiddleTiers(GuardTier tier, double intelMultiplier, int hpLossPercent)
        {
            Assert.Equal(intelMultiplier, ScoutingResolver.GuardIntelMultiplier(tier), precision: 6);
            Assert.Equal(hpLossPercent, ScoutingResolver.GuardHpLossPercent(tier));
        }

        [Fact]
        public void Resolve_NeverReducesHpBelowOne_AndNeverInjures()
        {
            // 調査は低リスク経路：瀕死の隊員を送っても死なず、負傷状態にもならない
            //（ここで人を失うと「調べてから挑む」という導線自体が成立しなくなるため）。
            var exhausted = MakeSpecialist(agiDex: 1, intel: 1);
            exhausted.CurrentHP = 1;

            var result = new ScoutingResolver(new AlwaysMaxRng()).Resolve(PartyOf(exhausted), MakeBoss(floor: 9));

            Assert.Equal(1, exhausted.CurrentHP);
            Assert.Equal(InjurySeverity.None, exhausted.Injury);
        }

        [Fact]
        public void Resolve_ThrowsForEmptyParty()
        {
            Assert.Throws<InvalidOperationException>(() =>
                new ScoutingResolver(new AlwaysMinRng()).Resolve(new Party(), MakeBoss()));
        }

        // ---------------- 参謀の作戦分析（→ AdvisorSystem.GetAdvisorSurveyIntelBonus、2026年9月再配線） ----------------

        /// <summary>7能力がすべて statAverage の参謀を作戦資料室に任命した状態。</summary>
        private static GameState StateWithAdvisor(int statAverage)
        {
            var advisor = new Adventurer
            {
                Name = "参謀ガレス",
                STR = statAverage, AGI = statAverage, VIT = statAverage, MND = statAverage,
                DEX = statAverage, LDR = statAverage, INT = statAverage,
            };
            return new GameState { RetiredAdventurers = { advisor }, AssignedAdvisor = advisor.Id };
        }

        [Fact]
        public void Resolve_WithAdvisor_AddsIntelBonus_ProportionalToAdvisorStats()
        {
            // 大成功（0.50）×(1＋参謀ボーナス 50×0.002＝0.10)×護衛「余裕」1.25 ＝ 0.6875。
            var party = PartyOf(MakeSpecialist(agiDex: 60, intel: 60), MakeSpecialist(agiDex: 60, intel: 60));
            var boss = MakeBoss(floor: 1);
            var state = StateWithAdvisor(statAverage: 50);

            var result = new ScoutingResolver(new AlwaysMinRng()).Resolve(party, boss, state);

            Assert.Equal(0.10, result.AdvisorIntelBonus, precision: 10);
            Assert.Equal("参謀ガレス", result.AdvisorName);
            Assert.Equal(ScoutingBalance.IntelGainGreatSuccess * 1.10 * AbundantGuard, result.IntelGained, precision: 10);
        }

        [Fact]
        public void Resolve_WithoutAdvisor_KeepsPreviousCalculation()
        {
            var party = PartyOf(MakeSpecialist(agiDex: 60, intel: 60), MakeSpecialist(agiDex: 60, intel: 60));
            var boss = MakeBoss(floor: 1);
            var stateWithoutAdvisor = new GameState();

            var result = new ScoutingResolver(new AlwaysMinRng()).Resolve(party, boss, stateWithoutAdvisor);

            Assert.Equal(0.0, result.AdvisorIntelBonus, precision: 10);
            Assert.Null(result.AdvisorName);
            Assert.Equal(ScoutingBalance.IntelGainGreatSuccess * AbundantGuard, result.IntelGained, precision: 10);
        }

        [Fact]
        public void Resolve_AdvisorBonus_IsAddedToResearchBonus_NotMultiplied()
        {
            // 研究ボーナス（IntelRateBonus）と参謀ボーナスは加算で合算する：(1＋研究＋参謀)倍。
            var party = PartyOf(MakeSpecialist(agiDex: 60, intel: 60), MakeSpecialist(agiDex: 60, intel: 60));
            var state = StateWithAdvisor(statAverage: 50);
            var research = ResearchBalance.GetAll().First(r => r.EffectType == ResearchEffectType.IntelRateBonus);
            state.CompletedResearchIds.Add(research.Id);
            double researchBonus = ResearchBalance.GetTotalEffectValue(state, ResearchEffectType.IntelRateBonus);

            var result = new ScoutingResolver(new AlwaysMinRng()).Resolve(party, MakeBoss(floor: 1), state);

            Assert.True(researchBonus > 0);
            Assert.Equal(ScoutingBalance.IntelGainGreatSuccess * (1 + researchBonus + 0.10) * AbundantGuard,
                result.IntelGained, precision: 10);
        }
    }
}
