| `hall.csv` | 03 §0.95 | HallBalance（ギルドのホールの押せる場所と椅子。**テーブル形式** `Id,Kind,Target,Label,X,Y,W,H,Squad,Seat`。Kind＝`area`〈押せる範囲〉・`seat`〈椅子の顔の中心、W は直径〉。X・Y・W・H は絵の幅・高さに対する割合。Target＝squad・dungeon・commission・warroom・research・infirmary・training・support・tavern・shop・dorm・ledger。絵を差し替えたら位置だけ合わせる） |
| `rivals.csv` | 03 §0.93・§0.94 | RivalBalance（ライバルギルドの人数・年齢・全盛の強さの幅・年齢の伸び・二つ名の表、年末のギルドの順位表の栄誉点とご褒美。key,value 形式） |
| `rival_guilds.csv` | 03 §0.93 | RivalBalance（ライバルギルドの一覧。**テーブル形式** `Id,Name,Specialty,Master`。Specialty＝Sword・Magic・Skill） |
| `rival_members.csv` | 03 §0.93 | RivalBalance（名簿を作るときに必ず入る看板の子。**テーブル形式** `GuildId,Name,Discipline,Age,Peak,Epithet`。Age は名簿を作るとき〈イザベラの来訪〉の年齢） |
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
| `tournaments.csv` | 03 §0.82 | TournamentBalance（大会の定義。**テーブル形式** `Id,Name,Kind,Discipline,Grade,Month,Week,MinStrength,MaxStrength,Prize1,Prize2,Prize4,Mood1,Mood2,Mood4,Note`。`{地方}`・`{フィールド}` は名前の差し込み、Month・Week が0の大会は暦を作るときに決める） |
| `tournament.csv` | 03 §0.82 | TournamentBalance（大会の式・暦・施設の開放の閾値。key,value 形式） |
| `isabella.csv` | 03 §0.84 | IsabellaBalance（イザベラの来訪・交流戦・派遣の教官。key,value 形式） |
| `postgame.csv` | 03 §0.90 | PostGameBalance（エンディングのあと：主が蘇るまでの週数・異変の季節・ルミナの能力と立ち絵。key,value 形式） |
| `story/story_01.txt`〜`story_04.txt` | 03 §0.86・§0.89 | StoryBalance（物語の台詞。物語帳〈アーティファクト「ギルド物語帳」〉の「台詞 01〜04」を書き出したもの。`s04_narration` はエンディングの窓の締めの語り。**CSVではない**：`## 場面のId　見出し` で場面が始まり、台詞は物語帳と同じ書式。書式はファイルの先頭の注記。場面を出す条件はコード `StorySystem`。物語帳を直したら書き出し直す。§0.91 からどの場面も頭に `〔背景：場面名〕`） |
| `story_images.csv` | 03 §0.91 | StoryImageBalance（物語の会話の小窓に出す背景と立ち絵。**テーブル形式** `Kind,Name,Expression,File`。Kind＝`background`・`portrait`、Name＝背景の名前〈台詞の〔背景：…〕〉か話者名〈`\|` で別名〉、Expression＝立ち絵の表情〈空は「通常」〉、File＝`assets/story/` からの相対パス。画像がまだ無い行も置いてよく、ゲームはファイルがあるときだけ出す） |
| `facility_unlock_lines.csv` | 03 §0.82・§0.84 | TournamentBalance.UnlockLines（施設が開いたときの台詞。**テーブル形式** `Style,Facility,Text`。Style＝Albert／Isabella／Royal、Facility＝Training・FirstTraining・各施設名・Any、`{facility}`・`{level}`・`{discipline}`・`{event}` を差し込む） |
| `titles.csv` | 03 §0.83 | HonorBalance.Titles（二つ名15種。**テーブル形式** `Id,Name,Rank,Param,Note`。Rank＝Legend／Name／Honor／Plain、Param＝条件の回数・年数・週数、Name の `{G1}` は大会の名前で埋める。条件の判定はコード `HonorSystem.GetTitles`） |
| `honor.csv` | 03 §0.83 | HonorBalance（殿堂の条件と顧問の効果の倍率・関係タグの閾値・引退式の行数。key,value 形式） |
| `retirement_lines.csv` | 03 §0.83 | HonorBalance.RetirementLines（引退式のアルベールの言葉。**テーブル形式** `Key,Text`。Key＝HallOfFame／Legend／Name／Honor／Plain／Short、`{name}`・`{title}` を差し込む） |
| `master_mood.csv` | 03 §8.1・§8.1.1 | MasterMoodBalance（マスターの機嫌の初期値・成果ごとの増減・退屈減衰・強制除籍への激怒・段階閾値・内職売上倍率・研究の手伝い〈旧・待機お手伝い〉の額／機嫌） |
| `recruitment.csv` | 03 §2.4・§7.3 | RecruitmentSystem, NameGeneratorBalance（第1週の新春ドラフト：`DraftCandidateCount`・`DraftHireCount`。旧 `TutorialCandidateCount` は削除）。先天特性の確率 `InnateTraitChancePercent` は§0.55で5→3（候補19種、1人あたり平均約0.57個）。生まれつきの欠点の確率 `InnateFlawChancePercent`（2、候補9種、平均約0.18個）は§0.56で新設。レア特性の確率 `RareTraitChancePercent`（1）と重み `RareTraitWeight_Genius`（1）・`RareTraitWeight_SingleStat`（2）は§0.57で新設 |
| `compatibility_advisor.csv` | 03 §5.3・§7 | CompatibilityBalance, AdvisorBalance（教官成長補正、参謀の大迷宮調査解析ボーナス・道中潜行走破力ボーナス、スカウト有望新人率） |
| `training.csv` | 03 §3.1〜3.4・§3.5改・§7.1 | TrainingBalance（勤勉 `DiligentGrowthRateBonus`、→ 03 §0.32。教官からの特性伝授 `TraitInheritanceBaseChance`・師匠肌ボーナス `MentorTraitInheritanceBonus`、→ 03 §0.34。教官の在任ボーナス `TrainerTenureBonusStepWeeks`・`TrainerTenureBonusPerStep`・`TrainerTenureBonusMax`、→ 03 §0.54。待機中の過ごし方 `IdleActivityHpRatio`・`SelfTrainingGrowthMultiplier`・`SelfTrainingHpCost`・研究の手伝い `ResearchCreditMax`・`ResearchCreditMaxDiscountRate`、→ 03 §0.73） |
| `trait.csv` | 03 §4.3・§5.3.2・§4.2.3 | TraitBalance（→ TraitCatalog。豪胆 `BraveSurvivalThresholdBonus`＝ボス戦の損耗−pt・注意深い `AttentiveScoutingBonus`＝隠密の寄与×(1＋値)・知識人 `ScholarAnalysisBonus`＝解析の寄与×(1＋値)、→ 03 §0.53。ペアシナジーの緩和係数は§0.53で削除）。全特性の表示名・説明・障害フラグ（`{Id}_DisplayName`・`{Id}_Description`・`{Id}_IsCurseOrInjury`、§0.31）。旧 `TraitTransmission*` 3キーは §0.34 で削除（→ training.csv）。§0.55で足した10種の効果量（`HawkEyeFlyingDamageReductionRate`・`SixthSenseInstantKillHpLossPct`・`GuardianGuardBonus`・`PathfinderTraversalBonus`・`SturdyMaxHpBonus`・`QuickHealerInjuryRecoveryBonus`・`QuickHealerRestRecoveryBonus`・`CheerfulSatisfactionBonus`・`HardworkerIdleHelpCreditMultiplier`（§0.73で `HardworkerIdleHelpGoldMultiplier` から改名）・`FireMageIntBonus`・`SwordMasterStatBonus`、すべて仮の値、→ 03 §5.3.2）。§0.56のマイナスの特性（地図読み `MapReaderTraversalBonus`、欠点の `Coward*`・`Reckless*`・`ClumsyStealthPenalty`・`PoorDirectionTraversalPenalty`・`SicklyMaxHpPenalty`・`FickleGrowthRatePenalty`・`MoodySatisfactionPenalty`・`SpendthriftWageMultiplier`、障害の `PoisonAftereffect*`・`LegWoundAgiReduction`・`ArmWoundStatReduction`・`Dread*`・`Burnout*`。すべて仮の値）。§0.57のレア特性（`GeniusStatBonus`＝0.15・`RareSingleStatBonus`＝0.30、仮の値） |
| `equipment.csv` | 03 §4.2.2 | EquipmentBalance（→ ItemCatalog）。**テーブル形式**（`Id,Price,HpBonus,BonusStr〜BonusLdr,note`、2026年9月に key,value 形式から移行）。`HpBonus`＝最大HP加算（武器は0）。§0.37で旧 `EffectValue`（個人CP／最大HPの二義）を個人CPの撤廃に伴い `HpBonus` へ改名 |
| `boss_gimmicks.csv` | 03 §4.5.4 | BossGimmickBalance（ボスのギミック7種への備えと罰、毒状態、ギミックの数。2026年10月・§0.68。旧 `consumables.csv` は撤去） |
| `boss_power_weights.csv` | 03 §4.5.4・§2.1 | DungeonBalance.GetBossPowerWeights（職業ごとの討伐火力の重み、2026年10月・§0.72。どの職業も合計4.2。dungeon.csv の `BossPowerWeight_*` は職業を決めない試算用の共通の重み）。ほかに §0.72 で combat.csv `ClericBlessingLossPctPerMnd`（0.08）・`ClericBlessingSevereThresholdRate`（0.8）＝神官の加護、recruitment.csv `JobAptitudePaBonus`（10）・`JobInaptitudePaPenalty`（10）＝採用の能力を職業に寄せる、dungeon.csv `GrowthJobWeightFloor`（1）＝出撃成長の能力の選び方を足した |
| `potential_estimate.csv` | 03 §0.74・§2.4・§5.1 | PotentialEstimateBalance（副官の見立て〈PAを隠して段階S〜Dで見せる〉の幅・目利き〈スカウトの顧問・段階研究・在籍の学者〉・在籍の月日で狭まる割合・魂魄融和の娘の倍率・確かとする幅・段階の境目、2026年10月・§0.74） |
| `progression.csv` | 03 §4.5.1・§4.2.2 | ProgressionBalance（初期の同時出撃枠、店の上位装備が入荷する倒したボスの数 `ShopTierUnlockBosses_1〜3`＝§0.61） |
| `dungeon.csv` | 03 §4.5.1・§4.5.4 | DungeonBalance（ボス能力重み・未踏破重損耗・ボス間隔・撃破実績点・出撃成長回数） |
| `dungeon_traversal.csv` | 03 §4.5.3 | DungeonTraversalBalance（**走破力の重み（VIT/MND/隊長LDR）**・**1歩の消費の尺度 `FloorsPerRatio`**（比率1.0の深さで週に進める階層数、4.0。進軍ランクの段 `RankRatioScale` は別。1歩の消費＝その階層の要求値÷(走破力×この値)÷解析倍率、週の予算1、→ 03 §0.49。旧リニア進軍は §0.25）・進軍ランク別の既踏損耗率・調査度連動走破倍率・夜目 `NightVisionUnexploredDamageReductionRate`（→ 03 §0.32）） |
| `scouting.csv` | 03 §4.5.3 | ScoutingBalance（**隠密適性の重み・専門職ボーナス・人数倍率・重装ペナルティ**、迷宮調査の4段階護衛判定・解析成果倍率・護衛HP損耗） |
| `gathering.csv` | 03 §4.5.5・§0.66 | GatheringBalance（採取スコア係数・職業ボーナス・報酬ゴールド、護衛の要求倍率と段階ごとの素材・損耗の倍率） |
| `materials.csv` | 03 §4.5.5・**§4.8.1** | MaterialBalance（全5フィールドの採取素材定義・MinFloor・基準獲得数・**売却額 `SellPrice`**） |
| `affixes.csv` | 03 §4.7.3・§4.2.2 | AffixBalance（鑑定品のランダムアフィックス＝接頭辞・接尾辞の一覧。**テーブル形式** `Id,Type,Name,TargetStat,MinValue,MaxValue,Tier,AllowedSlots,Weight`、2026年9月・§0.39） |
| `portraits.csv` | 03 §2.1 | PortraitBalance（採用の応募者に割り当てる顔グラフィックの一覧。**テーブル形式** `Id,Jobs,HairColor,EyeColor`。Id＝`assets/portraits/{Id}.png`、Jobs＝似合う職業の `\|` 区切りまたは `All`。2026年9月・§0.46） |
| `uniques.csv` | 03 §4.7.5 | UniqueBalance（固有武具＝固定アーティファクト〈紫〉・伝説級〈金〉・依頼人の固有武具〈§0.64〉の一覧。**テーブル形式** `Id,Grade,Name,BaseItemId,HpBonus,BonusStr〜BonusLdr,CounterGimmick,DropFieldId,DropFloor,SellPrice,PatronId`。伝説級は入手元のボス〈フィールドId・階層〉と対策ギミック、売値0＝売却不可。2026年9月・§0.45。§0.64で Grade `Patron` と列 `PatronId`〈commission_clients.csv の Id。Patron 以外は空〉を追加。依頼人の固有武具は売値0＝売却不可） |
| `research.csv` | 03 §4.6 | ResearchBalance（アルベール研究室の24プロジェクト（うち内職強化 SideBusinessGoldBonus 4種、魂魄融和の秘薬の解禁 `res_soul_fusion`＝SoulFusionUnlock・効果値0、§0.58、段階研究11種＝RecruitPaBonus・GrowthRateBonus・SoulFusionPaBonus・CultureTankBonus、§0.60）・必要素材・ゴールド・効果種別・効果値・**前提 `Prerequisite`**（§0.60で列を追加。空＝前提なし、段階研究のII・IIIは前の段のId）・**系統 `Branch`・段 `Lane`**（§0.77で列を追加。研究室のツリーの行。列は必要な素材のフィールドから決まる）） |
| `squad_orders.csv` | 03 §4.0.3 | SquadOrderBalance（部隊の方針と自動出撃、2026年10月・§0.63）：自動出撃の条件 `AutoDispatchMinHpPercent`（70）・扉前の構え（慎重・標準・強気、§0.68）ごとの火力の倍率・HP・備えの条件 |
| `commissions.csv` | 03 §4.9・§4.10 | CommissionBalance（依頼と迷宮の異変、2026年10月・§0.64）：依頼の届き方（届き始めは初めての入賞の次の季節・§0.84、`OffersPerSeason`＝3・`MaxAccepted`＝2・期限 `DeadlineWeeks_*`＝撃破24／他12・期限の知らせ `DeadlineWarningWeeks`＝2）、条件（納品の個数 `DeliverCountBase`・`DeliverFloorsPerExtra`、派遣の基準 `LoanRank`＝3番目・`LoanMinThreshold`＝20、派遣の期間と帰還の成長 `LoanWeeks`＝12・`LoanGrowthRolls`＝12・`LoanTraitChance`＝0.25〈§0.85〉）、報酬（`RewardMultiplier_*`＝撃破0.2・完全解析0.15・納品0.15・派遣1.5、`CompletionMoodGain`＝10、`FailureMoodLoss`＝10、遺物 `RewardRelicRollBonus`＝25・`RewardRelicMinRarity`＝Epic、依頼人の固有武具 `PatronUniqueCompletions`＝5件）、異変（`FirstAnomalyWeek`＝13・`AnomalyAnnounceWeekOfSeason`＝4・`AnomalyDurationWeeks`＝4・倍率 `Anomaly_*`） |
| `commission_clients.csv` | 03 §4.9・§0.85 | CommissionBalance.Clients（依頼人5人。**テーブル形式** `Id,Name,Types,BonusMaterialId,BonusMaterialCount,AlbertLine,LoanStats,LoanTraits`。Types は `Defeat\|Survey\|Deliver\|Loan` の `\|` 区切り、BonusMaterial はおまけの素材〈空＝なし〉、AlbertLine は掲示中の依頼に添えるアルベールの一言、LoanStats・LoanTraits は派遣から帰ってきたときに伸ばす能力と付く特性の候補〈§0.85、`\|` 区切り。派遣を頼む依頼人は LoanStats が必須〉） |
| `commission_texts.csv` | 03 §4.9 | CommissionBalance（依頼文の文例。**テーブル形式** `ClientId,Type,Text`。{field}{boss}{floor}{material}{count}{stat}{value}{weeks} を差し込む。依頼人が扱う種類ごとに1本以上ないと起動失敗） |
| `dungeon_anomalies.csv` | 03 §4.10 | CommissionBalance（迷宮の異変5種の名前と予告文。**テーブル形式** `Type,Name,Text`。{field}{boss}{floor}{weeks} を差し込む。倍率は commissions.csv） |
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

