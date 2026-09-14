using System;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// 低危険度任務（採取・巡回）の解決に関するテスト。
    /// → コアシステム刷新仕様「(2) 自動探索・戦闘シミュレーション」
    ///   （採取量の人数比例・負傷判定による即詰み防止）。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class LowDangerQuestTests
    {
        /// <summary>NextInt(min, max) が常に max を返すテスト用スタブ（＝最悪の結果を強制する）。</summary>
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

        private static Adventurer MakeAdventurer(int stat = 50)
        {
            var a = new Adventurer { STR = stat, AGI = stat, VIT = stat, MND = stat, DEX = stat, LDR = stat, INT = stat };
            a.CurrentHP = a.MaxHP;
            return a;
        }

        private static Party PartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members) party.TryAdd(m);
            return party;
        }

        // ---------------- 採取量の人数比例（→ 仕様「素材数 ＝ 基本数 × 派遣人数」） ----------------

        [Fact]
        public void Resolve_GatheringReward_ScalesWithMemberCount()
        {
            // 難易度1＝ほぼ確実に成功する採取任務。報酬が派遣人数に正比例することを確認する。
            var soloQuest = new Quest { QuestType = QuestType.Gathering, Difficulty = 1, RewardGold = 50 };
            var fourQuest = new Quest { QuestType = QuestType.Gathering, Difficulty = 1, RewardGold = 50 };

            var solo = new QuestResolver(new FixedRng(50)).Resolve(PartyOf(MakeAdventurer()), soloQuest);
            var four = new QuestResolver(new FixedRng(50)).Resolve(
                PartyOf(MakeAdventurer(), MakeAdventurer(), MakeAdventurer(), MakeAdventurer()), fourQuest);

            Assert.True(solo.QuestAchieved);
            Assert.True(four.QuestAchieved);
            Assert.Equal(50, solo.RewardGold);
            Assert.Equal(200, four.RewardGold); // 基本量50 × 4名
        }

        [Fact]
        public void Resolve_SubjugationReward_DoesNotScaleWithMemberCount()
        {
            // 採取以外は人数を増やしても報酬が増えない（人数を割く意味が採取とは異なる）。
            var soloQuest = new Quest { QuestType = QuestType.Subjugation, Difficulty = 1, RewardGold = 50 };
            var fourQuest = new Quest { QuestType = QuestType.Subjugation, Difficulty = 1, RewardGold = 50 };

            var solo = new QuestResolver(new FixedRng(50)).Resolve(PartyOf(MakeAdventurer()), soloQuest);
            var four = new QuestResolver(new FixedRng(50)).Resolve(
                PartyOf(MakeAdventurer(), MakeAdventurer(), MakeAdventurer(), MakeAdventurer()), fourQuest);

            Assert.Equal(50, solo.RewardGold);
            Assert.Equal(50, four.RewardGold);
        }

        [Fact]
        public void Resolve_GatheringReward_IsZero_WhenQuestFailed()
        {
            // 失敗時は人数に関わらず報酬0（比例は成功時のみ）。
            var quest = new Quest { QuestType = QuestType.Gathering, Difficulty = 100, RewardGold = 50 };
            var resolver = new QuestResolver(new AlwaysMaxRng());

            var result = resolver.Resolve(PartyOf(MakeAdventurer(5), MakeAdventurer(5)), quest);

            Assert.False(result.QuestAchieved);
            Assert.Equal(0, result.RewardGold);
        }

        // ---------------- 負傷判定（即詰み防止。→ 仕様「HPが0以下でもロストさせない」） ----------------

        [Fact]
        public void Resolve_LowDangerQuestFailure_NeverKills_AppliesLightInjuryInstead()
        {
            // 消耗した状態（残HP30%）で難易度の高い採取任務に失敗させ、最悪の乱数を引かせる。
            // それでも戦死・ダウンは発生せず、数週間の軽傷で済む＝人員を永久に失わない。
            var exhausted = MakeAdventurer();
            exhausted.CurrentHP = exhausted.MaxHP * 30 / 100;

            var quest = new Quest { QuestType = QuestType.Gathering, Difficulty = 100, RewardGold = 10 };
            var resolver = new QuestResolver(new AlwaysMaxRng());

            var result = resolver.Resolve(PartyOf(exhausted), quest);

            Assert.False(result.QuestAchieved);
            Assert.Empty(result.FallenAdventurerIds);   // 戦死は発生しない
            Assert.Empty(result.DownedAdventurerIds);   // ダウン（致死判定）にも入らない
            Assert.True(exhausted.CurrentHP >= 1, "低危険度任務ではHPが1未満にならない");

            Assert.Contains(exhausted.Id, result.LightlyInjuredAdventurerIds);
            Assert.Equal(InjurySeverity.Light, exhausted.Injury);
            Assert.InRange(exhausted.InjuryWeeksRemaining,
                QuestScoringBalance.LightInjuryWeeksMin, QuestScoringBalance.LightInjuryWeeksMax);
        }

        [Fact]
        public void Resolve_LowDangerQuest_DoesNotInjureMemberWhoKeptEnoughHp()
        {
            // 余力を残して帰還したメンバーは無傷（＝毎回必ず休養が必要になるわけではない）。
            var healthy = MakeAdventurer();
            var quest = new Quest { QuestType = QuestType.Gathering, Difficulty = 1, RewardGold = 10 };

            var result = new QuestResolver(new FixedRng(50)).Resolve(PartyOf(healthy), quest);

            Assert.Empty(result.LightlyInjuredAdventurerIds);
            Assert.Equal(InjurySeverity.None, healthy.Injury);
        }

        [Fact]
        public void Resolve_LightInjury_DoesNotOverwriteExistingSevereInjury()
        {
            // 既に重傷（全治5週）の者を軽傷で上書きして全治期間を短縮してしまわないこと。
            var wounded = MakeAdventurer();
            wounded.CurrentHP = 1;
            wounded.Injury = InjurySeverity.Severe;
            wounded.InjuryWeeksRemaining = 5;

            var quest = new Quest { QuestType = QuestType.Gathering, Difficulty = 100, RewardGold = 10 };

            new QuestResolver(new AlwaysMaxRng()).Resolve(PartyOf(wounded), quest);

            Assert.Equal(InjurySeverity.Severe, wounded.Injury);
            Assert.Equal(5, wounded.InjuryWeeksRemaining);
        }

        [Fact]
        public void Resolve_PatrolQuest_UsesNonCombatFlow_AndNeverKills()
        {
            // 巡回も採取と同じ低危険度フロー（致死判定を通らない）であることの確認。
            var weak = MakeAdventurer(5);
            weak.CurrentHP = 1;
            var quest = new Quest { QuestType = QuestType.Patrol, Difficulty = 100, RewardGold = 10 };

            var result = new QuestResolver(new AlwaysMaxRng()).Resolve(PartyOf(weak), quest);

            Assert.Empty(result.FallenAdventurerIds);
            Assert.Null(result.Outcome);            // 討伐の4区分ではない
            Assert.NotNull(result.NonCombatOutcome); // 3区分で判定されている
            Assert.True(weak.CurrentHP >= 1);
        }
    }
}
