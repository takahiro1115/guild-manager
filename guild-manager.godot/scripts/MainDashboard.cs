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
/// 満足度・施設・格付け等はまだ扱わない（→ docs/06_タスクリスト.md Phase 3以降）。
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
	private GuildRankSystem _guildRankSystem = null!;
	private SubsidySystem _subsidySystem = null!;
	private DefeatSystem _defeatSystem = null!;
	private AdvisorSystem _advisorSystem = null!;
	private EquipmentSystem _equipmentSystem = null!;
	private SaveLoadService _saveLoadService = null!;
	private PartyFormationSystem _partyFormationSystem = null!;
	private WeekProcessingSystem _weekProcessingSystem = null!;
	private AutoSkipService _autoSkipService = null!;

	private Label _weekLabel = null!;
	private Label _goldLabel = null!;
	private Label _rankLabel = null!;

	/// <summary>同時出撃枠の使用状況（→ コアシステム刷新仕様「4. 進行管理」）。</summary>
	private Label _squadSlotLabel = null!;

	/// <summary>ヘッダー領域の素材保有数サマリー（→ 2026年9月UI整理）。</summary>
	private Label _materialSummaryLabel = null!;

	private ItemList _adventurerList = null!;
	private RichTextLabel _adventurerDetailLabel = null!;
	private TextureRect _portraitTextureRect = null!;
	private RichTextLabel _resultLog = null!;
	private Button _nextWeekButton = null!;
	private Button _autoSkipButton = null!;
	private RecruitmentPopup _recruitmentPopup = null!;
	private AdvisorPopup _advisorPopup = null!;
	private Button _advisorButton = null!;
	private EquipmentPopup _equipmentPopup = null!;
	private Button _saveButton = null!;

	// ---- メニューナビゲーションバー & ペイン制御 ----
	private enum DashboardView
	{
		Dungeon = 0,
		Adventurer = 1,
		Party = 2,
		Research = 3,
		Facility = 4
	}

	private TabContainer _centerPanel = null!;
	private Control _rightPanel = null!;
	private Button _navDungeonBtn = null!;
	private Button _navAdventurerBtn = null!;
	private Button _navPartyBtn = null!;
	private Button _navResearchBtn = null!;
	private Button _navFacilityBtn = null!;

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

	/// <summary>大迷宮へ1部隊も出撃予定が無いまま週を進めようとした時の確認ダイアログ。</summary>
	private ConfirmationDialog _noDungeonDispatchDialog = null!;

	// ---- 決戦（ボス）ログのステップ再生（→ コアシステム刷新仕様 Phase 3） ----
	// 通常の任務は結果を一括表示してテンポを優先するが、大迷宮のボス討伐だけは
	// 1行ずつ間を置いて流し、「手に汗握る」時間を作る。自動スキップ経路は
	// LogWeeklySettlementを通らない（サマリーのみ表示する）ため、ここと競合しない。

	/// <summary>ステップ再生の1行あたりの表示間隔（秒）。</summary>
	private const double BossLogStepSeconds = 0.7;

	private readonly Queue<string> _bossLogQueue = new();
	private bool _bossPlaybackActive;

	/// <summary>
	/// ステップ再生の完了後に実行する処理（採用ポップアップ等の割り込み）。
	/// 再生中にポップアップが被さって演出が台無しになるのを避けるため、決算直後ではなく
	/// 再生完了まで遅延させる。
	/// </summary>
	private Action _afterBossPlayback;

	public override void _Ready()
	{
		_weekLabel = GetNode<Label>("%WeekLabel");
		_goldLabel = GetNode<Label>("%GoldLabel");
		_rankLabel = GetNode<Label>("%RankLabel");
		_squadSlotLabel = GetNode<Label>("%SquadSlotLabel");
		_materialSummaryLabel = GetNode<Label>("%MaterialSummaryLabel");
		_resultLog = GetNode<RichTextLabel>("%ResultLog");
		_nextWeekButton = GetNode<Button>("%NextWeekButton");
		_autoSkipButton = GetNode<Button>("%AutoSkipButton");
		_recruitmentPopup = GetNode<RecruitmentPopup>("%RecruitmentPopup");
		_advisorPopup = GetNode<AdvisorPopup>("%AdvisorPopup");
		_equipmentPopup = GetNode<EquipmentPopup>("%EquipmentPopup");

		_centerPanel = GetNode<TabContainer>("%CenterPanel");
		_centerPanel.TabsVisible = false;

		_rightPanel = GetNode<Control>("%RightPanel");

		_navDungeonBtn = GetNode<Button>("%NavDungeonBtn");
		_navAdventurerBtn = GetNode<Button>("%NavAdventurerBtn");
		_navPartyBtn = GetNode<Button>("%NavPartyBtn");
		_navResearchBtn = GetNode<Button>("%NavResearchBtn");
		_navFacilityBtn = GetNode<Button>("%NavFacilityBtn");

		_navDungeonBtn.Pressed += () => SwitchView(DashboardView.Dungeon);
		_navAdventurerBtn.Pressed += () => SwitchView(DashboardView.Adventurer);
		_navPartyBtn.Pressed += () => SwitchView(DashboardView.Party);
		_navResearchBtn.Pressed += () => SwitchView(DashboardView.Research);
		_navFacilityBtn.Pressed += () => SwitchView(DashboardView.Facility);

		_dungeonPanel = GetNode<DungeonPanel>("%DungeonTab");
		_dungeonPanel.LogRequested += AppendLog;
		_dungeonPanel.StateChanged += RefreshAll;

		_adventurerPanel = GetNode<AdventurerPanel>("%AdventurerDetailTab");
		_adventurerPanel.LogRequested += AppendLog;
		_adventurerPanel.StateChanged += RefreshAll;
		_adventurerPanel.EquipmentRequested += OnAdventurerEquipmentRequested;
		_adventurerPanel.AdvisorRequested += OnAdvisorButtonPressed;
		_advisorButton = _adventurerPanel.AdvisorButton;

		_partyFormationPanel = GetNode<PartyFormationPanel>("%PartyFormationTab");
		_partyFormationPanel.StateChanged += RefreshAll;

		_researchPanel = GetNode<ResearchPanel>("%ResearchTab");
		_researchPanel.LogRequested += AppendLog;
		_researchPanel.StateChanged += RefreshAll;

		_facilityPanel = GetNode<FacilityPanel>("%FacilityTab");
		_facilityPanel.StateChanged += RefreshAll;

		SwitchView(DashboardView.Dungeon);

		_noDungeonDispatchDialog = new ConfirmationDialog
		{
			Title = "確認",
			DialogText = "大迷宮へ部隊を派遣していませんが、週を進めますか？",
			OkButtonText = "週を進める",
			CancelButtonText = "やめる",
		};
		// 確定直後に採用試験ポップアップ等を開くことがあるため、ダイアログが閉じ切ってから
		// 週送りを実行する（同一フレームで別の排他ウィンドウを開くとGodotがエラーにする）。
		_noDungeonDispatchDialog.Confirmed += () => Callable.From(AdvanceWeek).CallDeferred();
		AddChild(_noDungeonDispatchDialog);

		_nextWeekButton.Pressed += OnNextWeekPressed;
		_autoSkipButton.Pressed += OnAutoSkipButtonPressed;
		_recruitmentPopup.Closed += OnRecruitmentPopupClosed;
		_advisorPopup.Closed += OnAdvisorPopupClosed;
		_equipmentPopup.Closed += OnEquipmentPopupClosed;
		_saveButton = GetNode<Button>("%SaveButton");
		_saveButton.Pressed += OnSaveButtonPressed;

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
		_guildRankSystem = new GuildRankSystem();
		_subsidySystem = new SubsidySystem();
		_defeatSystem = new DefeatSystem();
		_advisorSystem = new AdvisorSystem();
		_equipmentSystem = new EquipmentSystem();
		_partyFormationSystem = new PartyFormationSystem();
		_partyFormationPanel.Initialize(_partyFormationSystem);
		_facilityPanel.Initialize(_facilitySystem);
		// 大迷宮（→ ダンジョン攻略システム）。調査・討伐は別シードにし、互いの抽選回数が結果に
		// 影響しないようにする。強制除籍の余波（満足度・相性）は既存インスタンスを共有する。
		_dungeonExpeditionSystem = new DungeonExpeditionSystem(
			new ScoutingResolver(new SeededRng(1453)), new DungeonResolver(new SeededRng(1588)),
			_satisfactionSystem, _compatibilitySystem);
		_dungeonPanel.Initialize(_dungeonExpeditionSystem);
		_adventurerPanel.Initialize(_satisfactionSystem, _agingSystem);
		// 週次決算のオーケストレーション（→ 03 §1.3・自動スキップ）。既存の各Systemインスタンスを
		// そのまま共有し、二重管理（別インスタンスによる状態不整合）を避ける。
		_weekProcessingSystem = new WeekProcessingSystem(
			_guildRankSystem, _economySystem, _subsidySystem, _trainingSystem, _injuryRecoverySystem,
			_restRecoverySystem, _growthSystem, _satisfactionSystem, _agingSystem,
			_facilitySystem, _defeatSystem, _recruitmentSystem,
			_dungeonExpeditionSystem);
		_autoSkipService = new AutoSkipService(_weekProcessingSystem);
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
		// 旧通常クエスト（掲示板・受託依頼）は2026年9月、大迷宮への完全一本化で撤去した。
		// 出撃導線は大迷宮タブのみ。
		_state = new GameState
		{
			Adventurers = SampleData.CreateStarterAdventurers(),
			DungeonFields = SampleData.CreateDefaultFields(),
		};

		RefreshAll();

		if (_recruitmentSystem.IsTutorialRecruitmentWeek(_state.WeekNumber))
		{
			// 第1週チュートリアル採用試験（→ 初期編成改訂仕様）。初期固定メンバー3名に加え、
			// ここで3名を即時提示し選抜契約することで計6名体制になる。通常の新春採用試験
			// （418行目付近）と同様、意思決定（採用する/見送る）が済むまで次週へ進めさせない。
			DisableWeekAdvancement();
			_recruitmentPopup.Open(_state, _recruitmentSystem, RecruitmentBalance.TutorialCandidateCount);
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
		};
		dialog.Confirmed += () => OnContinueChosen(dialog);
		dialog.Canceled += () => OnNewGameChosen(dialog);
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
		RefreshAll();
		AppendLog($"[color=cyan]セーブデータから再開しました（第{_state.WeekNumber}週）。[/color]");
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

	/// <summary>「セーブ」ボタン（手動保存）。押すと即座にセーブし、完了を週報ログに通知する（→ 03 §12）。</summary>
	private void OnSaveButtonPressed()
	{
		_saveLoadService.Save(_state);
		AppendLog("[color=lime]セーブしました。[/color]");
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Keycode: Key.Space })
		{
			OnNextWeekPressed();
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
	}

	/// <summary>
	/// 「次週へ」「自動スキップ」の両方を無効化する（→ 03 §1.3）。ゲームオーバー時・
	/// 新春採用試験ポップアップ表示中・自動スキップ実行中など、週を進める操作全体を
	/// ブロックしたい場面でまとめて呼ぶ。
	/// </summary>
	private void DisableWeekAdvancement()
	{
		_nextWeekButton.Disabled = true;
		_autoSkipButton.Disabled = true;
	}

	/// <summary>「次週へ」「自動スキップ」の両方を再び有効化する（→ DisableWeekAdvancementの対）。</summary>
	private void EnableWeekAdvancement()
	{
		_nextWeekButton.Disabled = false;
		_autoSkipButton.Disabled = false;
	}

	/// <summary>
	/// 「次週へ」ボタン（Spaceキー連動）。出撃は大迷宮タブで週送り前に予約しておく方式のため、
	/// 大迷宮へ1部隊も出撃予定が無ければ、うっかり週を進めないよう確認ダイアログを挟む。
	/// </summary>
	private void OnNextWeekPressed()
	{
		if (_noDungeonDispatchDialog.Visible)
			return;

		if (_state.ActiveDungeonMissions.Count == 0)
		{
			_noDungeonDispatchDialog.PopupCentered();
			return;
		}

		AdvanceWeek();
	}

	/// <summary>
	/// 週送りの本体。週給引き落とし・負傷回復・週数の進行は出撃の有無にかかわらず必ず行う
	/// （→ 03 §3.6：全員負傷中でも時間を進めて回復を待てるようにするため）。
	/// </summary>
	private void AdvanceWeek()
	{
		int thisWeek = _state.WeekNumber;

		if (_state.ActiveDungeonMissions.Count == 0)
			AppendLog($"[color=gray]第{thisWeek}週：今週は誰も出撃せず、静養に努めた。[/color]");

		// 出撃操作以外の週次決算処理は、WeekProcessingSystemに集約されている
		// （→ 03 §1.3。手動の「次週へ」・自動スキップの両方がこの同じ実装を経由することで、
		// 挙動が食い違わないようにしている）。
		var settlement = _weekProcessingSystem.ProcessWeek(_state);
		LogWeeklySettlement(settlement);

		// 自動保存：毎週の決算処理完了後、次週の番号に進めた直後に行う（→ 03 §12）。
		_saveLoadService.Save(_state);

		RefreshAll();

		// 決戦（ボス討伐）のログが溜まっていれば、先にステップ再生を流し、
		// ポップアップ等の割り込みはその完了後に回す（→ Phase 3）。
		if (_bossLogQueue.Count > 0 && !_bossPlaybackActive)
		{
			_afterBossPlayback = () => HandlePostSettlementInterruptions(settlement);
			StartBossPlayback();
			return;
		}

		HandlePostSettlementInterruptions(settlement);
	}

	/// <summary>
	/// 週次決算の直後に割り込ませる処理（ゲームオーバー確定・新春採用試験）。
	/// 決戦ログのステップ再生がある週は、再生完了後にここが呼ばれる（→ StartBossPlayback）。
	/// </summary>
	private void HandlePostSettlementInterruptions(WeeklySettlementResult settlement)
	{
		if (settlement.Flags.DefeatOccurred)
		{
			DisableWeekAdvancement(); // ゲームオーバー：これ以上週を進められない
		}
		else if (settlement.Flags.RecruitmentTrialOccurred)
		{
			// 新春採用試験（2年目以降の新年第1週のみ）。ポップアップが閉じるまで次週へ進めさせない（→ 03 §9）。
			DisableWeekAdvancement();
			_recruitmentPopup.Open(_state, _recruitmentSystem);
		}
		else if (_state.DefeatReason == null)
		{
			// 割り込みが何も無かった場合の復帰。決戦ログのステップ再生中は週送りを
			// 止めているため、ここで戻さないとボタンが無効のまま操作不能になる
			// （敗北確定後だけは、意図的に無効のまま据え置く）。
			EnableWeekAdvancement();
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
		}

		if (settlement.SubsidyAmount.HasValue)
			AppendLog($"[color=lime]月次助成金 {settlement.SubsidyAmount.Value}G を受け取った。[/color]");

		LogGrowthEvents(settlement.TrainingGrowthEvents); // → 03 §3.1〜3.4：成長トリガー経路2（訓練場配置）
		LogNegotiationStatus(settlement.NegotiationTerminated); // → 03 §5.2：契約交渉・退団

		if (settlement.CompletedFacility != null)
			AppendLog($"[color=lime][b]🏗 {FacilityLabel(settlement.CompletedFacility.Type)}がLv{settlement.CompletedFacility.CurrentLevel}に完成した！[/b][/color]");

		if (settlement.RankChange != null)
		{
			string message = settlement.RankChange.IsPromotion
				? $"[color=gold][b]🏅 ギルド格付けが{settlement.RankChange.Current}ランクに昇格しました！[/b][/color]"
				: $"[color=orange][b]⚠ ギルド格付けが{settlement.RankChange.Current}ランクに降格しました。[/b][/color]";
			AppendLog(message);
		}

		// 敗北条件判定（→ 03 §8.3）：破産（所持金マイナス4週連続、猶予あり）／
		// 治安崩壊（脅威度100%到達、猶予なし即時敗北）。期限による敗北は無い。
		if (settlement.NewDefeatReason != null)
		{
			string reasonLabel = settlement.NewDefeatReason == DefeatReason.Bankruptcy ? "破産" : "治安崩壊";
			AppendLog($"[color=red][font_size=24][b]■■■ ゲームオーバー：{reasonLabel} ■■■[/b][/font_size][/color]");
			AppendLog(settlement.NewDefeatReason == DefeatReason.Bankruptcy
				? "[color=red]所持金マイナスが4週連続で解消されませんでした。[/color]"
				: "[color=red]脅威度が100%に到達し、街の治安が崩壊しました。[/color]");
		}
	}

	/// <summary>
	/// 「自動スキップ」ボタン（→ 03 §1.3）。停止条件（採用試験・満足度警告・施設完成・
	/// 複数週クエスト帰還・戦死古傷・脅威度閾値・Aランク到達・討伐期限切れ1週前のいずれか、
	/// またはゲームオーバー）が成立する週まで、もしくは上限週数に達するまで週次決算を
	/// 連続実行する。受注可能クエストへの操作は一切行わない（AutoSkipServiceが呼び出す
	/// WeekProcessingSystem.ProcessWeek自体に派遣操作が含まれない設計のため、これは
	/// 自然に満たされる）。
	///
	/// 実装メモ：処理自体はGodotのメインスレッド上で同期的に完了する（このプロジェクトに
	/// 非同期処理の仕組みは無い）ため、「処理完了までUIを無効化する」という指示の意図は、
	/// 呼び出しが返った後の後始末（DisableWeekAdvancement等）ではなく、処理中に他の
	/// 入力イベントが割り込む余地がそもそも無いという形で自然に満たされている。
	/// </summary>
	private void OnAutoSkipButtonPressed()
	{
		// 自動スキップは詳細な週報を出さない（→ 本メソッドのdocコメント）。大迷宮への出撃予定が残っていると、
		// 討伐の決着（強制除籍を含む）がサマリーに埋もれてしまうため、先に「次週へ」で決着させてもらう。
		if (_state.ActiveDungeonMissions.Count > 0)
		{
			AppendLog("[color=orange]大迷宮へ出撃予定の部隊がいるため、自動スキップできない。" +
				"「次週へ」で決着させるか、大迷宮タブで出撃を取り消すこと。[/color]");
			return;
		}

		DisableWeekAdvancement();

		int startWeek = _state.WeekNumber;
		var results = _autoSkipService.AutoSkip(_state);

		AppendLog($"[color=cyan][b]≫≫ 自動スキップ：第{startWeek}週から{results.Count}週分を処理した。[/b][/color]");

		int facilityCount = results.Count(r => r.FacilityConstructionCompleted);
		int deathCount = results.Count(r => r.DeathOrPermanentInjuryOccurred);
		int satisfactionCount = results.Count(r => r.SatisfactionWarningOccurred);
		int recruitmentCount = results.Count(r => r.RecruitmentTrialOccurred);
		int defeatCount = results.Count(r => r.DefeatOccurred);

		if (facilityCount > 0) AppendLog($"[color=lime]・施設建設が完了した週：{facilityCount}回[/color]");
		if (deathCount > 0) AppendLog($"[color=red][b]・強制除籍または不可逆の障害が発生した週：{deathCount}回[/b][/color]");
		if (satisfactionCount > 0) AppendLog($"[color=orange]・契約交渉（満足度警告）が新たに発生した週：{satisfactionCount}回[/color]");
		if (recruitmentCount > 0) AppendLog($"[color=yellow][b]・新春採用試験の週：{recruitmentCount}回[/b][/color]");
		if (defeatCount > 0) AppendLog("[color=red][font_size=24][b]■■■ ゲームオーバーが発生した ■■■[/b][/font_size][/color]");

		if (results.Count >= AutoSkipService.DefaultMaxWeeks && (results.Count == 0 || !results[^1].ShouldStopAutoSkip))
			AppendLog($"[color=gray]上限{AutoSkipService.DefaultMaxWeeks}週に到達したため停止した。[/color]");

		_saveLoadService.Save(_state);
		RefreshAll();

		var last = results.Count > 0 ? results[^1] : null;
		if (last != null && last.DefeatOccurred)
		{
			DisableWeekAdvancement(); // ゲームオーバー：これ以上週を進められない
		}
		else if (last != null && last.RecruitmentTrialOccurred)
		{
			// 新春採用試験：ポップアップが閉じるまで次週へ進めさせない（→ 03 §9）。
			_recruitmentPopup.Open(_state, _recruitmentSystem);
		}
		else
		{
			EnableWeekAdvancement();
		}
	}

	/// <summary>「顧問管理」ボタン。顧問役職割り当てポップアップを開く（いつでも自由に開閉できる）。</summary>
	private void OnAdvisorButtonPressed()
	{
		if (_state == null) return;
		_advisorPopup.Open(_state, _advisorSystem);
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

	/// <summary>決戦ログをステップ再生のキューへ積む（空行は除く）。</summary>
	private void EnqueueBossPlayback(string bbcodeBlock)
	{
		foreach (var line in bbcodeBlock.Split('\n'))
		{
			if (!string.IsNullOrWhiteSpace(line))
				_bossLogQueue.Enqueue(line.TrimEnd('\r'));
		}
	}

	/// <summary>
	/// 決戦ログを1行ずつ間を置いて流す。再生中は週送りを止め、完了後に
	/// 保留していた割り込み処理（→ _afterBossPlayback）を実行する。
	/// Godotのシグナル待ちを使うため async void だが、内部で例外を投げうる処理は行わない。
	/// </summary>
	private async void StartBossPlayback()
	{
		_bossPlaybackActive = true;
		DisableWeekAdvancement();

		while (_bossLogQueue.Count > 0)
		{
			AppendLog(_bossLogQueue.Dequeue());
			await ToSignal(GetTree().CreateTimer(BossLogStepSeconds), SceneTreeTimer.SignalName.Timeout);
		}

		_bossPlaybackActive = false;

		var followUp = _afterBossPlayback;
		_afterBossPlayback = null;

		if (followUp != null)
			followUp();
		else if (_state.DefeatReason == null)
			EnableWeekAdvancement();
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
		foreach (var line in FallenAdventurerLines(weekNumber, party, fallenIds))
			AppendLog(line);
	}

	/// <summary>致命傷→秘薬治療→強制除籍の報告文（→ LogFallenAdventurers）。大迷宮の決戦ログでも同じ文面を使う。</summary>
	private static IEnumerable<string> FallenAdventurerLines(int weekNumber, Party party, IEnumerable<Guid> fallenIds)
	{
		foreach (var id in fallenIds)
		{
			var fallen = party.Members.FirstOrDefault(m => m.Id == id);
			if (fallen == null)
				continue;

			yield return $"[color=red][b]✖ {fallen.Name} が致命傷を負った（第{weekNumber}週）。[/b][/color]";
			yield return $"[color=orange]アルベールの秘薬で一命は取り留めたが、「危ないじゃないか！」と激怒したマスターにより" +
				$"{fallen.Name}のギルド登録は強制抹消された。二度と戻らない。[/color]";
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
			sb.AppendLine($"[b]第{weekNumber}週：大迷宮 探索（採取）任務[/b]");
			sb.AppendLine($"[color=lime]【採取任務】{resolution.Field.Name}にて素材を回収（{materialName}×{gathering.MaterialCount}、" +
				$"換金{gathering.GoldEarned}Gを獲得）。[/color]");
			sb.AppendLine("[color=cyan]探索部隊は全員生還した。[/color]");
			AppendHpLossLines(sb, gathering.HpLostByAdventurer);

			AppendLog(sb.ToString());
			return;
		}

		if (resolution.ScoutingResult == null && resolution.TraversalResult == null && resolution.DungeonResult == null)
		{
			// 判定を伴わない帰還（討伐に向かったボスが既に倒されていた等）。
			sb.AppendLine($"[b]第{weekNumber}週：大迷宮 {resolution.Field.Name}からの帰還[/b]");
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
				? $"[b]第{weekNumber}週：大迷宮 第{boss.Floor}層「{boss.Name}」迷宮調査[/b]"
				: $"[b]第{weekNumber}週：大迷宮 第{boss.Floor}層「{boss.Name}」扉前での偵察[/b]");
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
			if (scouting.TierAdvanced)
				sb.AppendLine($"[color=gold][b]★ 新たな情報を掴んだ：解析段階が「{DungeonPanel.TierLabel(scouting.TierAfter)}」に到達！[/b][/color]");
			sb.AppendLine(survey
				? "[color=cyan]調査隊はギルドへ帰還した。[/color]"
				: "[color=cyan]部隊は扉前に留まり、突入か撤退かの指令を待っている。[/color]");
			AppendHpLossLines(sb, scouting.HpLostByAdventurer);

			AppendLog(sb.ToString());
			return;
		}

		if (resolution.TraversalResult != null)
		{
			var traversal = resolution.TraversalResult;
			sb.AppendLine($"[b]第{weekNumber}週：大迷宮 第{traversal.FloorBefore}層〜 道中進軍[/b]");
			sb.AppendLine(traversal.Rank switch
			{
				TraversalRank.Lightning => "[color=lime]◆ 敵の気配を巧みにかわし、電撃的に奥へ進んだ。[/color]",
				TraversalRank.Swift => "[color=cyan]◆ 淀みない足取りで、迅速に奥へ進んだ。[/color]",
				TraversalRank.Normal => "[color=cyan]◆ 着実に一歩ずつ、奥へ進んだ。[/color]",
				_ => "[color=orange]◆ 幾度も行く手を阻まれながら、なんとか奥へ進んだ。[/color]",
			});
			sb.AppendLine($"現在階層 第{traversal.FloorBefore}層 → 第{traversal.FloorAfter}層" +
				$"（+{traversal.FloorAfter - traversal.FloorBefore}階層）");
			if (traversal.IntelSpeedMultiplier > 1.0)
				sb.AppendLine($"[color=lime]◆ 解析済みの情報を活かし、走破速度 ×{traversal.IntelSpeedMultiplier:F1}で進んだ。[/color]");
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
				sb.AppendLine($"[color=gold][b]【扉前到達】部隊は第{traversal.TargetBoss.Floor}層ボスの扉前に到達。" +
					"突入準備を整えて指令を待機中[/b][/color]");
				sb.AppendLine("[color=gray]大迷宮タブで「ボス討伐に挑む」か「撤退・帰還する」かを指令すること。[/color]");
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
			AppendHpLossLines(sb, traversal.HpLostByAdventurer);

			AppendLog(sb.ToString());
			return;
		}

		var assault = resolution.DungeonResult;
		if (assault == null)
			return;

		sb.AppendLine($"[color=gold][b]⚔ 第{weekNumber}週：大迷宮 第{boss.Floor}層「{boss.Name}」討伐戦[/b][/color]");
		foreach (var type in assault.CounteredGimmicks)
			sb.AppendLine($"[color=lime]✔ {DungeonPanel.GimmickLabel(type)}への備えが機能した。[/color]");
		foreach (var type in assault.UncounteredGimmicks)
			sb.AppendLine($"[color=red]✖ {DungeonPanel.GimmickLabel(type)}に対抗できず、部隊が大きな損害を受けた。[/color]");
		if (assault.FullIntelBonusApplied)
			sb.AppendLine("[color=gold]◆ 完全解析の成果：弱点を正確に突いた。[/color]");

		sb.AppendLine(assault.Outcome == DungeonOutcome.Victory
			? $"[color=gold][font_size=20][b]🏆 【階層ボス撃破】部隊は見事に「{boss.Name}」を討伐した！ 第{boss.Floor}層を踏破！[/b][/font_size][/color]"
			: "[color=orange][b]火力が及ばず、撤退を余儀なくされた。ボスは傷を癒やし、次は仕切り直しになる。[/b][/color]");

		if (assault.Outcome == DungeonOutcome.Victory)
		{
			// 撃破報酬の内訳（→ FloorBoss.RewardGold/RewardReputation/RewardMaterialId、2026年9月新設）。
			string rewardLine = $"💰 報奨獲得：{boss.RewardGold} G ／ 👑 名声 +{boss.RewardReputation}";
			if (!string.IsNullOrEmpty(boss.RewardMaterialId))
				rewardLine += $" ／ 📦 {MaterialBalance.GetName(boss.RewardMaterialId)} ×{boss.RewardMaterialCount}";
			sb.AppendLine($"[color=lime]{rewardLine}[/color]");
		}

		AppendHpLossLines(sb, assault.HpLostByAdventurer);
		foreach (var line in FallenAdventurerLines(weekNumber, resolution.Party, assault.ForceRetiredAdventurerIds))
			sb.AppendLine(line);
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

		EnqueueBossPlayback(sb.ToString());
	}

	/// <summary>帰還時にギルドへ格納した道中の拾得物（何も無ければ出さない）。</summary>
	private static void AppendDepositLine(StringBuilder sb, DungeonMissionResolution resolution)
	{
		if (resolution.DepositedGold > 0 || resolution.DepositedMaterials.Count > 0)
			sb.AppendLine($"[color=lime]📦 道中の拾得物を格納：{LootText(resolution.DepositedGold, resolution.DepositedMaterials)}[/color]");
	}

	private static string LootText(int gold, Dictionary<string, int> materials)
	{
		var parts = new List<string> { $"{gold}G" };
		parts.AddRange(materials.Select(kv => $"{MaterialBalance.GetName(kv.Key)}×{kv.Value}"));
		return string.Join("、", parts);
	}

	/// <summary>HP消費の内訳（現役ロースターに残っている者のみ。強制除籍者は別途報告する）。</summary>
	private void AppendHpLossLines(StringBuilder sb, Dictionary<Guid, int> hpLostByAdventurer)
	{
		foreach (var kv in hpLostByAdventurer)
		{
			var adv = _state.Adventurers.FirstOrDefault(a => a.Id == kv.Key);
			if (adv != null)
				sb.AppendLine($" - {adv.Name}: HP -{kv.Value}（残りHP {adv.CurrentHP}/{adv.MaxHP}）");
		}
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



	private void AppendLog(string bbcodeText)
	{
		_resultLog.AppendText(bbcodeText + "\n");
	}

	/// <summary>
	/// メニューナビゲーションバーによるメインビューの切り替え。
	/// 大迷宮画面のみ右ペイン（作戦週報）を表示し、内政画面（人事・編成・研究・施設）では
	/// 週報ペインを非表示にして中央ペインを画面横幅の全領域（約1900px）へ拡張する。
	/// </summary>
	private void SwitchView(DashboardView view)
	{
		_centerPanel.CurrentTab = (int)view;
		_rightPanel.Visible = (view == DashboardView.Dungeon);

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
			(DashboardView.Adventurer, _navAdventurerBtn),
			(DashboardView.Party, _navPartyBtn),
			(DashboardView.Research, _navResearchBtn),
			(DashboardView.Facility, _navFacilityBtn)
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

	private void RefreshAll()
	{
		_weekLabel.Text = $"週: {_state.WeekNumber}";
		_goldLabel.Text = $"所持金: {_state.Gold} G";
		_rankLabel.Text = $"ギルド格付け: {_state.GuildRank}ランク（名声 {_state.Reputation}）";
		// 同時出撃枠の使用状況（→ コアシステム刷新仕様「4. 進行管理」）。
		// 大迷宮へ出撃中の部隊の数で枠を消費する（→ DungeonExpeditionSystem.CanDispatch）。
		_squadSlotLabel.Text = $"出撃枠: {_state.ActiveDungeonMissions.Count}/{_state.UnlockedSquadSlots}";
		RefreshMaterialSummary();

		_adventurerPanel.Refresh(_state);
		_dungeonPanel.Refresh(_state);
		_partyFormationPanel.Refresh(_state);
		_researchPanel.Refresh(_state);
		_facilityPanel.Refresh(_state);
	}

	/// <summary>
	/// ヘッダー領域に主要素材の保有数をコンパクトに表示する（→ 2026年9月UI整理）。
	/// 開放済みフィールドの素材と、在庫が1以上ある素材のみを表示する。
	/// </summary>
	private void RefreshMaterialSummary()
	{
		var allMaterials = MaterialBalance.GetAll();
		var unlockedFieldIds = _state.DungeonFields
			.Where(f => f.IsUnlocked)
			.Select(f => f.Id)
			.ToHashSet();

		var parts = allMaterials
			.Where(m => unlockedFieldIds.Contains(m.FieldId) ||
			            (_state.Materials.TryGetValue(m.Id, out int c) && c > 0))
			.Select(m =>
			{
				int stock = _state.Materials.TryGetValue(m.Id, out int count) ? count : 0;
				return $"{m.Name}:{stock}";
			})
			.ToList();

		_materialSummaryLabel.Text = parts.Count > 0
			? $"素材: {string.Join("  ", parts)}"
			: "";
	}





	private static string FacilityLabel(FacilityType type) => type switch
	{
		FacilityType.Dormitory => "宿舎",
		FacilityType.Infirmary => "医務室",
		FacilityType.WarRoom => "作戦資料室",
		FacilityType.Tavern => "ギルド酒場",
		FacilityType.WarriorHall => "戦士訓練所",
		FacilityType.Church => "教会",
		FacilityType.MageLab => "魔法研究所",
		FacilityType.ScoutPost => "斥候所",
		FacilityType.RecruitmentOffice => "冒険者支援室",
		_ => type.ToString()
	};

}