**携行ポーチの撤去（2026年10月、→ 03 §0.68）：** `consumables.csv`（大迷宮のボスギミック対策4種〈解毒薬15G・溶解液25G・捕縛網20G・
身代わりの護符20G〉の価格）は、携行ポーチと `ConsumableBalance`・`ConsumableCatalog` と共に削除した。旧セーブの返金に使う
撤去時の価格は `GameState.LegacyConsumablePrice` にだけ残している。

**ボスのギミック（2026年10月、→ 03 §0.68）：** `boss_gimmicks.csv`（`BossGimmickBalance`）を新設した。ギミックへの対策は
「備え」（0〜1）＝そのボスで効く職業の同行 `GimmickRoleReadiness`＋対策の能力の部隊合計÷基準の値（上限1）として段階的に効き、
足りない分（不足＝1−備え）だけ下の罰が効く。能力の基準の値と危険度の式は `SampleData.CreateBossGimmicks`（§0.68以前と同じ）。

| キー | 値 | 意味 |
|---|---|---|
| `GimmickRoleReadiness` | 0.5 | 効く職業が1人でも同行しているときの備え |
| `GimmickCountStep`・`GimmickCountMax` | 5・3 | ギミックの数＝1＋(何体目＋(フィールド順−1)×2)÷5、上限3 |
| `ExtraGimmickDangerReduction` | 1 | 2個目以降の危険度を下げる段 |
| `HeavyArmorPowerPenalty` | 0.30 | 重装甲：部隊火力×(1−0.30×不足) |
| `RegenerationRequirementBonus` | 0.30 | 再生：要求火力×(1＋0.30×不足) |
| `SwarmRearPowerPenalty`・`SwarmDamageFactor` | 0.40・0.5 | 群れ：後衛の火力×(1−0.40×不足)、損耗の加算は他の損耗型の半分 |
| `CharmExtraHpLossPct` | 40 | 魅了：操られた隊員の損耗に40%×不足を足す（火力は×備え） |
| `PoisonStatusStatPenalty`・`PoisonStatusWeeks`・`ResistPoisonStatusWeeksRate` | 0.30・4・0.5 | 猛毒の毒状態：全能力−30%×不足が4週。耐毒体質の本人は週数半分 |

