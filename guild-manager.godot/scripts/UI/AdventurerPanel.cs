#nullable enable
using Godot;
using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 「部隊・冒険者」画面の右ペイン＝個人詳細パネル（→ 03 §9、2026年9月に冒険者人事と部隊編成を統合）。
///
/// 個人ステータス詳細（基本情報、7大能力値の3層バー〈→ TripleStatBar〉、8年稼働タイムライン、
/// 特性スロット最大5枠、装備欄、操作ボタン群）を表示する。表示対象は部隊編成パネルの
/// 編成スロット・候補行のクリックで選ぶ（→ ShowAdventurer。旧・自前の冒険者一覧は廃止）。
///
/// 厳格ルール準拠：
/// 1. Core層の完全独立維持（Core変更ゼロ、描画と入力変換のみに徹する）
/// 2. Unique Name (%NodeName) による安全なノード参照
/// 3. 未選択状態の厳守（初期表示や更新時に自動で先頭を選択しない）
/// </summary>
public partial class AdventurerPanel : VBoxContainer
{
	private GameState _state = null!;

	/// <summary>志願者の表示（→ ShowCandidatePreview）で見立てに使うギルドの状態（§0.74：目利きはギルドで決まる）。</summary>
	private GameState _previewState = null!;
	private SatisfactionSystem _satisfactionSystem = null!;
	private AgingSystem _agingSystem = null!;

	// ---- 右ペイン：未選択 / 詳細表示切り替え ----
	private PanelContainer _noSelectionPanel = null!;
	private VBoxContainer _detailContainer = null!;

	// ---- 基本情報カード ----
	private TextureRect _portraitTextureRect = null!;
	private Label _nameLabel = null!;
	private Label _jobBadgeLabel = null!;
	private Label _ageLabel = null!;
	private RichTextLabel _statusLabel = null!;
	private Label _wageLabel = null!;
	private Label _negotiationWarning = null!;
	private Label _hpLabel = null!;
	private Label _satisfactionLabel = null!;
	private ProgressBar _hpBar = null!;
	private ProgressBar _satisfactionBar = null!;

	// ---- 7大能力値 ----
	private TripleStatBar _barSTR = null!;
	private Label _valSTR = null!;
	private TripleStatBar _barVIT = null!;
	private Label _valVIT = null!;
	private TripleStatBar _barAGI = null!;
	private Label _valAGI = null!;
	private TripleStatBar _barDEX = null!;
	private Label _valDEX = null!;
	private TripleStatBar _barINT = null!;
	private Label _valINT = null!;
	private TripleStatBar _barMND = null!;
	private Label _valMND = null!;
	private TripleStatBar _barLDR = null!;
	private Label _valLDR = null!;

	// ---- 8年稼働タイムライン ----
	private ProgressBar _timelineBar = null!;
	private Label _activeWeeksLabel = null!;
	private Label _remainingWeeksLabel = null!;
	private Label _contributionScoreLabel = null!;
	private Label _severanceEstimateLabel = null!;

	// ---- 特性スロット（最大5枠） ----
	private Label _traitSlot1 = null!;
	private Label _traitSlot2 = null!;
	private Label _traitSlot3 = null!;
	private Label _traitSlot4 = null!;
	private Label _traitSlot5 = null!;
	private Label[] _traitSlotLabels = null!;

	// 特性の忘却ボタン（→ 03 §5.3.2、2026年9月・§0.33）。シーンを触らず、各スロットのラベルの右にコードで並べる。
	private readonly Button[] _forgetButtons = new Button[Adventurer.MaxTraitCount];
	private ConfirmationDialog _forgetDialog = null!;

	/// <summary>忘却ダイアログで確認中の特性Id。</summary>
	private string? _pendingForgetTraitId;

	// ---- 装備スロット ----
	private Label _weaponLabel = null!;
	private Label _armorLabel = null!;
	private Label _accessory1Label = null!;
	private Label _accessory2Label = null!;

	// ---- 操作ボタン群 ----
	private Button _equipmentButton = null!;
	private Button _raiseWageButton = null!;
	private Button _payBonusButton = null!;
	private Button _retireButton = null!;
	private Button _renameButton = null!;
	private Button _compatButton = null!;

	// 待機中の過ごし方（§0.73）。コードで組み立て、操作ボタンのカードの上に置く
	private PanelContainer _idleCard = null!;
	private OptionButton _idleActivityOption = null!;
	private OptionButton _selfTrainingStatOption = null!;
	private Button _idleApplyAllButton = null!;
	private RichTextLabel _idleStatusLabel = null!;
	private bool _idleUpdating;

	/// <summary>自主練で伸ばす能力の選択肢（先頭の "" は「職業の伸び方」＝Adventurer.SelfTrainingStat が null）。</summary>
	private static readonly string[] SelfTrainingStatChoices = { "", "STR", "VIT", "AGI", "DEX", "INT", "MND", "LDR" };

	// 改名ダイアログ（→ 03 §2.1、2026年9月新設。コードで組み立てる）
	private ConfirmationDialog _renameDialog = null!;
	private LineEdit _renameEdit = null!;
	private Label _renameErrorLabel = null!;

	/// <summary>ステータス詳細パネルに表示中の冒険者Id。週送り後もこの人物の表示を維持する。</summary>
	private Guid? _detailAdventurerId;

	/// <summary>週報ログ（右ペイン）への追記を依頼する（BBCode文字列）。</summary>
	public event Action<string> LogRequested = delegate { };

	/// <summary>ゲーム状態が変わったことを通知する（MainDashboardが全体を再描画する）。</summary>
	public event Action StateChanged = delegate { };

	/// <summary>装備ポップアップの開放を依頼する（MainDashboardがポップアップを所有するため）。</summary>
	public event Action<Adventurer> EquipmentRequested = delegate { };

	/// <summary>
	/// 冒険者が改名された（→ MainDashboard が画面全体を再描画し、左ペインの編成スロット・候補一覧・大迷宮画面の名前を即時同期する）。
	/// </summary>
	public event Action<Guid> AdventurerRenamed = delegate { };

