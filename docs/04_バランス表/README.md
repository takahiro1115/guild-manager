# 04_バランス表（CSV版）

冒険者ギルドマネージャーのバランス調整用データ。
仕様書 `03_システム仕様_v2.0.md` の「→ BAL: シート/項目」が指す数値をここで管理する。

## 旧 04_バランス表.xlsx からの移行について

旧xlsxは v1.0〜v1.1 時点で作成された後、実装側の改訂（疲労廃止・ステータス改名・
訓練場4分割・期限撤廃等）が反映されないまま放置され、実コードと大きく乖離していた。
本CSV群はその全面再構築であり、**2026年9月時点の実コード（コミット 560fe3d）を
正として書き起こしたもの**。旧xlsxは廃止する。

## ファイル一覧

| ファイル | 対応する仕様書セクション | 対応する実装 |
|---|---|---|
| `economy.csv` | 03 §8.1・§5.2・§8.3 | EconomyBalance（初期資金・週給/契約金/退職金係数・退職金不足の機嫌低下・内職売上の基本額と間隔・破産判定週数）, SatisfactionBalance(賃金), RecruitmentSystem(契約金) |
| `combat.csv` | 03 §2.3・§4.3 | CombatBalance：最大HP（`MaxHpBase`・`MaxHpVitCoefficient`）・**負傷**（重傷の全治週数 `SevereInjuryWeeksMin/Max`・重傷になるHPの割合 `SevereInjuryHpThresholdPct`・軽傷の全治週数 `LightInjuryWeeksMin/Max`・軽傷の能力値低下 `LightInjuryStatPenaltyRate`、→ 03 §0.53）・耐毒体質 `ResistPoisonDamageReductionRate`・巨獣狩り `GiantHunterDamageBonusRate`・古傷 `OldWoundCriticalChance`・`OldWoundRetreatHpThresholdPct`・巨獣狩りの後天開眼 `GiantHunterAwakeningChance`。旧クエストの戦闘処理の値（敵CP・索敵・奇襲/不意打ち・戦闘比率・HP消費率・致死回避閾値・不可逆帯幅）は読み込むだけで未使用だったため §0.53 で削除 |
| `aging.csv` | 03 §3.0〜3.7 | GrowthBalance（年齢帯別成長ロール基礎確率＝新鋭/成長/全盛の3区分・難易度係数・成長量幅）, AgingSystem（満期引退年齢・稼働週数） |
| `growth_job_weights.csv` | 03 §3.1〜3.4 | GrowthBalance.JobStatWeights |
| `satisfaction.csv` | 03 §5.1・§5.2 | SatisfactionBalance |
| `facility.csv` | 03 §6・§6.1 | FacilityBalance |
| `master_mood.csv` | 03 §8.1・§8.1.1 | MasterMoodBalance（マスターの機嫌の初期値・成果ごとの増減・退屈減衰・強制除籍への激怒・段階閾値・内職売上倍率・待機お手伝いのG／機嫌） |
| `recruitment.csv` | 03 §2.4・§7.3 | RecruitmentSystem, NameGeneratorBalance（第1週の新春ドラフト：`DraftCandidateCount`・`DraftHireCount`。旧 `TutorialCandidateCount` は削除）。先天特性の確率 `InnateTraitChancePercent` は§0.55で5→3（候補19種、1人あたり平均約0.57個）。生まれつきの欠点の確率 `InnateFlawChancePercent`（2、候補9種、平均約0.18個）は§0.56で新設。レア特性の確率 `RareTraitChancePercent`（1）と重み `RareTraitWeight_Genius`（1）・`RareTraitWeight_SingleStat`（2）は§0.57で新設 |
| `compatibility_advisor.csv` | 03 §5.3・§7 | CompatibilityBalance, AdvisorBalance（教官成長補正、参謀の大迷宮調査解析ボーナス・道中潜行走破力ボーナス、スカウト有望新人率） |
| `training.csv` | 03 §3.1〜3.4・§3.5改・§7.1 | TrainingBalance（勤勉 `DiligentGrowthRateBonus`、→ 03 §0.32。教官からの特性伝授 `TraitInheritanceBaseChance`・師匠肌ボーナス `MentorTraitInheritanceBonus`、→ 03 §0.34。教官の在任ボーナス `TrainerTenureBonusStepWeeks`・`TrainerTenureBonusPerStep`・`TrainerTenureBonusMax`、→ 03 §0.54） |
| `trait.csv` | 03 §4.3・§5.3.2・§4.2.3 | TraitBalance（→ TraitCatalog。豪胆 `BraveSurvivalThresholdBonus`＝ボス戦の損耗−pt・注意深い `AttentiveScoutingBonus`＝隠密の寄与×(1＋値)・知識人 `ScholarAnalysisBonus`＝解析の寄与×(1＋値)、→ 03 §0.53。ペアシナジーの緩和係数は§0.53で削除）。全特性の表示名・説明・障害フラグ（`{Id}_DisplayName`・`{Id}_Description`・`{Id}_IsCurseOrInjury`、§0.31）。旧 `TraitTransmission*` 3キーは §0.34 で削除（→ training.csv）。§0.55で足した10種の効果量（`HawkEyeFlyingDamageReductionRate`・`SixthSenseInstantKillHpLossPct`・`GuardianGuardBonus`・`PathfinderTraversalBonus`・`SturdyMaxHpBonus`・`QuickHealerInjuryRecoveryBonus`・`QuickHealerRestRecoveryBonus`・`CheerfulSatisfactionBonus`・`HardworkerIdleHelpGoldMultiplier`・`FireMageIntBonus`・`SwordMasterStatBonus`、すべて仮の値、→ 03 §5.3.2）。§0.56のマイナスの特性（地図読み `MapReaderTraversalBonus`、欠点の `Coward*`・`Reckless*`・`ClumsyStealthPenalty`・`PoorDirectionTraversalPenalty`・`SicklyMaxHpPenalty`・`FickleGrowthRatePenalty`・`MoodySatisfactionPenalty`・`SpendthriftWageMultiplier`、障害の `PoisonAftereffect*`・`LegWoundAgiReduction`・`ArmWoundStatReduction`・`Dread*`・`Burnout*`。すべて仮の値）。§0.57のレア特性（`GeniusStatBonus`＝0.15・`RareSingleStatBonus`＝0.30、仮の値） |
| `equipment.csv` | 03 §4.2.2 | EquipmentBalance（→ ItemCatalog）。**テーブル形式**（`Id,Price,HpBonus,BonusStr〜BonusLdr,note`、2026年9月に key,value 形式から移行）。`HpBonus`＝最大HP加算（武器は0）。§0.37で旧 `EffectValue`（個人CP／最大HPの二義）を個人CPの撤廃に伴い `HpBonus` へ改名 |
| `consumables.csv` | 03 §4.5.4 | ConsumableBalance（→ ConsumableCatalog。大迷宮ボスギミック対策4種の価格） |
| `progression.csv` | 03 §4.5.1・§4.2.2 | ProgressionBalance（初期の同時出撃枠、店の上位装備が入荷する倒したボスの数 `ShopTierUnlockBosses_1〜3`＝§0.61） |
| `dungeon.csv` | 03 §4.5.1・§4.5.4 | DungeonBalance（ボス能力重み・未踏破重損耗・ボス間隔・撃破実績点・出撃成長回数） |
| `dungeon_traversal.csv` | 03 §4.5.3 | DungeonTraversalBalance（**走破力の重み（VIT/MND/隊長LDR）**・**1歩の消費の尺度 `FloorsPerRatio`**（比率1.0の深さで週に進める階層数、4.0。進軍ランクの段 `RankRatioScale` は別。1歩の消費＝その階層の要求値÷(走破力×この値)÷解析倍率、週の予算1、→ 03 §0.49。旧リニア進軍は §0.25）・進軍ランク別の既踏損耗率・調査度連動走破倍率・夜目 `NightVisionUnexploredDamageReductionRate`（→ 03 §0.32）） |
| `scouting.csv` | 03 §4.5.3 | ScoutingBalance（**隠密適性の重み・専門職ボーナス・人数倍率・重装ペナルティ**、迷宮調査の4段階護衛判定・解析成果倍率・護衛HP損耗） |
| `gathering.csv` | 03 §4.5.5 | GatheringBalance（採取スコア係数・職業ボーナス・報酬ゴールド） |
| `materials.csv` | 03 §4.5.5・**§4.8.1** | MaterialBalance（全5フィールドの採取素材定義・MinFloor・基準獲得数・**売却額 `SellPrice`**） |
| `affixes.csv` | 03 §4.7.3・§4.2.2 | AffixBalance（鑑定品のランダムアフィックス＝接頭辞・接尾辞の一覧。**テーブル形式** `Id,Type,Name,TargetStat,MinValue,MaxValue,Tier,AllowedSlots,Weight`、2026年9月・§0.39） |
| `portraits.csv` | 03 §2.1 | PortraitBalance（採用の応募者に割り当てる顔グラフィックの一覧。**テーブル形式** `Id,Jobs,HairColor,EyeColor`。Id＝`assets/portraits/{Id}.png`、Jobs＝似合う職業の `\|` 区切りまたは `All`。2026年9月・§0.46） |
| `uniques.csv` | 03 §4.7.5 | UniqueBalance（固有武具＝固定アーティファクト〈紫〉・伝説級〈金〉の一覧。**テーブル形式** `Id,Grade,Name,BaseItemId,HpBonus,BonusStr〜BonusLdr,CounterGimmick,DropFieldId,DropFloor,SellPrice`。伝説級は入手元のボス〈フィールドId・階層〉と対策ギミック、売値0＝売却不可。2026年9月・§0.45） |
| `research.csv` | 03 §4.6 | ResearchBalance（アルベール研究室の24プロジェクト（うち内職強化 SideBusinessGoldBonus 4種、魂魄融和の秘薬の解禁 `res_soul_fusion`＝SoulFusionUnlock・効果値0、§0.58、段階研究11種＝RecruitPaBonus・GrowthRateBonus・SoulFusionPaBonus・CultureTankBonus、§0.60）・必要素材・ゴールド・効果種別・効果値・**前提 `Prerequisite`**（§0.60で列を追加。空＝前提なし、段階研究のII・IIIは前の段のId）） |
| `squad_orders.csv` | 03 §4.0.3 | SquadOrderBalance（部隊の方針と自動出撃、2026年10月・§0.63）：自動出撃の条件 `AutoDispatchMinHpPercent`（70）・扉前で自動で挑む条件 `AutoEngageMinHpPercent`（60）・`AutoEngagePowerMargin`（1.0＝火力が要求以上） |
| `elixir.csv` | 03 §4.6.2 | ElixirBalance（霊薬、2026年10月・§0.61）：1人が生涯に飲める数 `MaxPerAdventurer`（3） |
| `elixir_recipes.csv` | 03 §4.6.2 | ElixirBalance.Recipes（霊薬の一覧。**テーブル形式** `Id,Name,Description,TargetStats,PaBonus,StatBonus,RequiredMaterials,RequiredGold`。TargetStats は能力名の `;` 区切り、RequiredMaterials は `素材Id:個数` の `;` 区切り。§0.61） |
| `soul_fusion.csv` | 03 §5.4 | SoulFusionBalance（魂魄融和の秘薬、2026年10月・§0.58）：娘のPAの基準で高い方の親に掛ける重み `HigherParentWeight`（0.75、§0.60で新設。0.5＝平均）・処方できる相性 `RequiredCompatibility`（100）・費用 `PrescriptionGold`（2500G）・培養 `CultureWeeks`（12週）・培養槽の数 `CultureTankCount`（1）・娘のPAのばらつき `PaVarianceMin/Max`（−5〜+10）・百合相性ごとの能力限界突破の確率 `BreakthroughChance_Destined/Complementary/Ordinary`（30/15/5%）・突破の上乗せ `BreakthroughBonusMin/Max`（5〜15）と上限 `BreakthroughPaCap`（120）・特性の継承確率 `TraitInheritChancePercent`（30）・`RareTraitInheritChancePercent`（15）・`FlawInheritChancePercent`（20）。すべて仮の値 |
| `soul_fusion_catalysts.csv` | 03 §5.4 | SoulFusionBalance.Catalysts（秘薬の触媒。**テーブル形式** `MaterialId,Count,EffectType,EffectValue,Note`。EffectType は `PaVarianceMinBonus`・`TraitInheritBonus`・`FlawInheritBonus`・`BreakthroughChanceBonus`・`RareTraitInheritBonus`。各フィールドの素材を1種ずつ、§0.58） |
| `relic.csv` | 03 §4.7・§4.8.2 | RelicBalance（未鑑定遺物の希少度4段階＝鑑定費用・鑑定結果比率・換金額と獲得個数の幅・武具の抽選プール・**売却額**、採取ドロップ確率、希少度ロール閾値、ボス撃破ドロップの補正）。**アフィックスの付与率・Tier範囲**（`Affix*`、→ AffixBalance） |

