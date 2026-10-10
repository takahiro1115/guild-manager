using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;
using GuildManager.Core.Systems;

/// <summary>
/// Phase 2 最小UI。「編成→週送り→結果→資金」の輪を一周させるためだけの画面。
/// 満足度・施設等はまだ扱わない（→ docs/06_タスクリスト.md Phase 3以降）。
///
/// 既知の割り切り（MVPの簡略化）：
///  - 週送りのたびにクエスト一覧・冒険者一覧の選択状態はリセットされる（毎週選び直す）。
///  - 重傷（出撃不可）の冒険者も選択自体は止めていない（ラベルで表示するのみ）。
/// </summary>
public partial class MainDashboard : Control
{
	private GameState _state = null!;
	private EconomySystem _economySystem = null!;
	private InjuryRecoverySystem _injuryRecoverySystem = null!;
	private AgingSystem _agingSystem = null!;
	private GrowthSystem _growthSystem = null!;
	private RestRecoverySystem _restRecoverySystem = null!;
	private TrainingSystem _trainingSystem = null!;
	private RecruitmentSystem _recruitmentSystem = null!;
	private SatisfactionSystem _satisfactionSystem = null!;
	private CompatibilitySystem _compatibilitySystem = null!;
	private FacilitySystem _facilitySystem = null!;
	private MasterMoodSystem _masterMoodSystem = null!;
	private DefeatSystem _defeatSystem = null!;
	private AdvisorSystem _advisorSystem = null!;
	private EquipmentSystem _equipmentSystem = null!;
	private SaveLoadService _saveLoadService = null!;
	private PartyFormationSystem _partyFormationSystem = null!;
	private WeekProcessingSystem _weekProcessingSystem = null!;
	private AutoSkipService _autoSkipService = null!;
	private SquadOrderSystem _squadOrderSystem = null!;
	private CommissionSystem _commissionSystem = null!;

	/// <summary>依頼が届いた週に採用試験が重なったとき、採用試験を閉じてから依頼掲示板を開く（§0.64）。</summary>
	private bool _openCommissionsAfterRecruitment;

	private Label _weekLabel = null!;
	private Label _goldLabel = null!;
	private Label _rankLabel = null!;

	/// <summary>同時出撃枠の使用状況（→ コアシステム刷新仕様「4. 進行管理」）。</summary>
	private Label _squadSlotLabel = null!;

	// ヘッダー右肩にあった素材保有数サマリー（%MaterialSummaryLabel）は2026年9月に撤去した。
	// 全素材を1行に並べる折り返し無しのLabelだったため、ヘッダーの最小幅が画面幅を超え、
	// ルートコンテナごと横へはみ出してセーブボタンや大迷宮画面の左端が見切れていた（→ 03 §9）。
	// 素材の確認は「倉庫・遺物」画面の採取素材一覧（→ §4.7.4）へ集約済み。

	private ItemList _adventurerList = null!;
	private RichTextLabel _adventurerDetailLabel = null!;
	private TextureRect _portraitTextureRect = null!;
	/// <summary>お知らせ（§0.80：右の週報の欄を撤去し、その場の操作の結果は画面下に数秒だけ出す）。</summary>
	private VBoxContainer _toastBox = null!;
	private Button _autoSkipButton = null!;
	private RecruitmentPopup _recruitmentPopup = null!;
	private AdvisorPopup _advisorPopup = null!;
	private EquipmentPopup _equipmentPopup = null!;
	private CommissionPopup _commissionPopup = null!;

	// ---- メニューナビゲーションバー & ペイン制御 ----
	private enum DashboardView
	{
		Dungeon = 0,

		/// <summary>部隊・冒険者（部隊編成と個人詳細の2ペイン。2026年9月に旧「冒険者人事」を統合、→ 03 §9）。</summary>
		Party = 1,
		Research = 2,
		Facility = 3,

		/// <summary>倉庫・遺物（未鑑定遺物の鑑定・採取素材・ギルド保管庫。→ 03 §4.7）。</summary>
		Warehouse = 4,

		/// <summary>システム（セーブ・将来のロード／環境設定。ナビ行の最右端。→ 03 §9・§12）。</summary>
		System = 5,

		/// <summary>お抱え商（武具の購入。タブはコードで末尾に足すため、ナビ行の並びとタブの番号は別。§0.67）。</summary>
		Shop = 6,

		/// <summary>大会（§0.82。タブはコードで末尾に足す。ナビ行では大迷宮の右に置く）。</summary>
		Tournament = 7,

		/// <summary>ギルドのホール（§0.95。タブはコードで末尾に足す。ナビ行では大迷宮の左に置く）。</summary>
		Hall = 8
	}

	private TabContainer _centerPanel = null!;
	private Button _navDungeonBtn = null!;
	private Button _navPartyBtn = null!;
	private Button _navResearchBtn = null!;
	private Button _navFacilityBtn = null!;
	private Button _navWarehouseBtn = null!;
	private Button _navShopBtn = null!;
	private ShopPanel _shopPanel = null!;
	private Button _navTournamentBtn = null!;
	private TournamentPanel _tournamentPanel = null!;
	private GuildHallPanel _hallPanel = null!;
	private Button _navHallBtn = null!;
	private TournamentSystem _tournamentSystem = null!;
	private TournamentResultPopup _tournamentResultPopup = null!;
	private HonorRecordPopup _honorRecordPopup = null!;
	private RetirementCeremonyPopup _retirementCeremonyPopup = null!;
	private StoryPopup _storyPopup = null!;
	private GuidePopup _guidePopup = null!;
	private Button _guideButton = null!;
	private Button _storyRecordButton = null!;
	private StoryRecordPopup _storyRecordPopup = null!;
	private Button _navSystemBtn = null!;

	// ---- 大迷宮（ダンジョン攻略システム：調査・討伐・採取。出撃の主画面） ----
	private DungeonPanel _dungeonPanel = null!;
	private DungeonExpeditionSystem _dungeonExpeditionSystem = null!;

	// ---- 冒険者管理（→ 2026年9月UI刷新で独立パネル化） ----
	private AdventurerPanel _adventurerPanel = null!;

	// ---- 編成・施設（旧ポップアップをタブ化） ----
	private PartyFormationPanel _partyFormationPanel = null!;
	private FacilityPanel _facilityPanel = null!;

	// ---- アルベールの研究室（素材投資システム） ----
	private ResearchPanel _researchPanel = null!;

	// ---- 倉庫・遺物（未鑑定遺物の鑑定・採取素材・ギルド保管庫。→ 03 §4.7） ----
	private InventoryPanel _inventoryPanel = null!;
	private AppraisalSystem _appraisalSystem = null!;

	// ---- システム（旧ヘッダーのセーブボタンを移設。将来のロード・環境設定の受け皿。→ 03 §9） ----
	private SystemPanel _systemPanel = null!;

	/// <summary>大迷宮へ1部隊も出撃予定が無いまま週を進めようとした時の確認ダイアログ。</summary>
	private ConfirmationDialog _noDungeonDispatchDialog = null!;

