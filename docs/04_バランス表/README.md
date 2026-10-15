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
| `economy.csv` | 03 §8.1・§5.2・§8.3 | EconomyBalance（初期資金・週給/契約金/退職金係数・退職金不足の機嫌低下・内職売上の基本額と間隔・破産判定週数・まとめての予算〈§0.99〉）, SatisfactionBalance(賃金), RecruitmentSystem(契約金) |
| `combat.csv` | 03 §2.3・§4.3・§0.125 | CombatBalance：最大HP（`MaxHpBase`・`MaxHpVitCoefficient`）・**負傷**（重傷の全治週数 `SevereInjuryWeeksMin/Max`・重傷になるHPの割合 `SevereInjuryHpThresholdPct`・軽傷の全治週数 `LightInjuryWeeksMin/Max`・軽傷の能力値低下 `LightInjuryStatPenaltyRate`、→ 03 §0.53）・耐毒体質 `ResistPoisonDamageReductionRate`・巨獣狩り `GiantHunterDamageBonusRate`・古傷 `OldWoundCriticalChance`・`OldWoundRetreatHpThresholdPct`・巨獣狩りの後天開眼 `GiantHunterAwakeningChance`・**HP0の強制除籍の確率** `FatalChancePercent`（§0.125。外れれば HP1 で一命を取り留めて重傷）。旧クエストの戦闘処理の値（敵CP・索敵・奇襲/不意打ち・戦闘比率・HP消費率・致死回避閾値・不可逆帯幅）は読み込むだけで未使用だったため §0.53 で削除 |
| `aging.csv` | 03 §3.0〜3.7 | GrowthBalance（年齢帯別成長ロール基礎確率＝新鋭/成長/全盛の3区分・難易度係数・成長量幅）, AgingSystem（満期引退年齢・稼働週数） |
| `growth_job_weights.csv` | 03 §3.1〜3.4 | GrowthBalance.JobStatWeights |
| `satisfaction.csv` | 03 §5.1・§5.2 | SatisfactionBalance |
| `facility.csv` | 03 §6・§6.1 | FacilityBalance |
| `tournaments.csv` | 03 §0.82 | TournamentBalance（大会の定義。**テーブル形式** `Id,Name,Kind,Discipline,Grade,Month,Week,MinStrength,MaxStrength,Prize1,Prize2,Prize4,Mood1,Mood2,Mood4,Note`。`{地方}`・`{フィールド}` は名前の差し込み、Month・Week が0の大会は暦を作るときに決める） |
| `tournament.csv` | 03 §0.82 | TournamentBalance（大会の式・暦・施設の開放の閾値・おすすめの申し込み〈§0.99〉。key,value 形式） |
| `isabella.csv` | 03 §0.84 | IsabellaBalance（イザベラの来訪・交流戦・派遣の教官。key,value 形式） |
| `funding.csv` | 03 §0.104 | FundingBalance（資金の調達：商人ギルドとイザベラの融資、スポンサー契約の前金・ノルマ・成功報酬・違約金。key,value 形式） |
| `policy.csv` | 03 §0.105 | PolicyBalance（ギルドの方針〈訓練の強さ・大会の賞金の分け方〉、引退者が去るまでの週数、個人特訓・スカウト網の予算。key,value 形式） |
| `postgame.csv` | 03 §0.90 | PostGameBalance（エンディングのあと：主が蘇るまでの週数・異変の季節・ルミナの能力と立ち絵。key,value 形式） |
| `story/story_01.txt`〜`story_04.txt` | 03 §0.86・§0.89 | StoryBalance（物語の台詞。物語帳〈アーティファクト「ギルド物語帳」〉の「台詞 01〜04」を書き出したもの。`s04_narration` はエンディングの窓の締めの語り。**CSVではない**：`## 場面のId　見出し` で場面が始まり、台詞は物語帳と同じ書式。書式はファイルの先頭の注記。場面を出す条件はコード `StorySystem`。物語帳を直したら書き出し直す。§0.91 からどの場面も頭に `〔背景：場面名〕`） |
| `story_images.csv` | 03 §0.91 | StoryImageBalance（物語の会話の小窓に出す背景と立ち絵。**テーブル形式** `Kind,Name,Expression,File`。Kind＝`background`・`portrait`、Name＝背景の名前〈台詞の〔背景：…〕〉か話者名〈`\|` で別名〉、Expression＝立ち絵の表情〈空は「通常」〉、File＝`assets/story/` からの相対パス。画像がまだ無い行も置いてよく、ゲームはファイルがあるときだけ出す） |
| `facility_unlock_lines.csv` | 03 §0.82・§0.84 | TournamentBalance.UnlockLines（施設が開いたときの台詞。**テーブル形式** `Style,Facility,Text`。Style＝Albert／Isabella／Royal、Facility＝Training・FirstTraining・各施設名・Any、`{facility}`・`{level}`・`{discipline}`・`{event}` を差し込む） |
| `titles.csv` | 03 §0.83 | HonorBalance.Titles（二つ名15種。**テーブル形式** `Id,Name,Rank,Param,Note`。Rank＝Legend／Name／Honor／Plain、Param＝条件の回数・年数・週数、Name の `{G1}` は大会の名前で埋める。条件の判定はコード `HonorSystem.GetTitles`） |
| `honor.csv` | 03 §0.83 | HonorBalance（殿堂の条件と顧問の効果の倍率・関係タグの閾値・引退式の行数。key,value 形式） |
| `retirement_lines.csv` | 03 §0.83 | HonorBalance.RetirementLines（引退式のアルベールの言葉。**テーブル形式** `Key,Text`。Key＝HallOfFame／Legend／Name／Honor／Plain／Short、`{name}`・`{title}` を差し込む） |
| `rulers.csv` | 03 §0.127 | RulerBalance（大迷宮の支配者と支配者戦。→ `docs/検討中_大迷宮の支配者.md` §3）：体力 `MaxHp`（100）・毎月の回復 `RegenPerMonth`（10）・前哨戦で退く体力 `OutpostRetreatHp`（50）・介入の回数 `InterventionsBase`（1）。ヴェスペラの一騎打ち `Vespera*`：代表の上限（5）・試合の上限（5）・強さ（前哨戦130／決着170。魔は大会の部門の倍率0.75を掛ける）・疲れ（試合ごと5%）・魅了の閾値（MND 60／80、相棒のいない子は+10、85%以上は揺らぐ×0.7）・削る体力（勝ち 20×強さの比〈10〜30〉、負け3）・HPの減り（勝ち10〜20%、負け30〜60%）・解析で見える手の内（30%・60%・100%）。介入「名を呼ぶ」`CallName*`（届く見込み＝40%＋相性÷4＋満足度÷4、上限90%）。見返り（§0.128）は他の表に置く：茨の欠片 `mat_ruler_thorn`（materials.csv。試合に勝つたびに1つ、売値150G、採取・鑑定では出ないよう MinFloor 999）、研究「多頭通信術式の強化」I・II `res_link_amulet_1/2`（research.csv。効果 `InterventionBonus`＝介入+1ずつ）、ヴェスペラの双剣 `LegendVesperaBlades`（uniques.csv。森70F、魅了を対策済み）。一対一の稽古場（森の100Fを倒すと個人特訓が同時に2人まで）は値を持たない。**メルジーネの調べの読み合い**（§0.129）`Melusine*`：出す部隊の上限（3）・閾値（前哨戦60／決着80）・呑まれる線（合い具合0.85）・甘い調べで魅了される線（INT が閾値の0.85倍未満）・削る体力（20×合い具合〈10〜25〉）・HPの減り（打ち破り10〜20%、呑まれ30〜50%）・解析で分かる曲（30%で1曲・60%で2曲・100%で全曲）。見返りは materials.csv の水妖の真珠 `mat_ruler_pearl`（売値150G）・uniques.csv の歌姫の耳飾り `LegendSirenEarring`（洞窟70F、再生を対策済み）・training.csv の瞑想 `MeditationGrowthMultiplier`（1.0）。**オルテンシアの玉座への回廊**（§0.130）`Ortensia*`：出す部隊の上限（3）・閾値（前哨戦60／決着80）・骸骨兵の間と玉座の人数（4）・削れる線（合い具合0.85）・幻惑の間で惑う線（0.85）・封印の強さ（100）と削る量（60×合い具合）・張り直し（3か月ごと）・援軍の守りの線（VIT 0.85）・威圧の線（LDR 0.85）と G1 の優勝1回で−1（最大−15）・玉座で削る体力（25×合い具合〈10〜35〉）・HPの減り（削れば10〜20%、押し返されれば30〜50%）・解析で張り直しが見える（50%）。見返りは materials.csv の古王の封蝋 `mat_ruler_seal`（売値150G）・uniques.csv の女王の聖骸布 `LegendQueenShroud`（廃墟70F、群れを対策済み）・古王の寝所の名のある家の娘のPA `NobleDaughterPaBonus`（+5）。**イグニアの灼熱の底**（§0.131）`Ignia*`：出す部隊の上限（3）・毎月の回復（20）・閾値（前哨戦65／決着85）・眷属と本体の人数（4）・削れる線（0.85）・削る体力（12×合い具合〈8〜20〉）・灼熱（15%×閾値÷VIT〈5〜25%〉、押し返されたら+10%）・戦線を離れる線（HP 3割）・崩れる線（2人）・押し切れ（合い具合×1.3・灼熱×1.5・強制除籍×2）・イグニアの炉（焔の鱗3枚・500G）。見返りは materials.csv の焔の鱗 `mat_ruler_scale`（売値150G）・uniques.csv の灼鱗の鎧 `LegendScorchMail`（峡谷70F、対策なし）。**ネフェリスの心臓の間**（§0.132）`Nepheris*`：出す部隊の上限（3）・閾値（前哨戦75／決着95）・削れる線（0.85）・倒していない支配者の仕掛けで外れる線（閾値の0.85倍）・削る体力（15×合い具合〈8〜25〉）・解析で調べが分かる（60%。メルジーネを倒すまでは分からない）。灼熱と押し切れはイグニアの値を使う。見返りは materials.csv の心臓のかけら `mat_ruler_shard`（売値150G）・uniques.csv のひび割れた双子冠 `LegendTwinCrown`（深淵70F、全能力+3・HP+15） |
| `report_lines.csv` | 03 §0.116・§0.124 | SquadReportBalance（部隊長の報告の台詞の型。**テーブル形式** `Kind,Speaker,Text`。Kind＝Victory／Defeat／Injury／Retreat／Growth／Survey／Gather／Dive／Idle、Speaker＝`temper:cautious`・`temper:normal`・`temper:bold`（気質）／`drive:志向名`（主志向）／`char:claudia`・`char:rina`・`char:fiona`（物語帳の人物）。`{主}`・`{階}`・`{フィールド}`・`{名前}`・`{成果}`・`{解析}`・`{部隊長}` を差し込む。Text は「〔地の文〕｜台詞｜台詞」の形で、`｜` で区切った段を1コマずつ出す〈§0.124〉。備考に ASCII のカンマを含めるときと同じく、Text は `"…"` で囲む） |
| `master_mood.csv` | 03 §8.1・§8.1.1 | MasterMoodBalance（マスターの機嫌の初期値・成果ごとの増減・退屈減衰・強制除籍への激怒・段階閾値・内職売上倍率・研究の手伝い〈旧・待機お手伝い〉の額／機嫌） |
| `recruitment.csv` | 03 §2.4・§7.3 | RecruitmentSystem, NameGeneratorBalance（第1週の新春ドラフト：`DraftCandidateCount`・`DraftHireCount`。旧 `TutorialCandidateCount` は削除）。先天特性の確率 `InnateTraitChancePercent` は§0.55で5→3（候補19種、1人あたり平均約0.57個）。生まれつきの欠点の確率 `InnateFlawChancePercent`（2、候補9種、平均約0.18個）は§0.56で新設。レア特性の確率 `RareTraitChancePercent`（1）と重み `RareTraitWeight_Genius`（1）・`RareTraitWeight_SingleStat`（2）は§0.57で新設 |
| `compatibility_advisor.csv` | 03 §5.3・§7 | CompatibilityBalance, AdvisorBalance（教官成長補正、参謀の大迷宮調査解析ボーナス・道中潜行走破力ボーナス、スカウト有望新人率） |
| `training.csv` | 03 §3.1〜3.4・§3.5改・§7.1 | TrainingBalance（勤勉 `DiligentGrowthRateBonus`、→ 03 §0.32。教官からの特性伝授 `TraitInheritanceBaseChance`・師匠肌ボーナス `MentorTraitInheritanceBonus`、→ 03 §0.34。教官の在任ボーナス `TrainerTenureBonusStepWeeks`・`TrainerTenureBonusPerStep`・`TrainerTenureBonusMax`、→ 03 §0.54。待機中の過ごし方 `IdleActivityHpRatio`・`SelfTrainingGrowthMultiplier`・`SelfTrainingHpCost`・研究の手伝い `ResearchCreditMax`・`ResearchCreditPerWeek`・`ResearchMaxShortenRate`、→ 03 §0.73・§0.114） |
| `trait.csv` | 03 §4.3・§5.3.2・§4.2.3 | TraitBalance（→ TraitCatalog。豪胆 `BraveSurvivalThresholdBonus`＝ボス戦の損耗−pt・注意深い `AttentiveScoutingBonus`＝隠密の寄与×(1＋値)・知識人 `ScholarAnalysisBonus`＝解析の寄与×(1＋値)、→ 03 §0.53。ペアシナジーの緩和係数は§0.53で削除）。全特性の表示名・説明・障害フラグ（`{Id}_DisplayName`・`{Id}_Description`・`{Id}_IsCurseOrInjury`、§0.31）。旧 `TraitTransmission*` 3キーは §0.34 で削除（→ training.csv）。§0.55で足した10種の効果量（`HawkEyeFlyingDamageReductionRate`・`SixthSenseInstantKillHpLossPct`・`GuardianGuardBonus`・`PathfinderTraversalBonus`・`SturdyMaxHpBonus`・`QuickHealerInjuryRecoveryBonus`・`QuickHealerRestRecoveryBonus`・`CheerfulSatisfactionBonus`・`HardworkerIdleHelpCreditMultiplier`（§0.73で `HardworkerIdleHelpGoldMultiplier` から改名）・`FireMageIntBonus`・`SwordMasterStatBonus`、すべて仮の値、→ 03 §5.3.2）。§0.56のマイナスの特性（地図読み `MapReaderTraversalBonus`、欠点の `Coward*`・`Reckless*`・`ClumsyStealthPenalty`・`PoorDirectionTraversalPenalty`・`SicklyMaxHpPenalty`・`FickleGrowthRatePenalty`・`MoodySatisfactionPenalty`・`SpendthriftWageMultiplier`、障害の `PoisonAftereffect*`・`LegWoundAgiReduction`・`ArmWoundStatReduction`・`Dread*`・`Burnout*`。すべて仮の値）。§0.57のレア特性（`GeniusStatBonus`＝0.15・`RareSingleStatBonus`＝0.30、仮の値） |
| `equipment.csv` | 03 §4.2.2 | EquipmentBalance（→ ItemCatalog）。**テーブル形式**（`Id,Price,HpBonus,BonusStr〜BonusLdr,note`、2026年9月に key,value 形式から移行）。`HpBonus`＝最大HP加算（武器は0）。§0.37で旧 `EffectValue`（個人CP／最大HPの二義）を個人CPの撤廃に伴い `HpBonus` へ改名 |
| `boss_gimmicks.csv` | 03 §4.5.4 | BossGimmickBalance（ボスのギミック7種への備えと罰、毒状態、ギミックの数。2026年10月・§0.68。旧 `consumables.csv` は撤去） |
| `boss_power_weights.csv` | 03 §4.5.4・§2.1 | DungeonBalance.GetBossPowerWeights（職業ごとの討伐火力の重み、2026年10月・§0.72。どの職業も合計4.2。dungeon.csv の `BossPowerWeight_*` は職業を決めない試算用の共通の重み）。ほかに §0.72 で combat.csv `ClericBlessingLossPctPerMnd`（0.08）・`ClericBlessingSevereThresholdRate`（0.8）＝神官の加護、recruitment.csv `JobAptitudePaBonus`（10）・`JobInaptitudePaPenalty`（10）＝採用の能力を職業に寄せる、dungeon.csv `GrowthJobWeightFloor`（1）＝出撃成長の能力の選び方を足した |
| `potential_estimate.csv` | 03 §0.74・§2.4・§5.1 | PotentialEstimateBalance（副官の見立て〈PAを隠して段階S〜Dで見せる〉の幅・目利き〈スカウトの顧問・段階研究・在籍の学者〉・在籍の月日で狭まる割合・魂魄融和の娘の倍率・確かとする幅・段階の境目、2026年10月・§0.74） |
| `personality.csv` | 03 §0.100〜§0.102 | PersonalityBalance（志向・気質・相性の素地の決め方、志向と気質による満足度の増減、見立ての回数、採用の一言。満足度と相性の段階・志向の力と成長・さぼり・かばい合い・素地の帯〈§0.101〉。出来事・引き抜き・事故・⚠の表示〈§0.102〉。key,value 形式） |
| `progression.csv` | 03 §4.5.1・§4.2.2 | ProgressionBalance（初期の同時出撃枠、3つ目の部隊に要る現役の人数 `Slot3MinActive`＝§0.120、店の上位装備が入荷する倒したボスの数 `ShopTierUnlockBosses_1〜3`＝§0.61） |
| `dungeon.csv` | 03 §4.5.1・§4.5.4 | DungeonBalance（ボス能力重み・未踏破重損耗・ボス間隔・撃破実績点・出撃成長回数） |
| `dungeon_traversal.csv` | 03 §4.5.3 | DungeonTraversalBalance（**走破力の重み（VIT/MND/隊長LDR）**・**1歩の消費の尺度 `FloorsPerRatio`**（比率1.0の深さで週に進める階層数、4.0。進軍ランクの段 `RankRatioScale` は別。1歩の消費＝その階層の要求値÷(走破力×この値)÷解析倍率、週の予算1、→ 03 §0.49。旧リニア進軍は §0.25）・進軍ランク別の既踏損耗率・調査度連動走破倍率・夜目 `NightVisionUnexploredDamageReductionRate`（→ 03 §0.32）） |
| `scouting.csv` | 03 §4.5.3 | ScoutingBalance（**隠密適性の重み・専門職ボーナス・人数倍率・重装ペナルティ**、迷宮調査の4段階護衛判定・解析成果倍率・護衛HP損耗） |
| `gathering.csv` | 03 §4.5.5・§0.66 | GatheringBalance（採取スコア係数・職業ボーナス・報酬ゴールド、護衛の要求倍率と段階ごとの素材・損耗の倍率） |
| `materials.csv` | 03 §4.5.5・**§4.8.1** | MaterialBalance（全5フィールドの採取素材定義・MinFloor・基準獲得数・**売却額 `SellPrice`**） |
| `affixes.csv` | 03 §4.7.3・§4.2.2 | AffixBalance（鑑定品のランダムアフィックス＝接頭辞・接尾辞の一覧。**テーブル形式** `Id,Type,Name,TargetStat,MinValue,MaxValue,Tier,AllowedSlots,Weight`、2026年9月・§0.39） |
| `portraits.csv` | 03 §2.1 | PortraitBalance（採用の応募者に割り当てる顔グラフィックの一覧。**テーブル形式** `Id,Jobs,HairColor,EyeColor`。Id＝`assets/portraits/{Id}.png`、Jobs＝似合う職業の `\|` 区切りまたは `All`。2026年9月・§0.46） |
| `uniques.csv` | 03 §4.7.5 | UniqueBalance（固有武具＝固定アーティファクト〈紫〉・伝説級〈金〉・依頼人の固有武具〈§0.64〉の一覧。**テーブル形式** `Id,Grade,Name,BaseItemId,HpBonus,BonusStr〜BonusLdr,CounterGimmick,DropFieldId,DropFloor,SellPrice,PatronId`。伝説級は入手元のボス〈フィールドId・階層〉と対策ギミック、売値0＝売却不可。2026年9月・§0.45。§0.64で Grade `Patron` と列 `PatronId`〈commission_clients.csv の Id。Patron 以外は空〉を追加。依頼人の固有武具は売値0＝売却不可） |
| `research.csv` | 03 §4.6 | ResearchBalance（アルベール研究室の24プロジェクト（うち内職強化 SideBusinessGoldBonus 4種、魂魄融和の秘薬の解禁 `res_soul_fusion`＝SoulFusionUnlock・効果値0、§0.58、段階研究11種＝RecruitPaBonus・GrowthRateBonus・SoulFusionPaBonus・CultureTankBonus、§0.60）・必要素材・ゴールド・効果種別・効果値・**前提 `Prerequisite`**（§0.60で列を追加。空＝前提なし、段階研究のII・IIIは前の段のId）・**系統 `Branch`・段 `Lane`**（§0.77で列を追加。研究室のツリーの行。列は必要な素材のフィールドから決まる）・**週 `Weeks`**（§0.114で列を追加。研究にかかる週）） |
| `squad_orders.csv` | 03 §4.0.3 | SquadOrderBalance（部隊の方針と自動出撃、2026年10月・§0.63）：自動出撃の条件 `AutoDispatchMinHpPercent`（70）・扉前の構え（慎重・標準・強気、§0.68）ごとの火力の倍率・HP・備えの条件 |
| `squad_report.csv` | 03 §0.116 | SquadReportBalance（部隊長の報告：成長を報告する伸びの下限 `GrowthMinGain`（3）・慎重／大胆な口調の気質の境目 `TemperamentCautiousMax`（−1）・`TemperamentBoldMin`（1）） |
| `commissions.csv` | 03 §4.9・§4.10 | CommissionBalance（依頼と迷宮の異変、2026年10月・§0.64）：依頼の届き方（届き始めは初めての入賞の次の季節・§0.84、`OffersPerSeason`＝3・`MaxAccepted`＝2・期限 `DeadlineWeeks_*`＝撃破24／他12・期限の知らせ `DeadlineWarningWeeks`＝2）、条件（納品の個数 `DeliverCountBase`・`DeliverFloorsPerExtra`、派遣の基準 `LoanRank`＝3番目・`LoanMinThreshold`＝20、派遣の期間と帰還の成長 `LoanWeeks`＝12・`LoanGrowthRolls`＝12・`LoanTraitChance`＝0.25〈§0.85〉）、報酬（`RewardMultiplier_*`＝撃破0.2・完全解析0.15・納品0.15・派遣1.5、`CompletionMoodGain`＝10、`FailureMoodLoss`＝10、遺物 `RewardRelicBandBonus`＝1（1つ深い帯で引く、§0.126）・`RewardRelicMinRarity`＝Epic、依頼人の固有武具 `PatronUniqueCompletions`＝5件）、異変（`FirstAnomalyWeek`＝13・`AnomalyAnnounceWeekOfSeason`＝4・`AnomalyDurationWeeks`＝4・倍率 `Anomaly_*`） |
| `commission_clients.csv` | 03 §4.9・§0.85 | CommissionBalance.Clients（依頼人5人。**テーブル形式** `Id,Name,Types,BonusMaterialId,BonusMaterialCount,AlbertLine,LoanStats,LoanTraits`。Types は `Defeat\|Survey\|Deliver\|Loan` の `\|` 区切り、BonusMaterial はおまけの素材〈空＝なし〉、AlbertLine は掲示中の依頼に添えるアルベールの一言、LoanStats・LoanTraits は派遣から帰ってきたときに伸ばす能力と付く特性の候補〈§0.85、`\|` 区切り。派遣を頼む依頼人は LoanStats が必須〉） |
| `commission_texts.csv` | 03 §4.9 | CommissionBalance（依頼文の文例。**テーブル形式** `ClientId,Type,Text`。{field}{boss}{floor}{material}{count}{stat}{value}{weeks} を差し込む。依頼人が扱う種類ごとに1本以上ないと起動失敗） |
| `dungeon_anomalies.csv` | 03 §4.10 | CommissionBalance（迷宮の異変5種の名前と予告文。**テーブル形式** `Type,Name,Text`。{field}{boss}{floor}{weeks} を差し込む。倍率は commissions.csv） |
| `elixir.csv` | 03 §4.6.2 | ElixirBalance（霊薬、2026年10月・§0.61）：1人が生涯に飲める数 `MaxPerAdventurer`（3） |
| `elixir_recipes.csv` | 03 §4.6.2 | ElixirBalance.Recipes（霊薬の一覧。**テーブル形式** `Id,Name,Description,TargetStats,PaBonus,StatBonus,RequiredMaterials,RequiredGold`。TargetStats は能力名の `;` 区切り、RequiredMaterials は `素材Id:個数` の `;` 区切り。§0.61） |
| `soul_fusion.csv` | 03 §5.4 | SoulFusionBalance（魂魄融和の秘薬、2026年10月・§0.58）：娘のPAの基準で高い方の親に掛ける重み `HigherParentWeight`（0.75、§0.60で新設。0.5＝平均）・処方できる相性 `RequiredCompatibility`（100）・費用 `PrescriptionGold`（2500G）・培養 `CultureWeeks`（12週）・培養槽の数 `CultureTankCount`（1）・娘のPAのばらつき `PaVarianceMin/Max`（−5〜+10）・百合相性ごとの能力限界突破の確率 `BreakthroughChance_Destined/Complementary/Ordinary`（30/15/5%）・突破の上乗せ `BreakthroughBonusMin/Max`（5〜15）と上限 `BreakthroughPaCap`（120）・特性の継承確率 `TraitInheritChancePercent`（30）・`RareTraitInheritChancePercent`（15）・`FlawInheritChancePercent`（20）。すべて仮の値。**§0.133（秘薬の世代）**：`ParentOnlyOnce`（0＝何度でも・誰とでも親になれる）・`RequiredCompatibility` 80・`CultureWeeks` 48・`PaVarianceMin` −2・血の蓄え `GenerationPaCapStep`（世代ごとに素質の上限+5）と `GenerationPaCapMaxGen`（4世代まで）・近い血 `InbreedPenaltyClose`（親子・同じ両親の姉妹 −10）と `InbreedPenaltyNear`（片親が同じ姉妹・祖母と孫・おばと姪・いとこ −5） |
| `shop.csv` | 03 §0.134 | ShopBalance（ギルドの店：引退した子が働く道具屋・酒場）：道具屋の枠 `ItemShopSlots`（2）・酒場の枠 `TavernSlotsPerLevel`（酒場Lv×1）・毎月の売上の基本 `ShopBaseSales`（200G）・得意な能力の線 `ShopStatLine`（50。道具屋＝DEX・INT、酒場＝LDR・MND）と1ptあたり `ShopSalesPerStat`（5G）・道具屋の在庫 `ItemShopStock*`（1つ0.5G、上限150G）・酒場の名声 `TavernFame*`（誉100・名200・伝説300）と殿堂入り `TavernHallOfFameBonus`（100）。満足度の契約交渉の直し（§0.134）は satisfaction.csv の `NegotiationSatisfactionGain`（+20）・`NegotiationCooldownWeeks`（12週） |
| `soul_fusion_catalysts.csv` | 03 §5.4 | SoulFusionBalance.Catalysts（秘薬の触媒。**テーブル形式** `MaterialId,Count,EffectType,EffectValue,Note`。EffectType は `PaVarianceMinBonus`・`TraitInheritBonus`・`FlawInheritBonus`・`BreakthroughChanceBonus`・`RareTraitInheritBonus`。各フィールドの素材を1種ずつ、§0.58） |
| `relic.csv` | 03 §4.7・§4.8.2・§0.126 | RelicBalance（未鑑定遺物の希少度4段階＝鑑定費用・鑑定結果比率・換金額と獲得個数の幅・武具の抽選プール・**売却額**、採取ドロップ確率、深さの段の帯ごとの希少度の割合〈§0.126〉、ボス撃破ドロップの補正と2つ目）。**アフィックスの付与率・Tier範囲**（`Affix*`、→ AffixBalance） |

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
（慎重 1.2・80・1.0・1.0／標準 1.0・60・0・0.5／強気 1.0・40・0・0）。
強気の火力の倍率は03 §0.96で0.9→1.0にした（決戦は火力が要求以上でないと勝てないので、1.0未満は必ず負ける戦いに挑んでいた）。
03 §0.98で決戦に勝率を入れ、`{構え}PowerMargin` を `{構え}MinWinChance`（慎重 0.9／標準 0.6／強気 0.3）に置き換えた（→ 下の「決戦の勝率」）。

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