`trait.csv`・`equipment.csv` は項目58（バランス値のCSV外部化）の時点では対応CSVが
存在せず対象外だったため、フォローアップとして追加した（値はTraitCatalog.cs・
ItemCatalog.csに直書きされていた旧値をそのまま書き起こしたもの。挙動は変わらない）。

## 大迷宮への再配線で追加された主なキー（2026年9月、→ 03 §0.8〜§0.9）

旧通常クエストの撤去に伴い、成長・満足度・功績・参謀ボーナスの各経路を大迷宮の3大任務
（道中進軍／迷宮調査／階層ボス討伐／採取）へ繋ぎ直した。その際に追加・移設されたキー：

**`dungeon.csv`**

- `BossPowerWeight_STR`〜`_INT` … ボス討伐の部隊火力に対する各能力の重み
  （旧 `quest_type_weights.csv` の「討伐」行から**値を変えずに移設**。→ `DungeonPowerCalculator`）。
- `GrowthRolls_Traversal`／`_BossVictory`／`_Survey`／`_Gathering`（2／5／2／2回）…
  任務ごとの成長ロール試行回数。対象能力は任務によって異なる（潜行＝STR/VIT/AGI/DEX、
  ボス撃破＝全7能力、調査＝INT/DEX/LDR/AGI、採取＝AGI/DEX/VIT。→ `GrowthSystem.ApplyExpeditionGrowth`）。
