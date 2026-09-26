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
/// 4. 職業に応じた配置区分（【前衛】/【後衛】）の明示
/// </summary>
public partial class AdventurerPanel : VBoxContainer
{
	private GameState _state = null!;
	private SatisfactionSystem _satisfactionSystem = null!;
	private AgingSystem _agingSystem = null!;

	// ---- 右ペイン：未選択 / 詳細表示切り替え ----
	private PanelContainer _noSelectionPanel = null!;
	private VBoxContainer _detailContainer = null!;

	// ---- 基本情報カード ----
	private TextureRect _portraitTextureRect = null!;
	private Label _nameLabel = null!;
	private Label _placementBadgeLabel = null!;
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
		_placementBadgeLabel = GetNode<Label>("%PlacementBadgeLabel");
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
	public void ShowCandidatePreview(Adventurer candidate)
	{
		ShowAdventurerDetail(candidate);

		GetNode<Control>("HeaderPanel").Visible = false;
		GetNode<Control>("Body/RightPane/DetailVBox/DetailContainer/LifecycleCard").Visible = false;
		GetNode<Control>("Body/RightPane/DetailVBox/DetailContainer/ActionButtonsCard").Visible = false;
		_renameButton.Visible = false;
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

		bool isFront = PlacementRules.GetDefault(a.JobClass) == Placement.Front;
		_placementBadgeLabel.Text = isFront ? "【前衛】" : "【後衛】";
		_placementBadgeLabel.AddThemeColorOverride("font_color", isFront
			? new Color(0.4f, 0.8f, 1.0f) // 前衛：水色
			: new Color(0.9f, 0.6f, 1.0f) // 後衛：薄紫色
		);

		_jobBadgeLabel.Text = JobLabel(a.JobClass);
		_ageLabel.Text = $"{a.Age}歳（{AgeBandLabel(a.AgeBand)}）";

		_statusLabel.Clear();
		if (a.Injury == InjurySeverity.Severe)
			_statusLabel.AppendText($"[color=red]重傷・出撃不可（全治まで{a.InjuryWeeksRemaining}週）[/color]");
		else if (a.Injury == InjurySeverity.Light)
			_statusLabel.AppendText($"[color=orange]軽傷（全治まで{a.InjuryWeeksRemaining}週）[/color]");
		else if (a.IsDispatched)
			_statusLabel.AppendText("[color=cyan]出撃中[/color]");
		else
			_statusLabel.AppendText("[color=lime]待機中[/color]");

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

		_hpLabel.Text = $"HP: {a.CurrentHP} / {a.MaxHP}";
		_hpBar.MaxValue = a.MaxHP > 0 ? a.MaxHP : 100;
		_hpBar.Value = Math.Clamp(a.CurrentHP, 0, _hpBar.MaxValue);

		_satisfactionLabel.Text = $"満足度: {a.Satisfaction} / 100";
		_satisfactionBar.MaxValue = 100;
		_satisfactionBar.Value = Math.Clamp(a.Satisfaction, 0, 100);

		_wageLabel.Text = $"週給: {a.WeeklyWage} G";

		// ---- ポートレート ----
		_portraitTextureRect.Texture = LoadPortraitTexture(a.PortraitId);

		// ---- 7大能力値 ----
		BindStatRow(_barSTR, _valSTR, a, "STR", a.PA_STR);
		BindStatRow(_barVIT, _valVIT, a, "VIT", a.PA_VIT);
		BindStatRow(_barAGI, _valAGI, a, "AGI", a.PA_AGI);
		BindStatRow(_barDEX, _valDEX, a, "DEX", a.PA_DEX);
		BindStatRow(_barINT, _valINT, a, "INT", a.PA_INT);
		BindStatRow(_barMND, _valMND, a, "MND", a.PA_MND);
		BindStatRow(_barLDR, _valLDR, a, "LDR", a.PA_LDR);

		// ---- 8年稼働タイムライン ----
		int maxWeeks = AgingSystem.MaxActiveWeeks;
		_timelineBar.MaxValue = maxWeeks;
		_timelineBar.Value = Math.Min(a.ActiveWeeks, maxWeeks);

		int remainingWeeks = Math.Max(0, maxWeeks - a.ActiveWeeks);
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
				_traitSlotLabels[i].Text = isCurse ? $"{i + 1}: 【{traitName}】🔒" : $"{i + 1}: 【{traitName}】";
				_traitSlotLabels[i].TooltipText = def?.Description ?? "";
				_traitSlotLabels[i].AddThemeColorOverride("font_color", isCurse
					? new Color(1.0f, 0.55f, 0.45f)  // 障害特性: 赤みのある橙
					: new Color(0.9f, 0.9f, 0.4f));  // 習得済み: 明るい黄色

				// 忘却ボタン：障害特性と出撃中は押せない（→ Adventurer.CanRemoveTrait、03 §5.3.2）。
				var forget = _forgetButtons[i];
				forget.Visible = true;
				forget.Disabled = isCurse || a.IsDispatched || !a.CanRemoveTrait(traitId);
				forget.TooltipText = isCurse ? "不可逆障害のため忘却不可"
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
		BindEquipmentSlot(_accessory1Label, "装飾1", a.EquippedAccessory1);
		BindEquipmentSlot(_accessory2Label, "装飾2", a.EquippedAccessory2);

		// ---- 操作ボタン群の個別ガード ----
		_raiseWageButton.Disabled = false;
		_payBonusButton.Disabled = false;
		_renameButton.Disabled = false; // 改名は名前だけの変更のため、出撃中でも行える

		// 出撃中は装備変更・引退をガード
		_equipmentButton.Disabled = a.IsDispatched;
		_retireButton.Disabled = a.IsDispatched;
	}

	/// <summary>
	/// 7大能力値の各行へ素の値（白）・特性補正後（緑／赤）・実効値（水色の右端）・潜在能力PA（灰）を反映する。
	/// バー全幅は常に上限100（→ TripleStatBar。§0.30で4層、§0.36で特性補正を分離）。実効値は特性・装備込み
	/// （→ Adventurer.GetEffectiveStat）で、特性補正後＝実効値 − 装備の能力値補正。行の右端の書式は TripleStatBar.FormatLabel。
	/// </summary>
	private static void BindStatRow(TripleStatBar bar, Label valLabel, Adventurer a, string stat, int pa)
	{
		int raw = AdventurerStatRaw(a, stat);
		double effectiveExact = a.GetEffectiveStat(stat);
		int effective = (int)Math.Round(effectiveExact);
		// 特性補正後＝実効値 − 装備の能力値補正（→ Adventurer.GetEffectiveStat の内訳。§0.36で特性と装備を分けて表示）
		int traitAdjusted = (int)Math.Round(effectiveExact - a.GetEquipmentStatBonus(stat));
		valLabel.Text = TripleStatBar.FormatLabel(raw, traitAdjusted, effective, pa);
		bar.SetValues(raw, traitAdjusted, effective, pa);
	}

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
