#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 中央ペイン「施設管理」ビュー（仕様書 03 §6・§6.1・§9）。
///
/// - 画面上部に単一建設キューのステータスバナー（施工施設名、残り週数、進捗ゲージ）を常設。
/// - メインエリアに全8施設（宿舎、医務室、酒場、鍛錬所、学問所、技巧所、作戦資料室、冒険者支援室。§0.75）を3列グリッドで配置。
/// - 各施設カードにアイコン、現在Lvバッジ、現在効果、次Lv効果プレビュー、改築費用・工期、着工アクションボタンを配置。
/// - 4大専門訓練所（および参謀・スカウト）のカード内に引退教官・顧問スロット（元冒険者氏名、前職、伝授成長補正等）を表示。
/// - 着工ボタンは「最大Lv到達」「他施設工事中」「資金不足」の3条件で防御的に無効化（Disabled）し理由を明示。
/// - 着工時は即時前払いされ、StateChanged イベントを発火して MainDashboard 全体を再描画する。
/// </summary>
public partial class FacilityPanel : VBoxContainer
{
	private PanelContainer _constructionBanner = null!;
	private Label _constructionStatusIcon = null!;
	private Label _constructionTitleLabel = null!;
	private Label _constructionWeeksLabel = null!;
	private ProgressBar _constructionProgressBar = null!;
	private Label _constructionDetailLabel = null!;
	private GridContainer _facilityGrid = null!;

	private readonly Dictionary<FacilityType, FacilityCardBinding> _cardBindings = new();

	private GameState _state = null!;
	private FacilitySystem _facilitySystem = null!;

	/// <summary>着工でゲーム状態（所持金・建設キュー）が変わったことを通知する。</summary>
	public event Action StateChanged = delegate { };

	/// <summary>
	/// 顧問（教官・参謀・スカウト）の任命ポップアップの開放を依頼する（MainDashboard がポップアップを所有するため）。
	/// 2026年9月：任命の入口を「部隊・冒険者」画面の個人詳細の右肩から、顧問の配属先である施設の画面へ移した。
	/// </summary>
	public event Action<FacilityType> AdvisorRequested = delegate { };

	private class FacilityCardBinding
	{
		public FacilityType Type { get; set; }
		public PanelContainer CardRoot { get; set; } = null!;
		public Label TitleLabel { get; set; } = null!;
		public Label LevelBadge { get; set; } = null!;
		public RichTextLabel CurrentEffectLabel { get; set; } = null!;
		public RichTextLabel NextEffectLabel { get; set; } = null!;
		public Label CostLabel { get; set; } = null!;
		public PanelContainer? InstructorPanel { get; set; }
		public RichTextLabel? InstructorLabel { get; set; }
		/// <summary>訓練施設の「伝授する特性」ボタン（教官欄の下。教官が任命されているときだけ表示、→ 03 §7.1・§0.54）。</summary>
		/// <summary>顧問の任命ボタン（顧問欄の下。顧問管理ポップアップを開く）。</summary>
		public Button? AppointButton { get; set; }
		public Button? FocusTraitButton { get; set; }

		/// <summary>訓練施設だけ（§0.70）：訓練生の一覧と、入れる・外すの選択。月のはじめだけ変えられる。</summary>
		public RichTextLabel? TraineeLabel { get; set; }
		public OptionButton? AddTraineeOption { get; set; }
		public OptionButton? RemoveTraineeOption { get; set; }
		/// <summary>訓練施設だけ（§0.75）：「両方」か片方に特化か。月のはじめだけ変えられる。</summary>
		public OptionButton? SpecialtyOption { get; set; }
		/// <summary>専門（§0.76、宿舎以外）：今の専門・選べる専門の説明と、専門ごとの「着工」「改装」ボタン（並びは FacilityBalance.GetSpecialtyOptions）。</summary>
		public RichTextLabel? FacilitySpecialtyLabel { get; set; }
		public List<Button> FacilitySpecialtyButtons { get; } = new();
		public List<Guid> AddCandidateIds { get; } = new();
		public List<Guid> RemoveCandidateIds { get; } = new();
		public Button ActionButton { get; set; } = null!;
	}

	/// <summary>重点伝授特性を選ぶメニュー（全訓練施設で共用。開いた施設は _focusMenuFacility で覚える）。</summary>
	private PopupMenu _focusTraitMenu = null!;
	private FacilityType _focusMenuFacility;
	private readonly List<string?> _focusMenuTraitIds = new();

	public override void _Ready()
	{
		_constructionBanner = GetNode<PanelContainer>("%ConstructionBanner");
		_constructionStatusIcon = GetNode<Label>("%ConstructionStatusIcon");
		_constructionTitleLabel = GetNode<Label>("%ConstructionTitleLabel");
		_constructionWeeksLabel = GetNode<Label>("%ConstructionWeeksLabel");
		_constructionProgressBar = GetNode<ProgressBar>("%ConstructionProgressBar");
		_constructionDetailLabel = GetNode<Label>("%ConstructionDetailLabel");
		_facilityGrid = GetNode<GridContainer>("%FacilityGrid");

		BindCard(FacilityType.Dormitory, "%Card_Dormitory");
		BindCard(FacilityType.Infirmary, "%Card_Infirmary");
		BindCard(FacilityType.Tavern, "%Card_Tavern");
		BindCard(FacilityType.DrillHall, "%Card_DrillHall");
		BindCard(FacilityType.Academy, "%Card_Academy");
		BindCard(FacilityType.SkillHall, "%Card_SkillHall");
		BindCard(FacilityType.WarRoom, "%Card_WarRoom");
		BindCard(FacilityType.RecruitmentOffice, "%Card_RecruitmentOffice");

		_focusTraitMenu = new PopupMenu();
		_focusTraitMenu.IdPressed += OnFocusTraitMenuIdPressed;
		AddChild(_focusTraitMenu);
	}

