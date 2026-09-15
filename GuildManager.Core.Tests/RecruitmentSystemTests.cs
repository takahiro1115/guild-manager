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
    /// 採用（新春採用試験）システム（仕様書 03 §2.4・§7.3）のテスト。
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

        /// <summary>NextInt(min, max) が固定値を [min, max] にクランプして返すテスト用スタブ。</summary>
        private class FixedRng : IRng
        {
            private readonly int _value;
            public FixedRng(int value) => _value = value;
            public int NextInt(int min, int max) => Math.Clamp(_value, min, max);
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

        // ---------------- 第1週チュートリアル採用試験（→ 初期編成改訂仕様） ----------------

        [Fact]
        public void Recruitment_WeekOne_ShouldTriggerRecruitment()
        {
            var system = new RecruitmentSystem(new AlwaysMinRng());

            // 第1週のみチュートリアル採用試験が発生する（通常の新春採用試験＝IsRecruitmentWeekとは
            // 別枠のイベント。IsRecruitmentWeekは1年目を丸ごと対象外にしている）。
            Assert.True(system.IsTutorialRecruitmentWeek(1));
            Assert.False(system.IsTutorialRecruitmentWeek(2));
            Assert.False(system.IsRecruitmentWeek(1));

            var offers = system.GenerateCandidates(new GameState(), candidateCount: RecruitmentBalance.TutorialCandidateCount);

            Assert.Equal(3, offers.Count);
        }

        // ---------------- 応募者生成（§2.4「契約年齢」「PA天井は年齢非依存」） ----------------

        [Fact]
        public void GenerateCandidates_ReturnsConfiguredCount()
        {
            var system = new RecruitmentSystem(new AlwaysMinRng());
            var offers = system.GenerateCandidates(new GameState());
            Assert.Equal(5, offers.Count);
        }

        [Fact]
        public void GenerateCandidates_AgeIsWithinContractRange()
        {
            var minSystem = new RecruitmentSystem(new AlwaysMinRng());
            var maxSystem = new RecruitmentSystem(new AlwaysMaxRng());

            Assert.All(minSystem.GenerateCandidates(new GameState()), o => Assert.Equal(15, o.Candidate.Age));
            Assert.All(maxSystem.GenerateCandidates(new GameState()), o => Assert.Equal(18, o.Candidate.Age));
        }

        [Fact]
        public void GenerateCandidates_YoungestCandidate_HasActualStatsFartherBelowPa()
        {
            // 15歳(AlwaysMinRng)は実効値がPAから遠い（伸びしろ最大）、
            // 18歳(AlwaysMaxRng)は実効値がPAに近い（即戦力）（→ 03 §2.4）。
            var young = new RecruitmentSystem(new AlwaysMinRng()).GenerateCandidates(new GameState())[0].Candidate;
            var old = new RecruitmentSystem(new AlwaysMaxRng()).GenerateCandidates(new GameState())[0].Candidate;

            double youngGap = young.PA_STR - young.STR;
            double oldGap = old.PA_STR - old.STR;

            Assert.True(youngGap > oldGap,
                $"15歳の実効値とPAの差({youngGap})は18歳の差({oldGap})より大きいはず");
        }

        [Fact]
        public void GenerateCandidates_ActualStatsNeverExceedPa()
        {
            var system = new RecruitmentSystem(new AlwaysMaxRng());
            foreach (var offer in system.GenerateCandidates(new GameState()))
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
            Assert.All(system.GenerateCandidates(new GameState()), o => Assert.Equal(o.Candidate.MaxHP, o.Candidate.CurrentHP));
        }

        [Fact]
        public void GenerateCandidates_SigningBonusIsPositive()
        {
            var system = new RecruitmentSystem(new AlwaysMinRng());
            Assert.All(system.GenerateCandidates(new GameState()), o => Assert.True(o.SigningBonus > 0));
        }

        // ---------------- 有望新人（総合PA75以上）の出現判定（→ 03 §7.3） ----------------

        [Fact]
        public void GenerateCandidates_UsesNormalPaRange_WhenHighPotentialRollFails()
        {
            // roll=30固定。基礎出現率25%（閾値25）を上回るため、通常レンジ(40〜80)で生成される。
            // FixedRng(30)は年齢ロールもclamp(30,15,18)=18(ageBonus=0)にする。
            var system = new RecruitmentSystem(new FixedRng(30));

            var candidate = system.GenerateCandidates(new GameState())[0].Candidate;

            Assert.Equal(40, candidate.PA_STR); // 通常レンジの下限(40)にclampされる
        }

        [Fact]
        public void GenerateCandidates_UsesHighPotentialPaRange_WhenScoutMasterBonusPushesRollUnderThreshold()
        {
            // 同じroll=30でも、スカウト顧問ボーナスで出現率が30%超になれば「有望新人」判定域に入り、
            // PA生成レンジが底上げされる(70〜100)。
            var system = new RecruitmentSystem(new FixedRng(30));

            var candidate = system.GenerateCandidates(new GameState(), scoutMasterBonus: 0.10)[0].Candidate;

            Assert.Equal(70, candidate.PA_STR); // 有望新人レンジの下限(70)にclampされる
        }

        [Fact]
        public void GenerateCandidates_DefaultsToNoScoutMasterBonus_WhenArgumentOmitted()
        {
            // 既存の呼び出し（引数省略）と同じ結果になることを確認（後方互換）。
            var withDefault = new RecruitmentSystem(new FixedRng(30)).GenerateCandidates(new GameState());
            var withExplicitZero = new RecruitmentSystem(new FixedRng(30)).GenerateCandidates(new GameState(), 0);

            Assert.Equal(withExplicitZero[0].Candidate.PA_STR, withDefault[0].Candidate.PA_STR);
        }

        // ---------------- 先天特性の付与判定（→ 03 §5.3.2、v1.4改訂） ----------------

        [Fact]
        public void GenerateCandidates_GrantsAllThreeInnateTraits_WhenRollsAlwaysSucceed()
        {
            // AlwaysMinRngはNextInt(1,100)=1を返す。付与率は仮値5%なので1<=5は必ず成功する。
            var system = new RecruitmentSystem(new AlwaysMinRng());

            var candidate = system.GenerateCandidates(new GameState())[0].Candidate;

            Assert.True(candidate.HasTrait(TraitCatalog.BraveId));
            Assert.True(candidate.HasTrait(TraitCatalog.AttentiveId));
            Assert.True(candidate.HasTrait(TraitCatalog.BeautifulId));
        }

        [Fact]
        public void GenerateCandidates_GrantsNoInnateTraits_WhenRollsAlwaysFail()
        {
            // AlwaysMaxRngはNextInt(1,100)=100を返す。付与率5%を上回るため必ず失敗する。
            var system = new RecruitmentSystem(new AlwaysMaxRng());

            var candidate = system.GenerateCandidates(new GameState())[0].Candidate;

            Assert.False(candidate.HasTrait(TraitCatalog.BraveId));
            Assert.False(candidate.HasTrait(TraitCatalog.AttentiveId));
            Assert.False(candidate.HasTrait(TraitCatalog.BeautifulId));
            Assert.Empty(candidate.TraitIds);
        }

        // ---------------- 氏名ジェネレーター連携（→ 03 §2.4、v1.8改訂） ----------------

        [Fact]
        public void GenerateCandidates_SetsGenderOnCandidate()
        {
            // 世界観設定（女性限定ギルド）：MaleGenderChancePercent=0のため、
            // AlwaysMinRngはRollGenderAndCultureで常に(Female, Western)を返す。
            var system = new RecruitmentSystem(new AlwaysMinRng());

            var candidate = system.GenerateCandidates(new GameState())[0].Candidate;

            Assert.Equal(Gender.Female, candidate.Gender);
        }

        [Fact]
        public void GenerateCandidates_AllCandidatesAreFemale()
        {
            // 女性限定ギルド仕様：応募者全員がGender.Femaleであること（性別分布によらず、
            // どの乱数を引いても男性が出現しない）。
            var system = new RecruitmentSystem(new SeededRng(999));

            var offers = system.GenerateCandidates(new GameState());

            Assert.All(offers, o => Assert.Equal(Gender.Female, o.Candidate.Gender));
        }

        [Fact]
        public void GenerateCandidates_AvoidsDuplicateName_WithExistingActiveRoster()
        {
            // AlwaysMinRngは常に同じ（西洋女性名プール先頭の。→ 女性限定ギルド仕様で
            // MaleGenderChancePercent=0になったため、参照するプールが変わった）名前を引こうとする。
            // 現役ロースターに既にその名前を持つ人物がいる場合、生成される候補の名前は
            // 重複回避ロジックによりリトライ後のフォールバック名（末尾に"2世"）になるはず
            // （→ NameGenerator.GenerateUniqueFirstName）。これにより、氏名ジェネレーターが
            // 実際に state.Adventurers（現役ロースター）を参照していることを確認する。
            var existingName = "セリア"; // WesternFemaleNames[0]（AlwaysMinRngが必ず引く名前）
            var state = new GameState { Adventurers = { new Adventurer { Name = existingName } } };
            var system = new RecruitmentSystem(new AlwaysMinRng());

            var candidate = system.GenerateCandidates(state)[0].Candidate;

            Assert.Equal($"{existingName}2世", candidate.Name);
        }

        [Fact]
        public void GenerateCandidates_AvoidsDuplicateNames_AmongCandidatesInSameBatch()
        {
            // 同じ採用試験内（1回のGenerateCandidates呼び出し）で生成される5名同士も、
            // 名前が重複しないこと（現役ロースターが空でも、バッチ内の既生成分を
            // existingNamesに逐次追加していることの確認）。ロールにばらつきが出るよう
            // 実際の乱数実装（SeededRng）を使う（AlwaysMinRng等の固定値スタブでは
            // 常に同じ名前しか引けず、この検証にならないため）。
            var system = new RecruitmentSystem(new SeededRng(12345));

            var offers = system.GenerateCandidates(new GameState());
            var names = offers.Select(o => o.Candidate.Name).ToList();

            Assert.Equal(names.Count, names.Distinct().Count());
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
            var offer = system.GenerateCandidates(new GameState())[0];
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
            var offer = system.GenerateCandidates(new GameState())[0];
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
            var offer = system.GenerateCandidates(new GameState())[0];
            var state = new GameState { Gold = 999_999 };
            for (int i = 0; i < 8; i++)
                state.Adventurers.Add(new Adventurer());

            bool result = system.TryHire(state, offer);

            Assert.False(result);
            Assert.Equal(999_999, state.Gold); // 変化しない
        }
    }
}
