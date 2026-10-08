using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;

namespace GuildManager.Core.Models
{
    /// <summary>
    /// ゲーム全体の状態。将来的にJSONへシリアライズしてセーブする前提（→ 05 技術メモ §4）。
    /// そのため参照の持ち方はシンプルに保つ（循環参照を避ける）。
    /// </summary>
    public class GameState
    {
        public int WeekNumber { get; set; } = 1;

        /// <summary>
        /// 初期資金。→ BAL: 経済/初期資金（economy.csv、EconomyBalance.InitialGold。
        /// → 03 §10.1、項目58）。SaveDataからのロード時（FromSaveData）はオブジェクト
        /// 初期化子の評価順序により、この初期値がdata.Moneyで確実に上書きされる。
        /// </summary>
        public int Gold { get; set; } = EconomyBalance.InitialGold;

        public List<Adventurer> Adventurers { get; set; } = new();

        /// <summary>
        /// 永続的なパーティー編成の一覧（仕様書 03 §4.0.2。v1.9改訂で新設）。
        /// クエスト派遣のたびに毎回4人を選び直す必要はなく、事前に編成したこの一覧から
        /// 1つ選ぶだけでよい（→ PartyFormationSystem・DungeonExpeditionSystem）。
        /// どのSavedPartyのMemberIdsにも含まれない現役冒険者は「未編成」として扱う
        /// （→ PartyFormationSystem.GetUnassignedAdventurers）。
        /// </summary>
        public List<SavedParty> SavedParties { get; set; } = new();

        /// <summary>
        /// 引退した冒険者の一覧（40歳強制引退・早期引退の両方。仕様書 03 §3.7・§7）。
        /// 現役ロースター（Adventurers）からは除外しつつ、データとしては破棄しない。
        /// 「顧問候補」として、AssignedTrainers・AssignedAdvisor・AssignedScoutMasterの
        /// いずれかに任意で任命できる。
        /// </summary>
        public List<Adventurer> RetiredAdventurers { get; set; } = new();

        /// <summary>
        /// 致命傷を負ってギルドを去った冒険者の一覧（仕様書 03 §4.3.1）。
        ///
        /// 世界観上の扱い（→ 01_コンセプト.md、ダンジョン攻略システムと統一）：
        /// 冒険者は「戦死」しない。アルベールの秘薬で必ず一命は取り留めるが、危険な目に
        /// 遭わせたことに激怒したマスターがギルド登録を強制抹消するため二度と戻らない。
        /// **システム上は恒久的なロスト**であり、扱いは変わらない。プロパティ名
        /// （Fallen～）はセーブデータのJSONキーとして既に使われているため据え置く。
        ///
        /// 現役ロースター（Adventurers）・
        /// パーティ編成・訓練場配置からは完全に除外する。氏名・戦死週（FellAtWeek）・
        /// 戦死時の年齢（Age）・職業（JobClass）は Adventurer 自身が保持したまま移される
        /// （RetiredAdventurersと同じパターン）。
        /// </summary>
        public List<Adventurer> FallenAdventurers { get; set; } = new();

        /// <summary>
        /// 冒険者2人1組ごとの相性値（0〜100。仕様書 03 §5.3.1）。キーは常に
        /// (小さいGuid, 大きいGuid) の順に正規化して格納する（→ CompatibilitySystem.
        /// NormalizeKey）。未登録のペアは初期値50（中立）として扱う
        /// （→ CompatibilitySystem.GetCompatibility。値が変化するまでは
        /// このDictionaryにエントリを作らない）。
        /// </summary>
        public Dictionary<(Guid, Guid), int> Compatibility { get; set; } = new();

        /// <summary>
        /// 訓練施設に配置されている冒険者と、配置先の施設種別（→ 03 §3.1〜3.4「成長トリガー・
        /// 経路2」・§6）。v1.3改訂：訓練場・道場の4分割に伴い、単一のHashSet&lt;Guid&gt;から
        /// 「どの施設に配置されているか」まで持つDictionaryに変更した。
        /// </summary>
        public Dictionary<Guid, FacilityType> TrainingAssignments { get; set; } = new();

        /// <summary>
        /// 各訓練施設（鍛錬所/学問所/技巧所）に配置されている教官
        /// （引退済み冒険者。仕様書 03 §7.1）。施設ごとに1名まで。未配置の施設はキー自体が
        /// 存在しないか値がnull。
        /// </summary>
        public Dictionary<FacilityType, Guid?> AssignedTrainers { get; set; } = new();

        /// <summary>
        /// 教官が今の訓練施設に続けて在任している週数（教官深化 Step 3、→ 03 §7.1）。
        /// 週次決算の伝授ロールの後に1増える（→ TrainingSystem.ProcessWeeklyTrainerTenure）。
        /// 任命・交代・解除・別ポストへの異動で0に戻る（→ AdvisorSystem）。キーが無ければ0週。
        /// </summary>
        public Dictionary<FacilityType, int> TrainerTenureWeeks { get; set; } = new();

        /// <summary>
        /// 教官が重点的に伝授する特性のId（施設ごとに1つ、教官深化 Step 3、→ 03 §7.1）。
        /// キーが無ければ自動（教官の特性枠の並び順）。教官が替わると解除される（→ AdvisorSystem）。
        /// </summary>
        public Dictionary<FacilityType, string> TrainerFocusTraits { get; set; } = new();

        /// <summary>
        /// 訓練施設ごとに特化して伸ばす能力（"STR" など。§0.75、→ TrainingSystem.SetSpecialtyStat）。
        /// キーが無ければ「両方」（施設の2つの能力のどちらかへランダムに伸びる）。変えられるのは月のはじめだけ。
        /// </summary>
        public Dictionary<FacilityType, string> TrainingSpecialtyStats { get; set; } = new();

        /// <summary>作戦資料室に配置されている参謀（引退済み冒険者。仕様書 03 §7.2）。1名まで。null＝未配置。</summary>
        public Guid? AssignedAdvisor { get; set; }

