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
	private QuestResolver _questResolver = null!;
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
	private QuestDispatchSystem _questDispatchSystem = null!;
	private GuildRankSystem _guildRankSystem = null!;
	private SecuritySystem _securitySystem = null!;
	private QuestBoardSystem _questBoardSystem = null!;
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
	private Label _threatLabel = null!;
	private ItemList _questList = null!;
	private ItemList _dispatchPartyList = null!;
	private Button _temporarySwapButton = null!;
	private ItemList _adventurerList = null!;
	private RichTextLabel _adventurerDetailLabel = null!;
	private TextureRect _portraitTextureRect = null!;
	private RichTextLabel _resultLog = null!;
	private Button _nextWeekButton = null!;
	private Button _autoSkipButton = null!;
	private RecruitmentPopup _recruitmentPopup = null!;
	private Button _raiseWageButton = null!;
	private Button _payBonusButton = null!;
	private FacilityPopup _facilityPopup = null!;
	private Button _facilityButton = null!;
	private AdvisorPopup _advisorPopup = null!;
	private Button _advisorButton = null!;
	private Button _retireButton = null!;
	private EquipmentPopup _equipmentPopup = null!;
	private Button _equipmentButton = null!;
	private Button _saveButton = null!;
	private PartyFormationPopup _partyFormationPopup = null!;
	private Button _partyFormationButton = null!;
	private TemporarySwapPopup _temporarySwapPopup = null!;

	/// <summary>ステータス詳細パネルに表示中の冒険者。週送り後もこの人物の表示を維持する。</summary>
	private Guid? _detailAdventurerId;

	/// <summary>
	/// 派遣直前の一時的な入れ替え結果（→ 03 §4.0.2）。null＝一時編成なし（保存された
	/// パーティーのMemberIdsをそのまま使う）。次週へ進めるたびに必ずリセットされる
	/// （「今回の出撃だけ」の一時的な適用のため、SavedParty自体は変更しない）。
	/// </summary>
	private List<Guid> _temporaryDispatchMemberIds;

	public override void _Ready()
	{
		_weekLabel = GetNode<Label>("%WeekLabel");
		_goldLabel = GetNode<Label>("%GoldLabel");
		_rankLabel = GetNode<Label>("%RankLabel");
		_threatLabel = GetNode<Label>("%ThreatLabel");
		_questList = GetNode<ItemList>("%QuestList");
		_dispatchPartyList = GetNode<ItemList>("%DispatchPartyList");
		_temporarySwapButton = GetNode<Button>("%TemporarySwapButton");
		_adventurerList = GetNode<ItemList>("%AdventurerList");
		_adventurerDetailLabel = GetNode<RichTextLabel>("%AdventurerDetailLabel");
		_portraitTextureRect = GetNode<TextureRect>("%PortraitTextureRect");
		_resultLog = GetNode<RichTextLabel>("%ResultLog");
		_nextWeekButton = GetNode<Button>("%NextWeekButton");
		_autoSkipButton = GetNode<Button>("%AutoSkipButton");
		_recruitmentPopup = GetNode<RecruitmentPopup>("%RecruitmentPopup");
		_raiseWageButton = GetNode<Button>("%RaiseWageButton");
		_payBonusButton = GetNode<Button>("%PayBonusButton");
		_facilityPopup = GetNode<FacilityPopup>("%FacilityPopup");
		_facilityButton = GetNode<Button>("%FacilityButton");
		_advisorPopup = GetNode<AdvisorPopup>("%AdvisorPopup");
		_advisorButton = GetNode<Button>("%AdvisorButton");
		_retireButton = GetNode<Button>("%RetireButton");
		_equipmentPopup = GetNode<EquipmentPopup>("%EquipmentPopup");
		_equipmentButton = GetNode<Button>("%EquipmentButton");
		_partyFormationPopup = GetNode<PartyFormationPopup>("%PartyFormationPopup");
		_partyFormationButton = GetNode<Button>("%PartyFormationButton");
		_temporarySwapPopup = GetNode<TemporarySwapPopup>("%TemporarySwapPopup");

		// 中央ペインのタブ化（→ 03 §9、項目57）。タブ本体（QuestDispatchTab/AdventurerTab）は
		// 既存の命名規約（ノード名は英語、日本語表示はtext/title側）に合わせて英語名のままにし、
		// タブ見出しはここでコードから設定する（初期表示は「クエスト・派遣」＝タブ0）。
		var centerPanel = GetNode<TabContainer>("%CenterPanel");
		centerPanel.SetTabTitle(0, "クエスト・派遣");
		centerPanel.SetTabTitle(1, "冒険者");

		_adventurerList.ItemClicked += OnAdventurerItemClicked;
		_nextWeekButton.Pressed += OnNextWeekPressed;
		_autoSkipButton.Pressed += OnAutoSkipButtonPressed;
		_recruitmentPopup.Closed += OnRecruitmentPopupClosed;
		_raiseWageButton.Pressed += OnRaiseWagePressed;
		_payBonusButton.Pressed += OnPayBonusPressed;
		_facilityButton.Pressed += OnFacilityButtonPressed;
		_facilityPopup.Closed += OnFacilityPopupClosed;
		_advisorButton.Pressed += OnAdvisorButtonPressed;
		_advisorPopup.Closed += OnAdvisorPopupClosed;
		_retireButton.Pressed += OnRetirePressed;
		_equipmentButton.Pressed += OnEquipmentButtonPressed;
		_equipmentPopup.Closed += OnEquipmentPopupClosed;
		_partyFormationButton.Pressed += OnPartyFormationButtonPressed;
		_partyFormationPopup.Closed += OnPartyFormationPopupClosed;
		_temporarySwapButton.Pressed += OnTemporarySwapButtonPressed;
		_temporarySwapPopup.Applied += OnTemporarySwapApplied;
		_saveButton = GetNode<Button>("%SaveButton");
		_saveButton.Pressed += OnSaveButtonPressed;

		// シード固定の乱数。同じシードなら毎回同じ結果になる（デバッグしやすくするため）。
		// 戦闘用と加齢用で別インスタンス・別シードにし、互いの抽選回数が結果に影響しないようにする。
		_questResolver = new QuestResolver(new SeededRng(42));
		_economySystem = new EconomySystem();
		_injuryRecoverySystem = new InjuryRecoverySystem();
		_agingSystem = new AgingSystem(new SeededRng(99));
		_growthSystem = new GrowthSystem(new SeededRng(7));
		_restRecoverySystem = new RestRecoverySystem();
		_trainingSystem = new TrainingSystem();
		_recruitmentSystem = new RecruitmentSystem(new SeededRng(2024));
		_satisfactionSystem = new SatisfactionSystem();
		_compatibilitySystem = new CompatibilitySystem(new SeededRng(2525));
		_facilitySystem = new FacilitySystem();
		_questDispatchSystem = new QuestDispatchSystem(_questResolver, _growthSystem, _economySystem, _satisfactionSystem, _compatibilitySystem);
		_guildRankSystem = new GuildRankSystem();
		_securitySystem = new SecuritySystem(new SeededRng(4649));
		_questBoardSystem = new QuestBoardSystem(new SeededRng(1192));
		_subsidySystem = new SubsidySystem();
		_defeatSystem = new DefeatSystem();
		_advisorSystem = new AdvisorSystem();
		_equipmentSystem = new EquipmentSystem();
		_partyFormationSystem = new PartyFormationSystem();
		// 週次決算のオーケストレーション（→ 03 §1.3・自動スキップ）。既存の各Systemインスタンスを
		// そのまま共有し、二重管理（別インスタンスによる状態不整合）を避ける。
		_weekProcessingSystem = new WeekProcessingSystem(
			_questDispatchSystem, _guildRankSystem, _securitySystem, _questBoardSystem,
			_economySystem, _subsidySystem, _trainingSystem, _injuryRecoverySystem,
			_restRecoverySystem, _growthSystem, _satisfactionSystem, _agingSystem,
			_facilitySystem, _defeatSystem, _recruitmentSystem);
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
		_state = new GameState
		{
			Adventurers = SampleData.CreateStarterAdventurers(),
			AvailableQuests = SampleData.CreateStarterQuests(),
		};

		RefreshAll();

		if (_questList.ItemCount > 0)
			_questList.Select(0);
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
		dialog.QueueFree();

		var loaded = _saveLoadService.Load();
		if (loaded == null)
		{
			ShowLoadFailedPrompt();
			return;
		}

		_state = loaded;
		RefreshAll();
		if (_questList.ItemCount > 0)
			_questList.Select(0);
		AppendLog($"[color=cyan]セーブデータから再開しました（第{_state.WeekNumber}週）。[/color]");
	}

	private void OnNewGameChosen(ConfirmationDialog dialog)
	{
		dialog.QueueFree();
		StartNewGame();
	}

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
		dialog.Confirmed += () => { dialog.QueueFree(); StartNewGame(); };
		dialog.Canceled += () => { dialog.QueueFree(); StartNewGame(); };
		AddChild(dialog);
		dialog.PopupCentered();
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
	/// 冒険者一覧のクリックでステータス詳細パネルを更新する（→ 03 §4.0.2改訂で
	/// 派遣メンバーの選択はDispatchPartyList側へ移ったため、この一覧は閲覧専用になった）。
	/// </summary>
	private void OnAdventurerItemClicked(long index, Vector2 atPosition, long mouseButtonIndex)
	{
		ShowAdventurerDetail(_state.Adventurers[(int)index]);
	}

	/// <summary>「パーティー編成」ボタン。永続的なパーティー編成画面を開く（→ 03 §4.0.2）。</summary>
	private void OnPartyFormationButtonPressed()
	{
		_partyFormationPopup.Open(_state, _partyFormationSystem);
	}

	/// <summary>パーティー編成画面が閉じた時のコールバック。編成の変化（未編成一覧等）を反映する。</summary>
	private void OnPartyFormationPopupClosed()
	{
		RefreshAll();
	}

	/// <summary>
	/// 「出撃メンバーを一時編成する」ボタン。選択中のパーティーを基準に、今回の出撃だけの
	/// 一時的な入れ替えを行う（→ 03 §4.0.2。SavedParty自体は変更しない）。
	/// </summary>
	private void OnTemporarySwapButtonPressed()
	{
		var selected = _dispatchPartyList.GetSelectedItems();
		if (selected.Length == 0)
		{
			AppendLog("[color=orange]一時編成する前に、出撃パーティーを選択してください。[/color]");
			return;
		}

		var savedParty = _state.SavedParties[selected[0]];
		var baseline = _temporaryDispatchMemberIds ?? savedParty.MemberIds;
		_temporarySwapPopup.Open(_state, baseline);
	}

	/// <summary>一時編成が確定した時のコールバック。今週の出撃にのみ適用する（SavedPartyは不変）。</summary>
	private void OnTemporarySwapApplied(List<Guid> memberIds)
	{
		_temporaryDispatchMemberIds = memberIds;
		AppendLog("[color=cyan]今回の出撃メンバーを一時的に変更した（保存されている編成は変わらない）。[/color]");
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
	/// 「次週へ」の本体。
	/// パーティを選んでいれば派遣（複数週クエストは満了週まで結果が出ない。→ 03 §4.0.1）し、
	/// 選んでいなければ「休養」として扱う。どちらの場合も、週給引き落とし・負傷回復・
	/// 週数の進行は必ず行う（→ 03 §3.6：全員負傷中でも時間を進めて回復を待てるようにするため）。
	/// </summary>
	private void OnNextWeekPressed()
	{
		int thisWeek = _state.WeekNumber;
		var selectedQuestIndices = _questList.GetSelectedItems();
		var selectedPartyIndices = _dispatchPartyList.GetSelectedItems();
		bool wantsToDispatch = selectedPartyIndices.Length > 0;

		if (wantsToDispatch)
		{
			if (selectedQuestIndices.Length == 0)
			{
				AppendLog("[color=orange]クエストを選択してください。[/color]");
				return;
			}

			// 派遣時、編成メンバーのうち出撃可能な者だけで自動的に出撃する（→ 03 §4.0.2）。
			// 一時編成（_temporaryDispatchMemberIds）が設定されていればそちらを優先し、
			// 無ければ保存されている編成（SavedParty.MemberIds）をそのまま使う。
			var savedParty = _state.SavedParties[selectedPartyIndices[0]];
			var memberIdSource = _temporaryDispatchMemberIds ?? savedParty.MemberIds;

			foreach (var unavailable in PartyFormationSystem.GetUnavailableMembers(_state, memberIdSource))
			{
				string reason = unavailable.IsDispatched ? "派遣中" : unavailable.IsRetired ? "引退済み" : "重傷";
				AppendLog($"[color=gray]{unavailable.Name}は{reason}のため出撃できません。[/color]");
			}

			var party = PartyFormationSystem.BuildDispatchParty(_state, memberIdSource);
			if (party.IsEmpty)
			{
				AppendLog($"[color=orange]「{savedParty.Name}」は出撃可能なメンバーがいないため、今週は派遣できません。[/color]");
			}
			else
			{
				var quest = _state.AvailableQuests[selectedQuestIndices[0]];
				_questDispatchSystem.Dispatch(_state, party, quest);

				if (quest.DurationWeeks > 1)
				{
					AppendLog($"[color=cyan]第{thisWeek}週：「{savedParty.Name}」（{party.Members.Count}名）が{quest.Name}へ出発した" +
						$"（拘束{quest.DurationWeeks}週間、第{thisWeek + quest.DurationWeeks - 1}週に結果判明）。[/color]");
				}
			}
		}
		else
		{
			AppendLog($"[color=gray]第{thisWeek}週：今週は誰も出撃せず、静養に努めた。[/color]");
		}

		// 一時編成は「今回の出撃だけ」の適用のため、週送りのたびに必ずリセットする（→ 03 §4.0.2）。
		_temporaryDispatchMemberIds = null;

		// 派遣の選択（出撃操作）以外の週次決算処理は、WeekProcessingSystemに集約されている
		// （→ 03 §1.3。手動の「次週へ」・自動スキップの両方がこの同じ実装を経由することで、
		// 挙動が食い違わないようにしている）。
		var settlement = _weekProcessingSystem.ProcessWeek(_state);
		LogWeeklySettlement(settlement);

		// 自動保存：毎週の決算処理完了後、次週の番号に進めた直後に行う（→ 03 §12）。
		_saveLoadService.Save(_state);

		RefreshAll();

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
	}

	/// <summary>
	/// 週次決算処理（WeekProcessingSystem.ProcessWeek）の結果を週報ログに反映する。
	/// 手動の「次週へ」・自動スキップ（結果サマリー表示前の詳細ログとして）の両方から呼ばれる
	/// 共通ログ処理（→ 03 §1.3）。
	/// </summary>
	private void LogWeeklySettlement(WeeklySettlementResult settlement)
	{
		int weekNumber = settlement.Flags.Week;

		for (int i = 0; i < settlement.DispatchResolutions.Count; i++)
		{
			var resolution = settlement.DispatchResolutions[i];
			LogResult(weekNumber, resolution.Quest, resolution.Result);
			LogGrowthEvents(resolution.GrowthEvents);
			LogFallenAdventurers(weekNumber, resolution.Party, resolution.Result.FallenAdventurerIds); // → 03 §4.3.1

			var (threatQuest, threatDelta) = settlement.ResolvedQuestThreatDeltas[i];
			LogThreatChange(threatQuest, threatDelta);
		}

		// 受注可能クエスト一覧の週次管理（→ 03 §4.0・§4.4）：期限切れ（放置）の除去と補充。
		// 放置された討伐クエストは脅威度上昇の対象になる。
		foreach (var (expiredQuest, abandonedThreatDelta) in settlement.AbandonedQuestThreatDeltas)
		{
			if (abandonedThreatDelta != 0)
				AppendLog($"[color=orange]「{expiredQuest.Name}」が期限切れで放置された。脅威度+{abandonedThreatDelta}%。[/color]");
		}

		// 討伐クエストの期限切れ「1週前」警告（→ 03 §1.3自動スキップ停止条件8）。
		foreach (var expiring in settlement.QuestsExpiringNextWeek)
			AppendLog($"[color=orange][b]⏰ 討伐クエスト「{expiring.Name}」が来週、期限切れになる。[/b][/color]");

		if (settlement.SubsidyAmount.HasValue)
			AppendLog($"[color=lime]月次助成金 {settlement.SubsidyAmount.Value}G を受け取った{(_state.ThreatLevel > SecurityBalance.SubsidyCutThreatThreshold ? "（脅威度75%超のため50%カット済み）" : "")}。[/color]");

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

		// Aランク新規到達フラグ（→ 03 §8.2、v1.10改訂）：最終討伐クエスト自体の中身は未実装。
		if (settlement.Flags.FinalQuestNewlyUnlocked)
		{
			AppendLog("[color=gold][font_size=20][b]★ ギルドがAランクに到達し、最終討伐クエストの依頼が" +
				"持ち込まれるようになった…！（詳細は追って判明する）[/b][/font_size][/color]");
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
		DisableWeekAdvancement();

		int startWeek = _state.WeekNumber;
		var results = _autoSkipService.AutoSkip(_state);

		AppendLog($"[color=cyan][b]≫≫ 自動スキップ：第{startWeek}週から{results.Count}週分を処理した。[/b][/color]");

		int facilityCount = results.Count(r => r.FacilityConstructionCompleted);
		int multiWeekReturnCount = results.Count(r => r.MultiWeekQuestReturned);
		int deathCount = results.Count(r => r.DeathOrPermanentInjuryOccurred);
		int satisfactionCount = results.Count(r => r.SatisfactionWarningOccurred);
		int threatCount = results.Count(r => r.ThreatThresholdNewlyCrossed);
		int expiringCount = results.Count(r => r.SubjugationQuestExpiringNextWeek);
		int finalQuestCount = results.Count(r => r.FinalQuestNewlyUnlocked);
		int recruitmentCount = results.Count(r => r.RecruitmentTrialOccurred);
		int defeatCount = results.Count(r => r.DefeatOccurred);

		if (facilityCount > 0) AppendLog($"[color=lime]・施設建設が完了した週：{facilityCount}回[/color]");
		if (multiWeekReturnCount > 0) AppendLog($"[color=cyan]・複数週クエストが帰還した週：{multiWeekReturnCount}回[/color]");
		if (deathCount > 0) AppendLog($"[color=red][b]・戦死または不可逆の障害が発生した週：{deathCount}回[/b][/color]");
		if (satisfactionCount > 0) AppendLog($"[color=orange]・契約交渉（満足度警告）が新たに発生した週：{satisfactionCount}回[/color]");
		if (threatCount > 0) AppendLog($"[color=orange][b]・脅威度が75%または100%を新たに跨いだ週：{threatCount}回[/b][/color]");
		if (expiringCount > 0) AppendLog($"[color=orange][b]・討伐クエストが翌週期限切れになる週：{expiringCount}回[/b][/color]");
		if (finalQuestCount > 0) AppendLog("[color=gold][b]★ ギルドがAランクに到達し、最終討伐クエストの依頼が持ち込まれるようになった…！[/b][/color]");
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

	/// <summary>「施設投資」ボタン。施設投資ポップアップを開く（採用試験とは異なり、いつでも自由に開閉できる）。</summary>
	private void OnFacilityButtonPressed()
	{
		_facilityPopup.Open(_state, _facilitySystem);
	}

	/// <summary>施設投資ポップアップが閉じた時のコールバック。着工・所持金の変化を反映する。</summary>
	private void OnFacilityPopupClosed()
	{
		RefreshAll();
	}

	/// <summary>「顧問管理」ボタン。顧問役職割り当てポップアップを開く（いつでも自由に開閉できる）。</summary>
	private void OnAdvisorButtonPressed()
	{
		_advisorPopup.Open(_state, _advisorSystem);
	}

	/// <summary>顧問管理ポップアップが閉じた時のコールバック。役職配置の変化を反映する。</summary>
	private void OnAdvisorPopupClosed()
	{
		RefreshAll();
	}

	/// <summary>
	/// 「引退させる（顧問候補にする）」ボタン（→ 03 §7「引退の経路」）。表示中の冒険者を
	/// 40歳未満でも任意のタイミングで早期引退させ、顧問候補にする。退職金は既存の
	/// 40歳強制引退と共通の処理（AgingSystem.RetireVoluntarily）で支給する。
	/// </summary>
	private void OnRetirePressed()
	{
		var target = CurrentDetailAdventurer();
		if (target == null) return;

		if (target.IsDispatched)
		{
			AppendLog($"[color=gray]{target.Name} は派遣中のため引退させられません（帰還を待ってください）。[/color]");
			return;
		}

		_agingSystem.RetireVoluntarily(_state, target);
		AppendLog($"[color=cyan]{target.Name} が引退し、顧問候補になった。[/color]");
		RefreshAll();
	}

	/// <summary>「装備」ボタン。表示中の冒険者の装備購入・着脱ポップアップを開く（→ 03 §4.2.2）。</summary>
	private void OnEquipmentButtonPressed()
	{
		var target = CurrentDetailAdventurer();
		if (target == null) return;

		_equipmentPopup.Open(_state, target, _equipmentSystem);
	}

	/// <summary>装備ポップアップが閉じた時のコールバック。所持金・装備の変化を反映する。</summary>
	private void OnEquipmentPopupClosed()
	{
		RefreshAll();
	}

	private void LogResult(int weekNumber, Quest quest, WeekResolutionResult result)
	{
		var sb = new StringBuilder();
		sb.AppendLine($"[b]第{weekNumber}週：{quest.Name}[/b]");
		sb.AppendLine($"遭遇: {result.Encounter} / 結果: {result.Outcome} (Ratio={result.Ratio:F2})");
		sb.AppendLine(result.QuestAchieved
			? $"達成！報酬 {result.RewardGold} G"
			: "任務失敗。報酬なし。");

		foreach (var kv in result.HpLostByAdventurer)
		{
			var adv = _state.Adventurers.FirstOrDefault(a => a.Id == kv.Key);
			if (adv != null)
				sb.AppendLine($" - {adv.Name}: HP -{kv.Value}（残りHP {adv.CurrentHP}/{adv.MaxHP}）");
		}

		AppendLog(sb.ToString());
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
	/// 討伐クエストの解決に伴う脅威度の増減を週報ログに報告する（→ 03 §4.4）。
	/// 討伐クエスト以外（脅威度に影響しない）やクランプで実質変化が無かった場合は何も表示しない。
	/// </summary>
	private void LogThreatChange(Quest quest, int threatDelta)
	{
		if (threatDelta == 0) return;

		string reason = threatDelta < 0 ? "達成" : "失敗";
		string sign = threatDelta > 0 ? "+" : "";
		AppendLog($"[color=orange]討伐クエスト「{quest.Name}」{reason}により脅威度{sign}{threatDelta}%。[/color]");
	}

	/// <summary>
	/// 戦死した冒険者を週報ログに報告する（→ 03 §4.3・§4.3.1）。
	/// 見逃さないよう赤・太字で目立たせる。氏名はPartyから引く
	/// （戦死者はGameState.Adventurersから既に除外済みのため）。
	/// </summary>
	private void LogFallenAdventurers(int weekNumber, Party party, HashSet<Guid> fallenIds)
	{
		foreach (var id in fallenIds)
		{
			var fallen = party.Members.FirstOrDefault(m => m.Id == id);
			if (fallen != null)
				AppendLog($"[color=red][b]† {fallen.Name} が戦死しました（第{weekNumber}週）。[/b][/color]");
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

	/// <summary>
	/// 「昇給する」ボタン（→ 03 §5.2）。表示中の冒険者の週給を1.5倍に引き上げる
	/// （倍率選択UIは未実装のため、仕様の下限=最小限の昇給で固定。→ 03 §5.2）。
	/// </summary>
	private void OnRaiseWagePressed()
	{
		var target = CurrentDetailAdventurer();
		if (target == null) return;

		_satisfactionSystem.RaiseWage(target, 1.5);
		AppendLog($"[color=lime]{target.Name} の週給を {target.WeeklyWage}G に引き上げた。[/color]");
		RefreshAll();
	}

	/// <summary>「ボーナスを払う」ボタン（→ 03 §5.2）。表示中の冒険者に一時金を支給する。</summary>
	private void OnPayBonusPressed()
	{
		var target = CurrentDetailAdventurer();
		if (target == null) return;

		int bonus = target.WeeklyWage * SatisfactionBalance.BonusWeeksEquivalent;
		_satisfactionSystem.PayBonus(_state, target);
		AppendLog($"[color=lime]{target.Name} にボーナス {bonus}G を支給した。[/color]");
		RefreshAll();
	}

	private Adventurer CurrentDetailAdventurer() =>
		_detailAdventurerId.HasValue
			? _state.Adventurers.FirstOrDefault(a => a.Id == _detailAdventurerId.Value)
			: null;

	private void AppendLog(string bbcodeText)
	{
		_resultLog.AppendText(bbcodeText + "\n");
	}

	private void RefreshAll()
	{
		_weekLabel.Text = $"週: {_state.WeekNumber}";
		_goldLabel.Text = $"所持金: {_state.Gold} G";
		_rankLabel.Text = $"ギルド格付け: {_state.GuildRank}ランク（名声 {_state.Reputation}）";
		_threatLabel.Text = $"脅威度: {_state.ThreatLevel}%";

		_questList.Clear();
		foreach (var q in _state.AvailableQuests)
		{
			_questList.AddItem($"[{q.Rank}] {q.Name}（{QuestTypeLabel(q.QuestType)} / 難易度{q.Difficulty} / " +
				$"規模{ScaleLabel(q.Scale)}・{q.DurationWeeks}週 / 報酬{q.RewardGold}G / 期限あと{q.DeadlineWeeks}週）");
		}

		// 出撃パーティー一覧（→ 03 §4.0.2）：保存済み編成のうち、現時点で出撃可能な人数を添えて表示する。
		_dispatchPartyList.Clear();
		foreach (var party in _state.SavedParties)
		{
			int availableCount = PartyFormationSystem.BuildDispatchParty(_state, party.MemberIds).Members.Count;
			int totalCount = party.MemberIds.Count(id => _state.Adventurers.Any(a => a.Id == id));
			_dispatchPartyList.AddItem($"{party.Name}（{availableCount}/{totalCount}名 出撃可能）");
		}

		_adventurerList.Clear();
		foreach (var a in _state.Adventurers)
		{
			string status = a.Injury == InjurySeverity.Severe
				? $"【重傷・出撃不可・回復まで{a.InjuryWeeksRemaining}週】"
				: a.IsDispatched
					? $"【派遣中・残り{GetDispatchWeeksRemaining(a)}週】"
					: "";
			_adventurerList.AddItem($"{a.Name}（{a.JobClass}・{PlacementLabel(a.Placement)}） HP{a.CurrentHP}/{a.MaxHP}　総合PA{a.TotalPA:F1}　{a.Age}歳 {status}");
		}

		RefreshAdventurerDetail();
	}

	/// <summary>
	/// ステータス詳細パネルを、直近にクリックされた冒険者の最新の値で再描画する。
	/// 週送り直後もパネルの表示対象を維持するため RefreshAll から毎回呼び出す。
	///
	/// 引退・装備・昇給・ボーナス支給は冒険者個人に関する事項のため、必ず
	/// 「冒険者を選択してから選ぶ」（→ ユーザー要望）。先頭の冒険者を暗黙に選択済み
	/// 扱いにするフォールバックは行わない（選択せずに誤って引退等を実行することを防ぐ）。
	/// 選択中の冒険者が居なくなった場合（引退・戦死・週送り直後など）も、
	/// 自動的に別の誰かへ選択を移さず、未選択状態に戻す。
	/// </summary>
	private void RefreshAdventurerDetail()
	{
		var target = CurrentDetailAdventurer();
		if (target != null)
			ShowAdventurerDetail(target);
		else
			ShowNoAdventurerSelected();
	}

	/// <summary>
	/// 冒険者が未選択の状態（ゲーム開始直後・週送りで選択対象が居なくなった場合等）の表示。
	/// 個人操作系ボタン（昇給・ボーナス支給・引退・装備）を無効化する。
	/// </summary>
	private void ShowNoAdventurerSelected()
	{
		_detailAdventurerId = null;
		_adventurerDetailLabel.Clear();
		_adventurerDetailLabel.AppendText("[color=gray]左の一覧から冒険者を選択してください。[/color]");
		_portraitTextureRect.Texture = null; // 未選択時はポートレートも表示しない
		_raiseWageButton.Disabled = true;
		_payBonusButton.Disabled = true;
		_retireButton.Disabled = true;
		_equipmentButton.Disabled = true;
	}

	/// <summary>冒険者1名分のステータス詳細（仕様書 03 §2）を詳細パネルに表示する。</summary>
	private void ShowAdventurerDetail(Adventurer a)
	{
		_detailAdventurerId = a.Id;

		var sb = new StringBuilder();
		sb.AppendLine($"[b]{a.Name}[/b]（{a.JobClass}） {a.Age}歳・{AgeBandLabel(a.AgeBand)}");
		sb.AppendLine($"配置: {PlacementLabel(a.Placement)}");
		sb.AppendLine($"HP {a.CurrentHP}/{a.MaxHP}　満足度 {a.Satisfaction}/100");
		sb.AppendLine(InjuryLabel(a));
		if (a.TraitIds.Count > 0)
			sb.AppendLine($"特性: {string.Join("、", a.TraitIds.Select(TraitLabel))}");
		if (a.NeedsNegotiation)
		{
			int remaining = Math.Max(0, SatisfactionBalance.NegotiationGraceWeeks - a.NegotiationWeeksElapsed);
			sb.AppendLine($"[color=orange]⚠ 契約交渉中（あと{remaining}週で対応しないと退団）[/color]");
		}
		sb.AppendLine();
		sb.AppendLine("[b]能力値（実効値 / 潜在能力PA）[/b]");
		sb.AppendLine($"STR {a.STR} / {a.PA_STR}　　VIT {a.VIT} / {a.PA_VIT}　　AGI {a.AGI} / {a.PA_AGI}");
		sb.AppendLine($"DEX {a.DEX} / {a.PA_DEX}　　MND {a.MND} / {a.PA_MND}　　INT {a.INT} / {a.PA_INT}");
		sb.AppendLine($"LDR {a.LDR} / {a.PA_LDR}");
		sb.AppendLine($"総合PA: {a.TotalPA:F1}");
		sb.AppendLine();
		sb.AppendLine("[b]装備[/b]（→ 03 §4.2.2）");
		sb.AppendLine($"武器: {EquipmentLabel(a.EquippedWeaponId)}　　防具: {EquipmentLabel(a.EquippedArmorId)}");
		sb.AppendLine($"アクセサリー1: {EquipmentLabel(a.EquippedAccessory1Id)}　　アクセサリー2: {EquipmentLabel(a.EquippedAccessory2Id)}");
		sb.AppendLine();
		sb.AppendLine($"週給: {a.WeeklyWage} G");

		_adventurerDetailLabel.Clear();
		_adventurerDetailLabel.AppendText(sb.ToString());
		_portraitTextureRect.Texture = LoadPortraitTexture(a.PortraitId);

		// 冒険者を選択したので、個人操作系ボタンを有効化する（→ ShowNoAdventurerSelectedの対）。
		_raiseWageButton.Disabled = false;
		_payBonusButton.Disabled = false;
		_retireButton.Disabled = false;
		_equipmentButton.Disabled = false;
	}

	/// <summary>
	/// ポートレート画像を解決する（→ 03 §2.1、項目62）。PortraitIdが設定されていれば
	/// `res://assets/portraits/{PortraitId}.png` を読み込み、未設定またはファイルが
	/// 存在しない場合はシルエット画像（unknown_silhouette.png）を返す。採用組の
	/// ほとんどはPortraitId未設定になるため、シルエット表示は異常系ではなく正常系。
	///
	/// 将来のモンタージュ方式（職業×性別×装備での合成）への布石：装備の
	/// VisualPartIdと同じ「IDだけCoreに持たせ、実際の画像解決はGodot側」という
	/// パターンに揃えてあるため、「PortraitIdが無い場合はシルエット」という
	/// この分岐を「PortraitIdが無い場合は職業×性別×装備から合成する」に
	/// 差し替えるだけで済む。
	/// </summary>
	private static Texture2D LoadPortraitTexture(string portraitId)
	{
		if (!string.IsNullOrEmpty(portraitId))
		{
			string path = $"res://assets/portraits/{portraitId}.png";
			if (ResourceLoader.Exists(path))
			{
				var texture = GD.Load<Texture2D>(path);
				if (texture != null)
					return texture;
			}
		}

		return GD.Load<Texture2D>("res://assets/portraits/unknown_silhouette.png");
	}

	/// <summary>装備スロット表示用のラベル（未装備ならその旨を表示する。→ 03 §4.2.2）。</summary>
	private static string EquipmentLabel(string itemId)
	{
		if (itemId == null) return "なし";
		var item = ItemCatalog.FindById(itemId);
		return item?.Name ?? "（不明）";
	}

	private static string PlacementLabel(Placement placement) => placement switch
	{
		Placement.Front => "前衛",
		Placement.Back => "後衛",
		_ => placement.ToString()
	};

	private static string ScaleLabel(QuestScale scale) => scale switch
	{
		QuestScale.Small => "小",
		QuestScale.Medium => "中",
		QuestScale.Large => "大",
		_ => scale.ToString()
	};

	private static string QuestTypeLabel(QuestType type) => type switch
	{
		QuestType.Subjugation => "討伐",
		QuestType.Exploration => "調査・探索",
		QuestType.Escort => "護衛",
		_ => type.ToString()
	};

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

	/// <summary>指定した冒険者が派遣中の案件の残り週数を返す（派遣中でなければ0）。</summary>
	private int GetDispatchWeeksRemaining(Adventurer a)
	{
		foreach (var dispatch in _state.ActiveDispatches)
			if (dispatch.Party.Members.Contains(a))
				return dispatch.WeeksRemaining;
		return 0;
	}

	private static string AgeBandLabel(AgeBand band) => band switch
	{
		AgeBand.GrowthPeriod => "成長期",
		AgeBand.PrimePeriod => "全盛期",
		AgeBand.MaturePeriod => "円熟期",
		AgeBand.LimitPeriod => "限界期",
		_ => band.ToString()
	};

	private static string InjuryLabel(Adventurer a) => a.Injury switch
	{
		InjurySeverity.None => "負傷: なし",
		InjurySeverity.Light => $"負傷: 軽傷（全治まで{a.InjuryWeeksRemaining}週）",
		InjurySeverity.Severe => $"[color=red]負傷: 重傷・出撃不可（全治まで{a.InjuryWeeksRemaining}週）[/color]",
		_ => a.Injury.ToString()
	};

	/// <summary>特性IDの表示名を返す（→ 03 §5.3）。カタログに無いIDはそのまま表示する（防御的フォールバック）。</summary>
	private static string TraitLabel(string traitId) => TraitCatalog.FindById(traitId)?.DisplayName ?? traitId;
}