## 方針と進路で追加されたファイルとキー（2026年10月、→ 03 §0.105）

`policy.csv`（新設、key,value 形式。すべて仮の値）。霊薬（`elixir_recipes.csv` `RequiredGold`）・秘薬（`soul_fusion.csv` `PrescriptionGold`）の値段は据え置き。

| キー | 値 | 意味 |
|---|---|---|
| `TrainingHardGrowthMultiplier`・`TrainingHardInjuryChance`・`TrainingHardSatisfaction` | 1.15・0.03・−1 | 訓練「厳しく」：成長の倍率・訓練生の毎週の軽傷の確率・満足度（志向が訓練の子は0） |
| `TrainingGentleGrowthMultiplier`・`TrainingGentleSatisfaction` | 0.9・1 | 訓練「穏やかに」 |
| `PrizeShareRate`・`PrizeShareSatisfaction`・`PrizeShareDriveBonus` | 0.5・5・3 | 大会の賞金「本人に分ける」：本人へ渡す割合・満足度・志向が大会／お金の子に足す満足度 |
| `RetireeRoleGraceWeeks` | 4 | 引退者（と役職を外れた子）がこの週数のうちに任命されなければ、ギルドを去る |
| `CoachingCost`・`CoachingWeeks`・`CoachingSlots` | 2500・12・1 | 個人特訓の費用・期間・同時の人数 |
| `CoachingRollsPerWeek`・`CoachingGrowthMultiplier` | 2・1.3 | 個人特訓の毎週の成長の判定の回数・確率の倍率 |
| `ScoutBudgetCost`・`ScoutBudgetExtraCandidates`・`ScoutBudgetHighPotentialBonus` | 2000・2・0.15 | スカウト網の予算：費用・志願者に足す人数・有望新人の確率に足す値 |