        /// <summary>
        /// 任命されているスカウト顧問（引退済み冒険者。仕様書 03 §7.3）。1名まで。null＝未任命。
        /// v1.4改訂：冒険者支援室（RecruitmentOffice）に紐づく役職となった。同施設がLv0
        /// （未建設）の間は、AdvisorSystem.TryAssignScoutMaster側でガードし配置できない。
        /// </summary>
        public Guid? AssignedScoutMaster { get; set; }

        /// <summary>
        /// 9施設の現在状態（仕様書 03 §6。v1.3改訂：訓練場・道場の4分割により4大施設から
        /// 8施設に拡張。v1.4改訂：冒険者支援室（RecruitmentOffice）を新設し9施設に拡張）。
        /// 宿舎・医務室・ギルド酒場のみ初期Lv1、残り6施設（訓練4施設・作戦資料室・
        /// 冒険者支援室）は初期Lv0（未建設）で開始する（→ §6）。
        /// </summary>
        public List<Facility> Facilities { get; set; } = CreateDefaultFacilities();

        /// <summary>
        /// 現在の建設キュー（仕様書 03 §6.1）。null＝工事中の施設なし。
        /// 同時に2件以上を持てない設計を、単一のnull許容フィールドで表現する。
        /// </summary>
        public FacilityConstruction? UnderConstruction { get; set; }

        /// <summary>
        /// 同時に派遣できる部隊の数（→ コアシステム刷新仕様「4. 進行管理」）。初期値1。
        /// 森の節目ボス撃破で拡張される（→ DungeonExpeditionSystem.ApplyFieldProgression）。
        /// 1枠の中で何名を出撃させるか（1〜4名のフリーアサイン）は制限しない
        /// ＝「枠」は部隊の数であって人数ではない。
        /// 実際の制限は DungeonExpeditionSystem.CanDispatch で行う。
        /// </summary>
        public int UnlockedSquadSlots { get; set; } = ProgressionBalance.InitialSquadSlots;

        /// <summary>
        /// 累計出撃回数。大迷宮への出撃が帰還するたびに1増える（取り消しは数えない、
        /// → DungeonExpeditionSystem）。
        /// </summary>
        public int TotalDispatchCount { get; set; } = 0;

        /// <summary>
        /// マスター（アルベール）の機嫌（仕様書 03 §8.1、0〜100）。大迷宮での成果で上がり、
        /// 成果ゼロの週は退屈して下がる（→ MasterMoodSystem）。内職売上の倍率を決め
        /// （→ EconomySystem.ProcessWeeklySideJobIncome）、0に達すると副官解雇＝敗北（→ DefeatSystem）。
        /// 旧・名声（Reputation）とギルド格付け（GuildRank）の後継（2026年9月）。
        /// </summary>
        public int MasterMood { get; set; } = MasterMoodBalance.InitialMood;

        /// <summary>
        /// 研究の手伝い（2026年10月・§0.73、G相当）。待機中に「アルベールの研究を手伝う」冒険者がいる週に貯まり
        /// （→ Systems.IdleActivitySystem、上限 TrainingBalance.ResearchCreditMax）、次の研究の研究費から割り引いて使う
        /// （→ Systems.ResearchSystem.GetDiscount）。旧セーブには無く0で読まれる。
        /// </summary>
        public int ResearchCredit { get; set; }

        /// <summary>
        /// クリアしたか（仕様書 03 §8.2、2026年10月・§0.59）。最終フィールド（深淵）の100Fボス撃破でtrueになり、
        /// 以後は取り消されない（→ DungeonExpeditionSystem.ApplyFieldProgression）。クリア後もギルドは続けられる。
        /// 旧名 FinalQuestUnlocked（最終討伐クエストの解禁フラグ）。セーブのJSONキーは互換のため FinalQuestUnlocked のまま。
        /// </summary>
        public bool IsGameCleared { get; set; } = false;

        /// <summary>クリアした週（→ GameState.WeekNumber）。null＝未クリア。エンディングの記録に使う（→ GuildChronicle）。</summary>
        public int? ClearedAtWeek { get; set; }

        /// <summary>深淵100Fのボスを倒した部隊の生還者のId（エンディングの記録に出す）。未クリアなら空。</summary>
        public List<Guid> ClearingMemberIds { get; set; } = new();

        /// <summary>
        /// 大迷宮での成果（新階層開拓・有効な調査・採取成功・ボス撃破）が最後にあってから経過した週数
        /// （→ 03 §8.1.1、MasterMoodSystem）。成果のあった週に0へリセットされ、成果ゼロの週は+1される。
        /// 旧 WeeksSinceLastRankAppropriateQuest の後継（旧セーブの値は引き継ぐ）。
        /// </summary>
        public int WeeksSinceLastGuildActivity { get; set; } = 0;

        /// <summary>
        /// 所持金がマイナスの週が連続何週続いているか（仕様書 03 §8.3「破産」）。
        /// プラスに戻った週に0へリセットされる。DefeatSystem.ProcessWeeklySettlementが更新する。
        /// </summary>
        public int ConsecutiveNegativeGoldWeeks { get; set; } = 0;

        /// <summary>
        /// 敗北理由（仕様書 03 §8.3）。null＝まだ敗北していない。一度確定したら変化しない
        /// （DefeatSystemが上書きしない）。
        /// </summary>
        public DefeatReason? DefeatReason { get; set; }

        // ==================== 大迷宮（ダンジョン攻略システム） ====================

        /// <summary>
        /// 大迷宮の全フィールド一覧（→ DungeonField・Data.SampleData.CreateDefaultFields、
        /// 大迷宮5フィールド拡張仕様）。各フィールドの解析率・撃破状態・開放状態・最高到達階層は
        /// すべてここに保持され、セーブ対象になる。旧・単一ダンジョンモデルの`FloorBosses`
        /// （フラットな一覧）はこちらへ統合済み（v2.0時点で撤去）。
        /// </summary>
        public List<DungeonField> DungeonFields { get; set; } = new();

        /// <summary>
        /// 大迷宮へ出撃中（次の週次決算で解決待ち）の部隊一覧（→ Systems.DungeonExpeditionSystem）。
        /// 同時出撃枠（UnlockedSquadSlots）はこの件数で消費される。
        /// </summary>
        public List<ActiveDungeonMission> ActiveDungeonMissions { get; set; } = new();