	public override void _Ready()
	{
		// 起動時はフルスクリーン。project.godot の window/size/mode=3（最初からフルスクリーンで作る）にすると、
		// 後から窓表示へ戻せない（Godot 4.7・Windows）ため、最大化（mode=2）で作ってからここで切り替える（→ SystemPanel）。
		GetWindow().Mode = Window.ModeEnum.Fullscreen;
		_weekLabel = GetNode<Label>("%WeekLabel");
		_goldLabel = GetNode<Label>("%GoldLabel");
		_rankLabel = GetNode<Label>("%RankLabel");
		_squadSlotLabel = GetNode<Label>("%SquadSlotLabel");
		BuildToastBox();
		_autoSkipButton = GetNode<Button>("%AutoSkipButton");
		_recruitmentPopup = GetNode<RecruitmentPopup>("%RecruitmentPopup");
		_advisorPopup = GetNode<AdvisorPopup>("%AdvisorPopup");
		_equipmentPopup = GetNode<EquipmentPopup>("%EquipmentPopup");
		_commissionPopup = GetNode<CommissionPopup>("%CommissionPopup");

		_centerPanel = GetNode<TabContainer>("%CenterPanel");
		_centerPanel.TabsVisible = false;

		_navDungeonBtn = GetNode<Button>("%NavDungeonBtn");
		_navPartyBtn = GetNode<Button>("%NavPartyBtn");
		_navResearchBtn = GetNode<Button>("%NavResearchBtn");
		_navFacilityBtn = GetNode<Button>("%NavFacilityBtn");
		_navWarehouseBtn = GetNode<Button>("%NavWarehouseBtn");
		_navShopBtn = GetNode<Button>("%NavShopBtn");
		_navSystemBtn = GetNode<Button>("%NavSystemBtn");

		// 大会（§0.82）のナビボタンはコードで足す（大迷宮の右）
		_navTournamentBtn = new Button { Text = "🏆 大会", ToggleMode = true, CustomMinimumSize = new Vector2(110, 36) };
		_navDungeonBtn.AddSibling(_navTournamentBtn);
		_navTournamentBtn.Pressed += () => SwitchView(DashboardView.Tournament);
		// ギルドのホール（§0.95）：ナビ行では大迷宮の左
		_navHallBtn = new Button { Text = "🏠 ホール", ToggleMode = true, CustomMinimumSize = new Vector2(110, 36) };
		_navDungeonBtn.GetParent().AddChild(_navHallBtn);
		_navDungeonBtn.GetParent().MoveChild(_navHallBtn, _navDungeonBtn.GetIndex());
		_navHallBtn.Pressed += () => SwitchView(DashboardView.Hall);
		_navDungeonBtn.Pressed += () => SwitchView(DashboardView.Dungeon);
		_navPartyBtn.Pressed += () => SwitchView(DashboardView.Party);
		_navResearchBtn.Pressed += () => SwitchView(DashboardView.Research);
		_navFacilityBtn.Pressed += () => SwitchView(DashboardView.Facility);
		_navWarehouseBtn.Pressed += () => SwitchView(DashboardView.Warehouse);
		_navShopBtn.Pressed += () => SwitchView(DashboardView.Shop);
		_navSystemBtn.Pressed += () => SwitchView(DashboardView.System);

		_dungeonPanel = GetNode<DungeonPanel>("%DungeonTab");
		_dungeonPanel.LogRequested += AppendLog;
		_dungeonPanel.StateChanged += RefreshAll;
		_dungeonPanel.CommissionsRequested += OpenCommissionPopup;

		_adventurerPanel = GetNode<AdventurerPanel>("%AdventurerDetailTab");
		_adventurerPanel.LogRequested += AppendLog;
		_adventurerPanel.StateChanged += RefreshAll;
		// 改名（→ 03 §2.1）：画面全体を再描画し、左ペインの編成スロット・候補一覧や大迷宮画面の名前を即時同期する。
		_adventurerPanel.AdventurerRenamed += _ => RefreshAll();
		_adventurerPanel.EquipmentRequested += OnAdventurerEquipmentRequested;

		_partyFormationPanel = GetNode<PartyFormationPanel>("%PartyFormationTab");
		_partyFormationPanel.StateChanged += RefreshAll;
		// 部隊・冒険者画面（→ 03 §9）：編成スロット・候補行のクリックで右ペインの個人詳細を切り替える。
		_partyFormationPanel.AdventurerSelected += id => _adventurerPanel.ShowAdventurer(id);

		_researchPanel = GetNode<ResearchPanel>("%ResearchTab");
		_researchPanel.LogRequested += AppendLog;
		_researchPanel.StateChanged += RefreshAll;

		_facilityPanel = GetNode<FacilityPanel>("%FacilityTab");
		_facilityPanel.StateChanged += RefreshAll;
		_facilityPanel.AdvisorRequested += OnAdvisorButtonPressed;

		_inventoryPanel = GetNode<InventoryPanel>("%InventoryTab");
		_inventoryPanel.LogRequested += AppendLog;
		_inventoryPanel.StateChanged += RefreshAll;

		_systemPanel = GetNode<SystemPanel>("%SystemTab");
		_systemPanel.SaveRequested += SaveProgress;
		_systemPanel.EndingRequested += ReplayEnding; // クリア後にエンディングを見直す（→ 03 §0.59。§0.89：都の心臓・祝宴・初出撃の場面から）
		_systemPanel.QuitRequested += ShowQuitPrompt; // ゲームの終了（§0.80でナビの「閉じる」から移した）
		_systemPanel.DebugAddGoldRequested += () =>
		{
			_state.Gold += 10000;
			AppendLog($"[color=gray]🛠 デバッグ：所持金を10,000G増やした（現在 {_state.Gold} G）。[/color]");
			RefreshAll();
		};

		SwitchView(DashboardView.Dungeon);

		_noDungeonDispatchDialog = new ConfirmationDialog
		{
			Title = "確認",
			DialogText = "方針のある部隊がいないので、誰も出撃しません。月を進めますか？",
			OkButtonText = "月を進める",
			CancelButtonText = "やめる",
		};
		// 確定直後に採用試験ポップアップ等を開くことがあるため、ダイアログが閉じ切ってから
		// 月送りを実行する（同一フレームで別の排他ウィンドウを開くとGodotがエラーにする）。
		_noDungeonDispatchDialog.Confirmed += () => Callable.From(AdvanceMonth).CallDeferred();
		AddChild(_noDungeonDispatchDialog);

		// 月報の小窓（§0.71）と、過去の月報を見返す【📅 月報】（ヘッダーの出撃枠の右）
		_monthlyReportPopup = new MonthlyReportPopup { Visible = false };
		AddChild(_monthlyReportPopup);
		_monthlyReportPopup.Closed += OnMonthlyReportClosed;
		_monthlyReportPopup.NextMonthRequested += () => Callable.From(OnNextMonthPressed).CallDeferred();
		_monthlyReportPopup.JumpRequested += OnMonthlyReportJump;
		var reportsButton = new Button { Text = "📅 月報", TooltipText = "直近12か月の月報を見返す" };
		reportsButton.Pressed += () => _monthlyReportPopup.ShowHistory(_state.MonthlyReports);
		_squadSlotLabel.GetParent().AddChild(reportsButton);
		_squadSlotLabel.GetParent().MoveChild(reportsButton, _squadSlotLabel.GetIndex() + 1);
		// ギルドの手引き（§0.87）：まだ済んでいないやることの数。押すと小窓
		_guideButton = new Button { Text = "📝 手引き", TooltipText = "ギルドの手引き：物語で教わった、今やること" };
		_guideButton.Pressed += () => _guidePopup.Open(_state);
		_squadSlotLabel.GetParent().AddChild(_guideButton);
		_squadSlotLabel.GetParent().MoveChild(_guideButton, reportsButton.GetIndex() + 1);
		// 物語の記録（§0.92）：見た場面を見返す。場面を1つも見ていなければ隠す
		_storyRecordButton = new Button { Text = "📖 物語", TooltipText = "物語の記録：これまでに見た場面を、もう一度読む" };
		_storyRecordButton.Pressed += () => _storyRecordPopup.Open(_state);
		_squadSlotLabel.GetParent().AddChild(_storyRecordButton);
		_squadSlotLabel.GetParent().MoveChild(_storyRecordButton, _guideButton.GetIndex() + 1);

		// §0.70：月を1ターンにする。「次の月へ」（旧・自動スキップのボタン）が主な操作で、「1週進める」はデバッグ用（最終的に撤去）。
		_autoSkipButton.Pressed += OnNextMonthPressed;
		_autoSkipButton.Text = "📅 次の月へ（Space）";
		_autoSkipButton.TooltipText = "今の月の残りを、方針どおりに進める。月の途中で決めることがあれば止まる";
		_recruitmentPopup.Closed += OnRecruitmentPopupClosed;
		_advisorPopup.Closed += OnAdvisorPopupClosed;
		_equipmentPopup.Closed += OnEquipmentPopupClosed;
		_commissionPopup.Closed += RefreshAll;
		_commissionPopup.LogRequested += AppendLog;

		// シード固定の乱数。同じシードなら毎回同じ結果になる（デバッグしやすくするため）。
		// 戦闘用と加齢用で別インスタンス・別シードにし、互いの抽選回数が結果に影響しないようにする。
		_economySystem = new EconomySystem();
		_injuryRecoverySystem = new InjuryRecoverySystem();
		_agingSystem = new AgingSystem(new SeededRng(99));
		_growthSystem = new GrowthSystem(new SeededRng(7));
		_restRecoverySystem = new RestRecoverySystem();
		_trainingSystem = new TrainingSystem(new SeededRng(831)); // 特性伝授ロール用（→ 特性伝授刷新仕様）
		_recruitmentSystem = new RecruitmentSystem(new SeededRng(2024));
		_satisfactionSystem = new SatisfactionSystem();
		_compatibilitySystem = new CompatibilitySystem(new SeededRng(2525));
		_facilitySystem = new FacilitySystem();
		_masterMoodSystem = new MasterMoodSystem();
		_defeatSystem = new DefeatSystem();
		_advisorSystem = new AdvisorSystem();
		_equipmentSystem = new EquipmentSystem();

		// お抱え商（武具の購入画面）：タブはコードで末尾に足す（番号＝DashboardView.Shop）
		_shopPanel = new ShopPanel();
		_shopPanel.Initialize(_equipmentSystem);
		_shopPanel.LogRequested += AppendLog;
		_shopPanel.StateChanged += RefreshAll;
		_centerPanel.AddChild(_shopPanel);
		// 大会（§0.82）：タブはコードで末尾に足す（番号＝DashboardView.Tournament）。ナビ行では大迷宮の右に置く
		_tournamentPanel = new TournamentPanel();
		_tournamentPanel.LogRequested += AppendLog;
		_tournamentPanel.StateChanged += RefreshAll;
		_centerPanel.AddChild(_tournamentPanel);
		// ギルドのホール（§0.95）：タブはコードで末尾に足す（番号＝DashboardView.Hall）
		_hallPanel = new GuildHallPanel();
		_hallPanel.AreaPressed += OnHallAreaPressed;
		_centerPanel.AddChild(_hallPanel);
		_tournamentResultPopup = new TournamentResultPopup { Visible = false };
		AddChild(_tournamentResultPopup);
		// 戦績の頁と引退式（大会と育成の栄光 段2）
		_honorRecordPopup = new HonorRecordPopup { Visible = false };
		AddChild(_honorRecordPopup);
		_retirementCeremonyPopup = new RetirementCeremonyPopup { Visible = false };
		AddChild(_retirementCeremonyPopup);
		// 物語の会話の小窓（§0.86）
		_storyPopup = new StoryPopup { Visible = false };
		AddChild(_storyPopup);
		_storyPopup.JumpRequested += OnStoryJump;
		// ギルドの手引き（§0.87）
		_guidePopup = new GuidePopup { Visible = false };
		AddChild(_guidePopup);
		_guidePopup.JumpRequested += OnStoryJump;
		// 物語の記録（§0.92）
		_storyRecordPopup = new StoryRecordPopup { Visible = false };
		AddChild(_storyRecordPopup);
		_storyRecordPopup.SceneRequested += ReplayStoryScene;
		_tournamentPanel.HonorRecordRequested += a => _honorRecordPopup.Open(_state, a);
		_adventurerPanel.HonorRecordRequested += a => _honorRecordPopup.Open(_state, a);
		_adventurerPanel.RetirementCeremonyRequested += a => Callable.From(() => _retirementCeremonyPopup.ShowCeremonies(_state, new[] { a })).CallDeferred();
		_partyFormationSystem = new PartyFormationSystem();
		_partyFormationPanel.Initialize(_partyFormationSystem);
		_facilityPanel.Initialize(_facilitySystem);
		// 鑑定（→ 03 §4.7）。他の判定と別シードにし、互いの抽選回数が結果に影響しないようにする。
		_appraisalSystem = new AppraisalSystem(new SeededRng(4649));
		_inventoryPanel.Initialize(_appraisalSystem, _economySystem);
		// 大迷宮（→ ダンジョン攻略システム）。調査・討伐は別シードにし、互いの抽選回数が結果に
		// 影響しないようにする。強制除籍の余波（満足度・相性）は既存インスタンスを共有する。
		_dungeonExpeditionSystem = new DungeonExpeditionSystem(
			new ScoutingResolver(new SeededRng(1453)), new DungeonResolver(new SeededRng(1588)),
			_satisfactionSystem, _compatibilitySystem);
		_dungeonPanel.Initialize(_dungeonExpeditionSystem);
		_adventurerPanel.Initialize(_satisfactionSystem, _agingSystem);
		// 魂魄融和の秘薬（→ 03 §5.4・§0.58）。子の能力・特性の抽選は他と別シードにする。
		_researchPanel.Initialize(new SoulFusionSystem(new SeededRng(1123)));
		// 週次決算のオーケストレーション（→ 03 §1.3・自動スキップ）。既存の各Systemインスタンスを
		// そのまま共有し、二重管理（別インスタンスによる状態不整合）を避ける。
		// 依頼と迷宮の異変（→ 03 §4.9・§4.10・§0.64）。依頼の生成・報酬の遺物の抽選は他と別シードにする。
		_commissionSystem = new CommissionSystem(new SeededRng(2718));
		// 大会（§0.82）。組み合わせ・試合の運・相手の名前は他と別シードにする。
		_tournamentSystem = new TournamentSystem(new SeededRng(3373));
		_weekProcessingSystem = new WeekProcessingSystem(
			_masterMoodSystem, _economySystem, _trainingSystem, _injuryRecoverySystem,
			_restRecoverySystem, _growthSystem, _satisfactionSystem, _agingSystem,
			_facilitySystem, _defeatSystem, _recruitmentSystem,
			_dungeonExpeditionSystem, _commissionSystem, _tournamentSystem);
		// 部隊の方針（自動出撃、→ 03 §4.0.3・§0.63）。週送り・自動スキップの決算の前に実行する。
		_squadOrderSystem = new SquadOrderSystem(_dungeonExpeditionSystem);
		_autoSkipService = new AutoSkipService(_weekProcessingSystem, _squadOrderSystem);
		// GuildManager.CoreはGodotに依存しない方針（→ 05技術メモ）のため、保存先の実パスは
		// Godot側からOS.GetUserDataDir()（user://に対応する実ディレクトリ）を注入する（→ 03 §12）。
		_saveLoadService = new SaveLoadService(OS.GetUserDataDir());

		if (_saveLoadService.SaveFileExists())
			ShowContinueOrNewGamePrompt();
		else
			StartNewGame();
	}

	/// <summary>新規ゲームとして開始する（既存の初期化処理。→ 03 §12「セーブが無ければ新規ゲーム」）。</summary>
	private void StartNewGame()
	{
		// 物語と手ほどき（§0.88）：一度クリアしたことがあれば「あり／なし」を選んでもらう。初回のプレイは必ず「あり」。
		if (!PlayerProfile.ClearedOnce)
		{
			BeginNewGame(tutorial: true);
			return;
		}
		var dialog = new AcceptDialog
		{
			Title = "新しいゲーム",
			DialogText = "物語と手ほどきをどうしますか？\n\n" +
				"あり：操作を教える場面とギルドの手引きを出し、画面は物語で教わるまで隠す（初めて遊んだときと同じ）。\n" +
				"なし：画面を初めから全部見せ、手引きと操作を教える場面を出さない。物語の山場（序章・ルミナ・イザベラなど）は出る。",
			OkButtonText = "あり（物語と手ほどき）",
		};
		dialog.AddButton("なし（物語の山場だけ）", true, "none");
		dialog.Confirmed += () => CloseDialogThenRun(dialog, () => BeginNewGame(tutorial: true));
		dialog.Canceled += () => CloseDialogThenRun(dialog, () => BeginNewGame(tutorial: true));
		dialog.CustomAction += action =>
		{
			if (action == "none")
				CloseDialogThenRun(dialog, () => BeginNewGame(tutorial: false));
		};
		AddChild(dialog);
		dialog.PopupCentered();
	}

	private void BeginNewGame(bool tutorial)
	{
		// 旧通常クエスト（掲示板・受託依頼）は2026年9月、大迷宮への完全一本化で撤去した。
		// 出撃導線は大迷宮タブのみ。
		_state = new GameState
		{
			Adventurers = SampleData.CreateStarterAdventurers(),
			DungeonFields = SampleData.CreateDefaultFields(),
		};
		if (!tutorial)
			StorySystem.DisableTutorial(_state);

		RefreshAll();

		if (_recruitmentSystem.IsInitialDraftWeek(_state.WeekNumber))
		{
			// 第1週の新春ドラフト（→ 03 §2.4、2026年9月）。初期固定メンバー3名に欠けている4職
			// （魔導士・学者・騎士・盗賊）を含む候補から2名を契約金0Gで採用し、職業ごとの初期装備を
			// 着た状態で加入させて計5名体制（4人出撃＋1人待機お手伝い）にする。
			// 2名の採用が済むまでポップアップは閉じず、次週へも進めさせない。
			DisableWeekAdvancement();
			// 序章（§0.86）：助けた女性たち＝ドラフトの候補。出会いの場面を見せてから選んでもらう。
			ShowStory(StoryTiming.Interactive, () => _recruitmentPopup.OpenDraft(_state, _recruitmentSystem));
		}
	}

	/// <summary>
	/// 起動時、セーブファイルが存在する場合に表示する「続きから／新規ゲーム」の選択ダイアログ
	/// （→ 03 §12・タイトル画面が無いMVPのため起動直後にモーダルで割り込む）。
	/// </summary>
	private void ShowContinueOrNewGamePrompt()
	{
		var dialog = new ConfirmationDialog
		{
			DialogText = "セーブデータが見つかりました。続きから再開しますか？",
			OkButtonText = "続きから",
			CancelButtonText = "新規ゲーム",
			Title = "確認",
			MinSize = new Vector2I(640, 220),
		};
		// ダイアログは別ウィンドウのためテーマが届かず既定の小さな文字になる。読みやすい大きさを直接指定する。
		const int dialogFontSize = 22;
		dialog.GetLabel().AddThemeFontSizeOverride("font_size", dialogFontSize);
		dialog.GetOkButton().AddThemeFontSizeOverride("font_size", dialogFontSize);
		dialog.GetCancelButton().AddThemeFontSizeOverride("font_size", dialogFontSize);
		dialog.GetOkButton().CustomMinimumSize = new Vector2(150, 48);
		dialog.GetCancelButton().CustomMinimumSize = new Vector2(150, 48);
		dialog.AddThemeFontSizeOverride("title_font_size", dialogFontSize);
		dialog.Confirmed += () => OnContinueChosen(dialog);
		dialog.Canceled += () => OnNewGameChosen(dialog);
		AddChild(dialog);
		dialog.PopupCentered();
	}

	/// <summary>システム画面の「ゲームを終了」：確認のうえゲームを終了する（フルスクリーンでは窓の×が使えないため）。</summary>
	private void ShowQuitPrompt()
	{
		var dialog = new ConfirmationDialog
		{
			DialogText = "ゲームを終了しますか？\n（保存していない進行状況は失われます）",
			OkButtonText = "終了する",
			CancelButtonText = "キャンセル",
			Title = "確認",
			MinSize = new Vector2I(640, 220),
		};
		const int dialogFontSize = 22;
		dialog.GetLabel().AddThemeFontSizeOverride("font_size", dialogFontSize);
		dialog.GetOkButton().AddThemeFontSizeOverride("font_size", dialogFontSize);
		dialog.GetCancelButton().AddThemeFontSizeOverride("font_size", dialogFontSize);
		dialog.GetOkButton().CustomMinimumSize = new Vector2(150, 48);
		dialog.GetCancelButton().CustomMinimumSize = new Vector2(150, 48);
		dialog.AddThemeFontSizeOverride("title_font_size", dialogFontSize);
		dialog.Confirmed += () => GetTree().Quit();
		dialog.Canceled += dialog.QueueFree;
		AddChild(dialog);
		dialog.PopupCentered();
	}

	private void OnContinueChosen(ConfirmationDialog dialog)
	{
		var loaded = _saveLoadService.Load();
		if (loaded == null)
		{
			CloseDialogThenRun(dialog, ShowLoadFailedPrompt);
			return;
		}

		dialog.QueueFree();
		_state = loaded;
		// 大迷宮（5フィールド拡張）の実装前に作られたセーブにはフィールドが無い。
		// 攻略対象が空のままにならないよう補う。
		if (_state.DungeonFields.Count == 0)
			_state.DungeonFields = SampleData.CreateDefaultFields();
		if (_state.IsGameCleared)
			PlayerProfile.MarkCleared(); // クリア済みのセーブ（§0.88：次の新しいゲームで手ほどきを選べる）
		RefreshAll();
		AppendLog($"[color=cyan]セーブデータから再開しました（{GameCalendar.Format(_state.WeekNumber)}）。[/color]");
	}

	private void OnNewGameChosen(ConfirmationDialog dialog) => CloseDialogThenRun(dialog, StartNewGame);

	/// <summary>
	/// ロード失敗時（破損ファイル・バージョン不一致等）のフォールバック（→ 03 §12）。
	/// 新規ゲーム開始のみ選択でき、既存セーブは上書きしない（プレイヤーが新規ゲームを
	/// 選んで初めて次の自動保存で上書きされる）。
	/// </summary>
	private void ShowLoadFailedPrompt()
	{
		var dialog = new AcceptDialog
		{
			DialogText = "セーブデータの読み込みに失敗しました。新規ゲームを開始します。",
		};
		dialog.Confirmed += () => CloseDialogThenRun(dialog, StartNewGame);
		dialog.Canceled += () => CloseDialogThenRun(dialog, StartNewGame);
		AddChild(dialog);
		dialog.PopupCentered();
	}

