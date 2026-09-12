using System;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 仕様書 03 §4.2 の勝敗境界表が、コードの境界値と一致しているかを確認するテスト。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class QuestResolverTests
    {
        /// <summary>NextInt(min, max) が常に max を返すテスト用スタブ。HP消費%を確実に上限にできる。</summary>
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

        [Theory]
        [InlineData(2.5, CombatOutcome.Victory)]   // 十分な勝利
        [InlineData(1.8, CombatOutcome.Victory)]   // 完全勝利の下限ちょうど
        [InlineData(1.79, CombatOutcome.NarrowWin)] // 完全勝利のすぐ下 → 辛勝
        [InlineData(1.0, CombatOutcome.NarrowWin)]  // 辛勝の下限ちょうど
        [InlineData(0.99, CombatOutcome.Defeat)]    // 辛勝のすぐ下 → 苦戦敗退
        [InlineData(0.6, CombatOutcome.Defeat)]     // 苦戦敗退の下限ちょうど
        [InlineData(0.59, CombatOutcome.Rout)]      // 苦戦敗退のすぐ下 → 戦線崩壊
        [InlineData(0.0, CombatOutcome.Rout)]       // 完敗
        public void ClassifyOutcome_ReturnsExpectedBucket(double ratio, CombatOutcome expected)
        {
            var (outcome, _, _) = QuestResolver.ClassifyOutcome(ratio);
            Assert.Equal(expected, outcome);
        }

        [Fact]
        public void ClassifyOutcome_VictoryHasLightestHpLossRange()
        {
            var (_, min, max) = QuestResolver.ClassifyOutcome(2.0);
            Assert.Equal(5, min);
            Assert.Equal(15, max);
        }

        [Fact]
        public void ClassifyOutcome_RoutHasHeaviestHpLossRange()
        {
            var (_, min, max) = QuestResolver.ClassifyOutcome(0.1);
            Assert.Equal(70, min);
            Assert.Equal(100, max);
        }

        // ---------------- 個人CPへのDEX・INT参加（→ 03 §4.2 v1.2改訂） ----------------

        [Fact]
        public void Resolve_HigherDex_YieldsHigherRatio_AllElseEqual()
        {
            // v1.2改訂：DEXは索敵専任に加え個人CPにも参加する（二重役割）。
            // DEXは索敵フェーズ（§4.1）にも影響するため、遭遇区分（Normal/Surprise/Ambushed）が
            // 両者で変わらない範囲の値を選び、CPへの寄与だけを比較できるようにしている。
            var lowDex = new Adventurer { STR = 50, AGI = 50, VIT = 50, MND = 50, DEX = 45, LDR = 50 };
            lowDex.CurrentHP = lowDex.MaxHP;
            var highDex = new Adventurer { STR = 50, AGI = 50, VIT = 50, MND = 50, DEX = 55, LDR = 50 };
            highDex.CurrentHP = highDex.MaxHP;

            var quest = new Quest { Difficulty = 50, ScoutRequirement = 50 };
            var resolver = new QuestResolver(new FixedRng(50));

            var lowResult = resolver.Resolve(PartyOf(lowDex), quest);
            var highResult = resolver.Resolve(PartyOf(highDex), quest);

            Assert.Equal(EncounterResult.Normal, lowResult.Encounter);
            Assert.Equal(EncounterResult.Normal, highResult.Encounter);
            Assert.True(highResult.Ratio > lowResult.Ratio,
                $"DEXが高い方のRatio({highResult.Ratio})は低い方({lowResult.Ratio})より高いはず");
        }

        [Fact]
        public void Resolve_HigherInt_YieldsHigherRatio_AllElseEqual()
        {
            // v1.2改訂：INTは予約フィールドから活性化し個人CPに参加する。
            var lowInt = new Adventurer { STR = 50, AGI = 50, VIT = 50, MND = 50, DEX = 50, LDR = 50, INT = 10 };
            lowInt.CurrentHP = lowInt.MaxHP;
            var highInt = new Adventurer { STR = 50, AGI = 50, VIT = 50, MND = 50, DEX = 50, LDR = 50, INT = 90 };
            highInt.CurrentHP = highInt.MaxHP;

            var quest = new Quest { Difficulty = 50, ScoutRequirement = 75 };
            var resolver = new QuestResolver(new FixedRng(50));

            var lowResult = resolver.Resolve(PartyOf(lowInt), quest);
            var highResult = resolver.Resolve(PartyOf(highInt), quest);

            Assert.True(highResult.Ratio > lowResult.Ratio,
                $"INTが高い方のRatio({highResult.Ratio})は低い方({lowResult.Ratio})より高いはず");
        }

        private static Party PartyOf(Adventurer member)
        {
            var party = new Party();
            party.TryAdd(member);
            return party;
        }

        /// <summary>
        /// Resolve()がHP0到達を検知したら DownedAdventurerIds に記録することを確認する
        /// （→ 03 §4.3の本来の致死判定を実装する際に使う想定。現状はどのSystemも未参照）。
        /// </summary>
        [Fact]
        public void Resolve_RecordsDownedAdventurer_WhenHpHitsZero()
        {
            // 極端に弱いパーティ×極端に高難易度のクエストでRatioを確実に0.6未満（戦線崩壊）にし、
            // AlwaysMaxRngでHP消費%を常に上限（Rout帯は70〜100%）にすることでHP0到達を保証する。
            var weakling = new Adventurer { STR = 1, AGI = 1, VIT = 1, MND = 1, DEX = 1, LDR = 1 };
            weakling.CurrentHP = weakling.MaxHP;
            var party = new Party();
            party.TryAdd(weakling);
            var quest = new Quest { Difficulty = 100, ScoutRequirement = 1 };
            var resolver = new QuestResolver(new AlwaysMaxRng());

            var result = resolver.Resolve(party, quest);

            Assert.Equal(CombatOutcome.Rout, result.Outcome);
            Assert.Contains(weakling.Id, result.DownedAdventurerIds);
            Assert.Equal(1, weakling.CurrentHP); // §4.3のMVP簡易版：ダウン後はHP1で重傷に留まる
        }

        // ---------------- 配置（Placement）による個人CP補正（→ 03 §4.2） ----------------

        [Fact]
        public void Resolve_FrontPlacement_YieldsHigherRatioThanBackPlacement_ForSameStats()
        {
            // Warriorは前衛が本来の役割（補正1.2倍）、後衛は役割から外れる（0.8倍）。
            // ステータス・職業を揃え、配置だけを変えて比較する。
            var frontWarrior = new Adventurer { JobClass = JobClass.Warrior, Placement = Placement.Front, STR = 50, AGI = 50, VIT = 50, MND = 50, DEX = 50, LDR = 50 };
            frontWarrior.CurrentHP = frontWarrior.MaxHP;
            var backWarrior = new Adventurer { JobClass = JobClass.Warrior, Placement = Placement.Back, STR = 50, AGI = 50, VIT = 50, MND = 50, DEX = 50, LDR = 50 };
            backWarrior.CurrentHP = backWarrior.MaxHP;

            var quest = new Quest { Difficulty = 50, ScoutRequirement = 75 }; // 索敵ぴったり合わせて「通常交戦」域に
            var resolver = new QuestResolver(new FixedRng(50));

            var frontParty = new Party();
            frontParty.TryAdd(frontWarrior);
            var backParty = new Party();
            backParty.TryAdd(backWarrior);

            var frontResult = resolver.Resolve(frontParty, quest);
            var backResult = resolver.Resolve(backParty, quest);

            Assert.True(frontResult.Ratio > backResult.Ratio,
                $"前衛Ratio({frontResult.Ratio})は後衛Ratio({backResult.Ratio})より高いはず");
        }

        // ---------------- 不意打ち時の後衛被弾ウェイト（→ 03 §4.1↔§4.2） ----------------

        [Fact]
        public void Resolve_Ambush_AppliesHeavierHpLossToBackRowThanFrontRow()
        {
            var front = new Adventurer { Placement = Placement.Front, STR = 1, AGI = 1, VIT = 50, MND = 1, DEX = 1, LDR = 1 };
            front.CurrentHP = front.MaxHP;
            var back = new Adventurer { Placement = Placement.Back, STR = 1, AGI = 1, VIT = 50, MND = 1, DEX = 1, LDR = 1 };
            back.CurrentHP = back.MaxHP;
            var party = new Party();
            party.TryAdd(front);
            party.TryAdd(back);

            // ScoutRequirementを極端に高くし、AlwaysMaxRngのscoutRoll=100が確実に
            // 上限（クランプ後、最大でも99）を超えるようにして「不意打ち」を確定させる。
            var quest = new Quest { Difficulty = 1, ScoutRequirement = 100 };
            var resolver = new QuestResolver(new AlwaysMaxRng());

            var result = resolver.Resolve(party, quest);

            Assert.Equal(EncounterResult.Ambushed, result.Encounter);
            int frontLoss = result.HpLostByAdventurer[front.Id];
            int backLoss = result.HpLostByAdventurer[back.Id];
            Assert.True(backLoss > frontLoss, $"不意打ち時は後衛の被弾({backLoss})が前衛({frontLoss})より重いはず");
        }

        [Fact]
        public void Resolve_NonAmbushEncounter_AppliesSameHpLossRegardlessOfPlacement()
        {
            // 奇襲成功・通常交戦では前衛/後衛で差をつけない（→ 03 §4.2追記）。
            var front = new Adventurer { Placement = Placement.Front, STR = 50, AGI = 50, VIT = 50, MND = 50, DEX = 50, LDR = 50 };
            front.CurrentHP = front.MaxHP;
            var back = new Adventurer { Placement = Placement.Back, STR = 50, AGI = 50, VIT = 50, MND = 50, DEX = 50, LDR = 50 };
            back.CurrentHP = back.MaxHP;
            var party = new Party();
            party.TryAdd(front);
            party.TryAdd(back);

            var quest = new Quest { Difficulty = 50, ScoutRequirement = 75 }; // 「通常交戦」域に収める
            var resolver = new QuestResolver(new FixedRng(50));

            var result = resolver.Resolve(party, quest);

            Assert.NotEqual(EncounterResult.Ambushed, result.Encounter);
            Assert.Equal(result.HpLostByAdventurer[front.Id], result.HpLostByAdventurer[back.Id]);
        }
    }
}