`squad_orders.csv` の扉前の自動判断のキー（`AutoEngageMinHpPercent`・`AutoEngagePowerMargin`）は、扉前の構え（慎重・標準・強気）の
`{構え}PowerMargin`・`{構え}MinHpPercent`・`{構え}MinReadiness`・`{構え}MinInstantKillReadiness` に置き換えた
（慎重 1.2・80・1.0・1.0／標準 1.0・60・0・0.5／強気 0.9・40・0・0）。

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

## 依頼人のイザベラと「派遣」の改訂で追加／変更されたキー（2026年10月、→ 03 §0.85）

すべて仮の値。`campaign 10 1440` で10回ともクリア、13年目〔11〜15〕（§0.84は15年目〔14〜19〕。献上で子を手放す代償が無くなったため。派遣の依頼を受けない場合も13年目〔11〜14〕）。

**commissions.csv**（献上 Tribute → 派遣 Loan）

| キー | 値 | 意味 |
|---|---|---|
| `DeadlineWeeks_Loan` | 12 | 派遣の依頼の返事の期限（旧 `DeadlineWeeks_Tribute`） |
| `LoanRank`・`LoanMinThreshold` | 3・20 | 派遣の基準（旧 `TributeRank`・`TributeMinThreshold`。値は同じ） |
| `LoanWeeks` | 12 | 派遣の期間（送り出した週を含めてこの週数の決算のあとに帰る） |
| `LoanGrowthRolls` | 12 | 帰ってきたときの成長の抽選の回数（訓練所Lv1・教官なしと同じ確率） |
| `LoanTraitChance` | 0.25 | 帰ってきたときに依頼人ごとの特性が1つ付く確率 |
| `LoanOfferSeasons` | Spring;Autumn | 派遣の依頼が届く季節（毎季節だとクリアが12年目に早まったため、年2回まで） |
| `RewardMultiplier_Loan` | 3 → **1.5** | 派遣の報酬ゴールド＝最前線のボスの撃破報酬×この倍率（旧 `RewardMultiplier_Tribute`） |

