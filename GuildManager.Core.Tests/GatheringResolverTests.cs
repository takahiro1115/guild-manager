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

            // AlwaysMinRng：NextInt(min,max)は常にminを返す → 抽選対象一覧の先頭（0番目）が選ばれる。
            // 到達階層1では変異胞子（MinFloor=5）はまだ対象外なので、月光草・霊樹の枝の2種のみが対象。
            var result = new GatheringResolver(new AlwaysMinRng()).Resolve(party, MakeField("forest", reachedFloor: 1));
            Assert.Equal(MaterialIds.ForestHerb, result.MaterialId);

            // 到達階層5以上になると変異胞子も抽選対象に加わる（MinFloorのみで判定、→ MaterialDefinition）。
            var deepEligible = MaterialBalance.GetEligibleMaterials("forest", reachedFloor: 5);
            Assert.Equal(3, deepEligible.Count);
        }

        [Fact]
        public void GatheringResolver_UndefinedField_YieldsNoMaterial()
        {
            // materials.csvに行が無いフィールドId（5フィールドすべてに素材定義が揃った後も、
            // 将来追加されうる未知のフィールドを想定）では空文字が返り、素材は抽選されない
            // （→ MaterialBalance.GetEligibleMaterials、防御的フォールバック）。
            var party = PartyOf(MakeAdventurer(agiDex: 40, ldr: 20));

            var result = new GatheringResolver(new AlwaysMinRng()).Resolve(party, MakeField("nonexistent_field"));

            Assert.Equal("", result.MaterialId);
        }

        [Theory]
        [InlineData("cave", "mat_cave_moss")]
        [InlineData("ruins", "mat_ruins_scrap")]
        [InlineData("canyon", "mat_canyon_ash")]
        [InlineData("abyss", "mat_abyss_dust")]
        public void GatheringResolver_ExtractsMaterials_ForNewFields(string fieldId, string expectedShallowMaterialId)
        {
            // 洞窟・廃墟・峡谷・深淵の各フィールドでも採取任務が成立し、materials.csvで定義した
            // 素材を獲得できること（2026年9月、候補Aの4フィールド拡張）。浅い到達階層（1）では
            // 各フィールドの2種のうちMinFloorが低い方（一覧の先頭、AlwaysMinRngで必ず選ばれる）
            // だけが対象になる。
            var party = PartyOf(MakeAdventurer(agiDex: 40, ldr: 20));

            var result = new GatheringResolver(new AlwaysMinRng()).Resolve(party, MakeField(fieldId, reachedFloor: 1));

            Assert.Equal(expectedShallowMaterialId, result.MaterialId);
            Assert.True(result.MaterialCount >= 1);
        }

        [Fact]
        public void GatheringResolver_EmptyParty_Throws()
        {
            var resolver = new GatheringResolver(new AlwaysMinRng());
            Assert.Throws<System.InvalidOperationException>(() => resolver.Resolve(new Party(), MakeField()));
        }

        // ---------------- スコアへの盗賊・斥候ボーナス／GameStateへの反映（指示書指定テスト） ----------------

        [Fact]
        public void GatheringResolver_CalculatesYield_BasedOnStatsAndClassBonus()
        {
            var warrior = MakeAdventurer(agiDex: 40, ldr: 20); // JobClassの既定値（Warrior）のまま
            var thief = MakeAdventurer(agiDex: 40, ldr: 20);
            thief.JobClass = JobClass.Thief;

            double warriorScore = GatheringResolver.CalculateGatheringScore(PartyOf(warrior));
            double thiefScore = GatheringResolver.CalculateGatheringScore(PartyOf(thief));

            Assert.Equal(warriorScore + GatheringBalance.ThiefRangerScoreBonus, thiefScore, precision: 6);

            var warriorResult = new GatheringResolver(new AlwaysMinRng()).Resolve(PartyOf(warrior), MakeField(reachedFloor: 1));
            var thiefResult = new GatheringResolver(new AlwaysMinRng()).Resolve(PartyOf(thief), MakeField(reachedFloor: 1));

            Assert.True(thiefResult.MaterialCount >= warriorResult.MaterialCount,
                "盗賊・斥候ボーナスにより採取スコアが上がるため、獲得数も同等以上になるはず");
        }

        // ---------------- 未鑑定遺物のドロップ（→ 03 §4.7、指示書指定テスト） ----------------

        [Fact]
        public void Gathering_CanDrop_UnidentifiedItem()
        {
            // AlwaysMinRng：ドロップ判定のNextInt(1,100)が常に1を返すため、ドロップ確率
            // （基礎5%〜上限15%）を必ず下回り、遺物が出土する。
            var member = MakeAdventurer(agiDex: 40, ldr: 20);
            var field = MakeField("forest", reachedFloor: 1);
            var state = new GameState { Adventurers = { member }, DungeonFields = { field } };

            var dungeon = new DungeonExpeditionSystem(
                new ScoutingResolver(new AlwaysMinRng()),
                new DungeonResolver(new AlwaysMinRng()),
                new SatisfactionSystem(),
                new CompatibilitySystem(new AlwaysMinRng()),
                gatheringResolver: new GatheringResolver(new AlwaysMinRng()));

            Assert.True(dungeon.TryDispatchGathering(state, PartyOf(member), field));
            var resolution = Assert.Single(dungeon.ProcessWeeklyMissions(state));

            var relic = Assert.Single(state.UnidentifiedItems);
            Assert.Same(relic, resolution.GatheringResult!.UnidentifiedItemFound);
            Assert.Same(relic, Assert.Single(resolution.RelicsFound));
            Assert.Equal("forest", relic.OriginFieldId);
            Assert.Equal(1, relic.OriginFloor);
            Assert.True(relic.AppraisalCost > 0);
            Assert.StartsWith("？？？", relic.Name);
        }

        [Fact]
        public void Gathering_DoesNotDropRelic_WhenRollExceedsDropRate()
        {
            // AlwaysMaxRng：ドロップ判定が常に100を返すため、上限15%を超えて必ず外れる。
            var member = MakeAdventurer(agiDex: 40, ldr: 20);
            var field = MakeField("forest", reachedFloor: 1);

            var result = new GatheringResolver(new AlwaysMaxRng()).Resolve(PartyOf(member), field);

            Assert.Null(result.UnidentifiedItemFound);
        }

        [Fact]
        public void Gathering_RelicDropChance_RisesWithScoreAndDepth()
        {
            // ドロップ確率は採取スコア（DEX/AGI/隊長LDR由来）と到達階層で伸びる
            // （→ RelicBalance.GetGatheringDropPercent）。
            double weakScore = GatheringResolver.CalculateGatheringScore(PartyOf(MakeAdventurer(agiDex: 5)));
            double strongScore = GatheringResolver.CalculateGatheringScore(PartyOf(MakeAdventurer(agiDex: 200, ldr: 100)));

            int shallowWeak = RelicBalance.GetGatheringDropPercent(weakScore, 1);
            int deepStrong = RelicBalance.GetGatheringDropPercent(strongScore, 80);

            Assert.Equal(RelicBalance.GatheringDropBasePct, shallowWeak);
            Assert.True(deepStrong > shallowWeak);
            Assert.True(deepStrong <= RelicBalance.GatheringDropMaxPct);
        }

        [Fact]
        public void Gathering_AddsMaterials_ToGameState()
        {
            var member = MakeAdventurer(agiDex: 40, ldr: 20);
            var field = MakeField("forest", reachedFloor: 1);
            var state = new GameState { Adventurers = { member }, DungeonFields = { field } };

            var dungeon = new DungeonExpeditionSystem(
                new ScoutingResolver(new AlwaysMinRng()),
                new DungeonResolver(new AlwaysMinRng()),
                new SatisfactionSystem(),
                new CompatibilitySystem(new AlwaysMinRng()),
                gatheringResolver: new GatheringResolver(new AlwaysMinRng()));

            Assert.True(dungeon.TryDispatchGathering(state, PartyOf(member), field));
            var resolutions = dungeon.ProcessWeeklyMissions(state);

            var gathering = Assert.Single(resolutions).GatheringResult!;
            Assert.NotEmpty(gathering.MaterialId);
            Assert.True(state.Materials.ContainsKey(gathering.MaterialId));
            Assert.Equal(gathering.MaterialCount, state.Materials[gathering.MaterialId]);
        }
    }
}
