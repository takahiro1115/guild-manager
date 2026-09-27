using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 顔グラフィックのプール（portraits.csv → PortraitBalance）と、採用時の割り当て
    /// （→ RecruitmentSystem.AssignPortrait、03 §2.1・§0.46）のテスト。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~PortraitPool`
    /// </summary>
    public class PortraitPoolTests
    {
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private static readonly string[] Header = { "Id", "Jobs", "HairColor", "EyeColor" };

        // ---------------- portraits.csv ----------------

        [Fact]
        public void PortraitsCsv_LoadsPool()
        {
            // 取り込みで増えていく前提（→ tools/portrait_tool/import_portraits.py）。初期の5枚が残っていることだけを確かめる。
            Assert.True(PortraitBalance.All.Count >= 5);
            foreach (var id in new[] { "adv_001", "adv_002", "adv_003", "adv_004", "adv_005" })
                Assert.NotNull(PortraitBalance.FindById(id));
            var mage = PortraitBalance.FindById("adv_003")!;
            Assert.True(mage.Suits(JobClass.Mage));
            Assert.False(mage.Suits(JobClass.Warrior));
            Assert.Equal("銀", mage.HairColor);
            Assert.Null(PortraitBalance.FindById("claudia")); // 初期メンバーの専用画像はプール外
        }

        [Fact]
        public void EveryJob_HasAtLeastOneSuitingPortrait()
        {
            foreach (var job in Enum.GetValues<JobClass>())
                Assert.Contains(PortraitBalance.All, p => p.Suits(job));
        }

        [Fact]
        public void Parse_AllMeansEveryJob_AndEmptyTableIsAllowed()
        {
            var defs = PortraitBalance.Parse(Header, new[] { new[] { "p1", "All", "", "" } });
            Assert.Equal(Enum.GetValues<JobClass>().Length, defs[0].Jobs.Count);
            Assert.Empty(PortraitBalance.Parse(Header, Array.Empty<string[]>()));
        }

        [Theory]
        [InlineData("", "All")]            // Id空
        [InlineData("a b", "All")]         // ファイル名に使えない文字
        [InlineData("p1", "")]             // Jobs空
        [InlineData("p1", "Warrior|Ninja")] // 不正な職業
        public void Parse_Throws_OnInvalidRow(string id, string jobs)
        {
            Assert.Throws<BalanceDataException>(() =>
                PortraitBalance.Parse(Header, new[] { new[] { id, jobs, "", "" } }));
        }

        [Fact]
        public void Parse_Throws_OnDuplicateIdOrMissingColumn()
        {
            Assert.Throws<BalanceDataException>(() => PortraitBalance.Parse(Header,
                new[] { new[] { "p1", "All", "", "" }, new[] { "p1", "All", "", "" } }));
            Assert.Throws<BalanceDataException>(() => PortraitBalance.Parse(new[] { "Id", "Jobs" }, Array.Empty<string[]>()));
        }

        // ---------------- 採用時の割り当て ----------------

        [Fact]
        public void GenerateCandidates_AssignsSuitingPortraits_WithoutDuplicatesWhilePoolLasts()
        {
            var offers = new RecruitmentSystem(new SeededRng(1), new SeededRng(2)).GenerateCandidates(new GameState(), candidateCount: 5);

            var ids = offers.Select(o => o.Candidate.PortraitId).ToList();
            Assert.All(ids, Assert.NotNull);
            Assert.Equal(5, ids.Distinct().Count()); // プールが5枚以上なら、応募者5名は全員違う顔
            Assert.All(offers, o => Assert.NotNull(PortraitBalance.FindById(o.Candidate.PortraitId)));
        }

        [Fact]
        public void GenerateCandidates_PrefersPortraitSuitingTheJob()
        {
            // AlwaysMinRng：職業は列挙の先頭（Warrior）。Warriorに似合う未使用の先頭は adv_001。
            var candidate = new RecruitmentSystem(new AlwaysMinRng(), new AlwaysMinRng())
                .GenerateCandidates(new GameState(), candidateCount: 1)[0].Candidate;

            Assert.Equal(JobClass.Warrior, candidate.JobClass);
            Assert.Equal("adv_001", candidate.PortraitId);
        }

        [Fact]
        public void GenerateCandidates_SkipsPortraitsUsedByActiveRoster_ButReusesRetiredOnes()
        {
            var state = new GameState
            {
                Adventurers =
                {
                    new Adventurer { Name = "現役", JobClass = JobClass.Warrior, PortraitId = "adv_001" },
                    new Adventurer { Name = "引退", JobClass = JobClass.Knight, PortraitId = "adv_004", IsRetired = true },
                },
            };

            var candidate = new RecruitmentSystem(new AlwaysMinRng(), new AlwaysMinRng())
                .GenerateCandidates(state, candidateCount: 1)[0].Candidate;

            // Warriorに似合う adv_001（使用中）・adv_004（引退者の顔＝再利用可）のうち、未使用の adv_004 が選ばれる。
            Assert.Equal("adv_004", candidate.PortraitId);
        }

        [Fact]
        public void GenerateCandidates_ReusesPortraits_WhenPoolIsExhausted()
        {
            int poolSize = PortraitBalance.All.Count;
            var offers = new RecruitmentSystem(new SeededRng(5), new SeededRng(6))
                .GenerateCandidates(new GameState(), candidateCount: poolSize + 3);

            Assert.All(offers, o => Assert.NotNull(o.Candidate.PortraitId));
            Assert.Equal(poolSize, offers.Select(o => o.Candidate.PortraitId).Distinct().Count());
        }

        [Fact]
        public void PortraitRng_DoesNotChangeStatsOfCandidates()
        {
            // 顔の割り当ては専用の乱数を使うため、能力値の出方は顔用の乱数に左右されない。
            var a = new RecruitmentSystem(new SeededRng(11), new SeededRng(1)).GenerateCandidates(new GameState());
            var b = new RecruitmentSystem(new SeededRng(11), new SeededRng(999)).GenerateCandidates(new GameState());

            Assert.Equal(a.Select(o => (o.Candidate.Name, o.Candidate.JobClass, o.Candidate.TotalPA)),
                         b.Select(o => (o.Candidate.Name, o.Candidate.JobClass, o.Candidate.TotalPA)));
        }

        [Fact]
        public void InitialDraft_AssignsPortraitsToo()
        {
            var draft = new RecruitmentSystem(new SeededRng(3)).StartInitialDraft(new GameState());

            Assert.All(draft.Offers, o => Assert.NotNull(o.Candidate.PortraitId));
        }
    }
}
