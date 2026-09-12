using System.Collections.Generic;
using GuildManager.Core.Models;

namespace GuildManager.Core.Data
{
    /// <summary>
    /// MVP動作確認用の固定データ。
    /// 初期メンバー4名は採用システム対象外（→ 03 §2.4）のため、氏名ジェネレーター
    /// （NameGenerator、v1.8改訂で新設）は使わず、既存どおり固定の直書きとする
    /// （→ docs/06_タスクリスト.md Phase 1「固定データで冒険者4〜6名・クエスト2〜3件を用意」）。
    /// 性格はまだ無いため、値はすべて仮の直書き。
    /// PAは「新人はステータス実効値 &lt; PA」（仕様書03 §2.2）に沿って現在値より少し高めに設定してある。
    /// </summary>
    public static class SampleData
    {
        public static List<Adventurer> CreateStarterAdventurers()
        {
            var list = new List<Adventurer>
            {
                new Adventurer
                {
                    Name = "ガレス", Age = 24, JobClass = JobClass.Warrior, Gender = Gender.Male,
                    STR = 60, AGI = 35, VIT = 55, MND = 10, DEX = 20, LDR = 40,
                    INT = 15, PA_INT = 20, // v1.2改訂：INT活性化に伴う暫定値。重戦士は低め
                    PA_STR = 80, PA_AGI = 50, PA_VIT = 75, PA_MND = 15, PA_DEX = 30, PA_LDR = 55,
                    WeeklyWage = 55
                },
                new Adventurer
                {
                    Name = "リナ", Age = 21, JobClass = JobClass.Ranger, Gender = Gender.Female,
                    STR = 30, AGI = 60, VIT = 35, MND = 15, DEX = 55, LDR = 25,
                    INT = 20, PA_INT = 30, // v1.2改訂：INT活性化に伴う暫定値。斥候は中間程度
                    PA_STR = 45, PA_AGI = 85, PA_VIT = 50, PA_MND = 20, PA_DEX = 80, PA_LDR = 35,
                    WeeklyWage = 40
                },
                new Adventurer
                {
                    Name = "エルシャ", Age = 26, JobClass = JobClass.Mage, Gender = Gender.Female,
                    STR = 10, AGI = 25, VIT = 25, MND = 65, DEX = 20, LDR = 30,
                    INT = 50, PA_INT = 80, // v1.2改訂：INT活性化に伴う暫定値。魔導士は高め
                    PA_STR = 15, PA_AGI = 35, PA_VIT = 35, PA_MND = 90, PA_DEX = 30, PA_LDR = 40,
                    WeeklyWage = 60
                },
                new Adventurer
                {
                    Name = "フィオナ", Age = 23, JobClass = JobClass.Cleric, Gender = Gender.Female,
                    STR = 15, AGI = 20, VIT = 30, MND = 45, DEX = 15, LDR = 20,
                    INT = 25, PA_INT = 35, // v1.2改訂：INT活性化に伴う暫定値。神官は中間〜低め
                    PA_STR = 20, PA_AGI = 30, PA_VIT = 40, PA_MND = 70, PA_DEX = 25, PA_LDR = 30,
                    WeeklyWage = 45
                },
            };

            foreach (var a in list)
            {
                // 開始時はHPを満タンにしておく（MaxHPはVITから計算されるため、
                // ステータス設定後にここで初期化する）。
                a.CurrentHP = a.MaxHP;

                // 配置（Placement）の初期値は職業から自動決定する（→ 03 §4.2）。
                a.Placement = PlacementRules.GetDefault(a.JobClass);
            }

            return list;
        }

        public static List<Quest> CreateStarterQuests()
        {
            return new List<Quest>
            {
                new Quest
                {
                    // → BAL: クエスト/期限。v1.10改訂でDeadlineWeeksの暫定レンジを5〜8週に
                    // 引き上げた（→ QuestBalance.MinDeadlineWeeks/MaxDeadlineWeeks）。
                    Name = "ゴブリン討伐", QuestType = QuestType.Subjugation, Rank = QuestRank.E,
                    Difficulty = 10, ScoutRequirement = 10,
                    RewardGold = 90, DeadlineWeeks = 5
                },
                new Quest
                {
                    Name = "山道の盗賊退治", QuestType = QuestType.Subjugation, Rank = QuestRank.D,
                    Difficulty = 22, ScoutRequirement = 18,
                    RewardGold = 180, DeadlineWeeks = 6
                },
                new Quest
                {
                    Name = "廃坑の魔物調査", QuestType = QuestType.Exploration, Rank = QuestRank.C,
                    Difficulty = 35, ScoutRequirement = 30,
                    RewardGold = 320, DeadlineWeeks = 7
                },
            };
        }
    }
}