シミュレーター（L3 simple）で、個人特訓とスカウト網の予算を使うとクリアが約1年早まる（投資なし 12年目→11年目）。方針の「厳しく」は11年目だが破産が増え、「穏やかに」は12年目で破産が少ない。

## 資金の調達で追加されたファイルとキー（2026年10月、→ 03 §0.104）

`funding.csv`（新設、key,value 形式。すべて仮の値）。依頼と派遣の報酬の倍率（`commissions.csv` `RewardMultiplier_*`）は据え置き。

| キー | 値 | 意味 |
|---|---|---|
| `MerchantLoanAmounts` | 3000;6000;10000 | 商人ギルドから借りられる額（1口まで） |
| `MerchantLoanFee`・`MerchantLoanMonths` | 0.25・12 | 手数料と返す回数（月の最後の週） |
| `MerchantLoanMoodLoss` | 1 | 返し終わるまで、マスターの機嫌が毎週下がる |
| `IsabellaLoanBase`・`IsabellaLoanPerExchangeWin`・`IsabellaLoanMax`・`IsabellaLoanStep` | 5000・1000・10000・1000 | イザベラの融資の上限（交流戦の勝ちで上がる）と刻み |
| `IsabellaLoanFee`・`IsabellaLoanMonths`・`IsabellaLoanPerYear` | 0.05・24・1 | 手数料・返す回数・1年に借りられる回数 |
| `SponsorFirstWeek`・`SponsorOffers`・`SponsorWeeks` | 25・2・24 | スポンサー契約が届き始める週（1年目の秋）・1回の話の数・期間 |
| `SponsorAdvanceBaseCap`・`SponsorAdvance_Modest`・`SponsorAdvance_Bold` | 8000・0.6・1.5 | 前金＝最前線のボスの撃破報酬（頭打ち）×倍率 |
| `SponsorBonus`・`SponsorPenalty` | 0.3・1.5 | 成功報酬・違約金（前金×倍率） |
| `SponsorSuccessMood`・`SponsorFailureMood` | 10・−12 | ノルマに届いた／届かなかったときの機嫌 |
| `SponsorTarget_Bosses_*`・`SponsorTarget_Placings_*`・`SponsorTarget_Commissions_*` | 2/4・2/5・2/3 | ノルマの数（控えめ／強気） |
| `SponsorNames` | 王都の商会;辺境伯家;魔導学院;鍛冶師組合;薬師組合 | スポンサーの名前 |