	private void BindCard(FacilityType type, string cardUniqueName)
	{
		var card = GetNode<PanelContainer>(cardUniqueName);
		UiStyles.ApplyPanelArt(card, innerMargin: 4); // 施設カード（大きなパネル）にアートの枠
		var vbox = card.GetNode<VBoxContainer>("Margin/VBox");
		var headerRow = vbox.GetNode<HBoxContainer>("HeaderRow");

		PanelContainer? instructorPanel = null;
		RichTextLabel? instructorLabel = null;

		if (vbox.HasNode("InstructorPanel"))
		{
			instructorPanel = vbox.GetNode<PanelContainer>("InstructorPanel");
			instructorLabel = instructorPanel.GetNode<RichTextLabel>("InstructorMargin/InstructorLabel");
		}

		// 顧問（教官・参謀・スカウト）の任命は、配属先の施設カードの顧問欄の直下から行う（コードで構築、シーン変更なし）。
		// 顧問管理ポップアップを開く（役職の選択はポップアップ側）。施設が未建設の間は押せない。
		Button? appointButton = null;
		if (instructorPanel != null)
		{
			// 顧問欄の中（文面の右）に置く：文面とボタンを横に並べ、欄を1つにまとめる
			var margin = instructorLabel!.GetParent();
			var inner = new HBoxContainer();
			inner.AddThemeConstantOverride("separation", 10);
			margin.RemoveChild(instructorLabel);
			margin.AddChild(inner);
			instructorLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			inner.AddChild(instructorLabel);
			appointButton = new Button { SizeFlagsVertical = SizeFlags.ShrinkCenter };
			appointButton.Pressed += () => AdvisorRequested.Invoke(type);
			inner.AddChild(appointButton);
		}

		// 訓練施設だけ、顧問欄の下に「伝授する特性」ボタンを置く。
		Button? focusTraitButton = null;
		if (instructorPanel != null && FacilityBalance.IsTrainingFacility(type))
		{
			focusTraitButton = new Button { Visible = false };
			focusTraitButton.Pressed += () => OpenFocusTraitMenu(type, focusTraitButton);
			vbox.AddChild(focusTraitButton);
			vbox.MoveChild(focusTraitButton, instructorPanel!.GetIndex() + 1);
		}

		// 訓練施設だけ、訓練生の欄（§0.70：訓練は月ごとの約束）を着工ボタンの上に置く。
		RichTextLabel? traineeLabel = null;
		OptionButton? addTrainee = null;
		OptionButton? removeTrainee = null;
		OptionButton? specialty = null;
		if (FacilityBalance.IsTrainingFacility(type))
		{
			traineeLabel = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			specialty = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 6);
			addTrainee = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			removeTrainee = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			row.AddChild(addTrainee);
			row.AddChild(removeTrainee);
			var actionButton = vbox.GetNode<Button>("ActionButton");
			vbox.AddChild(specialty);
			vbox.AddChild(traineeLabel);
			vbox.AddChild(row);
			vbox.MoveChild(specialty, actionButton.GetIndex());
			vbox.MoveChild(traineeLabel, actionButton.GetIndex());
			vbox.MoveChild(row, actionButton.GetIndex());
		}

		// 専門（§0.76）：説明と、選択肢ごとのボタン（Lv3では「〇〇で着工」、Lv4以上では「〇〇に改装」）を着工ボタンの上に置く。
		RichTextLabel? facilitySpecialtyLabel = null;
		var facilitySpecialtyButtons = new List<Button>();
		if (FacilityBalance.HasSpecialty(type))
		{
			var actionButton = vbox.GetNode<Button>("ActionButton");
			facilitySpecialtyLabel = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 6);
			foreach (var option in FacilityBalance.GetSpecialtyOptions(type))
			{
				var button = new Button { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
				button.Pressed += () => OnFacilitySpecialtyPressed(type, option);
				row.AddChild(button);
				facilitySpecialtyButtons.Add(button);
			}
			vbox.AddChild(facilitySpecialtyLabel);
			vbox.AddChild(row);
			vbox.MoveChild(facilitySpecialtyLabel, actionButton.GetIndex());
			vbox.MoveChild(row, actionButton.GetIndex());
		}

		var binding = new FacilityCardBinding
		{
			Type = type,
			CardRoot = card,
			TitleLabel = headerRow.GetNode<Label>("TitleLabel"),
			LevelBadge = headerRow.GetNode<Label>("LevelBadge"),
			CurrentEffectLabel = vbox.GetNode<RichTextLabel>("CurrentEffectLabel"),
			NextEffectLabel = vbox.GetNode<RichTextLabel>("NextEffectLabel"),
			CostLabel = vbox.GetNode<Label>("CostLabel"),
			InstructorPanel = instructorPanel,
			InstructorLabel = instructorLabel,
			AppointButton = appointButton,
			FocusTraitButton = focusTraitButton,
			ActionButton = vbox.GetNode<Button>("ActionButton"),
			TraineeLabel = traineeLabel,
			AddTraineeOption = addTrainee,
			RemoveTraineeOption = removeTrainee,
			SpecialtyOption = specialty,
			FacilitySpecialtyLabel = facilitySpecialtyLabel,
		};