- `GrowthBaseChancePercent`（35%）… 上記ロール1回あたりの成功確率。成功で対象能力+1（PA上限でクランプ）。
- `ContributionPerTraversedFloor`／`ContributionPerBossFloor`／`ContributionPerGathering` …
  累積功績（`Adventurer.TotalContributionScore`＝退職金の上乗せ原資）の加算量。
  ボス撃破はボスの階層×係数で加算される。

**`compatibility_advisor.csv`**

- `Advisor_SurveyIntelBonusCoeff`（0.002）… 参謀の引退時7能力平均×この係数を、迷宮調査の
  解析率上昇量への**加算率**とする（平均50で+10%。研究ボーナスと合算）。
- `Advisor_TraversalPowerBonusCoeff`（0.2）… 同平均×この係数を、道中潜行の走破力スコアへ
  **直接加算**する（平均50で+10）。
- 旧クエスト用の `AdvisorBonusCoefficient`（加算先を失った休眠キー）は §0.53 で削除した。
- `ExpeditionGain_BossVictory`（3）／`_Traversal`（1）／`_Survey`（1）／`_Gathering`（1）… 大迷宮の任務を
  達成して生還した部隊の、生還者同士の全ペアの相性上昇量（→ 03 §5.3.1・§0.22）。旧クエスト用の
  `AchievementGain`・`FailureLoss` を置き換えたもの（未達成時の下降は廃止）。

**`trait.csv`**

- `CountryBredGatheringBonusRate`（0.20）… 田舎育ちの採取スコアボーナス率。保有者本人の採取寄与
  （AGI×係数＋DEX×係数）の+20%を採取スコアへ上乗せする（→ 03 §5.3.2・§0.22）。旧・探索クエストの
  個人スコア加算 `CountryBredExplorationBonus` を置き換えたもの。

**`satisfaction.csv`**

- `Satisfaction_BossVictory`／`_BossDefeat`／`_SurveyAbundant`／`_SurveySufficient`／
  `_SurveyDeficient`／`_TraversalSuccess`／`_GatheringSuccess` … 大迷宮の任務成果に応じた
  週次の満足度増減。旧・クエスト達成時の `VictoryBonus` を置き換えたもの。適用対象は
  部隊の生存者のみ（強制除籍者には適用しない。→ `SatisfactionSystem.ApplyExpeditionSatisfaction`）。