シミュレーター（L3 simple）で控えめな話の達成は約8割。1回目の値（ノルマ1・前金×1.0・成功報酬×0.5）では達成が98%で、ただの収入になったので重くした。前金の基準の頭打ちは、終盤に前金と違約金が膨らみすぎないため（頭打ちなしでは終盤に違約金が2万Gを超えた）。

## お金の流れで追加／変更されたキー（2026年10月、→ 03 §0.103）

すべて仮の値。L3 simple のシミュレーターで維持費は年に約13,000G。

| ファイル | キー | 値 | 意味 |
|---|---|---|---|
| `facility.csv` | `UpgradeCost_Lv1`〜`Lv5` | 500・800・2000・5000・10000 | 累進の改築費（旧 `UpgradeCostPerLevel` 500＝今のLv×500 を撤去） |
| `facility.csv` | `Upkeep_Lv1`〜`Lv5` | 40・100・220・400・700 | 施設1つの月々の維持費（月の最後の週。8施設すべてLv5で月5,600G） |
| `economy.csv` | `StipendBase`・`StipendPerStat`・`StipendMax` | 15・0.5・40 | 教官・顧問の手当（週）＝15＋(引退したときの能力の平均−40)×0.5、上限40 |
| `economy.csv` | `FeastCostPerHead`・`FeastSatisfaction`・`FeastSatisfactionPerTavernLevel` | 50・8・1 | 酒場の慰労会（季節に1回） |
| `economy.csv` | `ContractMeritRaise`・`ContractMeritTopSatisfaction`・`ContractMeritOthersSatisfaction` | 0.12・10・−5 | 契約更改「成果に応じて」（上位3分の1） |
| `economy.csv` | `ContractEvenRaise`・`ContractEvenSatisfaction` | 0.05・3 | 同「全員を少しずつ」 |
| `economy.csv` | `ContractFreezeSatisfaction`・`ContractGapCompatLoss` | −5・3 | 同「据え置く」／「成果に応じて」で差がついた2人の相性 |
| `satisfaction.csv` | `TavernRecoveryCeiling` | 60 | 酒場の自然回復はこの満足度未満の子だけ |
| `satisfaction.csv` | `ContentDriftAbove`・`ContentDrift` | 70・−1 | 慣れ：この満足度を超える子は毎週下がる |
| `master_mood.csv` | `CheerfulBoredomMoodLoss` | 2 | 機嫌の張り合い：上機嫌で新しい成果の無い週に下がる |