		binding.FacilitySpecialtyButtons.AddRange(facilitySpecialtyButtons);
		binding.ActionButton.Pressed += () => OnStartConstructionPressed(type);
		if (addTrainee != null)
			addTrainee.ItemSelected += index => OnAddTraineeSelected(binding, index);
		if (removeTrainee != null)
			removeTrainee.ItemSelected += index => OnRemoveTraineeSelected(binding, index);
		if (specialty != null)
			specialty.ItemSelected += index => OnSpecialtySelected(binding, index);
		_cardBindings[type] = binding;
	}

	public void Initialize(FacilitySystem facilitySystem)
	{
		_facilitySystem = facilitySystem;
	}

	/// <summary>最新のゲーム状態でタブ全体を再描画する（MainDashboard.RefreshAllから毎回呼ぶ）。</summary>
	public void Refresh(GameState state)
	{
		_state = state;
		RefreshBanner();
		RefreshAllCards();
	}

	private void RefreshBanner()
	{
		if (_state.UnderConstruction != null)
		{
			var uc = _state.UnderConstruction;
			string facilityName = FacilityLabel(uc.Type);
			int prevLevel = Math.Max(0, uc.TargetLevel - 1);
			int totalWeeks = uc.IsRemodel ? FacilityBalance.RemodelWeeks : FacilityBalance.GetConstructionWeeks(uc.Type, prevLevel);
			int remaining = uc.WeeksRemaining;

			_constructionStatusIcon.Text = "🔨";
			string specialtyText = uc.TargetSpecialty != FacilitySpecialty.None ? $"（専門：{SpecialtyLabel(uc.TargetSpecialty)}）" : "";
			_constructionTitleLabel.Text = uc.IsRemodel
				? $"【改装工事中】{facilityName} Lv{uc.TargetLevel}{specialtyText}"
				: $"【改築工事中】{facilityName} Lv{uc.TargetLevel} 改築工事{specialtyText}";
			_constructionWeeksLabel.Text = $"残り {remaining} 週";
			_constructionWeeksLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.3f, 1.0f));

			_constructionProgressBar.Visible = true;
			_constructionProgressBar.MinValue = 0;
			_constructionProgressBar.MaxValue = Math.Max(totalWeeks, 1);
			int elapsed = Math.Max(0, totalWeeks - remaining);
			_constructionProgressBar.Value = elapsed;

			_constructionDetailLabel.Text = $"総工期：{totalWeeks}週（進捗：{elapsed}/{totalWeeks}週）。工事中も現在Lv（と専門）の効果は維持されます。";
		}
		else
		{
			_constructionStatusIcon.Text = "ℹ️";
			_constructionTitleLabel.Text = "現在進行中の工事はありません（同時着工1件まで）";
			_constructionWeeksLabel.Text = "待機中";
			_constructionWeeksLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.8f, 0.9f, 1.0f));

			_constructionProgressBar.Visible = false;
			_constructionDetailLabel.Text = "各施設カードの「🔨 改築着工」ボタンから改築工事を開始できます。";
		}
	}

	private void RefreshAllCards()
	{
		foreach (var binding in _cardBindings.Values)
		{
			RefreshCard(binding);
		}
	}

	private void RefreshCard(FacilityCardBinding binding)
	{
		int currentLevel = _state.GetFacilityLevel(binding.Type);
		bool isMaxLevel = currentLevel >= FacilityBalance.MaxLevel;
		int cost = FacilityBalance.GetUpgradeCost(binding.Type, currentLevel);
		int weeks = FacilityBalance.GetConstructionWeeks(binding.Type, currentLevel);

		// ヘッダー表示
		binding.TitleLabel.Text = FacilityTitle(binding.Type);
		bool available = FacilitySystem.IsAvailable(_state, binding.Type); // 段階的な開放（§0.78）
		binding.LevelBadge.Text = currentLevel == 0 ? "Lv 0（未建設）" : $"Lv {currentLevel}" + (isMaxLevel ? "（最大）" : "");
		if (!available)
			binding.LevelBadge.Text = "準備中";
		binding.LevelBadge.Modulate = currentLevel == 0
			? new Color(0.6f, 0.65f, 0.7f, 1.0f)
			: isMaxLevel
				? new Color(1.0f, 0.85f, 0.3f, 1.0f)
				: new Color(0.4f, 0.85f, 1.0f, 1.0f);

		// 現在効果
		binding.CurrentEffectLabel.Clear();
		binding.CurrentEffectLabel.AppendText(GetCurrentEffectText(binding.Type, currentLevel));

		// 次Lvプレビュー
		binding.NextEffectLabel.Clear();
		binding.NextEffectLabel.AppendText(GetNextEffectText(binding.Type, currentLevel));
		if (!available)
		{
			binding.NextEffectLabel.Clear();
			binding.NextEffectLabel.AppendText("[color=orange]準備中：最初の引退者（顧問候補）が出ると建てられる。引退者を顧問に置く施設なので、それまでは建てても何も起きない。[/color]");
		}

		// 改築費・工期
		if (isMaxLevel)
		{
			binding.CostLabel.Text = "改築費：―　工期：―";
		}
		else
		{
			string feeType = currentLevel == 0 ? "新設費" : "改築費";
			binding.CostLabel.Text = $"{feeType}：{cost} G　工期：{weeks} 週";
		}

		// 引退教官・顧問スロット
		if (binding.InstructorLabel != null)
		{
			binding.InstructorLabel.Clear();
			binding.InstructorLabel.AppendText(GetInstructorSlotText(binding.Type, currentLevel));
		}
		if (binding.AppointButton != null)
			RefreshAppointButton(binding.AppointButton, binding.Type, currentLevel);
		if (binding.FocusTraitButton != null)
			RefreshFocusTraitButton(binding.FocusTraitButton, binding.Type, currentLevel);
		if (binding.TraineeLabel != null)
			RefreshTrainees(binding, currentLevel);
		if (binding.FacilitySpecialtyLabel != null)
			RefreshFacilitySpecialty(binding, currentLevel, cost, weeks);
		// Lv3→4は専門を選んで着工する（専門ごとのボタンから。§0.76）
		binding.ActionButton.Visible = !FacilitySystem.NeedsSpecialtyChoice(binding.Type, currentLevel);

		// 着工ボタンの防御的ガード制御
		if (!available)
		{
			binding.ActionButton.Disabled = true;
			binding.ActionButton.Text = "準備中（引退者が出ると建てられる）";
			binding.ActionButton.TooltipText = "最初の引退者（顧問候補）が出ると建てられるようになる（§0.78）。";
		}
		else if (!isMaxLevel && FacilitySystem.IsBlockedByLevelCap(_state, binding.Type))
		{
			// 施設の上限Lv（§0.79）：倒したボスの数で上がる
			int next = currentLevel + 1;
			binding.ActionButton.Disabled = true;
			binding.ActionButton.Text = LevelCapText(next);
			binding.ActionButton.TooltipText = $"施設をLv{next}まで改築できるのは、ボスを{FacilityBalance.GetBossesRequiredForLevel(next)}体倒してから（全フィールドの合計）。";
		}
		else if (isMaxLevel)
		{
			binding.ActionButton.Disabled = true;
			binding.ActionButton.Text = "最大Lv (Lv5) 到達";
			binding.ActionButton.TooltipText = "この施設はすでに最大レベル（Lv5）に到達しています。";
		}
		else if (_state.UnderConstruction != null)
		{
			binding.ActionButton.Disabled = true;
			if (_state.UnderConstruction.Type == binding.Type)
			{
				binding.ActionButton.Text = $"🔨 工事進行中（残り {_state.UnderConstruction.WeeksRemaining}週）";
				binding.ActionButton.TooltipText = "現在、この施設の改築工事が進行中です。完成までお待ちください。";
			}
			else
			{
				string busyName = FacilityLabel(_state.UnderConstruction.Type);
				binding.ActionButton.Text = "他の工事が進行中";
				binding.ActionButton.TooltipText = $"現在、{busyName}の工事が進行中です（同時着工は1件まで）。";
			}
		}
		else if (_state.Gold < cost)
		{
			binding.ActionButton.Disabled = true;
			string actText = currentLevel == 0 ? "新設着工" : "改築着工";
			binding.ActionButton.Text = $"資金不足 ({cost}G)";
			binding.ActionButton.TooltipText = $"資金が不足しています（必要：{cost} G ／ 所持金：{_state.Gold} G）。";
		}
		else
		{
			binding.ActionButton.Disabled = false;
			string actText = currentLevel == 0 ? "新設着工" : "改築着工";
			binding.ActionButton.Text = $"🔨 {actText} ({cost}G / {weeks}週)";
			binding.ActionButton.TooltipText = $"{FacilityLabel(binding.Type)}の{actText}を行います。着工時に費用{cost}Gを前払いします。";
		}
	}

	// ==================== 訓練生（§0.70：訓練は月ごとの約束） ====================

	private readonly TrainingSystem _trainingSystem = new();

	/// <summary>訓練生の欄：今の訓練生と枠、入れる候補・外す候補。月のはじめでなければ選べない。</summary>
	private void RefreshTrainees(FacilityCardBinding binding, int level)
	{
		int capacity = _trainingSystem.GetSlotCapacity(_state, binding.Type);
		var trainees = _state.Adventurers.Where(a => _state.TrainingAssignments.TryGetValue(a.Id, out var f) && f == binding.Type).ToList();
		bool canChange = TrainingSystem.CanChangeAssignments(_state);

		binding.TraineeLabel!.Clear();
		string names = trainees.Count == 0 ? "[color=gray]なし[/color]" : string.Join("、", trainees.Select(a => a.Name));
		binding.TraineeLabel.AppendText($"訓練生（{trainees.Count}/{capacity}）：{names}");
		if (level > 0)
			binding.TraineeLabel.AppendText(canChange
				? "\n[color=gray]訓練生はこの月は出撃しない（部隊のほかの隊員だけで出る）。[/color]"
				: $"\n[color=orange]訓練生を入れる・外すのは月のはじめだけ（今は第{GameCalendar.WeekOfMonth(_state.WeekNumber)}週）。[/color]");

		var add = binding.AddTraineeOption!;
		add.Clear();
		binding.AddCandidateIds.Clear();
		add.AddItem("＋ 訓練に入れる…");
		foreach (var a in _state.Adventurers.Where(a => !a.IsRetired && !a.IsDispatched && !TrainingSystem.IsTraining(_state, a.Id)).OrderBy(a => a.Name))
		{
			add.AddItem($"{a.Name}（{a.Age}歳）");
			binding.AddCandidateIds.Add(a.Id);
		}
		add.Selected = 0;
		add.Disabled = !canChange || level == 0 || trainees.Count >= capacity || binding.AddCandidateIds.Count == 0;

		var remove = binding.RemoveTraineeOption!;
		remove.Clear();
		binding.RemoveCandidateIds.Clear();
		remove.AddItem("− 訓練から外す…");
		foreach (var a in trainees)
		{
			remove.AddItem(a.Name);
			binding.RemoveCandidateIds.Add(a.Id);
		}
		remove.Selected = 0;
		remove.Disabled = !canChange || trainees.Count == 0;

		RefreshSpecialty(binding, level, canChange);
	}

	/// <summary>育て方（§0.75）：0＝両方、1・2＝施設の能力の1つ目・2つ目に特化。月のはじめだけ変えられる。</summary>
	private void RefreshSpecialty(FacilityCardBinding binding, int level, bool canChange)
	{
		var option = binding.SpecialtyOption!;
		var stats = FacilityBalance.GetTrainingTargetStats(binding.Type);
		string? current = TrainingSystem.GetSpecialtyStat(_state, binding.Type);
		option.Clear();
		option.AddItem($"育て方：両方（{string.Join("・", stats)}）");
		foreach (var stat in stats)
			option.AddItem($"育て方：{stat}に特化（成長の確率 ×{FacilityBalance.TrainingSpecialtyGrowthMultiplier:0.##}）");
		option.Selected = current == null ? 0 : Array.IndexOf(stats, current) + 1;
		option.Disabled = !canChange || level == 0;
		option.TooltipText = canChange
			? "両方：成長が2つの能力のどちらかに入る。特化：選んだ能力にだけ入り、成長の確率が少し上がる。月のはじめだけ変えられる（無料）。"
			: "育て方を変えられるのは月のはじめだけ。";
	}

	private void OnSpecialtySelected(FacilityCardBinding binding, long index)
	{
		var stats = FacilityBalance.GetTrainingTargetStats(binding.Type);
		string? stat = index <= 0 || index > stats.Length ? null : stats[index - 1];
		TrainingSystem.SetSpecialtyStat(_state, binding.Type, stat);
		StateChanged.Invoke();
	}

	/// <summary>カードの効果欄に出す、訓練で伸びる能力（特化していればその能力だけ）。</summary>
	private string TrainingTargetText(FacilityType type) =>
		TrainingSystem.GetSpecialtyStat(_state, type) is string stat
			? $"[color=cyan]{stat}[/color]に特化"
			: $"[color=cyan]{string.Join("・", FacilityBalance.GetTrainingTargetStats(type))}[/color]";

	private void OnAddTraineeSelected(FacilityCardBinding binding, long index)
	{
		if (index <= 0 || index > binding.AddCandidateIds.Count) return;
		_trainingSystem.TryAssignForMonth(_state, binding.AddCandidateIds[(int)index - 1], binding.Type);
		StateChanged.Invoke();
	}

	private void OnRemoveTraineeSelected(FacilityCardBinding binding, long index)
	{
		if (index <= 0 || index > binding.RemoveCandidateIds.Count) return;
		_trainingSystem.TryUnassignForMonth(_state, binding.RemoveCandidateIds[(int)index - 1]);
		StateChanged.Invoke();
	}

	/// <summary>顧問の任命ボタン：役職名と、任命済みなら「交代」と出す。施設が未建設なら押せない。</summary>
	private void RefreshAppointButton(Button button, FacilityType type, int level)
	{
		string role = type == FacilityType.WarRoom ? "参謀" : type == FacilityType.RecruitmentOffice ? "スカウト" : "教官";
		bool assigned = type == FacilityType.WarRoom ? _state.AssignedAdvisor.HasValue
			: type == FacilityType.RecruitmentOffice ? _state.AssignedScoutMaster.HasValue
			: GetAssignedTrainer(type) != null;
		button.Disabled = level < 1;
		button.Text = assigned ? "👔 交代" : "👔 任命";
		button.TooltipText = level < 1
			? $"施設をLv1まで建設すると、引退した冒険者を{role}に任命できる。"
			: $"引退した冒険者を{role}に{(assigned ? "任命し直す・解任する" : "任命する")}（顧問管理を開く）。1人が担当できるのは1つの役職まで。";
	}

	private Adventurer? GetAssignedTrainer(FacilityType type) =>
		_state.AssignedTrainers.TryGetValue(type, out var trainerId) && trainerId.HasValue
			? _state.RetiredAdventurers.FirstOrDefault(a => a.Id == trainerId.Value)
			: null;

	private static string TraitName(string traitId) => TraitCatalog.FindById(traitId)?.DisplayName ?? traitId;

	/// <summary>「伝授する特性」ボタン：教官がいるときだけ表示し、今の選択（未設定なら自動）を出す（→ 03 §7.1・§0.54）。</summary>
	private void RefreshFocusTraitButton(Button button, FacilityType type, int level)
	{
		var trainer = level < 1 ? null : GetAssignedTrainer(type);
		button.Visible = trainer != null;
		if (trainer == null)
			return;

		bool hasCandidates = TrainingSystem.GetTransmittableTraitIds(trainer).Count > 0;
		string? focus = TrainingSystem.GetTrainerFocusTrait(_state, type);
		button.Disabled = !hasCandidates;
		button.Text = $"📜 伝授する特性：{(focus == null ? "自動" : TraitName(focus))} ▼";
		button.TooltipText = hasCandidates
			? "教官が重点的に伝授する特性を選ぶ。生徒がすでに持っていれば、ほかの特性を特性枠の順に伝授する。\n「自動」は教官の特性枠の順。教官が替わると自動に戻る。"
			: "この教官には伝授できる特性が無い。";
	}

	private void OpenFocusTraitMenu(FacilityType type, Button anchor)
	{
		var trainer = GetAssignedTrainer(type);
		if (trainer == null)
			return;

		_focusMenuFacility = type;
		_focusMenuTraitIds.Clear();
		_focusTraitMenu.Clear();

		string? focus = TrainingSystem.GetTrainerFocusTrait(_state, type);
		_focusMenuTraitIds.Add(null);
		_focusTraitMenu.AddRadioCheckItem("自動（教官の特性枠の順）", 0);
		_focusTraitMenu.SetItemChecked(0, focus == null);
		_focusTraitMenu.SetItemTooltip(0, "教官の特性枠の順に伝授する。");
		foreach (var traitId in TrainingSystem.GetTransmittableTraitIds(trainer))
		{
			int id = _focusMenuTraitIds.Count;
			_focusMenuTraitIds.Add(traitId);
			var def = TraitCatalog.FindById(traitId);
			// レアは★を付ける。説明はツールチップ（選ぶ前に効果を確かめられるように）
			_focusTraitMenu.AddRadioCheckItem($"{(def?.IsRare == true ? "★" : "")}{TraitName(traitId)}", id);
			int itemIndex = _focusTraitMenu.GetItemIndex(id);
			_focusTraitMenu.SetItemChecked(itemIndex, traitId == focus);
			_focusTraitMenu.SetItemTooltip(itemIndex, def?.Description ?? "");
		}

		var rect = anchor.GetGlobalRect();
		_focusTraitMenu.Popup(new Rect2I((Vector2I)(rect.Position + new Vector2(0, rect.Size.Y)), new Vector2I(Math.Max((int)rect.Size.X, 320), 0)));
	}

	private void OnFocusTraitMenuIdPressed(long id)
	{
		if (id < 0 || id >= _focusMenuTraitIds.Count)
			return;
		if (TrainingSystem.SetTrainerFocusTrait(_state, _focusMenuFacility, _focusMenuTraitIds[(int)id]))
			StateChanged.Invoke();
	}

	private void OnStartConstructionPressed(FacilityType type)
	{
		if (_facilitySystem.TryStartConstruction(_state, type))
		{
			StateChanged.Invoke();
		}
	}

	// ==================== 施設の専門（§0.76） ====================

	/// <summary>
	/// 専門の欄：Lv3まで＝Lv3→4の工事で選べる専門の案内、Lv3＝専門ごとの「〇〇で着工」、Lv4以上＝今の専門と、もう一方への「改装」。
	/// </summary>
	private void RefreshFacilitySpecialty(FacilityCardBinding binding, int level, int cost, int weeks)
	{
		var type = binding.Type;
		var options = FacilityBalance.GetSpecialtyOptions(type);
		var current = _state.GetFacilitySpecialty(type);
		var construction = _state.UnderConstruction;
		bool busy = construction != null;
		bool choose = FacilitySystem.NeedsSpecialtyChoice(type, level);
		var label = binding.FacilitySpecialtyLabel!;
		label.Clear();

		if (current == FacilitySpecialty.None)
		{
			label.AppendText(choose
				? "[color=gold]Lv4への改築で専門を1つ選ぶ（Lv4・5の効果は選んだ側だけ）。[/color]"
				: $"[color=gray]Lv{FacilityBalance.SpecialtyFromLevel}→Lv{FacilityBalance.SpecialtyFromLevel + 1}の改築で専門を選ぶ：{string.Join("／", options.Select(SpecialtyLabel))}[/color]");
		}
		else
		{
			label.AppendText($"専門：[b][color=gold]{SpecialtyLabel(current)}[/color][/b]（{SpecialtyEffectText(type, current, level)}）");
		}
		if (construction != null && construction.Type == type && construction.TargetSpecialty != FacilitySpecialty.None)
			label.AppendText($"\n[color=orange]{(construction.IsRemodel ? "改装中" : "工事中")}：完成すると専門は「{SpecialtyLabel(construction.TargetSpecialty)}」[/color]");

		int remodelCost = FacilityBalance.GetRemodelCost(level);
		for (int i = 0; i < options.Length; i++)
		{
			var option = options[i];
			var button = binding.FacilitySpecialtyButtons[i];
			int nextLevel = choose ? level + 1 : level;
			string effect = SpecialtyEffectText(type, option, Math.Max(nextLevel, FacilityBalance.SpecialtyFromLevel + 1));
			if (choose)
			{
				button.Visible = true;
				bool capped = FacilitySystem.IsBlockedByLevelCap(_state, type); // 施設の上限Lv（§0.79）
				button.Text = capped ? $"{SpecialtyLabel(option)}（{LevelCapText(level + 1)}）" : $"🔨 {SpecialtyLabel(option)}で改築 ({cost}G / {weeks}週)";
				button.Disabled = busy || _state.Gold < cost || capped;
				button.TooltipText = $"{SpecialtyLabel(option)}：{SpecialtyDescription(option)}\nLv4で：{effect}\n" +
					(capped ? $"ボスを{FacilityBalance.GetBossesRequiredForLevel(level + 1)}体倒すと、Lv4への改築（専門を選ぶ）ができる。" : busy ?"他の工事が進行中（同時着工は1件まで）。" : _state.Gold < cost ? $"資金が不足しています（必要：{cost} G）。" : $"着工時に費用{cost}Gを前払いします。");
			}
			else if (current != FacilitySpecialty.None && option != current)
			{
				button.Visible = true;
				button.Text = $"🛠 {SpecialtyLabel(option)}に改装 ({remodelCost}G / {FacilityBalance.RemodelWeeks}週)";
				button.Disabled = busy || _state.Gold < remodelCost;
				button.TooltipText = $"{SpecialtyLabel(option)}：{SpecialtyDescription(option)}\n今のLvで：{SpecialtyEffectText(type, option, level)}\n" +
					"改装中は今の専門のまま。建設キューを1件使う。" +
					(busy ? "\n他の工事が進行中（同時着工は1件まで）。" : _state.Gold < remodelCost ? $"\n資金が不足しています（必要：{remodelCost} G）。" : "");
			}
			else
			{
				button.Visible = false;
			}
		}
	}

	private void OnFacilitySpecialtyPressed(FacilityType type, FacilitySpecialty specialty)
	{
		int level = _state.GetFacilityLevel(type);
		bool done = FacilitySystem.NeedsSpecialtyChoice(type, level)
			? _facilitySystem.TryStartConstruction(_state, type, specialty)
			: _facilitySystem.TryStartRemodel(_state, type, specialty);
		if (done)
			StateChanged.Invoke();
	}

	/// <summary>上限Lv（§0.79）で止められている改築の案内：「Lv3へ：ボス12体で開く（今8体）」。</summary>
	private string LevelCapText(int level) =>
		$"Lv{level}へ：ボス{FacilityBalance.GetBossesRequiredForLevel(level)}体で開く（今{EquipmentSystem.CountDefeatedBosses(_state)}体）";

	/// <summary>専門の名前（§0.76）。</summary>
	public static string SpecialtyLabel(FacilitySpecialty specialty) => specialty switch
	{
		FacilitySpecialty.Elite => "精鋭",
		FacilitySpecialty.Rivalry => "切磋琢磨",
		FacilitySpecialty.Sanatorium => "療養院",
		FacilitySpecialty.FieldAid => "戦地救護",
		FacilitySpecialty.Appraisal => "目利き",
		FacilitySpecialty.Recruiting => "募集",
		FacilitySpecialty.Leisure => "憩い",
		FacilitySpecialty.Trade => "商い",
		FacilitySpecialty.Analysis => "解析",
		FacilitySpecialty.Pathfinding => "踏破",
		_ => "なし",
	};

	/// <summary>専門の性格（ボタンのツールチップ）。</summary>
	private static string SpecialtyDescription(FacilitySpecialty specialty) => specialty switch
	{
		FacilitySpecialty.Elite => "訓練枠は1名のまま、成長の確率が上がる（1人を深く伸ばす）",
		FacilitySpecialty.Rivalry => "訓練枠が2名になるが、成長の確率はLv3より低い（2人をそこそこ伸ばす）",
		FacilitySpecialty.Sanatorium => "静養のHP回復と重傷の回復が早くなる",
		FacilitySpecialty.FieldAid => "ボス戦で致命的な損耗を受けにくくなる（深く潜るときの保険）",
		FacilitySpecialty.Appraisal => "副官の見立て（伸びしろの段階）が細かくなる",
		FacilitySpecialty.Recruiting => "新春採用試験の応募者が増える",
		FacilitySpecialty.Leisure => "満足度が毎週よく回復する",
		FacilitySpecialty.Trade => "アルベールの内職の売上が増える",
		FacilitySpecialty.Analysis => "迷宮調査の解析が進みやすくなる",
		FacilitySpecialty.Pathfinding => "道中の走破力が上がる",
		_ => "",
	};

	/// <summary>その専門の施設がLv level のときの効果（専門の分）。</summary>
	private static string SpecialtyEffectText(FacilityType type, FacilitySpecialty specialty, int level) => specialty switch
	{
		FacilitySpecialty.Elite or FacilitySpecialty.Rivalry =>
			$"訓練枠 {FacilityBalance.GetTrainingSlotCapacity(level, specialty)}名・成長の確率 ×{FacilityBalance.GetTrainingGrowthMultiplier(level, specialty):F2}",
		FacilitySpecialty.FieldAid =>
			$"ボス戦の致命の損耗の閾値 +{FacilityBalance.GetFieldAidSurvivalBonus(level, specialty):0.#}",
		FacilitySpecialty.Sanatorium =>
			$"HP自然回復 ×{FacilityBalance.GetInfirmaryHpRecoveryMultiplier(level, specialty):F2}・重傷回復 {FacilityBalance.GetInfirmaryInjuryRecoverySpeed(level, specialty)}週/週",
		FacilitySpecialty.Appraisal => $"目利き +{FacilityBalance.GetAppraisalEyeBonus(level, specialty):0.0#}",
		FacilitySpecialty.Recruiting => $"応募者 +{FacilityBalance.GetRecruitingCandidateBonus(level, specialty)}名",
		FacilitySpecialty.Leisure => $"満足度回復 +{FacilityBalance.GetTavernSatisfactionRecovery(level, specialty)} pt/週",
		FacilitySpecialty.Trade => $"内職の売上 +{FacilityBalance.GetTradeSideJobBonus(level, specialty)}G/月",
		FacilitySpecialty.Analysis => $"解析率 +{FacilityBalance.GetAnalysisIntelBonus(level, specialty) * 100:0}%",
		FacilitySpecialty.Pathfinding => $"走破力 +{FacilityBalance.GetPathfindingTraversalBonus(level, specialty):0.#}",
		_ => "",
	};

	private string GetCurrentEffectText(FacilityType type, int level)
	{
		var specialty = _state.GetFacilitySpecialty(type);
		return type switch
		{
			FacilityType.Dormitory =>
				$"現役冒険者の保有枠上限：[b]{FacilityBalance.GetDormitoryCapacity(level)}名[/b]",

			FacilityType.Infirmary =>
				$"負傷の回復速度：[b]{FacilityBalance.GetInfirmaryInjuryRecoverySpeed(level, specialty)}週/週[/b]　HP自然回復：[b]×{FacilityBalance.GetInfirmaryHpRecoveryMultiplier(level, specialty):F2}[/b]"
				+ (specialty == FacilitySpecialty.FieldAid ? $"\nボス戦の致命の損耗の閾値：[b]+{FacilityBalance.GetFieldAidSurvivalBonus(level, specialty):0.#}[/b]" : ""),

			FacilityType.Tavern =>
				$"週次満足度自然回復：[b]+{FacilityBalance.GetTavernSatisfactionRecovery(level, specialty)} pt/週[/b]"
				+ (specialty == FacilitySpecialty.Trade ? $"\n内職の売上：[b]+{FacilityBalance.GetTradeSideJobBonus(level, specialty)}G/月[/b]" : ""),

			_ when FacilityBalance.IsTrainingFacility(type) =>
				level == 0
					? "[color=gray]未稼働（未建設・訓練枠 0名）[/color]"
					: $"訓練枠：[b]{FacilityBalance.GetTrainingSlotCapacity(level, specialty)}名[/b]　成長の確率：[b]×{TrainingSystem.GetFacilityGrowthMultiplier(_state, type):F2}[/b]（{TrainingTargetText(type)}）",

			FacilityType.WarRoom =>
				level == 0
					? "[color=gray]未稼働（未建設・参謀配置不可）[/color]"
					: "参謀本部（迷宮調査の解析支援・道中潜行の走破支援）"
					  + (specialty != FacilitySpecialty.None ? $"\n専門の効果：[b]{SpecialtyEffectText(type, specialty, level)}[/b]" : ""),

			FacilityType.RecruitmentOffice =>
				level == 0
					? "[color=gray]未稼働（未建設・スカウト配置不可）[/color]"
					: "新人スカウト本部（新春採用試験の有望新人応募率向上）"
					  + (specialty != FacilitySpecialty.None ? $"\n専門の効果：[b]{SpecialtyEffectText(type, specialty, level)}[/b]" : ""),

			_ => $"施設効果（Lv {level}）"
		};
	}

	private string GetNextEffectText(FacilityType type, int level)
	{
		if (level >= FacilityBalance.MaxLevel)
			return "[color=gray]最大Lv到達済み（改築上限）[/color]";

		// Lv3→4は専門を選ぶ。Lv4→5は今の専門が伸びる（§0.76）。
		if (FacilitySystem.NeedsSpecialtyChoice(type, level))
			return "次Lv：[color=gold]専門を選んで改築[/color]（下のボタン。ボタンにかざすとLv4の効果が見える）";
		var specialty = _state.GetFacilitySpecialty(type);
		if (specialty != FacilitySpecialty.None)
			return $"次Lv：{SpecialtyLabel(specialty)} [color=cyan]{SpecialtyEffectText(type, specialty, level + 1)}[/color]";

		return type switch
		{
			FacilityType.Dormitory =>
				$"次Lv：現役枠上限 [color=cyan]+4名[/color]（計 {FacilityBalance.GetDormitoryCapacity(level + 1)}名）",

			FacilityType.Infirmary =>
				"次Lv：重傷回復 [color=cyan]+1週/週[/color]、HP自然回復 [color=cyan]+0.25倍[/color]",

			FacilityType.Tavern =>
				$"次Lv：満足度回復 [color=cyan]+1 pt/週[/color]（計 +{FacilityBalance.GetTavernSatisfactionRecovery(level + 1, FacilitySpecialty.None)} pt/週）",

			_ when FacilityBalance.IsTrainingFacility(type) =>
				level == 0
					? "次Lv：訓練枠 [color=cyan]1名[/color] 開放、教官配置 開放"
					: $"次Lv：成長の確率 [color=cyan]×{FacilityBalance.GetTrainingGrowthMultiplier(level + 1, FacilitySpecialty.None):F2}[/color]（特化の倍率は別に掛かる）",

			FacilityType.WarRoom =>
				level == 0
					? "次Lv：参謀配置機能 開放"
					: $"次Lv：施設拡充（Lv{level + 1}）",

			FacilityType.RecruitmentOffice =>
				level == 0
					? "次Lv：スカウト顧問配置機能 開放"
					: $"次Lv：施設拡充（Lv{level + 1}）",

			_ => $"次Lv：Lv {level + 1}"
		};
	}
	private string GetInstructorSlotText(FacilityType type, int level)
	{
		// 訓練3施設（§0.75）
		if (FacilityBalance.IsTrainingFacility(type))
		{
			if (level < 1)
				return "[color=gray]教官：未開放（Lv1建設で開放）[/color]";

			if (_state.AssignedTrainers.TryGetValue(type, out var trainerId) && trainerId.HasValue)
			{
				var trainer = _state.RetiredAdventurers.FirstOrDefault(a => a.Id == trainerId.Value);
				if (trainer != null)
				{
					double bonus = AdvisorSystem.GetTrainerBonus(trainer, type);
					var stats = FacilityBalance.GetTrainingTargetStats(type);
					string statsStr = string.Join("・", stats);
					// 教官から生徒へ受け継がれうる特性（→ TrainingSystem.GetTransmittableTraitIds、03 §7.1・§0.35）。
					// 重点伝授特性（→ 03 §0.54）には★を付ける。
					string? focus = TrainingSystem.GetTrainerFocusTrait(_state, type);
					var transmittable = TrainingSystem.GetTransmittableTraitIds(trainer)
						.Select(id => id == focus ? $"★{TraitName(id)}" : TraitName(id))
						.ToList();
					string traitLine = transmittable.Count > 0
						? $"伝授可能: [color=cyan]{string.Join(", ", transmittable)}[/color]"
						: "[color=gray]伝授可能特性なし[/color]";
					// 在任週数と今の伝授確率（基礎＋師匠肌＋在任ボーナス、→ TrainingSystem.GetInheritanceChance、03 §7.1・§0.54）。
					int tenureWeeks = TrainingSystem.GetTrainerTenureWeeks(_state, type);
					double tenureBonus = TrainingSystem.GetTenureBonus(tenureWeeks);
					double chance = TrainingSystem.GetInheritanceChance(trainer, tenureWeeks);
					string tenureLine = $"在任 {tenureWeeks}週（伝授確率 +{tenureBonus * 100:0.#}%）・伝授確率 [color=lime]{chance * 100:0.#}%[/color]/週";
					return $"[color=gold]🎖️ 教官：{trainer.Name}（元{AdventurerPanel.JobLabel(trainer.JobClass)}）[/color]\n" +
					       $"伝授：[color=lime]{statsStr}成長率 +{bonus * 100:F1}%[/color]\n" +
					       tenureLine + "\n" +
					       traitLine;
				}
			}

			return "[color=gray]教官：未任命（右のボタンから任命できる）[/color]";
		}

		// 作戦資料室（参謀）
		if (type == FacilityType.WarRoom)
		{
			if (level < 1)
				return "[color=gray]参謀：未開放（Lv1建設で開放）[/color]";

			if (_state.AssignedAdvisor.HasValue)
			{
				var advisor = AdvisorSystem.GetAssignedAdvisor(_state);
				if (advisor != null)
				{
					double intelBonus = AdvisorSystem.GetAdvisorSurveyIntelBonus(_state);
					double travelBonus = AdvisorSystem.GetAdvisorTraversalPowerBonus(_state);
					return $"[color=cyan]🖋 参謀：{advisor.Name}（元{AdventurerPanel.JobLabel(advisor.JobClass)}）[/color]\n" +
					       $"支援：[color=lime]解析 +{intelBonus * 100:F0}% ／ 走破 +{travelBonus:F1}pt[/color]";
				}
			}

			return "[color=gray]参謀：未任命（右のボタンから任命できる）[/color]";
		}

		// 冒険者支援室（スカウト）
		if (type == FacilityType.RecruitmentOffice)
		{
			if (level < 1)
				return "[color=gray]スカウト：未開放（Lv1建設で開放）[/color]";

			if (_state.AssignedScoutMaster.HasValue)
			{
				var scout = _state.RetiredAdventurers.FirstOrDefault(a => a.Id == _state.AssignedScoutMaster.Value);
				if (scout != null)
				{
					double bonus = AdvisorSystem.GetScoutMasterBonus(scout);
					return $"[color=cyan]🧭 スカウト：{scout.Name}（元{AdventurerPanel.JobLabel(scout.JobClass)}）[/color]\n" +
					       $"補正：[color=lime]有望新人応募率 +{bonus * 100:F1}%[/color]";
				}
			}

			return "[color=gray]スカウト：未任命（右のボタンから任命できる）[/color]";
		}

		return "";
	}

	public static string FacilityTitle(FacilityType type) => type switch
	{
		FacilityType.Dormitory => "🛏️ 宿舎",
		FacilityType.Infirmary => "💉 医務室",
		FacilityType.Tavern => "🍺 ギルド酒場",
		FacilityType.DrillHall => "⚔️ 鍛錬所",
		FacilityType.Academy => "📚 学問所",
		FacilityType.SkillHall => "🏹 技巧所",
		FacilityType.WarRoom => "📜 作戦資料室",
		FacilityType.RecruitmentOffice => "🤝 冒険者支援室",
		_ => type.ToString()
	};

	public static string FacilityLabel(FacilityType type) => type switch
	{
		FacilityType.Dormitory => "宿舎",
		FacilityType.Infirmary => "医務室",
		FacilityType.Tavern => "ギルド酒場",
		FacilityType.DrillHall => "鍛錬所",
		FacilityType.Academy => "学問所",
		FacilityType.SkillHall => "技巧所",
		FacilityType.WarRoom => "作戦資料室",
		FacilityType.RecruitmentOffice => "冒険者支援室",
		_ => type.ToString()
	};
}