	/// <summary>
	/// ダイアログを閉じ、シーンツリーから実際に外れてから後続処理を呼ぶ。
	/// QueueFree() は削除をフレーム末尾まで遅らせるため、同じフレームで採用ポップアップ等の
	/// 排他ウィンドウを開くと「既に排他的な子ウィンドウがある」エラーで開かず、
	/// DisableWeekAdvancement() 済みの「次週へ」が二度と有効にならなかった。
	/// </summary>
	private async void CloseDialogThenRun(Window dialog, Action followUp)
	{
		dialog.QueueFree();
		await ToSignal(dialog, Node.SignalName.TreeExited);
		followUp();
	}

	/// <summary>
	/// システム画面「💾 進行状況を保存」（手動保存。旧ヘッダーのセーブボタンを移設、→ 03 §9・§12）。
	/// 即座にセーブし、結果を週報ログにも残す。成否はシステム画面側のフィードバック表示に使う。
	/// </summary>
	private bool SaveProgress()
	{
		try
		{
			_saveLoadService.Save(_state);
		}
		catch (Exception e)
		{
			GD.PushError($"セーブに失敗しました: {e.Message}");
			AppendLog($"[color=red]セーブに失敗しました: {e.Message}[/color]");
			return false;
		}
		AppendLog("[color=lime]進行状況を保存しました。[/color]");
		return true;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		// Space は「次の月へ」（§0.70：月を1ターンにする）
		if (@event is InputEventKey { Pressed: true, Keycode: Key.Space } && !_autoSkipButton.Disabled)
		{
			OnNextMonthPressed();
			GetViewport().SetInputAsHandled();
		}
	}



	/// <summary>
	/// 新春採用試験ポップアップ（→ 03 §2.4・§9）が閉じた時のコールバック。
	/// 採用処理が確定したので「次週へ」を再度有効化し、ロスター・所持金の変化を反映する。
	/// </summary>
	private void OnRecruitmentPopupClosed()
	{
		EnableWeekAdvancement();
		RefreshAll();
		if (_openCommissionsAfterRecruitment)
		{
			// 採用試験と依頼の到着が同じ週（新年＝春のはじめ）：ポップアップが閉じ切ってから開く（排他ウィンドウの重なりを避ける）。
			_openCommissionsAfterRecruitment = false;
			Callable.From(OpenCommissionPopup).CallDeferred();
		}
	}

	// ==================== 物語の場面（§0.86） ====================

	/// <summary>
	/// その時機に出す物語の場面を会話の小窓で順に見せ、見終えたら then を呼ぶ（場面が無ければすぐ then）。
	/// 小窓が開いている間に呼ばれたら、then だけ呼ぶ（同じ場面を二重に出さない）。
	/// </summary>
	private void ShowStory(StoryTiming timing, Action then = null)
	{
		var due = _storyPopup.Visible ? new List<StoryShowing>() : StorySystem.DueScenes(_state, timing);
		if (due.Count == 0)
		{
			then?.Invoke();
			return;
		}
		void OnClosed()
		{
			_storyPopup.Closed -= OnClosed;
			RefreshAll();
			if (then != null)
				Callable.From(then).CallDeferred(); // 別の排他ウィンドウは閉じ切ってから開く
		}
		_storyPopup.Closed += OnClosed;
		_storyPopup.ShowScenes(_state, due);
	}

	/// <summary>物語の記録から場面を見返す（§0.92）：記録の小窓を閉じて会話の小窓で見せ、閉じたら記録の小窓へ戻る。見たことの記録は変えない。</summary>
	private void ReplayStoryScene(string sceneId)
	{
		if (_storyPopup.Visible)
			return;
		_storyRecordPopup.Hide();
		void OnClosed()
		{
			_storyPopup.Closed -= OnClosed;
			Callable.From(() => _storyRecordPopup.Open(_state)).CallDeferred();
		}
		_storyPopup.Closed += OnClosed;
		_storyPopup.ShowScenes(_state, new[] { StorySystem.Replay(_state, sceneId) }, replay: true);
	}

	/// <summary>
	/// 操作のあとの場面（部隊を組んだ・方針を付けたなど）：ほかの小窓が開いていない、月を進めている最中でないときだけ見る。
	/// 画面の再描画（RefreshAll）から呼ぶ。
	/// </summary>
	private void CheckInteractiveStory()
	{
		if (_state == null || _storyPopup.Visible || _monthlyReportSettlement != null || _recruitmentPopup.Visible || !_autoSkipButton.Visible || _autoSkipButton.Disabled)
			return;
		if (StorySystem.DueScenes(_state, StoryTiming.Interactive).Count > 0)
			Callable.From(() => ShowStory(StoryTiming.Interactive)).CallDeferred();
	}

	/// <summary>
	/// ギルドの手引きのボタン（まだ済んでいないやることの数。手引きが無ければ隠す）と、まだ教えていない画面のタブを隠す（§0.87）。
	/// 今見ている画面が隠れたら大迷宮へ戻す。
	/// </summary>
	private void RefreshGuideAndTabs()
	{
		var groups = GuideSystem.Groups(_state);
		int open = groups.Sum(g => g.Items.Count(i => !i.Done));
		_guideButton.Visible = groups.Count > 0;
		_storyRecordButton.Visible = _state.StorySeenWeeks.Count > 0;
		_guideButton.Text = open > 0 ? $"📝 手引き（{open}）" : "📝 手引き";
		_guideButton.Modulate = open > 0 ? new Color(1f, 0.95f, 0.6f) : Colors.White;
		if (_guidePopup.Visible)
			_guidePopup.Refresh();

		var tabs = new (DashboardView View, Button Btn, GuideTab Tab)[]
		{
			(DashboardView.Warehouse, _navWarehouseBtn, GuideTab.Warehouse),
			(DashboardView.Shop, _navShopBtn, GuideTab.Shop),
			(DashboardView.Research, _navResearchBtn, GuideTab.Research),
			(DashboardView.Tournament, _navTournamentBtn, GuideTab.Tournament),
			(DashboardView.Facility, _navFacilityBtn, GuideTab.Facility),
		};
		foreach (var (view, btn, tab) in tabs)
		{
			bool openTab = GuideSystem.IsTabOpen(_state, tab);
			btn.Visible = openTab;
			if (!openTab && _centerPanel.CurrentTab == (int)view)
				SwitchView(DashboardView.Dungeon);
		}
	}

	/// <summary>会話の小窓の【画面へ】：ボタンの文言から画面を選んで切り替える（小窓はそのまま）。</summary>
	/// <summary>ギルドのホール（§0.95）で押した場所の画面へ移る。テーブルは大迷宮（部隊の方針）、依頼の掲示板は依頼の小窓、施設の扉・家具は施設管理。</summary>
	private void OnHallAreaPressed(HallTarget target, int squadNo)
	{
		switch (target)
		{
			case HallTarget.Commission: OpenCommissionPopup(); return;
			case HallTarget.Squad or HallTarget.Dungeon: SwitchView(DashboardView.Dungeon); return;
			case HallTarget.Research: SwitchView(DashboardView.Research); return;
			case HallTarget.Shop: SwitchView(DashboardView.Shop); return;
			case HallTarget.Dorm: SwitchView(DashboardView.Party); return;
			default: SwitchView(DashboardView.Facility); return; // 作戦資料室・医務室・訓練所・冒険者支援室・酒場・帳簿
		}
	}

	private void OnStoryJump(string label)
	{
		DashboardView? view = label switch
		{
			_ when label.Contains("大会") => DashboardView.Tournament,
			_ when label.Contains("施設") => DashboardView.Facility,
			_ when label.Contains("研究室") => DashboardView.Research,
			_ when label.Contains("倉庫") => DashboardView.Warehouse,
			_ when label.Contains("大迷宮") || label.Contains("方針") => DashboardView.Dungeon,
			_ when label.Contains("部隊") || label.Contains("冒険者") => DashboardView.Party,
			_ => null,
		};
		if (view is DashboardView v)
			SwitchView(v);
	}

	/// <summary>依頼掲示板を開く（依頼が届いた週は自動で、大迷宮画面の「📜 依頼掲示板」からはいつでも。§0.64）。</summary>
	private void OpenCommissionPopup()
	{
		if (_state == null) return;
		_commissionPopup.Open(_state, _commissionSystem);
	}

	/// <summary>
	/// 「次の月へ」を無効化する（→ 03 §1.3。§0.80で「（デバッグ）1週進める」は撤去）。ゲームオーバー時・
	/// 新春採用試験ポップアップ表示中・自動スキップ実行中など、週を進める操作全体を
	/// ブロックしたい場面でまとめて呼ぶ。
	/// </summary>
	private void DisableWeekAdvancement()
	{
		_autoSkipButton.Disabled = true;
	}

	/// <summary>「次の月へ」を再び有効化する（→ DisableWeekAdvancementの対）。</summary>
	private void EnableWeekAdvancement()
	{
		_autoSkipButton.Disabled = false;
	}

	/// <summary>
	/// 「（デバッグ）1週進める」ボタン（§0.70。最終的には撤去する）。確認ダイアログは出さない。
	/// </summary>
	/// <summary>
	/// 「次の月へ」ボタン（Spaceキー連動、§0.70：月を1ターンにする）。方針のある部隊が無く誰も出撃していなければ、
	/// うっかり月を進めないよう確認ダイアログを挟む。
	/// </summary>
	private void OnNextMonthPressed()
	{
		if (_noDungeonDispatchDialog.Visible)
			return;

		if (_state.ActiveDungeonMissions.Count == 0 && !_state.SavedParties.Any(p => p.Order != SquadOrder.None))
		{
			_noDungeonDispatchDialog.PopupCentered();
			return;
		}

		AdvanceMonth();
	}

	/// <summary>
	/// 週次決算の直後に割り込ませる処理（ゲームオーバー確定・新春採用試験）。
	/// 月報の小窓を閉じた後に呼ばれる（→ OnMonthlyReportClosed）。
	/// </summary>
	private void HandlePostSettlementInterruptions(WeeklySettlementResult settlement)
	{
		if (settlement.Flags.GameCleared)
		{
			// クリア（→ 03 §8.2・§0.59）：エンディングを先に見せ、閉じてから残りの割り込み（採用試験など）へ進む。
			settlement.Flags.GameCleared = false;
			DisableWeekAdvancement();
			// 都の心臓・祝宴・初出撃（§0.89）を見せてから、締めの語りとギルドの記録の窓
			ShowStory(StoryTiming.Ending, () =>
			{
				if (PostGameSystem.Lumina(_state) is { } lumina) // 初出撃のあと、ルミナが名簿に加わった（§0.90）
					AppendLog($"[color=lime][b]🏹 {lumina.Name}が冒険者の名簿に加わった。[/b][/color][color=gray]（斥候。年を取らず、引退しない）[/color]");
				ShowEnding(() => HandlePostSettlementInterruptions(settlement));
			});
			return;
		}

		if (settlement.Flags.DefeatOccurred)
		{
			DisableWeekAdvancement(); // ゲームオーバー：これ以上週を進められない
		}
		else if (settlement.Flags.RecruitmentTrialOccurred)
		{
			// 新春採用試験（2年目以降の新年第1週のみ）。ポップアップが閉じるまで次週へ進めさせない（→ 03 §9）。
			// 春のはじめは依頼も届くので、採用試験を閉じた後に依頼掲示板を開く（§0.64）。
			_openCommissionsAfterRecruitment = settlement.Flags.CommissionsOffered;
			DisableWeekAdvancement();
			_recruitmentPopup.Open(_state, _recruitmentSystem);
		}
		else if (_state.DefeatReason == null)
		{
			// 割り込みが何も無かった場合の復帰。決戦ログのステップ再生中は週送りを
			// 止めているため、ここで戻さないとボタンが無効のまま操作不能になる
			// （敗北確定後だけは、意図的に無効のまま据え置く）。
			EnableWeekAdvancement();
			// 季節のはじめに依頼が届いた（§0.64）：受けるかどうかを選んでもらう（閉じても週は進められる）。
			if (settlement.Flags.CommissionsOffered)
				OpenCommissionPopup();
		}
	}