**commission_clients.csv**：列 `LoanStats`・`LoanTraits` を足した（辺境伯家＝STR・VIT・LDR／守り手・頑強・豪胆、エルフの里＝DEX・AGI・MND／夜目・健脚・地図読み）。
依頼人 `Knights`（王都の騎士団）を **`Isabella`（イザベラ〈白百合の杖〉）** に替え、Types の `Tribute` を `Loan` に。

**commission_texts.csv**：イザベラ（撃破3通り）・辺境伯家（派遣3通り）は物語帳の案、エルフの里（撃破2・完全解析1・納品1・派遣1）は長老ユーフェミアの言葉に書き直した。

**uniques.csv**：`PatronKnightsBlade` の名前を「騎士団の宝剣」→「**白百合の宝剣**」、PatronId を `Isabella` に（Id は旧セーブのためそのまま）。

## イザベラの来訪から大会を開く改訂で追加／変更されたファイルとキー（2026年10月、→ 03 §0.84）

すべて仮の値。物語の正本はアーティファクト「ギルド物語帳」（「ゲームの決まりが変わる点」）。
`dotnet run --project tools/balance_sim -c Release -- campaign 10 1440` で10回ともクリア、15年目〔14〜19〕（§0.83は14年目〔12〜17〕。シミュレーターでは来訪が2.6年目と遅く、最初の訓練所・大会・依頼がその分遅れる）。

**isabella.csv**（新規）

