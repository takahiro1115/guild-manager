using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests.Systems
{
    /// <summary>
    /// 第1週の新春ドラフト（→ RecruitmentSystem.StartInitialDraft・TryDraftHire、03 §2.4、2026年9月）のテスト。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~RecruitmentDraft`
    /// </summary>
    public class RecruitmentDraftTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        private static GameState NewGame() => new()
        {
            Adventurers = SampleData.CreateStarterAdventurers(),
        };

        [Fact]
        public void CsvValues_AreLoaded()
        {
            Assert.Equal(5, RecruitmentBalance.DraftCandidateCount);
            Assert.Equal(2, RecruitmentBalance.DraftHireCount);
        }

        [Theory]
        [InlineData(0)] // 5人目の職業抽選が最小値に寄る
        [InlineData(1)] // 最大値に寄る
        public void Candidates_AlwaysIncludeMageScholarKnightThief(int rngKind)
        {
            IRng rng = rngKind == 0 ? new AlwaysMinRng() : new AlwaysMaxRng();
            var draft = new RecruitmentSystem(rng).StartInitialDraft(NewGame());

            var jobs = draft.Offers.Select(o => o.Candidate.JobClass).ToList();
            Assert.Equal(5, jobs.Count);
            foreach (var job in new[] { JobClass.Mage, JobClass.Scholar, JobClass.Knight, JobClass.Thief })
                Assert.Contains(job, jobs);
        }

        [Fact]
        public void Candidates_AreAll18_AndFree_AndHaveUniqueNames()
        {
            var state = NewGame();
            var draft = new RecruitmentSystem(new SeededRng(2024)).StartInitialDraft(state);

            Assert.All(draft.Offers, o =>
            {
                Assert.Equal(18, o.Candidate.Age);
                Assert.Equal(AgeBand.Young, o.Candidate.AgeBand);
                Assert.Equal(0, o.SigningBonus);
            });
            var names = draft.Offers.Select(o => o.Candidate.Name).Concat(state.Adventurers.Select(a => a.Name)).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
        }

        [Fact]
        public void DraftHire_CostsNoGold_AndAddsToRoster()
        {
            var state = NewGame();
            state.Gold = 0; // 所持金が無くても採用できる
            var system = new RecruitmentSystem(new AlwaysMinRng());
            var draft = system.StartInitialDraft(state);
            var offer = draft.Offers[0];

            Assert.True(system.TryDraftHire(state, draft, offer));

            Assert.Equal(0, state.Gold);
            Assert.Equal(4, state.Adventurers.Count);
            Assert.Contains(offer.Candidate, state.Adventurers);
            Assert.DoesNotContain(offer, draft.Offers);
            Assert.Equal(1, draft.HiresRemaining);
        }

        [Theory]
        [InlineData(JobClass.Mage, ItemCatalog.MageStaffId, ItemCatalog.RobeId)]
        [InlineData(JobClass.Scholar, ItemCatalog.MageStaffId, ItemCatalog.ScholarCoatId)]
        [InlineData(JobClass.Knight, ItemCatalog.IronSwordId, ItemCatalog.ChainmailId)]
        [InlineData(JobClass.Thief, ItemCatalog.DaggerId, ItemCatalog.LeatherArmorId)]
        public void DraftHire_EquipsJobStarterGear_AtFullHp(JobClass job, string weaponId, string armorId)
        {
            var state = NewGame();
            var system = new RecruitmentSystem(new AlwaysMinRng());
            var draft = system.StartInitialDraft(state);
            var offer = draft.Offers.First(o => o.Candidate.JobClass == job);
            int nakedMaxHp = offer.Candidate.MaxHP;

            Assert.True(system.TryDraftHire(state, draft, offer));

            var a = offer.Candidate;
            Assert.Equal(weaponId, a.EquippedWeaponId);
            Assert.Equal(armorId, a.EquippedArmorId);
            Assert.True(a.EquippedWeapon!.IsAllowedFor(job));
            Assert.True(a.EquippedArmor!.IsAllowedFor(job));
            Assert.True(a.MaxHP > nakedMaxHp); // 防具のHP加算（＋VIT補正）が乗っている
            Assert.Equal(a.MaxHP, a.CurrentHP);
        }

        [Theory]
        [InlineData(JobClass.Warrior, ItemCatalog.IronSwordId, ItemCatalog.LeatherArmorId)]
        [InlineData(JobClass.Knight, ItemCatalog.IronSwordId, ItemCatalog.ChainmailId)]
        [InlineData(JobClass.Ranger, ItemCatalog.DaggerId, ItemCatalog.LeatherArmorId)]
        [InlineData(JobClass.Thief, ItemCatalog.DaggerId, ItemCatalog.LeatherArmorId)]
        [InlineData(JobClass.Mage, ItemCatalog.MageStaffId, ItemCatalog.RobeId)]
        [InlineData(JobClass.Cleric, ItemCatalog.MaceId, ItemCatalog.RobeId)]
        [InlineData(JobClass.Scholar, ItemCatalog.MageStaffId, ItemCatalog.ScholarCoatId)]
        public void StarterEquipment_CoversAllJobs_WithAllowedItems(JobClass job, string weaponId, string armorId)
        {
            Assert.Equal((weaponId, armorId), StarterEquipment.GetLoadout(job));
            Assert.True(ItemCatalog.FindById(weaponId)!.IsAllowedFor(job));
            Assert.True(ItemCatalog.FindById(armorId)!.IsAllowedFor(job));
        }

        [Fact]
        public void AfterTwoHires_DraftIsComplete_AndFurtherHiresAreRejected()
        {
            var state = NewGame();
            var system = new RecruitmentSystem(new AlwaysMinRng());
            var draft = system.StartInitialDraft(state);

            Assert.True(system.TryDraftHire(state, draft, draft.Offers[0]));
            Assert.False(draft.IsComplete);
            Assert.True(system.TryDraftHire(state, draft, draft.Offers[0]));

            Assert.True(draft.IsComplete);
            Assert.Equal(0, draft.HiresRemaining);
            Assert.Equal(5, state.Adventurers.Count); // 初期3名＋2名
            Assert.False(system.TryDraftHire(state, draft, draft.Offers[0]));
            Assert.Equal(5, state.Adventurers.Count);

            // 以後の採用は通常の新春採用試験（2年目以降の年度第1週）だけ。
            Assert.False(system.IsInitialDraftWeek(2));
            Assert.False(system.IsRecruitmentWeek(2));
        }

        [Fact]
        public void DraftHire_RejectsOfferNotInDraft()
        {
            var state = NewGame();
            var system = new RecruitmentSystem(new AlwaysMinRng());
            var draft = system.StartInitialDraft(state);
            var stranger = new RecruitmentOffer(new Adventurer { Name = "部外者", JobClass = JobClass.Mage }, 0);

            Assert.False(system.TryDraftHire(state, draft, stranger));
            Assert.Equal(3, state.Adventurers.Count);
            Assert.Equal(2, draft.HiresRemaining);
        }
    }
}