## 出来事と事故で追加されたキー（2026年10月、→ 03 §0.102）

`personality.csv` に足した（すべて仮の値）。

| キー | 値 | 意味 |
|---|---|---|
| `EventChance_*` | 4・1・6・12 | 出来事の確率（%、週ごと・子ごと）：上機嫌（プラス）・ふつう（半々）・不満・限界（マイナス） |
| `EventWeightBase`・`EventWeightPull` | 2・3 | 出来事の選ばれやすさ（その子の志向・気質・特性に合う出来事に足す） |
| `QuarrelCompatLoss`・`QuarrelSatisfaction` | 5・−3 | 口論：相性（素地の帯の下がり方を掛ける）・2人の満足度 |
| `GloomWeeksMin`・`GloomWeeksMax` | 1・3 | ふさぎ込みの週数（医務室Lv3以上で1週短い） |
| `WasteWageWeeks` | 4 | 無駄づかい：週給×この週数のお金が減る |
| `FretBossHpLossPct` | 10 | 焦り：次の決戦のHPの減り（%ポイント） |
| `MentorCompatGain`・`MentorSatisfaction`・`TreatSatisfaction`・`PumpedPowerBonus` | 3・3・2・0.05 | 後輩の面倒・差し入れ・張り切り |
| `BondChancePercent`・`BondCompatGain`・`BondSatisfaction` | 3・3・5 | 休日の絆（相棒の2人） |
| `BrawlChancePercent`・`BrawlCompatLoss` | 4・5 | 宿舎の衝突（険悪の2人、2人とも軽傷1週） |
| `PoachDeadlineWeeks`・`PoachRaiseSatisfaction`・`PoachBonusSatisfaction` | 2・20・10 | 引き抜きの話：答えの期限・引き留めの満足度 |
| `BoycottChance_*`・`BoycottDriveModifier` | 5・15・5 | ボイコット（不満・限界）と、苦手な仕事（＋）・主志向の仕事と相棒（−） |
| `RefuseChanceHostile`・`FeudThreshold` | 10・10 | 険悪な相手と出るのを拒む確率・犬猿の相性（同じ部隊に入れられない） |
| `DisobeyChance_*`・`DisobeyVeryModifier`・`DisobeyLeaderLdrDivisor` | 8・15・5・160 | 命令無視の確率（×(1−リーダーのLDR÷160)、半分より下がらない） |
| `DisobeyFleeSatisfaction`・`DisobeyFleeCompatLoss` | −5・3 | 逃げた子の満足度・仲間との相性 |
| `ShowAccidentWarnings` | 1 | 危険の表示（⚠）を出すか（1＝出す、0＝隠す） |