| キー | 値 | 意味 |
|---|---|---|
| `VisitFieldId`・`VisitFloor` | forest・40 | このフィールドのこの階層のボスを初めて倒した週にイザベラが来訪し、翌月の第4週に最初の交流戦 |
| `ExchangeFactor_Sword`・`_Magic`・`_Skill` | 1.08・1.18・1.08 | 交流戦の相手の強さ＝来訪したときの自分のギルドで部門の強さが最も高い子の値×この倍率（初回に勝てるのは3本勝負で約35%） |
| `ExchangeGrowthPerMatch` | 0.04 | 交流戦を1回行うごとに相手が強くなる割合 |
| `ExchangeGrowthPerYear` | 0.05 | 来訪から1年ごとに相手が強くなる割合（週で按分） |
| `ExchangeOpponent_*` | ブリジット・セシリア・ニナ | 部門ごとの相手の名前 |
| `ExchangeWeekOfMonth` | 4 | 交流戦を行う月の中の週 |
| `ExchangePrize`・`ExchangeMood` | 1000・8 | 2勝目以降の交流戦に勝ったときの賞金と機嫌（初勝利のご褒美は派遣の教官） |
| `GuestTrainerName`・`GuestTrainerAge` | マルグリット・34 | 派遣の教官 |
| `GuestTrainerWeeks` | 24 | 派遣の教官がいる週数（訓練所が建っていれば来て、来た週から数える） |
| `GuestTrainerTopCount` | 4 | 派遣の教官の能力＝来た週の現役の上位この人数の、能力ごとの平均 |
| `GuestTrainerTraitId` | Diligent | 派遣の教官の特性（勤勉） |

**commissions.csv**：`FirstOfferWeek` を **`FirstAnomalyWeek`**（13）に改めた。迷宮の異変が起き始める週だけを表す。依頼が届き始めるのは初めての入賞（ベスト4以上）の次の季節のはじめ（セーブの `CommissionsFromWeek`）。

**facility_unlock_lines.csv**：Style の `Adjutant`（副官の提案）を **`Isabella`**（イザベラの助言）に替えて台詞を書き直し、最初の交流戦のあとに開く訓練所の台詞 `Isabella,FirstTraining`（3通り。`{discipline}`＝剣・魔・技、`{facility}`）を足した。

## 戦績・二つ名・殿堂・引退式の改訂で追加されたファイルとキー（2026年10月、→ 03 §0.83）

すべて仮の値。設計は `docs/検討中_大会と育成の栄光.md` §4.1（段2）。ゲームの計算に効くのは殿堂入りした顧問の倍率だけ。
`dotnet run --project tools/balance_sim -c Release -- campaign 10 1440` で10回ともクリア、14年目前後（12〜17年目。§0.82の15年目〔14〜18〕と乱数の揺れの範囲）。

**titles.csv**（格：伝説＞名＞誉＞並。表に出すのはいちばん格の高い1つ）

| 格 | 二つ名（Param） |
|---|---|
| 伝説 | 王都最強・三冠・{G1}三連覇（3年）・二代制覇 |
| 名 | {G1}二連覇（2年）・双璧の覇者（2部門）・迷宮の踏破者・不屈（重傷が治ってから48週以内のG1優勝）・深淵を討つ者 |
| 誉 | {G1}の覇者・主討ち（主役10回）・新人王・叩き上げ |
| 並 | 古強者（撃破20回）・入賞常連（入賞10回） |

**honor.csv**

| キー | 値 | 意味 |
|---|---|---|
| `HallOfFameG1Wins` | 2 | 殿堂入りに要るG1の優勝の数（ほかに最強決定戦の優勝・伝説の称号でも入る。引退のときに判定） |
| `HallOfFameAdvisorMultiplier` | 1.1 | 殿堂入りした者を教官・参謀・スカウト顧問にしたときの効果の倍率 |
| `BuddyCompatibility`・`BuddyBossKills` | 60・5 | 関係タグ「戦友」：相性60以上で、同じ部隊のボス撃破5回 |
| `PerfectPairCompatibility` | 100 | 関係タグ「名コンビ」の相性 |
| `MentorWeeks` | 8 | 関係タグ「師弟」：教官のもとで訓練した週数 |
| `RelationsShown` | 3 | 戦績の頁に出す関係の深い相手の人数 |
| `CeremonyHighlights`・`CeremonyTitleLines` | 8・2 | 引退式の歩みの行数（加入と引退は必ず載せる）・そのうち「二つ名を得た」行の上限 |
| `ShortCeremonyMaxEntries` | 3 | 年表がこの行数以下で、称号も勝ち鞍も無い者の引退式は短い版 |

- `retirement_lines.csv`：引退式のアルベールの言葉（殿堂・伝説・名・誉・並・短い版で2〜3通り。冒険者ごとに決まった1つを選ぶ）。

## 大会と、施設を大会のご褒美で開く改訂で追加されたファイルとキー（2026年10月、→ 03 §0.82）

すべて仮の値（ほかの要素と合わせて今後調整する・ユーザー判断）。設計は `docs/検討中_大会と育成の栄光.md`。
`dotnet run --project tools/balance_sim -c Release -- campaign 10 1440` で10回ともクリア、15年目前後（14〜18年目。`from=10` の10回は15年目前後〈14〜17〉）。
シミュレーターでは主力4名をG1・特別な大会・得意な部門のG2だけに出し、ほかの大会は控えを出す（`nomain`＝主力を個人の大会に出さない、`notourney`＝大会に出ない）。

**tournaments.csv**（相手の強さの幅。これに部門の倍率と年ごとの上昇を掛ける）

