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
| `economy.csv` | 03 §8.1・§5.2・§8.3 | EconomyBalance（初期資金・週給/契約金/退職金係数・破産判定週数）, SubsidyBalance, SatisfactionBalance(賃金), RecruitmentSystem(契約金) |
| `combat.csv` | 03 §4.2・§4.3 | PlacementBalance（配置補正）, CombatBalance（旧討伐フロー用の値の多くは旧クエスト撤去で休眠中） |
| `aging.csv` | 03 §3.0〜3.7 | GrowthBalance（年齢帯別成長ロール基礎確率＝新鋭/成長/全盛の3区分・難易度係数・成長量幅）, AgingSystem（満期引退年齢・稼働週数） |
| `growth_job_weights.csv` | 03 §3.1〜3.4 | GrowthBalance.JobStatWeights |
| `satisfaction.csv` | 03 §5.1・§5.2 | SatisfactionBalance |
| `facility.csv` | 03 §6・§6.1 | FacilityBalance |
| `guild_rank.csv` | 03 §8.1 | GuildRankBalance.Thresholds |
| `guild_rank_params.csv` | 03 §8.1.1 | GuildRankBalance（名声増減・減衰） |
| `recruitment.csv` | 03 §2.4・§7.3 | RecruitmentSystem, NameGeneratorBalance |
| `compatibility_advisor.csv` | 03 §5.3・§7 | CompatibilityBalance, AdvisorBalance（教官成長補正、参謀の大迷宮調査解析ボーナス・道中潜行走破力ボーナス、スカウト有望新人率） |
| `training.csv` | 03 §3.1〜3.4・§3.5改 | TrainingBalance |
| `trait.csv` | 03 §4.3・§5.3.2・§4.2.3 | TraitBalance（→ TraitCatalog）、ペアシナジーの隊長LDR緩和係数 |
| `equipment.csv` | 03 §4.2.2 | EquipmentBalance（→ ItemCatalog） |
| `consumables.csv` | パーティ携行アイテム刷新仕様 | ConsumableBalance（→ ConsumableCatalog） |
| `progression.csv` | 03 §4.5.1 | ProgressionBalance（初期の同時出撃枠） |
| `dungeon.csv` | 03 §4.5.1・§4.5.4 | DungeonBalance（ボス能力重み・未踏破重損耗・ボス間隔・撃破実績点・出撃成長回数） |
| `dungeon_traversal.csv` | 03 §4.5.3 | DungeonTraversalBalance（走破力係数・進軍ランク閾値・調査度連動走破倍率） |
| `scouting.csv` | 03 §4.5.3 | ScoutingBalance（迷宮調査の4段階護衛判定・解析成果倍率・護衛HP損耗） |
| `gathering.csv` | 03 §4.5.5 | GatheringBalance（採取スコア係数・職業ボーナス・報酬ゴールド） |
| `materials.csv` | 03 §4.5.5 | MaterialBalance（全5フィールドの採取素材定義・MinFloor・希少度） |
| `research.csv` | 03 §4.6 | ResearchBalance（アルベール研究室の8プロジェクト・必要素材・ゴールド・効果種別・効果値） |

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

- `BankruptcyConsecutiveWeeksThreshold`（4週）… 破産＝唯一の敗北条件の判定週数
  （→ `DefeatSystem`）。脅威度の撤去で `security.csv` を廃止した際、脅威度とは無関係な
  この値だけをここへ移設した（→ 03 §0.10）。

**旧・環境ギミックの撤去（2026年9月、→ 03 §0.12）：** `gimmick.csv` は、旧通常クエストの
撤去（→ 03 §0.8）で発生源を失い呼び出し元が無くなっていた評価器（`GimmickEvaluator`・
`GimmickBalance`・`GimmickMitigationLevel`）と共に削除した。大迷宮のボスギミック
（`dungeon.csv`・`BossGimmickType`）は現役の別システムであり、影響を受けない。

`consumables.csv`はパーティ携行アイテム刷新仕様で新設した。ギミック相殺5種は価格のみ
（旧・環境ギミック用。評価器の撤去により現在は効果を持たないデータ）、効果アイテム3種
（煙幕弾・高品質傷薬・携帯糧食）は価格に加えて効果量を持つ。

**旧通常クエストの撤去（2026年9月、→ 03 §0.8）：** `quest.csv`・`quest_templates.csv`・
`quest_type_weights.csv`・`quest_scoring.csv`・`quest_events.csv`・`pair_synergy.csv`・
`success_rate.csv`・`emergency.csv` は、対応する旧クエスト系の実装と共に削除した。
`quest_type_weights.csv` の討伐（`Subjugation`）行の能力重みは、階層ボスの部隊火力として
`dungeon.csv` の `BossPowerWeight_*` へ同じ値のまま移設している。`progression.csv` は
初期の同時出撃枠のみを残し、昇格試験のキーは削除した。`guild_rank_params.csv` の
クエスト達成／失敗による名声増減キーも削除した。

## 凡例

- `key` … コード側の定数名に対応する識別子。**変更しないこと**（読み込み時のキーになる）。
- `value` … 調整対象の数値。ここを書き換えてバランスを調整する。
- `unit` … 単位。
- `note` … 説明・出典セクション・注意事項。

## CSV化していない値（コード側に残すもの）

以下は「バランス調整用の数値」ではなく**構造を定義する値**のため、意図的にCSVへ
出していない。変更すると他の数値の前提が連鎖的に崩れるため、コード変更として扱う。

- 1年=48週（`WeeksPerYear`）、1ヶ月=4週（`SubsidyBalance.WeeksPerMonth`）
- 年齢帯の境界（18/22。`Adventurer.AgeBand`。→ 03 §3.0の3区分）
- 各種クランプの上下限（満足度0〜100、相性0〜100、実効値の下限0）
- 施設の最大Lv（5）
- 訓練施設ごとの対象ステータス対応（戦士訓練所→STR/VIT 等。職業・施設の定義そのもの）
- 衰微対象ステータス（STR/AGI/VIT。§3.0の「フィジカル衰微」の定義そのもの）

## 未実装のため記載していない項目

- **勝利条件（最終討伐クエスト＝魔王戦）**：詳細未設計のため数値なし。
  中間目標（Aランク到達で`FinalQuestUnlocked`が立つ）までは実装済みだが、
  ここにバランス値として持つべき数値は現時点で存在しない（→ 03 §8.2・§11）。
- **市場・素材価格**：市場システム自体が未実装（→ 03 §11）。
- **疲労（Fatigue）**：v1.1で廃止済み。旧xlsxに残っていたシートは削除した。