        /// <summary>
        /// 現在の攻略対象フィールド（開放済みかつ未制覇のうち、最も若いOrderのフィールド）。
        /// 全フィールド制覇済み・DungeonFields未設定ならnull。
        /// </summary>
        public DungeonField? GetActiveField() =>
            DungeonFields.Where(f => f.IsUnlocked && f.GetNextActiveBoss() != null)
                .OrderBy(f => f.Order).FirstOrDefault();

        /// <summary>
        /// 現在の攻略対象ボス（→ GetActiveField().GetNextActiveBoss()）。全フィールド制覇済み・
        /// 未設定ならnull。旧・単一ダンジョンモデル時代からのAPIをそのまま維持しており、
        /// 呼び出し側（DungeonPanel.cs等）は複数フィールドの存在を意識せずに使い続けられる。
        /// </summary>
        public FloorBoss? GetCurrentFloorBoss() => GetActiveField()?.GetNextActiveBoss();

        /// <summary>
        /// ギルドの素材インベントリ（→ Balance.MaterialBalance、探索（採取）任務の成果）。
        /// キーは素材Id、値は所持数。未所持の素材はキー自体が存在しない（→ AddMaterial）。
        /// </summary>
        public Dictionary<string, int> Materials { get; set; } = new();

        /// <summary>
        /// 素材を加算する（→ Systems.GatheringResolverの成果適用）。countが0以下、または
        /// materialIdが空の場合は何もしない（防御的：不正な呼び出しを黙って無視する）。
        /// </summary>
        public void AddMaterial(string materialId, int count)
        {
            if (string.IsNullOrEmpty(materialId) || count <= 0)
                return;

            Materials.TryGetValue(materialId, out int current);
            Materials[materialId] = current + count;
        }

        /// <summary>
        /// 未鑑定の古代遺物（レリック）の在庫（→ Models.UnidentifiedItem、03 §4.7）。探索（採取）任務の
        /// 副産物と階層ボス撃破の確定ドロップで積み上がり、鑑定（→ Systems.AppraisalSystem.Appraise）で
        /// 1個ずつ消費される。本フィールド追加前の既存セーブにはJSON側にキー自体が無いが、
        /// System.Text.Jsonは未知プロパティを無視して既定値（空リスト）のまま復元するため、
        /// ロード自体が失敗することはない（→ CompletedResearchIdsと同じ互換性の担保）。
        /// </summary>
        public List<UnidentifiedItem> UnidentifiedItems { get; set; } = new();

        /// <summary>
        /// ギルド保管庫（→ Models.EquipmentItem、03 §4.7）。鑑定で出土した武具など、まだ誰にも
        /// 装備させていない現物の在庫。冒険者側は従来どおりカタログIdの文字列で装備状態を持つ
        /// （→ Adventurer.EquippedWeaponId）ため、ここは「在庫」専用で装備状態は表現しない。
        /// UnidentifiedItemsと同じく、本フィールドを持たない旧セーブでは空リストで復元される。
        /// </summary>
        public List<EquipmentItem> Armory { get; set; } = new();

        /// <summary>
        /// 入手済みの固有武具Id（→ uniques.csv、Systems.UniqueItemSystem、03 §4.7.5、2026年9月・§0.45）。
        /// 固有武具は1つのセーブにつき1本のため、売却・ロストで手元から消えてもIdは残り、二度と出ない。
        /// 本フィールドを持たない旧セーブでは空集合で復元される。
        /// </summary>
        public HashSet<string> ObtainedUniqueIds { get; set; } = new();

        /// <summary>
        /// 完了済みの研究Id一覧（→ Models.ResearchDefinition・アルベールの研究室）。
        /// 一度完了した研究は取り消せない（削除する経路を用意しない）。初期値は空集合、
        /// 既存セーブ（本フィールド追加前）はJSON側にキーが無いため復元時は自動的に空集合になる
        /// （→ 互換性維持。研究未完了の状態として扱われるだけで、ロード自体は失敗しない）。
        /// </summary>
        public HashSet<string> CompletedResearchIds { get; set; } = new();

        /// <summary>指定した研究が完了済みか。</summary>
        public bool IsResearchCompleted(string researchId) => CompletedResearchIds.Contains(researchId);

        /// <summary>
        /// 培養槽で育っている子（→ SoulFusionCulture・Systems.SoulFusionSystem、03 §5.4・§0.58）。
        /// 旧セーブには無く、空のまま読まれる。
        /// </summary>
        public List<SoulFusionCulture> SoulFusionCultures { get; set; } = new();

        /// <summary>
        /// 直近の月報（2026年10月・§0.71、→ Systems.MonthlyReport.Record）。古い順で、最大 MonthlyReport.KeptReports（12）件。
        /// 画面上部の【📅 月報】から見返す。旧セーブには無く、空のまま読まれる。
        /// </summary>
        public List<Systems.MonthlyReport> MonthlyReports { get; set; } = new();

        // ---- 大会（2026年10月・§0.82、→ Systems.TournamentSystem・FacilityUnlockSystem） ----

        /// <summary>暦に置いた大会（今年の分と、結果の出た過去の分）。年のはじめに今年の分を置く（→ TournamentSystem.EnsureSchedule）。</summary>
        public List<TournamentEvent> TournamentEvents { get; set; } = new();

        /// <summary>今月の出場（月のはじめに決める。月が変わると消える）。</summary>
        public List<TournamentEntry> TournamentEntries { get; set; } = new();

        /// <summary>施設ごとの「建ててよいLv」（大会などのご褒美で開く、→ FacilityUnlockSystem）。キーが無ければ初期値。</summary>
        public Dictionary<FacilityType, int> FacilityUnlockedLevels { get; set; } = new();

        /// <summary>ギルドの入賞（ベスト4以上）の累計（宿舎のご褒美・地方大会の数に使う）。</summary>
        public int TournamentPlacingsTotal { get; set; }

        /// <summary>大会の賞金の累計（ギルド酒場のご褒美に使う）。</summary>
        public int TournamentPrizeTotal { get; set; }

        /// <summary>もう出した招待の鍵（"lord:フィールドId"・"royal:年"・"margrave"）。同じ招待を二度出さない。</summary>
        public HashSet<string> TournamentInviteKeys { get; set; } = new();

