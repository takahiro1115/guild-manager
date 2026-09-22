using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests.Systems
{
    /// <summary>
    /// 未鑑定遺物の鑑定システム（→ AppraisalSystem、03 §4.7）の単体テスト。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~Appraisal`
    ///
    /// 鑑定結果の種別は「1〜100の乱数」を希少度ごとの比率（武具率／素材率／換金率）と
    /// 比べて決まる（→ AppraisalSystem.RollContents）。そのため、固定値を返す IRng を
    /// 差し替えるだけで「必ず武具」「必ず換金」といった経路を決め打ちで検証できる。
    /// </summary>
    public class AppraisalSystemTests
    {
        /// <summary>常に下限を返す乱数。種別ロールは1になるため、武具率が1以上なら必ず武具が出る。</summary>
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        /// <summary>常に上限を返す乱数。種別ロールは100になるため、必ず換金（最後の区分）が出る。</summary>
        private class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        /// <summary>指定した値を1回目だけ返し、2回目以降は下限を返す乱数（種別ロールだけを狙い撃つ用）。</summary>
        private class FirstRollRng : IRng
        {
            private readonly int _firstRoll;
            private bool _consumed;

            public FirstRollRng(int firstRoll) => _firstRoll = firstRoll;

            public int NextInt(int min, int max)
            {
                if (_consumed) return min;
                _consumed = true;
                return Math.Clamp(_firstRoll, min, max);
            }
        }

        private static UnidentifiedItem MakeRelic(ItemRarity rarity = ItemRarity.Common, string fieldId = "forest", int floor = 1) => new()
        {
            Name = "？？？ テスト用の封印箱",
            Rarity = rarity,
            OriginFieldId = fieldId,
            OriginFloor = floor,
            AppraisalCost = RelicBalance.GetAppraisalCost(rarity),
        };

        /// <summary>
        /// 種別ロールの狙い撃ち。武具区分は必ず先頭（1〜EquipmentRate）なのでロール1で確定し、
        /// 素材区分は武具区分の直後から始まる（→ AppraisalSystem.RollContents）。
        /// </summary>
        private const int EquipmentRoll = 1;

        private static int MaterialRoll(ItemRarity rarity)
        {
            var profile = RelicBalance.Get(rarity);
            return profile.EquipmentRate + 1; // 武具区分の直後＝素材区分の先頭
        }

        // ---------------- 費用の引き落としと在庫の消費（指示書指定テスト） ----------------

        [Fact]
        public void Appraise_DeductsGold_AndRemovesUnidentifiedItem()
        {
            var relic = MakeRelic(ItemRarity.Rare);
            var state = new GameState { Gold = 10000, UnidentifiedItems = { relic } };
            int costBefore = relic.AppraisalCost;

            var result = new AppraisalSystem(new AlwaysMinRng()).Appraise(state, relic.Id);

            Assert.NotNull(result);
            Assert.Equal(costBefore, result!.AppraisalCost);
            Assert.Empty(state.UnidentifiedItems);
            // 換金だった場合は獲得ゴールドが戻るため、「費用が引かれたこと」は
            // 「開始資金 − 費用 + 獲得ゴールド」と一致することで確認する。
            Assert.Equal(10000 - costBefore + result.ResultGold, state.Gold);
        }

        [Fact]
        public void Appraise_CostMatchesRarityTable()
        {
            foreach (var rarity in Enum.GetValues<ItemRarity>())
            {
                var relic = MakeRelic(rarity);
                Assert.Equal(RelicBalance.GetAppraisalCost(rarity), relic.AppraisalCost);
            }

            // 希少度が上がるほど鑑定費用も高くなる（relic.csvの単調性）。
            Assert.True(RelicBalance.GetAppraisalCost(ItemRarity.Common) < RelicBalance.GetAppraisalCost(ItemRarity.Rare));
            Assert.True(RelicBalance.GetAppraisalCost(ItemRarity.Rare) < RelicBalance.GetAppraisalCost(ItemRarity.Epic));
            Assert.True(RelicBalance.GetAppraisalCost(ItemRarity.Epic) < RelicBalance.GetAppraisalCost(ItemRarity.Legendary));
        }

        // ---------------- 武具（指示書指定テスト） ----------------

        [Fact]
        public void Appraise_GeneratesEquipment_AndAddsToArmory()
        {
            var relic = MakeRelic(ItemRarity.Epic);
            var state = new GameState { Gold = 10000, WeekNumber = 31, UnidentifiedItems = { relic } };

            // 種別ロール1 → 必ず武具区分（EquipmentRateは全希少度で1以上）。
            var result = new AppraisalSystem(new FirstRollRng(EquipmentRoll)).Appraise(state, relic.Id);

            Assert.NotNull(result);
            Assert.Equal(AppraisalResultType.Equipment, result!.Type);
            Assert.NotNull(result.ResultEquipment);

            var stored = Assert.Single(state.Armory);
            Assert.Same(result.ResultEquipment, stored);
            Assert.Equal(result.ItemName, stored.Name);
            Assert.Equal(31, stored.AcquiredAtWeek);

            // 保管庫の個体はカタログ定義へ解決できる（→ EquipmentItem.GetDefinition）。
            var definition = stored.GetDefinition();
            Assert.NotNull(definition);
            Assert.Contains(stored.ItemId, RelicBalance.Get(ItemRarity.Epic).EquipmentPool);
        }

        [Fact]
        public void Appraise_Equipment_StacksIndependently_ForSameCatalogItem()
        {
            // 同じカタログIdの武具が2本出ても、別個体として保管庫に2件積まれること
            // （→ EquipmentItem.Id。これが Item（カタログ定義）と分けた理由そのもの）。
            var state = new GameState { Gold = 10000 };
            state.UnidentifiedItems.Add(MakeRelic(ItemRarity.Common));
            state.UnidentifiedItems.Add(MakeRelic(ItemRarity.Common));

            var system = new AppraisalSystem(new FirstRollRng(EquipmentRoll));
            foreach (var id in state.UnidentifiedItems.Select(i => i.Id).ToList())
                Assert.Equal(AppraisalResultType.Equipment, system.Appraise(state, id)!.Type);

            Assert.Equal(2, state.Armory.Count);
            Assert.NotEqual(state.Armory[0].Id, state.Armory[1].Id);
        }

        // ---------------- 素材（指示書指定テスト） ----------------

        [Fact]
        public void Appraise_GeneratesMaterial_AndAddsToMaterials()
        {
            var relic = MakeRelic(ItemRarity.Rare, fieldId: "forest", floor: 1);
            var state = new GameState { Gold = 10000, UnidentifiedItems = { relic } };

            // 種別ロールを素材区分の先頭に合わせる。以降の抽選（素材種・個数）は下限固定。
            var result = new AppraisalSystem(new FirstRollRng(MaterialRoll(ItemRarity.Rare))).Appraise(state, relic.Id);

            Assert.NotNull(result);
            Assert.Equal(AppraisalResultType.Material, result!.Type);
            Assert.NotNull(result.ResultMaterialId);
            Assert.True(result.ResultMaterialCount >= RelicBalance.Get(ItemRarity.Rare).MaterialCountMin);
            Assert.Equal(result.ResultMaterialCount, state.Materials[result.ResultMaterialId!]);

            // 抽選対象は出土地・出土階層に紐づく（→ materials.csv、MaterialBalance）。
            var eligible = MaterialBalance.GetEligibleMaterials("forest", 1).Select(m => m.Id);
            Assert.Contains(result.ResultMaterialId, eligible);
        }

        [Fact]
        public void Appraise_Material_FallsBackToGold_WhenOriginFieldHasNoMaterialTable()
        {
            // materials.csvに行が無いフィールドから出土した遺物は、素材を引けないため換金へ振り替える
            // （遺物が無に帰さないための防御的フォールバック。→ AppraisalSystem.RollContents）。
            var relic = MakeRelic(ItemRarity.Rare, fieldId: "nonexistent_field");
            var state = new GameState { Gold = 10000, UnidentifiedItems = { relic } };

            var result = new AppraisalSystem(new FirstRollRng(MaterialRoll(ItemRarity.Rare))).Appraise(state, relic.Id);

            Assert.NotNull(result);
            Assert.Equal(AppraisalResultType.Gold, result!.Type);
            Assert.Empty(state.Materials);
        }

        // ---------------- 換金 ----------------

        [Fact]
        public void Appraise_GeneratesGold_AndAddsToTreasury()
        {
            var relic = MakeRelic(ItemRarity.Legendary);
            var state = new GameState { Gold = 10000, UnidentifiedItems = { relic } };
            var profile = RelicBalance.Get(ItemRarity.Legendary);

            // 種別ロール100 → 必ず最後の区分（換金）。以降のロールも上限なので獲得額は上限値。
            var result = new AppraisalSystem(new AlwaysMaxRng()).Appraise(state, relic.Id);

            Assert.NotNull(result);
            Assert.Equal(AppraisalResultType.Gold, result!.Type);
            Assert.Equal(profile.GoldRewardMax, result.ResultGold);
            Assert.Equal(10000 - relic.AppraisalCost + profile.GoldRewardMax, state.Gold);
            Assert.Empty(state.Armory);
            Assert.Empty(state.Materials);
        }

        // ---------------- 資金不足（指示書指定テスト） ----------------

        [Fact]
        public void Appraise_Fails_WhenInsufficientGold()
        {
            var relic = MakeRelic(ItemRarity.Legendary);
            var state = new GameState { Gold = relic.AppraisalCost - 1, UnidentifiedItems = { relic } };

            Assert.False(AppraisalSystem.CanAppraise(state, relic.Id));

            var result = new AppraisalSystem(new AlwaysMinRng()).Appraise(state, relic.Id);

            Assert.Null(result);
            Assert.Equal(relic.AppraisalCost - 1, state.Gold); // 1Gも引かれていない
            Assert.Single(state.UnidentifiedItems);            // 在庫も減っていない
            Assert.Empty(state.Armory);
        }

        [Fact]
        public void CanAppraise_IsTrue_AtExactlyTheCost()
        {
            var relic = MakeRelic(ItemRarity.Epic);
            var state = new GameState { Gold = relic.AppraisalCost, UnidentifiedItems = { relic } };

            Assert.True(AppraisalSystem.CanAppraise(state, relic.Id));
        }

        [Fact]
        public void Appraise_ReturnsNull_ForUnknownItemId()
        {
            var state = new GameState { Gold = 10000 };

            Assert.False(AppraisalSystem.CanAppraise(state, Guid.NewGuid().ToString()));
            Assert.Null(new AppraisalSystem(new AlwaysMinRng()).Appraise(state, Guid.NewGuid().ToString()));
        }

        // ---------------- 遺物の生成（希少度ロール） ----------------

        [Fact]
        public void CreateRelic_LowRoll_ShallowFloor_YieldsCommon()
        {
            var relic = AppraisalSystem.CreateRelic(new AlwaysMinRng(), "forest", floor: 1);

            Assert.Equal(ItemRarity.Common, relic.Rarity);
            Assert.Equal("forest", relic.OriginFieldId);
            Assert.Equal(1, relic.OriginFloor);
            Assert.Equal(RelicBalance.GetAppraisalCost(ItemRarity.Common), relic.AppraisalCost);
            Assert.StartsWith("？？？", relic.Name);
        }

        [Fact]
        public void CreateRelic_HighRoll_YieldsLegendary()
        {
            var relic = AppraisalSystem.CreateRelic(new AlwaysMaxRng(), "abyss", floor: 100);

            Assert.Equal(ItemRarity.Legendary, relic.Rarity);
        }

        [Fact]
        public void CreateRelic_DeeperFloor_NeverLowersRarity()
        {
            // 同じロール値（ここでは常に下限＝1）でも、深い階層ほど希少度は下がらない
            // （深度ボーナスは加算のみ。→ RelicBalance.RollRarity）。
            var shallow = AppraisalSystem.CreateRelic(new AlwaysMinRng(), "forest", floor: 1);
            var deep = AppraisalSystem.CreateRelic(new AlwaysMinRng(), "abyss", floor: 100);

            Assert.True(deep.Rarity >= shallow.Rarity);
        }

        [Fact]
        public void CreateRelic_MinimumRarity_RaisesLowRolls()
        {
            var relic = AppraisalSystem.CreateRelic(
                new AlwaysMinRng(), "forest", floor: 1, rollBonus: 0, minimumRarity: ItemRarity.Epic);

            Assert.Equal(ItemRarity.Epic, relic.Rarity);
            Assert.Equal(RelicBalance.GetAppraisalCost(ItemRarity.Epic), relic.AppraisalCost);
        }

        [Fact]
        public void CreateRelic_MinimumRarity_DoesNotLowerHighRolls()
        {
            var relic = AppraisalSystem.CreateRelic(
                new AlwaysMaxRng(), "abyss", floor: 100, rollBonus: 0, minimumRarity: ItemRarity.Rare);

            Assert.Equal(ItemRarity.Legendary, relic.Rarity);
        }

        // ---------------- relic.csv の整合性 ----------------

        [Fact]
        public void RelicBalance_RatesSumTo100_ForEveryRarity()
        {
            foreach (var profile in RelicBalance.GetAll())
                Assert.Equal(100, profile.EquipmentRate + profile.MaterialRate + profile.GoldRate);
        }

        [Fact]
        public void RelicBalance_EquipmentPools_ResolveAgainstItemCatalog()
        {
            foreach (var profile in RelicBalance.GetAll())
            {
                Assert.NotEmpty(profile.EquipmentPool);
                foreach (var itemId in profile.EquipmentPool)
                    Assert.NotNull(ItemCatalog.FindById(itemId));
            }
        }

        [Fact]
        public void RelicBalance_GatheringDropPercent_StaysWithinConfiguredBand()
        {
            Assert.Equal(RelicBalance.GatheringDropBasePct, RelicBalance.GetGatheringDropPercent(0, 1));
            Assert.Equal(RelicBalance.GatheringDropMaxPct, RelicBalance.GetGatheringDropPercent(100000, 100));
            Assert.InRange(
                RelicBalance.GetGatheringDropPercent(150, 40),
                RelicBalance.GatheringDropBasePct, RelicBalance.GatheringDropMaxPct);
        }

        [Fact]
        public void SortForDisplay_PutsRarestFirst()
        {
            var state = new GameState();
            state.UnidentifiedItems.Add(MakeRelic(ItemRarity.Common));
            state.UnidentifiedItems.Add(MakeRelic(ItemRarity.Legendary));
            state.UnidentifiedItems.Add(MakeRelic(ItemRarity.Rare));

            var sorted = AppraisalSystem.SortForDisplay(state);

            Assert.Equal(ItemRarity.Legendary, sorted[0].Rarity);
            Assert.Equal(ItemRarity.Rare, sorted[1].Rarity);
            Assert.Equal(ItemRarity.Common, sorted[2].Rarity);
        }

        // ---------------- 結果サマリー（UI・週報ログの表記） ----------------

        [Fact]
        public void BuildResultSummary_FormatsEachResultType()
        {
            Assert.Equal("鉄の剣（武具）", AppraisalSystem.BuildResultSummary(new AppraisalResult
            {
                ItemName = "鉄の剣", Type = AppraisalResultType.Equipment,
            }));

            Assert.Equal("月光草 ×4（素材）", AppraisalSystem.BuildResultSummary(new AppraisalResult
            {
                ItemName = "月光草", Type = AppraisalResultType.Material, ResultMaterialCount = 4,
            }));

            Assert.Equal("古代硬貨 1200G（換金）", AppraisalSystem.BuildResultSummary(new AppraisalResult
            {
                ItemName = "古代硬貨", Type = AppraisalResultType.Gold, ResultGold = 1200,
            }));
        }

        [Fact]
        public void Appraise_AlwaysProducesFlavorText()
        {
            var relic = MakeRelic(ItemRarity.Legendary);
            var state = new GameState { Gold = 10000, UnidentifiedItems = { relic } };

            var result = new AppraisalSystem(new AlwaysMinRng()).Appraise(state, relic.Id);

            Assert.NotNull(result);
            Assert.False(string.IsNullOrWhiteSpace(result!.FlavorText));
            Assert.Equal(ItemRarity.Legendary, result.Rarity);
        }
    }
}