| 大会 | 格 | 相手の強さ | 賞金（優勝／準優勝／ベスト4） | 機嫌 |
|---|---|---|---|---|
| 新人戦（春1の月 第4週） | G3 | 40〜70 | 300／150／80 | +5 |
| 地方大会（剣・魔・技） | G3 | 50〜95 | 300／150／80 | +5 |
| 王都大会（剣・魔・技） | G2 | 85〜135 | 1,000／500／250 | +8／+3 |
| 王国剣闘祭・魔導祭・射技祭（秋2・秋3・冬1の月 第4週） | G1 | 110〜170 | 3,000／1,500／700 | +15／+6／+2 |
| 迷宮踏破杯（冬2の月 第4週、部隊戦） | G1 | 250〜500（走破力） | 3,000／1,500／700 | +15／+6／+2 |
| 王都最強決定戦（冬3の月 第4週） | G1 | 140〜190 | 5,000／2,000／1,000 | +20／+8／+3 |
| 領主杯・辺境伯杯（招待） | 特別 | 85〜135 | 1,500／700／300・1,000／500／250 | +8／+3 |
| 王族の御前試合（招待） | 特別 | 110〜170 | 3,000／1,500／700 | +10／+4 |

**tournament.csv**

| キー | 値 | 意味 |
|---|---|---|
| `DisciplineFactor_Sword`・`_Magic`・`_Skill`・`_Party` | 1.0・0.75・1.1・1.0 | 相手の強さに掛ける部門ごとの倍率（魔法職は部門の強さが3割ほど低い・技は高い。シミュレーターの実測） |
| `YearlyGrowth` | 0.02 | 相手の強さが1年ごとに上がる割合 |
| `BracketSize`・`EntrantsPerGuild` | 8・2 | トーナメントの人数・ギルドから1大会に出られる人数 |
| `WinExponent`・`LuckMin`・`LuckMax` | 4・0.9・1.1 | 勝つ見込み＝強さ^4の比。強さに掛ける運の幅 |
| `HpFactorBase`・`HpCostPerMatch` | 0.6・0.1 | 試合の強さのHPの補正＝0.6＋0.4×HP比率。1試合ごとに最大HPの10%を使う |
| `SatisfactionLow*`・`SatisfactionHigh*`・`BraveFactor` | 40未満×0.9・80以上×1.05・×1.05 | 満足度・豪胆の補正 |
| `PartyCompatibilityBonusMax` | 0.1 | 部隊戦の相性の平均による上乗せの上限 |
| `WinnerSatisfaction` | 10 | 優勝した者の満足度 |
| `RestRecoveryMultiplier` | 2.0 | 休養：大会の月のHPの回復の倍率 |
| `PushHpCost`・`PushGrowthMultiplier` | 5・1.0 | 追い込み：毎週のHPの消費・部門の能力の成長の抽選の倍率 |
| `LocalBaseCount`・`LocalYearBonusMax`・`LocalPlacingsPer`・`LocalPlacingsBonusMax`・`LocalMax` | 8・4・5・4・16 | 地方大会の数＝8＋(年−1、最大4)＋(入賞÷5、最大4)、最大16 |
| `LocalRegions` | 東方;西方;… | 地方大会の名前に使う地方（`;` 区切り） |
| `InviteLeadMonths` | 2 | 招待大会を何か月後に置くか |
| `PartyEntryMinFloor` | 30 | 部隊戦に出るのに要る、どこかで倒したボスの階 |
| `DormPlacings_Lv2`〜`Lv5` | 3・8・15・25 | 宿舎が開く入賞（ベスト4以上）の累計 |
| `TavernPrize_Lv2`〜`Lv5` | 2,000・8,000・20,000・40,000 | ギルド酒場が開く賞金の累計（G） |

- 訓練所・作戦資料室・冒険者支援室の開く条件（大会の格と順位）はコード（`FacilityUnlockSystem.ComputeLevel`）にある（→ 03 §0.82 #6）。
- `facility_unlock_lines.csv`：開いたときの台詞（施設・型ごとに2〜3通り）。

## 施設の上限Lvで追加されたキー（2026年10月、→ 03 §0.79）

施設を改築できる上限のLvを、倒したボスの数（全フィールドの合計）で上げる（全施設共通・すべて仮の値。`facility.csv`）。**§0.82で医務室だけの条件になった**（ほかの施設は大会などのご褒美で開く、→ 下の「大会」の節）。
`dotnet run --project tools/balance_sim -c Release -- campaign 10 1440` で10回ともクリア、14年目前後（12〜15年目。`from=10` の10回は13年目前後〈12〜15〉）。

| キー | 値 | 意味 |
|---|---|---|
| `LevelCapBosses_Lv2` | 5 | ボスをこの数倒すとLv2まで改築できる（シミュレーターでは1年目の後半） |
| `LevelCapBosses_Lv3` | 12 | 同・Lv3まで（2〜3年目） |
| `LevelCapBosses_Lv4` | 20 | 同・Lv4まで。Lv3→4で専門を選ぶ（4年目） |
| `LevelCapBosses_Lv5` | 30 | 同・Lv5まで（7年目） |

## 研究のツリーで追加／変更された列と前提（2026年10月、→ 03 §0.77）

`research.csv` に列 `Branch`（系統：Recovery・Dungeon・Gathering・Growth・Talent・Trade・SoulFusion）と `Lane`（系統の中の段、0〜）を足した。
研究室のツリーの行になる（列は必要な素材のフィールドで決まるので CSV には持たない）。効果・費用・素材は変えていない。
前提（`Prerequisite`）を次のように足した。`dotnet run --project tools/balance_sim -c Release -- campaign 10 1440` は §0.76 と同じ12年目〔12〜16〕（`from=10` は14年目〔12〜16〕）。