        // ---- イザベラの来訪と交流戦（2026年10月・§0.84、→ Systems.IsabellaSystem） ----

        /// <summary>イザベラが来訪した週（森の40Fのボスを初めて倒した週）。まだなら null（大会も依頼も無い）。</summary>
        public int? IsabellaVisitWeek { get; set; }

        /// <summary>交流戦の相手の強さの基準（来訪したときの、自分のギルドの部門ごとの最も強い子の部門の強さ）。</summary>
        public Dictionary<TournamentDiscipline, double> ExchangeAnchor { get; set; } = new();

        /// <summary>行った交流戦の数（相手の強さが1回ごとに上がる）。</summary>
        public int ExchangeMatchesPlayed { get; set; }

        /// <summary>交流戦に勝った数（初勝利で派遣の教官、2勝目から賞金と機嫌）。</summary>
        public int ExchangeWins { get; set; }

        /// <summary>大会の暦を置き始める週（最初の交流戦の次の月のはじめ）。まだなら null（大会を置かない）。</summary>
        public int? TournamentCalendarFromWeek { get; set; }

        /// <summary>依頼が届き始める週（初めての入賞の次の季節のはじめ）。まだなら null（依頼は届かない）。</summary>
        public int? CommissionsFromWeek { get; set; }

        /// <summary>§0.84より前の決まりで依頼が届き始めた週（1年目の夏のはじめ）。旧セーブを読むときだけ使う。</summary>
        public const int LegacyFirstOfferWeek = 13;

        /// <summary>依頼人イザベラ（白百合の杖）のId（→ commission_clients.csv、§0.85）。</summary>
        public const string IsabellaClientId = "Isabella";

        /// <summary>§0.85より前の依頼人「王都の騎士団」のId。旧セーブを読むときにイザベラへ読み替える。</summary>
        public const string LegacyKnightsClientId = "Knights";

        /// <summary>派遣の教官（マルグリット）が、訓練所が建つのを待っている（交流戦に初めて勝ったあと）。</summary>
        public bool GuestTrainerPending { get; set; }

        /// <summary>派遣の教官が帰る週（来た週＋GuestTrainerWeeks）。いなければ null。教官本人は RetiredAdventurers に IsGuest で入る。</summary>
        public int? GuestTrainerUntilWeek { get; set; }

        /// <summary>引退者のうち、ギルドの元冒険者（派遣の教官を除く）。最初の引退者の判定・年代記に使う。</summary>
        public IEnumerable<Adventurer> GuildRetirees => RetiredAdventurers.Where(a => !a.IsGuest);

        /// <summary>辺境伯杯の優勝のご褒美：次の改築費が半額。</summary>
        public bool NextUpgradeHalfPrice { get; set; }

        /// <summary>
        /// 掲示中・受けた依頼（→ GuildCommission・Systems.CommissionSystem、03 §4.9・§0.64）。
        /// 旧セーブには無く、空のまま読まれる（次の季節のはじめから届く）。
        /// </summary>
        public List<GuildCommission> Commissions { get; set; } = new();

        /// <summary>依頼人ごとの達成件数（依頼人Id→件数。固有武具が届く条件、→ CommissionBalance.PatronUniqueCompletions）。</summary>
        public Dictionary<string, int> CommissionCompletions { get; set; } = new();

        /// <summary>予告中・発生中の迷宮の異変（→ DungeonAnomaly・Systems.DungeonAnomalySystem、03 §4.10・§0.64）。無ければnull。</summary>
        public DungeonAnomaly? Anomaly { get; set; }

        /// <summary>現役・引退者・除籍者のどこかにいる冒険者をIdで引く（魂魄融和の親の表示など）。見つからなければ null。</summary>
        public Adventurer? FindAdventurer(Guid id) =>
            Adventurers.FirstOrDefault(a => a.Id == id)
            ?? RetiredAdventurers.FirstOrDefault(a => a.Id == id)
            ?? FallenAdventurers.FirstOrDefault(a => a.Id == id);

        /// <summary>
        /// 指定した種類の施設の専門（§0.76）。Lvが FacilityBalance.SpecialtyFromLevel 以下なら None。
        /// それより上なのに専門が無い（テストで直接Lvを上げた等）ときは FacilityBalance.GetDefaultSpecialty。
        /// </summary>
        public FacilitySpecialty GetFacilitySpecialty(FacilityType type)
        {
            var facility = Facilities.FirstOrDefault(f => f.Type == type);
            if (facility == null || facility.CurrentLevel <= FacilityBalance.SpecialtyFromLevel || !FacilityBalance.HasSpecialty(type))
                return FacilitySpecialty.None;
            return facility.Specialty != FacilitySpecialty.None ? facility.Specialty : FacilityBalance.GetDefaultSpecialty(type);
        }

        /// <summary>指定した種類の施設の現在Lvを返す。該当データが無い場合は1を返す（防御的フォールバック）。</summary>
        public int GetFacilityLevel(FacilityType type)
        {
            foreach (var facility in Facilities)
                if (facility.Type == type) return facility.CurrentLevel;
            return 1;
        }

        // ==================== セーブ/ロード（→ 03 §12） ====================

