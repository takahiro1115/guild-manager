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
| `economy.csv` | 03 §8.1・§5.2 | SubsidyBalance, SatisfactionBalance(賃金), RecruitmentSystem(契約金) |
| `quest.csv` | 03 §4.0 | QuestBalance |
| `quest_templates.csv` | 03 §4.0 | QuestBalance.Templates |
| `combat.csv` | 03 §4.1〜4.3 | QuestResolver（討伐フロー）, PlacementBalance, CombatBalance |
| `quest_type_weights.csv` | 03 §4.2.3 | QuestScoringBalance.GetStatWeights（種別×ステータスの重み） |
| `quest_scoring.csv` | 03 §4.2.3 | QuestScoringBalance（探索・護衛の要求係数・3区分閾値・軽量HP消費） |
| `aging.csv` | 03 §3.0〜3.7 | GrowthBalance, AgingSystem |
| `growth_job_weights.csv` | 03 §3.1〜3.4 | GrowthBalance.JobStatWeights |
| `satisfaction.csv` | 03 §5.1・§5.2 | SatisfactionBalance |
| `facility.csv` | 03 §6・§6.1 | FacilityBalance |
| `guild_rank.csv` | 03 §8.1 | GuildRankBalance.Thresholds |
| `guild_rank_params.csv` | 03 §8.1.1 | GuildRankBalance（名声増減・減衰） |
| `recruitment.csv` | 03 §2.4・§7.3 | RecruitmentSystem, NameGeneratorBalance |
| `compatibility_advisor.csv` | 03 §5.3・§7 | CompatibilityBalance, AdvisorBalance |
| `security.csv` | 03 §4.4・§8.3 | SecurityBalance |
| `training.csv` | 03 §3.1〜3.4・§3.5改 | TrainingBalance |
| `trait.csv` | 03 §4.3・§5.3.2・§4.2.3 | TraitBalance（→ TraitCatalog）、ペアシナジーの隊長LDR緩和係数 |
| `pair_synergy.csv` | 03 §4.2.3 | PairSynergyBalance（→ Systems.PairSynergyCalculator） |
| `quest_events.csv` | 03 §4.2.3 | QuestEventBalance（探索・護衛のランダムイベント3種） |
| `equipment.csv` | 03 §4.2.2 | EquipmentBalance（→ ItemCatalog） |
| `gimmick.csv` | 環境ギミック刷新仕様 | GimmickBalance（→ Systems.GimmickEvaluator） |
| `consumables.csv` | パーティ携行アイテム刷新仕様 | ConsumableBalance（→ ConsumableCatalog） |
| `success_rate.csv` | コアシステム刷新仕様(1) | SuccessRateBalance（→ Systems.SuccessRateCalculator） |
| `progression.csv` | コアシステム刷新仕様(4) | ProgressionBalance（→ Systems.GuildProgressionSystem） |
| `emergency.csv` | コアシステム刷新仕様(3) | EmergencyBalance（→ Systems.EmergencySupportSystem） |

`trait.csv`・`equipment.csv` は項目58（バランス値のCSV外部化）の時点では対応CSVが
存在せず対象外だったため、フォローアップとして追加した（値はTraitCatalog.cs・
ItemCatalog.csに直書きされていた旧値をそのまま書き起こしたもの。挙動は変わらない）。

`quest_type_weights.csv`・`quest_scoring.csv` は項目63（クエスト種別ごとの統一点数
計算式、→ 03 §4.2.3）で新設した。あわせて以下を `combat.csv` から整理している：

- 旧 `WeightSTR`〜`WeightLDR`（個人CP重み）→ `quest_type_weights.csv` の
  `Subjugation` 行へ移行（値は同一）。
- 旧 `AptitudeMinMultiplier`・`AptitudeMaxMultiplier`・`AptitudeReferenceStatValue`
  → クエスト適性ボーナスの廃止に伴い削除。
- **討伐の要求係数は `combat.csv` の `EnemyCpCoefficient`（敵CP係数）をそのまま使う。**
  討伐にとっての要求値は従来どおり「敵CP」であり、同じ値を `quest_scoring.csv` にも
  置くと二重管理になるため、探索・護衛の要求係数のみを新ファイル側に置いている。

`pair_synergy.csv` は項目64（ペア特性シナジー、→ 03 §4.2.3）で新設した表形式ファイル。
列は `trait_a,trait_b,quest_type,value,note`：

- `trait_a`・`trait_b` … 組み合わせる特性のID（→ TraitCatalog の各Id）。
- `quest_type` … 対象クエスト種別（`Subjugation`／`Exploration`／`Escort`）。
  全種別に適用する場合は `All` と書く。
- `value` … 成立1ペアあたりの加算量（符号あり。マイナスは仲違い）。