	public override void _Ready()
	{
		// 右ペイン切り替え
		_noSelectionPanel = GetNode<PanelContainer>("%NoSelectionPanel");
		_detailContainer = GetNode<VBoxContainer>("%DetailContainer");

		// 基本情報
		_portraitTextureRect = GetNode<TextureRect>("%PortraitTextureRect");
		_nameLabel = GetNode<Label>("%NameLabel");
		_jobBadgeLabel = GetNode<Label>("%JobBadgeLabel");
		_ageLabel = GetNode<Label>("%AgeLabel");
		_statusLabel = GetNode<RichTextLabel>("%StatusLabel");
		_wageLabel = GetNode<Label>("%WageLabel");
		_negotiationWarning = GetNode<Label>("%NegotiationWarning");
		_hpLabel = GetNode<Label>("%HpLabel");
		_satisfactionLabel = GetNode<Label>("%SatisfactionLabel");
		_hpBar = GetNode<ProgressBar>("%HpBar");
		_satisfactionBar = GetNode<ProgressBar>("%SatisfactionBar");

		// 7大能力値
		_barSTR = GetNode<TripleStatBar>("%Bar_STR");
		_valSTR = GetNode<Label>("%Val_STR");
		_barVIT = GetNode<TripleStatBar>("%Bar_VIT");
		_valVIT = GetNode<Label>("%Val_VIT");
		_barAGI = GetNode<TripleStatBar>("%Bar_AGI");
		_valAGI = GetNode<Label>("%Val_AGI");
		_barDEX = GetNode<TripleStatBar>("%Bar_DEX");
		_valDEX = GetNode<Label>("%Val_DEX");
		_barINT = GetNode<TripleStatBar>("%Bar_INT");
		_valINT = GetNode<Label>("%Val_INT");
		_barMND = GetNode<TripleStatBar>("%Bar_MND");
		_valMND = GetNode<Label>("%Val_MND");
		_barLDR = GetNode<TripleStatBar>("%Bar_LDR");
		_valLDR = GetNode<Label>("%Val_LDR");

		// 8年稼働タイムライン
		_timelineBar = GetNode<ProgressBar>("%TimelineBar");
		_activeWeeksLabel = GetNode<Label>("%ActiveWeeksLabel");
		_remainingWeeksLabel = GetNode<Label>("%RemainingWeeksLabel");
		_contributionScoreLabel = GetNode<Label>("%ContributionScoreLabel");
		_severanceEstimateLabel = GetNode<Label>("%SeveranceEstimateLabel");

		// 特性スロット
		_traitSlot1 = GetNode<Label>("%TraitSlot1");
		_traitSlot2 = GetNode<Label>("%TraitSlot2");
		_traitSlot3 = GetNode<Label>("%TraitSlot3");
		_traitSlot4 = GetNode<Label>("%TraitSlot4");
		_traitSlot5 = GetNode<Label>("%TraitSlot5");
		_traitSlotLabels = new[] { _traitSlot1, _traitSlot2, _traitSlot3, _traitSlot4, _traitSlot5 };
		BuildForgetButtons();

		// 装備スロット
		_weaponLabel = GetNode<Label>("%WeaponLabel");
		_armorLabel = GetNode<Label>("%ArmorLabel");
		_accessory1Label = GetNode<Label>("%Accessory1Label");
		_accessory2Label = GetNode<Label>("%Accessory2Label");

		// 操作ボタン
		_equipmentButton = GetNode<Button>("%EquipmentButton");
		_raiseWageButton = GetNode<Button>("%RaiseWageButton");
		_payBonusButton = GetNode<Button>("%PayBonusButton");
		_retireButton = GetNode<Button>("%RetireButton");
		_renameButton = GetNode<Button>("%RenameButton");

		// シグナル配線
		_equipmentButton.Pressed += OnEquipmentButtonPressed;
		_raiseWageButton.Pressed += OnRaiseWagePressed;
		_payBonusButton.Pressed += OnPayBonusPressed;
		_retireButton.Pressed += OnRetirePressed;
		_renameButton.Pressed += OnRenamePressed;
		BuildRenameDialog();
		BuildCompatibilityButton();
		BuildIdleActivityCard();

		// 初期状態は未選択
		ShowNoAdventurerSelected();
	}

	/// <summary>MainDashboard._Ready から呼ばれる1回限りの初期化。Coreシステムの注入。</summary>
	public void Initialize(SatisfactionSystem satisfactionSystem, AgingSystem agingSystem)
	{
		_satisfactionSystem = satisfactionSystem;
		_agingSystem = agingSystem;
	}

	/// <summary>最新のゲーム状態でパネル全体を再描画する（MainDashboard.RefreshAllから毎回呼ぶ）。</summary>
	public void Refresh(GameState state)
	{
		_state = state;
		RefreshAdventurerDetail();
	}

	/// <summary>
	/// 表示対象の冒険者を切り替える（→ 部隊編成パネルの編成スロット・候補行のクリック。MainDashboardが中継する）。
	/// 2026年9月の「部隊・冒険者」画面統合で、旧・自前の冒険者一覧（ItemList）を廃止し、選択元を部隊編成パネルへ一本化した。
	/// 現役ロースターに居ない冒険者（引退・除籍済み）が渡された場合は未選択状態にする。
	/// </summary>
	public void ShowAdventurer(Guid adventurerId)
	{
		_detailAdventurerId = adventurerId;
		if (_state != null)
			RefreshAdventurerDetail();
	}

	/// <summary>現在表示中の冒険者Id（未選択ならnull）。部隊編成パネルの選択強調に使う。</summary>
	public Guid? SelectedAdventurerId => _detailAdventurerId;

