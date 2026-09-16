using System;
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
            Assert.Equal(QuestEventOutcome.GreatSuccess, result.AnalysisOutcome);
            Assert.Equal(ScoutingBalance.IntelGainGreatSuccess, result.IntelGained, precision: 10);
            Assert.Equal(ScoutingBalance.IntelGainGreatSuccess, boss.IntelRate, precision: 10);
        }

        [Fact]
        public void Resolve_LowIntParty_StillBringsBackPartialIntel()
        {
            // 解析に失敗しても断片情報は持ち帰れる（調査が完全な無駄骨にはならない）。
            var party = PartyOf(MakeSpecialist(agiDex: 80, intel: 1));
            var boss = MakeBoss(floor: 3);

            var result = new ScoutingResolver(new AlwaysMinRng()).Resolve(party, boss);

            Assert.Equal(QuestEventOutcome.Failure, result.AnalysisOutcome);
            Assert.Equal(ScoutingBalance.IntelGainPartial, result.IntelGained, precision: 10);
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

            // 要求値＝2×18=36。AGI+DEX=17+17=34 では僅かに届かないが、LDR60×0.5=30 を足せば超える。
            var withoutLeader = resolver.Resolve(PartyOf(MakeSpecialist(agiDex: 17, intel: 50, ldr: 0)), boss1);
            var withLeader = resolver.Resolve(PartyOf(MakeSpecialist(agiDex: 17, intel: 50, ldr: 60)), boss2);

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
            Assert.Equal(QuestEventOutcome.Success, result.AnalysisOutcome); // 大成功→成功へ格下げ
            Assert.Equal(ScoutingBalance.IntelGainSuccess, result.IntelGained, precision: 10);
        }

        // ---------------- HP消費（低リスク経路：致死判定に接続しない） ----------------

        [Fact]
        public void Resolve_DiscoveredParty_LosesMoreHpThanStealthyParty()
        {
            var stealthy = MakeSpecialist(agiDex: 90, intel: 50);
            var discovered = MakeSpecialist(agiDex: 1, intel: 50);
            var resolver = new ScoutingResolver(new AlwaysMaxRng());

            var stealthResult = resolver.Resolve(PartyOf(stealthy), MakeBoss(floor: 1));
            var discoveredResult = resolver.Resolve(PartyOf(discovered), MakeBoss(floor: 5));

            Assert.True(stealthResult.StealthSucceeded);
            Assert.False(discoveredResult.StealthSucceeded);
            Assert.True(discoveredResult.HpLostByAdventurer[discovered.Id] > stealthResult.HpLostByAdventurer[stealthy.Id],
                "見つかった調査隊の方がHP消費が大きいはず");
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
    }
}