        /// <summary>
        /// 現在の状態をセーブ用データ（SaveData）へ変換する。System.Text.Jsonで
        /// 直接シリアライズできない形（タプルキーの辞書・Party等）を、
        /// JSON化可能な形（一覧・文字列キーの辞書）に変換する処理を担う。
        /// </summary>
        public SaveData ToSaveData()
        {
            var data = new SaveData
            {
                CurrentTurn = WeekNumber,
                Money = Gold,
                MasterMood = MasterMood,
                ResearchCredit = ResearchCredit,
                ConsecutiveNegativeGoldWeeks = ConsecutiveNegativeGoldWeeks,
                DefeatReason = DefeatReason?.ToString(),
                WeeksSinceLastGuildActivity = WeeksSinceLastGuildActivity,
                FinalQuestUnlocked = IsGameCleared,
                ClearedAtWeek = ClearedAtWeek,
                ClearingMemberIds = new List<Guid>(ClearingMemberIds),
                UnlockedSquadSlots = UnlockedSquadSlots,
                TotalDispatchCount = TotalDispatchCount,
                ActiveAdventurers = new List<Adventurer>(Adventurers),
                RetiredAdvisorCandidates = new List<Adventurer>(RetiredAdventurers),
                FallenAdventurers = new List<Adventurer>(FallenAdventurers),
                SavedParties = new List<SavedParty>(SavedParties),
                DungeonFields = new List<DungeonField>(DungeonFields),
                Materials = new Dictionary<string, int>(Materials),
                UnidentifiedItems = new List<UnidentifiedItem>(UnidentifiedItems),
                Armory = new List<EquipmentItem>(Armory),
                ObtainedUniqueIds = new HashSet<string>(ObtainedUniqueIds),
                CompletedResearchIds = new HashSet<string>(CompletedResearchIds),
                SoulFusionCultures = new List<SoulFusionCulture>(SoulFusionCultures),
                MonthlyReports = new List<Systems.MonthlyReport>(MonthlyReports),
                TournamentEvents = new List<TournamentEvent>(TournamentEvents),
                TournamentEntries = new List<TournamentEntry>(TournamentEntries),
                FacilityUnlockedLevels = FacilityUnlockedLevels.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                TournamentPlacingsTotal = TournamentPlacingsTotal,
                TournamentPrizeTotal = TournamentPrizeTotal,
                TournamentInviteKeys = new List<string>(TournamentInviteKeys),
                NextUpgradeHalfPrice = NextUpgradeHalfPrice,
                StoryRulesVersion = 1,
                IsabellaVisitWeek = IsabellaVisitWeek,
                ExchangeAnchor = ExchangeAnchor.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                ExchangeMatchesPlayed = ExchangeMatchesPlayed,
                ExchangeWins = ExchangeWins,
                TournamentCalendarFromWeek = TournamentCalendarFromWeek,
                CommissionsFromWeek = CommissionsFromWeek,
                GuestTrainerPending = GuestTrainerPending,
                GuestTrainerUntilWeek = GuestTrainerUntilWeek,
                Commissions = new List<GuildCommission>(Commissions),
                CommissionCompletions = new Dictionary<string, int>(CommissionCompletions),
                Anomaly = Anomaly,
            };

            foreach (var kv in Compatibility)
                data.CompatibilityPairs.Add(new CompatibilityPairRecord { IdA = kv.Key.Item1, IdB = kv.Key.Item2, Value = kv.Value });

            foreach (var kv in TrainingAssignments)
                data.TrainingAssignments.Add(new TrainingAssignmentRecord { AdventurerId = kv.Key, Facility = kv.Value.ToString() });

            foreach (var facility in Facilities)
            {
                data.FacilityLevels[facility.Type.ToString()] = facility.CurrentLevel;
                if (facility.Specialty != FacilitySpecialty.None)
                    data.FacilitySpecialties[facility.Type.ToString()] = facility.Specialty.ToString(); // §0.76
            }

            if (UnderConstruction != null)
            {
                data.FacilityUnderConstruction = UnderConstruction.Type.ToString();
                data.ConstructionTargetLevel = UnderConstruction.TargetLevel;
                data.ConstructionWeeksRemaining = UnderConstruction.WeeksRemaining;
                if (UnderConstruction.TargetSpecialty != FacilitySpecialty.None)
                    data.ConstructionTargetSpecialty = UnderConstruction.TargetSpecialty.ToString();
                data.ConstructionIsRemodel = UnderConstruction.IsRemodel;
            }

            // 教官（訓練施設ごと）・参謀（作戦資料室＝WarRoom）・スカウト（冒険者支援室＝
            // RecruitmentOffice）を、施設種別名→冒険者Idの単一辞書に統合する（v1.4改訂で
            // 参謀・スカウトも施設に紐づくポストになったため。→ SaveData.AdvisorAssignments）。
            foreach (var kv in AssignedTrainers)
                data.AdvisorAssignments[kv.Key.ToString()] = kv.Value;
            foreach (var kv in TrainerTenureWeeks)
                data.TrainerTenureWeeks[kv.Key.ToString()] = kv.Value;
            foreach (var kv in TrainerFocusTraits)
                data.TrainerFocusTraits[kv.Key.ToString()] = kv.Value;
            foreach (var kv in TrainingSpecialtyStats)
                data.TrainingSpecialtyStats[kv.Key.ToString()] = kv.Value;
            if (AssignedAdvisor.HasValue)
                data.AdvisorAssignments[FacilityType.WarRoom.ToString()] = AssignedAdvisor;
            if (AssignedScoutMaster.HasValue)
                data.AdvisorAssignments[FacilityType.RecruitmentOffice.ToString()] = AssignedScoutMaster;

            foreach (var mission in ActiveDungeonMissions)
            {
                data.DungeonMissions.Add(new DungeonMissionRecord
                {
                    FieldId = mission.Field.Id,
                    BossId = mission.Boss?.Id,
                    MissionType = mission.MissionType.ToString(),
                    PartyMemberIds = mission.Party.Members.Select(m => m.Id).ToList(),
                    Status = mission.Status.ToString(),
                    CurrentFloor = mission.CurrentFloor,
                    TargetedBossId = mission.TargetedBoss?.Id,
                    WeeksElapsed = mission.WeeksElapsed,
                    CarriedGold = mission.CarriedGold,
                    CarriedMaterials = new Dictionary<string, int>(mission.CarriedMaterials),
                    SavedPartyId = mission.SavedPartyId,
                });
            }

            return data;
        }