- `OpportunityPenaltyMinAge`（23）／`OpportunityPenaltyMaxAge`（26）… 出場機会ペナルティの
  対象年齢。年齢帯の3区分化（→ 03 §0.11）に合わせ、新・全盛期へ整合させた際に
  コード直書きから外部化した（→ 03 §0.12）。22歳以下は育成猶予として対象外。

**`economy.csv`**

- `BankruptcyConsecutiveWeeksThreshold`（4週）… 破産（敗北条件の1つ。もう1つは機嫌0の副官解雇）の判定週数
  （→ `DefeatSystem`）。脅威度の撤去で `security.csv` を廃止した際、脅威度とは無関係な
  この値だけをここへ移設した（→ 03 §0.10）。

**旧・環境ギミックの撤去（2026年9月、→ 03 §0.12）：** `gimmick.csv` は、旧通常クエストの
撤去（→ 03 §0.8）で発生源を失い呼び出し元が無くなっていた評価器（`GimmickEvaluator`・
`GimmickBalance`・`GimmickMitigationLevel`）と共に削除した。大迷宮のボスギミック
（`dungeon.csv`・`BossGimmickType`）は現役の別システムであり、影響を受けない。

`consumables.csv`はパーティ携行アイテム刷新仕様で新設した。**大迷宮のボスギミック対策
4種の価格のみ**を持つ（→ 03 §4.5.4・`ConsumableCatalog`）。効果は「対策済みならその
ギミックのペナルティを受けない」で固定のため、効果量のキーは持たない。

| キー | アイテム | 対策するギミック |
|---|---|---|
| `Antidote_Price` | 解毒薬 | 猛毒（Poison） |
| `AcidFlask_Price` | 溶解液 | 重装甲（HeavyArmor） |
| `Net_Price` | 捕縛網 | 飛行（Flying） |
| `Charm_Price` | 身代わりの護符 | 即死級攻撃（InstantKill） |

旧・環境ギミックの相殺アイテム（聖水・松明・登攀具）と効果アイテム（煙幕弾・高品質傷薬・
携帯糧食）のキーは、参照元を失っていたため2026年9月に削除した（→ 03 §0.13）。

**旧通常クエストの撤去（2026年9月、→ 03 §0.8）：** `quest.csv`・`quest_templates.csv`・
`quest_type_weights.csv`・`quest_scoring.csv`・`quest_events.csv`・`pair_synergy.csv`・
`success_rate.csv`・`emergency.csv` は、対応する旧クエスト系の実装と共に削除した。
`quest_type_weights.csv` の討伐（`Subjugation`）行の能力重みは、階層ボスの部隊火力として
`dungeon.csv` の `BossPowerWeight_*` へ同じ値のまま移設している。`progression.csv` は
初期の同時出撃枠のみを残し、昇格試験のキーは削除した。`guild_rank_params.csv` の
クエスト達成／失敗による名声増減キーも削除した。

**名声・ギルド格付け・助成金の廃止（2026年9月、→ 03 §0.20）：** `guild_rank.csv`・`guild_rank_params.csv`
は `GuildRank`・`GuildRankSystem`・`GuildRankBalance` と共に削除した。`economy.csv` の
`SubsidyWeeksPerMonth`・`SubsidyBaseAmount_G〜S` は `SideJobIntervalWeeks`・`SideJobBaseAmount`（旧Gの200G）へ、
`SeveranceShortfallReputationPenalty` は `SeveranceShortfallMoodPenalty` へ置き換え、`dungeon.csv` の
`BossBaseReputation` は削除した。代わりに `master_mood.csv` を新設した。

## 未鑑定遺物・鑑定・売却で追加されたキー（2026年9月、→ 03 §0.14〜§0.16）

**`relic.csv`（新設。→ 03 §4.7・§4.8.2、`RelicBalance`）**

希少度4段階（`Common`＝銅／`Rare`＝銀／`Epic`＝金／`Legendary`＝虹）ごとに、キー名の末尾へ
希少度名を付けて定義する（例：`AppraisalCostRare`）。

| キー（末尾に希少度名） | 意味 |
|---|---|
| `AppraisalCost*` | 鑑定費用（銅100／銀250／金500／虹1200G） |
| `EquipmentRate*` / `MaterialRate*` / `GoldRate*` | 鑑定結果の比率（%）。**3つの合計が100でなければ読み込み時に例外** |
| `GoldRewardMin*` / `GoldRewardMax*` | 換金だった場合の獲得ゴールドの幅 |
| `MaterialCountMin*` / `MaterialCountMax*` | 素材だった場合の獲得個数の幅（Min>Maxなら例外） |
| `EquipmentPool*` | 武具だった場合の抽選プール（`ItemCatalog` のIdを「;」区切り。空・未登録Idなら例外） |
| `SellPrice*` | 鑑定で出土した武具を保管庫から売却する際の1点あたりの額（銅50／銀125／金250／虹600G、→ §4.8.2） |
| `AffixPrefixRate*` / `AffixSuffixRate*` | 鑑定で武具が出たときに接頭辞・接尾辞が付く確率（%、0〜100。銅20/0・銀100/30・金100/100・虹100/100、2026年9月・§0.39、→ affixes.csv） |
| `AffixMinTier*` / `AffixMaxTier*` | 抽選するアフィックスのTier範囲（銅1〜1・銀1〜2・金2〜3・虹3〜3。1〜3の外・Min>Maxなら例外） |
| `ArtifactRate*` | 鑑定で武具が出たときに、まだ入手していない固定アーティファクト（→ uniques.csv）へ化ける確率（%、0〜100。銅0・銀0・金4・虹12、2026年9月・§0.45、→ 03 §4.7.5） |
| `AffixSellBonusTier1`〜`AffixSellBonusTier3`（希少度名ではなくTier番号が末尾） | 鑑定品の武具の売却額へ、付いているアフィックス1枠ごとに足す額（Tier1＝25／Tier2＝60／Tier3＝150G。負なら例外。2026年9月・§0.40、→ 03 §4.8.2） |

