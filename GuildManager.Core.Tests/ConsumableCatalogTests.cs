using System.Linq;
using GuildManager.Core.Models;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// パーティ携行アイテム（消耗品）カタログのテスト。
    ///
    /// 2026年9月の棚卸し（→ 03 §0.13）で、旧・通常クエストの環境ギミック相殺アイテム
    /// （聖水・松明・登攀具）と効果アイテム（煙幕弾・高品質傷薬・携帯糧食）を撤去し、
    /// **大迷宮のボスギミック4種に1対1で対応する4アイテムのみ**へ純化した。
    /// 「増やしすぎない」ことを構造として守るためのテスト。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class ConsumableCatalogTests
    {
        [Fact]
        public void GetAll_ContainsExactlyTheFourBossGimmickCounters()
        {
            var ids = ConsumableCatalog.GetAll().Select(i => i.Id).ToArray();

            Assert.Equal(4, ids.Length);
            Assert.Equal(
                new[]
                {
                    ConsumableCatalog.AntidoteId,
                    ConsumableCatalog.AcidFlaskId,
                    ConsumableCatalog.NetId,
                    ConsumableCatalog.CharmId,
                },
                ids);
        }

        [Theory]
        [InlineData(ConsumableCatalog.AntidoteId, "解毒薬", BossGimmickType.Poison)]
        [InlineData(ConsumableCatalog.AcidFlaskId, "溶解液", BossGimmickType.HeavyArmor)]
        [InlineData(ConsumableCatalog.NetId, "捕縛網", BossGimmickType.Flying)]
        [InlineData(ConsumableCatalog.CharmId, "身代わりの護符", BossGimmickType.InstantKill)]
        public void FindById_LoadsEachCounterItem_WithNameGimmickAndPrice(
            string id, string expectedName, BossGimmickType expectedGimmick)
        {
            var item = ConsumableCatalog.FindById(id);

            Assert.NotNull(item);
            Assert.Equal(expectedName, item!.Name);
            Assert.Equal(expectedGimmick, item.TargetGimmick);
            Assert.Equal(ConsumableEffectType.GimmickCounter, item.EffectType);
            Assert.True(item.Price > 0, "価格は consumables.csv から読み込まれる（→ ConsumableBalance）。");
        }

        [Fact]
        public void EveryBossGimmickType_HasExactlyOneCounterItem()
        {
            // ボスギミック4種すべてにアイテム対策口が揃っている（→ 03 §4.5.4）ことを保証する。
            // どちらかにだけ種別を足すと、対策不能なギミックや宙に浮いたアイテムが生まれる。
            foreach (BossGimmickType gimmick in System.Enum.GetValues<BossGimmickType>())
                Assert.Single(ConsumableCatalog.GetAll(), i => i.TargetGimmick == gimmick);
        }

        [Theory]
        [InlineData("HolyWater")]    // 旧・霊体相殺（聖水）
        [InlineData("Torch")]        // 旧・暗黒相殺（松明）
        [InlineData("ClimbingGear")] // 旧・隘路相殺（登攀具）
        [InlineData("SmokeBomb")]
        [InlineData("QualityHealingSalve")]
        [InlineData("TravelRations")]
        public void FindById_ReturnsNull_ForRemovedLegacyItems(string removedId)
        {
            // 撤去済みIDを持つ古いセーブ（Party.ConsumableItemIds）を読んでも、
            // 例外ではなく null が返り、単に「持っていない」扱いになる。
            Assert.Null(ConsumableCatalog.FindById(removedId));
        }

        [Fact]
        public void FindById_ReturnsNull_ForNullId()
        {
            Assert.Null(ConsumableCatalog.FindById(null));
        }
    }
}