	/// <summary>
	/// 週次決算処理（WeekProcessingSystem.ProcessWeek）の結果を週報ログに反映する。
	/// 手動の「次週へ」・自動スキップ（結果サマリー表示前の詳細ログとして）の両方から呼ばれる
	/// 共通ログ処理（→ 03 §1.3）。
	/// </summary>
	private void LogWeeklySettlement(WeeklySettlementResult settlement)
	{
		int weekNumber = settlement.Flags.Week;

		// 大迷宮への出撃（潜行・迷宮調査・採取・ボス討伐）の結果と、それに伴う出撃成長
		// （→ DungeonExpeditionSystem・GrowthSystem.ApplyExpeditionGrowth）。
		foreach (var dungeonResolution in settlement.DungeonMissionResolutions)
		{
			LogDungeonMission(weekNumber, dungeonResolution);
			LogGrowthEvents(dungeonResolution.GrowthEvents);
			// 障害特性（古傷・トラウマ）の付与と、それに伴う通常特性の侵食（→ 03 §5.3.2・§0.33 完全開示）。
			// 巨獣狩りの後天開眼（→ 03 §4.5.4・§0.35）は良い出来事なので金色・✨で分ける。
			foreach (var grant in dungeonResolution.TraitGrantEvents)
				AppendLog(grant.Cause == TraitGrantCause.Awakening
					? $"[color=gold]✨ {grant.ToLogText()}[/color]"
					: $"[color=orange]⚠ {grant.ToLogText()}[/color]");
			// 負傷（→ 03 §4.3・§0.53）：ボス戦の重傷は出撃不可、道中・調査・採取の軽傷は能力値が下がる。
			foreach (var injury in dungeonResolution.InjuryEvents)
				AppendLog(injury.Severity == InjurySeverity.Severe
					? $"[color=red]🩹 {injury.Name} が重傷を負った（全治{injury.Weeks}週・出撃不可。医務室で早く治る）。[/color]"
					: $"[color=orange]🩹 {injury.Name} が軽傷を負った（全治{injury.Weeks}週。治るまで能力値−{CombatBalance.LightInjuryStatPenaltyRate * 100:0}%）。[/color]");
		}

		LogCommissions(settlement); // → 03 §4.9・§4.10・§0.64：依頼の達成・失敗（機嫌の内訳より先に）
		LogMasterMood(settlement.MoodReport); // → 03 §8.1・§8.1.1：マスターの機嫌の変動内訳

		// 研究の手伝い（→ 03 §8.1・§0.73）：待機中にアルベールの研究を手伝った冒険者を1行にまとめる（毎週出るので短く）。
		if (settlement.IdleHelpEntries.Count > 0)
		{
			int credit = settlement.IdleHelpEntries.Sum(h => h.Credit);
			// 怠け者（→ 03 §0.56）：手伝わずにだらけていたが、マスターの話し相手にはなった。
			var lazy = settlement.IdleHelpEntries.Where(h => h.Credit == 0).Select(h => h.Name).ToList();
			AppendLog($"[color=lime]☕ 研究の手伝い：{string.Join("・", settlement.IdleHelpEntries.Select(h => h.Name))}（+{credit}G 分・貯まり {_state.ResearchCredit}G、機嫌 +{settlement.IdleHelpEntries.Count * MasterMoodBalance.IdleAdventurerHelpMood}）" +
				(lazy.Count > 0 ? $"[color=gray]　※{string.Join("・", lazy)}はだらけて手伝わなかった[/color]" : "") + "[/color]");
		}
		if (settlement.SelfTrainerCount > 0)
			AppendLog($"[color=lime]🏃 自主練：{settlement.SelfTrainerCount}名[/color]");

		// アルベールの市販薬・内職売上（→ 03 §8.1。4週に1回、機嫌に応じた倍率。旧・月次助成金）。
		if (settlement.SideJobIncome != null)
		{
			var income = settlement.SideJobIncome;
			// 基本額＝初期基本額＋内職強化研究（→ ResearchEffectType.SideBusinessGoldBonus）。
			AppendLog($"[color=lime]【アルベールの内職】市販薬売上: +{income.FinalGold}G " +
				$"(基本 {income.BaseGold}G［初期{income.InitialBaseGold}+研究{income.ResearchBonus}{(income.TradeBonus > 0 ? $"+商い{income.TradeBonus}" : "")}］ × 機嫌倍率 {income.Multiplier:F1}［{MoodTierLabel(income.Tier)}］)[/color]");
		}

		LogGrowthEvents(settlement.TrainingGrowthEvents); // → 03 §3.1〜3.4：成長トリガー経路2（訓練場配置）
		LogGrowthEvents(settlement.SelfTrainingGrowthEvents); // → §0.73：自主練
		// 教官からの特性伝授（奥義継承、→ 03 §7.1・§0.34）。
		foreach (var transmission in settlement.TraitTransmissionEvents)
			AppendLog($"[color=gold]📜 {transmission.ToLogText()}[/color]");
		// 任務の外で付いた後天の障害（燃え尽き、→ 03 §5.3.2・§0.56）。
		foreach (var grant in settlement.TraitGrantEvents)
			AppendLog($"[color=orange]⚠ {grant.ToLogText()}[/color]");
		LogNegotiationStatus(settlement.NegotiationTerminated); // → 03 §5.2：契約交渉・退団

		// 魂魄融和の誕生（→ 03 §5.4・§0.58）。
		foreach (var birth in settlement.SoulFusionBirths)
			LogSoulFusionBirth(birth);

		// クリア（→ 03 §8.2・§0.59）。
		if (settlement.Flags.GameCleared)
			AppendLog($"[color=gold][font_size=24][b]🏆 深淵100Fを制覇した！ 最後の生体コードがアルベールの手に渡った。[/b][/font_size][/color]");


		LogCommissionArrivals(settlement.Arrivals); // → §0.64：新しい週に届いた依頼・異変の予告・期限の近い依頼

		// 敗北条件判定（→ 03 §8.3）：副官解雇（マスターの機嫌0、猶予なし即時敗北）／
		// 破産（所持金マイナス4週連続、猶予あり）。期限による敗北は無い。
		if (settlement.NewDefeatReason != null)
		{
			string reasonLabel = settlement.NewDefeatReason switch
			{
				DefeatReason.DismissedByMaster => "副官解雇",
				DefeatReason.Bankruptcy => "破産",
				_ => "治安崩壊",
			};
			AppendLog($"[color=red][font_size=24][b]■■■ ゲームオーバー：{reasonLabel} ■■■[/b][/font_size][/color]");
			AppendLog(settlement.NewDefeatReason switch
			{
				DefeatReason.DismissedByMaster => "[color=red]マスターの機嫌が0に達した。「退屈なギルドに副官は要らん」――あなたはアルベールに解雇された。[/color]",
				DefeatReason.Bankruptcy => "[color=red]所持金マイナスが4週連続で解消されませんでした。[/color]",
				_ => "[color=red]脅威度が100%に到達し、街の治安が崩壊しました。[/color]",
			});
		}
	}

	/// <summary>
	/// マスターの機嫌の週次変動を週報に開示する（→ 03 §8.1・§8.1.1、§4.2.3 特記事項）。
	/// 成果ゼロの週は退屈減衰を明記する。
	/// </summary>
	private void LogMasterMood(MasterMoodReport report)
	{
		string reasons = report.Entries.Count == 0
			? "変動なし"
			: string.Join("、", report.Entries.Select(e =>
				e.Applied == e.Delta ? $"{e.Reason} {e.Delta:+0;-0;+0}" : $"{e.Reason} {e.Delta:+0;-0;+0}（上下限で{e.Applied:+0;-0;+0}）"));
		string color = report.Delta > 0 ? "lime" : report.Delta < 0 ? "orange" : "gray";
		var moodTier = MasterMoodSystem.GetTier(report.MoodAfter);
		AppendLog($"[color={color}]【マスターの機嫌】現在: {report.MoodAfter}/100 (今週の変動: {report.Delta:+0;-0;+0} / {reasons})" +
			$"［{MoodTierLabel(moodTier)}］[/color] [color=cyan]アルベール{MasterMoodSystem.GetAlbertLine(moodTier)}[/color]");
		if (report.Bored)
		{
			AppendLog($"[color=orange]大迷宮での成果がなく、アルベールは退屈して機嫌を損ねている (機嫌 -{MasterMoodBalance.BoredomMoodDecay})" +
				$"〔成果なし {report.WeeksSinceLastGuildActivity}週連続〕[/color]");
		}
	}

	/// <summary>依頼の達成・失敗を週報に記録する（→ CommissionSystem.ProcessSettlement、§0.64）。</summary>
	private void LogCommissions(WeeklySettlementResult settlement)
	{
		foreach (var done in settlement.Commissions.Completed)
			foreach (var line in CommissionLog.CompletionLines(done))
				AppendLog(line);
		foreach (var failed in settlement.Commissions.Failed)
			AppendLog($"[color=orange]📜 {failed.ClientName}の依頼（{CommissionSystem.TypeLabel(failed.Commission.Type)}）を果たせなかった（{failed.Reason}）。" +
				$"マスターの機嫌 {failed.MoodApplied:+0;-0;+0}[/color]");
	}

	/// <summary>決算後の新しい週に届いたもの（→ CommissionSystem.ProcessNewWeek、§0.64）を週報に記録する。</summary>
	private void LogCommissionArrivals(CommissionArrivals arrivals)
	{
		if (arrivals.Offered.Count > 0)
			AppendLog($"[color=khaki][b]📜 {GameCalendar.Format(_state.WeekNumber)}：依頼が{arrivals.Offered.Count}件届いた（" +
				$"{string.Join("・", arrivals.Offered.Select(c => $"{CommissionSystem.ClientName(c)}の{CommissionSystem.TypeLabel(c.Type)}"))}）。" +
				$"大迷宮画面の「📜 依頼掲示板」で受けるか選ぶ（{CommissionBalance.MaxAccepted}件まで）。[/b][/color]");
		if (arrivals.AnnouncedAnomaly != null)
			AppendLog($"[color=orange][b]⚠ 迷宮の異変の予告：{DungeonAnomalySystem.GetName(arrivals.AnnouncedAnomaly)}[/b] " +
				$"{DungeonAnomalySystem.Describe(_state, arrivals.AnnouncedAnomaly)}[/color]");
		foreach (var c in arrivals.DeadlineNear)
			AppendLog($"[color=orange]⏳ {CommissionSystem.ClientName(c)}の依頼（{CommissionSystem.TypeLabel(c.Type)}）の期限まで残り{c.WeeksLeft(_state.WeekNumber)}週：" +
				$"{CommissionSystem.DescribeCondition(_state, c)}[/color]");
	}

	/// <summary>
	/// 施設の知らせ（完成・上限Lvの開放・作戦資料室と冒険者支援室の開放、§0.78・§0.79）。出撃の無い週でも出すので、
	/// 週次決算のログ（LogWeeklySettlement。出撃した週だけ）とは分けて、月の全週について呼ぶ。
	/// </summary>
	private void LogFacilityUnlocks(WeeklySettlementResult settlement)
	{
		if (settlement.CompletedFacility != null)
			AppendLog($"[color=lime][b]🏗 {FacilityLabel(settlement.CompletedFacility.Type)}がLv{settlement.CompletedFacility.CurrentLevel}に完成した！{(settlement.CompletedFacility.Specialty != FacilitySpecialty.None ? $"（専門：{FacilityPanel.SpecialtyLabel(settlement.CompletedFacility.Specialty)}）" : "")}[/b][/color]");
		foreach (var notice in settlement.FacilityUnlocks) // 大会のご褒美で施設が開いた（§0.82）
			AppendLog($"[color=gold][b]🏗 {FacilityUnlockSystem.DescribeUnlock(notice)}。[/b][/color] {notice.Line}" +
				(notice.Level == FacilityBalance.SpecialtyFromLevel + 1 && FacilityUnlockSystem.TrainingFacilities.Contains(notice.Facility) ? "[color=gray]（Lv3→4の改築で専門を選ぶ）[/color]" : ""));
		foreach (var ev in settlement.TournamentsResolved) // 大会の結果（§0.82）
			AppendLog($"[color=khaki][b]🏆 {ev.Name}（{TournamentSystem.GradeLabel(ev.Grade)}）[/b]：優勝 {(ev.Result == null ? "" : TournamentSystem.WithGuild(ev.Result.WinnerName, ev.Result.WinnerGuild))}" +
				string.Concat(ev.Result?.Placings.Select(p => $"／{p.Name} {TournamentSystem.PlacingLabel(p.Placing)}{(p.Prize > 0 ? $"（賞金 {p.Prize}G）" : "")}") ?? Enumerable.Empty<string>()) + "[/color]");
		if (settlement.YearStanding?.Rows.FirstOrDefault(r => r.Ours) is { } standing) // 年末のギルドの順位表（§0.94）
			AppendLog($"[color=khaki][b]🏅 {settlement.YearStanding.Year}年目のギルドの順位[/b]：当ギルドは{standing.Rank}位（栄誉点{standing.Points}）" +
				(standing.RewardGold > 0 ? $"。ご褒美に賞金{standing.RewardGold}G・マスターの機嫌+{standing.RewardMood}" : "") + "[/color]");
		foreach (var ev in settlement.TournamentInvitations)
			AppendLog($"[color=khaki][b]✉ 招待が届いた：{ev.Name}[/b]（{GameCalendar.Format(GameCalendar.WeekNumberOf(ev.Year, ev.Month, ev.Week))}）[/color]");
		// イザベラの来訪と交流戦（§0.84）
		if (settlement.IsabellaVisited)
			AppendLog("[color=plum][b]✉ 白百合の杖のイザベラが来訪した。[/b]「わたくしの冒険者と、実力を試してみませんこと？」[/color][color=gray]（来月の第4週に交流戦。🏆 大会のタブで剣・魔・技の出場者を選ぶ）[/color]");
		foreach (var m in settlement.ExchangeMatches)
		{
			string bouts = string.Join("／", m.Bouts.Select(b => $"{TournamentSystem.DisciplineLabel(b.Discipline)}：{b.OurName} {(b.Won ? "○" : "×")} {b.OpponentName}"));
			AppendLog($"[color=plum][b]⚔ 交流戦に{(m.Won ? "勝った" : "負けた")}（{m.Wins}勝{m.Bouts.Count - m.Wins}敗）[/b] {bouts}" +
				(m.FirstWin ? "　ご褒美に教官のマルグリットが派遣される" : m.Prize > 0 ? $"　賞金 {m.Prize}G・機嫌+{m.Mood}" : "") + "[/color]" +
				(m.First ? "[color=gray]（来月から王都の大会に出られる）[/color]" : ""));
		}
		if (settlement.GuestTrainer.Arrived is { } guest)
			AppendLog($"[color=plum][b]✉ 派遣の教官{guest.Name}が着任した[/b]（{IsabellaBalance.GuestTrainerWeeks}週" +
				(settlement.GuestTrainer.AssignedTo is FacilityType t ? $"・{FacilityLabel(t)}の教官" : "・顧問の任命で教官に置ける") + "）[/color]");
		if (settlement.GuestTrainer.Left is { } left)
			AppendLog($"[color=gray]✉ 派遣の教官{left.Name}が白百合の杖へ帰った。[/color]");
		if (settlement.RevivedBosses.Count > 0) // エンディングのあと、記憶から主が蘇った（§0.90）
			AppendLog("[color=plum][b]🌀 記憶から主が蘇った：[/b]" + string.Join("・", settlement.RevivedBosses.Select(b => $"{_state.DungeonFields.FirstOrDefault(f => f.Bosses.Contains(b))?.Name} {b.Floor}F「{b.Name}」")) + "[/color]");
		foreach (var back in settlement.LoanReturns) // 依頼の派遣から帰ってきた（§0.85）
			AppendLog($"[color=plum][b]🏠 {back.Adventurer.Name}が{back.ClientName}から帰ってきた。[/b]" +
				(back.Growth.Count > 0 ? "　" + string.Join("・", back.Growth.GroupBy(g => g.Stat).Select(g => $"{g.Key}+{g.Sum(e => e.After - e.Before)}")) : "") +
				(back.TraitId != null ? $"　特性「{TraitCatalog.FindById(back.TraitId)?.DisplayName ?? back.TraitId}」が付いた" : "") + "[/color]");
		if (settlement.CommissionsUnlocked && _state.CommissionsFromWeek is int from)
			AppendLog($"[color=gold][b]📜 初めての入賞で王都に名が知られた。[/b]{GameCalendar.FormatMonth(from)}から依頼が届く。[/color]");
		if (settlement.AdvisorFacilitiesOpened) // 最初の引退者（§0.78）
			AppendLog("[color=gold][b]🏗 引退者が出たので、作戦資料室と冒険者支援室を建てられるようになった。[/b][/color][color=gray]（引退者を参謀・スカウトに置く施設）[/color]");
	}