希少度に紐づかないキー：

- `GatheringDropBasePct`（5）／`GatheringDropMaxPct`（15）／`GatheringDropScoreDivisor`（40）／
  `GatheringDropFloorDivisor`（20）… 探索（採取）任務での遺物ドロップ確率。
  基礎 ＋ 採取スコア÷40 ＋ 到達階層÷20 を上限15%でクランプする。
- `RarityFloorBonusDivisor`（10）／`RareRollThreshold`（60）／`EpicRollThreshold`（85）／
  `LegendaryRollThreshold`（100）… 希少度ロール（1〜100の乱数 ＋ 出土階層÷10 ＋ 任務ボーナス）の閾値。
- `BossRelicRollBonus`（25）／`BossMinimumRarity`（`Rare`）… 階層ボス撃破ドロップの補正と最低保証希少度。

> **素材の抽選テーブルは `relic.csv` に複製しない。** 鑑定で素材が出た場合の抽選対象は、遺物が
> 覚えている出土地・出土階層を `materials.csv`（`MaterialBalance.GetEligibleMaterials`）へ渡して
> 引き直す。フィールドや素材を増やしたときに直すファイルを1つに保つため（→ 03 §10.1）。

**`materials.csv` に `SellPrice` 列を追加（→ 03 §4.8.1）**

列は `Id,Name,Description,FieldId,MinFloor,MaxFloor,BaseYield,SellPrice` の8列になった。
素材1個あたりの売却額で、深いフィールドの素材ほど高い（月光草8／霊樹の枝12／変異胞子25／
発光苔10／黒鉱石30／遺物の欠片12／ルーン石35／火山灰14／紅玉鉱石45／虚空の粉塵20／
深淵の結晶70G）。研究レシピ（`research.csv`）で使う素材を売り払うか貯めておくかが、
素材経済のトレードオフになる。

> **カタログ品の武具の売却額は新しいキーを作らない。** `equipment.csv` の `Price` 列（定価）の
> 50%（端数切り捨て）を導出して使う（→ 03 §4.8.2・`EquipmentSystem.GetSellPrice`）。
> 定価と売却額を二重に持たないため。

## 走破力・隠密適性の差別化で追加されたキー（2026年9月、→ 03 §0.17）

改訂前は `dungeon_traversal.csv` の `StatCoefficient`／`LeaderCoefficient` と
`scouting.csv` の `StealthStatCoefficient`／`LeaderPanicPreventionCoefficient` が
**同じ Σ(AGI+DEX) ＋ 部隊長LDR という式を別々に持っていた**ため、編成画面の
「走破力予測」と「隠密適性」がどんな編成でも同値になっていた。旧4キーは削除し、
それぞれ別系統のキーへ置き換えた。

**`dungeon_traversal.csv`**

- `Traversal_Weight_Vit`（1.0）／`Traversal_Weight_Mnd`（0.8）／`Traversal_Weight_Ldr`（1.0）…
  走破力＝Σ(VIT×重み ＋ MND×重み) ＋ 部隊長LDR×重み。**AGI/DEXは参照しない。**

**`scouting.csv`**

- `Stealth_Weight_Agi`（1.0）／`Stealth_Weight_Dex`（1.0）… 隠密の素点の重み。素点は隊員の平均（§0.41で合計→平均）。
- `Stealth_Weight_Ldr`（0.25、§0.41で0.5→0.25）… 部隊長LDR×この値を倍率適用後に加算。
- `Stealth_Bonus_RangerThief`（15、§0.41で30→15）… 斥候（Ranger）・盗賊（Thief）1名につき倍率適用後に加算。
- `Stealth_PartySize_Mult_1`〜`_4`（1.10／1.00／0.85／0.70）… 人数倍率（平均素点にだけ**乗算**）。
- `Stealth_HeavyArmor_Penalty`（15、§0.41で30→15）… 重装者1名につき**倍率適用後に直接減算**
  （重装鎧の装備者、または職業が重戦士・騎士。両方該当でも1名分）。結果は0未満にならない。
- `StealthRequirementPerFloor`（§0.41で18→7、§0.47で1.4）… 隠密の要求値の階層あたりの増分（下記「要求値の見直し」）。

**`scouting.csv` の護衛（§0.41）**

- `Guard_Support_Ratio`（0.3、新設）… 護衛力＝主護衛の max(STR,VIT,INT) ＋ 他の隊員の max(STR,VIT,INT) の合計×この値。
- `BaseRequiredGuardPower`（§0.41で35→50）… §0.47で削除し、`GuardRequirementBase`・`GuardRequirementPerFloor` へ置き換え。

## 大迷宮の要求値の見直しで追加／変更されたキー（2026年9月、→ 03 §0.47）

要求値はすべて **(基礎値＋階層×増分)×フィールド倍率** の形にそろえた（`DungeonBalance.ScaleRequirement`）。
1つ目のフィールドは「10F＝各能力25前後の部隊、100F＝各能力90の精鋭が届く」に合わせてある
（討伐・走破は4人部隊、調査は斥候1名を含む3人の調査隊が基準。`DungeonRequirementAnchorTests` で固定）。