	/// <summary>
	/// ギルド未加入の志願者を表示する（→ 採用画面 RecruitmentPopup の右ペイン、03 §2.4・§9、§0.37）。
	/// 名簿に居ない人物を直接受け取り、同じ基本情報・7大能力値バー・特性・装備欄で描く。
	/// 人事業務（装備変更・昇給・ボーナス・引退勧告・改名・特性の忘却）と、在籍前提の
	/// 満足度・8年稼働タイムラインは出さない。以後この Panel は志願者表示専用として使う
	/// （部隊・冒険者画面の Panel とは別インスタンス。Refresh は呼ばれない）。
	/// </summary>
	public void ShowCandidatePreview(Adventurer candidate, GameState state)
	{
		_previewState = state;
		ShowAdventurerDetail(candidate);

		GetNode<Control>("Body/RightPane/DetailVBox/DetailContainer/BasicInfoCard/Margin/BasicInfoHBox/BasicInfoVBox/LifecycleCard").Visible = false;
		GetNode<Control>("Body/RightPane/DetailVBox/DetailContainer/ActionButtonsCard").Visible = false;
		_idleCard.Visible = false;
		_renameButton.Visible = false;
		_compatButton.Visible = false;
		_negotiationWarning.Visible = false;
		_satisfactionLabel.Visible = false;
		_satisfactionBar.Visible = false;
		foreach (var forget in _forgetButtons)
			forget.Visible = false;

		_statusLabel.Clear();
		_statusLabel.AppendText("[color=yellow]志願者（ギルド未加入）[/color]");
		_wageLabel.Text = $"加入想定週給: {candidate.WeeklyWage} G";
		if (candidate.EquippedWeapon == null)
			_weaponLabel.Text = "武器: なし（平服）";
		if (candidate.EquippedArmor == null)
			_armorLabel.Text = "防具: なし（平服）";
	}

	// ==================== 相性ウィンドウ ====================

	/// <summary>名前の行（改名ボタンの右）に「💞 相性」を置く。押すと、この冒険者と他の現役冒険者との相性の一覧を別ウィンドウで開く。</summary>
	private void BuildCompatibilityButton()
	{
		_compatButton = new Button
		{
			Text = "💞 相性",
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			TooltipText = "この冒険者と、ほかの現役冒険者との相性の一覧を開く。",
		};
		_compatButton.Pressed += OnCompatibilityPressed;
		var row = _renameButton.GetParent();
		row.AddChild(_compatButton);
		row.MoveChild(_compatButton, _renameButton.GetIndex() + 1);
	}

	private void OnCompatibilityPressed()
	{
		var target = CurrentDetailAdventurer();
		if (target == null || _state == null) return;

		var list = new VBoxContainer { CustomMinimumSize = new Vector2(620, 0) };
		list.AddThemeConstantOverride("separation", 4);
		var others = _state.Adventurers.Where(o => o.Id != target.Id)
			.Select(o => (Adventurer: o, Value: CompatibilitySystem.GetCompatibility(_state, target.Id, o.Id)))
			.OrderByDescending(x => x.Value)
			.ToList();

		list.AddChild(new Label
		{
			Text = $"険悪（{CompatibilityBalance.HostileThreshold}未満）は同じ部隊に入れると不利。{SoulFusionBalance.RequiredCompatibility}で魂魄融和の秘薬の親になれる。",
			Modulate = new Color(0.7f, 0.72f, 0.78f),
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		});

		foreach (var (other, value) in others)
		{
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 10);
			bool sameSquad = _state.SavedParties.Any(p => p.MemberIds.Contains(target.Id) && p.MemberIds.Contains(other.Id));
			// 列を固定幅にして、バーと数値が縦にそろうようにする（名前・「同じ部隊」・バー・数値の4列）
		row.AddChild(new Label { Text = $"{other.Name}（{JobLabel(other.JobClass)}・{other.Age}歳）", CustomMinimumSize = new Vector2(250, 0), ClipText = true });
		var squadTag = new Label { Text = sameSquad ? "同じ部隊" : "", CustomMinimumSize = new Vector2(80, 0) };
		squadTag.AddThemeColorOverride("font_color", new Color(0.6f, 0.8f, 1f));
		row.AddChild(squadTag);
			var bar = new ProgressBar
			{
				MinValue = 0, MaxValue = CompatibilityBalance.MaxValue, Value = value, ShowPercentage = false,
				CustomMinimumSize = new Vector2(200, 16), SizeFlagsVertical = SizeFlags.ShrinkCenter,
			};
			row.AddChild(bar);
			var (color, note) = value < CompatibilityBalance.HostileThreshold ? (new Color(1f, 0.45f, 0.4f), "険悪")
				: value >= SoulFusionBalance.RequiredCompatibility ? (new Color(1f, 0.6f, 0.85f), "★秘薬可")
				: value >= 70 ? (new Color(0.5f, 1f, 0.6f), "良好")
				: (new Color(0.85f, 0.87f, 0.9f), "");
			var valueLabel = new Label { Text = $"{value} {note}".TrimEnd(), CustomMinimumSize = new Vector2(100, 0) };
			valueLabel.AddThemeColorOverride("font_color", color);
			row.AddChild(valueLabel);
			list.AddChild(row);
		}
		if (others.Count == 0)
			list.AddChild(new Label { Text = "ほかの現役冒険者がいない。" });