	/// <summary>月報の小窓を閉じたとき（月を進めた直後のもの）：残りの割り込み（エンディング・採用試験・依頼）へ進む。</summary>
	private void OnMonthlyReportClosed()
	{
		if (_monthlyReportSettlement == null)
			return; // 過去の月報を見返していただけ
		var settlement = _monthlyReportSettlement;
		_monthlyReportSettlement = null;
		// 月のはじめの場面（§0.86：最初の月報のあと・長老の来訪・交流戦の申し込みなど）を見せてから、残りの割り込みへ
		ShowStory(StoryTiming.AfterReport, () => HandlePostSettlementInterruptions(settlement));
	}

	/// <summary>月報の「来月への注意」から画面へ移る。</summary>
	private void OnMonthlyReportJump(MonthlyNoteTarget target)
	{
		SwitchView(target switch
		{
			MonthlyNoteTarget.Facility => DashboardView.Facility,
			MonthlyNoteTarget.Adventurer => DashboardView.Party,
			_ => DashboardView.Dungeon,
		});
	}

	/// <summary>月報の小窓（§0.71）。</summary>
	private MonthlyReportPopup _monthlyReportPopup = null!;

	/// <summary>月報の小窓を開いた月の最後の決算。閉じたら残りの割り込みを流す（過去の月報を見返すときは null）。</summary>
	private WeeklySettlementResult _monthlyReportSettlement;

	/// <summary>月を進めている間の週報ログの写し（月報の小窓の「週ごとの記録」用）。</summary>
	private System.Text.StringBuilder _logCapture;

	/// <summary>機嫌の段階名（→ MasterMoodSystem.GetTier）。</summary>
	private static string MoodTierLabel(MasterMoodTier tier) => tier switch
	{
		MasterMoodTier.Cheerful => "上機嫌",
		MasterMoodTier.Normal => "平常",
		MasterMoodTier.Grumpy => "不機嫌",
		_ => "危機",
	};

	/// <summary>
	/// 「次の月へ」の本体（§0.70：月を1ターンにする、→ AutoSkipService.AdvanceMonth）。今の月の残りの週を方針どおりに進め、
	/// 月の途中で止める条件（→ WeekResult.ShouldStopMonth）が成り立てばそこで止まる。週ごとの記録を流したあと、月報を出す。
	/// 処理はメインスレッドで同期的に終わるので、処理中に他の入力が割り込む余地は無い。
	/// </summary>
	private void AdvanceMonth()
	{
		DisableWeekAdvancement();

		int startWeek = _state.WeekNumber;
		int goldBefore = _state.Gold;
		var weeks = _autoSkipService.AdvanceMonth(_state);
		var results = weeks.Select(w => w.Settlement.Flags).ToList();

		// 月の出来事は月報の小窓の「週ごとの記録」に書く（§0.71。§0.80で右の週報の欄は撤去し、月報に一本化した）。
		// 方針で出撃した週は、週報を省略せずに残す（§0.63）。「待機」は毎週出ると多すぎるので、出撃・扉前の判断だけを出す。
		_logCapture = new System.Text.StringBuilder();
		foreach (var week in weeks)
		{
			if (week.Orders.Any(e => e.Action != SquadOrderAction.Waiting) || week.Settlement.DungeonMissionResolutions.Count > 0)
			{
				LogSquadOrders(week.Orders.Where(e => e.Action != SquadOrderAction.Waiting).ToList());
				LogWeeklySettlement(week.Settlement);
			}
			LogFacilityUnlocks(week.Settlement); // 出撃の無い週（年度末の引退など）でも知らせる（§0.78・§0.79）
		}
		if (results.Any(r => r.DefeatOccurred))
			AppendLog("[color=red][font_size=24][b]■■■ ゲームオーバーが発生した ■■■[/b][/font_size][/color]");
		string weekly = _logCapture.ToString();
		_logCapture = null;

		var report = MonthlyReport.Build(_state, weeks, goldBefore);
		MonthlyReport.Record(_state, report); // 直近12か月をセーブに残す（§0.71）

		_saveLoadService.Save(_state);
		RefreshAll();

		if (weeks.Count == 0)
		{
			EnableWeekAdvancement();
			return;
		}

		// 月報の小窓を開く（§0.71）。閉じたら、最後の週の割り込み（エンディング・採用試験・依頼）を「1週進める」と同じ処理に任せる。
		// 割り込みがある月・ゲームオーバーの月は【閉じて次の月へ】を出さない。
		var last = weeks[^1].Settlement;
		bool interrupted = last.Flags.GameCleared || last.Flags.DefeatOccurred || last.Flags.RecruitmentTrialOccurred
			|| last.Flags.CommissionsOffered || _state.DefeatReason != null;
		_monthlyReportSettlement = last;
		// 大会に出た月・施設が開いた月は、月報の前に大会の結果の小窓（トーナメント表と施設の知らせ）を見せる（§0.82）
		var tournaments = weeks.SelectMany(w => w.Settlement.TournamentsResolved).ToList();
		var unlocks = weeks.SelectMany(w => w.Settlement.FacilityUnlocks).ToList();
		void ShowReport() => _monthlyReportPopup.ShowMonth(report, weekly, allowNext: !interrupted);
		// 年度末に満期で引退した者は、大会の結果のあと・月報の前に引退式（1人1枚、大会と育成の栄光 段2）
		var retirees = weeks.SelectMany(w => w.Settlement.Retirees).Select(id => _state.FindAdventurer(id)).Where(a => a != null).ToList();
		void ShowCeremonies()
		{
			if (retirees.Count == 0)
			{
				ShowReport();
				return;
			}
			void OnCeremonyClosed()
			{
				_retirementCeremonyPopup.Closed -= OnCeremonyClosed;
				Callable.From(ShowReport).CallDeferred(); // 別の排他ウィンドウは閉じ切ってから開く
			}
			_retirementCeremonyPopup.Closed += OnCeremonyClosed;
			_retirementCeremonyPopup.ShowCeremonies(_state, retirees);
		}
		// 物語の場面（§0.86）：大会の結果の小窓のあと、引退式と月報の前（来訪・交流戦の結果・ボスの撃破など）
		void ShowStoryThenCeremonies() => ShowStory(StoryTiming.BeforeReport, ShowCeremonies);
		if (tournaments.Any(e => e.Result?.Placings.Count > 0) || unlocks.Count > 0)
		{
			void OnResultsClosed()
			{
				_tournamentResultPopup.Closed -= OnResultsClosed;
				Callable.From(ShowStoryThenCeremonies).CallDeferred(); // 別の排他ウィンドウは閉じ切ってから開く
			}
			_tournamentResultPopup.Closed += OnResultsClosed;
			_tournamentResultPopup.ShowResults(tournaments, unlocks);
		}
		else
		{
			ShowStoryThenCeremonies();
		}
	}

	/// <summary>施設管理画面の「👔 顧問を任命」ボタン。顧問役職割り当てポップアップを開く（いつでも自由に開閉できる）。</summary>
	private void OnAdvisorButtonPressed(FacilityType focus)
	{
		if (_state == null) return;
		_advisorPopup.Open(_state, _advisorSystem, focus);
	}

	/// <summary>顧問管理ポップアップが閉じた時のコールバック。役職配置の変化を反映する。</summary>
	private void OnAdvisorPopupClosed()
	{
		RefreshAll();
	}

	/// <summary>冒険者詳細パネルからの装備ポップアップ開放依頼。</summary>
	private void OnAdventurerEquipmentRequested(Adventurer target)
	{
		_equipmentPopup.Open(_state, target, _equipmentSystem);
	}

	/// <summary>装備ポップアップが閉じた時のコールバック。所持金・装備の変化を反映する。</summary>
	private void OnEquipmentPopupClosed()
	{
		RefreshAll();
	}

	/// <summary>
	/// 今週の成長トリガー（→ 03 §3.1〜3.4）で実際にステータスが伸びた者を週報ログに報告する。
	/// 見逃さないよう、色（黄）＋太字＋大きめフォントサイズで目立たせる（→ ユーザー要望）。
	/// </summary>
	private void LogGrowthEvents(List<GrowthEvent> events)
	{
		foreach (var e in events)
		{
			AppendLog(
				$"[color=yellow][font_size=20][b]▲ {e.Adventurer.Name} の {e.Stat} が上昇！ {e.Before} → {e.After}[/b][/font_size][/color]");
		}
	}

	/// <summary>
	/// 致命傷を負い、ギルドを去った冒険者を週報ログに報告する（→ 03 §4.3・§4.3.1）。
	/// 見逃さないよう赤・太字で目立たせる。氏名はPartyから引く
	/// （対象はGameState.Adventurersから既に除外済みのため）。
	///
	/// 世界観上の扱い（→ 01_コンセプト.md・ダンジョン攻略システムと統一）：
	/// 冒険者は「戦死」しない。アルベールの秘薬によって必ず一命は取り留めるが、
	/// 危険な目に遭わせたことに激怒したマスターが即座にギルド登録を抹消するため、
	/// 二度と戻らない。**システム上は恒久的なロスト**であり、扱いは従来と同じ
	/// （GameState.FallenAdventurers へ移される。識別子はセーブデータ互換のため据え置き）。
	/// </summary>
	private void LogFallenAdventurers(int weekNumber, Party party, HashSet<Guid> fallenIds)
	{
		foreach (var line in FallenAdventurerLines(weekNumber, party, fallenIds, new Dictionary<Guid, List<EquipmentItem>>()))
			AppendLog(line);
	}

	/// <summary>
	/// 致命傷→秘薬治療→強制除籍の報告文（→ LogFallenAdventurers）。大迷宮の決戦ログでも同じ文面を使う。
	/// recoveredEquipment（→ DungeonMissionResolution.RecoveredEquipment）には、アルベールが回収して
	/// ギルド保管庫へ返還された装備が入る（→ 03 §4.2.2「離脱時の自動回収」）。該当が無ければ空の辞書を渡す。
	/// </summary>
	private static IEnumerable<string> FallenAdventurerLines(
		int weekNumber, Party party, IEnumerable<Guid> fallenIds,
		IReadOnlyDictionary<Guid, List<EquipmentItem>> recoveredEquipment)
	{
		foreach (var id in fallenIds)
		{
			var fallen = party.Members.FirstOrDefault(m => m.Id == id);
			if (fallen == null)
				continue;

			yield return $"[color=red][b]✖ {fallen.Name} が致命傷を負った（{GameCalendar.Format(weekNumber)}）。[/b][/color]";
			yield return $"[color=orange]アルベールの秘薬で一命は取り留めたが、「危ないじゃないか！」と激怒したマスターにより" +
				$"{fallen.Name}のギルド登録は強制抹消された。二度と戻らない。[/color]";

			if (recoveredEquipment.TryGetValue(id, out var recovered) && recovered.Count > 0)
			{
				yield return $"[color=cyan]📦 アルベールは{fallen.Name}の武具を回収し、ギルド備品としてギルド保管庫へ返還した" +
					$"（{string.Join("、", recovered.Select(e => e.Name))}）。[/color]";
			}
		}
	}