        /// <summary>
        /// セーブ用データ（SaveData）から状態を復元する。列挙型の文字列パースに
        /// 失敗した場合（セーブフォーマットの破損・非対応バージョンとの混入等）は
        /// FormatExceptionを投げる。呼び出し側（SaveLoadService.Load）はこれを
        /// 捕捉し、ロード失敗として扱う想定（→ 03 §12「ロード失敗時は新規ゲームの
        /// みを提示し、既存セーブを保護する」）。
        /// </summary>
        public static GameState FromSaveData(SaveData data)
        {
            var state = new GameState
            {
                WeekNumber = data.CurrentTurn,
                Gold = data.Money,
                // 機嫌を持たない旧セーブ（名声・格付けの時代）は初期値（平常）で始める（→ 03 §8.1）。
                MasterMood = Math.Clamp(data.MasterMood ?? MasterMoodBalance.InitialMood, MasterMoodBalance.Min, MasterMoodBalance.Max),
                // 研究の手伝い（§0.73）。キーを持たない旧セーブは0。壊れた値は0〜上限に収める。
                ResearchCredit = Math.Clamp(data.ResearchCredit, 0, TrainingBalance.ResearchCreditMax),
                ConsecutiveNegativeGoldWeeks = data.ConsecutiveNegativeGoldWeeks,
                DefeatReason = data.DefeatReason == null ? null : ParseEnum<DefeatReason>(data.DefeatReason, nameof(DefeatReason)),
                // 旧キー（WeeksSinceLastRankAppropriateQuest）しか無い旧セーブは、その値を引き継ぐ。
                WeeksSinceLastGuildActivity = data.WeeksSinceLastGuildActivity ?? data.WeeksSinceLastRankAppropriateQuest ?? 0,
                IsGameCleared = data.FinalQuestUnlocked,
                // クリアの週を持たない旧セーブ（§0.59 より前に深淵100Fを倒していた）は、読み込んだ週をクリアの週として補う。
                ClearedAtWeek = data.FinalQuestUnlocked ? (int?)(data.ClearedAtWeek ?? data.CurrentTurn) : null,
                ClearingMemberIds = new List<Guid>(data.ClearingMemberIds ?? new List<Guid>()),
                // 進行管理（→ コアシステム刷新仕様「4. 進行管理」）。同時出撃枠は
                // 0（＝一切派遣できない不整合な状態）で復元されないよう、未設定の
                // 古いセーブでは初期値へフォールバックする。
                UnlockedSquadSlots = data.UnlockedSquadSlots > 0 ? data.UnlockedSquadSlots : ProgressionBalance.InitialSquadSlots,
                TotalDispatchCount = data.TotalDispatchCount,
                Adventurers = new List<Adventurer>(data.ActiveAdventurers),
                RetiredAdventurers = new List<Adventurer>(data.RetiredAdvisorCandidates),
                FallenAdventurers = new List<Adventurer>(data.FallenAdventurers),
                SavedParties = new List<SavedParty>(data.SavedParties),
                DungeonFields = new List<DungeonField>(data.DungeonFields),
                Materials = new Dictionary<string, int>(data.Materials),
                // 未鑑定遺物・ギルド保管庫（→ 03 §4.7）。本フィールドを持たない旧セーブでは
                // System.Text.Jsonがプロパティ初期化子の空リストをそのまま残すため、空で復元される。
                UnidentifiedItems = new List<UnidentifiedItem>(data.UnidentifiedItems ?? new List<UnidentifiedItem>()),
                Armory = new List<EquipmentItem>(data.Armory ?? new List<EquipmentItem>()),
                ObtainedUniqueIds = new HashSet<string>(data.ObtainedUniqueIds ?? new HashSet<string>()),
                CompletedResearchIds = new HashSet<string>(data.CompletedResearchIds),
                // 培養中の子（§0.58）。キーを持たない旧セーブ、または null が書かれていても空で始める。
                // 子の Adventurer が欠けた記録（壊れたデータ）は捨てる。
                SoulFusionCultures = (data.SoulFusionCultures ?? new List<SoulFusionCulture>())
                    .Where(c => c?.Child != null).ToList(),
                // 直近の月報（§0.71）。キーを持たない旧セーブ、または null が書かれていても空で始める。
                MonthlyReports = (data.MonthlyReports ?? new List<Systems.MonthlyReport>()).Where(r => r != null).ToList(),
                // 大会（§0.82）。null は空で始める。
                TournamentEvents = (data.TournamentEvents ?? new List<TournamentEvent>()).Where(e => e != null).ToList(),
                TournamentEntries = (data.TournamentEntries ?? new List<TournamentEntry>()).Where(e => e != null).ToList(),
                FacilityUnlockedLevels = (data.FacilityUnlockedLevels ?? new Dictionary<string, int>())
                    .Where(kv => Enum.TryParse<FacilityType>(kv.Key, out _))
                    .ToDictionary(kv => Enum.Parse<FacilityType>(kv.Key), kv => kv.Value),
                TournamentPlacingsTotal = data.TournamentPlacingsTotal,
                TournamentPrizeTotal = data.TournamentPrizeTotal,
                TournamentInviteKeys = new HashSet<string>(data.TournamentInviteKeys ?? new List<string>()),
                NextUpgradeHalfPrice = data.NextUpgradeHalfPrice,
                // イザベラの来訪と交流戦（§0.84）。§0.84より前のセーブ（StoryRulesVersion 0）は下で「来訪済み・大会と依頼は開いている」に補う。
                IsabellaVisitWeek = data.IsabellaVisitWeek,
                ExchangeAnchor = (data.ExchangeAnchor ?? new Dictionary<string, double>())
                    .Where(kv => Enum.TryParse<TournamentDiscipline>(kv.Key, out _))
                    .ToDictionary(kv => Enum.Parse<TournamentDiscipline>(kv.Key), kv => kv.Value),
                ExchangeMatchesPlayed = data.ExchangeMatchesPlayed,
                ExchangeWins = data.ExchangeWins,
                TournamentCalendarFromWeek = data.TournamentCalendarFromWeek,
                CommissionsFromWeek = data.CommissionsFromWeek,
                GuestTrainerPending = data.GuestTrainerPending,
                GuestTrainerUntilWeek = data.GuestTrainerUntilWeek,
                // 依頼と迷宮の異変（§0.64）。キーを持たない旧セーブ、または null が書かれていても空・無しで始める。
                Commissions = (data.Commissions ?? new List<GuildCommission>()).Where(c => c != null).ToList(),
                CommissionCompletions = new Dictionary<string, int>(data.CommissionCompletions ?? new Dictionary<string, int>()),
                Anomaly = data.Anomaly,
                Facilities = new List<Facility>(),
            };

            // §0.84より前のセーブ（StoryRulesVersion 0）：大会は初めから開き、依頼は1年目の夏から届く決まりだったので、
            // 「来訪済み・大会の暦は初めから・依頼は第13週から」として読む（進行中の大会や依頼が消えないように）。
            // 交流戦の相手の強さの基準は、最初に使うときに今のギルドから決める（→ IsabellaSystem.Anchor）。
            if (data.StoryRulesVersion < 1)
            {
                state.IsabellaVisitWeek ??= 1;
                state.TournamentCalendarFromWeek ??= 1;
                state.CommissionsFromWeek ??= LegacyFirstOfferWeek;
            }

            // 依頼人「王都の騎士団」（Knights）は§0.85でイザベラ（Isabella）に替えた。旧セーブの依頼と達成件数をイザベラの分として読む。
            foreach (var c in state.Commissions.Where(c => c.ClientId == LegacyKnightsClientId))
                c.ClientId = IsabellaClientId;
            if (state.CommissionCompletions.Remove(LegacyKnightsClientId, out int knights))
                state.CommissionCompletions[IsabellaClientId] = state.CommissionCompletions.GetValueOrDefault(IsabellaClientId) + knights;

            // ボスの所属フィールド（→ FloorBoss.FieldOrder、§0.47）は、この項目を持たない旧セーブでは既定値1のまま
            // 読み込まれるため、所属フィールドの攻略順から付け直す（新しいセーブでも値は一致する）。
            foreach (var field in state.DungeonFields)
                foreach (var boss in field.Bosses)
                    boss.FieldOrder = field.Order;

            // 待機中の過ごし方（§0.73）。知らない過ごし方・能力名が書かれていたら既定（研究を手伝う・職業の伸び方）に戻す。
            foreach (var a in state.Adventurers.Concat(state.RetiredAdventurers).Concat(state.FallenAdventurers))
            {
                if (!Enum.IsDefined(a.IdleActivity)) a.IdleActivity = IdleActivity.Help;
                if (a.SelfTrainingStat != null && !Systems.AdventurerStatAccessor.AllStatNames.Contains(a.SelfTrainingStat)) a.SelfTrainingStat = null;
                // 戦績と観測日誌（大会と育成の栄光 段2）。段2より前のセーブには無いので空で補う（年表は次の決算で加入から書き始める）。
                a.TournamentRecords ??= new List<TournamentRecord>();
                a.BossKills ??= new List<BossKillRecord>();
                a.PeakStats ??= new Dictionary<string, int>();
                a.JoinEstimate ??= "";
                a.Journal ??= new List<JournalEntry>();
                a.EarnedTitleIds ??= new List<string>();
                a.MentorWeeks ??= new Dictionary<Guid, int>();
                a.HallOfFameReason ??= "";
            }

            foreach (var record in data.CompatibilityPairs)
            {
                // CompatibilitySystem.NormalizeKeyと同じ正規化ルール（小さいGuid,大きいGuid）。
                // Modelsレイヤーの循環参照を避けるため、Systems層を参照せずここで直接計算する。
                var key = record.IdA.CompareTo(record.IdB) <= 0 ? (record.IdA, record.IdB) : (record.IdB, record.IdA);
                state.Compatibility[key] = record.Value;
            }

            foreach (var record in data.TrainingAssignments)
                state.TrainingAssignments[record.AdventurerId] = ParseEnum<FacilityType>(record.Facility, nameof(FacilityType));

            foreach (var kv in data.FacilityLevels)
            {
                var type = ParseEnum<FacilityType>(kv.Key, nameof(FacilityType));
                // 専門（§0.76）。施設の選択肢に無い記録は捨て、Lv4以上で専門が無ければ既定（GameState.GetFacilitySpecialty）で扱う。
                var specialty = data.FacilitySpecialties.TryGetValue(kv.Key, out var name) ? ParseEnum<FacilitySpecialty>(name, nameof(FacilitySpecialty)) : FacilitySpecialty.None;
                if (System.Array.IndexOf(FacilityBalance.GetSpecialtyOptions(type), specialty) < 0)
                    specialty = FacilitySpecialty.None;
                state.Facilities.Add(new Facility { Type = type, CurrentLevel = kv.Value, Specialty = specialty });
            }

            if (data.FacilityUnderConstruction != null)
            {
                state.UnderConstruction = new FacilityConstruction
                {
                    Type = ParseEnum<FacilityType>(data.FacilityUnderConstruction, nameof(FacilityType)),
                    TargetLevel = data.ConstructionTargetLevel,
                    WeeksRemaining = data.ConstructionWeeksRemaining,
                    TargetSpecialty = data.ConstructionTargetSpecialty != null ? ParseEnum<FacilitySpecialty>(data.ConstructionTargetSpecialty, nameof(FacilitySpecialty)) : FacilitySpecialty.None,
                    IsRemodel = data.ConstructionIsRemodel,
                };
            }

            foreach (var kv in data.AdvisorAssignments)
            {
                var facilityType = ParseEnum<FacilityType>(kv.Key, nameof(FacilityType));
                if (facilityType == FacilityType.WarRoom)
                    state.AssignedAdvisor = kv.Value;
                else if (facilityType == FacilityType.RecruitmentOffice)
                    state.AssignedScoutMaster = kv.Value;
                else
                    state.AssignedTrainers[facilityType] = kv.Value;
            }

            // 教官の在任週数・重点伝授特性（教官深化 Step 3）。旧セーブには無いので空＝在任0週・自動のまま読む。
            // 教官のいない施設の記録は捨てる（任命が外れた記録が残らないように）。
            foreach (var kv in data.TrainerTenureWeeks)
            {
                var facilityType = ParseEnum<FacilityType>(kv.Key, nameof(FacilityType));
                if (state.AssignedTrainers.TryGetValue(facilityType, out var trainerId) && trainerId != null)
                    state.TrainerTenureWeeks[facilityType] = Math.Max(0, kv.Value);
            }
            foreach (var kv in data.TrainerFocusTraits)
            {
                var facilityType = ParseEnum<FacilityType>(kv.Key, nameof(FacilityType));
                if (state.AssignedTrainers.TryGetValue(facilityType, out var trainerId) && trainerId != null
                    && !string.IsNullOrEmpty(kv.Value))
                    state.TrainerFocusTraits[facilityType] = kv.Value;
            }

            // 訓練施設の特化（§0.75）。施設の扱う能力でない記録は捨てて「両方」に戻す。
            foreach (var kv in data.TrainingSpecialtyStats)
            {
                var facilityType = ParseEnum<FacilityType>(kv.Key, nameof(FacilityType));
                if (FacilityBalance.IsTrainingFacility(facilityType) && FacilityBalance.GetTrainingTargetStats(facilityType).Contains(kv.Value))
                    state.TrainingSpecialtyStats[facilityType] = kv.Value;
            }

            // 大迷宮へ出撃中の部隊の復元：同一のAdventurerインスタンスを使い回すため
            // （派遣中メンバーの状態変化が現役ロースター側にも同じインスタンスとして
            // 反映されるよう）、Id→Adventurerの参照辞書を1つ作ってから引く。
            var adventurersById = state.Adventurers
                .Concat(state.RetiredAdventurers)
                .Concat(state.FallenAdventurers)
                .ToDictionary(a => a.Id);

            // 大迷宮への出撃（→ ActiveDungeonMission）。メンバーは
            // 同一インスタンスを引き、ボスも各DungeonField.Bosses内の同一インスタンスを指すよう解決する。
            foreach (var record in data.DungeonMissions)
            {
                // ボスを対象にする出撃（調査・討伐）はボスIdから所属フィールドを逆引きする
                // （→ FieldId未保存の旧セーブとの互換）。採取（Gathering）はボスを持たないため
                // FieldIdから直接引く。
                DungeonField field;
                FloorBoss? boss = null;

                if (record.BossId.HasValue)
                {
                    boss = state.DungeonFields.SelectMany(f => f.Bosses).FirstOrDefault(b => b.Id == record.BossId.Value)
                        ?? throw new FormatException($"セーブデータが破損しています：出撃先の階層ボスId {record.BossId} が見つかりません。");
                    field = state.DungeonFields.FirstOrDefault(f => f.Bosses.Contains(boss))
                        ?? throw new FormatException($"セーブデータが破損しています：階層ボス「{boss.Name}」の所属フィールドが見つかりません。");
                }
                else
                {
                    field = state.DungeonFields.FirstOrDefault(f => f.Id == record.FieldId)
                        ?? throw new FormatException($"セーブデータが破損しています：出撃先のフィールドId「{record.FieldId}」が見つかりません。");
                }

                var party = new Party();
                foreach (var memberId in record.PartyMemberIds)
                {
                    if (!adventurersById.TryGetValue(memberId, out var member))
                        throw new FormatException($"セーブデータが破損しています：大迷宮出撃中のメンバーId {memberId} が見つかりません。");
                    party.TryAdd(member);
                }
                var missionType = ParseEnum<DungeonMissionType>(record.MissionType, nameof(DungeonMissionType));

                // 複数週潜行の状態（→ ActiveDungeonMission.Status等）。項目の無い旧セーブは、
                // 討伐＝扉前から決戦へ臨む状態、それ以外＝1階層から進軍する状態として復元する。
                var status = string.IsNullOrEmpty(record.Status)
                    ? (missionType == DungeonMissionType.BossAssault ? ExpeditionStatus.EngagingBoss : ExpeditionStatus.Advancing)
                    : ParseEnum<ExpeditionStatus>(record.Status, nameof(ExpeditionStatus));
                FloorBoss? targetedBoss = null;
                if (record.TargetedBossId.HasValue)
                {
                    targetedBoss = field.Bosses.FirstOrDefault(b => b.Id == record.TargetedBossId.Value)
                        ?? throw new FormatException($"セーブデータが破損しています：扉前の階層ボスId {record.TargetedBossId} が見つかりません。");
                }
                else if (status == ExpeditionStatus.EngagingBoss)
                {
                    targetedBoss = boss;
                }

                state.ActiveDungeonMissions.Add(new ActiveDungeonMission
                {
                    Party = party,
                    Field = field,
                    Boss = boss,
                    MissionType = missionType,
                    Status = status,
                    CurrentFloor = record.CurrentFloor > 0 ? record.CurrentFloor : (targetedBoss?.Floor ?? 1),
                    TargetedBoss = targetedBoss,
                    WeeksElapsed = record.WeeksElapsed,
                    CarriedGold = record.CarriedGold,
                    CarriedMaterials = new Dictionary<string, int>(record.CarriedMaterials ?? new Dictionary<string, int>()),
                    SavedPartyId = record.SavedPartyId,
                });
            }

            return state;
        }

