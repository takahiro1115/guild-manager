using System.Collections.Generic;
using GuildManager.Core.Models;

namespace GuildManager.Core.Data
{
    /// <summary>
    /// MVP動作確認用の固定データ。
    /// 初期メンバーは採用システム対象外（→ 03 §2.4）のため、氏名ジェネレーター
    /// （NameGenerator、v1.8改訂で新設）は使わず、既存どおり固定の直書きとする
    /// （→ docs/06_タスクリスト.md Phase 1「固定データで冒険者4〜6名・クエスト2〜3件を用意」）。
    /// 性格はまだ無いため、値はすべて仮の直書き。
    /// PAは「新人はステータス実効値 &lt; PA」（仕様書03 §2.2）に沿って現在値より少し高めに設定してある。
    ///
    /// 初期編成改訂（→ 初期編成改訂仕様）：固定初期メンバーを旧4名から3名（前衛の重戦士・斥候、
    /// 後衛の神官）に絞り、開始直後の第1週チュートリアル採用試験（→ RecruitmentSystem.
    /// IsTutorialRecruitmentWeek）でプレイヤー自身が3名を選抜契約することで計6名体制になる。
    /// 魔導士（旧エルシャ）はあえて初期メンバーから外し、「魔導士は自分で選んで採用する」
    /// 最初の意思決定をチュートリアルに組み込んでいる。
    ///
    /// 世界観設定改訂（→ 女性限定ギルド仕様）：本ギルドは女性限定のため、初期メンバー・
    /// 新規採用候補（→ RecruitmentSystem・recruitment.csv MaleGenderChancePercent=0）とも
    /// 全員 Gender.Female で統一する。旧「ガレス」（Gender.Male）は「クラウディア」に
    /// 改名・性別変更した（ステータス・職業・週給は据え置き、能力データとしては同一人物の
    /// 引き継ぎ）。
    /// </summary>
    public static class SampleData
    {
        public static List<Adventurer> CreateStarterAdventurers()
        {
            var list = new List<Adventurer>
            {
                new Adventurer
                {
                    Name = "クラウディア", Age = 24, JobClass = JobClass.Warrior, Gender = Gender.Female,
                    // → 項目62。対応するポートレート画像（claudia.png）は未用意のため、
                    // Godot側のフォールバック仕様（PortraitId不明時はunknown_silhouette.png）により
                    // シルエット表示になる（→ Adventurer.PortraitIdのクラスdocコメント）。
                    PortraitId = "claudia",
                    STR = 60, AGI = 35, VIT = 55, MND = 10, DEX = 20, LDR = 40,
                    INT = 15, PA_INT = 20, // v1.2改訂：INT活性化に伴う暫定値。重戦士は低め
                    PA_STR = 80, PA_AGI = 50, PA_VIT = 75, PA_MND = 15, PA_DEX = 30, PA_LDR = 55,
                    WeeklyWage = 55
                },
                new Adventurer
                {
                    Name = "リナ", Age = 21, JobClass = JobClass.Ranger, Gender = Gender.Female,
                    PortraitId = "rina", // → 項目62。guild-manager.godot/assets/portraits/rina.png
                    STR = 30, AGI = 60, VIT = 35, MND = 15, DEX = 55, LDR = 25,
                    INT = 20, PA_INT = 30, // v1.2改訂：INT活性化に伴う暫定値。斥候は中間程度
                    PA_STR = 45, PA_AGI = 85, PA_VIT = 50, PA_MND = 20, PA_DEX = 80, PA_LDR = 35,
                    WeeklyWage = 40
                },
                new Adventurer
                {
                    Name = "フィオナ", Age = 23, JobClass = JobClass.Cleric, Gender = Gender.Female,
                    PortraitId = "fiona", // → 項目62。guild-manager.godot/assets/portraits/fiona.png
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

        /// <summary>
        /// 大迷宮の階層ボス（→ FloorBoss・ダンジョン攻略システム）。新規ゲーム開始時に
        /// GameState.FloorBosses へ設定する。
        ///
        /// 設計方針：どのギミックにも「職業」または「ステータス合算」の対策口を必ず持たせる。
        /// 携行アイテム（→ ConsumableCatalog）はUIからの持ち込み手段がまだ無いため、
        /// アイテムだけが対策口のギミックを置くと攻略不能になってしまう。
        /// 第1層は初期メンバーの神官（フィオナ）で対策が成立する＝「調べて、対策が揃っていれば勝てる」
        /// という導線を最初に体験させる難度にしてある。
        /// </summary>
        public static List<FloorBoss> CreateFloorBosses()
        {
            return new List<FloorBoss>
            {
                MakeBoss("腐毒の大蜘蛛", floor: 1, maxHp: 600,
                    new BossGimmick
                    {
                        Type = BossGimmickType.Poison, DangerLevel = 2,
                        RequiredCounterRole = JobClass.Cleric,
                        RequiredCounterStat = "MND", RequiredCounterStatThreshold = 90,
                        RequiredItemId = ConsumableCatalog.AntidoteId,
                    }),
                MakeBoss("鋼殻の翼竜", floor: 2, maxHp: 1200,
                    new BossGimmick
                    {
                        Type = BossGimmickType.HeavyArmor, DangerLevel = 2,
                        RequiredCounterRole = JobClass.Mage,
                        RequiredCounterStat = "STR", RequiredCounterStatThreshold = 140,
                    },
                    new BossGimmick
                    {
                        Type = BossGimmickType.Flying, DangerLevel = 2,
                        RequiredCounterRole = JobClass.Ranger,
                        RequiredCounterStat = "DEX", RequiredCounterStatThreshold = 130,
                    }),
                MakeBoss("冥府の門番", floor: 3, maxHp: 2000,
                    new BossGimmick
                    {
                        Type = BossGimmickType.Poison, DangerLevel = 2,
                        RequiredCounterRole = JobClass.Cleric,
                        RequiredCounterStat = "MND", RequiredCounterStatThreshold = 120,
                        RequiredItemId = ConsumableCatalog.AntidoteId,
                    },
                    new BossGimmick
                    {
                        Type = BossGimmickType.InstantKill, DangerLevel = 5,
                        RequiredCounterRole = JobClass.Knight,
                        RequiredCounterStat = "LDR", RequiredCounterStatThreshold = 160,
                        RequiredItemId = ConsumableCatalog.CharmId,
                    }),
                MakeBoss("天穹の古竜", floor: 4, maxHp: 3200,
                    new BossGimmick
                    {
                        Type = BossGimmickType.Flying, DangerLevel = 3,
                        RequiredCounterRole = JobClass.Ranger,
                        RequiredCounterStat = "DEX", RequiredCounterStatThreshold = 180,
                    },
                    new BossGimmick
                    {
                        Type = BossGimmickType.HeavyArmor, DangerLevel = 3,
                        RequiredCounterRole = JobClass.Mage,
                        RequiredCounterStat = "STR", RequiredCounterStatThreshold = 200,
                    },
                    new BossGimmick
                    {
                        Type = BossGimmickType.InstantKill, DangerLevel = 5,
                        RequiredCounterRole = JobClass.Scholar,
                        RequiredCounterStat = "INT", RequiredCounterStatThreshold = 180,
                        RequiredItemId = ConsumableCatalog.CharmId,
                    }),
            };
        }

        private static FloorBoss MakeBoss(string name, int floor, int maxHp, params BossGimmick[] gimmicks) => new FloorBoss
        {
            Name = name,
            Floor = floor,
            MaxHp = maxHp,
            CurrentHp = maxHp,
            Gimmicks = new List<BossGimmick>(gimmicks),
        };
    }
}