	/// <summary>
	/// 大迷宮への出撃1件の結果を週報に記録する（→ DungeonMissionResolution）。
	///  - 調査任務：生還の報告と、解析率の上昇（段階が上がれば新情報として強調）を一括表示する。
	///  - ボス討伐：決戦として扱い、1行ずつのステップ再生に回す（→ Phase 3）。
	/// </summary>
	private void LogDungeonMission(int weekNumber, DungeonMissionResolution resolution)
	{
		var boss = resolution.Boss;
		var sb = new StringBuilder();

		if (resolution.GatheringResult != null)
		{
			var gathering = resolution.GatheringResult;
			string materialName = MaterialBalance.GetName(gathering.MaterialId);
			sb.AppendLine($"[b]{GameCalendar.Format(weekNumber)}：大迷宮 探索（採取）任務[/b]");
			sb.AppendLine($"[color=lime]【採取任務】{resolution.Field.Name}にて素材を回収（{materialName}×{gathering.MaterialCount}、" +
				$"換金{gathering.GoldEarned}Gを獲得）。[/color]");
			// 判定内訳の開示（→ 03 §4.2.3「開発・バランス調整期間の特記事項」）。
			sb.AppendLine($"[color=gray]【探索採取】採取スコア{gathering.Score:F0}（{DungeonPanel.GatheringBreakdownText(gathering.ScoreBreakdown)}／うち田舎育ちボーナス+{gathering.ScoreBreakdown.RuralTotal:F0}）" +
				$" → 採取枠{gathering.MaterialCount}個（素材基礎{gathering.BaseYield}＋スコア枠{gathering.ScoreYield}〔÷{GatheringBalance.MaterialYieldDivisor}〕" +
				$"＋階層枠{gathering.FloorYield}〔{resolution.Field.ReachedFloor}F÷{GatheringBalance.ReachedFloorDivisor}〕＋研究{gathering.ResearchYield}）" +
				$" / 遺物発見率{gathering.RelicDropPercent}% / 換金{gathering.Score:F0}×{GatheringBalance.GoldPerScore:0.0#}={gathering.GoldEarned}G[/color]");
			sb.AppendLine($"[color=gray]【護衛】護衛{gathering.GuardPower:F0}／要求{gathering.GuardRequirement:F0} → {DungeonPanel.GuardTierLabel(gathering.GuardTier)}" +
				$"（素材 ×{GatheringResolver.GuardYieldMultiplier(gathering.GuardTier):0.##}・損耗 ×{GatheringResolver.GuardHpLossMultiplier(gathering.GuardTier):0.##}）[/color]");
			sb.AppendLine("[color=cyan]探索部隊は全員生還した。[/color]");
			AppendRelicLines(sb, resolution);
			AppendBondLine(sb, resolution);
			AppendHpLossLines(sb, gathering.HpLostByAdventurer);

			AppendLog(sb.ToString());
			return;
		}

		if (resolution.ScoutingResult == null && resolution.TraversalResult == null && resolution.DungeonResult == null)
		{
			// 判定を伴わない帰還（討伐に向かったボスが既に倒されていた等）。
			sb.AppendLine($"[b]{GameCalendar.Format(weekNumber)}：大迷宮 {resolution.Field.Name}からの帰還[/b]");
			sb.AppendLine("[color=gray]目標のボスは既に討たれていたため、部隊は戦わずにギルドへ帰還した。[/color]");
			AppendDepositLine(sb, resolution);
			AppendLog(sb.ToString());
			return;
		}

		if (resolution.ScoutingResult != null)
		{
			var scouting = resolution.ScoutingResult;
			bool survey = resolution.MissionType == DungeonMissionType.Survey;
			sb.AppendLine(survey
				? $"[b]{GameCalendar.Format(weekNumber)}：大迷宮 第{boss.Floor}層「{boss.Name}」迷宮調査[/b]"
				: $"[b]{GameCalendar.Format(weekNumber)}：大迷宮 第{boss.Floor}層「{boss.Name}」扉前での偵察[/b]");
			// 護衛評価（→ GuardTier、2026年9月新設）：解析成果の倍率とHP消費を決める。
			sb.AppendLine($"護衛評価：{DungeonPanel.GuardTierLabel(scouting.GuardTier)}　" + scouting.GuardTier switch
			{
				GuardTier.Abundant => "[color=lime]護衛が残党を完封し、調査隊は無傷のまま調べ尽くした。[/color]",
				GuardTier.Sufficient => "[color=cyan]護衛が残党を退け、軽い手傷で調査を続けられた。[/color]",
				GuardTier.Marginal => "[color=orange]護衛の手が回らず被弾し、調査は途切れがちになった。[/color]",
				_ => "[color=red][b]魔物の残党に強襲され調査隊が潰走。解析の成果を持ち帰れなかった。[/b][/color]",
			});
			if (scouting.GuardTier != GuardTier.Deficient)
			{
				sb.AppendLine(scouting.StealthSucceeded
					? "[color=cyan]気づかれることなく潜り込み、じっくりと観察を続けた。[/color]"
					: "[color=orange]途中で見つかり、落ち着いて観察できなかった。[/color]");
				sb.AppendLine(scouting.AnalysisOutcome switch
				{
					SurveyOutcome.GreatSuccess => "[color=lime]◆ 生態を細部まで読み解き、貴重な情報を持ち帰った。[/color]",
					SurveyOutcome.Success => "[color=lime]◆ 要点を掴み、有益な情報を持ち帰った。[/color]",
					_ => "[color=gray]◆ 断片的な情報しか持ち帰れなかった。[/color]",
				});
			}
			// 参謀の作戦分析（→ AdvisorSystem.GetAdvisorSurveyIntelBonus、§7.2）。任命されている週のみ。
			if (scouting.AdvisorName != null)
			{
				sb.AppendLine($"[color=cyan]🖋 参謀{scouting.AdvisorName}の作戦分析により解析が促進された" +
					$"（解析量 +{scouting.AdvisorIntelBonus * 100:F0}%）。[/color]");
			}
			sb.AppendLine($"解析率 {resolution.IntelRateBefore * 100:F0}% → {scouting.IntelRateAfter * 100:F0}%" +
				$"（+{scouting.IntelGained * 100:F0}%）");
			// 判定内訳の開示（→ 03 §4.2.3「開発・バランス調整期間の特記事項」）。
			sb.AppendLine($"[color=gray]【迷宮調査】護衛判定: 護衛力{scouting.GuardPower:F0}（{scouting.GuardCarrierName}:{scouting.GuardCarrierStat}{scouting.GuardCarrierValue:F0}{(scouting.GuardSupportPower > 0 ? $" ＋支援{scouting.GuardSupportPower:F0}" : "")}）" +
				$" / 要求値{scouting.GuardRequirement:F1}（{resolution.Field.Name}{boss.Floor}F基準）＝ 比率{DungeonPanel.FormatRatio(scouting.GuardRatio)}" +
				$"［{DungeonPanel.GuardTierLabel(scouting.GuardTier)}］。損耗{scouting.HpLossPercent}% / 解析倍率×{scouting.GuardIntelMultiplier:0.0#}" +
				$" / 解析進展+{scouting.IntelGained * 100:F1}%（{scouting.IntelRateBefore * 100:F0}%→{scouting.IntelRateAfter * 100:F0}%）[/color]");
			sb.AppendLine($"[color=gray]　隠密 {scouting.StealthScore:F0} / 要求 {scouting.StealthRequirement:F0}（{(scouting.StealthSucceeded ? "成功" : "発見・解析1段階低下")}）" +
				$"　解析 {scouting.AnalysisScore:F0} / 要求 {scouting.AnalysisRequirement:F0}（比率{DungeonPanel.FormatRatio(scouting.AnalysisRatio)} → {DungeonPanel.SurveyOutcomeLabel(scouting.AnalysisOutcome)}）[/color]");
			if (scouting.TierAdvanced)
				sb.AppendLine($"[color=gold][b]★ 新たな情報を掴んだ：解析段階が「{DungeonPanel.TierLabel(scouting.TierAfter)}」に到達！[/b][/color]");
			sb.AppendLine(survey
				? "[color=cyan]調査隊はギルドへ帰還した。[/color]"
				: "[color=cyan]部隊は扉前に留まり、突入か撤退かの指令を待っている。[/color]");
			AppendBondLine(sb, resolution);
			AppendHpLossLines(sb, scouting.HpLostByAdventurer);

			AppendLog(sb.ToString());
			return;
		}

		if (resolution.TraversalResult != null)
		{
			var traversal = resolution.TraversalResult;
			sb.AppendLine($"[b]{GameCalendar.Format(weekNumber)}：大迷宮 第{traversal.FloorBefore}層〜 道中進軍[/b]");
			sb.AppendLine(traversal.Rank switch
			{
				TraversalRank.Godspeed => "[color=gold]◆ 神速の踏破。道なき道を一気に駆け抜け、はるか奥まで進んだ。[/color]",
				TraversalRank.Gale => "[color=lime]◆ 疾風のごとく魔物の群れを振り切り、一気に奥へ進んだ。[/color]",
				TraversalRank.Lightning => "[color=lime]◆ 敵の気配を巧みにかわし、電撃的に奥へ進んだ。[/color]",
				TraversalRank.Swift => "[color=cyan]◆ 淀みない足取りで、迅速に奥へ進んだ。[/color]",
				TraversalRank.Normal => "[color=cyan]◆ 着実に一歩ずつ、奥へ進んだ。[/color]",
				_ => "[color=orange]◆ 幾度も行く手を阻まれながら、なんとか奥へ進んだ。[/color]",
			});
			sb.AppendLine($"現在階層 第{traversal.FloorBefore}層 → 第{traversal.FloorAfter}層" +
				$"（+{traversal.FloorAfter - traversal.FloorBefore}階層を走破／{DungeonPanel.TraversalRankLabel(traversal.Rank)}）");
			if (traversal.IntelSpeedMultiplier > 1.0)
				sb.AppendLine($"[color=lime]◆ 解析済みの情報を活かし、実効平均 走破速度 ×{traversal.IntelSpeedMultiplier:F1}で進んだ。[/color]");
			// 判定・損耗内訳の開示（→ 03 §4.2.3「開発・バランス調整期間の特記事項」）。
			sb.AppendLine($"[color=gray]【大迷宮潜行】走破力{traversal.TraversalScore:F0} / 先頭の要求値{traversal.Requirement:F0}（{traversal.FrontFloor}F×{DungeonTraversalBalance.RequirementPerFloor}×フィールド倍率）" +
				$"＝ 比率{DungeonPanel.FormatRatio(traversal.Ratio)}［{DungeonPanel.TraversalRankLabel(traversal.Rank)}＝floor(比率×{DungeonTraversalBalance.RankRatioScale:0.#})段・この深さで週{traversal.BaseFloors}階層＝floor(比率×{DungeonTraversalBalance.FloorsPerRatio:0.#})］" +
				$"　1歩の消費＝その階層の要求値÷(走破力×{DungeonTraversalBalance.FloorsPerRatio:0.#})÷区間倍率、週の予算1[/color]");
			if (traversal.Segments.Count > 0)
			{
				string segments = string.Join(" + ", traversal.Segments.Select(s =>
					$"{s.FromFloor}〜{s.ToFloor}F({(s.DamageMultiplier < 1.0 ? "解析済" : "未解析")}・{(s.IsUnexplored ? "未踏破" : "既踏")}: " +
					$"損耗{s.BaseLossPct:0.#}%×{s.DamageMultiplier:0.0#}={s.EffectiveLossPct:F1}%, 速度×{s.SpeedMultiplier:F1})"));
				sb.AppendLine($"[color=gray]　第{traversal.FloorBefore}F〜{traversal.FloorAfter}Fへ進軍。内訳: {segments}" +
					$" → 実効平均速度×{traversal.IntelSpeedMultiplier:F1} / 実効損耗{traversal.EffectiveLossPct:F1}%（階層数で加重平均）[/color]");
			}
			// 神官の加護（§0.72）
			if (traversal.ClericBlessingPct > 0)
				sb.AppendLine($"[color=cyan]✝ 神官の加護が隊を守り、損耗を{traversal.ClericBlessingPct:0.#}%ポイント軽くした。[/color]");
			// 夜目（→ 03 §4.5.3・§5.3.2）：未踏破階層の基礎損耗を一律で軽減した週のみ。
			if (traversal.NightVisionApplied)
				sb.AppendLine($"[color=cyan]🌙 夜目の利く隊員が暗がりを先導し、未踏破区間の損耗を{DungeonTraversalBalance.NightVisionUnexploredDamageReductionRate * 100:0}%抑えた。[/color]");
			// 参謀のルート指導（→ AdvisorSystem.GetAdvisorTraversalPowerBonus、§7.2）。任命されている週のみ。
			if (traversal.AdvisorName != null)
			{
				sb.AppendLine($"[color=cyan]🗺 参謀{traversal.AdvisorName}のルート指導が道筋を照らした" +
					$"（走破力 +{traversal.AdvisorTraversalBonus:F0}）。[/color]");
			}
			if (traversal.LootGold > 0 || traversal.LootMaterials.Count > 0)
				sb.AppendLine($"道中で拾った：{LootText(traversal.LootGold, traversal.LootMaterials)}（帰還時にギルドへ格納）");
			if (resolution.ArrivedAtBossDoor && traversal.TargetBoss != null)
			{
				// 扉前の判断は、着いた週のうちに構えで済む（§0.69）。挑むなら続く決戦の行で結果を出す。
				var orderedParty = _state.SavedParties.FirstOrDefault(p => p.MemberIds.Any(id => resolution.Party.Members.Any(m => m.Id == id)));
				string stance = SquadOrderSystem.StanceLabel(orderedParty?.Stance ?? DoorStance.Standard);
				if (resolution.DoorRetreatReason == null)
				{
					sb.AppendLine($"[color=gold][b]【扉前到達】部隊は第{traversal.TargetBoss.Floor}層ボスの扉前に到達し、構え「{stance}」で挑んだ。[/b][/color]");
				}
				else
				{
					sb.AppendLine($"[color=orange][b]【扉前到達】部隊は第{traversal.TargetBoss.Floor}層ボスの扉前に到達したが、構え「{stance}」で撤退した：{resolution.DoorRetreatReason}。[/b][/color]");
					AppendDepositLine(sb, resolution);
				}
			}
			else if (resolution.ReturnedHome)
			{
				sb.AppendLine("[color=cyan]最深部まで踏破し、部隊はギルドへ帰還した。[/color]");
				AppendDepositLine(sb, resolution);
			}
			else
			{
				sb.AppendLine("[color=cyan]部隊は次週さらに奥へ進軍する。[/color]");
			}
			AppendBondLine(sb, resolution);
			AppendHpLossLines(sb, traversal.HpLostByAdventurer, traversal.EffectiveLossPctByAdventurer);

			AppendLog(sb.ToString());
			return;
		}

		var assault = resolution.DungeonResult;
		if (assault == null)
			return;

		sb.AppendLine($"[color=gold][b]⚔ {GameCalendar.Format(weekNumber)}：大迷宮 第{boss.Floor}層「{boss.Name}」討伐戦[/b][/color]");
		foreach (var type in assault.CounteredGimmicks)
			sb.AppendLine($"[color=lime]✔ {DungeonPanel.GimmickLabel(type)}への備えが機能した。[/color]");
		// 備えが一部（橙）・無策（赤）のギミック（§0.68）。足りない分だけ罰が効いた。
		foreach (var type in assault.UncounteredGimmicks)
		{
			bool none = assault.ShortfallOf(type) >= 1.0;
			sb.AppendLine(none
				? $"[color=red]✖ {DungeonPanel.GimmickLabel(type)}に備えが無く、{GimmickPenaltyText(type)}。[/color]"
				: $"[color=orange]△ {DungeonPanel.GimmickLabel(type)}への備えが足りず、{GimmickPenaltyText(type)}。[/color]");
		}
		if (assault.CharmedAdventurerId is { } charmedId && _state.Adventurers.FirstOrDefault(a => a.Id == charmedId) is { } charmed)
			sb.AppendLine($"[color=orange]💫 {charmed.Name}が魅了に囚われ、仲間に刃を向けかけた。[/color]");
		foreach (var (poisonedId, weeks) in assault.PoisonWeeksByAdventurer)
		{
			var poisoned = _state.Adventurers.FirstOrDefault(a => a.Id == poisonedId);
			if (poisoned != null)
				sb.AppendLine($"[color=orange]🟣 {poisoned.Name}は毒に冒された（{weeks}週ほど全能力が{assault.PoisonStatPenalty * 100:0}%下がる）。[/color]");
		}
		if (assault.FullIntelBonusApplied)
			sb.AppendLine("[color=gold]◆ 完全解析の成果：弱点を正確に突いた。[/color]");
		// 神官の加護（§0.72）
		if (assault.ClericBlessingPct > 0)
			sb.AppendLine($"[color=cyan]✝ 神官の加護が隊を守り、損耗を{assault.ClericBlessingPct:0.#}%ポイント軽くし、重傷を遠ざけた。[/color]");
		// 耐毒体質・巨獣狩り（→ 03 §4.5.4・§5.3.2）：効いた戦闘のみ開示する。
		if (assault.ResistPoisonApplied)
			sb.AppendLine($"[color=cyan]🧪 耐毒体質の隊員が毒に耐え、猛毒による被害の上乗せを{CombatBalance.ResistPoisonDamageReductionRate * 100:0}%抑えた。[/color]");
		// 鷹の目・危機察知（→ 03 §0.55・§5.3.2）
		if (assault.HawkEyeApplied)
			sb.AppendLine($"[color=cyan]🦅 鷹の目の隊員が空の敵の動きを見切り、飛行による被害の上乗せを{TraitBalance.HawkEyeFlyingDamageReductionRate * 100:0}%抑えた。[/color]");
		foreach (var senseId in assault.SixthSenseAdventurerIds)
		{
			var sensor = _state.Adventurers.FirstOrDefault(a => a.Id == senseId);
			if (sensor != null)
				sb.AppendLine($"[color=cyan]⚡ {sensor.Name}は危機察知で致命の一撃を寸前でかわした（損耗{TraitBalance.SixthSenseInstantKillHpLossPct}%で止まった）。[/color]");
		}
		foreach (var hunterId in assault.GiantHunterAdventurerIds)
		{
			var hunter = _state.Adventurers.FirstOrDefault(a => a.Id == hunterId);
			if (hunter != null)
				sb.AppendLine($"[color=cyan]🗡 {hunter.Name}の巨獣狩りの技が重装甲を穿った（火力 +{CombatBalance.GiantHunterDamageBonusRate * 100:0}%）。[/color]");
		}

		sb.AppendLine(assault.Outcome == DungeonOutcome.Victory
			? $"[color=gold][font_size=20][b]🏆 【階層ボス撃破】部隊は見事に「{boss.Name}」を討伐した！ 第{boss.Floor}層を踏破！[/b][/font_size][/color]"
			: "[color=orange][b]火力が及ばず、撤退を余儀なくされた。ボスは傷を癒やし、次は仕切り直しになる。[/b][/color]");

		if (assault.Outcome == DungeonOutcome.Victory)
		{
			// 撃破報酬の内訳（→ FloorBoss.RewardGold/RewardMaterialId、2026年9月新設）。名声は廃止し、マスターの機嫌の上昇として週報末尾に集計する。
			string rewardLine = $"💰 報奨獲得：{boss.RewardGold} G ／ 😊 マスターの機嫌 +{MasterMoodBalance.BossDefeatMoodGain}";
			if (!string.IsNullOrEmpty(boss.RewardMaterialId))
				rewardLine += $" ／ 📦 {MaterialBalance.GetName(boss.RewardMaterialId)} ×{boss.RewardMaterialCount}";
			sb.AppendLine($"[color=lime]{rewardLine}[/color]");
			AppendRelicLines(sb, resolution);
			// 伝説級の固有武具（→ 03 §4.7.5、2026年9月・§0.45）：割り当てのあるボスの初回撃破で確定入手。
			if (resolution.LegendaryFound is { } legendary)
			{
				sb.AppendLine($"[color=gold][b]👑【伝説級】{ItemColorHelper.GetColoredBBCode(legendary)} を手に入れた！[/b][/color]");
				sb.AppendLine($"[color=gray]性能：{legendary.DescribeEffects()}。ギルド保管庫へ納めた（ギルドの宝のため売却不可）。[/color]");
			}
		}

		AppendBondLine(sb, resolution);
		AppendHpLossLines(sb, assault.HpLostByAdventurer);
		foreach (var line in FallenAdventurerLines(
			weekNumber, resolution.Party, assault.ForceRetiredAdventurerIds, resolution.RecoveredEquipment))
			sb.AppendLine(line);
		// 強制除籍（不死薬による現場からの永久離脱）へのアルベールの激怒（→ 03 §8.1、除籍1名につき機嫌−20）。
		if (resolution.MasterFuryRetiredCount > 0)
		{
			sb.AppendLine($"[color=red][b]【アルベールの激怒】{MasterMoodSystem.FuryLine} " +
				$"(機嫌 -{MasterMoodBalance.ForcedRetirementMoodLoss * resolution.MasterFuryRetiredCount}" +
				$"{(resolution.MasterFuryRetiredCount > 1 ? $"〔除籍{resolution.MasterFuryRetiredCount}名×{MasterMoodBalance.ForcedRetirementMoodLoss}〕" : "")})[/b][/color]");
		}
		if (resolution.ReturnedHome)
		{
			bool wiped = resolution.Party.Members.All(m => !_state.Adventurers.Contains(m));
			sb.AppendLine(wiped
				? "[color=red]部隊は全滅した。道中の拾得物も失われた。[/color]"
				: "[color=cyan]決着後、部隊はギルドへ帰還した。次回は第1層から潜り直す。[/color]");
			AppendDepositLine(sb, resolution);
		}

		if (assault.Outcome == DungeonOutcome.Victory)
		{
			sb.AppendLine("[color=cyan]【生体コード回収】アルベールはボスから古代遺伝子結晶の採取に成功した！[/color]");

			var next = _state.GetCurrentFloorBoss();
			sb.AppendLine(next != null
				? $"[color=cyan]さらに深層、第{next.Floor}層への道が開けた。[/color]"
				: "[color=gold][b]★ 大迷宮の全階層を踏破した！[/b][/color]");

			// 新フィールド開放（→ DungeonExpeditionSystem.ApplyFieldProgression、2026年9月新設）。
			if (resolution.FieldNewlyUnlocked != null)
			{
				sb.AppendLine($"[color=gold][font_size=18][b]🗺 【探索域拡大】新たなフィールド" +
					$"「{resolution.FieldNewlyUnlocked.Name}」への進軍が可能になった！[/b][/font_size][/color]");
			}

			// 森の節目ボス撃破による出撃枠拡張（「古代エルフの多頭通信術式」復元、→ DungeonExpeditionSystem）。
			if (resolution.SquadSlotsExpandedTo.HasValue)
			{
				sb.AppendLine($"[color=gold][font_size=18][b]📡 【古代通信術式復元】同時出撃枠が" +
					$"【{resolution.SquadSlotsExpandedTo.Value}枠】に拡張された！[/b][/font_size][/color]");
			}
		}

		foreach (var line in sb.ToString().Split('\n'))
			if (!string.IsNullOrWhiteSpace(line))
				AppendLog(line.TrimEnd('\r'));
	}

