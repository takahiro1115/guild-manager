using System.Linq;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 採用（新春採用試験）システム（仕様書 03 §2.4）のテスト。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class RecruitmentSystemTests
    {
        /// <summary>NextInt(min, max) が常に min を返すテスト用スタブ。</summary>
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        /// <summary>NextInt(min, max) が常に max を返すテスト用スタブ。</summary>
        private class AlwaysMaxRng : IRng
        {
            public int NextInt(int min, int max) => max;
        }

        // ---------------- 新春採用試験の週判定（§2.4「タイミング」） ----------------

        [Theory]
        [InlineData(1, false)]   // 1年目・第1週：初期メンバー配備済みのため対象外
        [InlineData(2, false)]
        [InlineData(48, false)]  // 1年目・年度末
        [InlineData(49, true)]   // 2年目・第1週：最初の採用試験
        [InlineData(96, false)]  // 2年目・年度末
        [InlineData(97, true)]   // 3年目・第1週
        public void IsRecruitmentWeek_OnlyTrueAtWeekOneOfEachYearExceptTheFirst(int weekNumber, bool expected)
        {
            var system = new RecruitmentSystem(new AlwaysMinRng());
            Assert.Equal(expected, system.IsRecruitmentWeek(weekNumber));
        }

        // ---------------- 応募者生成（§2.4「契約年齢」「PA天井は年齢非依存」） ----------------

        [Fact]
        public void GenerateCandidates_ReturnsConfiguredCount()
        {
            var system = new RecruitmentSystem(new AlwaysMinRng());
            var offers = system.GenerateCandidates();
            Assert.Equal(5, offers.Count);
        }

        [Fact]
        public void GenerateCandidates_AgeIsWithinContractRange()
        {
            var minSystem = new RecruitmentSystem(new AlwaysMinRng());
            var maxSystem = new RecruitmentSystem(new AlwaysMaxRng());

            Assert.All(minSystem.GenerateCandidates(), o => Assert.Equal(15, o.Candidate.Age));
            Assert.All(maxSystem.GenerateCandidates(), o => Assert.Equal(18, o.Candidate.Age));
        }

        [Fact]
        public void GenerateCandidates_YoungestCandidate_HasActualStatsFartherBelowPa()
        {
            // 15歳(AlwaysMinRng)は実効値がPAから遠い（伸びしろ最大）、
            // 18歳(AlwaysMaxRng)は実効値がPAに近い（即戦力）（→ 03 §2.4）。
            var young = new RecruitmentSystem(new AlwaysMinRng()).GenerateCandidates()[0].Candidate;
            var old = new RecruitmentSystem(new AlwaysMaxRng()).GenerateCandidates()[0].Candidate;

            double youngGap = young.PA_STR - young.STR;
            double oldGap = old.PA_STR - old.STR;

            Assert.True(youngGap > oldGap,
                $"15歳の実効値とPAの差({youngGap})は18歳の差({oldGap})より大きいはず");
        }

        [Fact]
        public void GenerateCandidates_ActualStatsNeverExceedPa()
        {
            var system = new RecruitmentSystem(new AlwaysMaxRng());
            foreach (var offer in system.GenerateCandidates())
            {
                foreach (var stat in new[] { "STR", "AGI", "VIT", "MND", "DEX", "LDR", "INT" })
                {
                    int actual = stat switch
                    {
                        "STR" => offer.Candidate.STR,
                        "AGI" => offer.Candidate.AGI,
                        "VIT" => offer.Candidate.VIT,
                        "MND" => offer.Candidate.MND,
                        "DEX" => offer.Candidate.DEX,
                        "LDR" => offer.Candidate.LDR,
                        _ => offer.Candidate.INT,
                    };
                    int pa = stat switch
                    {
                        "STR" => offer.Candidate.PA_STR,
                        "AGI" => offer.Candidate.PA_AGI,
                        "VIT" => offer.Candidate.PA_VIT,
                        "MND" => offer.Candidate.PA_MND,
                        "DEX" => offer.Candidate.PA_DEX,
                        "LDR" => offer.Candidate.PA_LDR,
                        _ => offer.Candidate.PA_INT,
                    };
                    Assert.True(actual <= pa, $"{stat}: 実効値({actual})がPA({pa})を超えている");
                }
            }
        }

        [Fact]
        public void GenerateCandidates_StartAtFullHp()
        {
            var system = new RecruitmentSystem(new AlwaysMinRng());
            Assert.All(system.GenerateCandidates(), o => Assert.Equal(o.Candidate.MaxHP, o.Candidate.CurrentHP));
        }

        [Fact]
        public void GenerateCandidates_SigningBonusIsPositive()
        {
            var system = new RecruitmentSystem(new AlwaysMinRng());
            Assert.All(system.GenerateCandidates(), o => Assert.True(o.SigningBonus > 0));
        }

        // ---------------- 雇用枠（§2.4「雇用枠」） ----------------

        [Fact]
        public void GetOpenSlotCount_ReturnsFullCapWhenRosterEmpty()
        {
            var system = new RecruitmentSystem(new AlwaysMinRng());
            var state = new GameState();
            Assert.Equal(8, system.GetOpenSlotCount(state));
        }

        [Fact]
        public void GetOpenSlotCount_ExcludesRetiredAdventurersFromActiveCount()
        {
            var system = new RecruitmentSystem(new AlwaysMinRng());
            var state = new GameState
            {
                Adventurers =
                {
                    new Adventurer(),
                    new Adventurer { IsRetired = true },
                },
            };

            // 現役1名・引退1名 → 引退者は現役枠を占有しないので空きは 8-1=7。
            Assert.Equal(7, system.GetOpenSlotCount(state));
        }

        [Fact]
        public void GetOpenSlotCount_NeverGoesNegative()
        {
            var system = new RecruitmentSystem(new AlwaysMinRng());
            var state = new GameState();
            for (int i = 0; i < 20; i++)
                state.Adventurers.Add(new Adventurer());

            Assert.Equal(0, system.GetOpenSlotCount(state));
        }

        // ---------------- 採用（雇用） ----------------

        [Fact]
        public void TryHire_Succeeds_DeductsGoldAndAddsToRoster()
        {
            var system = new RecruitmentSystem(new AlwaysMinRng());
            var offer = system.GenerateCandidates()[0];
            var state = new GameState { Gold = offer.SigningBonus + 1000 }; // 余裕を持った所持金

            bool result = system.TryHire(state, offer);

            Assert.True(result);
            Assert.Equal(1000, state.Gold);
            Assert.Contains(offer.Candidate, state.Adventurers);
        }

        [Fact]
        public void TryHire_Fails_WhenGoldInsufficient()
        {
            var system = new RecruitmentSystem(new AlwaysMinRng());
            var offer = system.GenerateCandidates()[0];
            var state = new GameState { Gold = offer.SigningBonus - 1 };

            bool result = system.TryHire(state, offer);

            Assert.False(result);
            Assert.Equal(offer.SigningBonus - 1, state.Gold); // 変化しない
            Assert.DoesNotContain(offer.Candidate, state.Adventurers);
        }

        [Fact]
        public void TryHire_Fails_WhenNoOpenSlots()
        {
            var system = new RecruitmentSystem(new AlwaysMinRng());
            var offer = system.GenerateCandidates()[0];
            var state = new GameState { Gold = 999_999 };
            for (int i = 0; i < 8; i++)
                state.Adventurers.Add(new Adventurer());

            bool result = system.TryHire(state, offer);

            Assert.False(result);
            Assert.Equal(999_999, state.Gold); // 変化しない
        }
    }
}