| ファイル | キー | 値 | 10F／30F／100F |
|---|---|---|---|
| `dungeon.csv` | `PartyPowerRequirementBase`（新設）／`PartyPowerRequirementPerFloor`（45→15） | 270／15 | 420／720／1,770 |
| `scouting.csv` | `AnalysisRequirementBase`（新設）／`AnalysisRequirementPerFloor`（14→2.15） | 50／2.15 | 71.5／114.5／265 |
| `scouting.csv` | `StealthRequirementBase`（新設）／`StealthRequirementPerFloor`（7→1.4） | 48／1.4 | 62／90／188 |
| `scouting.csv` | `GuardRequirementBase`／`GuardRequirementPerFloor`（ともに新設） | 28／1.15 | 39.5／62.5／143 |
| `dungeon_traversal.csv` | `RequirementPerFloor`（15→7.5。基礎値は置かない） | 7.5 | 75／225／750 |
| `dungeon.csv` | `FieldRequirementMultiplier_1`〜`_5`（新設） | すべて1.0（§0.50で 1.0／1.1／1.2／1.3／1.5） | 上記すべてに掛けるフィールド倍率。2つ目以降は§0.50で仮の値を入れた（下記） |

## 走破を階層ごとの要求値で数える改訂で変更／追加されたキー（2026年9月、→ 03 §0.49）

進軍の数え方が「出発階層の比率で予算 `floor(比率×2)` 階層」から
「1週の予算1、1歩の消費＝その階層の要求値÷(走破力×`FloorsPerRatio`)÷区間の解析倍率」へ変わった。

| ファイル | キー | 値 | 意味 |
|---|---|---|---|
| `dungeon_traversal.csv` | `FloorsPerRatio`（2.0→4.0） | 4.0 | 比率1.0の深さで1週に進める階層数。大きくするほど深い階層まで速く潜れる（全能力90・全区間未解析で1F→100Fが 2.0＝30週、4.0＝14週、6.0＝9週） |
| `dungeon_traversal.csv` | `RankRatioScale`（新設） | 2.0 | 進軍ランクの段＝floor(先頭の階層での比率×この値)。既踏の損耗率を決める。速さ（`FloorsPerRatio`）と切り離し、比率ごとのランクは従来どおり |
| `dungeon_traversal.csv` | `RequirementPerFloor` | 7.5 | 1歩ごとに、踏み出す階層×この値を要求値にする（旧：出発階層だけで1回） |

## 2つ目以降のフィールドの倍率を決めた改訂で変更されたキー（2026年9月、→ 03 §0.50）

後のフィールドほど段階的に難しくする（仮の値。100Fに届かせる仕組みが決まったら見直す）。各フィールドの伝説級（40F、深淵は50F）に、
完全解析・HP満タンの4人部隊（装備なし）で届く全能力の目安を段階的に上げた（`DungeonRequirementAnchorTests` で固定）。

| ファイル | キー | 値 | 伝説級に届く全能力の目安 |
|---|---|---|---|
| `dungeon.csv` | `FieldRequirementMultiplier_2`（嘆きの鍾乳洞） | 1.0→1.1 | 40F＝48 |
| `dungeon.csv` | `FieldRequirementMultiplier_3`（忘却の古代廃墟） | 1.0→1.2 | 40F＝52 |
| `dungeon.csv` | `FieldRequirementMultiplier_4`（焦熱の峡谷） | 1.0→1.3 | 40F＝56 |
| `dungeon.csv` | `FieldRequirementMultiplier_5`（深淵の特異点） | 1.0→1.5 | 50F＝76 |

## 成長・戦闘の特性と要求値の見直し（クリアへのバランス 第3段）で追加／変更されたキー（2026年10月、→ 03 §0.62）

3段の調整の後、`dotnet run --project tools/balance_sim -- campaign 10 1440` で10回ともクリア、16年目前後（13〜18年目。秘薬なしは18年目前後）。

| ファイル | キー | 旧 → 新 | 意味 |
|---|---|---|---|
| `trait.csv` | `LateBloomerPeakGrowthMultiplier`・`EarlyBloomerYouthGrowthMultiplier` | （新設）1.6・1.3 | 大器晩成（全盛期）・早熟（22歳まで）の成長の確率の倍率。表示名・説明も `{Id}_DisplayName` 等で追加 |
| `trait.csv` | `SoulChildStatBonus`・`SoulChildGrowthMultiplier` | （新設）0.05・1.15 | 魂魄の申し子（秘薬の娘だけ）：7能力×1.05・成長×1.15 |
| `trait.csv` | `VeteranPowerBonus`・`InspiringPartyPowerBonus` | （新設）0.10・0.04 | 歴戦の勇士（本人の討伐火力）・鼓舞（部隊の討伐火力、1回分） |
| `soul_fusion.csv` | `SoulChildChancePercent` | （新設）50 | 娘が魂魄の申し子を持つ確率 |
| `recruitment.csv` | `InnateTraitChancePercent` の備考 | 候補20種 → 24種 | 先天の候補に大器晩成・早熟・歴戦の勇士・鼓舞を追加（確率3%は据え置き） |
| `dungeon.csv` | `PartyPowerRequirementPerFloor` | 7.5 → 6.5 | 討伐の要求火力の階層あたりの増分 |
| `dungeon.csv` | `FieldRequirementMultiplier_2`〜`_5` | 1.07／1.14／1.2／1.27 → 1.04／1.08／1.12／1.16 | フィールド倍率（調査・走破の要求にも掛かる）。目安は森100F＝各能力50・深淵100F＝各能力58 |

## 装備と霊薬（クリアへのバランス 第2段）で追加されたキー（2026年10月、→ 03 §0.61）