		var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(640, 380), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		scroll.AddChild(list);
		var dialog = new AcceptDialog { Title = $"{target.Name} の相性", OkButtonText = "閉じる" };
		dialog.AddChild(scroll);
		dialog.Confirmed += () => dialog.QueueFree();
		dialog.Canceled += () => dialog.QueueFree();
		AddChild(dialog);
		dialog.PopupCentered();
	}

	// ==================== 待機中の過ごし方（§0.73） ====================

	/// <summary>
	/// 「待機中の過ごし方」のカードを操作ボタンのカードの上に置く：研究を手伝う／自主練、自主練で伸ばす能力、
	/// 全員を同じ過ごし方にするボタン、今週どう過ごすか（静養ならその理由）。
	/// </summary>
	private void BuildIdleActivityCard()
	{
		_idleCard = new PanelContainer();
		var margin = new MarginContainer();
		foreach (var side in new[] { "left", "top", "right", "bottom" })
			margin.AddThemeConstantOverride($"margin_{side}", 8);
		_idleCard.AddChild(margin);
		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 4);
		margin.AddChild(vbox);

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		vbox.AddChild(row);
		row.AddChild(new Label
		{
			Text = "待機中の過ごし方",
			TooltipText = $"出撃しておらず、訓練施設にも入っておらず、HPが{TrainingBalance.IdleActivityHpRatio * 100:0}%を超え、負傷も毒も無い週にすること。それ以外の週は静養する。",
			MouseFilter = MouseFilterEnum.Pass,
		});

		_idleActivityOption = new OptionButton
		{
			TooltipText = $"研究を手伝う：研究の手伝いが週{MasterMoodBalance.IdleAdventurerHelpResearchCredit}G分貯まり（次の研究費の{TrainingBalance.ResearchCreditMaxDiscountRate * 100:0}%まで割り引く）、マスターの機嫌が+{MasterMoodBalance.IdleAdventurerHelpMood}。\n" +
				$"自主練：能力が少し伸びることがある（訓練施設の{TrainingBalance.SelfTrainingGrowthMultiplier * 100:0}%ほどの伸び。費用なし）。HPを{TrainingBalance.SelfTrainingHpCost}使う。",
		};
		_idleActivityOption.AddItem(IdleActivitySystem.Label(IdleActivity.Help), (int)IdleActivity.Help);
		_idleActivityOption.AddItem(IdleActivitySystem.Label(IdleActivity.SelfTraining), (int)IdleActivity.SelfTraining);
		_idleActivityOption.ItemSelected += OnIdleActivitySelected;
		row.AddChild(_idleActivityOption);

		_selfTrainingStatOption = new OptionButton { TooltipText = "自主練で伸ばす能力。「職業の伸び方」なら、職業ごとの伸びやすさで選ぶ。" };
		foreach (var stat in SelfTrainingStatChoices)
			_selfTrainingStatOption.AddItem(stat == "" ? "職業の伸び方" : $"{stat}を伸ばす");
		_selfTrainingStatOption.ItemSelected += OnSelfTrainingStatSelected;
		row.AddChild(_selfTrainingStatOption);

		_idleApplyAllButton = new Button { TooltipText = "現役の全員の待機中の過ごし方を、この冒険者と同じにする（自主練で伸ばす能力は各自のまま）。" };
		_idleApplyAllButton.Pressed += OnIdleApplyAllPressed;
		row.AddChild(_idleApplyAllButton);

		_idleStatusLabel = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		vbox.AddChild(_idleStatusLabel);

		var actionCard = GetNode<Control>("Body/RightPane/DetailVBox/DetailContainer/ActionButtonsCard");
		_detailContainer.AddChild(_idleCard);
		_detailContainer.MoveChild(_idleCard, actionCard.GetIndex());
	}

	/// <summary>訓練施設に入っているか（志願者の表示ではギルドの状態が無いので false）。</summary>
	private bool IsTrainingNow(Adventurer a) => _state != null && TrainingSystem.IsTraining(_state, a.Id);

	/// <summary>カードを選択中の冒険者の値で描き直す。</summary>
	private void RefreshIdleActivityCard(Adventurer a)
	{
		if (_state == null) return;
		_idleCard.Visible = true;
		_idleUpdating = true;
		_idleActivityOption.Select(_idleActivityOption.GetItemIndex((int)a.IdleActivity));
		_selfTrainingStatOption.Select(Math.Max(0, Array.IndexOf(SelfTrainingStatChoices, a.SelfTrainingStat ?? "")));
		_idleUpdating = false;
		_selfTrainingStatOption.Visible = a.IdleActivity == IdleActivity.SelfTraining;
		_idleApplyAllButton.Text = $"全員を「{IdleActivitySystem.Label(a.IdleActivity)}」に";

		var activity = IdleActivitySystem.GetWeekActivity(_state, a);
		string now = activity switch
		{
			WeekActivity.Resting => $"[color=orange]静養（{IdleActivitySystem.RestReason(a)}）[/color]",
			WeekActivity.Dispatched or WeekActivity.Training => $"[color=gray]{IdleActivitySystem.Label(activity)}（待機中の過ごし方はしない）[/color]",
			_ => $"[color=lime]{IdleActivitySystem.Label(activity)}[/color]",
		};
		_idleStatusLabel.Clear();
		_idleStatusLabel.AppendText($"今週：{now}　[color=gray]研究の手伝いの貯まり {_state.ResearchCredit} G[/color]");
	}

	private void OnIdleActivitySelected(long index)
	{
		var target = CurrentDetailAdventurer();
		if (_idleUpdating || target == null) return;
		target.IdleActivity = (IdleActivity)_idleActivityOption.GetItemId((int)index);
		StateChanged.Invoke();
	}

	private void OnSelfTrainingStatSelected(long index)
	{
		var target = CurrentDetailAdventurer();
		if (_idleUpdating || target == null) return;
		target.SelfTrainingStat = index == 0 ? null : SelfTrainingStatChoices[index];
		StateChanged.Invoke();
	}

	private void OnIdleApplyAllPressed()
	{
		var target = CurrentDetailAdventurer();
		if (target == null || _state == null) return;
		foreach (var a in _state.Adventurers)
			a.IdleActivity = target.IdleActivity;
		LogRequested($"[color=cyan]現役の全員の待機中の過ごし方を「{IdleActivitySystem.Label(target.IdleActivity)}」にした。[/color]");
		StateChanged.Invoke();
	}

	// ==== 詳細表示 ====

	/// <summary>
	/// ステータス詳細パネルを、直近にクリックされた冒険者の最新の値で再描画する。
	/// 選択中の冒険者が居なくなった場合（引退・戦死・未選択など）は未選択状態を維持する。
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
	/// 冒険者が未選択の状態の表示。個人操作系ボタンを無効化する。
	/// </summary>
	private void ShowNoAdventurerSelected()
	{
		_detailAdventurerId = null;
		_noSelectionPanel.Visible = true;
		_detailContainer.Visible = false;
		SetActionButtonsDisabled(true);
	}

	/// <summary>冒険者1名分のステータス詳細を右ペインに表示する。</summary>
	private void ShowAdventurerDetail(Adventurer a)
	{
		_detailAdventurerId = a.Id;
		_noSelectionPanel.Visible = false;
		_detailContainer.Visible = true;

		// ---- 基本情報 ----
		_nameLabel.Text = a.Name;

		_jobBadgeLabel.Text = $"{JobLabel(a.JobClass)}（{JobMainStatsText(a.JobClass)}）";
		_jobBadgeLabel.TooltipText = JobRoleTooltip(a.JobClass);
		_ageLabel.Text = $"{a.Age}歳（{AgeBandLabel(a.AgeBand)}）";

		_statusLabel.Clear();
		if (a.Injury == InjurySeverity.Severe)
			_statusLabel.AppendText($"[color=red]重傷・出撃不可（全治まで{a.InjuryWeeksRemaining}週）[/color]");
		else if (a.Injury == InjurySeverity.Light)
			_statusLabel.AppendText($"[color=orange]軽傷（全治まで{a.InjuryWeeksRemaining}週・能力値−{CombatBalance.LightInjuryStatPenaltyRate * 100:0}%）[/color]");
		else if (a.IsDispatched)
			_statusLabel.AppendText("[color=cyan]出撃中[/color]");
		else if (IsTrainingNow(a))
			_statusLabel.AppendText("[color=lime]訓練中[/color]");
		else
			_statusLabel.AppendText("[color=lime]待機中[/color]");

		RefreshIdleActivityCard(a);

		// 魂魄融和で生まれた娘なら両親を出す（→ 03 §5.4・§0.58）。秘薬の親になった者には印を付ける。
		if (a.ParentIds.Count == 2)
		{
			string parentA = _state.FindAdventurer(a.ParentIds[0])?.Name ?? "？";
			string parentB = _state.FindAdventurer(a.ParentIds[1])?.Name ?? "？";
			_statusLabel.AppendText($"　[color=violet]🧪 {parentA}と{parentB}の娘[/color]");
		}
		if (a.HasUsedSoulFusion)
			_statusLabel.AppendText("　[color=violet]🫧 秘薬の親[/color]");

		if (a.NeedsNegotiation)
		{
			int remaining = Math.Max(0, SatisfactionBalance.NegotiationGraceWeeks - a.NegotiationWeeksElapsed);
			_negotiationWarning.Text = $"⚠ 契約交渉中（あと{remaining}週で対応しないと退団）";
			_negotiationWarning.Visible = true;
		}
		else
		{
			_negotiationWarning.Visible = false;
		}

		// 最大HPは素の値・装備の加算・特性や装備の能力値補正で決まる。能力値のバーと同じく、補正の分を見せる
		int hpBase = (int)(a.VIT * CombatBalance.MaxHpVitCoefficient + CombatBalance.MaxHpBase);
		int hpEquipment = a.GetEquipmentHpBonus();
		int hpOther = a.MaxHP - hpBase - hpEquipment; // 特性（頑強・病弱・古傷など）と、装備の能力値（VIT）補正
		string hpParts = (hpEquipment != 0 ? $" 装備{hpEquipment:+0;-0}" : "") + (hpOther != 0 ? $" 特性・補正{hpOther:+0;-0}" : "");
		_hpLabel.Text = $"HP: {a.CurrentHP} / {a.MaxHP}" + (hpParts.Length > 0 ? $"（素の値 {hpBase}{hpParts}）" : "");
		_hpLabel.TooltipText = $"最大HP＝素のVIT×{CombatBalance.MaxHpVitCoefficient:0.#}＋{CombatBalance.MaxHpBase:0.#}（{hpBase}）＋装備の加算（{hpEquipment:+0;-0;0}）＋特性・装備の能力値による補正（{hpOther:+0;-0;0}）";
		_hpLabel.MouseFilter = Control.MouseFilterEnum.Pass;
		_hpBar.MaxValue = a.MaxHP > 0 ? a.MaxHP : 100;
		_hpBar.Value = Math.Clamp(a.CurrentHP, 0, _hpBar.MaxValue);

		_satisfactionLabel.Text = $"満足度: {a.Satisfaction} / 100";
		_satisfactionBar.MaxValue = 100;
		_satisfactionBar.Value = Math.Clamp(a.Satisfaction, 0, 100);

		_wageLabel.Text = $"週給: {a.WeeklyWage} G";

		// ---- ポートレート ----
		_portraitTextureRect.Texture = LoadPortraitTexture(a.PortraitId);

		// ---- 7大能力値 ----
		BindStatRow(_barSTR, _valSTR, a, "STR");
		BindStatRow(_barVIT, _valVIT, a, "VIT");
		BindStatRow(_barAGI, _valAGI, a, "AGI");
		BindStatRow(_barDEX, _valDEX, a, "DEX");
		BindStatRow(_barINT, _valINT, a, "INT");
		BindStatRow(_barMND, _valMND, a, "MND");
		BindStatRow(_barLDR, _valLDR, a, "LDR");

		// ---- 8年稼働タイムライン ----
		// 満期は年齢で決まる（24歳なら残り2年）ため、残り週数は Core が年齢と今の週から出し、在籍期間の全体はその分に合わせる
		int remainingWeeks = AgingSystem.GetRemainingActiveWeeks(a, _state?.WeekNumber ?? 1);
		int maxWeeks = Math.Max(1, a.ActiveWeeks + remainingWeeks);
		_timelineBar.MaxValue = maxWeeks;
		_timelineBar.Value = Math.Min(a.ActiveWeeks, maxWeeks);

		int severanceEstimate = AgingSystem.CalculateSeverancePay(a);

		_activeWeeksLabel.Text = $"在籍期間: {a.ActiveWeeks} / {maxWeeks}週";
		_remainingWeeksLabel.Text = $"引退まで: 残り {remainingWeeks}週";
		_contributionScoreLabel.Text = $"累積功績スコア: {a.TotalContributionScore} pt";
		_severanceEstimateLabel.Text = $"退職金見込額: {severanceEstimate:N0} G";

		// ---- 特性スロット（最大5枠） ----
		for (int i = 0; i < 5; i++)
		{
			if (i < a.TraitIds.Count)
			{
				string traitId = a.TraitIds[i];
				var def = TraitCatalog.FindById(traitId);
				string traitName = def?.DisplayName ?? traitId;
				bool isCurse = def?.IsCurseOrInjury == true;
				bool isFlaw = def?.IsFlaw == true; // 生まれつきの欠点（§0.56）
				bool isRare = def?.IsRare == true; // レア特性（§0.57）
				bool isSoulChild = traitId == "SoulChild"; // 魂魄の申し子（§0.62）。レアの中でも別の色にする
				_traitSlotLabels[i].Text = isCurse ? $"{i + 1}: 【{traitName}】🔒"
					: isFlaw ? $"{i + 1}: 【{traitName}】▼"
					: isSoulChild ? $"{i + 1}: ✦【{traitName}】"
					: isRare ? $"{i + 1}: ★【{traitName}】"
					: $"{i + 1}: 【{traitName}】";
				_traitSlotLabels[i].TooltipText = def?.Description ?? "";
				_traitSlotLabels[i].AddThemeColorOverride("font_color", isCurse
					? new Color(1.0f, 0.55f, 0.45f)  // 障害特性: 赤みのある橙
					: isFlaw
						? new Color(0.75f, 0.6f, 0.95f)  // 生まれつきの欠点: 薄い紫
						: isSoulChild
							? new Color(0.45f, 0.9f, 1.0f)  // 魂魄の申し子: 水色
							: isRare
								? new Color(1.0f, 0.75f, 0.2f)  // レア特性: 金色
								: new Color(0.9f, 0.9f, 0.4f));  // 習得済み: 明るい黄色

				// 忘却ボタン：障害特性・生まれつきの欠点と出撃中は押せない（→ Adventurer.CanRemoveTrait、03 §5.3.2）。
				string? overcomeBy = isFlaw && def!.OvercomeByTraitId != null
					? TraitCatalog.FindById(def.OvercomeByTraitId)?.DisplayName
					: null;
				var forget = _forgetButtons[i];
				forget.Visible = true;
				forget.Disabled = isCurse || isFlaw || a.IsDispatched || !a.CanRemoveTrait(traitId);
				forget.TooltipText = isCurse ? "不可逆障害のため忘却不可"
					: isFlaw ? (overcomeBy != null
						? $"生まれつきの欠点のため忘却不可。教官から『{overcomeBy}』を教われば克服できる"
						: "生まれつきの欠点のため忘却不可（克服できない）")
					: a.IsDispatched ? "出撃中は忘却できない"
					: $"『{traitName}』を忘却して枠を空ける（取り消し不可）";
			}
			else
			{
				_traitSlotLabels[i].Text = $"{i + 1}: （空きスロット）";
				_traitSlotLabels[i].TooltipText = "";
				_traitSlotLabels[i].AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f)); // 空き: グレー
				_forgetButtons[i].Visible = false;
			}
		}

		// ---- 装備スロット（→ 03 §4.2.2。2026年9月改訂：個体（EquipmentItem）を表示する） ----
		// §0.40：名前はアフィックス込みの表示名で、付与数に応じて色分けする（→ ItemColorHelper）。
		// 性能の内訳（アフィックス分を含む）はツールチップに出す。
		BindEquipmentSlot(_weaponLabel, "武器", a.EquippedWeapon);
		BindEquipmentSlot(_armorLabel, "防具", a.EquippedArmor);
		BindEquipmentSlot(_accessory1Label, "装飾品", a.EquippedAccessory1);
		BindEquipmentSlot(_accessory2Label, "装飾品", a.EquippedAccessory2);

		// ---- 操作ボタン群の個別ガード ----
		_raiseWageButton.Disabled = false;
		_payBonusButton.Disabled = false;
		_renameButton.Disabled = false; // 改名は名前だけの変更のため、出撃中でも行える
		_compatButton.Visible = true;

		// 出撃中は装備変更・引退をガード
		_equipmentButton.Disabled = a.IsDispatched;
		_retireButton.Disabled = a.IsDispatched;
	}

	/// <summary>
	/// 7大能力値の各行へ素の値（白）・特性補正後（緑／赤）・実効値（水色の右端）・潜在能力PA（灰）を反映する。
	/// バー全幅は常に上限100（→ TripleStatBar。§0.30で4層、§0.36で特性補正を分離）。実効値は特性・装備込み
	/// （→ Adventurer.GetEffectiveStat）で、特性補正後＝実効値 − 装備の能力値補正。行の右端の書式は TripleStatBar.FormatLabel。
	/// </summary>
	private void BindStatRow(TripleStatBar bar, Label valLabel, Adventurer a, string stat)
	{
		// 伸びしろは副官の見立て（§0.74、→ PotentialEstimateSystem）。段階（「B?」）で出し、バーは見立ての位置まで描く
		var state = _state ?? _previewState;
		int pa = state != null ? PotentialEstimateSystem.EstimatePa(state, a, stat) : AdventurerStatAccessorPa(a, stat);
		string rank = state != null ? PotentialEstimateSystem.RankLabel(state, a, stat) : pa.ToString();
		int raw = AdventurerStatRaw(a, stat);
		double effectiveExact = a.GetEffectiveStat(stat);
		int effective = (int)Math.Round(effectiveExact);
		// 特性補正後＝実効値 − 装備の能力値補正（→ Adventurer.GetEffectiveStat の内訳。§0.36で特性と装備を分けて表示）
		int traitAdjusted = (int)Math.Round(effectiveExact - a.GetEquipmentStatBonus(stat));
		valLabel.Text = TripleStatBar.FormatLabel(raw, traitAdjusted, effective, rank);
		bar.SetValues(raw, traitAdjusted, effective, pa, rank);
	}

	/// <summary>本当の PA（見立てに使うギルドの状態が無いときだけ）。</summary>
	private static int AdventurerStatAccessorPa(Adventurer a, string stat) => stat switch
	{
		"STR" => a.PA_STR, "VIT" => a.PA_VIT, "AGI" => a.PA_AGI, "DEX" => a.PA_DEX, "INT" => a.PA_INT, "MND" => a.PA_MND, _ => a.PA_LDR,
	};

	/// <summary>素の能力値（成長・訓練が読み書きする値。特性・装備の補正を含まない）。</summary>
	private static int AdventurerStatRaw(Adventurer a, string stat) => stat switch
	{
		"STR" => a.STR,
		"VIT" => a.VIT,
		"AGI" => a.AGI,
		"DEX" => a.DEX,
		"INT" => a.INT,
		"MND" => a.MND,
		"LDR" => a.LDR,
		_ => throw new ArgumentException($"未知のステータス: {stat}"),
	};

	// ==== 操作ボタン群 ====

	// ==== 改名（→ 03 §2.1、2026年9月新設） ====

	/// <summary>
	/// 改名ダイアログ（入力欄＋エラー表示）を組み立てる。OKで閉じる既定動作を切り、入力が不正なら
	/// ダイアログを開いたままエラーを表示する（→ OnRenameConfirmed）。
	/// </summary>
	private void BuildRenameDialog()
	{
		_renameDialog = new ConfirmationDialog
		{
			Title = "冒険者の改名",
			OkButtonText = "改名する",
			CancelButtonText = "やめる",
			DialogHideOnOk = false,
			Exclusive = true,
		};

		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 6);
		_renameEdit = new LineEdit
		{
			PlaceholderText = $"新しい名前を入力（1〜{Adventurer.MaxNameLength}文字）",
			MaxLength = Adventurer.MaxNameLength,
			CustomMinimumSize = new Vector2(320, 0),
		};
		_renameErrorLabel = new Label { Visible = false };
		_renameErrorLabel.AddThemeColorOverride("font_color", new Color(1f, 0.45f, 0.35f));
		box.AddChild(_renameEdit);
		box.AddChild(_renameErrorLabel);
		_renameDialog.AddChild(box);
		_renameDialog.RegisterTextEnter(_renameEdit); // 入力欄でEnterを押してもOK扱い
		_renameDialog.Confirmed += OnRenameConfirmed;
		AddChild(_renameDialog);
	}

	/// <summary>「✏️」ボタン。現在の名前を入れて全選択・フォーカスした状態でダイアログを開く。</summary>
	private void OnRenamePressed()
	{
		var target = CurrentDetailAdventurer();
		if (target == null) return;

		_renameEdit.Text = target.Name;
		_renameErrorLabel.Visible = false;
		_renameDialog.PopupCentered();
		_renameEdit.GrabFocus();
		_renameEdit.SelectAll();
	}

	/// <summary>
	/// 改名の確定。トリム後1〜12文字なら Adventurer.Rename で反映し、氏名ラベルを即時更新して
	/// AdventurerRenamed を発火する（→ 左ペインの編成スロット・候補一覧も同期）。不正ならダイアログを閉じずにエラー表示。
	/// </summary>
	private void OnRenameConfirmed()
	{
		var target = CurrentDetailAdventurer();
		if (target == null)
		{
			_renameDialog.Hide();
			return;
		}

		string oldName = target.Name;
		try
		{
			target.Rename(_renameEdit.Text);
		}
		catch (ArgumentException ex)
		{
			_renameErrorLabel.Text = $"⚠ {ex.Message.Split(" (Parameter", 2)[0]}";
			_renameErrorLabel.Visible = true;
			_renameEdit.GrabFocus();
			return;
		}

		_renameDialog.Hide();
		_nameLabel.Text = target.Name;
		if (target.Name != oldName)
		{
			LogRequested($"[color=cyan]✏️ {oldName} は「{target.Name}」と名乗ることになった。[/color]");
			AdventurerRenamed(target.Id);
		}
	}

	/// <summary>全操作ボタンの有効/無効を一括切り替え。</summary>
	private void SetActionButtonsDisabled(bool disabled)
	{
		_equipmentButton.Disabled = disabled;
		_raiseWageButton.Disabled = disabled;
		_payBonusButton.Disabled = disabled;
		_retireButton.Disabled = disabled;
		_renameButton.Disabled = disabled;
	}

	/// <summary>「装備変更」ボタン（→ 03 §4.2.2）。MainDashboardに装備ポップアップの開放を依頼する。</summary>
	private void OnEquipmentButtonPressed()
	{
		var target = CurrentDetailAdventurer();
		if (target == null) return;

		if (target.IsDispatched)
		{
			LogRequested($"[color=gray]{target.Name} は出撃中のため装備を変更できません。[/color]");
			return;
		}

		EquipmentRequested(target);
	}

	/// <summary>
	/// 「昇給」ボタン（→ 03 §5.2）。表示中の冒険者の週給を1.5倍に引き上げる
	/// （倍率選択UIは未実装のため、仕様の下限=最小限の昇給で固定。→ 03 §5.2）。
	/// </summary>
	private void OnRaiseWagePressed()
	{
		var target = CurrentDetailAdventurer();
		if (target == null) return;

		_satisfactionSystem.RaiseWage(target, 1.5);
		LogRequested($"[color=lime]{target.Name} の週給を {target.WeeklyWage}G に引き上げた。[/color]");
		StateChanged();
	}

	/// <summary>「ボーナス支給」ボタン（→ 03 §5.2）。表示中の冒険者に一時金を支給する。</summary>
	private void OnPayBonusPressed()
	{
		var target = CurrentDetailAdventurer();
		if (target == null) return;

		int bonus = target.WeeklyWage * SatisfactionBalance.BonusWeeksEquivalent;
		_satisfactionSystem.PayBonus(_state, target);
		LogRequested($"[color=lime]{target.Name} にボーナス {bonus}G を支給した。[/color]");
		StateChanged();
	}

	/// <summary>
	/// 「引退勧告」ボタン（→ 03 §7）。表示中の冒険者に早期任意引退を勧告する。
	/// 退職金は AgingSystem.RetireVoluntarily が処理する。
	/// </summary>
	private void OnRetirePressed()
	{
		var target = CurrentDetailAdventurer();
		if (target == null) return;

		if (target.IsDispatched)
		{
			LogRequested($"[color=gray]{target.Name} は派遣中のため引退させられません（帰還を待ってください）。[/color]");
			return;
		}

		// 引退に伴い、身につけていた武具はギルド保管庫へ返還される（→ 03 §4.2.2「離脱時の自動回収」）。
		var recovered = _agingSystem.RetireVoluntarily(_state, target);
		LogRequested($"[color=cyan]{target.Name} が引退し、顧問候補になった。[/color]");
		if (recovered.Count > 0)
		{
			LogRequested($"[color=cyan]📦 装備していた武具がギルド備品としてギルド保管庫へ返還された" +
				$"（{string.Join("、", recovered.Select(e => e.Name))}）。[/color]");
		}
		StateChanged();
	}

	// ==== 特性の忘却（→ 03 §5.3.2、2026年9月・§0.33） ====

	/// <summary>
	/// 各特性スロットのラベルを HBox に入れ直し、右に「忘却」ボタンを並べる。あわせて確認ダイアログを組み立てる。
	/// シーン（scenes/AdventurerPanel.tscn とルート直下の複製）を手で編集せずに済むよう、コードで構築する。
	/// </summary>
	private void BuildForgetButtons()
	{
		for (int i = 0; i < _traitSlotLabels.Length; i++)
		{
			var label = _traitSlotLabels[i];
			var parent = label.GetParent();
			int index = label.GetIndex();

			var row = new HBoxContainer();
			parent.AddChild(row);
			parent.MoveChild(row, index);
			label.Reparent(row);
			label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			label.MouseFilter = MouseFilterEnum.Pass; // ツールチップ（説明文）を出すため

			int slot = i;
			var button = new Button { Text = "忘却", Visible = false, FocusMode = FocusModeEnum.None };
			button.Pressed += () => OnForgetPressed(slot);
			row.AddChild(button);
			_forgetButtons[i] = button;
		}

		_forgetDialog = new ConfirmationDialog { Title = "特性の忘却", OkButtonText = "忘却する", CancelButtonText = "やめる" };
		_forgetDialog.Confirmed += OnForgetConfirmed;
		AddChild(_forgetDialog);
	}

	private void OnForgetPressed(int slot)
	{
		var target = CurrentDetailAdventurer();
		if (target == null || slot >= target.TraitIds.Count) return;

		string traitId = target.TraitIds[slot];
		if (!target.CanRemoveTrait(traitId) || target.IsDispatched) return;

		_pendingForgetTraitId = traitId;
		string traitName = TraitCatalog.FindById(traitId)?.DisplayName ?? traitId;
		_forgetDialog.DialogText = $"{target.Name} の特性『{traitName}』を忘却し、枠を空けますか？\nこの操作は取り消せません。";
		_forgetDialog.PopupCentered();
	}

	private void OnForgetConfirmed()
	{
		var target = CurrentDetailAdventurer();
		string? traitId = _pendingForgetTraitId;
		_pendingForgetTraitId = null;
		if (target == null || traitId == null || target.IsDispatched) return;

		string traitName = TraitCatalog.FindById(traitId)?.DisplayName ?? traitId;
		if (!target.TryRemoveTrait(traitId))
		{
			LogRequested($"[color=gray]『{traitName}』は忘却できない。[/color]");
			return;
		}

		LogRequested($"[color=cyan]🕯 {target.Name} は特性『{traitName}』を忘却し、特性枠を1つ空けた。[/color]");
		ShowAdventurerDetail(target);
		StateChanged();
	}

	// ==== ヘルパー ====

	private Adventurer? CurrentDetailAdventurer() =>
		_detailAdventurerId.HasValue
			? _state.Adventurers.FirstOrDefault(a => a.Id == _detailAdventurerId.Value)
			: null;

	/// <summary>
	/// ポートレート画像を解決する（→ 03 §2.1、項目62）。PortraitIdが設定されていれば
	/// res://assets/portraits/{PortraitId}.png を読み込み、未設定またはファイルが
	/// 存在しない場合はシルエット画像を返す。
	/// </summary>
	private static Texture2D LoadPortraitTexture(string? portraitId)
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

	/// <summary>
	/// 装備スロット1枠のラベル（→ 03 §4.2.2）。未装備なら「なし」、装備中なら表示名（アフィックス込み、
	/// → EquipmentItem.DisplayName）とカタログの効果（→ Item.DescribeEffects）を出す。カタログ定義が引けない旧データは「（不明）」。
	/// §0.40：文字色をアフィックスの付与数で変え（→ ItemColorHelper。1枠＝水色・2枠＝黄緑）、
	/// アフィックスの内訳込みの効果（→ EquipmentItem.DescribeEffects。例：`STR+2・VIT+1 [剛力: STR+3]`）をツールチップに出す。
	/// 行が長くなりすぎないよう、ラベル本文のカッコ内はカタログの効果だけにしている（アフィックスは名前と色、能力値バーの水色で分かる）。
	/// </summary>
	private static void BindEquipmentSlot(Label label, string slotName, EquipmentItem? equipped)
	{
		label.MouseFilter = MouseFilterEnum.Pass; // Label の既定（Ignore）のままだとツールチップが出ない
		label.AddThemeColorOverride("font_color", ItemColorHelper.GetItemColor(equipped));
		if (equipped == null)
		{
			label.Text = $"{slotName}: なし";
			label.TooltipText = "";
			return;
		}

		var definition = equipped.GetDefinition();
		label.Text = definition == null
			? $"{slotName}: {equipped.DisplayName}（不明）"
			: $"{slotName}: {equipped.DisplayName}（{definition.DescribeEffects()}）";
		label.TooltipText = $"{equipped.DisplayName}\n{equipped.DescribeEffects()}";
	}

	/// <summary>討伐火力で重く測る能力（§0.72、→ BAL: boss_power_weights.csv。重み0.8以上を重い順に）。例：「STR・VIT」。</summary>
	public static string JobMainStatsText(JobClass job) =>
		string.Join("・", DungeonBalance.GetBossPowerWeights(job).Where(w => w.Weight >= 0.8).OrderByDescending(w => w.Weight).Select(w => w.Stat));

	/// <summary>職業の役割の説明（ツールチップ）：火力の主な能力・伸びやすい能力・神官の加護。</summary>
	public static string JobRoleTooltip(JobClass job)
	{
		string growth = string.Join("・", GrowthBalance.GetJobAptitudeOrder(job).Take(2));
		string text = $"討伐火力は {JobMainStatsText(job)} を重く測る。出撃では {growth} が伸びやすい。";
		if (job == JobClass.Cleric)
			text += $"\n神官の加護：部隊にいると、ボス戦と道中の損耗を（MND×{CombatBalance.ClericBlessingLossPctPerMnd:0.##}）%ポイント軽くし、ボス戦の後に重傷になりにくい。";
		return text;
	}

	/// <summary>職業の日本語表示名。DungeonPanel.JobLabel と同じマッピング。</summary>
	public static string JobLabel(JobClass job) => job switch
	{
		JobClass.Warrior => "重戦士",
		JobClass.Knight => "騎士",
		JobClass.Ranger => "斥候",
		JobClass.Thief => "盗賊",
		JobClass.Mage => "魔導士",
		JobClass.Cleric => "神官",
		JobClass.Scholar => "学者",
		_ => job.ToString(),
	};

	private static string AgeBandLabel(AgeBand band) => band switch
	{
		AgeBand.Young => "新鋭期",
		AgeBand.Growing => "成長期",
		AgeBand.Peak => "全盛期",
		_ => band.ToString()
	};
}
