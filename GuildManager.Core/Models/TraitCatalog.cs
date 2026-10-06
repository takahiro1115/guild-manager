using System.Linq;
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
        public const string HawkEyeId = "HawkEye";
        public const string SixthSenseId = "SixthSense";
        public const string GuardianId = "Guardian";
        public const string PathfinderId = "Pathfinder";
        public const string SturdyId = "Sturdy";
        public const string QuickHealerId = "QuickHealer";
        public const string CheerfulId = "Cheerful";
        public const string HardworkerId = "Hardworker";
        public const string FireMageId = "FireMage";
        public const string SwordMasterId = "SwordMaster";
        public const string MapReaderId = "MapReader";
        // 生まれつきの欠点（§0.56）
        public const string CowardId = "Coward";
        public const string ClumsyId = "Clumsy";
        public const string PoorDirectionId = "PoorDirection";
        public const string RecklessId = "Reckless";
        public const string SicklyId = "Sickly";
        public const string FickleId = "Fickle";
        public const string MoodyId = "Moody";
        public const string SlothfulId = "Slothful";
        public const string SpendthriftId = "Spendthrift";
        // 後天の障害（§0.56）
        public const string PoisonAftereffectId = "PoisonAftereffect";
        public const string LegWoundId = "LegWound";
        public const string ArmWoundId = "ArmWound";
        public const string DreadId = "Dread";
        public const string BurnoutId = "Burnout";
        // レア特性（§0.57）
        public const string GeniusId = "Genius";
        public const string ArchmageId = "Archmage";
        public const string SwordSaintId = "SwordSaint";
        public const string SaintId = "Saint";
        public const string CharismaId = "Charisma";
        public const string ImmortalBodyId = "ImmortalBody";
        public const string IdatenId = "Idaten";
        public const string MarksmanId = "Marksman";
        // 成長・素質系と戦闘系（§0.62）
        public const string LateBloomerId = "LateBloomer";
        public const string EarlyBloomerId = "EarlyBloomer";
        public const string SoulChildId = "SoulChild";
        public const string VeteranId = "Veteran";
        public const string InspiringId = "Inspiring";

        /// <summary>
        /// ソードマスター（→ SwordMaster）が「剣」とみなす武器のカタログId。鑑定で出た個体もカタログIdで判定する。
        /// 短剣は盗賊の武器のため含めない（2026年10月・§0.55）。構造値のためCSV化しない。
        /// </summary>
        public static readonly IReadOnlyCollection<string> SwordWeaponItemIds = new HashSet<string>
        {
            ItemCatalog.IronSwordId, ItemCatalog.GreatSwordId,
        };

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
        /// 豪胆（先天的）。新人採用時に低確率で付与される。階層ボス戦で本人が受けるHP損耗率を
        /// 固定ポイント減らす（踏みとどまる。→ DungeonResolver、BAL: trait.csv BraveSurvivalThresholdBonus。
        /// §0.53で旧・致死判定の生存閾値ボーナスから付け直した）。
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
        /// 注意深い（先天的）。新人採用時に低確率で付与される。隠密への本人の寄与（AGI＋DEX）を
        /// 割合で高める（→ ScoutingResolver.GetStealthValue、BAL: trait.csv AttentiveScoutingBonus。
        /// §0.53で旧・索敵フェーズのDEX寄与補正から付け直した）。
        /// </summary>
        public static readonly TraitDefinition Attentive = Define(new TraitDefinition
        {
            Id = AttentiveId,
            BlocksDeployment = false,
            IsTransmittable = true, // → 特性伝授刷新仕様
            Effects = new List<TraitEffect>
            {
                new TraitEffect { EffectType = TraitEffectType.ScoutingModifier, TargetStat = "", Value = TraitBalance.AttentiveScoutingBonus }
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
        /// 知識人（先天的）。新人採用時に低確率で付与される。解析への本人の寄与（INT×係数）を割合で高める
        /// （→ ScoutingResolver.GetAnalysisValue、BAL: trait.csv ScholarAnalysisBonus）。
        /// §0.53で付与：旧来はペア特性シナジー専用で単独の効果を持たず、シナジーの撤去（旧クエスト撤去）で効果が無くなっていた。
        /// </summary>
        public static readonly TraitDefinition Scholar = Define(new TraitDefinition
        {
            Id = ScholarId,
            IsTransmittable = true, // 教官深化 Step 1（§0.34）：障害特性と容姿秀麗以外はすべて伝授対象
            BlocksDeployment = false,
            Effects = new List<TraitEffect>
            {
                new TraitEffect { EffectType = TraitEffectType.AnalysisModifier, TargetStat = "", Value = TraitBalance.ScholarAnalysisBonus }
            },
        });

        /// <summary>
        /// 師匠肌（先天的。→ 特性伝授刷新仕様で新設）。単独の戦闘効果は持たない
        /// （他システム専用のマーカー特性）。この特性を持つ引退済み冒険者が
        /// 訓練施設の教官として配置されていると、週次の特性伝授ロールに追加ボーナスが乗る
        /// （→ TrainingBalance.MentorTraitInheritanceBonus・TrainingSystem.
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
        // 表示名・説明・障害フラグは trait.csv が正本。効果は §0.32 で各システムに接続済みで、TraitEffect のリストではなく
        // 各システムが HasTrait で判定する（効果の数値は combat.csv・dungeon_traversal.csv・training.csv）。そのため Effects は空。
        // 入手経路は採用時の先天付与（→ RecruitmentSystem.InnateTraitPool）・教官からの伝授・巨獣狩りの後天開眼。

        /// <summary>耐毒体質：未対策の「猛毒」ギミックによる被ダメージの上乗せを軽減する（→ DungeonResolver、CombatBalance.ResistPoisonDamageReductionRate）。</summary>
        public static readonly TraitDefinition ResistPoison = Define(new TraitDefinition
        {
            Id = ResistPoisonId,
            IsTransmittable = true, // 教官深化 Step 1（§0.34）：障害特性と容姿秀麗以外はすべて伝授対象
            Effects = new List<TraitEffect>(),
        });

        /// <summary>夜目：部隊に1人いれば、道中の未踏破階層の損耗を軽減する（→ DungeonTraversalResolver.UnexploredLossMultiplier）。</summary>
        public static readonly TraitDefinition NightVision = Define(new TraitDefinition
        {
            Id = NightVisionId,
            IsTransmittable = true, // 教官深化 Step 1（§0.34）：障害特性と容姿秀麗以外はすべて伝授対象
            Effects = new List<TraitEffect>(),
        });

        /// <summary>巨獣狩り：「重装甲」ギミックのボスに対して本人の討伐火力が上がる（→ DungeonPowerCalculator.MemberPower）。</summary>
        public static readonly TraitDefinition GiantHunter = Define(new TraitDefinition
        {
            Id = GiantHunterId,
            IsTransmittable = true, // 教官深化 Step 1（§0.34）：障害特性と容姿秀麗以外はすべて伝授対象
            Effects = new List<TraitEffect>(),
        });

        /// <summary>勤勉：訓練施設での成長判定の基礎確率が上がる（→ GrowthSystem.ProcessTrainingGrowth、TrainingBalance.DiligentGrowthRateBonus）。</summary>
        public static readonly TraitDefinition Diligent = Define(new TraitDefinition
        {
            Id = DiligentId,
            IsTransmittable = true, // 教官深化 Step 1（§0.34）：障害特性と容姿秀麗以外はすべて伝授対象
            Effects = new List<TraitEffect>(),
        });

        // ---- 2026年10月・§0.55で新設：効いていなかった場所を埋める10種（→ 03 §5.3.2） ----
        // 表示名・説明・障害フラグ・効果の数値は trait.csv。火の魔術師だけは能力値の割合補正（StatPercentReduction の正の値）として
        // Effects に持ち、残りは各システムが HasTrait で判定する（Effects は空）。すべて先天プール（→ RecruitmentSystem.InnateTraitPool）に入り、伝授できる。

        /// <summary>鷹の目：部隊に1人いれば、未対策の「飛行」による被ダメージの上乗せを軽減する（→ DungeonResolver.CalculateDamageMultiplier）。</summary>
        public static readonly TraitDefinition HawkEye = Define(new TraitDefinition { Id = HawkEyeId, IsTransmittable = true, Effects = new List<TraitEffect>() });

        /// <summary>危機察知：未対策の「即死級」を受けても、本人のHP消費率を全損ではなく SixthSenseInstantKillHpLossPct にする（→ DungeonResolver.ApplyHpLoss）。</summary>
        public static readonly TraitDefinition SixthSense = Define(new TraitDefinition { Id = SixthSenseId, IsTransmittable = true, Effects = new List<TraitEffect>() });

        /// <summary>守り手：本人の護衛値が上がる（→ ScoutingResolver.GetGuardValue）。</summary>
        public static readonly TraitDefinition Guardian = Define(new TraitDefinition { Id = GuardianId, IsTransmittable = true, Effects = new List<TraitEffect>() });

        /// <summary>健脚：本人の走破への寄与が上がる（→ DungeonTraversalResolver.GetMemberTraversalValue）。</summary>
        public static readonly TraitDefinition Pathfinder = Define(new TraitDefinition { Id = PathfinderId, IsTransmittable = true, Effects = new List<TraitEffect>() });

        /// <summary>頑強：最大HPの基礎部分が上がる（→ Adventurer.MaxHP）。</summary>
        public static readonly TraitDefinition Sturdy = Define(new TraitDefinition { Id = SturdyId, IsTransmittable = true, Effects = new List<TraitEffect>() });

        /// <summary>治りが早い：負傷の回復と静養のHP回復が速い（→ InjuryRecoverySystem・RestRecoverySystem）。</summary>
        public static readonly TraitDefinition QuickHealer = Define(new TraitDefinition { Id = QuickHealerId, IsTransmittable = true, Effects = new List<TraitEffect>() });

        /// <summary>快活：本人の満足度が毎週上がる（→ SatisfactionSystem.ProcessWeeklySatisfaction）。</summary>
        public static readonly TraitDefinition Cheerful = Define(new TraitDefinition { Id = CheerfulId, IsTransmittable = true, Effects = new List<TraitEffect>() });

        /// <summary>働き者：研究の手伝いで貯まる額が増える（→ IdleActivitySystem.GetHelpCredit、§0.73）。</summary>
        public static readonly TraitDefinition Hardworker = Define(new TraitDefinition { Id = HardworkerId, IsTransmittable = true, Effects = new List<TraitEffect>() });

        /// <summary>
        /// 火の魔術師：INTの素の値部分が上がる（古傷などの割合減と合算）。火炎特攻（火に弱いボスへの上乗せ）は、
        /// 大迷宮のボスに弱点を足すときに接続する（→ 06）。
        /// </summary>
        public static readonly TraitDefinition FireMage = Define(new TraitDefinition
        {
            Id = FireMageId,
            IsTransmittable = true,
            Effects = new List<TraitEffect>
            {
                new TraitEffect { EffectType = TraitEffectType.StatPercentReduction, TargetStat = "INT", Value = TraitBalance.FireMageIntBonus },
            },
        });

        /// <summary>ソードマスター：剣（→ SwordWeaponItemIds）を装備しているときだけ、STR・AGIの素の値部分が上がる（→ Adventurer.EffectiveStat）。</summary>
        public static readonly TraitDefinition SwordMaster = Define(new TraitDefinition { Id = SwordMasterId, IsTransmittable = true, Effects = new List<TraitEffect>() });

        // ---- 2026年10月・§0.56：地図読み・生まれつきの欠点9種・後天の障害5種（→ 03 §5.3.2） ----

        /// <summary>地図読み：本人の走破への寄与が上がる。方向音痴を克服する長所（→ DungeonTraversalResolver.GetMemberTraversalValue）。</summary>
        public static readonly TraitDefinition MapReader = Define(new TraitDefinition { Id = MapReaderId, IsTransmittable = true, Effects = new List<TraitEffect>() });

        // 生まれつきの欠点：手動で忘却できず、侵食もされない。OvercomeByTraitId の長所を得ると置き換わる（→ Adventurer.TryAddTrait）。伝授されない。

        /// <summary>臆病：階層ボス戦で本人の損耗が増える（豪胆の逆。SurvivalThresholdModifier の負の値）。豪胆で克服。</summary>
        public static readonly TraitDefinition Coward = Define(new TraitDefinition
        {
            Id = CowardId, IsFlaw = true, OvercomeByTraitId = BraveId,
            Effects = new List<TraitEffect>
            {
                new TraitEffect { EffectType = TraitEffectType.SurvivalThresholdModifier, TargetStat = "", Value = -TraitBalance.CowardBossHpLossPenalty },
            },
        });

        /// <summary>不器用：隠密への本人の寄与が下がる（注意深いの逆。ScoutingModifier の負の値）。注意深いで克服。</summary>
        public static readonly TraitDefinition Clumsy = Define(new TraitDefinition
        {
            Id = ClumsyId, IsFlaw = true, OvercomeByTraitId = AttentiveId,
            Effects = new List<TraitEffect>
            {
                new TraitEffect { EffectType = TraitEffectType.ScoutingModifier, TargetStat = "", Value = TraitBalance.ClumsyStealthPenalty },
            },
        });

        /// <summary>方向音痴：走破への本人の寄与が下がる（→ DungeonTraversalResolver.GetMemberTraversalValue）。地図読みで克服。</summary>
        public static readonly TraitDefinition PoorDirection = Define(new TraitDefinition { Id = PoorDirectionId, IsFlaw = true, OvercomeByTraitId = MapReaderId, Effects = new List<TraitEffect>() });

        /// <summary>猪突猛進：討伐火力が上がる（→ DungeonPowerCalculator.MemberPower）が、階層ボス戦の損耗も増える。豪胆で克服。</summary>
        public static readonly TraitDefinition Reckless = Define(new TraitDefinition
        {
            Id = RecklessId, IsFlaw = true, OvercomeByTraitId = BraveId,
            Effects = new List<TraitEffect>
            {
                new TraitEffect { EffectType = TraitEffectType.SurvivalThresholdModifier, TargetStat = "", Value = -TraitBalance.RecklessBossHpLossPenalty },
            },
        });

        /// <summary>病弱：最大HPの基礎部分が下がる（→ Adventurer.MaxHP）。頑強で克服。</summary>
        public static readonly TraitDefinition Sickly = Define(new TraitDefinition { Id = SicklyId, IsFlaw = true, OvercomeByTraitId = SturdyId, Effects = new List<TraitEffect>() });

        /// <summary>飽きっぽい：訓練施設での成長判定の基礎確率が下がる（→ GrowthSystem）。勤勉で克服。</summary>
        public static readonly TraitDefinition Fickle = Define(new TraitDefinition { Id = FickleId, IsFlaw = true, OvercomeByTraitId = DiligentId, Effects = new List<TraitEffect>() });

        /// <summary>気難しい：本人の満足度が毎週下がる（→ SatisfactionSystem）。快活で克服。</summary>
        public static readonly TraitDefinition Moody = Define(new TraitDefinition { Id = MoodyId, IsFlaw = true, OvercomeByTraitId = CheerfulId, Effects = new List<TraitEffect>() });

        /// <summary>怠け者：研究の手伝いが貯まらない（機嫌は上がる、→ IdleActivitySystem.GetHelpCredit、§0.73）。働き者で克服。</summary>
        public static readonly TraitDefinition Slothful = Define(new TraitDefinition { Id = SlothfulId, IsFlaw = true, OvercomeByTraitId = HardworkerId, Effects = new List<TraitEffect>() });

        /// <summary>浪費家：採用時の週給と適正週給が高い（→ RecruitmentSystem・SatisfactionSystem）。克服できない。</summary>
        public static readonly TraitDefinition Spendthrift = Define(new TraitDefinition { Id = SpendthriftId, IsFlaw = true, OvercomeByTraitId = null, Effects = new List<TraitEffect>() });

        // 後天の障害（IsCurseOrInjury は trait.csv で true）：古傷・トラウマと同じく忘却できず、満杯なら通常特性を侵食して付く。

        /// <summary>毒の後遺症：VITが恒久的に下がる。対策していない猛毒のボス戦で、HPが大きく削れて生還した隊員に付く（→ DungeonResolver）。</summary>
        public static readonly TraitDefinition PoisonAftereffect = Define(new TraitDefinition
        {
            Id = PoisonAftereffectId,
            Effects = new List<TraitEffect>
            {
                new TraitEffect { EffectType = TraitEffectType.StatPercentReduction, TargetStat = "VIT", Value = TraitBalance.PoisonAftereffectVitReduction },
            },
        });

        /// <summary>足の古傷：AGIが恒久的に大きく下がる。古傷の判定に当たったとき部位として選ばれる（→ CriticalInjury.RollOldWound）。</summary>
        public static readonly TraitDefinition LegWound = Define(new TraitDefinition
        {
            Id = LegWoundId,
            Effects = new List<TraitEffect>
            {
                new TraitEffect { EffectType = TraitEffectType.StatPercentReduction, TargetStat = "AGI", Value = TraitBalance.LegWoundAgiReduction },
            },
        });

        /// <summary>腕の古傷：STR・DEXが恒久的に下がる。古傷の判定に当たったとき部位として選ばれる。</summary>
        public static readonly TraitDefinition ArmWound = Define(new TraitDefinition
        {
            Id = ArmWoundId,
            Effects = new List<TraitEffect>
            {
                new TraitEffect { EffectType = TraitEffectType.StatPercentReduction, TargetStat = "STR", Value = TraitBalance.ArmWoundStatReduction },
                new TraitEffect { EffectType = TraitEffectType.StatPercentReduction, TargetStat = "DEX", Value = TraitBalance.ArmWoundStatReduction },
            },
        });

        /// <summary>戦慄：討伐火力が下がる（→ DungeonPowerCalculator.MemberPower）。対策していない即死級を受けて生き残った隊員に付く。</summary>
        public static readonly TraitDefinition Dread = Define(new TraitDefinition { Id = DreadId, Effects = new List<TraitEffect>() });

        /// <summary>燃え尽き：満足度が毎週下がる。休まず続けて出撃した冒険者に付く（→ SatisfactionSystem）。</summary>
        public static readonly TraitDefinition Burnout = Define(new TraitDefinition { Id = BurnoutId, Effects = new List<TraitEffect>() });

        // ---- 2026年10月・§0.57：レア特性8種（採用時に別枠の低確率、→ RecruitmentSystem.RareTraitPool。伝授されない） ----

        /// <summary>天才：7能力すべての素の値が上がる（StatPercentReduction の正の値）。</summary>
        public static readonly TraitDefinition Genius = Define(new TraitDefinition
        {
            Id = GeniusId,
            IsRare = true,
            Effects = new[] { "STR", "VIT", "AGI", "DEX", "INT", "MND", "LDR" }
                .Select(stat => new TraitEffect { EffectType = TraitEffectType.StatPercentReduction, TargetStat = stat, Value = TraitBalance.GeniusStatBonus })
                .ToList(),
        });

        /// <summary>大魔導士：INTが大きく上がる。</summary>
        public static readonly TraitDefinition Archmage = DefineRareSingleStat(ArchmageId, "INT");

        /// <summary>剣聖：STRが大きく上がる。</summary>
        public static readonly TraitDefinition SwordSaint = DefineRareSingleStat(SwordSaintId, "STR");

        /// <summary>聖人：MNDが大きく上がる。</summary>
        public static readonly TraitDefinition Saint = DefineRareSingleStat(SaintId, "MND");

        /// <summary>カリスマ：LDRが大きく上がる。</summary>
        public static readonly TraitDefinition Charisma = DefineRareSingleStat(CharismaId, "LDR");

        /// <summary>不滅の肉体：VITが大きく上がる（最大HPも上がる）。</summary>
        public static readonly TraitDefinition ImmortalBody = DefineRareSingleStat(ImmortalBodyId, "VIT");

        /// <summary>韋駄天：AGIが大きく上がる。</summary>
        public static readonly TraitDefinition Idaten = DefineRareSingleStat(IdatenId, "AGI");

        /// <summary>百発百中：DEXが大きく上がる。</summary>
        public static readonly TraitDefinition Marksman = DefineRareSingleStat(MarksmanId, "DEX");

        // ---- 2026年10月・§0.62：成長・素質系と戦闘系（クリアへのバランス 第3段） ----

        /// <summary>大器晩成：全盛期（23歳〜）の成長の確率が上がる（→ GrowthSystem.TraitGrowthMultiplier）。先天・伝授できる。</summary>
        public static readonly TraitDefinition LateBloomer = Define(new TraitDefinition { Id = LateBloomerId, IsTransmittable = true, Effects = new List<TraitEffect>() });

        /// <summary>早熟：22歳までの成長の確率が上がる（→ GrowthSystem.TraitGrowthMultiplier）。先天・伝授できる。</summary>
        public static readonly TraitDefinition EarlyBloomer = Define(new TraitDefinition { Id = EarlyBloomerId, IsTransmittable = true, Effects = new List<TraitEffect>() });

        /// <summary>
        /// 魂魄の申し子：魂魄融和で生まれた娘だけが確率で持つ（→ SoulFusionSystem、soul_fusion.csv SoulChildChancePercent）。
        /// 7能力の素の値が少し上がり、成長の確率も上がる。伝授・親からの継承はされない。
        /// </summary>
        public static readonly TraitDefinition SoulChild = Define(new TraitDefinition
        {
            Id = SoulChildId,
            Effects = new[] { "STR", "VIT", "AGI", "DEX", "INT", "MND", "LDR" }
                .Select(stat => new TraitEffect { EffectType = TraitEffectType.StatPercentReduction, TargetStat = stat, Value = TraitBalance.SoulChildStatBonus })
                .ToList(),
        });

        /// <summary>歴戦の勇士：本人の討伐火力が上がる（→ DungeonPowerCalculator.TraitPowerModifier）。先天・伝授できる。</summary>
        public static readonly TraitDefinition Veteran = Define(new TraitDefinition { Id = VeteranId, IsTransmittable = true, Effects = new List<TraitEffect>() });

        /// <summary>鼓舞：部隊の討伐火力が上がる（部隊に何人いても1回分。→ DungeonPowerCalculator.PartyPower）。先天・伝授できる。</summary>
        public static readonly TraitDefinition Inspiring = Define(new TraitDefinition { Id = InspiringId, IsTransmittable = true, Effects = new List<TraitEffect>() });

        private static TraitDefinition DefineRareSingleStat(string id, string stat) => Define(new TraitDefinition
        {
            Id = id,
            IsRare = true,
            Effects = new List<TraitEffect>
            {
                new TraitEffect { EffectType = TraitEffectType.StatPercentReduction, TargetStat = stat, Value = TraitBalance.RareSingleStatBonus },
            },
        });

        /// <summary>古傷の判定に当たったときの部位の候補（→ CriticalInjury.RollOldWound）。この順で等確率に選ぶ（未所持のものから）。</summary>
        public static readonly IReadOnlyList<string> OldWoundVariantIds = new[] { OldWoundId, LegWoundId, ArmWoundId };

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
            HawkEye, SixthSense, Guardian, Pathfinder, Sturdy, QuickHealer, Cheerful, Hardworker, FireMage, SwordMaster,
            MapReader,
            Coward, Clumsy, PoorDirection, Reckless, Sickly, Fickle, Moody, Slothful, Spendthrift,
            PoisonAftereffect, LegWound, ArmWound, Dread, Burnout,
            Genius, Archmage, SwordSaint, Saint, Charisma, ImmortalBody, Idaten, Marksman,
            LateBloomer, EarlyBloomer, SoulChild, Veteran, Inspiring,
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
            HawkEyeId => HawkEye,
            SixthSenseId => SixthSense,
            GuardianId => Guardian,
            PathfinderId => Pathfinder,
            SturdyId => Sturdy,
            QuickHealerId => QuickHealer,
            CheerfulId => Cheerful,
            HardworkerId => Hardworker,
            FireMageId => FireMage,
            SwordMasterId => SwordMaster,
            MapReaderId => MapReader,
            CowardId => Coward,
            ClumsyId => Clumsy,
            PoorDirectionId => PoorDirection,
            RecklessId => Reckless,
            SicklyId => Sickly,
            FickleId => Fickle,
            MoodyId => Moody,
            SlothfulId => Slothful,
            SpendthriftId => Spendthrift,
            PoisonAftereffectId => PoisonAftereffect,
            LegWoundId => LegWound,
            ArmWoundId => ArmWound,
            DreadId => Dread,
            BurnoutId => Burnout,
            GeniusId => Genius,
            ArchmageId => Archmage,
            SwordSaintId => SwordSaint,
            SaintId => Saint,
            CharismaId => Charisma,
            ImmortalBodyId => ImmortalBody,
            IdatenId => Idaten,
            MarksmanId => Marksman,
            LateBloomerId => LateBloomer,
            EarlyBloomerId => EarlyBloomer,
            SoulChildId => SoulChild,
            VeteranId => Veteran,
            InspiringId => Inspiring,
            _ => null,
        };
    }
}