第2段後は15年目にボス44体（第1段のみ42体）、30年で48〜49体、クリアはまだ。

| ファイル | キー・行 | 値 | 意味 |
|---|---|---|---|
| `equipment.csv` | `Mithril*`・`Orichalcum*`・`StarIron*`（各7行） | 補正+8／+13／+18前後、価格1500〜2000／4500〜6000／15000〜18000G | 店の上位装備（段はコードの `Item.ShopTier`） |
| `progression.csv` | `ShopTierUnlockBosses_1〜3` | 15／25／35 | 上位装備が店に並ぶ、倒した階層ボスの総数 |
| `relic.csv` | `AffixDepthScaleStartFloor`／`AffixDepthScaleAt100` | 20／2.0 | 鑑定品のアフィックスの値に掛ける出土階層の倍率（20F以下1.0、100Fで2.0、その間は直線） |
| `research.csv` | `res_elixir_brewing` | 5000G・黒鉱石×5・ルーン石×3 | 霊薬の解禁（ElixirUnlock） |
| `elixir.csv`・`elixir_recipes.csv` | （新設） | 1人3本・5種 | 霊薬（→ 上のファイル一覧） |

## 世代で強くなる仕組み（クリアへのバランス 第1段）で追加／変更されたキー（2026年10月、→ 03 §0.60）

5フィールドを深淵100Fまで回すシミュレーション（`dotnet run --project tools/balance_sim -- campaign 10 1440`）で、改訂前は30年でもクリアできず
（ボス撃破28体前後で頭打ち）、お金が約130万G余っていた。目標はクリアまで15年前後（→ 03 §8.2）。第1段後は30年で47体（クリアはまだ）。

| ファイル | キー | 旧 → 新 | 意味 |
|---|---|---|---|
| `aging.csv` | `GrowthProbability_Young`／`_Growing`／`_Peak` | 0.35／0.30／0.12 → 0.42／0.36／0.15 | 訓練の成長ロールの基礎確率（約1.2倍） |
| `dungeon.csv` | `GrowthBaseChancePercent` | 35 → 42 | 出撃成長のロール1回の成功確率 |
| `soul_fusion.csv` | `HigherParentWeight` | （新設）0.75 | 娘のPAの基準＝高い方の親×この値＋低い方×(1−この値)。§0.58は平均（0.5相当） |
| `research.csv` | `Prerequisite` 列 | （新設） | 前提の研究Id。段階研究11行（`res_scout_network_1〜3`＝RecruitPaBonus 5・`res_training_method_1〜3`＝GrowthRateBonus 0.20・`res_elixir_purity_1〜3`＝SoulFusionPaBonus 3・`res_culture_tank_2〜3`＝CultureTankBonus 1）を追加 |

## 要求値の傾きを緩めた改訂で変更されたキー（2026年9月、→ 03 §0.51）

目安を「森の10F＝各能力25（据え置き）、森の100F＝各能力55、深淵の100F＝各能力70」に改めた（旧：森の100F＝各能力90。成長の上限では作れないことをシミュレーションで確認）。
討伐・走破は4人部隊、調査は斥候1名を含む3人の調査隊が基準（`DungeonRequirementAnchorTests` で固定）。上の§0.47・§0.50の表の値を置き換える。

| ファイル | キー | 旧 → 新 | 10F／30F／100F（森） |
|---|---|---|---|
| `dungeon.csv` | `PartyPowerRequirementBase`／`PartyPowerRequirementPerFloor` | 270／15 → 345／7.5 | 420／570／1,095 |
| `scouting.csv` | `GuardRequirementBase`／`GuardRequirementPerFloor` | 28／1.15 → 34／0.53 | 39.3／49.9／87 |
| `scouting.csv` | `StealthRequirementBase`／`StealthRequirementPerFloor` | 48／1.4 → 56／0.63 | 62.3／74.9／119 |
| `scouting.csv` | `AnalysisRequirementBase`／`AnalysisRequirementPerFloor` | 50／2.15 → 61.5／1.0 | 71.5／91.5／161.5 |
| `dungeon_traversal.csv` | `RequirementPerFloor` | 7.5 → 4.5 | 45／135／450 |
| `dungeon.csv` | `FieldRequirementMultiplier_2`〜`_5` | 1.1／1.2／1.3／1.5 → 1.07／1.14／1.2／1.27 | 深淵の100F＝各能力70で届く |

値を動かすときは `dotnet run --project tools/balance_sim -- anchor` で目安部隊の値と要求値を並べて確かめられる（→ tools/balance_sim/README.md）。

## 序盤の経済・人件費の見直しで変更されたキー（2026年9月、→ 03 §0.52）

人件費を全体に下げ、内職を少し増やした（シミュレーションで破産10回中7回→0回。`tools/balance_sim` の `game` モードの「お金の出入り」表で確かめられる）。

| ファイル | キー | 旧 → 新 | 意味 |
|---|---|---|---|
| `economy.csv` | `SigningBonusCoefficient` | 3.0 → 1.0 | 契約金＝総合PA×年齢×この値（18歳加入で1名およそ1000〜1500G） |
| `economy.csv` | `WeeklyWageCoefficient` | 0.6 → 0.4 | 採用時の週給＝総合PA×この値。初期メンバーの週給（コード側 `Data.SampleData`）も 55／40／45 → 37／27／30 |
| `economy.csv` | `AppropriateWageCoefficient` | 0.6 → 0.4 | 満足度の適正週給。`WeeklyWageCoefficient` と同じ値にそろえる（テストで固定） |
| `economy.csv` | `SideJobBaseAmount` | 200 → 300 | 内職の初期基本額（4週ごと、機嫌の倍率が掛かる） |

