using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
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
    /// 後衛の神官）に絞り、開始直後の第1週「新春ドラフト」（→ RecruitmentSystem.StartInitialDraft、
    /// 2026年9月に旧チュートリアル採用試験を置き換え）でプレイヤー自身が2名を無料で選抜契約することで
    /// 計5名体制（4人出撃＋1人待機お手伝い）になる。
    /// 魔導士（旧エルシャ）はあえて初期メンバーから外し、ドラフト候補に必ず含まれる4職
    /// （魔導士・学者・騎士・盗賊）から「誰を足すか」を選ぶ最初の意思決定にしている。
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
                // 初期装備（2026年9月、武具の7大能力値補正）：職業ごとの基本装備を着た状態で加入する
                // （→ StarterEquipment。新春ドラフトの新人と同じ表：クラウディア＝鉄の剣＋革鎧、
                // リナ＝短剣＋革鎧、フィオナ＝メイス＋ローブ）。装備補正込みの最大HPでHPも満タンにする。
                StarterEquipment.Equip(a);

                // 配置（Placement）の初期値は職業から自動決定する（→ 03 §4.2）。
                a.Placement = PlacementRules.GetDefault(a.JobClass);
            }

            return list;
        }

        /// <summary>
        /// フィールド1つ分の定義（→ CreateDefaultFields）。Idは英語スラッグ、Themeはボス名の
        /// 生成に使う怪物名（ギミック種別ごと4種）。
        /// </summary>
        private readonly record struct FieldDefinition(string Id, string Name, int Order, string[] MonsterByGimmick);

        /// <summary>
        /// 大迷宮の全5フィールド（→ DungeonField・大迷宮5フィールド拡張仕様）。新規ゲーム開始時に
        /// GameState.DungeonFields へ設定する。各フィールドは10〜100階（→ BAL: dungeon.csv
        /// BossIntervalFloors刻み・計10体）の階層ボスを持つ。初期状態で開放済みなのは
        /// 第1フィールド（森）のみ（→ DungeonField.IsUnlocked、
        /// DungeonExpeditionSystem.ApplyFieldProgressionが順次開放する）。
        ///
        /// 設計方針：どのギミックにも「職業」または「ステータス合算」の対策口を必ず持たせる。
        /// 携行アイテム（→ ConsumableCatalog）はUIからの持ち込み手段がまだ無いため、
        /// アイテムだけが対策口のギミックを置くと攻略不能になってしまう。
        /// 第1フィールドの5階層目は初期メンバーの神官（フィオナ）で対策が成立する＝
        /// 「調べて、対策が揃っていれば勝てる」という導線を最初に体験させる難度にしてある。
        /// </summary>
        public static List<DungeonField> CreateDefaultFields()
        {
            var definitions = new List<FieldDefinition>
            {
                new("forest", "翠緑の原生林", 1, new[] { "毒蜘蛛", "甲殻の大猪", "森の怪鳥", "深淵樹霊" }),
                new("cave", "嘆きの鍾乳洞", 2, new[] { "瘴気の蝙蝠", "岩肌の巨蟹", "鍾乳洞の翼竜", "洞窟の古竜" }),
                new("ruins", "忘却の古代廃墟", 3, new[] { "腐敗した番人", "古代の鋼鉄兵", "廃墟の石像鬼", "忘却の守護者" }),
                new("canyon", "焦熱の峡谷", 4, new[] { "灼熱の毒蠍", "溶岩鎧の巨人", "峡谷の火竜", "焦熱の魔王" }),
                new("abyss", "深淵の特異点", 5, new[] { "深淵の腐蝕体", "漆黒の重装兵", "深淵の堕天使", "特異点の支配者" }),
            };

            return definitions.Select(def => new DungeonField
            {
                Id = def.Id,
                Name = def.Name,
                Order = def.Order,
                IsUnlocked = def.Order == 1, // 初期開放は第1フィールド（森）のみ
                Bosses = CreateFieldBosses(def),
            }).ToList();
        }

        /// <summary>
        /// 1フィールド分の10体（10, 20, ..., 100階、→ BAL: dungeon.csv BossIntervalFloors）を生成する。
        ///
        /// HP・報酬ゴールドは「段」（このフィールド内での何体目か＋フィールド順による格上げ）を
        /// 指数としてCSV係数で滑らかにスケールさせる（→ BAL: dungeon.csv
        /// BossBaseHp/BossHpFloorMultiplier/BossBaseRewardGold/BossRewardGoldMultiplier）。
        /// 森（Order=1）の10Fボス（段1）が基準値そのものになる。
        /// </summary>
        private static List<FloorBoss> CreateFieldBosses(FieldDefinition def)
        {
            var bosses = new List<FloorBoss>();

            for (int floor = DungeonField.BossInterval; floor <= DungeonField.MaxFloor; floor += DungeonField.BossInterval)
            {
                int index = floor / DungeonField.BossInterval - 1; // 0〜9
                int fieldEscalation = def.Order - 1; // フィールドが深いほど全体的に格上げ

                // ボス数が20体→10体になったため、強さの形容（5段階）・危険度の刻みも
                // 半分の間隔（2体ごと）に合わせて縮める（→ GimmickTierPrefix。旧モデルは4体ごと）。
                var gimmickType = (BossGimmickType)(index % 4); // Poison→HeavyArmor→Flying→InstantKillの順に循環
                string tier = GimmickTierPrefix(index / 2); // 2体ごとに強さの形容を変える（若き→…→災厄の）

                // HP・報酬ゴールド：段（index＋fieldEscalation）を指数にCSV係数で乗算する。
                int stageExponent = index + fieldEscalation;
                double hpMultiplier = Math.Pow(DungeonBalance.BossHpFloorMultiplier, stageExponent);
                double rewardMultiplier = Math.Pow(DungeonBalance.BossRewardGoldMultiplier, stageExponent);
                int maxHp = (int)Math.Round(DungeonBalance.BossBaseHp * hpMultiplier);
                int rewardGold = (int)Math.Round(DungeonBalance.BossBaseRewardGold * rewardMultiplier);

                int dangerLevel = Math.Clamp(1 + index / 2 + fieldEscalation, 1, 5);
                double counterThreshold = 40 + floor * 1.5 + fieldEscalation * 30;

                // 確定ドロップ素材（→ materials.csv、Balance.MaterialBalance）。
                // forest（Order=1）：全10体で3種を巡回させる（候補A最初の実装分）。
                // cave/ruins/canyon/abyss（Order 2〜5、2026年9月拡張）：節目ボス（10F・20F）のみ、
                // フィールド固有の希少素材を確定ドロップする（materials.csv側のMinFloorが高い
                // ＝採取では入手しづらい方の素材。ボス討伐という難所を通した安定供給経路にする）。
                string? rewardMaterialId = def.Order == 1
                    ? (index % 3) switch
                    {
                        0 => MaterialIds.ForestHerb,
                        1 => MaterialIds.ForestWood,
                        _ => MaterialIds.ForestSpore,
                    }
                    : (floor == 10 || floor == 20)
                        ? def.Id switch
                        {
                            "cave" => MaterialIds.CaveOre,
                            "ruins" => MaterialIds.RuinsRune,
                            "canyon" => MaterialIds.CanyonGem,
                            "abyss" => MaterialIds.AbyssCrystal,
                            _ => null,
                        }
                        : null;
                int rewardMaterialCount = rewardMaterialId != null ? Math.Clamp(3 + index / 4, 3, 5) : 0;

                bosses.Add(new FloorBoss
                {
                    Name = tier + def.MonsterByGimmick[index % 4],
                    Floor = floor,
                    MaxHp = maxHp,
                    CurrentHp = maxHp,
                    RewardGold = rewardGold,
                    RewardMaterialId = rewardMaterialId,
                    RewardMaterialCount = rewardMaterialCount,
                    Gimmicks = { CreateGimmick(gimmickType, dangerLevel, counterThreshold) },
                });
            }

            return bosses;
        }

        /// <summary>
        /// ギミック種別ごとの対策口（職業＋ステータス＋携行アイテム、→ DungeonResolver.IsCountered
        /// のOR判定）。4種すべてにアイテム対策口を持たせる（2026年9月、パーティ携行アイテム
        /// ポーチ仕様で全種対応：猛毒＝解毒薬、重装甲＝溶解液、飛行＝捕縛網、即死級＝護符）。
        /// </summary>
        private static BossGimmick CreateGimmick(BossGimmickType type, int dangerLevel, double counterThreshold) => type switch
        {
            BossGimmickType.Poison => new BossGimmick
            {
                Type = type, DangerLevel = dangerLevel,
                RequiredCounterRole = JobClass.Cleric,
                RequiredCounterStat = "MND", RequiredCounterStatThreshold = counterThreshold,
                RequiredItemId = ConsumableCatalog.AntidoteId,
            },
            BossGimmickType.HeavyArmor => new BossGimmick
            {
                Type = type, DangerLevel = dangerLevel,
                RequiredCounterRole = JobClass.Mage,
                RequiredCounterStat = "STR", RequiredCounterStatThreshold = counterThreshold,
                RequiredItemId = ConsumableCatalog.AcidFlaskId,
            },
            BossGimmickType.Flying => new BossGimmick
            {
                Type = type, DangerLevel = dangerLevel,
                RequiredCounterRole = JobClass.Ranger,
                RequiredCounterStat = "DEX", RequiredCounterStatThreshold = counterThreshold,
                RequiredItemId = ConsumableCatalog.NetId,
            },
            _ => new BossGimmick // InstantKill
            {
                Type = type, DangerLevel = dangerLevel,
                RequiredCounterRole = JobClass.Knight,
                RequiredCounterStat = "LDR", RequiredCounterStatThreshold = counterThreshold,
                RequiredItemId = ConsumableCatalog.CharmId,
            },
        };

        /// <summary>同じ怪物名が5回（20体÷4種）続けて出ないよう、強さの形容で変化を付ける（tier 0〜4）。</summary>
        private static string GimmickTierPrefix(int tier) => tier switch
        {
            0 => "若き",
            1 => "手練れの",
            2 => "歴戦の",
            3 => "伝説の",
            _ => "災厄の",
        };
    }
}
