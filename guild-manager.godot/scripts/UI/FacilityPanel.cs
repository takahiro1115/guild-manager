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
/// - メインエリアに全9施設（宿舎、医務室、酒場、戦士訓練所、教会、魔法研究所、斥候所、作戦資料室、冒険者支援室）を3列グリッドで配置。
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
	public event Action AdvisorRequested = delegate { };

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
		public Button ActionButton { get; set; } = null!;
	}

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
		BindCard(FacilityType.WarriorHall, "%Card_WarriorHall");
		BindCard(FacilityType.Church, "%Card_Church");
		BindCard(FacilityType.MageLab, "%Card_MageLab");
		BindCard(FacilityType.ScoutPost, "%Card_ScoutPost");
		BindCard(FacilityType.WarRoom, "%Card_WarRoom");
		BindCard(FacilityType.RecruitmentOffice, "%Card_RecruitmentOffice");

		BuildAdvisorRow();
	}

	/// <summary>
	/// 画面の先頭に「👔 顧問を任命」の行を置く（→ AdvisorRequested）。教官（4訓練所）・参謀（作戦資料室）・スカウト（冒険者支援室）は
	/// いずれも施設に紐づく役職のため、各カードの顧問欄と同じ画面から任命できるようにする（2026年9月、個人詳細の右肩から移設）。
	/// </summary>
	private void BuildAdvisorRow()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 12);

		var note = new Label
		{
			Text = "引退した冒険者を、教官（4訓練所）・参謀（作戦資料室）・スカウト（冒険者支援室）に任命できる。",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			VerticalAlignment = VerticalAlignment.Center,
		};
		note.AddThemeColorOverride("font_color", new Color(0.7f, 0.72f, 0.78f));
		row.AddChild(note);

		var button = new Button { Text = "👔 顧問を任命", CustomMinimumSize = new Vector2(140, 32) };
		button.Pressed += () => AdvisorRequested.Invoke();
		row.AddChild(button);

		AddChild(row);
		MoveChild(row, 0);
	}

	private void BindCard(FacilityType type, string cardUniqueName)
	{
		var card = GetNode<PanelContainer>(cardUniqueName);
		var vbox = card.GetNode<VBoxContainer>("Margin/VBox");
		var headerRow = vbox.GetNode<HBoxContainer>("HeaderRow");

		PanelContainer? instructorPanel = null;
		RichTextLabel? instructorLabel = null;

		if (vbox.HasNode("InstructorPanel"))
		{
			instructorPanel = vbox.GetNode<PanelContainer>("InstructorPanel");
			instructorLabel = instructorPanel.GetNode<RichTextLabel>("InstructorMargin/InstructorLabel");
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
			ActionButton = vbox.GetNode<Button>("ActionButton"),
		};

		binding.ActionButton.Pressed += () => OnStartConstructionPressed(type);
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
			int totalWeeks = FacilityBalance.GetConstructionWeeks(uc.Type, prevLevel);
			int remaining = uc.WeeksRemaining;

			_constructionStatusIcon.Text = "🔨";
			_constructionTitleLabel.Text = $"【改築工事中】{facilityName} Lv{uc.TargetLevel} 改築工事";
			_constructionWeeksLabel.Text = $"残り {remaining} 週";
			_constructionWeeksLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.3f, 1.0f));

			_constructionProgressBar.Visible = true;
			_constructionProgressBar.MinValue = 0;
			_constructionProgressBar.MaxValue = Math.Max(totalWeeks, 1);
			int elapsed = Math.Max(0, totalWeeks - remaining);
			_constructionProgressBar.Value = elapsed;

			_constructionDetailLabel.Text = $"総工期：{totalWeeks}週（進捗：{elapsed}/{totalWeeks}週）。工事中も現在Lvの効果は維持されます。";
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
		binding.LevelBadge.Text = currentLevel == 0 ? "Lv 0（未建設）" : $"Lv {currentLevel}" + (isMaxLevel ? "（最大）" : "");
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

		// 着工ボタンの防御的ガード制御
		if (isMaxLevel)
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

	private void OnStartConstructionPressed(FacilityType type)
	{
		if (_facilitySystem.TryStartConstruction(_state, type))
		{
			StateChanged.Invoke();
		}
	}

	private string GetCurrentEffectText(FacilityType type, int level) => type switch
	{
		FacilityType.Dormitory =>
			$"現役冒険者の保有枠上限：[b]{FacilityBalance.GetDormitoryCapacity(level)}名[/b]",

		FacilityType.Infirmary =>
			$"重傷回復速度：[b]{FacilityBalance.GetInfirmaryInjuryRecoverySpeed(level)}週/週[/b]　HP自然回復：[b]×{FacilityBalance.GetInfirmaryHpRecoveryMultiplier(level):F2}[/b]",

		FacilityType.Tavern =>
			$"週次満足度自然回復：[b]+{FacilityBalance.GetTavernSatisfactionRecovery(level)} pt/週[/b]",

		FacilityType.WarriorHall =>
			level == 0
				? "[color=gray]未稼働（未建設・訓練枠 0名）[/color]"
				: $"訓練枠：[b]{FacilityBalance.GetTrainingSlotCapacity(level)}名[/b]（対象能力：[color=cyan]STR・VIT[/color]）",

		FacilityType.Church =>
			level == 0
				? "[color=gray]未稼働（未建設・訓練枠 0名）[/color]"
				: $"訓練枠：[b]{FacilityBalance.GetTrainingSlotCapacity(level)}名[/b]（対象能力：[color=cyan]MND[/color]）",

		FacilityType.MageLab =>
			level == 0
				? "[color=gray]未稼働（未建設・訓練枠 0名）[/color]"
				: $"訓練枠：[b]{FacilityBalance.GetTrainingSlotCapacity(level)}名[/b]（対象能力：[color=cyan]INT[/color]）",

		FacilityType.ScoutPost =>
			level == 0
				? "[color=gray]未稼働（未建設・訓練枠 0名）[/color]"
				: $"訓練枠：[b]{FacilityBalance.GetTrainingSlotCapacity(level)}名[/b]（対象能力：[color=cyan]AGI・DEX[/color]）",

		FacilityType.WarRoom =>
			level == 0
				? "[color=gray]未稼働（未建設・参謀配置不可）[/color]"
				: "参謀本部（迷宮調査の解析支援・道中潜行の走破支援）",

		FacilityType.RecruitmentOffice =>
			level == 0
				? "[color=gray]未稼働（未建設・スカウト配置不可）[/color]"
				: "新人スカウト本部（新春採用試験の有望新人応募率向上）",

		_ => $"施設効果（Lv {level}）"
	};

	private string GetNextEffectText(FacilityType type, int level)
	{
		if (level >= FacilityBalance.MaxLevel)
			return "[color=gray]最大Lv到達済み（改築上限）[/color]";

		return type switch
		{
			FacilityType.Dormitory =>
				$"次Lv：現役枠上限 [color=cyan]+4名[/color]（計 {FacilityBalance.GetDormitoryCapacity(level + 1)}名）",

			FacilityType.Infirmary =>
				"次Lv：重傷回復 [color=cyan]+1週/週[/color]、HP自然回復 [color=cyan]+0.25倍[/color]",

			FacilityType.Tavern =>
				$"次Lv：満足度回復 [color=cyan]+1 pt/週[/color]（計 +{FacilityBalance.GetTavernSatisfactionRecovery(level + 1)} pt/週）",

			FacilityType.WarriorHall =>
				level == 0
					? "次Lv：訓練枠 [color=cyan]1名[/color] 開放、教官配置 開放"
					: $"次Lv：訓練枠 [color=cyan]+1名[/color]（計 {level + 1}名）",

			FacilityType.Church =>
				level == 0
					? "次Lv：訓練枠 [color=cyan]1名[/color] 開放、教官配置 開放"
					: $"次Lv：訓練枠 [color=cyan]+1名[/color]（計 {level + 1}名）",

			FacilityType.MageLab =>
				level == 0
					? "次Lv：訓練枠 [color=cyan]1名[/color] 開放、教官配置 開放"
					: $"次Lv：訓練枠 [color=cyan]+1名[/color]（計 {level + 1}名）",

			FacilityType.ScoutPost =>
				level == 0
					? "次Lv：訓練枠 [color=cyan]1名[/color] 開放、教官配置 開放"
					: $"次Lv：訓練枠 [color=cyan]+1名[/color]（計 {level + 1}名）",

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
		// 4大専門訓練所
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
					var transmittable = TrainingSystem.GetTransmittableTraitIds(trainer)
						.Select(id => TraitCatalog.FindById(id)?.DisplayName ?? id)
						.ToList();
					string traitLine = transmittable.Count > 0
						? $"伝授可能: [color=cyan]{string.Join(", ", transmittable)}[/color]"
						: "[color=gray]伝授可能特性なし[/color]";
					return $"[color=gold]🎖️ 教官：{trainer.Name}（元{trainer.JobClass}）[/color]\n" +
					       $"伝授：[color=lime]{statsStr}成長率 +{bonus * 100:F1}%[/color]\n" +
					       traitLine;
				}
			}

			return "[color=gray]教官：未任命（上の「👔 顧問を任命」から任命できる）[/color]";
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
					return $"[color=cyan]🖋 参謀：{advisor.Name}（元{advisor.JobClass}）[/color]\n" +
					       $"支援：[color=lime]解析 +{intelBonus * 100:F0}% ／ 走破 +{travelBonus:F1}pt[/color]";
				}
			}

			return "[color=gray]参謀：未任命（上の「👔 顧問を任命」から任命できる）[/color]";
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
					return $"[color=cyan]🧭 スカウト：{scout.Name}（元{scout.JobClass}）[/color]\n" +
					       $"補正：[color=lime]有望新人応募率 +{bonus * 100:F1}%[/color]";
				}
			}

			return "[color=gray]スカウト：未任命（上の「👔 顧問を任命」から任命できる）[/color]";
		}

		return "";
	}

	public static string FacilityTitle(FacilityType type) => type switch
	{
		FacilityType.Dormitory => "🛏️ 宿舎",
		FacilityType.Infirmary => "💉 医務室",
		FacilityType.Tavern => "🍺 ギルド酒場",
		FacilityType.WarriorHall => "⚔️ 戦士訓練所",
		FacilityType.Church => "⛪ 教会",
		FacilityType.MageLab => "🔮 魔法研究所",
		FacilityType.ScoutPost => "🏹 斥候所",
		FacilityType.WarRoom => "📜 作戦資料室",
		FacilityType.RecruitmentOffice => "🤝 冒険者支援室",
		_ => type.ToString()
	};

	public static string FacilityLabel(FacilityType type) => type switch
	{
		FacilityType.Dormitory => "宿舎",
		FacilityType.Infirmary => "医務室",
		FacilityType.Tavern => "ギルド酒場",
		FacilityType.WarriorHall => "戦士訓練所",
		FacilityType.Church => "教会",
		FacilityType.MageLab => "魔法研究所",
		FacilityType.ScoutPost => "斥候所",
		FacilityType.WarRoom => "作戦資料室",
		FacilityType.RecruitmentOffice => "冒険者支援室",
		_ => type.ToString()
	};
}