## 道中損耗・機嫌・内職・相性で追加／変更されたキー（2026年9月、→ 03 §0.19〜§0.22）

**`dungeon_traversal.csv` ／ `dungeon.csv`（§0.19）** … キーの追加・変更なし。`FullIntelDamageMultiplier`（0.3）と
`UnexploredHpLossPctMin/Max`（30〜50%）の**適用方法**だけが変わった：損耗は階層ごとに「その階層の基礎率
（既踏＝進軍ランクの `HpLossPct*`／未踏破＝`UnexploredHpLossPct*`）÷歩いた階層数×その区間の被ダメージ倍率」を
合算する（旧：進軍全体の基礎率×歩いた階層の平均倍率）。

**`master_mood.csv`（§0.20・§0.21で新設。`guild_rank.csv`・`guild_rank_params.csv` は削除）**

| キー | 値 | 意味 |
|---|---|---|
| `InitialMood` | 50 | マスターの機嫌の初期値（旧セーブの読み込み時もこの値） |
| `BossDefeatMoodGain` | 20 | 階層ボス撃破1体ごとの上昇 |
| `PioneerMoodPerFloor` | 2 | 道中潜行で進んだ未踏破階層1階層ごとの上昇 |
| `SurveySuccessMoodGain` ／ `SurveyRoutedMoodLoss` | 3 ／ 3 | 迷宮調査：護衛「余裕」「十分」で帰還時+3／「不足」（潰走）で−3 |
| `GatheringMoodGain` | 2 | 探索採取で素材を1個以上持ち帰った時の上昇 |
| `AppraisalMoodGain` | 1 | 未鑑定遺物1個の鑑定ごとの上昇（即時） |
| `BoredomMoodDecay` | 5 | 大迷宮での成果ゼロの週の退屈減衰 |
| `ForcedRetirementMoodLoss` | 20 | 強制除籍1名ごとの低下（アルベールの激怒、§0.21） |
| `TierThreshold_Cheerful` ／ `_Normal` ／ `_Grumpy` | 80 ／ 50 ／ 20 | 段階の下限（未満は危機。0で副官解雇） |
| `SideJobMultiplier_Cheerful` ／ `_Normal` ／ `_Grumpy` ／ `_Crisis` | 1.5 ／ 1.0 ／ 0.5 ／ 0.0 | 段階ごとの内職売上倍率 |

**`economy.csv`（§0.20）** … `SideJobIntervalWeeks`（4週）・`SideJobBaseAmount`（200G）＝アルベールの市販薬・
内職売上の入金間隔と初期基本額（旧 `SubsidyWeeksPerMonth`・`SubsidyBaseAmount_G〜S` を置き換え）。
`SeveranceShortfallMoodPenalty`（15）＝退職金不足時の機嫌低下（旧 `SeveranceShortfallReputationPenalty`）。

**`research.csv`（§0.21）** … 効果種別 `SideBusinessGoldBonus` の4研究を追加（`res_beauty_lotion`+150G・
`res_energy_tonic`+250G・`res_trade_route`+400G・`res_vitality_elixir`+600G）。内職売上＝(基本200G＋研究加算)×機嫌倍率。

**`compatibility_advisor.csv`（§0.22）** … `ExpeditionGain_BossVictory`（3）／`_Traversal`／`_Survey`／`_Gathering`（各1）。
（旧 `AchievementGain`・`FailureLoss` を置き換え。詳細は上記「大迷宮への再配線で追加された主なキー」の節）

**`trait.csv`（§0.22）** … `CountryBredGatheringBonusRate`（0.20）＝田舎育ちの採取スコアボーナス率
（旧 `CountryBredExplorationBonus` を置き換え）。

## 凡例

- `key` … コード側の定数名に対応する識別子。**変更しないこと**（読み込み時のキーになる）。
- `value` … 調整対象の数値。ここを書き換えてバランスを調整する。
- `unit` … 単位。
- `note` … 説明・出典セクション・注意事項。

## CSV化していない値（コード側に残すもの）

以下は「バランス調整用の数値」ではなく**構造を定義する値**のため、意図的にCSVへ
出していない。変更すると他の数値の前提が連鎖的に崩れるため、コード変更として扱う。

- 1年=48週（`WeeksPerYear`）
- 年齢帯の境界（18/22。`Adventurer.AgeBand`。→ 03 §3.0の3区分）
- 各種クランプの上下限（満足度0〜100、相性0〜100、マスターの機嫌0〜100、実効値の下限0）
- 施設の最大Lv（5）
- 訓練施設ごとの対象ステータス対応（戦士訓練所→STR/VIT 等。職業・施設の定義そのもの）
- 衰微対象ステータス（STR/AGI/VIT。§3.0の「フィジカル衰微」の定義そのもの）

## 未実装のため記載していない項目

- **勝利条件（最終討伐クエスト＝魔王戦）**：詳細未設計のため数値なし。
  中間目標（Aランク到達で`FinalQuestUnlocked`が立つ）までは実装済みだが、
  ここにバランス値として持つべき数値は現時点で存在しない（→ 03 §8.2・§11）。
- **市場・素材価格の変動**：売却額そのものは実装済み（`materials.csv` の `SellPrice`・
  `relic.csv` の `SellPrice*`、→ 03 §4.8）だが、**相場が動く市場システム**は未実装（→ 03 §11）。
  現時点の売却額は固定値で、需給・時期による変動は持たない。
- **疲労（Fatigue）**：v1.1で廃止済み。旧xlsxに残っていたシートは削除した。