        private static TEnum ParseEnum<TEnum>(string value, string enumTypeName) where TEnum : struct, Enum
        {
            if (Enum.TryParse<TEnum>(value, out var result))
                return result;
            throw new FormatException($"セーブデータが破損しています：'{value}' は有効な{enumTypeName}ではありません。");
        }

        /// <summary>
        /// 8施設の初期状態（→ 03 §6。§0.75で訓練施設を3つにして8施設）。v1.4改訂：宿舎・医務室・ギルド酒場（基幹3施設）は
        /// 既存どおりLv1スタート、それ以外（訓練施設・作戦資料室・冒険者支援室）は
        /// Lv0（未建設）スタートに変更した。Lv0の施設は訓練枠・顧問スロットが0扱いになる
        /// （→ FacilityBalance.GetTrainingSlotCapacity・AdvisorSystemの各Try*Assign*）。
        /// </summary>
        private static List<Facility> CreateDefaultFacilities() => new()
        {
            new Facility { Type = FacilityType.Dormitory, CurrentLevel = 1 },
            new Facility { Type = FacilityType.Infirmary, CurrentLevel = 1 },
            new Facility { Type = FacilityType.Tavern, CurrentLevel = 1 },
            new Facility { Type = FacilityType.WarRoom, CurrentLevel = 0 },
            new Facility { Type = FacilityType.DrillHall, CurrentLevel = 0 },
            new Facility { Type = FacilityType.Academy, CurrentLevel = 0 },
            new Facility { Type = FacilityType.SkillHall, CurrentLevel = 0 },
            new Facility { Type = FacilityType.RecruitmentOffice, CurrentLevel = 0 },
        };
    }
}