| 研究 | 前提（新設） |
|---|---|
| `res_cave_ointment`（硬化軟膏の調合） | `res_herb_poultice`（薬草湿布の調合） |
| `res_abyss_preservation`（魂魄安定の霊香） | `res_cave_ointment` |
| `res_ruins_tactics`（古代戦術録の解読） | `res_scout_reagent`（生体蛍光試薬） |
| `res_canyon_tread`（耐熱踏破法） | `res_light_tread`（軽量踏破靴） |
| `res_energy_tonic`（滋養強壮アンプル） | `res_beauty_lotion`（潤い美肌液の調合） |
| `res_trade_route`（古代香料の王都外販） | `res_energy_tonic` |
| `res_vitality_elixir`（不老長生薬の密売） | `res_trade_route` |
| `res_elixir_brewing`（霊薬の調合法） | `res_training_method_1`（古代戦技の写本） |

## 施設の専門で追加されたキー（2026年10月、→ 03 §0.76・§6.2）

宿舎以外の施設は、Lv3→4の改築で専門を2つから1つ選ぶ。Lv4・5の上乗せは選んだ専門の分だけ（すべて仮の値。すべて `facility.csv`）。
訓練施設の精鋭は既存の `TrainingGrowthMultiplier_Lv4`・`Lv5`（1.45・1.6）を使う。
`dotnet run --project tools/balance_sim -c Release -- campaign 10 1440` で10回ともクリア、精鋭は12年目前後（12〜16年目）、オプション `rivalry`（切磋琢磨）は14年目前後（11〜16年目）。

| キー | 値 | 意味 |
|---|---|---|
| `SpecialtyFromLevel` | 3 | このLvから次のLvへの改築で専門を選ぶ |
| `RemodelCostPerLevel` | 1000 | 改装（専門の選び直し）の費用＝今のLv×この額 |
| `RemodelWeeks` | 2 | 改装の工期（週） |
| `RivalrySlotCapacity` | 2 | 切磋琢磨の訓練枠 |
| `RivalryGrowthRate_Lv4` ／ `_Lv5` | 0.7 ／ 0.9 | 切磋琢磨の成長の確率＝Lv3の倍率×この値（×0.91 ／ ×1.17） |
| `SanatoriumHpMultiplierPerLevel` | 0.35 | 療養院：Lv4以上の1Lvごとに足すHP自然回復の倍率 |
| `SanatoriumInjurySpeedPerLevel` | 1 | 療養院：Lv4以上の1Lvごとに足す重傷の回復（週/週） |
| `FieldAidSurvivalPerLevel` | 2 | 戦地救護：Lv4以上の1Lvごとに足すボス戦の致命の損耗の閾値 |
| `AppraisalEyePerLevel` | 0.1 | 目利き：Lv4以上の1Lvごとに足す目利き |
| `RecruitingCandidatesPerLevel` | 1 | 募集：Lv4以上の1Lvごとに足す応募者数 |
| `LeisureSatisfactionPerLevel` | 2 | 憩い：Lv4以上の1Lvごとに足す満足度の回復（pt/週） |
| `TradeSideJobGoldPerLevel` | 100 | 商い：Lv4以上の1Lvごとに足す内職の基本売上（G/月） |
| `AnalysisIntelRatePerLevel` | 0.1 | 解析：Lv4以上の1Lvごとに足す解析率の上昇倍率 |
| `PathfindingTraversalPerLevel` | 10 | 踏破：Lv4以上の1Lvごとに足す走破力 |

## 訓練施設を3つに組み直す改訂で追加／変更されたキー（2026年10月、→ 03 §0.75）

訓練施設を鍛錬所（STR・VIT）・学問所（MND・INT）・技巧所（AGI・DEX）の3つにした。枠はLvにかかわらず1名で、Lvは成長の確率に効く（すべて仮の値）。
`dotnet run --project tools/balance_sim -c Release -- campaign 10 1440` で10回ともクリア、12年目前後（12〜16年目。`from=10` の10回は14年目前後〈12〜16〉）。

| ファイル | キー | 値 | 意味 |
|---|---|---|---|
| `facility.csv` | `TrainingSlotPerLevel` → `TrainingSlotCapacity` | 1 | 訓練施設の枠（Lv1以上ならLvにかかわらずこの値。旧はLv×1） |
| `facility.csv` | `TrainingGrowthMultiplier_Lv1`〜`Lv5`（新設） | 1.0／1.15／1.3／1.45／1.6 | 施設Lvごとの成長の確率の倍率（`aging.csv TrainingFacilityMultiplier` に掛ける）。Lv4・5は⑤の2段目で専門〈精鋭／切磋琢磨〉に分ける予定 |
| `facility.csv` | `TrainingSpecialtyGrowthMultiplier`（新設） | 1.1 | 片方の能力に特化したときの成長の確率の倍率（成長は選んだ能力にだけ入る） |

## PAを隠して副官の見立てにする改訂で追加されたキー（2026年10月、→ 03 §0.74）

`potential_estimate.csv` を新設した（すべて仮の値）。採用時の週給・契約金は副官の見立ての総合PA×係数（`economy.csv` の係数は据え置き）。
`dotnet run --project tools/balance_sim -c Release -- campaign 10 1440` で10回ともクリア、15年目前後（12〜18年目。`from=10` の10回は14年目前後）。

| キー | 値 | 意味 |
|---|---|---|
| `BaseWidth` | 20 | 見立てのずれの幅（目利き0・在籍0か月で ±20） |
| `MaxEye` | 0.8 | 目利きの上限 |
| `ScoutMasterEyeCoeff` | 0.004 | スカウトの顧問の (LDR+DEX)/2 に掛ける係数 |
| `ResearchEyePerStep` | 0.1 | 推薦状などの段階研究（RecruitPaBonus）1段ごと |
| `ScholarEyeCoeff` | 0.002 | 在籍の学者の最も高い INT に掛ける係数 |
| `MonthlyNarrowRate` | 0.1 | 在籍1か月ごとに幅を狭める割合（10か月で確定） |
| `SoulFusionWidthRate` | 0.5 | 魂魄融和の娘の幅の倍率 |
| `ConfirmWidth` | 1 | 幅がこの値以下で「確か」（「B?」→「B」） |
| `RankS` ／ `RankA` ／ `RankB` ／ `RankC` | 90 ／ 80 ／ 70 ／ 55 | 段階の境目（未満は D） |

