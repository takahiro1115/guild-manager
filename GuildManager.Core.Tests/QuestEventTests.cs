using System;
using System.Collections.Generic;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 探索・護衛のランダムイベント3種（→ 03 §4.2.3、項目65）のテスト。
    ///
    /// QuestResolver内の乱数呼び出しは、探索・護衛では次の順序で行われる：
    ///   1. 索敵ロール
    ///   2. ①強敵との遭遇の発生ロール（1〜100。10以下で発生）
    ///   3. ②宝物庫の発見の発生ロール（探索のみ。15以下で発生）
    ///   4. ③深追いの発生ロール（15以下で発生）
    ///   5. HP消費%ロール（メンバーごと。イベント①で撃ち合った回は致死判定ロールも続く）
    ///   6. ②が失敗した場合の罠ロール（30以下で作動）→ 作動時はメンバーごとの追加HP消費ロール
    ///   7. ③が大成功／失敗の場合のメンバーごとの追加HP消費ロール
    /// テストはこの順序に合わせた SequenceRng で、発生させたいイベントだけを狙って起こす。
    /// </summary>
    public class QuestEventTests
    {
        /// <summary>呼び出し順に決め打ちの値を返すスタブ（[min,max]にクランプ。尽きたらminを返す）。</summary>
        private class SequenceRng : IRng
        {
            private readonly Queue<int> _values;
            public SequenceRng(params int[] values) => _values = new Queue<int>(values);
            public int NextInt(int min, int max) =>
                Math.Clamp(_values.Count > 0 ? _values.Dequeue() : min, min, max);
        }

        /// <summary>常に同じ値を[min,max]にクランプして返すスタブ。</summary>
        private class FixedRng : IRng
        {
            private readonly int _value;
            public FixedRng(int value) => _value = value;
            public int NextInt(int min, int max) => Math.Clamp(_value, min, max);
        }

        private const int Occur = 5;      // 発生ロール：10以下 → ①②③いずれも発生する値
        private const int DoNotOccur = 50; // 発生ロール：閾値より大きい → 発生しない値

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members)
                party.TryAdd(m);
            return party;
        }

        private static Adventurer Member(int str = 50, int vit = 50, int agi = 50, int mnd = 50, int dex = 50, int ldr = 50, int @int = 50)
        {
            var a = new Adventurer { STR = str, VIT = vit, AGI = agi, MND = mnd, DEX = dex, LDR = ldr, INT = @int };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static Quest ExplorationQuest(int difficulty = 50) =>
            new Quest { QuestType = QuestType.Exploration, Difficulty = difficulty, ScoutRequirement = 50, RewardGold = 300 };

        private static Quest EscortQuest(int difficulty = 50) =>
            new Quest { QuestType = QuestType.Escort, Difficulty = difficulty, ScoutRequirement = 50, RewardGold = 300 };

        // ---------------- ①強敵との遭遇：押し切り／退避／苦戦の分岐 ----------------

        [Fact]
        public void StrongEnemy_PushesThrough_WhenPushScoreReachesRequirement()
        {
            // 押し切りスコア = (STR90+VIT90) + 隊長LDR50×0.5 = 205 ≧ 要求値 50×2.0 = 100。
            var member = Member(str: 90, vit: 90, agi: 1, ldr: 50, @int: 1);
            // [索敵, ①発生, ②非発生, ③非発生, HP消費%(辛勝帯20〜45の下限20)]
            var rng = new SequenceRng(50, Occur, DoNotOccur, DoNotOccur, 20);

            var result = new QuestResolver(rng).Resolve(PartyOf(member), ExplorationQuest());

            var strongEnemy = Assert.IsType<StrongEnemyEventResult>(result.Events.StrongEnemy);
            Assert.Equal(StrongEnemyOutcome.PushedThrough, strongEnemy.Outcome);
            Assert.True(strongEnemy.UsedCombatDamage);
            Assert.Equal(200, strongEnemy.BonusRewardGold); // 押し切りの追加報酬
        }

        [Fact]
        public void StrongEnemy_Evades_WhenOnlyEvadeScoreReachesRequirement()
        {
            // 押し切り = (1+1) + 1×0.5 = 2.5 < 100、退避 = (AGI90+INT90) = 180 ≧ 100。
            var member = Member(str: 1, vit: 1, agi: 90, ldr: 1, @int: 90);
            var rng = new SequenceRng(50, Occur, DoNotOccur, DoNotOccur, 10);

            var result = new QuestResolver(rng).Resolve(PartyOf(member), ExplorationQuest());

            var strongEnemy = Assert.IsType<StrongEnemyEventResult>(result.Events.StrongEnemy);
            Assert.Equal(StrongEnemyOutcome.Evaded, strongEnemy.Outcome);
            Assert.False(strongEnemy.UsedCombatDamage); // 退避＝通常の軽量HP消費のまま
            Assert.Equal(0, strongEnemy.BonusRewardGold);
            Assert.Empty(result.DownedAdventurerIds);
            Assert.Empty(result.FallenAdventurerIds);
        }

        [Fact]
        public void StrongEnemy_Struggles_WhenNeitherScoreReachesRequirement()
        {
            // 押し切り = 2.5、退避 = 2。いずれも要求値100に届かない。
            var member = Member(str: 1, vit: 1, agi: 1, ldr: 1, @int: 1);
            var rng = new SequenceRng(50, Occur, DoNotOccur, DoNotOccur, 40);

            var result = new QuestResolver(rng).Resolve(PartyOf(member), ExplorationQuest());

            var strongEnemy = Assert.IsType<StrongEnemyEventResult>(result.Events.StrongEnemy);
            Assert.Equal(StrongEnemyOutcome.Struggled, strongEnemy.Outcome);
            Assert.True(strongEnemy.UsedCombatDamage); // 苦戦敗退相当の損害を流用
            Assert.Equal(0, strongEnemy.BonusRewardGold);
        }

        [Fact]
        public void StrongEnemy_BraveTrait_HelpsPushThrough()
        {
            // 豪胆ボーナス+20が押し切りスコアに乗る（保有者が1人でもいれば1回だけ）。
            // 素点 = (STR40+VIT40) + LDR0×0.5 = 80 → 要求値100に届かない（＝苦戦）。
            // 豪胆ありなら 80+20 = 100 ≧ 100 で押し切りに変わる。
            Adventurer Make(bool brave)
            {
                var a = Member(str: 40, vit: 40, agi: 1, ldr: 0, @int: 1);
                if (brave) a.TryAddTrait(TraitCatalog.BraveId);
                a.CurrentHP = a.MaxHP;
                return a;
            }

            var withoutBrave = new QuestResolver(new SequenceRng(50, Occur, DoNotOccur, DoNotOccur, 40))
                .Resolve(PartyOf(Make(false)), ExplorationQuest());
            var withBrave = new QuestResolver(new SequenceRng(50, Occur, DoNotOccur, DoNotOccur, 20))
                .Resolve(PartyOf(Make(true)), ExplorationQuest());

            Assert.Equal(StrongEnemyOutcome.Struggled, withoutBrave.Events.StrongEnemy!.Outcome);
            Assert.Equal(StrongEnemyOutcome.PushedThrough, withBrave.Events.StrongEnemy!.Outcome);
        }

        [Fact]
        public void StrongEnemy_CombatDamage_CanCauseDeath_EvenOnExplorationQuest()
        {
            // 押し切り・苦戦では討伐フローの致死判定を流用するため、探索でも戦死しうる
            // （通常の探索・護衛ではHP下限1で致死判定自体が発生しない）。
            // VITは低くしておく：致死回避閾値は clamp(VIT + 隊長LDR×0.2, 5, 90) で、
            // VITが高いと閾値90＋不可逆帯20＝110となり、最大ロール100でも戦死しなくなるため。
            // 押し切りスコア = (STR90+VIT1) + 隊長LDR50×0.5 = 116 ≧ 要求値100。
            var member = Member(str: 90, vit: 1, agi: 1, ldr: 50, @int: 1);
            member.CurrentHP = 10; // 辛勝帯の消費（45%＝23）で確実にHP0へ（MaxHP=52）
            // [索敵, ①発生, ②非発生, ③非発生, HP消費%=45, 致死判定ロール=50]
            // 致死回避閾値 = clamp(1 + 50×0.2, 5, 90) = 11、不可逆帯は11〜31 → ロール50は戦死。
            var rng = new SequenceRng(50, Occur, DoNotOccur, DoNotOccur, 45, 50);

            var result = new QuestResolver(rng).Resolve(PartyOf(member), ExplorationQuest());

            Assert.Equal(StrongEnemyOutcome.PushedThrough, result.Events.StrongEnemy!.Outcome);
            Assert.Contains(member.Id, result.DownedAdventurerIds);
            Assert.Contains(member.Id, result.FallenAdventurerIds);
            Assert.Equal(0, member.CurrentHP);
        }

        [Fact]
        public void StrongEnemy_CombatDamage_CanGrantOldWound_OnExplorationQuest()
        {
            // 上のテストと同条件だが、致死判定ロールを不可逆帯（11〜31）に収める。
            var member = Member(str: 90, vit: 1, agi: 1, ldr: 50, @int: 1);
            member.CurrentHP = 10;
            // [索敵, ①発生, ②非発生, ③非発生, HP消費%=45, 致死判定ロール=20（古傷帯）, 全治週数=5]
            var rng = new SequenceRng(50, Occur, DoNotOccur, DoNotOccur, 45, 20, 5);

            var result = new QuestResolver(rng).Resolve(PartyOf(member), ExplorationQuest());

            Assert.DoesNotContain(member.Id, result.FallenAdventurerIds);
            Assert.True(member.HasTrait(TraitCatalog.OldWoundId));
            Assert.Equal(1, member.CurrentHP);
        }

        [Fact]
        public void StrongEnemy_DoesNotApplyLightweightHpLossTwice()
        {
            // イベント①で撃ち合った回は、通常の軽量HP消費を適用しない（二重消費の防止）。
            // MaxHP = VIT90×2+50 = 230、辛勝帯の消費20% → 46。HPはちょうど230-46=184になるはず
            // （軽量消費が重ねて適用されていれば、これより低くなる）。
            var member = Member(str: 90, vit: 90, agi: 1, ldr: 50, @int: 1);
            Assert.Equal(230, member.MaxHP);
            var rng = new SequenceRng(50, Occur, DoNotOccur, DoNotOccur, 20);

            var result = new QuestResolver(rng).Resolve(PartyOf(member), ExplorationQuest());

            Assert.Equal(StrongEnemyOutcome.PushedThrough, result.Events.StrongEnemy!.Outcome);
            Assert.Equal(184, member.CurrentHP);
            Assert.Equal(46, result.HpLostByAdventurer[member.Id]);
        }

        [Fact]
        public void StrongEnemy_DoesNotChangeQuestOutcomeItself()
        {
            // イベントは探索・護衛としての成否（大成功/成功/失敗）を変更しない（→ 03 §4.2.3）。
            var member = Member(str: 90, vit: 90, agi: 1, ldr: 50, @int: 1);
            var rngWithEvent = new SequenceRng(50, Occur, DoNotOccur, DoNotOccur, 20);
            var rngWithoutEvent = new SequenceRng(50, DoNotOccur, DoNotOccur, DoNotOccur, 20);

            var withEvent = new QuestResolver(rngWithEvent).Resolve(PartyOf(Member(str: 90, vit: 90, agi: 1, ldr: 50, @int: 1)), ExplorationQuest());
            var withoutEvent = new QuestResolver(rngWithoutEvent).Resolve(PartyOf(member), ExplorationQuest());

            Assert.NotNull(withEvent.Events.StrongEnemy);
            Assert.Null(withoutEvent.Events.StrongEnemy);
            Assert.Equal(withoutEvent.NonCombatOutcome, withEvent.NonCombatOutcome);
            Assert.Equal(withoutEvent.Ratio, withEvent.Ratio, precision: 10);
        }

        [Fact]
        public void StrongEnemy_OccursOnEscortQuest_Too()
        {
            var member = Member(str: 90, vit: 90, agi: 1, ldr: 50, @int: 1);
            // 護衛は②宝物庫のロールが無いため、発生ロールは[①, ③]の2つだけ。
            var rng = new SequenceRng(50, Occur, DoNotOccur, 20);

            var result = new QuestResolver(rng).Resolve(PartyOf(member), EscortQuest());

            Assert.NotNull(result.Events.StrongEnemy);
            Assert.Null(result.Events.TreasureVault); // 宝物庫は探索限定
        }

        // ---------------- ②宝物庫の発見（探索のみ） ----------------

        [Theory]
        [InlineData(90, 90, QuestEventOutcome.GreatSuccess, 400)] // (DEX90+INT90)=180 / 100 = 1.8
        [InlineData(60, 60, QuestEventOutcome.Success, 150)]      // 120 / 100 = 1.2
        [InlineData(20, 20, QuestEventOutcome.Failure, 0)]        // 40 / 100 = 0.4
        public void TreasureVault_ClassifiesByDexAndInt(int dex, int @int, QuestEventOutcome expected, int expectedGold)
        {
            var member = Member(dex: dex, @int: @int);
            // [索敵, ①非発生, ②発生, ③非発生, HP消費%, (失敗時のみ)罠ロール=非作動]
            var rng = new SequenceRng(50, DoNotOccur, Occur, DoNotOccur, 5, 99);

            var result = new QuestResolver(rng).Resolve(PartyOf(member), ExplorationQuest());

            var vault = Assert.IsType<TreasureVaultEventResult>(result.Events.TreasureVault);
            Assert.Equal(expected, vault.Outcome);
            Assert.Equal(expectedGold, vault.BonusRewardGold);
        }

        [Fact]
        public void TreasureVault_AttentiveTrait_RaisesScore()
        {
            // 注意深い+20により、素点80（DEX40+INT40）が100になり失敗→成功へ変わる。
            Adventurer Make(bool attentive)
            {
                var a = Member(dex: 40, @int: 40);
                if (attentive) a.TryAddTrait(TraitCatalog.AttentiveId);
                a.CurrentHP = a.MaxHP;
                return a;
            }

            var plain = new QuestResolver(new SequenceRng(50, DoNotOccur, Occur, DoNotOccur, 5, 99))
                .Resolve(PartyOf(Make(false)), ExplorationQuest());
            var attentive = new QuestResolver(new SequenceRng(50, DoNotOccur, Occur, DoNotOccur, 5))
                .Resolve(PartyOf(Make(true)), ExplorationQuest());

            Assert.Equal(QuestEventOutcome.Failure, plain.Events.TreasureVault!.Outcome);
            Assert.Equal(QuestEventOutcome.Success, attentive.Events.TreasureVault!.Outcome);
        }

        [Fact]
        public void TreasureVault_Trap_OnlyTriggersOnFailure_AndCostsExtraHp()
        {
            // 宝物庫の判定スコア = DEX20+INT20 = 40 に対し要求値100 → 失敗。
            // 罠が作動した回だけ、通常のHP消費の上に罠の分（MaxHPの8%）が上乗せされる。
            Adventurer Run(int trapRoll, out WeekResolutionResult result)
            {
                var m = Member(dex: 20, @int: 20);
                // [索敵, ①非発生, ②発生, ③非発生, HP消費%（軽量帯の下限にクランプされる）,
                //  罠ロール, 罠のHP消費%=8]
                var rng = new SequenceRng(50, DoNotOccur, Occur, DoNotOccur, 0, trapRoll, 8);
                result = new QuestResolver(rng).Resolve(PartyOf(m), ExplorationQuest());
                return m;
            }

            var trapped = Run(trapRoll: 1, out var trappedResult);    // 1 ≦ 30 → 作動
            var unharmed = Run(trapRoll: 99, out var unharmedResult); // 99 > 30 → 不作動

            Assert.Equal(QuestEventOutcome.Failure, trappedResult.Events.TreasureVault!.Outcome);
            Assert.True(trappedResult.Events.TreasureVault.TrapTriggered);
            Assert.False(unharmedResult.Events.TreasureVault!.TrapTriggered);

            // 罠の分（MaxHPの8%）だけ余分に減っている。
            Assert.Equal(unharmed.CurrentHP - unharmed.MaxHP * 8 / 100, trapped.CurrentHP);
        }

        [Fact]
        public void TreasureVault_Trap_DoesNotTrigger_OnSuccess()
        {
            var member = Member(dex: 90, @int: 90); // 大成功する編成
            int maxHp = member.MaxHP;
            var rng = new SequenceRng(50, DoNotOccur, Occur, DoNotOccur, 0, 1);

            var result = new QuestResolver(rng).Resolve(PartyOf(member), ExplorationQuest());

            Assert.Equal(QuestEventOutcome.GreatSuccess, result.Events.TreasureVault!.Outcome);
            Assert.False(result.Events.TreasureVault.TrapTriggered);
            Assert.Equal(maxHp, member.CurrentHP); // HP消費0%のまま（罠も無し）
        }

        [Fact]
        public void TreasureVault_DoesNotOccurOnEscortQuest()
        {
            var member = Member(dex: 90, @int: 90);
            var rng = new FixedRng(1); // すべての発生ロールが通る値

            var result = new QuestResolver(rng).Resolve(PartyOf(member), EscortQuest());

            Assert.Null(result.Events.TreasureVault); // 探索限定
            Assert.NotNull(result.Events.StrongEnemy);
            Assert.NotNull(result.Events.PushingOn);
        }

        // ---------------- ③深追い（探索・護衛） ----------------

        [Theory]
        [InlineData(90, 90, QuestEventOutcome.GreatSuccess, 300)] // (VIT90+MND90)=180 / 100 = 1.8
        [InlineData(60, 60, QuestEventOutcome.Success, 100)]      // 120 / 100 = 1.2
        [InlineData(20, 20, QuestEventOutcome.Failure, 0)]        // 40 / 100 = 0.4
        public void PushingOn_ClassifiesByVitAndMnd(int vit, int mnd, QuestEventOutcome expected, int expectedGold)
        {
            var member = Member(vit: vit, mnd: mnd);
            // [索敵, ①非発生, ②非発生, ③発生, HP消費%, 追加HP消費%]
            var rng = new SequenceRng(50, DoNotOccur, DoNotOccur, Occur, 5, 5);

            var result = new QuestResolver(rng).Resolve(PartyOf(member), ExplorationQuest());

            var pushingOn = Assert.IsType<PushingOnEventResult>(result.Events.PushingOn);
            Assert.Equal(expected, pushingOn.Outcome);
            Assert.Equal(expectedGold, pushingOn.BonusRewardGold);
        }

        [Theory]
        [InlineData(90, 90, true)]  // 大成功：粘った代償として追加消費あり
        [InlineData(60, 60, false)] // 成功：追加消費なし
        [InlineData(20, 20, true)]  // 失敗：それなりの追加消費あり
        public void PushingOn_AppliesExtraHpLoss_ExceptOnPlainSuccess(int vit, int mnd, bool expectExtraLoss)
        {
            var member = Member(vit: vit, mnd: mnd);
            int maxHp = member.MaxHP;
            // 通常のHP消費%を0にして、追加消費の有無だけをHPの変化で見る。
            var rng = new SequenceRng(50, DoNotOccur, DoNotOccur, Occur, 0, 10);

            var result = new QuestResolver(rng).Resolve(PartyOf(member), ExplorationQuest());

            Assert.Equal(expectExtraLoss, result.Events.PushingOn!.AppliedExtraHpLoss);
            if (expectExtraLoss)
                Assert.True(member.CurrentHP < maxHp, "追加HP消費が適用されるはず");
            else
                Assert.Equal(maxHp, member.CurrentHP);
        }

        [Fact]
        public void PushingOn_DoesNotChangeQuestDuration()
        {
            // 深追いしても拘束期間は変えない（→ 03 §4.0.1と矛盾させないための決定）。
            var quest = ExplorationQuest();
            int durationBefore = quest.DurationWeeks;
            var rng = new SequenceRng(50, DoNotOccur, DoNotOccur, Occur, 5, 5);

            new QuestResolver(rng).Resolve(PartyOf(Member(vit: 90, mnd: 90)), quest);

            Assert.Equal(durationBefore, quest.DurationWeeks);
        }

        // ---------------- 討伐ではイベントが一切発生しない ----------------

        [Fact]
        public void SubjugationQuest_NeverTriggersAnyEvent()
        {
            // FixedRng(1)はすべての発生ロールが通る値だが、討伐はイベントの対象外。
            var member = Member(str: 90, vit: 90, dex: 90, mnd: 90, @int: 90);
            var quest = new Quest { QuestType = QuestType.Subjugation, Difficulty = 50, ScoutRequirement = 50, RewardGold = 300 };

            var result = new QuestResolver(new FixedRng(1)).Resolve(PartyOf(member), quest);

            Assert.False(result.Events.AnyOccurred);
            Assert.Null(result.Events.StrongEnemy);
            Assert.Null(result.Events.TreasureVault);
            Assert.Null(result.Events.PushingOn);
            Assert.Equal(0, result.Events.TotalBonusRewardGold);
        }

        // ---------------- 追加報酬の合算 ----------------

        [Fact]
        public void EventBonusRewards_AreAddedToRewardGold()
        {
            // 押し切り(+200)・宝物庫大成功(+400)・深追い大成功(+300) が同時に成立する編成。
            var member = Member(str: 90, vit: 90, agi: 90, mnd: 90, dex: 90, ldr: 90, @int: 90);
            var quest = ExplorationQuest();
            // [索敵, ①発生, ②発生, ③発生, HP消費%, (深追い大成功の)追加HP消費%]
            var rng = new SequenceRng(50, Occur, Occur, Occur, 20, 3);

            var result = new QuestResolver(rng).Resolve(PartyOf(member), quest);

            Assert.Equal(StrongEnemyOutcome.PushedThrough, result.Events.StrongEnemy!.Outcome);
            Assert.Equal(QuestEventOutcome.GreatSuccess, result.Events.TreasureVault!.Outcome);
            Assert.Equal(QuestEventOutcome.GreatSuccess, result.Events.PushingOn!.Outcome);
            Assert.Equal(900, result.Events.TotalBonusRewardGold);
            // クエスト自体の報酬（達成なら300）にイベント分が合算されている。
            Assert.Equal((result.QuestAchieved ? quest.RewardGold : 0) + 900, result.RewardGold);
        }
    }
}
