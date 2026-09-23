using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests.Balance
{
    /// <summary>
    /// 隠密適性の重み・補正（→ scouting.csv の Stealth_*、03 §4.5.3）と、
    /// それを使う隠密計算（→ ScoutingResolver.CalculateStealthScore）のテスト。
    /// 実行方法: このフォルダで `dotnet test --filter FullyQualifiedName~Scouting`
    ///
    /// 実効隠密＝max(0, floor(基礎隠密 × 人数倍率) − 重装ペナルティ合計)。
    /// 基礎隠密＝Σ(AGI×1.0＋DEX×1.0) ＋ 部隊長LDR×0.5 ＋ 専門職ボーナス合計。
    /// </summary>
    public class ScoutingBalanceTests
    {
        private static Adventurer Make(JobClass job, int agiDex, int ldr = 0)
        {
            var a = new Adventurer
            {
                Name = job.ToString(), JobClass = job,
                STR = 10, AGI = agiDex, VIT = 30, MND = 10, DEX = agiDex, LDR = ldr, INT = 10,
            };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        /// <summary>隠密ペナルティの対象にならない職（学者）を既定にする。</summary>
        private static Adventurer MakeLight(int agiDex, int ldr = 0) => Make(JobClass.Scholar, agiDex, ldr);

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            return party;
        }

        // ---------------- CSVの値 ----------------

        [Fact]
        public void StealthKeys_AreLoadedFromCsv()
        {
            Assert.Equal(1.0, ScoutingBalance.StealthWeightAgi, precision: 6);
            Assert.Equal(1.0, ScoutingBalance.StealthWeightDex, precision: 6);
            Assert.Equal(0.5, ScoutingBalance.StealthWeightLdr, precision: 6);
            Assert.Equal(30, ScoutingBalance.StealthBonusRangerThief, precision: 6);
            Assert.Equal(30, ScoutingBalance.StealthHeavyArmorPenalty, precision: 6);
        }

        [Theory]
        [InlineData(1, 1.10)]
        [InlineData(2, 1.00)]
        [InlineData(3, 0.85)]
        [InlineData(4, 0.70)]
        public void PartySizeMultiplier_MatchesTable(int memberCount, double expected) =>
            Assert.Equal(expected, ScoutingBalance.GetStealthPartySizeMultiplier(memberCount), precision: 6);

        [Fact]
        public void PartySizeMultiplier_ClampsOutOfRange()
        {
            // 0名は1名分、5名以上は4名分へ丸める（Party.MaxSlotsは4のため通常は起きない防御的処理）。
            Assert.Equal(ScoutingBalance.GetStealthPartySizeMultiplier(1), ScoutingBalance.GetStealthPartySizeMultiplier(0), precision: 6);
            Assert.Equal(ScoutingBalance.GetStealthPartySizeMultiplier(4), ScoutingBalance.GetStealthPartySizeMultiplier(9), precision: 6);
        }

        // ---------------- 基礎隠密 ----------------

        [Fact]
        public void CalculateStealthScore_MatchesFormula()
        {
            // 基礎＝(30+30)+(20+20)=100 ＋ 部隊長LDR20×0.5=10 ＝ 110、2名倍率1.00、重装0名 → 110
            var party = PartyOf(MakeLight(agiDex: 30, ldr: 20), MakeLight(agiDex: 20));

            Assert.Equal(110, ScoutingResolver.CalculateBaseStealthScore(party), precision: 6);
            Assert.Equal(110, ScoutingResolver.CalculateStealthScore(party), precision: 6);
            Assert.Equal(0, ScoutingResolver.CalculateStealthScore(new Party()));
            Assert.Equal(0, ScoutingResolver.CalculateBaseStealthScore(new Party()));
        }

        [Fact]
        public void CalculateStealthScore_DoesNotChangeWithVitOrMnd()
        {
            // 走破力との差別化：VIT/MNDをいくら盛っても隠密は動かない。
            var tough = MakeLight(agiDex: 30);
            tough.VIT = 99;
            tough.MND = 99;
            var frail = MakeLight(agiDex: 30);
            frail.VIT = 5;
            frail.MND = 5;

            Assert.Equal(
                ScoutingResolver.CalculateStealthScore(PartyOf(frail)),
                ScoutingResolver.CalculateStealthScore(PartyOf(tough)),
                precision: 6);
        }

        // ---------------- 専門職ボーナス（指示書指定テスト） ----------------

        [Theory]
        [InlineData(JobClass.Ranger)]
        [InlineData(JobClass.Thief)]
        public void CalculateStealthScore_AppliesRangerThiefBonus(JobClass specialistJob)
        {
            var plain = PartyOf(MakeLight(agiDex: 30), MakeLight(agiDex: 30));
            var withSpecialist = PartyOf(Make(specialistJob, agiDex: 30), MakeLight(agiDex: 30));

            Assert.Equal(1, ScoutingResolver.CountStealthSpecialists(withSpecialist));
            Assert.Equal(
                ScoutingBalance.StealthBonusRangerThief,
                ScoutingResolver.CalculateStealthScore(withSpecialist) - ScoutingResolver.CalculateStealthScore(plain),
                precision: 6);
        }

        [Fact]
        public void CalculateStealthScore_StacksSpecialistBonusPerMember()
        {
            var one = PartyOf(Make(JobClass.Thief, 30), MakeLight(30));
            var two = PartyOf(Make(JobClass.Thief, 30), Make(JobClass.Ranger, 30));

            Assert.Equal(2, ScoutingResolver.CountStealthSpecialists(two));
            Assert.Equal(
                ScoutingBalance.StealthBonusRangerThief,
                ScoutingResolver.CalculateStealthScore(two) - ScoutingResolver.CalculateStealthScore(one),
                precision: 6);
        }

        // ---------------- 人数ペナルティ（指示書指定テスト） ----------------

        [Fact]
        public void CalculateStealthScore_AppliesPartySizePenalty()
        {
            // 同じ能力の隊員を1人ずつ増やし、人数倍率がそのまま乗ることを確認する。
            var members = Enumerable.Range(0, 4).Select(_ => MakeLight(agiDex: 25)).ToArray();

            var solo = PartyOf(members[0]);
            // 単独：基礎＝25+25＝50（部隊長LDR0）→ ×1.10 ＝ floor(55) ＝ 55
            Assert.Equal(55, ScoutingResolver.CalculateStealthScore(solo), precision: 6);

            var four = PartyOf(members);
            // 4名：基礎＝50×4＝200 → ×0.70 ＝ floor(140) ＝ 140
            Assert.Equal(200, ScoutingResolver.CalculateBaseStealthScore(four), precision: 6);
            Assert.Equal(140, ScoutingResolver.CalculateStealthScore(four), precision: 6);

            // 1名あたりの素点は同じなのに、4人になると「1人分×4×0.7」まで目減りする。
            Assert.True(ScoutingResolver.CalculateStealthScore(four) < 4 * ScoutingResolver.CalculateStealthScore(solo));
        }

        [Fact]
        public void CalculateStealthScore_FloorsAfterPartySizeMultiplier()
        {
            // 倍率を掛けた時点で切り捨てる（→ 03 §4.5.3の式）。
            // 基礎＝(25+25)×3 ＋ 部隊長LDR2×0.5 ＝ 151 → ×0.85 ＝ 128.35 → 128
            var party = PartyOf(MakeLight(agiDex: 25, ldr: 2), MakeLight(agiDex: 25), MakeLight(agiDex: 25));
            Assert.Equal(151, ScoutingResolver.CalculateBaseStealthScore(party), precision: 6);
            Assert.Equal(128, ScoutingResolver.CalculateStealthScore(party), precision: 6);
        }

        // ---------------- 重装ペナルティ（指示書指定テスト） ----------------

        [Fact]
        public void CalculateStealthScore_AppliesHeavyArmorPenalty()
        {
            // 職業が重戦士・騎士なら、装備に関わらず重装扱い。
            var lightPair = PartyOf(MakeLight(agiDex: 40), MakeLight(agiDex: 40));
            var withWarrior = PartyOf(Make(JobClass.Warrior, agiDex: 40), MakeLight(agiDex: 40));
            var withKnight = PartyOf(Make(JobClass.Knight, agiDex: 40), MakeLight(agiDex: 40));

            Assert.Equal(0, ScoutingResolver.CountHeavyMembers(lightPair));
            Assert.Equal(1, ScoutingResolver.CountHeavyMembers(withWarrior));
            Assert.Equal(1, ScoutingResolver.CountHeavyMembers(withKnight));
            Assert.Equal(
                ScoutingBalance.StealthHeavyArmorPenalty,
                ScoutingResolver.CalculateStealthScore(lightPair) - ScoutingResolver.CalculateStealthScore(withWarrior),
                precision: 6);
        }

        [Fact]
        public void CalculateStealthScore_AppliesHeavyArmorPenalty_ForEquippedHeavyArmor()
        {
            // 軽装職でも重装鎧を着ていれば重装扱い（→ ItemCatalog.HeavyArmorId）。
            var cleric = Make(JobClass.Cleric, agiDex: 40);
            var bare = PartyOf(cleric, MakeLight(agiDex: 40));
            double before = ScoutingResolver.CalculateStealthScore(bare);

            cleric.SetEquippedId(EquipmentSlot.Armor, ItemCatalog.HeavyArmorId);

            Assert.Equal(1, ScoutingResolver.CountHeavyMembers(bare));
            Assert.Equal(
                ScoutingBalance.StealthHeavyArmorPenalty,
                before - ScoutingResolver.CalculateStealthScore(bare),
                precision: 6);
        }

        [Fact]
        public void CalculateStealthScore_CountsHeavyMemberOnce_WhenBothJobAndArmorMatch()
        {
            // 重戦士が重装鎧を着ていても、ペナルティは1名分（二重取りしない）。
            var warrior = Make(JobClass.Warrior, agiDex: 40);
            warrior.SetEquippedId(EquipmentSlot.Armor, ItemCatalog.HeavyArmorId);
            var party = PartyOf(warrior, MakeLight(agiDex: 40));

            Assert.Equal(1, ScoutingResolver.CountHeavyMembers(party));
            Assert.Equal(ScoutingBalance.StealthHeavyArmorPenalty,
                ScoutingResolver.CalculateHeavyArmorPenalty(party), precision: 6);
        }

        // ---------------- 0未満へのクランプ（指示書指定テスト） ----------------

        [Fact]
        public void CalculateStealthScore_ClampsToZero()
        {
            // 能力が極端に低い重装4人：基礎＝(1+1)×4＝8 → ×0.70＝floor(5.6)=5、
            // 重装ペナルティ4名分＝120 → 5-120 は負になるが0で止まる。
            var party = PartyOf(
                Make(JobClass.Warrior, agiDex: 1),
                Make(JobClass.Warrior, agiDex: 1),
                Make(JobClass.Knight, agiDex: 1),
                Make(JobClass.Knight, agiDex: 1));

            Assert.Equal(4, ScoutingResolver.CountHeavyMembers(party));
            Assert.Equal(0, ScoutingResolver.CalculateStealthScore(party), precision: 6);
        }

        // ---------------- 2指標の差別化（本改訂の眼目） ----------------

        [Fact]
        public void StealthAndTraversal_AreNoLongerIdentical()
        {
            // 旧モデルは両方とも Σ(AGI+DEX)＋部隊長LDR×0.5 で、どんな編成でも必ず同値になっていた。
            var mixed = PartyOf(
                Make(JobClass.Warrior, agiDex: 30, ldr: 30),
                Make(JobClass.Ranger, agiDex: 30),
                Make(JobClass.Cleric, agiDex: 30));

            Assert.NotEqual(
                ScoutingResolver.CalculateStealthScore(mixed),
                DungeonTraversalResolver.CalculateTraversalScore(mixed));

            // 編成の向き不向きが出る：重装4人の鈍足部隊は走破力が高く隠密が低い。
            var heavy = PartyOf(
                Make(JobClass.Warrior, agiDex: 5, ldr: 30),
                Make(JobClass.Warrior, agiDex: 5),
                Make(JobClass.Knight, agiDex: 5),
                Make(JobClass.Knight, agiDex: 5));
            Assert.True(
                ScoutingResolver.CalculateStealthScore(heavy) < DungeonTraversalResolver.CalculateTraversalScore(heavy),
                "重装4人は隠密が走破力を下回るはず");

            // 逆に、身軽な斥候の単独潜入は隠密が走破力を上回る。
            var scout = PartyOf(Make(JobClass.Ranger, agiDex: 60, ldr: 20));
            Assert.True(
                ScoutingResolver.CalculateStealthScore(scout) > DungeonTraversalResolver.CalculateTraversalScore(scout),
                "身軽な斥候の単独潜入は隠密が走破力を上回るはず");
        }
    }
}