## 待機中の過ごし方で追加／変更されたキー（2026年10月、→ 03 §0.73）

待機お手伝い（+15G・機嫌+1）を、冒険者ごとに決める「研究を手伝う」「自主練」に置き換えた。
`dotnet run --project tools/balance_sim -c Release -- campaign 10 1440` で10回ともクリア、13年目前後（12〜17年目）。
`selftrain`（機嫌60以上で自主練）・`selftrainall`（常に自主練）も10回ともクリア、13年目前後（12〜15年目）。

| ファイル | キー | 値 | 意味 |
|---|---|---|---|
| master_mood.csv | `IdleAdventurerHelp_ResearchCredit`（旧 `IdleAdventurerHelp_Gold`＝15G） | 10 | 研究を手伝う1名ごとに週ごとに貯まる研究の手伝い（G相当。所持金は増えない） |
| master_mood.csv | `IdleAdventurerHelp_Mood` | 1 | 同上の機嫌の上昇（値は据え置き） |
| training.csv | `IdleActivityHpRatio` | 0.7 | 研究の手伝い・自主練をする条件：HPが最大HP×この比率を超える（以下なら静養。旧・待機お手伝いはHP満タンが条件） |
| training.csv | `SelfTrainingGrowthMultiplier` | 0.5 | 自主練の成長ロールの倍率（訓練施設は1.0＋教官） |
| training.csv | `SelfTrainingHpCost` | 5 | 自主練をした週のHP消費（静養の回復は受ける） |
| training.csv | `ResearchCreditMax` | 5000 | 研究の手伝いが貯まる上限 |
| training.csv | `ResearchCreditMaxDiscountRate` | 0.5 | 研究1件の研究費から割り引ける上限の割合 |
| trait.csv | `HardworkerIdleHelpCreditMultiplier`（旧 `HardworkerIdleHelpGoldMultiplier`） | 1.5 | 働き者：研究の手伝いで貯める額の倍率。働き者・怠け者の説明文も研究の手伝いに合わせた |

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

## ライバルギルドの改訂で追加されたファイルとキー（2026年10月、→ 03 §0.93）

大会と育成の栄光 段3-1。すべて仮の値。`dotnet run --project tools/balance_sim -c Release -- campaign 10 1440` で10回ともクリア、13年目〔12〜14〕（変更前13年目〔11〜14〕）。
最初のG1優勝は剣8年目（10回中8回）・魔6年目（10回中9回）・技8年目（10回中5回）。ライバルの名簿の乱数で揺れる。

**rival_guilds.csv**（新規）：白百合の杖（Magic・イザベラ）・紅蓮の牙（Sword・ヴァレリア）・銀月の弓（Skill・シルヴィア）。

**rival_members.csv**（新規）：白百合の杖の看板。セシリア（Magic・21歳・全盛175・「氷華」）・ブリジット（Sword・18歳・165）・ニナ（Skill・17歳・160）。

**rivals.csv**（新規）

| キー | 値 | 意味 |
|---|---|---|
| `RosterSize`・`SpecialtyCount` | 6・3 | 1ギルドの人数と、そのうち得意な部門の人数（ほかの部門は1人ずつ、残りは抽選） |
| `StartAgeMin`・`StartAgeMax` | 18・25 | 名簿を作るとき（イザベラの来訪）の年齢の幅 |
| `RookieAge` | 18 | 年のはじめに入る新人の年齢（引退は aging.csv の RetirementAge＝26歳の年度末） |
| `PeakMin`・`PeakMax` | 110・175 | 全盛（26歳）の強さの幅（大会の相手の強さと同じ尺度） |
| `AgeCurve_18`〜`_26` | 0.30〜1.00 | 年齢ごとの強さ＝全盛×この値 |
| `RecordMinPlacing` | 4 | ライバルの戦績に残す順位 |
| `Epithets_Sword`・`_Magic`・`_Skill` | 6つずつ | 初めてG1を勝ったライバルに付く二つ名 |

**isabella.csv**：`ExchangeOpponent_*` は、ライバルの名簿がまだ無いとき（来訪の前）の交流戦の相手の名前になった（名簿があれば白百合の杖の看板、引退後はその部門の最強の子）。

## 年末のギルドの順位表の改訂で追加されたキー（2026年10月、→ 03 §0.94）

大会と育成の栄光 段3-2。すべて仮の値。シミュレーター（`campaign 10 1440`）でクリアは13年目〔11〜14〕。当ギルドは6年目から多くの年で1位（12年ほどのうち5〜9年）。

**rivals.csv**（追加）

| キー | 値 | 意味 |
|---|---|---|
| `StandingPoints_Final`・`_G1`・`_G2`・`_G3` | 15・10・4・2 | 栄誉点：優勝（王都最強決定戦・G1・G2と招待・G3） |
| `StandingRunnerUpRatio`・`StandingTop4Ratio` | 0.5・0.25 | 準優勝・ベスト4は優勝の点×この値（切り上げ） |
| `StandingPointsPerBoss` | 1 | 迷宮のボス1体ごとの栄誉点 |
| `StandingPrize_1`・`StandingMood_1` | 5000・15 | 1位のご褒美 |
| `StandingPrize_2`・`StandingMood_2` | 2000・5 | 2位のご褒美 |
