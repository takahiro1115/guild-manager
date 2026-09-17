using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 大迷宮「探索（採取）」（→ GatheringResolver、第3の任務）の単体テスト。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~Gathering`
    /// </summary>
    public class GatheringResolverTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        private static Adventurer MakeAdventurer(int agiDex, int ldr = 0)
        {
            var a = new Adventurer { STR = 10, AGI = agiDex, VIT = 30, MND = 10, DEX = agiDex, LDR = ldr, INT = 10 };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            return party;
        }

        private static DungeonField MakeField(string id = "forest", int reachedFloor = 1) =>
            new() { Id = id, Name = "テスト用フィールド", Order = 1, IsUnlocked = true, ReachedFloor = reachedFloor };

        // ---------------- 採取スコアの式 ----------------

        [Fact]
        public void CalculateGatheringScore_MatchesFormula()
        {
            var member = MakeAdventurer(agiDex: 40, ldr: 20);
            var party = PartyOf(member);

            // (AGI40×1.0 + DEX40×1.2 + 隊長LDR20×0.5) × HP比率1.0 = (40+48+10)×1.0 = 98
            Assert.Equal(98, GatheringResolver.CalculateGatheringScore(party), precision: 6);
            Assert.Equal(0, GatheringResolver.CalculateGatheringScore(new Party()));
        }

        [Fact]
        public void CalculateGatheringScore_ScalesWithHpRatio()
        {
            var member = MakeAdventurer(agiDex: 40, ldr: 20);
            member.CurrentHP = member.MaxHP / 2; // HP比率0.5相当（丸めの影響を避けるため単純に半分にする）
            var party = PartyOf(member);

            double fullScore = 98; // 上のテストと同じ98が満タン時の値
            double actual = GatheringResolver.CalculateGatheringScore(party);

            Assert.True(actual < fullScore, "HPが減っていれば採取スコアも下がるはず");
            Assert.True(actual > 0);
        }

        // ---------------- 獲得数・ゴールド・HP消費 ----------------

        [Fact]
        public void GatheringResolver_HighDexAgi_YieldsMoreMaterials()
        {
            var strongParty = PartyOf(MakeAdventurer(agiDex: 200, ldr: 100));
            var weakParty = PartyOf(MakeAdventurer(agiDex: 5, ldr: 0));
            var field = MakeField(reachedFloor: 1);

            var strongResult = new GatheringResolver(new AlwaysMinRng()).Resolve(strongParty, field);
            var weakResult = new GatheringResolver(new AlwaysMinRng()).Resolve(weakParty, field);

            Assert.True(strongResult.MaterialCount > weakResult.MaterialCount,
                "高AGI/DEX部隊は低能力部隊より多くの素材を獲得するはず");
            Assert.True(strongResult.GoldEarned > weakResult.GoldEarned);
        }

        [Fact]
        public void GatheringResolver_WeakParty_StillYieldsAtLeastOneMaterial()
        {
            var party = PartyOf(MakeAdventurer(agiDex: 1));
            var field = MakeField(reachedFloor: 1);

            var result = new GatheringResolver(new AlwaysMinRng()).Resolve(party, field);

            Assert.True(result.MaterialCount >= 1, "採取スコアが低くても最低1個は持ち帰れるはず（Math.Max(1, ...)）");
        }

        [Fact]
        public void GatheringResolver_DeeperField_YieldsMoreMaterials()
        {
            var party = PartyOf(MakeAdventurer(agiDex: 40, ldr: 20));

            var shallow = new GatheringResolver(new AlwaysMinRng()).Resolve(party, MakeField(reachedFloor: 1));
            var deep = new GatheringResolver(new AlwaysMinRng()).Resolve(party, MakeField(reachedFloor: 50));

            Assert.True(deep.MaterialCount > shallow.MaterialCount,
                "到達階層が深いほど獲得数ボーナスが乗るはず（→ GatheringBalance.ReachedFloorDivisor）");
        }

        [Fact]
        public void GatheringResolver_NeverKillsAdventurer()
        {
            var member = MakeAdventurer(agiDex: 40);
            member.CurrentHP = 1; // 既に瀕死
            var party = PartyOf(member);
            var field = MakeField();

            // HP消費率が最大でも、下限1でクランプされ0以下にはならない。
            var result = new GatheringResolver(new AlwaysMaxRng()).Resolve(party, field);

            Assert.Equal(1, member.CurrentHP);
            Assert.Equal(0, result.HpLostByAdventurer[member.Id]);
        }

        [Fact]
        public void GatheringResolver_HpLoss_StaysWithinConfiguredRange()
        {
            var member = MakeAdventurer(agiDex: 40);
            var party = PartyOf(member);
            var field = MakeField();

            var result = new GatheringResolver(new AlwaysMaxRng()).Resolve(party, field);

            int expectedMaxLoss = member.MaxHP * GatheringBalance.HpLossPctMax / 100;
            Assert.InRange(result.HpLostByAdventurer[member.Id], 0, expectedMaxLoss);
        }

        // ---------------- フィールド固有の素材抽選 ----------------

        [Fact]
        public void GatheringResolver_RollsMaterial_FromFieldSpecificTable()
        {
            var party = PartyOf(MakeAdventurer(agiDex: 40, ldr: 20));

            // AlwaysMinRng：NextInt(1,100)は常に1を返す → 各フィールドの抽選表で最初の（＝最も出現率が
            // 高い）素材が選ばれる。
            var forestResult = new GatheringResolver(new AlwaysMinRng()).Resolve(party, MakeField("forest"));
            Assert.Equal(MaterialCatalog.HerbMoonlightId, forestResult.MaterialId);

            var caveResult = new GatheringResolver(new AlwaysMinRng()).Resolve(PartyOf(MakeAdventurer(40, 20)), MakeField("cave"));
            Assert.Equal(MaterialCatalog.MossLuminousId, caveResult.MaterialId);
        }

        [Fact]
        public void GatheringResolver_EmptyParty_Throws()
        {
            var resolver = new GatheringResolver(new AlwaysMinRng());
            Assert.Throws<System.InvalidOperationException>(() => resolver.Resolve(new Party(), MakeField()));
        }
    }
}