	/// <summary>
	/// 未鑑定の古代遺物の持ち帰り報告（→ 03 §4.7）。採取の副産物・階層ボス撃破の確定ドロップの
	/// 両方で使う。何も出なかった週は1行も出さない。
	/// </summary>
	private static void AppendRelicLines(StringBuilder sb, DungeonMissionResolution resolution)
	{
		foreach (var relic in resolution.RelicsFound)
		{
			var profile = RelicBalance.Get(relic.Rarity);
			sb.AppendLine($"[color=gold][b]【遺物発見】未鑑定の古代遺物を持ち帰った！[/b][/color]　" +
				$"[color={profile.ColorName}]{relic.Name}（{profile.Label}）[/color]");
			sb.AppendLine($"[color=gray]倉庫・遺物タブでアルベールに鑑定させること（鑑定費用 {relic.AppraisalCost}G）。[/color]");
		}
	}

	/// <summary>帰還時にギルドへ格納した道中の拾得物（何も無ければ出さない）。</summary>
	private static void AppendDepositLine(StringBuilder sb, DungeonMissionResolution resolution)
	{
		if (resolution.DepositedGold > 0 || resolution.DepositedMaterials.Count > 0)
			sb.AppendLine($"[color=lime]📦 道中の拾得物を格納：{LootText(resolution.DepositedGold, resolution.DepositedMaterials)}[/color]");
	}

	/// <summary>ギミックに備えが足りなかったときに起きたこと（週報の決戦の行、§0.68）。</summary>
	private static string GimmickPenaltyText(BossGimmickType type) => type switch
	{
		BossGimmickType.Poison => "毒で部隊が削られた",
		BossGimmickType.HeavyArmor => "攻撃が装甲に阻まれた",
		BossGimmickType.Flying => "空からの攻撃に部隊が削られた",
		BossGimmickType.InstantKill => "致命の一撃を受けた",
		BossGimmickType.Regeneration => "削った傷が塞がっていった",
		BossGimmickType.Swarm => "取り巻きに後衛を襲われた",
		BossGimmickType.Charm => "一番の使い手が惑わされた",
		_ => "部隊が損害を受けた",
	};

	private static string LootText(int gold, Dictionary<string, int> materials)
	{
		var parts = new List<string> { $"{gold}G" };
		parts.AddRange(materials.Select(kv => $"{MaterialBalance.GetName(kv.Key)}×{kv.Value}"));
		return string.Join("、", parts);
	}

	/// <summary>
	/// 任務達成による相性の上昇（→ CompatibilitySystem.ApplyExpeditionOutcome、03 §5.3.1、2026年9月再配線）。
	/// 上昇が無かった（未達成・生還者が2人未満）場合は何も出さない。
	/// </summary>
	private static void AppendBondLine(StringBuilder sb, DungeonMissionResolution resolution)
	{
		if (resolution.CompatibilityGain <= 0)
			return;
		sb.AppendLine($"[color=pink]【部隊の結束】共闘により隊員間の相性値が向上 (+{resolution.CompatibilityGain})" +
			"[/color][color=gray]〔生還者の全ペア。「容姿秀麗」が関与するペアは倍率あり〕[/color]");
	}

	/// <summary>HP消費の内訳（現役ロースターに残っている者のみ。強制除籍者は別途報告する）。</summary>
	/// <param name="lossPctByAdventurer">省略可能。渡した場合、各員の実効損耗率（%）も併記する（道中進軍の内訳開示用）。</param>
	private void AppendHpLossLines(StringBuilder sb, Dictionary<Guid, int> hpLostByAdventurer, Dictionary<Guid, double> lossPctByAdventurer = null)
	{
		foreach (var kv in hpLostByAdventurer)
		{
			var adv = _state.Adventurers.FirstOrDefault(a => a.Id == kv.Key);
			if (adv == null)
				continue;
			string pct = lossPctByAdventurer != null && lossPctByAdventurer.TryGetValue(kv.Key, out double p)
				? $"〔最大HP{adv.MaxHP}×{p:F1}%〕"
				: "";
			sb.AppendLine($" - {adv.Name}: HP -{kv.Value}{pct}（残りHP {adv.CurrentHP}/{adv.MaxHP}）");
		}
	}

	/// <summary>部隊の方針による自動の行動（→ SquadOrderSystem、03 §0.63）を週報ログに出す。待機は灰色で短く。</summary>
	private void LogSquadOrders(List<SquadOrderEvent> events)
	{
		foreach (var ev in events)
		{
			switch (ev.Action)
			{
				case SquadOrderAction.Dispatched:
					AppendLog($"[color=cyan]📋 「{ev.Party.Name}」が方針どおり出撃：{ev.Detail}。[/color]");
					break;
				default:
					AppendLog($"[color=gray]📋 「{ev.Party.Name}」は待機：{ev.Detail}。[/color]");
					break;
			}
		}
	}

	/// <summary>
	/// 魂魄融和の娘の誕生を週報ログに報告する（→ 03 §5.4・§0.58）。能力限界突破した能力と、受け継いだ・生まれ持った特性も出す。
	/// </summary>
	private void LogSoulFusionBirth(SoulFusionCulture birth)
	{
		var child = birth.Child;
		string parentA = _state.FindAdventurer(birth.ParentAId)?.Name ?? "？";
		string parentB = _state.FindAdventurer(birth.ParentBId)?.Name ?? "？";
		AppendLog($"[color=violet][b]🧪 培養槽から{parentA}と{parentB}の娘、{child.Name}（{AdventurerPanel.JobLabel(child.JobClass)}）が誕生し、ギルドに加わった！[/b][/color]" +
			$"\n[color=gray]百合相性：{SoulFusionSystem.GetNyxTierLabel(birth.NyxTier)}・総合 見立て{PotentialEstimateSystem.TotalRankLabel(_state, child)}・週給 {child.WeeklyWage}G[/color]");
		foreach (var stat in birth.BreakthroughStats)
			AppendLog($"[color=gold]✨ 能力限界突破！ {child.Name}の{stat}の潜在能力が限界を超えた（見立て {PotentialEstimateSystem.RankLabel(_state, child, stat)}）。[/color]");
		if (child.TraitIds.Count > 0)
			AppendLog($"[color=gray]特性：{string.Join("・", child.TraitIds.Select(id => TraitCatalog.FindById(id)?.DisplayName ?? id))}[/color]");
	}

