using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 特性スロット自由枠（最大5枠）と障害特性の恒久占有（→ Adventurer.CanAddTrait／CanRemoveTrait／
    /// TryRemoveTrait／TryReplaceTrait、03 §5.3、2026年9月）、および trait.csv 由来の特性定義のテスト。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~TraitSlot`
    /// </summary>
    public class TraitSlotTests
    {
        private static Adventurer WithTraits(params string[] traitIds)
        {
            var a = new Adventurer();
            foreach (var id in traitIds) Assert.True(a.TryAddTrait(id));
            return a;
        }

        private static Adventurer Full() => WithTraits(
            TraitCatalog.OldWoundId, TraitCatalog.BraveId, TraitCatalog.AttentiveId,
            TraitCatalog.CountryBredId, TraitCatalog.MentorId);

        // ---------------- スロット上限・重複 ----------------

        [Fact]
        public void MaxTraitCount_IsFive()
        {
            Assert.Equal(5, Adventurer.MaxTraitCount);
        }

        [Fact]
        public void FullSlots_RejectSixthTrait()
        {
            var a = Full();

            Assert.False(a.CanAddTrait(TraitCatalog.DiligentId));
            Assert.False(a.TryAddTrait(TraitCatalog.DiligentId));
            Assert.Equal(5, a.TraitIds.Count);
            Assert.DoesNotContain(TraitCatalog.DiligentId, a.TraitIds);
        }

        [Fact]
        public void DuplicateTrait_IsRejected()
        {
            var a = WithTraits(TraitCatalog.BraveId);

            Assert.False(a.CanAddTrait(TraitCatalog.BraveId));
            Assert.False(a.TryAddTrait(TraitCatalog.BraveId));
            Assert.Single(a.TraitIds);
        }

        [Fact]
        public void CanAddTrait_True_WhenSlotFreeAndNotHeld()
        {
            var a = WithTraits(TraitCatalog.BraveId);

            Assert.True(a.CanAddTrait(TraitCatalog.GiantHunterId));
        }

        // ---------------- 忘却（削除） ----------------

        [Fact]
        public void NormalTrait_CanBeRemoved()
        {
            var a = WithTraits(TraitCatalog.BraveId, TraitCatalog.AttentiveId);

            Assert.True(a.CanRemoveTrait(TraitCatalog.BraveId));
            Assert.True(a.TryRemoveTrait(TraitCatalog.BraveId));
            Assert.Equal(new[] { TraitCatalog.AttentiveId }, a.TraitIds);
        }

        [Theory]
        [InlineData(TraitCatalog.OldWoundId)]
        [InlineData(TraitCatalog.TraumaId)]
        public void InjuryTrait_CannotBeRemoved(string injuryId)
        {
            var a = WithTraits(injuryId, TraitCatalog.BraveId);

            Assert.False(a.CanRemoveTrait(injuryId));
            Assert.False(a.TryRemoveTrait(injuryId));
            Assert.Contains(injuryId, a.TraitIds);
        }

        [Fact]
        public void CanRemoveTrait_False_WhenNotHeld()
        {
            var a = WithTraits(TraitCatalog.BraveId);

            Assert.False(a.CanRemoveTrait(TraitCatalog.AttentiveId));
            Assert.False(a.TryRemoveTrait(TraitCatalog.AttentiveId));
        }

        // ---------------- 入替（上書き） ----------------

        [Fact]
        public void ReplaceNormalTrait_Succeeds_EvenWhenSlotsAreFull_AndKeepsPosition()
        {
            var a = Full(); // [古傷, 豪胆, 注意深い, 田舎育ち, 師匠肌]

            Assert.True(a.TryReplaceTrait(TraitCatalog.AttentiveId, TraitCatalog.NightVisionId));

            Assert.Equal(5, a.TraitIds.Count);
            Assert.Equal(TraitCatalog.NightVisionId, a.TraitIds[2]);
            Assert.DoesNotContain(TraitCatalog.AttentiveId, a.TraitIds);
        }

        [Theory]
        [InlineData(TraitCatalog.OldWoundId)]
        [InlineData(TraitCatalog.TraumaId)]
        public void ReplaceInjuryTrait_Fails(string injuryId)
        {
            var a = WithTraits(injuryId, TraitCatalog.BraveId);

            Assert.False(a.TryReplaceTrait(injuryId, TraitCatalog.ResistPoisonId));

            Assert.Equal(new[] { injuryId, TraitCatalog.BraveId }, a.TraitIds);
        }

        [Fact]
        public void Replace_Fails_WhenNewTraitAlreadyHeld_OrSameId()
        {
            var a = WithTraits(TraitCatalog.BraveId, TraitCatalog.AttentiveId);

            Assert.False(a.TryReplaceTrait(TraitCatalog.BraveId, TraitCatalog.AttentiveId));
            Assert.False(a.TryReplaceTrait(TraitCatalog.BraveId, TraitCatalog.BraveId));
            Assert.Equal(new[] { TraitCatalog.BraveId, TraitCatalog.AttentiveId }, a.TraitIds);
        }

        [Fact]
        public void Replace_Fails_WhenOldTraitNotHeld()
        {
            var a = WithTraits(TraitCatalog.BraveId);

            Assert.False(a.TryReplaceTrait(TraitCatalog.AttentiveId, TraitCatalog.DiligentId));
            Assert.Equal(new[] { TraitCatalog.BraveId }, a.TraitIds);
        }

        // ---------------- trait.csv 由来の定義 ----------------

        [Theory]
        [InlineData(TraitCatalog.ResistPoisonId, "耐毒体質", "猛毒・瘴気フロアでの被弾損耗を大幅に軽減する")]
        [InlineData(TraitCatalog.NightVisionId, "夜目", "暗黒・濃霧フロアの踏破時、未踏破区間の進行損耗を抑制する")]
        [InlineData(TraitCatalog.GiantHunterId, "巨獣狩り", "大型ボスに対する討伐能力を高める")]
        [InlineData(TraitCatalog.DiligentId, "勤勉", "訓練場に配置された際の能力成長判定確率が上昇する")]
        public void NewTraits_AreLoadedFromTraitCsv(string id, string displayName, string description)
        {
            var text = TraitBalance.GetDefinitionText(id);
            Assert.Equal(displayName, text.DisplayName);
            Assert.Equal(description, text.Description);
            Assert.False(text.IsCurseOrInjury);

            var def = TraitCatalog.FindById(id)!;
            Assert.Equal(displayName, def.DisplayName);
            Assert.Equal(description, def.Description);
            Assert.False(def.IsCurseOrInjury);
        }

        [Theory]
        [InlineData(TraitCatalog.OldWoundId, "古傷", true)]
        [InlineData(TraitCatalog.TraumaId, "トラウマ", true)]
        [InlineData(TraitCatalog.BraveId, "豪胆", false)]
        [InlineData(TraitCatalog.AttentiveId, "注意深い", false)]
        [InlineData(TraitCatalog.BeautifulId, "容姿秀麗", false)]
        [InlineData(TraitCatalog.CountryBredId, "田舎育ち", false)]
        [InlineData(TraitCatalog.ScholarId, "知識人", false)]
        [InlineData(TraitCatalog.MentorId, "師匠肌", false)]
        public void ExistingTraits_KeepNames_AndOnlyInjuriesAreCurses(string id, string displayName, bool isCurse)
        {
            var def = TraitCatalog.FindById(id)!;

            Assert.Equal(displayName, def.DisplayName);
            Assert.Equal(isCurse, def.IsCurseOrInjury);
            Assert.False(string.IsNullOrWhiteSpace(def.Description));
        }

        [Fact]
        public void Catalog_ListsAllTwelveTraits_FindableById()
        {
            var all = TraitCatalog.GetAll();

            Assert.Equal(12, all.Count);
            Assert.Equal(all.Count, all.Select(t => t.Id).Distinct().Count());
            Assert.All(all, t => Assert.Same(t, TraitCatalog.FindById(t.Id)));
        }

        [Fact]
        public void GetDefinitionText_UnknownTrait_Throws()
        {
            Assert.Throws<BalanceDataException>(() => TraitBalance.GetDefinitionText("__NoSuchTrait__"));
        }
    }
}
