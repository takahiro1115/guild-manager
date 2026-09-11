using System;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;
using Xunit;

namespace GuildManager.Core.Tests
{
    /// <summary>
    /// レベルアップ制度（仕様書 03 §3.8）のテスト。
    /// 実行方法: このフォルダで `dotnet test`
    /// </summary>
    public class LevelingSystemTests
    {
        /// <summary>NextInt(min, max) が常に min を返すテスト用スタブ。
        /// レベルアップ時のステータス抽選を先頭（STR）に固定できる。</summary>
        private class AlwaysMinRng : IRng
        {
            public int NextInt(int min, int max) => min;
        }

        private static Party CreatePartyOf(params Adventurer[] members)
        {
            var party = new Party();
            foreach (var m in members)
                party.TryAdd(m);
            return party;
        }

        [Fact]
        public void AwardExperience_GrantsXpEqualToQuestDifficulty()
        {
            var adventurer = new Adventurer { Experience = 0, Level = 1 };
            var party = CreatePartyOf(adventurer);
            var quest = new Quest { Difficulty = 10 };
            var result = new WeekResolutionResult();
            var system = new LevelingSystem(new AlwaysMinRng());

            system.AwardExperience(party, quest, result);

            Assert.Equal(10, adventurer.Experience);
            Assert.Equal(1, adventurer.Level); // レベル1→2には50必要なのでまだ上がらない
        }

        [Fact]
        public void AwardExperience_DownedAdventurer_GetsNoExperience()
        {
            var adventurer = new Adventurer { Experience = 0, Level = 1 };
            var party = CreatePartyOf(adventurer);
            var quest = new Quest { Difficulty = 30 };
            var result = new WeekResolutionResult { DownedAdventurerIds = { adventurer.Id } };
            var system = new LevelingSystem(new AlwaysMinRng());

            system.AwardExperience(party, quest, result);

            Assert.Equal(0, adventurer.Experience);
            Assert.Equal(1, adventurer.Level);
        }

        [Fact]
        public void AwardExperience_OnlyDownedMembersAreExcluded_OthersStillGainXp()
        {
            var downed = new Adventurer();
            var survivor = new Adventurer();
            var party = CreatePartyOf(downed, survivor);
            var quest = new Quest { Difficulty = 15 };
            var result = new WeekResolutionResult { DownedAdventurerIds = { downed.Id } };
            var system = new LevelingSystem(new AlwaysMinRng());

            system.AwardExperience(party, quest, result);

            Assert.Equal(0, downed.Experience);
            Assert.Equal(15, survivor.Experience);
        }

        [Fact]
        public void AwardExperience_LevelsUpWhenThresholdReached_AndCarriesOverRemainder()
        {
            // レベル1→2に必要な経験値は 1*50=50。難易度60を獲得 → レベル2、残り経験値10。
            var adventurer = new Adventurer { STR = 40, PA_STR = 80 };
            var party = CreatePartyOf(adventurer);
            var quest = new Quest { Difficulty = 60 };
            var result = new WeekResolutionResult();
            var system = new LevelingSystem(new AlwaysMinRng());

            system.AwardExperience(party, quest, result);

            Assert.Equal(2, adventurer.Level);
            Assert.Equal(10, adventurer.Experience);
        }

        [Fact]
        public void AwardExperience_LevelUp_GrowsChosenStatAndItsPaCap()
        {
            // AlwaysMinRngは常にAllStatNamesの先頭＝STRを選ぶ。
            var adventurer = new Adventurer { STR = 40, PA_STR = 80, AGI = 40, PA_AGI = 80 };
            var party = CreatePartyOf(adventurer);
            var quest = new Quest { Difficulty = 50 }; // ちょうどレベル1→2の閾値
            var result = new WeekResolutionResult();
            var system = new LevelingSystem(new AlwaysMinRng());

            system.AwardExperience(party, quest, result);

            Assert.Equal(2, adventurer.Level);
            Assert.Equal(42, adventurer.STR);   // +2（仮値）
            Assert.Equal(82, adventurer.PA_STR); // PAも同じ量だけ伸びる
            Assert.Equal(40, adventurer.AGI);   // 選ばれなかったステータスは不変
            Assert.Equal(80, adventurer.PA_AGI);
        }

        [Fact]
        public void AwardExperience_LevelUpGrowth_ClampsAtMaxStatValue()
        {
            var adventurer = new Adventurer { STR = 99, PA_STR = 99 };
            var party = CreatePartyOf(adventurer);
            var quest = new Quest { Difficulty = 50 };
            var result = new WeekResolutionResult();
            var system = new LevelingSystem(new AlwaysMinRng());

            system.AwardExperience(party, quest, result);

            Assert.Equal(100, adventurer.STR);
            Assert.Equal(100, adventurer.PA_STR);
        }

        [Fact]
        public void AwardExperience_CanTriggerMultipleLevelUpsFromOneLargeGain()
        {
            // レベル1→2に50、2→3に100必要。難易度200を一括獲得 → レベル3まで上がる。
            var adventurer = new Adventurer();
            var party = CreatePartyOf(adventurer);
            var quest = new Quest { Difficulty = 200 };
            var result = new WeekResolutionResult();
            var system = new LevelingSystem(new AlwaysMinRng());

            system.AwardExperience(party, quest, result);

            Assert.Equal(3, adventurer.Level);
            Assert.Equal(50, adventurer.Experience); // 200 - 50 - 100 = 50
        }

        [Fact]
        public void AwardExperience_StopsAtLevelCapAndDiscardsSurplusExperience()
        {
            var adventurer = new Adventurer { Level = 20, Experience = 0 };
            var party = CreatePartyOf(adventurer);
            var quest = new Quest { Difficulty = 100 };
            var result = new WeekResolutionResult();
            var system = new LevelingSystem(new AlwaysMinRng());

            system.AwardExperience(party, quest, result);

            Assert.Equal(20, adventurer.Level); // 上限を超えない
            Assert.Equal(0, adventurer.Experience); // 余剰経験値は切り捨てられる
        }
    }
}