	/// <summary>
	/// エンディング（→ 03 §8.2・§0.59）：アルベールの語りとギルドの記録（→ GuildChronicle）を出す。
	/// 「ギルドを続ける」で閉じると afterClose を呼ぶ（クリア後もそのまま遊べる）。システム画面から見直すときは afterClose なし。
	/// </summary>
	private void ShowEnding(Action afterClose = null)
	{
		PlayerProfile.MarkCleared(); // 一度クリアした（§0.88：次の新しいゲームで「物語と手ほどき あり／なし」を選べる）
		var chronicle = GuildChronicle.Build(_state);

		RichTextLabel MakeText(string bbcode)
		{
			var label = new RichTextLabel
			{
				BbcodeEnabled = true,
				FitContent = true,
				CustomMinimumSize = new Vector2(820, 0),
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			label.AppendText(bbcode);
			return label;
		}

		// 語りは文字送りで出し、終わるとギルドの記録がふっと現れる。クリックで飛ばせる。
		var narration = MakeText(EndingNarration());
		var record = MakeText(EndingRecord(chronicle));
		narration.VisibleRatio = 0f;
		record.Modulate = new Color(1, 1, 1, 0);

		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 28);
		column.AddChild(narration);
		column.AddChild(new HSeparator());
		column.AddChild(record);
		var margin = new MarginContainer();
		foreach (var side in new[] { "left", "top", "right", "bottom" })
			margin.AddThemeConstantOverride($"margin_{side}", 20);
		margin.AddChild(column);

		var scroll = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		scroll.AddChild(margin);

		// 舞台：ギルドの広間の背景に暗幕を重ね、左にアルベールの立ち絵、右に語りと記録の板
		var stage = new Control { CustomMinimumSize = new Vector2(1400, 720) };
		var background = new TextureRect
		{
			Texture = GD.Load<Texture2D>("res://assets/ending/ending_bg.jpg"),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		stage.AddChild(background);
		var dim = new ColorRect { Color = new Color(0, 0, 0, 0.35f), MouseFilter = Control.MouseFilterEnum.Ignore };
		dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		stage.AddChild(dim);

		var layout = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		layout.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layout.AddThemeConstantOverride("separation", 16);
		var albert = new TextureRect
		{
			Texture = GD.Load<Texture2D>("res://assets/ending/albert_standing.png"),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			CustomMinimumSize = new Vector2(440, 0),
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Modulate = new Color(1, 1, 1, 0),
		};
		layout.AddChild(albert);
		var plate = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		var plateStyle = new StyleBoxFlat { BgColor = new Color(0.04f, 0.04f, 0.06f, 0.82f), BorderColor = new Color(0.55f, 0.42f, 0.2f) };
		plateStyle.SetBorderWidthAll(2);
		plateStyle.SetCornerRadiusAll(6);
		plate.AddThemeStyleboxOverride("panel", plateStyle);
		plate.AddChild(scroll);
		var plateMargin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		foreach (var side in new[] { "top", "right", "bottom" })
			plateMargin.AddThemeConstantOverride($"margin_{side}", 20);
		plateMargin.AddChild(plate);
		layout.AddChild(plateMargin);
		stage.AddChild(layout);

		var dialog = new AcceptDialog { Title = "エンディング ― 失われた理想郷", OkButtonText = "ギルドを続ける" };
		dialog.AddChild(stage);
		dialog.Confirmed += () => CloseDialogThenRun(dialog, () => afterClose?.Invoke());
		dialog.Canceled += () => CloseDialogThenRun(dialog, () => afterClose?.Invoke());
		AddChild(dialog);
		dialog.PopupCentered();

		double revealSeconds = Math.Clamp(narration.GetTotalCharacterCount() * 0.06, 3.0, 14.0);
		var tween = dialog.CreateTween();
		dialog.CreateTween().TweenProperty(albert, "modulate:a", 1.0, 1.5);
		tween.TweenProperty(narration, "visible_ratio", 1.0, revealSeconds);
		tween.TweenProperty(record, "modulate:a", 1.0, 1.2);
		void Skip(InputEvent e)
		{
			if (e is InputEventMouseButton { Pressed: true } && tween.IsValid())
			{
				tween.Kill();
				narration.VisibleRatio = 1f;
				record.Modulate = Colors.White;
				albert.Modulate = Colors.White;
			}
		}
		narration.GuiInput += Skip;
		scroll.GuiInput += Skip;
		stage.GuiInput += Skip;
	}

	/// <summary>エンディングの語り（世界観は 01_コンセプト.md）。</summary>
	/// <summary>
	/// 締めの語り（§0.89：台詞ファイルの s04_narration、正本は物語帳「エンディング 4　締めの語り」）。
	/// 最初の〔…〕を見出し、残りの〔…〕を段落、台詞を斜体の「…」、最後の〔…〕を灰色の結びにする。
	/// </summary>
	private string EndingNarration()
	{
		var lines = StorySystem.Narration(_state);
		var sb = new StringBuilder();
		for (int i = 0; i < lines.Count; i++)
		{
			var line = lines[i];
			string text = line.Text.Replace("[", "[lb]");
			if (i == 0 && line.Kind == StoryLineKind.Narration)
				sb.Append($"[font_size=22][b]{text}[/b][/font_size]\n\n");
			else if (line.Kind == StoryLineKind.Speech)
				sb.Append($"[i]「{text}」[/i]\n\n");
			else if (i == lines.Count - 1)
				sb.Append($"[color=gray]{text}。[/color]");
			else
				sb.Append($"{text}。\n\n");
		}
		return sb.ToString();
	}

	/// <summary>システム画面の「エンディングを見直す」：都の心臓・祝宴・初出撃の場面から見せ、締めの語りとギルドの記録の窓へ（§0.89）。</summary>
	private void ReplayEnding()
	{
		void OnClosed()
		{
			_storyPopup.Closed -= OnClosed;
			Callable.From(() => ShowEnding()).CallDeferred();
		}
		_storyPopup.Closed += OnClosed;
		_storyPopup.ShowScenes(_state, StorySystem.EndingScenes(_state));
	}

	/// <summary>エンディングに並べるギルドの記録。</summary>
	private static string EndingRecord(GuildChronicle c)
	{
		var sb = new StringBuilder();
		sb.AppendLine("[font_size=20][b]ギルドの記録[/b][/font_size]");
		sb.AppendLine($"・制覇：{GameCalendar.Format(c.ClearedAtWeek)}（{c.Years}年目）");
		if (c.ClearingMemberNames.Count > 0)
			sb.AppendLine($"・深淵100Fを制した部隊：{string.Join("・", c.ClearingMemberNames)}");
		sb.AppendLine($"・在籍した冒険者：{c.TotalMembers}名（現役 {c.ActiveCount}・引退 {c.RetiredCount}・強制除籍 {c.ExpelledCount}）");
		if (c.DaughterCount > 0)
			sb.AppendLine($"・魂魄融和で生まれた娘：{c.DaughterCount}名（最も新しい世代：第{c.MaxGeneration}世代）");
		else
			sb.AppendLine("・魂魄融和で生まれた娘：なし");
		sb.AppendLine($"・倒した階層ボス：{c.BossesDefeated} / {c.TotalBosses}体（制覇したフィールド {c.FieldsConquered} / {c.TotalFields}）");
		sb.AppendLine($"・出撃の回数：{c.TotalDispatchCount}回");
		if (c.TopContributorName != null)
			sb.AppendLine($"・最も功績を挙げた冒険者：{c.TopContributorName}（功績 {c.TopContributorScore} pt）");
		return sb.ToString();
	}

	/// <summary>
	/// 契約交渉の状況を週報ログに報告する（→ 03 §5.2）。
	/// 警告中の全員に残り猶予週数を毎週リマインドし、契約解除された者を報告する。
	/// </summary>
	private void LogNegotiationStatus(List<Adventurer> terminated)
	{
		foreach (var a in _state.Adventurers)
		{
			if (!a.NeedsNegotiation) continue;
			int remaining = Math.Max(0, SatisfactionBalance.NegotiationGraceWeeks - a.NegotiationWeeksElapsed);
			AppendLog($"[color=orange][b]⚠ {a.Name} が契約に不満（満足度{a.Satisfaction}）。" +
				$"あと{remaining}週以内に昇給かボーナスで対応しないと退団する。[/b][/color]");
		}

		foreach (var a in terminated)
		{
			AppendLog($"[color=red][b]✕ {a.Name} が契約を解除し、他都市へ移籍した。[/b][/color]");
		}
	}



	/// <summary>
	/// 出来事を知らせる（§0.80）。月を進めている間は月報の小窓の「週ごとの記録」に書き（§0.71）、
	/// それ以外（研究・売却・購入・方針の変更など、その場の操作の結果）は画面下のお知らせに数秒だけ出す。
	/// 旧・右の「作戦週報」の欄は、月報と二重で、大迷宮の画面でしか見えなかったため撤去した。
	/// </summary>
	private void AppendLog(string bbcodeText)
	{
		if (_logCapture != null)
		{
			_logCapture.AppendLine(bbcodeText);
			return;
		}
		ShowToast(bbcodeText);
	}

	// ---- お知らせ（§0.80） ----
	private const int ToastMax = 4;
	private const double ToastSeconds = 3.5;

	/// <summary>お知らせを積む箱：画面下の中央、「次の月へ」の上。マウス操作は下の画面へ通す。</summary>
	private void BuildToastBox()
	{
		_toastBox = new VBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Alignment = BoxContainer.AlignmentMode.End,
			CustomMinimumSize = new Vector2(900, 0),
		};
		_toastBox.AddThemeConstantOverride("separation", 6);
		AddChild(_toastBox);
		_toastBox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom, Control.LayoutPresetMode.KeepWidth);
		_toastBox.GrowVertical = Control.GrowDirection.Begin;
		_toastBox.GrowHorizontal = Control.GrowDirection.Both;
		_toastBox.OffsetBottom = -72; // フッター（次の月へ）の上
	}

	/// <summary>お知らせを1つ出す。ToastSeconds 秒後に消える。多すぎるときは古いものから消す。</summary>
	private void ShowToast(string bbcodeText)
	{
		while (_toastBox.GetChildCount() >= ToastMax)
		{
			var oldest = _toastBox.GetChild(0);
			_toastBox.RemoveChild(oldest);
			oldest.QueueFree();
		}

		var style = new StyleBoxFlat { BgColor = new Color(0.08f, 0.09f, 0.12f, 0.94f), BorderColor = new Color(0.55f, 0.47f, 0.3f) };
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(6);
		style.SetContentMarginAll(10);
		var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		panel.AddThemeStyleboxOverride("panel", style);
		var label = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, MouseFilter = Control.MouseFilterEnum.Ignore };
		label.AppendText(bbcodeText);
		panel.AddChild(label);
		_toastBox.AddChild(panel);

		var tween = panel.CreateTween();
		tween.TweenInterval(ToastSeconds);
		tween.TweenProperty(panel, "modulate:a", 0.0f, 0.4f);
		tween.TweenCallback(Callable.From(panel.QueueFree));
	}

	/// <summary>
	/// メニューナビゲーションバーによるメインビューの切り替え（§0.80で右の週報の欄を撤去したので、どの画面も中央ペインが全幅）。
	/// </summary>
	private void SwitchView(DashboardView view)
	{
		_centerPanel.CurrentTab = (int)view;

		UpdateButtonHighlights(view);
	}

	/// <summary>
	/// ナビゲーションボタンのアクティブ状態および視覚強調（トグル押し込み・ハイライト色）を更新する。
	/// </summary>
	private void UpdateButtonHighlights(DashboardView activeView)
	{
		var buttons = new (DashboardView View, Button Btn)[]
		{
			(DashboardView.Dungeon, _navDungeonBtn),
			(DashboardView.Party, _navPartyBtn),
			(DashboardView.Research, _navResearchBtn),
			(DashboardView.Facility, _navFacilityBtn),
			(DashboardView.Warehouse, _navWarehouseBtn),
			(DashboardView.Shop, _navShopBtn),
			(DashboardView.Hall, _navHallBtn),
			(DashboardView.Tournament, _navTournamentBtn),
			(DashboardView.System, _navSystemBtn)
		};

		foreach (var (view, btn) in buttons)
		{
			bool isActive = (view == activeView);
			btn.SetPressedNoSignal(isActive);
			if (isActive)
			{
				btn.Modulate = new Color(1.0f, 1.0f, 0.5f, 1.0f);
			}
			else
			{
				btn.Modulate = new Color(0.9f, 0.9f, 0.9f, 1.0f);
			}
		}
	}

	/// <summary>季節のアイコン（ヘッダーの暦表示用）。</summary>
	private static string SeasonIcon(Season season) => season switch
	{
		Season.Spring => "🌸",
		Season.Summer => "☀️",
		Season.Autumn => "🍁",
		_ => "❄️",
	};

	/// <summary>季節の文字色（ヘッダーの暦表示用）。</summary>
	private static Color SeasonColor(Season season) => season switch
	{
		Season.Spring => new Color("#f9a8d4"),
		Season.Summer => new Color("#fcd34d"),
		Season.Autumn => new Color("#fb923c"),
		_ => new Color("#93c5fd"),
	};

	private void RefreshAll()
	{
		// 暦の表記「1年目 🌸春 第3週」（→ Core の GameCalendar。1年48週＝4季節×12週、第1週＝春の第1週）。
		var season = GameCalendar.SeasonOf(_state.WeekNumber);
		_weekLabel.Text = $"{GameCalendar.YearOf(_state.WeekNumber)}年目 {SeasonIcon(season)}{GameCalendar.SeasonLabel(season)} 第{GameCalendar.WeekOfSeason(_state.WeekNumber)}週";
		_weekLabel.AddThemeColorOverride("font_color", SeasonColor(season));
		_weekLabel.MouseFilter = Control.MouseFilterEnum.Pass;
		_weekLabel.TooltipText = $"通算 第{_state.WeekNumber}週（その年の第{GameCalendar.WeekOfYear(_state.WeekNumber)}週）\n" +
			$"1年＝{GameCalendar.WeeksPerYear}週＝春夏秋冬×{GameCalendar.WeeksPerSeason}週。毎年 春の第1週に新春採用試験（2年目から）。";
		_goldLabel.Text = $"所持金: {_state.Gold} G";
		// マスターの機嫌（→ 03 §8.1。旧・ギルド格付け／名声の表示枠を流用）。
		var moodTier = MasterMoodSystem.GetTier(_state.MasterMood);
		// §0.80：段階名と数値だけを見せ、内職の倍率と段階の境目はツールチップへ回す（ヘッダーが詰まって読みにくかった）。
		_rankLabel.Text = $"マスターの機嫌：{MoodTierLabel(moodTier)}（{_state.MasterMood}）";
		_rankLabel.AddThemeColorOverride("font_color", moodTier switch
		{
			MasterMoodTier.Cheerful => new Color(0.55f, 0.9f, 0.55f),
			MasterMoodTier.Normal => new Color(0.85f, 0.85f, 0.85f),
			MasterMoodTier.Grumpy => new Color(1.0f, 0.7f, 0.3f),
			_ => new Color(1.0f, 0.4f, 0.4f),
		});
		_rankLabel.MouseFilter = Control.MouseFilterEnum.Pass;
		_rankLabel.TooltipText = $"マスター（アルベール）の機嫌 {_state.MasterMood}/100。大迷宮での成果で上がり、何もしないと下がる。0になると副官（あなた）は解雇される。\n" +
			$"内職の売上 ×{MasterMoodSystem.GetSideJobMultiplier(moodTier):0.0}（上機嫌 {MasterMoodBalance.TierThresholdCheerful}以上 ×{MasterMoodSystem.GetSideJobMultiplier(MasterMoodTier.Cheerful):0.0}／平常 {MasterMoodBalance.TierThresholdNormal}以上 ×{MasterMoodSystem.GetSideJobMultiplier(MasterMoodTier.Normal):0.0}／" +
			$"不機嫌 {MasterMoodBalance.TierThresholdGrumpy}以上 ×{MasterMoodSystem.GetSideJobMultiplier(MasterMoodTier.Grumpy):0.0}／危機 ×{MasterMoodSystem.GetSideJobMultiplier(MasterMoodTier.Crisis):0.0}）";
		// 同時出撃枠の使用状況（→ コアシステム刷新仕様「4. 進行管理」）。
		// 大迷宮へ出撃中の部隊の数で枠を消費する（→ DungeonExpeditionSystem.CanDispatch）。
		_squadSlotLabel.Text = $"出撃中の部隊：{_state.ActiveDungeonMissions.Count}/{_state.UnlockedSquadSlots}";
		_squadSlotLabel.MouseFilter = Control.MouseFilterEnum.Pass;
		_squadSlotLabel.TooltipText = $"同時に大迷宮へ出せる部隊は{_state.UnlockedSquadSlots}つまで（出撃中 {_state.ActiveDungeonMissions.Count}）。森の節目のボスを倒すと増える。";

		_adventurerPanel.Refresh(_state);
		_dungeonPanel.Refresh(_state);
		_partyFormationPanel.Refresh(_state);
		_researchPanel.Refresh(_state);
		_facilityPanel.Refresh(_state);
		_inventoryPanel.Refresh(_state);
		_systemPanel.SetEndingAvailable(_state.IsGameCleared);
		_shopPanel.Refresh(_state);
		_tournamentPanel.Refresh(_state);
		_tournamentPanel.Refresh(_state);
		_hallPanel.Refresh(_state);

		UpdateButtonHighlights((DashboardView)_centerPanel.CurrentTab);
		RefreshGuideAndTabs();
		CheckInteractiveStory(); // 部隊を組んだ・方針を付けたなどの場面（§0.86）
	}





	private static string FacilityLabel(FacilityType type) => type switch
	{
		FacilityType.Dormitory => "宿舎",
		FacilityType.Infirmary => "医務室",
		FacilityType.WarRoom => "作戦資料室",
		FacilityType.Tavern => "ギルド酒場",
		FacilityType.DrillHall => "鍛錬所",
		FacilityType.Academy => "学問所",
		FacilityType.SkillHall => "技巧所",
		FacilityType.RecruitmentOffice => "冒険者支援室",
		_ => type.ToString()
	};

}
