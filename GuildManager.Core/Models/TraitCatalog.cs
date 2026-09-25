using System.Collections.Generic;
using GuildManager.Core.Balance;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// ゲーム内で使用可能な特性の静的カタログ。仕様書 03 §5.3・§4.3 参照。
    /// v1.4改訂：古傷（後天的）に加え、トラウマ（後天的）・豪胆／注意深い／容姿秀麗
    /// （いずれも先天的）を追加した（→ 03 §5.3.2）。「頑強」「師匠肌」等は引き続き
    /// post-MVP（→ §11）。
    /// 各TraitEffect.Valueは docs/04_バランス表/trait.csv 由来（→ TraitBalance、
    /// 項目58フォローアップ）。2026年9月：表示名・説明・障害フラグ（IsCurseOrInjury）も trait.csv の
    /// `{Id}_DisplayName` 等の行が正本になり（→ Define）、新規特性4種（耐毒体質・夜目・巨獣狩り・勤勉）を追加した。
    /// </summary>
    public static class TraitCatalog
    {
        public const string OldWoundId = "OldWound";
        public const string TraumaId = "Trauma";
        public const string BraveId = "Brave";
        public const string AttentiveId = "Attentive";
        public const string BeautifulId = "Beautiful";
        public const string CountryBredId = "CountryBred";
        public const string ScholarId = "Scholar";
        public const string MentorId = "Mentor";
        public const string ResistPoisonId = "ResistPoison";
        public const string NightVisionId = "NightVision";
        public const string GiantHunterId = "GiantHunter";
        public const string DiligentId = "Diligent";

        /// <summary>
        /// 古傷（不可逆障害）。STR/VIT/AGI/DEXを恒久的に低下させる（減少率→ BAL: 戦闘/古傷減少率）。
        /// MND/INT/LDRは対象外。出撃制限は課さない（→ 03 §4.3）。
        /// </summary>
        public static readonly TraitDefinition OldWound = Define(new TraitDefinition
        {
            Id = OldWoundId,
            BlocksDeployment = false,
            Effects = new List<TraitEffect>
            {
                new TraitEffect { EffectType = TraitEffectType.StatPercentReduction, TargetStat = "STR", Value = TraitBalance.OldWoundStatReduction },
                new TraitEffect { EffectType = TraitEffectType.StatPercentReduction, TargetStat = "VIT", Value = TraitBalance.OldWoundStatReduction },
                new TraitEffect { EffectType = TraitEffectType.StatPercentReduction, TargetStat = "AGI", Value = TraitBalance.OldWoundStatReduction },
                new TraitEffect { EffectType = TraitEffectType.StatPercentReduction, TargetStat = "DEX", Value = TraitBalance.OldWoundStatReduction },
            }
        });

        /// <summary>
        /// トラウマ（後天的）。戦死の現場に居合わせた生存者に確率で付与される（→ 03 §5.1・§5.3.1）。
        /// MNDを恒久的に低下させる（減少率→ BAL: 特性/トラウマ減少率）。
        /// </summary>
        public static readonly TraitDefinition Trauma = Define(new TraitDefinition
        {
            Id = TraumaId,
            BlocksDeployment = false,
            Effects = new List<TraitEffect>
            {
                new TraitEffect { EffectType = TraitEffectType.StatPercentReduction, TargetStat = "MND", Value = TraitBalance.TraumaMndReduction }
            }
        });

        /// <summary>
        /// 豪胆（先天的）。新人採用時に低確率で付与される。致死判定のSurvivalThreshold
        /// （→ 03 §4.3）に固定ボーナスを与える（→ BAL: 特性/豪胆ボーナス）。
        /// </summary>
        public static readonly TraitDefinition Brave = Define(new TraitDefinition
        {
            Id = BraveId,
            BlocksDeployment = false,
            IsTransmittable = true, // → 特性伝授刷新仕様：教官が持っていれば週次で伝授しうる
            Effects = new List<TraitEffect>
            {
                new TraitEffect { EffectType = TraitEffectType.SurvivalThresholdModifier, TargetStat = "", Value = TraitBalance.BraveSurvivalThresholdBonus }
            }
        });

        /// <summary>
        /// 注意深い（先天的）。新人採用時に低確率で付与される。索敵フェーズ（→ 03 §4.1）の
        /// DEX寄与を補正する（→ BAL: 特性/注意深い補正）。
        /// </summary>
        public static readonly TraitDefinition Attentive = Define(new TraitDefinition
        {
            Id = AttentiveId,
            BlocksDeployment = false,
            IsTransmittable = true, // → 特性伝授刷新仕様
            Effects = new List<TraitEffect>
            {
                new TraitEffect { EffectType = TraitEffectType.ScoutingModifier, TargetStat = "DEX", Value = TraitBalance.AttentiveScoutingBonus }
            }
        });

        /// <summary>
        /// 容姿秀麗（先天的）。新人採用時に低確率で付与される。このキャラクターが関与する
        /// 相性ペアの上昇量に倍率がかかる（下降量には影響しない。→ 03 §5.3.1・BAL: 特性/容姿秀麗倍率）。
        /// </summary>
        public static readonly TraitDefinition Beautiful = Define(new TraitDefinition
        {
            Id = BeautifulId,
            BlocksDeployment = false,
            IsTransmittable = false, // 先天的な容姿の特性のため、後天的な伝授の対象にはしない
            Effects = new List<TraitEffect>
            {
                new TraitEffect { EffectType = TraitEffectType.CompatibilityGainMultiplier, TargetStat = "", Value = TraitBalance.BeautifulCompatibilityGainMultiplier }
            }
        });

        /// <summary>
        /// 田舎育ち（先天的）。新人採用時に低確率で付与される。野山で培った目利きで、探索採取の
        /// 採取スコアに本人の採取寄与の+20%を上乗せする（→ GatheringResolver、03 §5.3.2・BAL: 特性。
        /// 2026年9月再設計：旧・探索クエストの個人スコア加算は旧クエストの撤去で効果を失っていた）。
        /// Id（"CountryBred"）はセーブ互換のため据え置く。
        /// </summary>
        public static readonly TraitDefinition CountryBred = Define(new TraitDefinition
        {
            Id = CountryBredId,
            BlocksDeployment = false,
            IsTransmittable = true, // → 特性伝授刷新仕様
            Effects = new List<TraitEffect>
            {
                new TraitEffect
                {
                    EffectType = TraitEffectType.GatheringScoreBonus,
                    TargetStat = "",
                    Value = TraitBalance.CountryBredGatheringBonusRate,
                }
            }
        });

        /// <summary>
        /// 知識人（先天的）。新人採用時に低確率で付与される。項目64時点では**単独の効果を持たない**
        /// （ペア特性シナジー専用の特性：「豪胆」との組み合わせで護衛クエストに負のシナジーを持つ。
        /// → 03 §4.2.3・pair_synergy.csv）。指示書（項目64）が「単独効果は無いか最小限でよい／
        /// trait.csvの該当行は省略可」としているため、独自の効果量を仮置きせず空のままにしてある。
        /// </summary>
        public static readonly TraitDefinition Scholar = Define(new TraitDefinition
        {
            Id = ScholarId,
            BlocksDeployment = false,
            Effects = new List<TraitEffect>(),
        });

        /// <summary>
        /// 師匠肌（先天的。→ 特性伝授刷新仕様で新設）。単独の戦闘効果は持たない
        /// （知識人と同じ、他システム専用のマーカー特性）。この特性を持つ引退済み冒険者が
        /// 訓練施設の教官として配置されていると、週次の特性伝授ロールに追加ボーナスが乗る
        /// （→ TrainingBalance.TraitTransmissionMentorBonusPercent・TrainingSystem.
        /// ProcessWeeklyTraitTransmission）。自身も伝授対象（教え上手は教え上手から学べる）。
        /// </summary>
        public static readonly TraitDefinition Mentor = Define(new TraitDefinition
        {
            Id = MentorId,
            BlocksDeployment = false,
            IsTransmittable = true,
            Effects = new List<TraitEffect>(),
        });

        // ---- 2026年9月新設：大迷宮ギミック対策・訓練系のコア特性4種（→ 03 §5.3.2） ----
        // 表示名・説明・障害フラグは trait.csv が正本。効果の数値（どのフロア・ボスで何%軽減するか等）は
        // 仕様がまだ決まっていないため、知識人・師匠肌と同じく効果リストを空のまま置く（数値を仮置きしない）。
        // 入手経路（採用時の先天付与・教官からの伝授など）も未接続（→ 06 タスクリスト「特性深化」）。

        /// <summary>耐毒体質：猛毒・瘴気フロアでの被弾損耗を大幅に軽減する（効果は未接続）。</summary>
        public static readonly TraitDefinition ResistPoison = Define(new TraitDefinition
        {
            Id = ResistPoisonId,
            Effects = new List<TraitEffect>(),
        });

        /// <summary>夜目：暗黒・濃霧フロアの踏破時、未踏破区間の進行損耗を抑制する（効果は未接続）。</summary>
        public static readonly TraitDefinition NightVision = Define(new TraitDefinition
        {
            Id = NightVisionId,
            Effects = new List<TraitEffect>(),
        });

        /// <summary>巨獣狩り：大型ボスに対する討伐能力を高める（効果は未接続）。</summary>
        public static readonly TraitDefinition GiantHunter = Define(new TraitDefinition
        {
            Id = GiantHunterId,
            Effects = new List<TraitEffect>(),
        });

        /// <summary>勤勉：訓練場に配置された際の能力成長判定確率が上昇する（効果は未接続）。</summary>
        public static readonly TraitDefinition Diligent = Define(new TraitDefinition
        {
            Id = DiligentId,
            Effects = new List<TraitEffect>(),
        });

        // 将来ここに「頑強」等を追加していく（post-MVP）。

        /// <summary>
        /// 構造だけを書いた定義へ、trait.csv の表示名・説明・障害フラグを流し込む
        /// （→ TraitBalance.GetDefinitionText。行が欠けていれば起動失敗）。
        /// </summary>
        private static TraitDefinition Define(TraitDefinition definition)
        {
            var text = TraitBalance.GetDefinitionText(definition.Id);
            definition.DisplayName = text.DisplayName;
            definition.Description = text.Description;
            definition.IsCurseOrInjury = text.IsCurseOrInjury;
            return definition;
        }

        /// <summary>カタログの全特性（定義順）。</summary>
        public static IReadOnlyList<TraitDefinition> GetAll() => new[]
        {
            OldWound, Trauma, Brave, Attentive, Beautiful, CountryBred, Scholar, Mentor,
            ResistPoison, NightVision, GiantHunter, Diligent,
        };

        public static TraitDefinition? FindById(string id) => id switch
        {
            ResistPoisonId => ResistPoison,
            NightVisionId => NightVision,
            GiantHunterId => GiantHunter,
            DiligentId => Diligent,
            OldWoundId => OldWound,
            TraumaId => Trauma,
            BraveId => Brave,
            AttentiveId => Attentive,
            BeautifulId => Beautiful,
            CountryBredId => CountryBred,
            ScholarId => Scholar,
            MentorId => Mentor,
            _ => null,
        };
    }
}