## 満足度と相性の段階で追加されたキー（2026年10月、→ 03 §0.101）

`personality.csv` に足した（すべて仮の値）。満足度は今も上機嫌に張り付きやすいので、段4c・4d のあとで合わせ直す。

| キー | 値 | 意味 |
|---|---|---|
| `MoraleCheerfulEnter`・`MoraleCheerfulExit` | 80・75 | 上機嫌になる・抜ける満足度（のりしろ） |
| `MoraleDiscontentEnter`・`MoraleDiscontentExit` | 40・45 | 不満になる・不満と限界を抜ける満足度 |
| `MoraleBreakingEnter`・`MoraleBreakingExit` | 20・25 | 限界になる・限界から不満へ戻る満足度 |
| `MoraleMultiplier_*` | 1.05・0.95・0.90 | 上機嫌・不満・限界の力の倍率（1人分の値に掛ける。大会の旧い満足度の補正を置き換え） |
| `MoraleCheerfulGrowthBonus`・`MoraleCheerfulHelpCreditMultiplier` | 0.02・1.5 | 上機嫌の成長の判定・研究の手伝い |
| `MoraleLowTraversalHpLossMultiplier`・`MoraleBreakingContagion` | 1.1・−1 | 不満・限界の道中のHPの減り／限界の子と同じ部隊の子の満足度（毎週） |
| `SlackChance_Discontent`・`SlackChance_Breaking`・`SlackTraitModifier` | 10・25・5 | さぼる確率（%）と、怠け者・飽きっぽい（＋）・求道派（−）の加減 |
| `DrivePower_Main`・`_Sub`・`_Disliked` | 0.06・0.03・−0.05 | 志向の力の上乗せ（その仕事に出ているとき） |
| `DriveGrowth_Main`・`_Sub`・`_Disliked` | 0.03・0.02・−0.03 | 志向の成長の上乗せ |
| `BuddyThreshold`・`BuddyPowerBonus` | 80・0.03 | 相棒の相性・力の上乗せ |
| `HostilePowerPenalty`・`HostileTraversalHpLossMultiplier` | −0.05・1.1 | 険悪な相手と一緒の力・道中のHPの減り |
| `GuardChancePercent`・`GuardShareRate` | 25・0.5 | かばい合いの確率・相棒が引き受ける損耗の割合 |
| `AffinityBand_*Max` | 24・64・104 | 素地の隔たりの帯（共鳴・調和・ふつう、これより遠いと不協和） |
| `AffinityGain_*`・`AffinityLoss_*`・`AffinityInitial_*` | 1.75〜0.5・0.5〜1.5・60〜45 | 帯ごとの相性の上がり方・下がり方・はじめの値 |

`tournament.csv` の `SatisfactionLowThreshold`・`SatisfactionLowFactor`・`SatisfactionHighThreshold`・`SatisfactionHighFactor` は撤去した。

## 冒険者の個性の土台で追加されたファイル（2026年10月、→ 03 §0.100）

`personality.csv`（新設、すべて仮の値）。段4a は満足度にだけ効く。力の上乗せ・相性の素地の効き目は段4b から（→ `docs/検討中_経営と育成.md`）。

