#nullable enable
using Godot;
using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 中央ペイン「冒険者・人事」タブのサブタブ「冒険者」パネル（→ 2026年9月高視認性UI刷新）。
///
/// 左ペイン：冒険者一覧リスト（職業・配置・氏名・年齢・状態・週給を整列表示）
/// 右ペイン：個人ステータス詳細（基本情報、7大能力値プログレスバー、8年稼働タイムライン、
///           特性スロット最大5枠、装備欄、操作ボタン群）
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

	// ---- 左ペイン：冒険者一覧 ----
	private ItemList _adventurerList = null!;

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
	private ProgressBar _barSTR = null!;
	private Label _valSTR = null!;
	private ProgressBar _barVIT = null!;
	private Label _valVIT = null!;
	private ProgressBar _barAGI = null!;
	private Label _valAGI = null!;
	private ProgressBar _barDEX = null!;
	private Label _valDEX = null!;
	private ProgressBar _barINT = null!;
	private Label _valINT = null!;
	private ProgressBar _barMND = null!;
	private Label _valMND = null!;
	private ProgressBar _barLDR = null!;
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
	private Button _advisorButton = null!;

	/// <summary>ステータス詳細パネルに表示中の冒険者Id。週送り後もこの人物の表示を維持する。</summary>
	private Guid? _detailAdventurerId;

	/// <summary>週報ログ（右ペイン）への追記を依頼する（BBCode文字列）。</summary>
	public event Action<string> LogRequested = delegate { };

	/// <summary>ゲーム状態が変わったことを通知する（MainDashboardが全体を再描画する）。</summary>
	public event Action StateChanged = delegate { };

	/// <summary>装備ポップアップの開放を依頼する（MainDashboardがポップアップを所有するため）。</summary>
	public event Action<Adventurer> EquipmentRequested = delegate { };

	/// <summary>顧問管理ポップアップの開放を依頼する（MainDashboardがポップアップを所有するため）。</summary>
	public event Action AdvisorRequested = delegate { };

	/// <summary>顧問管理ボタンへの参照。</summary>
	public Button AdvisorButton => _advisorButton;

	public override void _Ready()
	{
		// 左ペイン
		_adventurerList = GetNode<ItemList>("%AdventurerList");

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
		_barSTR = GetNode<ProgressBar>("%Bar_STR");
		_valSTR = GetNode<Label>("%Val_STR");
		_barVIT = GetNode<ProgressBar>("%Bar_VIT");
		_valVIT = GetNode<Label>("%Val_VIT");
		_barAGI = GetNode<ProgressBar>("%Bar_AGI");
		_valAGI = GetNode<Label>("%Val_AGI");
		_barDEX = GetNode<ProgressBar>("%Bar_DEX");
		_valDEX = GetNode<Label>("%Val_DEX");
		_barINT = GetNode<ProgressBar>("%Bar_INT");
		_valINT = GetNode<Label>("%Val_INT");
		_barMND = GetNode<ProgressBar>("%Bar_MND");
		_valMND = GetNode<Label>("%Val_MND");
		_barLDR = GetNode<ProgressBar>("%Bar_LDR");
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
		_advisorButton = GetNode<Button>("%AdvisorButton");

		// シグナル配線
		_adventurerList.ItemClicked += OnAdventurerItemClicked;
		_equipmentButton.Pressed += OnEquipmentButtonPressed;
		_raiseWageButton.Pressed += OnRaiseWagePressed;
		_payBonusButton.Pressed += OnPayBonusPressed;
		_retireButton.Pressed += OnRetirePressed;
		_advisorButton.Pressed += () => AdvisorRequested.Invoke();

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
		RefreshAdventurerList();
		RefreshAdventurerDetail();
	}

	// ==== 左ペイン：冒険者一覧 ====

	/// <summary>
	/// 冒険者一覧を更新する。各行に職業・配置区分・氏名・年齢・状態・週給を整列表示する。
	/// 未選択状態の厳守：自動で先頭を選択せず、以前選択していた対象が存在する場合のみ選択を維持する。
	/// </summary>
	private void RefreshAdventurerList()
	{
		_adventurerList.Clear();
		int selectedIndex = -1;

		for (int i = 0; i < _state.Adventurers.Count; i++)
		{
			var a = _state.Adventurers[i];
			string placement = PlacementRules.GetDefault(a.JobClass) == Placement.Front ? "前衛" : "後衛";
			string jobName = JobLabel(a.JobClass);
			string status = a.Injury == InjurySeverity.Severe
				? "【重傷】"
				: a.Injury == InjurySeverity.Light
					? "【軽傷】"
					: a.IsDispatched
						? "【出撃中】"
						: "【待機】";

			_adventurerList.AddItem($"[{placement}] {jobName}　{a.Name} ({a.Age}歳)　{status}　{a.WeeklyWage}G/週");

			if (_detailAdventurerId.HasValue && a.Id == _detailAdventurerId.Value)
				selectedIndex = i;
		}

		if (selectedIndex >= 0)
		{
			_adventurerList.Select(selectedIndex);
		}
		else
		{
			_adventurerList.DeselectAll();
		}
	}

	/// <summary>
	/// 冒険者一覧のクリックでステータス詳細パネルを更新する。
	/// </summary>
	private void OnAdventurerItemClicked(long index, Vector2 atPosition, long mouseButtonIndex)
	{
		if (index >= 0 && index < _state.Adventurers.Count)
			ShowAdventurerDetail(_state.Adventurers[(int)index]);
	}

	// ==== 右ペイン：詳細表示 ====

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
		_adventurerList.DeselectAll();
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
		BindStatRow(_barSTR, _valSTR, a.STR, a.PA_STR);
		BindStatRow(_barVIT, _valVIT, a.VIT, a.PA_VIT);
		BindStatRow(_barAGI, _valAGI, a.AGI, a.PA_AGI);
		BindStatRow(_barDEX, _valDEX, a.DEX, a.PA_DEX);
		BindStatRow(_barINT, _valINT, a.INT, a.PA_INT);
		BindStatRow(_barMND, _valMND, a.MND, a.PA_MND);
		BindStatRow(_barLDR, _valLDR, a.LDR, a.PA_LDR);

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
				_traitSlotLabels[i].Text = $"{i + 1}: 【{traitName}】";
				_traitSlotLabels[i].AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.4f)); // 習得済み: 明るい黄色
			}
			else
			{
				_traitSlotLabels[i].Text = $"{i + 1}: （空きスロット）";
				_traitSlotLabels[i].AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f)); // 空き: グレー
			}
		}

		// ---- 装備スロット（→ 03 §4.2.2。2026年9月改訂：個体（EquipmentItem）を表示する） ----
		_weaponLabel.Text = $"武器: {EquipmentSlotText(a.EquippedWeapon)}";
		_armorLabel.Text = $"防具: {EquipmentSlotText(a.EquippedArmor)}";
		_accessory1Label.Text = $"装飾1: {EquipmentSlotText(a.EquippedAccessory1)}";
		_accessory2Label.Text = $"装飾2: {EquipmentSlotText(a.EquippedAccessory2)}";

		// ---- 操作ボタン群の個別ガード ----
		_raiseWageButton.Disabled = false;
		_payBonusButton.Disabled = false;

		// 出撃中は装備変更・引退をガード
		_equipmentButton.Disabled = a.IsDispatched;
		_retireButton.Disabled = a.IsDispatched;
	}

	/// <summary>7大能力値の各行へ実効値と潜在上限PAを反映する。</summary>
	private static void BindStatRow(ProgressBar bar, Label valLabel, int value, int pa)
	{
		valLabel.Text = $"{value} / {pa}";
		bar.MinValue = 0;
		bar.MaxValue = pa > 0 ? pa : 100;
		bar.Value = Math.Clamp(value, 0, (int)bar.MaxValue);
	}

	// ==== 操作ボタン群 ====

	/// <summary>全操作ボタンの有効/無効を一括切り替え。</summary>
	private void SetActionButtonsDisabled(bool disabled)
	{
		_equipmentButton.Disabled = disabled;
		_raiseWageButton.Disabled = disabled;
		_payBonusButton.Disabled = disabled;
		_retireButton.Disabled = disabled;
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

		_agingSystem.RetireVoluntarily(_state, target);
		LogRequested($"[color=cyan]{target.Name} が引退し、顧問候補になった。[/color]");
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
	/// 装備スロット表示用のラベル（→ 03 §4.2.2）。未装備ならその旨を、装備中なら名称と
	/// 効果量（個人CP／最大HPへの加算）を表示する。カタログ定義が引けない旧データは「（不明）」。
	/// </summary>
	private static string EquipmentSlotText(EquipmentItem? equipped)
	{
		if (equipped == null) return "なし";

		var definition = equipped.GetDefinition();
		if (definition == null) return $"{equipped.Name}（不明）";

		string effect = definition.EffectType == EquipmentEffectType.PersonalCpBonus
			? $"個人CP+{definition.EffectValue}"
			: $"最大HP+{definition.EffectValue}";
		return $"{definition.Name}（{effect}）";
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