隊長LDRによる負のシナジー緩和係数（`PairSynergyLdrMitigationCoefficient`）は、
特性まわりの数値をまとめている `trait.csv` 側に置いている
（表形式の `pair_synergy.csv` にはスカラー値を置けないため）。

`quest_events.csv` は項目65（探索・護衛のランダムイベント3種、→ 03 §4.2.3）で新設した。
キーは `{イベント名}_{項目}` の形（`StrongEnemy_` / `TreasureVault_` / `PushingOn_`）で、
各イベントの発生確率・判定の要求係数・特性補正・イベント専用のRatio閾値・追加報酬・
追加HP消費レンジを持つ。討伐（Subjugation）はいずれのイベントの対象にもしていない。

`gimmick.csv`は環境ギミック刷新仕様で新設した表形式ファイル。列は
`tag,stat,threshold_partial,threshold_full,phase,none_multiplier,partial_multiplier,full_multiplier,counter_item,note`：

- `tag` … 環境ギミックの種類（→ Models.EnvironmentTag の各値）。
- `stat` … 対策点数の算出に使う実効値（パーティ全員の合算）。
- `threshold_partial`/`threshold_full` … 合算値がこの値以上で+1点/+2点。
- `phase` … ペナルティ係数の適用先（`Scouting`＝フェーズ1索敵値／`Score`＝フェーズ2点数／
  `Attrition`＝損耗・HP消費%）。
- `none_multiplier`/`partial_multiplier`/`full_multiplier` … 対策達成度（未充足/一部/完全）ごとの
  係数。完全充足は1.0（ペナルティ無効）で統一している。
- `counter_item` … この点数を+1する携行アイテムのId（→ ConsumableCatalog）。

`consumables.csv`はパーティ携行アイテム刷新仕様で新設した。ギミック相殺5種は価格のみ
（効果は+1点で固定・仕様側で規定）、効果アイテム3種（煙幕弾・高品質傷薬・携帯糧食）は
価格に加えて効果量を持つ。

`success_rate.csv`・`progression.csv`・`emergency.csv` はコアシステム刷新仕様
（序盤のグラインド排除）で新設した：

- `success_rate.csv` … 出撃前の成功率予測（→ SuccessRateCalculator）。
  **算出した確率そのものはUIに出さない**（情報公開の原則。→ コミットd7c7f39）。
  `ConfidenceThreshold_*` は定性表現（→ Models.SuccessConfidence）へ丸めるための閾値。
- `progression.csv` … 同時出撃枠の段階開放とランクE昇格試験（→ GuildProgressionSystem）。
  「累計出撃3回・資金100G」という提示条件は、プレイ開始40〜60分で第2部隊枠へ
  到達させることを狙った値。
- `emergency.csv` … 緊急撤退・緊急回復（→ EmergencySupportSystem）。
  緊急回復のコストは、素材・市場システムが未実装のため暫定的にGoldで表現している。

`quest_templates.csv` にはコアシステム刷新仕様で `recommended_members` 列（推奨人数）を
追加し、あわせて低危険度の採取（Gathering）・巡回（Patrol）テンプレートを新設した。

## 凡例

- `key` … コード側の定数名に対応する識別子。**変更しないこと**（読み込み時のキーになる）。
- `value` … 調整対象の数値。ここを書き換えてバランスを調整する。
- `unit` … 単位。
- `note` … 説明・出典セクション・注意事項。

## CSV化していない値（コード側に残すもの）

以下は「バランス調整用の数値」ではなく**構造を定義する値**のため、意図的にCSVへ
出していない。変更すると他の数値の前提が連鎖的に崩れるため、コード変更として扱う。

- 1年=48週（`WeeksPerYear`）、1ヶ月=4週（`SubsidyBalance.WeeksPerMonth`）
- 年齢帯の境界（15/21/27/34/40。`Adventurer.AgeBand`）
- 各種クランプの上下限（満足度0〜100、相性0〜100、脅威度0〜100、実効値の下限0）
- 施設の最大Lv（5）
- 訓練施設ごとの対象ステータス対応（戦士訓練所→STR/VIT 等。職業・施設の定義そのもの）
- クエスト種別ごとの適性対象ステータス（QuestAptitudeBalance.GetAptitudeStats）
- 衰微対象ステータス（STR/AGI/VIT。§3.0の「フィジカル衰微」の定義そのもの）

## 未実装のため記載していない項目

- **勝利条件（最終討伐クエスト＝魔王戦）**：詳細未設計のため数値なし。
  中間目標（Aランク到達で`FinalQuestUnlocked`が立つ）までは実装済みだが、
  ここにバランス値として持つべき数値は現時点で存在しない（→ 03 §8.2・§11）。
- **市場・素材価格**：市場システム自体が未実装（→ 03 §11）。
- **疲労（Fatigue）**：v1.1で廃止済み。旧xlsxに残っていたシートは削除した。