| キー | 値 | 意味 |
|---|---|---|
| `SubDriveChancePercent`・`DislikeChancePercent` | 60・40 | 副志向・苦手を持つ確率（%）。主志向は必ず1つ。苦手は仕事の5つから |
| `TraitPullWeight` | 4 | 特性に合う志向の選ばれやすさ（勤勉→訓練、浪費家→お金、カリスマ→仲間、飽きっぽい→訓練が苦手） |
| `TemperamentWeight_*` | 1・2・3・2・1 | 気質（とても慎重〜とても大胆）の選ばれやすさ |
| `AffinityInheritSpread` | 8 | 秘薬の娘の相性の素地＝両親の中点±この値（0〜255で循環） |
| `SatisfactionMain`・`SatisfactionSub`・`SatisfactionDisliked` | 2・1・−2 | 主・副志向のことをした週／苦手な仕事をした週の満足度 |
| `MotiveNegativeMain`・`MotiveNegativeSub` | −2・−1 | 動機（仲間・お金・名声）の嫌うきっかけの満足度 |
| `ComradesFriendThreshold` | 70 | 仲間：この相性以上の相手と同じ部隊なら喜ぶ |
| `FameDroughtWeeks` | 24 | 名声：手柄の無いこの週数のあと、季節に1回焦る |
| `TemperamentMatch`・`TemperamentMismatch`・`TemperamentBoldInjury` | 1・−1・−5 | 扉前の構えが気質に合う／合わない（「とても」は2倍）／強気の決戦で重傷を負った慎重寄りの子 |
| `RevealLikely`・`RevealSure` | 4・8 | 見立て：きっかけを見た回数で「〜らしい」・確か（目利きで減り、秘薬の娘は半分） |
| `HintTruthPercent` | 75 | 採用の一言が主志向を言い当てる確率（%） |

## 作業を減らす「まとめて」の操作で追加されたキー（2026年10月、→ 03 §0.99）

まとめて鑑定・交渉の自動の予算と、大会の【おすすめの子で申し込む】の選び方。どれもシミュレーターの手入れと同じ考え方の値で、ゲームの決まりは変えていない（シミュレーターの数字は §0.98 のまま）。

| ファイル | キー | 値 | 意味 |
|---|---|---|---|
| `economy.csv` | `ChoreReserveWageWeeks` | 4 | まとめて鑑定・交渉の自動で残しておく所持金＝現役の週給の合計×この週数（派遣中の子の週給は数えない） |
| `tournament.csv` | `RecommendMinHpRatio` | 0.6 | おすすめの申し込み：HPがこの割合未満の子は選ばない |
| `tournament.csv` | `RecommendStrengthRatio` | 0.85 | 同：試合の強さが相手の幅の中央×この倍率に届かない大会には出さない（新人戦は除く） |
| `tournament.csv` | `RecommendPushHpRatio` | 0.8 | 同：HPがこの割合以上なら過ごし方を追い込み、未満なら休養 |

まとめて売る素材の「残す数」と、おすすめの装備の値打ち（`boss_power_weights.csv` の職業の重み）は既存の表から計算する（新しいキーは無い）。

## 決戦の勝率で追加／変更されたキー（2026年10月、→ 03 §0.98）

決戦の勝ち負けを、火力÷要求の比 r から決まる勝率で抽選する（勝率＝1÷(1＋e^(−傾き×(r−中心)))）。目安：比0.85で2割・0.9で3割・0.95で5割・1.0で7割・1.1で9割。

| ファイル | キー | 値 | 意味 |
|---|---|---|---|
| `dungeon.csv` | `BossWinCenterRatio` | 0.95 | 勝率5割になる火力÷要求の比 |
| `dungeon.csv` | `BossWinSteepness` | 14 | 曲線の傾き（大きいほど比の差で勝率が大きく動く） |
| `dungeon.csv` | `BossWinSureRatio` | 1.3 | この比以上なら必ず勝つ |
| `dungeon.csv` | `BossWinHopelessRatio` | 0.7 | この比未満なら勝てない |
| `squad_orders.csv` | `CautiousMinWinChance`・`StandardMinWinChance`・`BoldMinWinChance` | 0.9・0.6・0.3 | 扉前の構えごとの、挑む勝率の下限（旧 `*PowerMargin` 1.2・1.0・1.0 を撤去） |

`audit 30 1440 L3 simple` で30/30・13年目、強気は12年目（決戦の負けと重傷が約2倍）。慎重はすべてのギミックに万全の備えを求めるため、まだ0/30（→ 03 §0.98）。

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

- `GatheringDropBasePct`（5）／`GatheringDropMaxPct`（20）／`GatheringDropScoreDivisor`（40）／
  `GatheringDropPerDepthPct`（1）… 探索（採取）任務での遺物ドロップ確率。
  基礎 ＋ 採取スコア÷40 ＋ 深さの段×1 を上限20%でクランプする（§0.126。以前は到達階層÷20・上限15%）。
- `RarityDepthBandMax`（3;6;9;12）／`RarityWeightsBand1`〜`RarityWeightsBand5`（銅;銀;金;虹の%）… 希少度の帯（§0.126）。
  **深さの段**＝出土階層÷10（切り捨て）＋(フィールドの順−1)、最低1（森の10F＝1・森の100F＝10・深淵の100F＝14）。
  段の帯ごとの割合で1〜100の乱数から希少度を決める：帯1（段1〜3）80;20;0;0・帯2（4〜6）55;35;10;0・帯3（7〜9）35;40;22;3・
  帯4（10〜12）20;40;32;8・帯5（13〜）10;35;40;15。浅い帯では金・虹が出ない。以前の閾値方式（乱数＋階層÷10＋上乗せを60・85・100と比べる）を置き換えた。
- `BossRarityBandBonus`（1）／`BossMinimumRarity`（`Rare`）… 階層ボス撃破ドロップは1つ深い帯で引き、最低保証は銀（§0.126。以前は `BossRelicRollBonus`＋25）。
- `BossExtraRelicMinDepth`（7）／`BossExtraRelicPctPerDepth`（5）… 深さの段7以上の主の撃破で、2つ目の遺物が出る確率＝段×5%（§0.126）。

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
| `VisitFieldId`・`VisitFloor` | forest・60（§0.121で40から） | このフィールドのこの階層のボスを初めて倒した週にイザベラが来訪し、翌月の第4週に最初の交流戦 |
| `ExchangeFactor_Sword`・`_Magic`・`_Skill` | 1.00・1.08・1.00（§0.121で1.08・1.18・1.08から） | 交流戦の相手の強さ＝来訪したときの自分のギルドで部門の強さが最も高い子の値×この倍率（初回に勝てる見込みは3本勝負で約4割） |
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
| `BraveFactor` | ×1.05 | 豪胆の補正（満足度の補正 `SatisfactionLow*`・`SatisfactionHigh*` は §0.101 で撤去し、満足度の段階〈personality.csv `MoraleMultiplier_*`〉に置き換えた） |
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
| `progression.csv` | `Slot3MinActive` | 7 | 3つ目の部隊が開くのに要る現役の人数（森の20Fの主を倒したうえで。§0.120） |
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

## 研究の期間と費用の見直しで追加／変更されたキー（2026年10月、→ 03 §0.114）

テストプレイの感想「研究費用が安すぎる」「研究には数か月の期間が必要」を受けた。研究室で進められる研究は同時に1件。研究費と素材は始めたときに払い、`Weeks` 週たった週の決算で完了する。
研究の手伝い（§0.73）は研究費の割り引きをやめ、進めている研究を早めるものにした。すべて仮の値。

