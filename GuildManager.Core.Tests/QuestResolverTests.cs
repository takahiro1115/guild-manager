using System;
using System.Collections.Generic;
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

        /// <summary>
        /// 呼び出し順に決め打ちの値を1つずつ返すテスト用スタブ（[min,max]にクランプ）。
        /// フェーズ3の致死判定（生存/古傷/戦死の3分岐）のように、同じ_rng上で
        /// 複数の異なる乱数呼び出し（索敵ロール→HP消費%→致死判定ロール→全治週数）を
        /// 個別に制御したいテストで使う。値が尽きたら以降はminを返す。
        /// </summary>
        private class SequenceRng : IRng
        {
            private readonly Queue<int> _values;
            public SequenceRng(params int[] values) => _values = new Queue<int>(values);
            public int NextInt(int min, int max) =>
                Math.Clamp(_values.Count > 0 ? _values.Dequeue() : min, min, max);
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
        /// （フェーズ3・致死判定の対象の絞り込みに使う。→ 03 §4.3）。
        /// AlwaysMaxRngは致死判定ロールも最大値になるため、このケースは戦死に至る
        /// （具体的な生存/古傷/戦死の分岐は下の Resolve_DeathJudgment_* 系で個別に検証する）。
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
            Assert.Contains(weakling.Id, result.FallenAdventurerIds); // AlwaysMaxRng＝致死判定ロールも最大→戦死
            Assert.Equal(0, weakling.CurrentHP); // 戦死：HPは0のまま（生存時のHP1レスキューはしない）
        }

        // ---------------- フェーズ3：負傷・致死判定の3分岐（→ 03 §4.3） ----------------

        /// <summary>
        /// 致死判定に使う一人パーティを組み立てる。VIT=1・LDR=1・神官なしのため
        /// SurvivalThresholdは下限の5にクランプされる（clamp(1+0+0.2,5,90)=5）。
        /// PermanentBand=20なので、致死判定ロールは 1〜5=生存・6〜25=古傷判定域・26〜100=戦死
        /// にきれいに区切られる。
        /// </summary>
        private static (Party Party, Adventurer Member) BuildDeathJudgmentParty()
        {
            var member = new Adventurer { STR = 1, AGI = 1, VIT = 1, MND = 1, DEX = 1, LDR = 1 };
            member.CurrentHP = member.MaxHP;
            var party = new Party();
            party.TryAdd(member);
            return (party, member);
        }

        /// <summary>索敵ロール・HP消費%ロールは結果に影響しない値、致死判定ロールだけを差し替えるための共通クエスト。</summary>
        private static Quest DeathJudgmentQuest() => new Quest { Difficulty = 100, ScoutRequirement = 1 };

        [Fact]
        public void Resolve_DeathJudgment_Survives_WhenRollAtOrBelowSurvivalThreshold()
        {
            var (party, member) = BuildDeathJudgmentParty();
            // [索敵ロール, HP消費%(強制的に上限へ), 致死判定ロール=3(<=5→生存), 全治週数=5]
            var resolver = new QuestResolver(new SequenceRng(50, 1000, 3, 5));

            var result = resolver.Resolve(party, DeathJudgmentQuest());

            Assert.Contains(member.Id, result.DownedAdventurerIds);
            Assert.DoesNotContain(member.Id, result.FallenAdventurerIds);
            Assert.Empty(member.TraitIds);
            Assert.Equal(InjurySeverity.Severe, member.Injury);
            Assert.Equal(5, member.InjuryWeeksRemaining);
            Assert.Equal(1, member.CurrentHP);
        }

        [Fact]
        public void Resolve_DeathJudgment_GrantsOldWoundTrait_WhenRollInPermanentBand()
        {
            var (party, member) = BuildDeathJudgmentParty();
            // 致死判定ロール=15（5<15<=25＝古傷判定域）
            var resolver = new QuestResolver(new SequenceRng(50, 1000, 15, 5));

            var result = resolver.Resolve(party, DeathJudgmentQuest());

            Assert.DoesNotContain(member.Id, result.FallenAdventurerIds);
            Assert.True(member.HasTrait(TraitCatalog.OldWoundId));
            Assert.Equal(InjurySeverity.Severe, member.Injury); // 古傷を負った直後は重傷も併発
            Assert.Equal(1, member.CurrentHP);
        }

        [Fact]
        public void Resolve_DeathJudgment_RoundsToSevereInjury_WhenAlreadyHasOldWound()
        {
            // 重複禁止：既に古傷を持つ冒険者が再び古傷判定域に入っても新たな付与はせず、
            // 「生存（重傷）」として扱う（→ 03 §4.3）。
            var (party, member) = BuildDeathJudgmentParty();
            member.TryAddTrait(TraitCatalog.OldWoundId);
            member.CurrentHP = member.MaxHP; // 古傷適用後のMaxHP（実効VIT低下を反映）で満タンに再設定
            var resolver = new QuestResolver(new SequenceRng(50, 1000, 15, 5));

            var result = resolver.Resolve(party, DeathJudgmentQuest());

            Assert.DoesNotContain(member.Id, result.FallenAdventurerIds);
            Assert.Single(member.TraitIds); // 重複追加されていない
            Assert.Equal(InjurySeverity.Severe, member.Injury);
            Assert.Equal(1, member.CurrentHP);
        }

        [Fact]
        public void Resolve_DeathJudgment_RecordsDeath_WhenRollExceedsPermanentBand()
        {
            var (party, member) = BuildDeathJudgmentParty();
            // 致死判定ロール=50（>25＝戦死）
            var resolver = new QuestResolver(new SequenceRng(50, 1000, 50));

            var result = resolver.Resolve(party, DeathJudgmentQuest());

            Assert.Contains(member.Id, result.FallenAdventurerIds);
            Assert.Equal(0, member.CurrentHP); // 戦死：HP1へのレスキューはしない
            Assert.Empty(member.TraitIds); // 戦死時は古傷を付与しない
        }

        // ---------------- 特性による索敵・致死判定の補正（→ 03 §5.3・v1.4改訂） ----------------

        [Fact]
        public void Resolve_AttentiveTrait_IncreasesPartyScout_ViaHigherDexContribution()
        {
            // 「注意深い」はこのメンバー自身のDEX寄与を+10%する（GetScoutingDex）。
            // 索敵値が上がるほど下限・上限帯がプラス側にシフトし、同じscoutRollでも
            // 遭遇区分が「不意打ち」から「通常交戦」以上へ変わりうる（→ 03 §4.1）。
            var plain = new Adventurer { STR = 50, AGI = 50, VIT = 50, MND = 50, DEX = 50, LDR = 50 };
            plain.CurrentHP = plain.MaxHP;
            var attentive = new Adventurer { STR = 50, AGI = 50, VIT = 50, MND = 50, DEX = 50, LDR = 50 };
            attentive.CurrentHP = attentive.MaxHP;
            attentive.TryAddTrait(TraitCatalog.AttentiveId);

            var quest = new Quest { Difficulty = 50, ScoutRequirement = 50 };
            var resolver = new QuestResolver(new FixedRng(50));

            var plainResult = resolver.Resolve(PartyOf(plain), quest);
            var attentiveResult = resolver.Resolve(PartyOf(attentive), quest);

            Assert.True(attentiveResult.Ratio >= plainResult.Ratio,
                "「注意深い」で索敵値が上がった分、遭遇が有利側（Normal以上）にシフトするはず");
            Assert.NotEqual(EncounterResult.Ambushed, attentiveResult.Encounter);
        }

        [Fact]
        public void Resolve_BraveTrait_AddsFixedBonusToSurvivalThreshold()
        {
            // 「豪胆」はSurvivalThresholdに固定+5（→ TraitCatalog.Brave）。
            // BuildDeathJudgmentPartyと同条件（VIT=1,LDR=1,神官なし）だと、
            // 通常はclamp(1+0+0.2,5,90)=5だが、豪胆込みでclamp(6.2,5,90)=6.2になる。
            // 致死判定ロール=6（豪胆なしなら6>5=戦死、豪胆ありなら6<=6.2=生存）で判定させる。
            var (party, member) = BuildDeathJudgmentParty();
            member.TryAddTrait(TraitCatalog.BraveId);
            var resolver = new QuestResolver(new SequenceRng(50, 1000, 6, 5));

            var result = resolver.Resolve(party, DeathJudgmentQuest());

            Assert.DoesNotContain(member.Id, result.FallenAdventurerIds);
            Assert.Contains(member.Id, result.DownedAdventurerIds);
            Assert.Equal(1, member.CurrentHP);
        }

        [Fact]
        public void Resolve_WithoutBraveTrait_SameRollFallsIntoPermanentBand_InsteadOfSurviving()
        {
            // 上のテストとの対比：豪胆を持たなければSurvivalThresholdは5のままなので、
            // 同じロール=6は「生存」ではなく古傷判定域（6〜25）に入る
            // （豪胆の+5固定ボーナスが無ければ、6<=閾値の生存判定を満たせないことの確認）。
            var (party, member) = BuildDeathJudgmentParty();
            var resolver = new QuestResolver(new SequenceRng(50, 1000, 6, 5));

            var result = resolver.Resolve(party, DeathJudgmentQuest());

            Assert.DoesNotContain(member.Id, result.FallenAdventurerIds);
            Assert.True(member.HasTrait(TraitCatalog.OldWoundId));
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
