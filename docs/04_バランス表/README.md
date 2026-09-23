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
| `combat.csv` | 03 §4.2・§4.3 | PlacementBalance（配置補正）, CombatBalance（旧討伐フロー用の値の多くは旧クエスト撤去で休眠中） |
| `aging.csv` | 03 §3.0〜3.7 | GrowthBalance（年齢帯別成長ロール基礎確率＝新鋭/成長/全盛の3区分・難易度係数・成長量幅）, AgingSystem（満期引退年齢・稼働週数） |
| `growth_job_weights.csv` | 03 §3.1〜3.4 | GrowthBalance.JobStatWeights |
| `satisfaction.csv` | 03 §5.1・§5.2 | SatisfactionBalance |
| `facility.csv` | 03 §6・§6.1 | FacilityBalance |
| `master_mood.csv` | 03 §8.1・§8.1.1 | MasterMoodBalance（マスターの機嫌の初期値・成果ごとの増減・退屈減衰・強制除籍への激怒・段階閾値・内職売上倍率） |
| `recruitment.csv` | 03 §2.4・§7.3 | RecruitmentSystem, NameGeneratorBalance |
| `compatibility_advisor.csv` | 03 §5.3・§7 | CompatibilityBalance, AdvisorBalance（教官成長補正、参謀の大迷宮調査解析ボーナス・道中潜行走破力ボーナス、スカウト有望新人率） |
| `training.csv` | 03 §3.1〜3.4・§3.5改 | TrainingBalance |
| `trait.csv` | 03 §4.3・§5.3.2・§4.2.3 | TraitBalance（→ TraitCatalog）、ペアシナジーの隊長LDR緩和係数 |
| `equipment.csv` | 03 §4.2.2 | EquipmentBalance（→ ItemCatalog） |
| `consumables.csv` | 03 §4.5.4 | ConsumableBalance（→ ConsumableCatalog。大迷宮ボスギミック対策4種の価格） |
| `progression.csv` | 03 §4.5.1 | ProgressionBalance（初期の同時出撃枠） |
| `dungeon.csv` | 03 §4.5.1・§4.5.4 | DungeonBalance（ボス能力重み・未踏破重損耗・ボス間隔・撃破実績点・出撃成長回数） |
| `dungeon_traversal.csv` | 03 §4.5.3 | DungeonTraversalBalance（**走破力の重み（VIT/MND/隊長LDR）**・進軍ランク閾値・調査度連動走破倍率） |
| `scouting.csv` | 03 §4.5.3 | ScoutingBalance（**隠密適性の重み・専門職ボーナス・人数倍率・重装ペナルティ**、迷宮調査の4段階護衛判定・解析成果倍率・護衛HP損耗） |
| `gathering.csv` | 03 §4.5.5 | GatheringBalance（採取スコア係数・職業ボーナス・報酬ゴールド） |
| `materials.csv` | 03 §4.5.5・**§4.8.1** | MaterialBalance（全5フィールドの採取素材定義・MinFloor・基準獲得数・**売却額 `SellPrice`**） |
| `research.csv` | 03 §4.6 | ResearchBalance（アルベール研究室の12プロジェクト（うち内職強化 SideBusinessGoldBonus 4種）・必要素材・ゴールド・効果種別・効果値） |
| `relic.csv` | 03 §4.7・§4.8.2 | RelicBalance（未鑑定遺物の希少度4段階＝鑑定費用・鑑定結果比率・換金額と獲得個数の幅・武具の抽選プール・**売却額**、採取ドロップ確率、希少度ロール閾値、ボス撃破ドロップの補正） |

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
- `AdvisorBonusCoefficient` は旧クエスト用で加算先を失っており、休眠中のキー。

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

> **カタログ品の武具の売却額は新しいキーを作らない。** `equipment.csv` の `*_Price`（定価）の
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

- `Stealth_Weight_Agi`（1.0）／`Stealth_Weight_Dex`（1.0）／`Stealth_Weight_Ldr`（0.5）… 基礎隠密の重み。
- `Stealth_Bonus_RangerThief`（30）… 斥候（Ranger）・盗賊（Thief）1名につき加算。
- `Stealth_PartySize_Mult_1`〜`_4`（1.10／1.00／0.85／0.70）… 人数倍率（**乗算**）。
- `Stealth_HeavyArmor_Penalty`（30）… 重装者1名につき**倍率適用後に直接減算**
  （重装鎧の装備者、または職業が重戦士・騎士。両方該当でも1名分）。結果は0未満にならない。

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