**research.csv**：列 `Weeks`（研究にかかる週、1以上）を足した。森の研究は4〜6週、洞窟〜深淵は8〜16週、段階研究のI・II・IIIは8・16・24週（秘薬の純度は8・12・16、培養槽は8・12）、魂魄融和の秘薬と霊薬の調合法は12週。
研究費は2000G以下だったものを上げた（段階研究など2500G以上は据え置き）。

| 研究 | 研究費（旧→新） | 週 |
|---|---|---|
| 薬草湿布の調合・特殊保存嚢 | 200→600・250→750 | 4・4 |
| 生体蛍光試薬・軽量踏破靴 | 300→900・300→900 | 6・6 |
| 硬化軟膏・古代戦術録・耐熱踏破法・魂魄安定の霊香 | 500→1500・800→2400・1200→3600・2000→6000 | 8・10・12・16 |
| 潤い美肌液・滋養強壮アンプル・古代香料の外販・不老長生薬 | 100→800・200→1500・300→2500・500→4000 | 4・6・8・10 |
| 魂魄融和の秘薬 | 1500→3000 | 12 |

**training.csv**

| キー | 値 | 意味 |
|---|---|---|
| `ResearchCreditMax` | 5000→600 | 研究の手伝いが貯まる上限（単位を G から pt に） |
| `ResearchCreditPerWeek`（新設） | 60 | 研究の手伝いをこれだけ使うと、進めている研究が1週ぶん余計に進む（1週に1回まで） |
| `ResearchMaxShortenRate`（新設） | 0.5 | 研究の手伝いで縮められる週の上限＝研究の週×この値（切り捨て） |
| `ResearchCreditMaxDiscountRate` | （削除） | 研究費の割り引きをやめた |

**master_mood.csv**：`IdleAdventurerHelp_ResearchCredit`（10）の単位を G から pt に。

シミュレーター（`audit 30 1440 L3 simple`）：クリア 28/30・12年目〔10〜14〕（変更前 28/30・11年目〔9〜14〕）、敗北2回（同2回）。年のはじめの所持金の中央値は2年目 5,006G・4年目 3,156G・7年目 13,004G（変更前 4,946G・3,251G・14,704G）。
シミュレーターは研究を1つずつ、商い（内職の売上）を先に、あとは CSV の順に始める。CSV の順のまま（内職が9番目）だとクリア 23/30・敗北6回で、内職の研究が遅れて所持金が細る。研究室の使い方の場面（s01_research）でアルベールが「内職の薬から」と勧める。
`campaign 10 1440`：クリア 10/10・12年目〔11〜13〕、済ませた研究 21〜23件（全25件）。

## 部隊長の報告で追加されたファイル（2026年10月、→ 03 §0.116）

テストプレイの感想「月報として、各部隊のリーダーが結果を報告しに来る。冒険者に愛着が持てるように」を受けた。値も台詞もすべて仮。

**squad_report.csv**（新規）

| キー | 値 | 意味 |
|---|---|---|
| `GrowthMinGain` | 3 | 「成長」を報告する条件：部隊長以外の隊員のうち、その月の能力の伸びの合計が最も大きい子がこの値以上 |
| `TemperamentCautiousMax`・`TemperamentBoldMin` | −1・1 | 気質がこれ以下なら慎重、これ以上なら大胆な口調（ほかと個性の無い子は中庸） |

**report_lines.csv**（新規）：報告の種類9つ × 気質3つの台詞、主志向ごとの言い回し（お金・仲間・名声・訓練・潜行・調査・採取）、初期の3人（クラウディア・リナ・フィオナ）の専用の台詞。物語帳の人物は専用の台詞だけ、ほかの子は気質と主志向の台詞を合わせた中から月ごとに回して選ぶ。

## 副官の注目で追加されたキー（2026年10月、→ 03 §0.117）

**personality.csv**（すべて仮の値）

| キー | 値 | 意味 |
|---|---|---|
| `FocusSlots` | 2 | 同時に注目できる人数 |
| `FocusSlotsExtra`・`FocusExtraBosses` | 1・20 | 倒した階層ボスが20体以上なら、注目できる人数が1人増える |
| `FocusExposureMultiplier` | 2 | 注目している子は、志向・気質のきっかけを見た回数をこの倍で数える |
| `FocusMonthlyReveals` | 1 | 注目している子は、月の最後の週に、まだ確かでない主志向・副志向・苦手・気質をこの回数ぶん数える |

## 培養槽の2年と育て方で変更／追加されたキー（2026年10月、→ 03 §0.118）

**soul_fusion.csv**（すべて仮の値）

| キー | 値 | 意味 |
|---|---|---|
| `CultureWeeks` | 12→96 | 処方から名簿に加わるまでの週（2年） |
| `ChildJoinAge`（新設） | 16 | 娘が加わるときの年齢（満期は26歳の年度末なので10年現役） |
| `NurtureSeasonWeeks`（新設） | 12 | 育て方を反映する間隔（季節。2年で8回） |
| `NurtureStatGain`（新設） | 3 | 体を鍛える（STR・VIT・AGI）／技を磨く（DEX・INT）／心を育てる（MND・LDR）：1回ごとに今の値に足す（素質で頭打ち） |
| `NurtureEntrustGain`（新設） | 1 | アルベールにまかせる（選ばなかったとき）：1回ごとに全能力に足す |
| `NurtureMotherCompatibility`・`NurtureMotherSatisfaction`（新設） | 5・5 | 母に会わせる：娘と母2人の相性・母の満足度 |
| `NurtureWorldExposure`（新設） | 2 | 外の世界を見せる：主志向と気質の見立ての回数 |

シミュレーター（育て方は「体を鍛える」）：`campaign 10 1440` でクリア 9/10・12年目〔11〜13〕、秘薬の娘は1回に1〜6人（前 5〜8人）。`audit 30 1440 L3 simple` で 27/30・12年目。

## イザベラの来訪を遅らせた改訂で変えたキー（2026年10月、→ 03 §0.121）

| キー | 値（前→今） | 意味 |
|---|---|---|
| `isabella.csv` の `VisitFloor` | 40 → 60 | イザベラが来訪する森の階（来訪で大会・交流戦・最初の訓練所・依頼・スポンサー・イザベラの融資が開く） |
| `isabella.csv` の `ExchangeFactor_Sword`・`_Magic`・`_Skill` | 1.08・1.18・1.08 → 1.00・1.08・1.00 | 交流戦の相手の強さの倍率。初戦に勝てる見込みが約3割→約4割 |

シミュレーター：来訪は 2.6年目→4.6年目（森の60F）。初戦の見込み（中央値）は倍率を据え置くと28%、下げて38%。
`campaign 20 1440` でクリア 16/20・13年目（前 18/20・12年目）、`audit 30 1440 L3 simple` で 30/30・12年目（前 28/30・12年目）。
