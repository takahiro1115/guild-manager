#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 中央ペイン「大迷宮（Dungeon）」タブ（→ ダンジョン攻略システム）。
///
/// 3ゾーン構造（Zone A: 探索状況, Zone B: 階層ボス・ポーチ, Zone C: 出撃）により、
/// 潜行・調査・採取の3大任務および扉前決断（討伐・撤退）の全動線を視覚的かつ堅牢に集約する。
/// （画面上の見出しは「探索状況」「階層ボス」「出撃」。Zone A〜C はコード上の呼び名。2026年9月・§0.43）
///
/// 2026年9月・§0.43（大迷宮UIの改善）：
///  - Zone A の探索状況を 1F〜100F の縦バー（→ DungeonDepthBar）で描く。区間クリックでそのボスを Zone B に「閲覧」表示できる。
///  - Zone B の特殊能力は、判明したものだけをカードにする（解析率50%未満は「？？？」の1枚）。判明した対策アイテムはポーチの選択肢で ★ 表示。
///  - Zone C の見立ては任務ごとの3枚のカード（値／要求と ✔／✖）に分け、出撃ボタンの真上に並べる。
///  - 文字の大きさはテーマ（themes/dungeon_theme.tres）で本文13に揃え、補足12・見出し15の3段階だけにする。
///
/// 週報ログへの書き込み・画面全体の再描画は MainDashboard の責務のため、
/// LogRequested・StateChanged イベントで依頼する。
/// </summary>
public partial class DungeonPanel : ScrollContainer
{
	/// <summary>文字の大きさの3段階（本文はテーマの既定13。§0.43）。</summary>
	private const int FontNote = 12;
	private const int FontBody = 13;

	// ==================== Zone A: 探索状況 ====================
	private OptionButton _fieldSelector = null!;
	private DungeonDepthBar _depthBar = null!;
	private RichTextLabel _depthLegend = null!;
	private RichTextLabel _floorDisplay = null!;
	private PanelContainer _expeditionLootContainer = null!;
	private RichTextLabel _expeditionLootLabel = null!;
	private RichTextLabel _materialsLabel = null!;

	// ==================== Zone B: ボス警戒・ポーチ ====================
	private PanelContainer _bossProfileCard = null!;
	private RichTextLabel _bossHeaderLabel = null!;
	private ProgressBar _bossHpBar = null!;
	private Label _bossHpLabel = null!;
	private ProgressBar _intelProgressBar = null!;
	private Label _intelPercentLabel = null!;
	private RichTextLabel _intelTierLabel = null!;
	private RichTextLabel _completeBadgeLabel = null!;
	private Control _bossHpRow = null!;
	private Button _backToTargetButton = null!;
	private GridContainer _gimmickContainer = null!;
	private OptionButton _pouchSlot1 = null!;
	private OptionButton _pouchSlot2 = null!;
	private RichTextLabel _pouchCostLabel = null!;

	// ==================== Zone C: 部隊状況・コマンド ====================
	private HBoxContainer _squadButtons = null!;
	private readonly ButtonGroup _squadButtonGroup = new() { AllowUnpress = true };
	private HBoxContainer _squadMemberContainer = null!;
	private RichTextLabel _traversalPreviewLabel = null!;
	private RichTextLabel _surveyPreviewLabel = null!;
	private RichTextLabel _gatheringPreviewLabel = null!;
	private PanelContainer _traversalCard = null!;
	private PanelContainer _surveyCard = null!;
	private PanelContainer _gatheringCard = null!;
	private RichTextLabel _traversalReasonLabel = null!;
	private RichTextLabel _surveyReasonLabel = null!;
	private RichTextLabel _gatheringReasonLabel = null!;
	private RichTextLabel _dispatchStatusLabel = null!;

	private HBoxContainer _commandAreaStandby = null!;
	private Button _surveyButton = null!;
	private Button _scoutingButton = null!;
	private Button _gatheringButton = null!;

	private VBoxContainer _commandAreaAdvancing = null!;
	private RichTextLabel _advancingStatusLabel = null!;
	private Button _emergencyRetreatButton = null!;

	private VBoxContainer _commandAreaBossDecision = null!;
	private RichTextLabel _bossDecisionLabel = null!;
	private Button _engageBossButton = null!;
	private Button _retreatButton = null!;

	private Button _cancelMissionButton = null!;
	private ItemList _pendingMissionList = null!;

	// ==================== 状態変数 ====================
	private GameState _state = null!;
	private DungeonExpeditionSystem _expeditionSystem = null!;

	/// <summary>
	/// 選択中の出撃部隊（SavedParty.Id）。週送り・再描画で部隊ボタンを作り直しても
	/// 選択が外れないよう、インデックスではなくIdで記憶。
	/// </summary>
	private Guid? _selectedPartyId;

	/// <summary>選択中のダンジョン（フィールド）のId。</summary>
	private string? _selectedFieldId;

	/// <summary>選択中のフィールドの実体キャッシュ。</summary>
	private DungeonField? _selectedField;

	/// <summary>待機中メンバーを1人以上含む出撃部隊が存在するか。</summary>
	private bool _hasSelectableParty;

	/// <summary>
	/// 縦バーのクリックで Zone B に表示しているボス（null＝現在の目標＝次の未撃破ボスを表示。§0.43）。
	/// 表示だけの切り替えで、出撃・ポーチの対象は常に現在の目標。フィールドを変えると解除する。
	/// </summary>
	private Guid? _viewedBossId;

	/// <summary>週報ログ（右ペイン）への追記を依頼する（BBCode文字列）。</summary>
	public event Action<string> LogRequested = delegate { };

	/// <summary>出撃・取り消し等でゲーム状態が変わったことを通知する。</summary>
	public event Action StateChanged = delegate { };

	public override void _Ready()
	{
		// Zone A
		_fieldSelector = GetNode<OptionButton>("%FieldSelector");
		_depthBar = GetNode<DungeonDepthBar>("%DepthBar");
		_depthLegend = GetNode<RichTextLabel>("%DepthLegend");
		_floorDisplay = GetNode<RichTextLabel>("%FloorDisplay");
		_expeditionLootContainer = GetNode<PanelContainer>("%ExpeditionLootContainer");
		_expeditionLootLabel = GetNode<RichTextLabel>("%ExpeditionLootLabel");
		_materialsLabel = GetNode<RichTextLabel>("%MaterialsLabel");

		// Zone B
		_bossProfileCard = GetNode<PanelContainer>("%BossProfileCard");
		_bossHeaderLabel = GetNode<RichTextLabel>("%BossHeaderLabel");
		_bossHpBar = GetNode<ProgressBar>("%BossHpBar");
		_bossHpLabel = GetNode<Label>("%BossHpLabel");
		_intelProgressBar = GetNode<ProgressBar>("%IntelProgressBar");
		_intelPercentLabel = GetNode<Label>("%IntelPercentLabel");
		_intelTierLabel = GetNode<RichTextLabel>("%IntelTierLabel");
		_completeBadgeLabel = GetNode<RichTextLabel>("%CompleteBadgeLabel");
		_gimmickContainer = GetNode<GridContainer>("%GimmickContainer");
		_pouchSlot1 = GetNode<OptionButton>("%PouchSlot1");
		_pouchSlot2 = GetNode<OptionButton>("%PouchSlot2");
		_pouchCostLabel = GetNode<RichTextLabel>("%PouchCostLabel");

		// Zone C
		_squadButtons = GetNode<HBoxContainer>("%SquadButtons");
		_squadMemberContainer = GetNode<HBoxContainer>("%SquadMemberContainer");
		_traversalPreviewLabel = GetNode<RichTextLabel>("%TraversalPreviewLabel");
		_surveyPreviewLabel = GetNode<RichTextLabel>("%SurveyPreviewLabel");
		_gatheringPreviewLabel = GetNode<RichTextLabel>("%GatheringPreviewLabel");
		_traversalCard = GetNode<PanelContainer>("%TraversalPreviewCard");
		_surveyCard = GetNode<PanelContainer>("%SurveyPreviewCard");
		_gatheringCard = GetNode<PanelContainer>("%GatheringPreviewCard");
		_traversalReasonLabel = GetNode<RichTextLabel>("%TraversalReasonLabel");
		_surveyReasonLabel = GetNode<RichTextLabel>("%SurveyReasonLabel");
		_gatheringReasonLabel = GetNode<RichTextLabel>("%GatheringReasonLabel");
		_dispatchStatusLabel = GetNode<RichTextLabel>("%DispatchStatusLabel");

		_commandAreaStandby = GetNode<HBoxContainer>("%CommandAreaStandby");
		_scoutingButton = GetNode<Button>("%ScoutingButton");
		_surveyButton = GetNode<Button>("%SurveyButton");
		_gatheringButton = GetNode<Button>("%GatheringButton");

		_commandAreaAdvancing = GetNode<VBoxContainer>("%CommandAreaAdvancing");
		_advancingStatusLabel = GetNode<RichTextLabel>("%AdvancingStatusLabel");
		_emergencyRetreatButton = GetNode<Button>("%EmergencyRetreatButton");

		_commandAreaBossDecision = GetNode<VBoxContainer>("%CommandAreaBossDecision");
		_bossDecisionLabel = GetNode<RichTextLabel>("%BossDecisionLabel");
		_engageBossButton = GetNode<Button>("%EngageBossButton");
		_retreatButton = GetNode<Button>("%RetreatButton");

		_cancelMissionButton = GetNode<Button>("%CancelMissionButton");
		_pendingMissionList = GetNode<ItemList>("%PendingMissionList");

		_depthBar.BossClicked += OnDepthBarBossClicked;
		_depthLegend.AppendText(
			"[color=#0b0b0e]■[/color]未踏破　[color=#e5e7eb]■[/color]踏破済み　" +
			"[color=#60a5fa]■[/color]解析済み（濃いほど解析が進む／[color=#2d52a3]■[/color]は未踏破の区間）\n" +
			"[color=#4ade80]✔[/color]撃破済み　[color=#fb923c]⚔[/color]現在の目標　[color=#22d3ee]▶[/color]到達階層　[color=#f472b6]●[/color]潜行中の部隊");

		_bossHpRow = _bossHpBar.GetParent<Control>();
		_backToTargetButton = new Button { Text = "⚔ 現在の目標のボスを表示", Visible = false, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
		_backToTargetButton.Pressed += () =>
		{
			_viewedBossId = null;
			var target = _selectedField?.GetNextActiveBoss();
			RefreshProgress(target);
			RefreshBossInfo(target, target);
		};
		var bossVBox = _bossHeaderLabel.GetParent();
		bossVBox.AddChild(_backToTargetButton);
		bossVBox.MoveChild(_backToTargetButton, _bossHeaderLabel.GetIndex() + 1);

		_bossHpBar.MinValue = 0;
		_bossHpBar.MaxValue = 100;

		_intelProgressBar.MinValue = 0;
		_intelProgressBar.MaxValue = 100;

		// ボタンテーマ色
		_engageBossButton.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
		_retreatButton.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.5f));
		_emergencyRetreatButton.AddThemeColorOverride("font_color", new Color(1f, 0.8f, 0.3f));

		// イベント接続
		_fieldSelector.ItemSelected += OnFieldSelected;
		_scoutingButton.Pressed += OnDispatchPressed;
		_surveyButton.Pressed += OnSurveyDispatchPressed;
		_gatheringButton.Pressed += OnGatheringDispatchPressed;
		_emergencyRetreatButton.Pressed += OnRetreatPressed;
		_engageBossButton.Pressed += OnEngageBossPressed;
		_retreatButton.Pressed += OnRetreatPressed;
		_pendingMissionList.ItemSelected += _ => OnPendingMissionSelected();
		_cancelMissionButton.Pressed += OnCancelMissionPressed;

		VisibilityChanged += () =>
		{
			if (IsVisibleInTree() && _state != null)
				Refresh(_state);
		};

		PopulatePouchSlot(_pouchSlot1);
		PopulatePouchSlot(_pouchSlot2);
		_pouchSlot1.ItemSelected += OnPouchSlot1Selected;
		_pouchSlot2.ItemSelected += OnPouchSlot2Selected;
	}

	/// <summary>出撃の派遣・取り消しに使うシステムを受け取る（MainDashboard._Readyから1回呼ぶ）。</summary>
	public void Initialize(DungeonExpeditionSystem expeditionSystem)
	{
		_expeditionSystem = expeditionSystem;
	}

	/// <summary>最新のゲーム状態でタブ全体を再描画する（MainDashboard.RefreshAllから毎回呼ぶ）。</summary>
	public void Refresh(GameState state)
	{
		_state = state;

		RefreshFieldOptions();
		var boss = _selectedField?.GetNextActiveBoss();
		RefreshProgress(boss);
		RefreshExpeditionLoot();
		RefreshMaterialsInfo();
		RefreshBossInfo(DisplayedBoss(boss), boss);
		RefreshPouchCost();
		RefreshPartyOptions();
		RefreshDispatchSection(boss);
		RefreshPendingMissions();
		UpdateCommandAreaVisibility();
	}

	// ==================== Zone A: ダンジョン選択・進軍モニター ====================

	private void RefreshFieldOptions()
	{
		var fields = _state.DungeonFields.OrderBy(f => f.Order).ToList();
		_fieldSelector.Clear();

		if (fields.Count == 0)
		{
			_selectedField = null;
			_selectedFieldId = null;
			return;
		}

		string defaultFieldId = _state.GetActiveField()?.Id
			?? fields.LastOrDefault(f => f.IsUnlocked)?.Id
			?? fields[0].Id;

		int selectIndex = 0;
		for (int i = 0; i < fields.Count; i++)
		{
			var field = fields[i];
			_fieldSelector.AddItem(field.IsUnlocked
				? $"{field.Name}（到達: {field.ReachedFloor}/{DungeonField.MaxFloor}F）"
				: "？？？（未開放）");
			_fieldSelector.SetItemDisabled(i, !field.IsUnlocked);

			bool isKeptSelection = _selectedFieldId != null && field.Id == _selectedFieldId && field.IsUnlocked;
			bool isDefaultSelection = _selectedFieldId == null && field.Id == defaultFieldId;
			if (isKeptSelection || isDefaultSelection)
				selectIndex = i;
		}

		_fieldSelector.Select(selectIndex);
		_selectedField = fields[selectIndex];
		_selectedFieldId = _selectedField.Id;
	}

	private void OnFieldSelected(long index)
	{
		var fields = _state.DungeonFields.OrderBy(f => f.Order).ToList();
		if (index < 0 || index >= fields.Count || !fields[(int)index].IsUnlocked)
			return;

		_selectedField = fields[(int)index];
		_selectedFieldId = _selectedField.Id;
		_viewedBossId = null;

		var boss = _selectedField.GetNextActiveBoss();
		RefreshProgress(boss);
		RefreshExpeditionLoot();
		RefreshMaterialsInfo();
		RefreshBossInfo(boss, boss);
		RefreshDispatchSection(boss);
	}

	/// <summary>Zone B に表示するボス：縦バーで閲覧中のボス（このフィールドに居れば）、無ければ現在の目標。</summary>
	private FloorBoss? DisplayedBoss(FloorBoss? target) =>
		_viewedBossId.HasValue ? _selectedField?.Bosses.FirstOrDefault(b => b.Id == _viewedBossId.Value) ?? target : target;

	/// <summary>縦バーの区間クリック：そのボスを Zone B に表示する（目標のボスなら閲覧を解く）。</summary>
	private void OnDepthBarBossClicked(Guid bossId)
	{
		var target = _selectedField?.GetNextActiveBoss();
		_viewedBossId = target != null && target.Id == bossId ? null : bossId;
		RefreshProgress(target);
		RefreshBossInfo(DisplayedBoss(target), target);
	}

	/// <summary>
	/// 探索状況：上に短い要約（到達・撃破数・現在の目標・出発区間の進軍速度・潜行中の部隊の位置）、下に 1F〜100F の縦バー（→ DungeonDepthBar）。
	/// §0.43：旧来の横バーと文章の区間一覧（制覇区間 10F✔ …）は縦バーへ移し、進軍速度の式はツールチップへ移した。
	/// </summary>
	private void RefreshProgress(FloorBoss? boss)
	{
		_floorDisplay.Clear();
		if (_selectedField == null)
		{
			_depthBar.SetData(null, null, null, Array.Empty<(int, string)>());
			_floorDisplay.AppendText("[color=gray]ダンジョン情報がありません。[/color]");
			return;
		}

		var allBosses = _state.DungeonFields.SelectMany(f => f.Bosses).ToList();
		int totalCleared = allBosses.Count(b => b.IsDefeated);
		int fieldCleared = _selectedField.Bosses.Count(b => b.IsDefeated);

		// この迷宮を潜行中の部隊（縦バーのピンと要約の1行）
		var missions = _state.ActiveDungeonMissions
			.Where(m => m.Field.Id == _selectedField.Id && m.MissionType == DungeonMissionType.Scouting)
			.ToList();
		var pins = missions.Select(m => (m.CurrentFloor, $"{MissionPartyName(m)} {MissionStatusPlain(m)}")).ToList();
		_depthBar.SetData(_selectedField, boss?.Id, _viewedBossId, pins);

		var sb = new StringBuilder();
		sb.AppendLine($"最高到達 [color=cyan][b]{_selectedField.ReachedFloor}F[/b][/color]／{DungeonField.MaxFloor}F　" +
			$"撃破 {fieldCleared}/{_selectedField.Bosses.Count}体　[color=gray]（大迷宮全体 {totalCleared}/{allBosses.Count}体）[/color]");
		sb.AppendLine(boss == null
			? $"[color=gold][b]🏆 {_selectedField.Name} 完全踏破！[/b][/color]"
			: $"現在の目標：[color=#fb923c]⚔ 第{boss.Floor}層「{boss.Name}」[/color]");

		// 進軍速度：潜行中は部隊の現在階層、待機中は出発階層（毎回1Fから潜る）の区間担当ボスで求める。
		var activeMission = missions.FirstOrDefault();
		int speedFloor = activeMission?.CurrentFloor ?? 1;
		var segmentBoss = _selectedField.GetSegmentBoss(speedFloor);
		double speed = DungeonTraversalResolver.IntelSpeedMultiplier(segmentBoss);
		sb.Append($"⚡ {(activeMission != null ? "現在地" : "出発")}区間の進軍速度 [color=lime][b]×{speed:0.0#}[/b][/color]");
		foreach (var mission in missions)
			sb.Append($"\n[color=#f472b6]●[/color] {MissionPartyName(mission)}：{MissionStatusText(mission)} 第{mission.CurrentFloor}層");

		_floorDisplay.AppendText(sb.ToString());
		_floorDisplay.TooltipText = segmentBoss == null
			? "区間担当ボスなし"
			: $"進軍速度＝1.0＋区間担当ボスの解析率×{DungeonTraversalBalance.IntelSpeedBonusPerIntel:0.#}\n" +
			  $"{speedFloor}F→{segmentBoss.Floor}F区間の主「{segmentBoss.Name}」解析{segmentBoss.IntelRate * 100:F0}% → ×{speed:0.0#}\n" +
			  "縦バーで青い（解析済みの）区間ほど速く進める。";
	}

	private static string MissionPartyName(ActiveDungeonMission mission) =>
		mission.Party.Members.Count > 0 ? $"{mission.Party.Members[0].Name}隊" : "部隊";

	private static string MissionStatusText(ActiveDungeonMission mission) => mission.Status switch
	{
		ExpeditionStatus.AwaitingBossDecision => "[color=gold]扉前待機[/color]",
		ExpeditionStatus.EngagingBoss => "[color=red]討伐突入[/color]",
		ExpeditionStatus.Retreating => "[color=gray]撤退中[/color]",
		_ => "[color=cyan]進軍中[/color]",
	};

	private static string MissionStatusPlain(ActiveDungeonMission mission) => mission.Status switch
	{
		ExpeditionStatus.AwaitingBossDecision => "扉前待機",
		ExpeditionStatus.EngagingBoss => "討伐突入",
		ExpeditionStatus.Retreating => "撤退中",
		_ => "進軍中",
	};

	private void RefreshExpeditionLoot()
	{
		_expeditionLootLabel.Clear();
		if (_selectedField == null)
		{
			_expeditionLootLabel.AppendText("[color=gray]ダンジョンが選択されていません。[/color]");
			return;
		}

		var fieldMissions = _state.ActiveDungeonMissions
			.Where(m => m.Field.Id == _selectedField.Id && m.MissionType == DungeonMissionType.Scouting)
			.ToList();

		if (fieldMissions.Count == 0)
		{
			_expeditionLootLabel.AppendText("[color=gray]待機中（この迷宮を潜行中の部隊はありません）[/color]");
			return;
		}

		int totalGold = fieldMissions.Sum(m => m.CarriedGold);
		var allMats = fieldMissions.SelectMany(m => m.CarriedMaterials)
			.GroupBy(kv => kv.Key)
			.ToDictionary(g => g.Key, g => g.Sum(x => x.Value));

		var sb = new StringBuilder();
		sb.AppendLine($"💰 拾得ゴールド：[color=gold][b]{totalGold}G[/b][/color]");
		if (allMats.Count > 0)
		{
			string matSummary = string.Join("、", allMats.Select(kv => $"{MaterialBalance.GetName(kv.Key)}×{kv.Value}"));
			sb.Append($"📦 拾得素材：{matSummary}");
		}
		else
		{
			sb.Append("[color=gray]📦 拾得素材：なし[/color]");
		}

		_expeditionLootLabel.AppendText(sb.ToString());
	}

	private void RefreshMaterialsInfo()
	{
		_materialsLabel.Clear();
		if (_selectedField == null)
			return;

		var materials = MaterialBalance.GetFieldMaterials(_selectedField.Id);
		if (materials.Count == 0)
		{
			_materialsLabel.AppendText("[color=gray]このフィールドで採れる素材の情報はまだ無い。[/color]");
			return;
		}

		string parts = string.Join("、", materials.Select(m =>
		{
			int stock = _state.Materials.TryGetValue(m.Id, out int count) ? count : 0;
			return $"{m.Name}（在庫{stock}）";
		}));

		_materialsLabel.AppendText($"[b]獲得可能な素材[/b]：{parts}");
	}

	// ==================== Zone B: ボス警戒・ポーチ ====================

	// カタログは大迷宮のボスギミック対策4種のみで構成される（→ 03 §0.13）ため、
	// ポーチの選択肢はカタログ全件をそのまま使う。対策対象のギミックも
	// ConsumableItem.TargetGimmick から引けるので、UI側に対応表は持たない。
	private static readonly string[] PouchItemIds =
		ConsumableCatalog.GetAll().Select(i => i.Id).ToArray();

	private void PopulatePouchSlot(OptionButton slot)
	{
		slot.Clear();
		slot.AddItem("なし (0G)");
		foreach (var item in ConsumableCatalog.GetAll())
			slot.AddItem(PouchItemText(item, effective: false));
	}

	private static string PouchItemText(ConsumableItem item, bool effective) =>
		$"{(effective ? "★有効 " : "")}{item.Name} ({item.Price}G) [{GimmickLabel(item.TargetGimmick)}対策]";

	/// <summary>
	/// 現在の目標のボスに判明している対策アイテム（解析段階「対策情報」以上で、ギミックの RequiredItemId が一致）を
	/// ポーチの選択肢で「★有効」と示す（§0.43）。対策内容は解析率75%で開示される情報のため、それ未満では付けない。
	/// </summary>
	private void RefreshPouchHighlights(FloorBoss? target)
	{
		var effectiveIds = target != null && ScoutingResolver.GetTier(target.IntelRate) >= IntelTier.Countermeasures
			? target.Gimmicks.Select(g => g.RequiredItemId).Where(id => !string.IsNullOrEmpty(id)).ToHashSet()
			: new HashSet<string?>();

		var items = ConsumableCatalog.GetAll().ToList();
		foreach (var slot in new[] { _pouchSlot1, _pouchSlot2 })
		{
			for (int i = 0; i < items.Count && i + 1 < slot.ItemCount; i++)
				slot.SetItemText(i + 1, PouchItemText(items[i], effectiveIds.Contains(items[i].Id)));
		}
	}

	private List<string> GetSelectedPouchItemIds()
	{
		var ids = new List<string>();
		foreach (var slot in new[] { _pouchSlot1, _pouchSlot2 })
		{
			int index = slot.Selected;
			if (index >= 1 && index - 1 < PouchItemIds.Length)
				ids.Add(PouchItemIds[index - 1]);
		}
		return ids;
	}

	private void ResetPouchSelection()
	{
		_pouchSlot1.Select(0);
		_pouchSlot2.Select(0);
		RefreshPouchCost();
	}

	private void OnPouchSlot1Selected(long index)
	{
		if (index > 0 && index == _pouchSlot2.Selected)
		{
			_pouchSlot2.Select(0);
		}
		OnPouchChanged();
	}

	private void OnPouchSlot2Selected(long index)
	{
		if (index > 0 && index == _pouchSlot1.Selected)
		{
			_pouchSlot1.Select(0);
		}
		OnPouchChanged();
	}

	private void OnPouchChanged()
	{
		RefreshPouchCost();
		var boss = _selectedField?.GetNextActiveBoss();
		RefreshBossInfo(DisplayedBoss(boss), boss);
		RefreshDispatchSection(boss);
		RefreshBossDecision();
	}

	private void RefreshPouchCost()
	{
		_pouchCostLabel.Clear();
		var selectedItemIds = GetSelectedPouchItemIds();
		int pouchCost = DungeonExpeditionSystem.CalculateConsumableCost(selectedItemIds);
		if (pouchCost == 0)
		{
			_pouchCostLabel.AppendText("[color=gray]ポーチ費用：0G（アイテム未携行）[/color]");
			return;
		}

		bool insufficient = _state.Gold < pouchCost;
		if (insufficient)
		{
			_pouchCostLabel.AppendText($"[color=red][b]⚠ ポーチ代金不足：必要 {pouchCost}G／所持金 {_state.Gold}G[/b][/color]");
		}
		else
		{
			_pouchCostLabel.AppendText($"ポーチ代金：[color=gold][b]{pouchCost}G[/b][/color]（所持金 {_state.Gold}G）※決戦突入時に支払い");
		}
	}

	/// <summary>
	/// 階層ボス欄。boss＝表示するボス（縦バーで閲覧中のボス、または現在の目標）、target＝現在の目標（出撃・ポーチの対象）。
	/// §0.43：見出しの「出現階層」行（見出しと重複）を削り、HPが未解析（解析率25%未満）の間はバーを隠して「？？？」だけにした。
	/// 閲覧中（目標以外）は見出しに（閲覧中）を付け、「⚔ 現在の目標のボスを表示」ボタンで戻れる。
	/// </summary>
	private void RefreshBossInfo(FloorBoss? boss, FloorBoss? target)
	{
		ClearGimmickCards();
		_bossHeaderLabel.Clear();
		_intelTierLabel.Clear();
		_completeBadgeLabel.Clear();
		RefreshPouchHighlights(target);

		bool viewingOther = boss != null && target != null && boss.Id != target.Id
			|| boss != null && target == null;
		_backToTargetButton.Visible = viewingOther && target != null;

		// 選択部隊＋ポーチでの対策状況プレビュー
		var saved = SelectedSavedParty();
		var previewParty = saved == null ? new Party() : PartyFormationSystem.BuildDispatchParty(_state, saved.MemberIds);
		foreach (var itemId in GetSelectedPouchItemIds())
			previewParty.TryAddConsumable(itemId);

		if (boss == null)
		{
			_bossHeaderLabel.AppendText(_selectedField != null
				? $"[color=gold][b]🏆 このダンジョンは完全踏破されました（{_selectedField.ReachedFloor}/{DungeonField.MaxFloor}F）。[/b][/color]"
				: "[color=gray]挑むべき階層ボスはいない。[/color]");
			_bossHpRow.Visible = false;
			_intelProgressBar.Value = 0;
			_intelPercentLabel.Text = "-";
			_completeBadgeLabel.Visible = false;
			PopulateGimmickCards(null, IntelTier.Unknown, previewParty);
			return;
		}

		var tier = ScoutingResolver.GetTier(boss.IntelRate);

		string state = boss.IsDefeated ? "　[color=#4ade80]✔ 撃破済み[/color]"
			: viewingOther ? "　[color=#fbbf24]（閲覧中）[/color]"
			: "　[color=#fb923c]⚔ 現在の目標[/color]";
		_bossHeaderLabel.AppendText($"[font_size=15][b]第{boss.Floor}層の主「{boss.Name}」[/b][/font_size]{state}");

		_bossHpRow.Visible = true;
		if (tier < IntelTier.Basic)
		{
			_bossHpBar.Visible = false;
			_bossHpLabel.Text = "？？？（解析率25%で判明）";
		}
		else
		{
			_bossHpBar.Visible = true;
			_bossHpBar.MinValue = 0;
			_bossHpBar.MaxValue = Math.Max(1, boss.MaxHp);
			_bossHpBar.Value = Math.Clamp(boss.CurrentHp, 0, boss.MaxHp);
			double hpPct = (double)boss.CurrentHp / Math.Max(1, boss.MaxHp) * 100.0;
			_bossHpLabel.Text = $"{boss.CurrentHp} / {boss.MaxHp} ({hpPct:F0}%)";
		}

		_intelProgressBar.Value = Math.Round(boss.IntelRate * 100);
		_intelPercentLabel.Text = $"{boss.IntelRate * 100:F0}%";

		_intelTierLabel.AppendText($"解析段階：[color={TierColor(tier)}][b]{TierLabel(tier)}[/b][/color]");
		string? nextHint = NextTierHint(tier);
		if (nextHint != null)
			_intelTierLabel.AppendText($"　[color=gray]（{nextHint}）[/color]");

		_completeBadgeLabel.Visible = tier == IntelTier.Complete;
		if (tier == IntelTier.Complete)
		{
			_completeBadgeLabel.AppendText(
				$"[bgcolor=#5a4500][color=gold][b] ◆ 完全解析済み：弱点看破により討伐時の与ダメージ+{DungeonBalance.FullIntelDamageBonus * 100:F0}% ボーナス中 [/b][/color][/bgcolor]");
		}

		PopulateGimmickCards(boss, tier, previewParty);
	}

	/// <summary>
	/// 特殊能力のカード（§0.43：判明したものだけ）。解析段階「危険情報」（50%）未満は能力の数も伏せて「？？？」の1枚、
	/// それ以降はボスが持つ能力ごとに1枚（危険度・75%で対策内容・部隊＋ポーチでの対策充足）。能力が無ければ「特殊能力なし」の1枚。
	/// 旧来は4大能力を常に4枚並べ、持っていない能力も「安全」と表示していた（能力を増やすと並べきれない）。
	/// </summary>
	private void PopulateGimmickCards(FloorBoss? boss, IntelTier tier, Party previewParty)
	{
		ClearGimmickCards();

		if (boss == null)
		{
			AddGimmickCard("[color=gray]対象のボスなし[/color]");
			return;
		}

		if (tier < IntelTier.Hazards)
		{
			AddGimmickCard("[b]？？？[/b]\n[color=yellow]特殊能力は未判明（解析率50%で判明）[/color]");
			return;
		}

		if (boss.Gimmicks.Count == 0)
		{
			AddGimmickCard("[color=lime][b]特殊能力なし[/b][/color]");
			return;
		}

		foreach (var gimmick in boss.Gimmicks)
		{
			var sb = new StringBuilder();
			sb.Append($"[b]【{GimmickLabel(gimmick.Type)}】[/b] 危険度 [color=orange]{DangerStars(gimmick.DangerLevel)}[/color]\n");
			sb.Append($"[color=gray]{GimmickDescription(gimmick.Type)}[/color]\n");

			if (tier >= IntelTier.Countermeasures)
			{
				var routes = CounterRoutes(gimmick);
				string routeStr = routes.Count > 0 ? string.Join("／", routes) : "有効な対策なし";
				sb.Append($"[color=cyan]対策：{routeStr}[/color]\n");
			}
			else
			{
				sb.Append("[color=gray]対策：？？？（解析率75%で開示）[/color]\n");
			}

			if (DungeonResolver.IsCountered(gimmick, previewParty))
				sb.Append("[color=lime][b]✔ 対策充足（選択中の部隊＋ポーチ）[/b][/color]");
			else if (gimmick.Type == BossGimmickType.InstantKill)
				sb.Append("[color=red][b]☠ 即死級 未対策（壊滅リスク）[/b][/color]");
			else if (gimmick.Type == BossGimmickType.HeavyArmor)
				sb.Append("[color=red][b]✖ 重装甲 未対策（被ダメ増）[/b][/color]");
			else
				sb.Append("[color=red][b]✖ 未対策[/b][/color]");

			AddGimmickCard(sb.ToString());
		}
	}

	/// <summary>
	/// 特殊能力のカードを1枚追加する。FitContent は使わない（2026年9月のレイアウト適正化、→ 03 §9）：
	/// RichTextLabel の最小幅は0のため、FitContent=true だと「幅ほぼ0で折り返した場合の高さ」が最小高さとして報告され、
	/// Zone B が縦に膨らんで出撃欄を画面外へ押し出していた。カードの固定高さに収め、はみ出す分は切り詰める。
	/// </summary>
	private void AddGimmickCard(string bbcode)
	{
		var card = new PanelContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(200, 86),
		};
		var margin = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
		margin.AddThemeConstantOverride("margin_left", 8);
		margin.AddThemeConstantOverride("margin_top", 4);
		margin.AddThemeConstantOverride("margin_right", 8);
		margin.AddThemeConstantOverride("margin_bottom", 4);
		card.AddChild(margin);

		var label = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = false,
			ScrollActive = false,
			CustomMinimumSize = new Vector2(180, 0),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		label.AppendText(bbcode);
		margin.AddChild(label);
		_gimmickContainer.AddChild(card);
	}

	private static List<string> CounterRoutes(BossGimmick gimmick)
	{
		var routes = new List<string>();
		if (gimmick.RequiredCounterRole.HasValue)
			routes.Add($"{JobLabel(gimmick.RequiredCounterRole.Value)}の同行");
		if (!string.IsNullOrEmpty(gimmick.RequiredCounterStat))
			routes.Add($"部隊全体の{gimmick.RequiredCounterStat}が十分に高いこと");
		if (!string.IsNullOrEmpty(gimmick.RequiredItemId))
			routes.Add($"「{ConsumableCatalog.FindById(gimmick.RequiredItemId)?.Name ?? gimmick.RequiredItemId}」の携行");
		return routes;
	}

	private void ClearGimmickCards()
	{
		foreach (var child in _gimmickContainer.GetChildren())
		{
			_gimmickContainer.RemoveChild(child);
			child.QueueFree();
		}
	}

	// ==================== Zone C: 部隊状況・出撃フロー ====================

	/// <summary>
	/// 出撃部隊の選択：第1〜第4部隊をトグルボタンで横に並べる（§0.43。旧来はプルダウン）。
	/// 出撃枠の未開放の部隊は「第N部隊（未開放）」、待機中のメンバーがいない部隊は押せない。
	/// 表示は「[待機中/編成人数] 部隊名」（人数を先に置き、長い部隊名は末尾を「…」で切る）。ツールチップに全名とメンバーを出す。選択中のボタンをもう一度押すと選択を解く
	/// （未選択の間は、扉前で判断待ちの部隊があればその指令欄を優先表示する。→ UpdateCommandAreaVisibility）。
	/// </summary>
	private void RefreshPartyOptions()
	{
		foreach (var child in _squadButtons.GetChildren())
		{
			_squadButtons.RemoveChild(child);
			child.QueueFree();
		}

		_hasSelectableParty = false;
		bool keepSelection = false;
		int unlocked = Math.Max(1, _state.UnlockedSquadSlots);
		for (int i = 0; i < _state.SavedParties.Count && i < 4; i++)
		{
			var saved = _state.SavedParties[i];
			var members = saved.MemberIds.Select(id => _state.Adventurers.FirstOrDefault(a => a.Id == id)).Where(a => a != null).ToList();
			int available = PartyFormationSystem.BuildDispatchParty(_state, saved.MemberIds).Members.Count;
			bool isLocked = i >= unlocked;

			var button = new Button
			{
				ToggleMode = true,
				ButtonGroup = _squadButtonGroup,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				CustomMinimumSize = new Vector2(0, 30),
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
				Text = isLocked ? $"第{i + 1}部隊（未開放）" : $"[{available}/{members.Count}] {saved.Name}",
				Disabled = isLocked || available == 0,
				TooltipText = isLocked
					? "出撃枠が未開放の部隊（森の節目ボス撃破で開放）"
					: $"{saved.Name}\n{(members.Count == 0 ? "（編成が空）" : string.Join("・", members.Select(a => a!.Name)))}" +
					  (available == 0 && members.Count > 0 ? "\n全員が出撃中・負傷などで出撃できない" : ""),
			};

			if (!button.Disabled)
				_hasSelectableParty = true;
			if (!button.Disabled && saved.Id == _selectedPartyId)
			{
				button.SetPressedNoSignal(true);
				keepSelection = true;
			}

			var partyId = saved.Id;
			button.Toggled += pressed => SelectParty(pressed ? partyId : null);
			_squadButtons.AddChild(button);
		}

		if (!keepSelection)
			_selectedPartyId = null;
	}

	private void SelectParty(Guid? partyId)
	{
		// ButtonGroup で別のボタンへ切り替えると、旧ボタンの解除（false）→ 新ボタンの押下（true）の順に届くことがある。
		// 解除側の通知で選択を消さないよう、解除はそのボタンが選択中だった場合に限る。
		if (partyId == null && _squadButtonGroup.GetPressedButton() != null)
			return;

		_selectedPartyId = partyId;

		if (_selectedPartyId != null)
		{
			_pendingMissionList.DeselectAll();
			_cancelMissionButton.Disabled = true;
		}

		var boss = _selectedField?.GetNextActiveBoss();
		RefreshBossInfo(DisplayedBoss(boss), boss);
		RefreshDispatchSection(boss);
		UpdateCommandAreaVisibility();
	}

	private SavedParty? SelectedSavedParty() =>
		_selectedPartyId.HasValue ? _state.SavedParties.FirstOrDefault(p => p.Id == _selectedPartyId.Value) : null;

	private void RefreshSquadMemberCards()
	{
		foreach (var child in _squadMemberContainer.GetChildren())
		{
			_squadMemberContainer.RemoveChild(child);
			child.QueueFree();
		}

		var saved = SelectedSavedParty();
		var members = saved != null
			? saved.MemberIds.Select(id => _state.Adventurers.FirstOrDefault(a => a.Id == id)).ToList()
			: new List<Adventurer?>();
		var worstLosses = saved == null
			? new List<(string Mission, double LossPct)>()
			: WorstCaseLossPercents(PartyFormationSystem.BuildDispatchParty(_state, saved.MemberIds), _selectedField?.GetNextActiveBoss());

		for (int i = 0; i < 4; i++)
		{
			var card = new PanelContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				CustomMinimumSize = new Vector2(90, 64),
			};
			SetMemberCardStyle(card, HpLevel.Normal);
			var margin = new MarginContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill,
			};
			margin.AddThemeConstantOverride("margin_left", 6);
			margin.AddThemeConstantOverride("margin_top", 4);
			margin.AddThemeConstantOverride("margin_right", 6);
			margin.AddThemeConstantOverride("margin_bottom", 4);
			card.AddChild(margin);

			var hbox = new HBoxContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill,
			};
			hbox.AddThemeConstantOverride("separation", 6);
			margin.AddChild(hbox);

			if (saved != null && i < members.Count && members[i] != null)
			{
				var adv = members[i]!;
				bool isFront = PlacementRules.GetDefault(adv.JobClass) == Placement.Front;
				bool isDispatched = _state.ActiveDungeonMissions.Any(m => m.Party.Members.Any(p => p.Id == adv.Id));
				bool isAvailable = adv.CurrentHP > 0 && !isDispatched;

				// ポートレート
				var portrait = new TextureRect
				{
					CustomMinimumSize = new Vector2(32, 32),
					SizeFlagsVertical = SizeFlags.ShrinkCenter,
					ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional,
					StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				};
				LoadPortraitTexture(portrait, adv.PortraitId);
				hbox.AddChild(portrait);

				var vbox = new VBoxContainer
				{
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					SizeFlagsVertical = SizeFlags.ShrinkCenter,
				};
				vbox.AddThemeConstantOverride("separation", 2);
				hbox.AddChild(vbox);

				// 氏名
				var nameLabel = new Label
				{
					Text = adv.Name,
				};
				nameLabel.AddThemeFontSizeOverride("font_size", FontBody);
				nameLabel.AddThemeColorOverride("font_color", isAvailable ? new Color(1, 1, 1) : new Color(0.6f, 0.6f, 0.6f));
				vbox.AddChild(nameLabel);

				// 職業・前後衛
				var badgeRow = new HBoxContainer();
				badgeRow.AddThemeConstantOverride("separation", 4);

				var jobLabel = new Label
				{
					Text = JobLabel(adv.JobClass),
				};
				jobLabel.AddThemeFontSizeOverride("font_size", FontNote);
				jobLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.85f, 0.9f));
				badgeRow.AddChild(jobLabel);

				var posBadge = new Label
				{
					Text = isFront ? "【前衛】" : "【後衛】",
				};
				posBadge.AddThemeFontSizeOverride("font_size", FontNote);
				posBadge.AddThemeColorOverride("font_color", isFront
					? new Color(0.4f, 0.9f, 1.0f)
					: new Color(0.85f, 0.65f, 1.0f));
				badgeRow.AddChild(posBadge);
				vbox.AddChild(badgeRow);

				// HPバー＆数値（§0.43：HPの割合で色分け。70%以上＝緑／40〜70%＝黄／40%未満＝赤、重傷は空のバーと残り週数）
				bool isSevere = adv.Injury == InjurySeverity.Severe;
				double hpRatio = adv.MaxHP > 0 ? (double)adv.CurrentHP / adv.MaxHP : 0;
				var level = isSevere ? HpLevel.Severe : HpLevelOf(hpRatio);

				var hpRow = new HBoxContainer();
				hpRow.AddThemeConstantOverride("separation", 4);
				var hpBar = new ProgressBar
				{
					MinValue = 0,
					MaxValue = Math.Max(1, adv.MaxHP),
					Value = isSevere ? 0 : adv.CurrentHP,
					ShowPercentage = false,
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					SizeFlagsVertical = SizeFlags.ShrinkCenter,
					CustomMinimumSize = new Vector2(0, 6),
				};
				hpBar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = HpLevelColor(level) });
				hpRow.AddChild(hpBar);

				var hpLabel = new Label
				{
					Text = isSevere ? $"重傷 残り{adv.InjuryWeeksRemaining}週" : isDispatched ? "出撃中" : $"{adv.CurrentHP}/{adv.MaxHP}",
				};
				hpLabel.AddThemeFontSizeOverride("font_size", FontNote);
				hpLabel.AddThemeColorOverride("font_color", level is HpLevel.Severe or HpLevel.Danger ? HpLevelColor(HpLevel.Danger)
					: level == HpLevel.Caution ? HpLevelColor(HpLevel.Caution) : new Color(0.9f, 0.9f, 0.9f));
				hpRow.AddChild(hpLabel);
				vbox.AddChild(hpRow);

				// 帰還時のHP見込み（最悪の損耗）：いずれかの任務で40%を割る恐れがあれば名前に ⚠（今すでに40%未満なら枠が赤いので付けない）
				var risky = isSevere || isDispatched || hpRatio < HpDangerRatio
					? new List<(string Mission, double After)>()
					: worstLosses.Select(w => (w.Mission, After: hpRatio - w.LossPct / 100.0)).ToList();
				bool atRisk = risky.Any(r => r.After < HpDangerRatio);
				if (atRisk)
				{
					nameLabel.Text = $"⚠ {adv.Name}";
					nameLabel.AddThemeColorOverride("font_color", new Color("#fb923c"));
				}

				SetMemberCardStyle(card, level);
				card.TooltipText = $"{adv.Name}：HP {adv.CurrentHP}/{adv.MaxHP}（{hpRatio * 100:F0}%）" +
					(isSevere ? $"\n重傷のため出撃不可（全治まで{adv.InjuryWeeksRemaining}週）" : "") +
					(risky.Count > 0
						? "\n帰還時のHP見込み（最悪の損耗）：" + string.Join("・", risky.Select(r => $"{r.Mission} {Math.Max(0, r.After) * 100:F0}%")) +
						  (atRisk ? "\n⚠ 40%を割る恐れ。HPの割合は討伐火力・採取スコアに掛かる。休ませるなら今。" : "")
						: "") +
					"\nHPの色：70%以上＝緑／40〜70%＝黄／40%未満＝赤";
			}
			else
			{
				var emptyLabel = new Label
				{
					Text = saved == null ? "（部隊未選択）" : "（空枠）",
					HorizontalAlignment = HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center,
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					SizeFlagsVertical = SizeFlags.ExpandFill,
				};
				emptyLabel.AddThemeFontSizeOverride("font_size", FontNote);
				emptyLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
				hbox.AddChild(emptyLabel);
			}

			_squadMemberContainer.AddChild(card);
		}
	}

	// ==== 部隊カードのHP表示（2026年9月・§0.43） ====

	/// <summary>HPの割合の段階の境目（表示用。70%以上＝通常、40〜70%＝注意、40%未満＝危険）。</summary>
	private const double HpCautionRatio = 0.7;
	private const double HpDangerRatio = 0.4;

	private enum HpLevel { Normal, Caution, Danger, Severe }

	private static HpLevel HpLevelOf(double ratio) =>
		ratio >= HpCautionRatio ? HpLevel.Normal : ratio >= HpDangerRatio ? HpLevel.Caution : HpLevel.Danger;

	private static Color HpLevelColor(HpLevel level) => level switch
	{
		HpLevel.Normal => new Color("#4ade80"),
		HpLevel.Caution => new Color("#fbbf24"),
		_ => new Color("#f87171"),
	};

	/// <summary>部隊カードの枠：通常＝灰の細枠、注意＝黄、危険＝赤、重傷＝赤の太枠。</summary>
	private static void SetMemberCardStyle(PanelContainer card, HpLevel level)
	{
		var (border, width) = level switch
		{
			HpLevel.Caution => (HpLevelColor(HpLevel.Caution), 2),
			HpLevel.Danger => (HpLevelColor(HpLevel.Danger), 2),
			HpLevel.Severe => (HpLevelColor(HpLevel.Danger), 3),
			_ => (new Color(0.3f, 0.3f, 0.35f), 1),
		};
		var style = new StyleBoxFlat { BgColor = CardBackground, BorderColor = border };
		style.SetBorderWidthAll(width);
		style.SetCornerRadiusAll(4);
		card.AddThemeStyleboxOverride("panel", style);
	}

	/// <summary>
	/// 任務ごとの最悪の損耗（最大HPに対する%）。部隊カードの「⚠ 40%を割る恐れ」の判定に使う。値は見立てカードと同じ Core の範囲の上限：
	///  - 潜行：進軍ランクの既踏の損耗上限。予測到達が未踏破の階層に及ぶなら未踏破の損耗上限（夜目の軽減込み）との大きい方
	///  - 迷宮調査：護衛評価ごとのHP消費（完全解析済みなら出撃しないので除く）
	///  - 探索：採取の損耗上限
	/// </summary>
	private List<(string Mission, double LossPct)> WorstCaseLossPercents(Party party, FloorBoss? boss)
	{
		var result = new List<(string, double)>();
		if (party.IsEmpty || _selectedField == null) return result;

		if (boss != null)
		{
			double score = DungeonTraversalResolver.CalculateTraversalScore(party, _state);
			double requirement = DungeonTraversalResolver.FloorRequirement(1);
			double ratio = requirement <= 0 ? double.MaxValue : score / requirement;
			int baseFloors = DungeonTraversalResolver.CalculateBaseFloors(ratio);
			var rank = DungeonTraversalResolver.RankFromFloors(baseFloors);
			int predictedReach = DungeonTraversalResolver.PredictFloorAfter(_selectedField, 1, baseFloors);
			double worst = DungeonTraversalResolver.RankHpLossRange(rank).Max;
			if (predictedReach > _selectedField.ReachedFloor)
				worst = Math.Max(worst, DungeonBalance.UnexploredHpLossPctMax * DungeonTraversalResolver.UnexploredLossMultiplier(party));
			result.Add(("潜行", worst));

			if (ScoutingResolver.GetTier(boss.IntelRate) != IntelTier.Complete)
				result.Add(("調査", ScoutingResolver.GuardHpLossPercent(ScoutingResolver.PreviewGuardTier(party, boss))));
		}

		result.Add(("探索", GatheringBalance.HpLossPctMax));
		return result;
	}

	private static void LoadPortraitTexture(TextureRect rect, string? portraitId)
	{
		var path = !string.IsNullOrEmpty(portraitId) ? $"res://assets/portraits/{portraitId}.png" : null;
		if (path != null && ResourceLoader.Exists(path))
		{
			rect.Texture = GD.Load<Texture2D>(path);
		}
		else
		{
			const string fallback = "res://assets/portraits/unknown_silhouette.png";
			if (ResourceLoader.Exists(fallback))
				rect.Texture = GD.Load<Texture2D>(fallback);
			else
				rect.Texture = null;
		}
	}

	/// <summary>
	/// 出撃欄。選択部隊のメンバーカードと、任務ごとの3枚のカード（潜行→討伐／迷宮調査／探索）を並べる（§0.43）。
	/// 各カードは見立て（「値 ／ 要求」と ✔／✖）と出撃ボタンを1枚にまとめたもので、枠の色で総合判定を示す
	/// （緑＝主な判定がすべて ✔／黄＝一部 ✖／赤＝主要な ✖〈火力不足・護衛不足・採れる素材なし〉／灰＝出撃できない）。
	/// 3任務とも出撃できない共通の理由（部隊未選択・出撃枠が満杯など）はカードの上の1行に、任務ごとの理由はそのカードのボタンの上に出す。
	/// 式・区間ごとの内訳はカードとボタンのツールチップへ回した。値はすべて Core（各 Resolver・DungeonResolver.CalculateBossPower／RequiredPower）から取る。
	/// </summary>
	private void RefreshDispatchSection(FloorBoss? boss)
	{
		RefreshSquadMemberCards();
		foreach (var label in new[] { _traversalPreviewLabel, _surveyPreviewLabel, _gatheringPreviewLabel })
		{
			label.Clear();
			label.TooltipText = "";
		}
		_dispatchStatusLabel.Clear();
		_scoutingButton.TooltipText = "";
		_surveyButton.TooltipText = "";
		_surveyButton.RemoveThemeColorOverride("font_color");
		_gatheringButton.TooltipText = "";

		var saved = SelectedSavedParty();
		var party = saved == null ? new Party() : PartyFormationSystem.BuildDispatchParty(_state, saved.MemberIds);

		if (saved == null || party.IsEmpty)
		{
			_scoutingButton.Disabled = true;
			_surveyButton.Disabled = true;
			_gatheringButton.Disabled = true;

			string reason = !_hasSelectableParty
				? "出撃可能な待機部隊がありません"
				: GetDispatchBlockedReason(boss, saved, party) ?? GetGatheringBlockedReason(saved, party)
					?? "出撃部隊が選択されていません。上の部隊ボタンから選んでください。";
			_dispatchStatusLabel.AppendText($"[color=gray]{reason}[/color]");
			_traversalPreviewLabel.AppendText("[b]🏃 潜行 → ⚔️ 討伐[/b]\n[color=gray]部隊を選ぶと見立てを表示[/color]");
			_surveyPreviewLabel.AppendText("[b]🔍 迷宮調査[/b]\n[color=gray]部隊を選ぶと見立てを表示[/color]");
			_gatheringPreviewLabel.AppendText("[b]🌿 探索（採取）[/b]\n[color=gray]部隊を選ぶと見立てを表示[/color]");
			SetCardReason(_traversalReasonLabel, null);
			SetCardReason(_surveyReasonLabel, null);
			SetCardReason(_gatheringReasonLabel, null);
			foreach (var card in new[] { _traversalCard, _surveyCard, _gatheringCard })
				SetCardVerdict(card, CardVerdict.Unavailable);
			return;
		}

		string? blockedReason = GetDispatchBlockedReason(boss, saved, party);
		_scoutingButton.Disabled = blockedReason != null;
		bool fullyAnalyzed = boss != null && ScoutingResolver.GetTier(boss.IntelRate) == IntelTier.Complete;
		_surveyButton.Disabled = blockedReason != null || fullyAnalyzed;
		if (fullyAnalyzed)
			_surveyButton.TooltipText = "対象ボスは完全解析済み。これ以上の調査は不要。";

		string? gatheringBlockedReason = GetGatheringBlockedReason(saved, party);
		_gatheringButton.Disabled = gatheringBlockedReason != null;

		// 3任務とも同じ理由で出撃できない（出撃枠が満杯など）ならカードの上に1行だけ。任務ごとに違えば各カードへ。
		bool commonReason = blockedReason != null && blockedReason == gatheringBlockedReason;
		if (commonReason)
			_dispatchStatusLabel.AppendText($"[color=gray]{blockedReason}[/color]");
		SetCardReason(_traversalReasonLabel, commonReason ? null : blockedReason);
		SetCardReason(_surveyReasonLabel, commonReason ? null : blockedReason ?? (fullyAnalyzed ? "完全解析済みのため調査は不要" : null));
		SetCardReason(_gatheringReasonLabel, commonReason ? null : gatheringBlockedReason);

		var traversal = FillTraversalPreview(boss, party);
		var survey = FillSurveyPreview(boss, party, fullyAnalyzed);
		var gathering = FillGatheringPreview(party);
		SetCardVerdict(_traversalCard, _scoutingButton.Disabled ? CardVerdict.Unavailable : traversal);
		SetCardVerdict(_surveyCard, _surveyButton.Disabled ? CardVerdict.Unavailable : survey);
		SetCardVerdict(_gatheringCard, _gatheringButton.Disabled ? CardVerdict.Unavailable : gathering);
	}

	/// <summary>カードの総合判定（枠の色）。</summary>
	private enum CardVerdict { Unavailable, Good, Caution, Bad }

	private static readonly Color CardBackground = new(0.13f, 0.13f, 0.16f);

	private static void SetCardVerdict(PanelContainer card, CardVerdict verdict)
	{
		var (border, width) = verdict switch
		{
			CardVerdict.Good => (new Color("#4ade80"), 2),
			CardVerdict.Caution => (new Color("#fbbf24"), 2),
			CardVerdict.Bad => (new Color("#f87171"), 2),
			_ => (new Color(0.35f, 0.35f, 0.4f), 1),
		};
		var style = new StyleBoxFlat { BgColor = CardBackground, BorderColor = border };
		style.SetBorderWidthAll(width);
		style.SetCornerRadiusAll(4);
		card.AddThemeStyleboxOverride("panel", style);
		card.TooltipText = verdict switch
		{
			CardVerdict.Good => "総合判定：主な判定はすべて ✔",
			CardVerdict.Caution => "総合判定：一部に ✖ あり（出撃はできるが成果が落ちる）",
			CardVerdict.Bad => "総合判定：主要な ✖ あり（火力不足・護衛不足・採れる素材なし）",
			_ => "",
		};
	}

	/// <summary>任務ごとの出撃できない理由（ボタンの上に灰色で）。null なら隠す。</summary>
	private static void SetCardReason(RichTextLabel label, string? reason)
	{
		label.Clear();
		label.Visible = reason != null;
		if (reason != null)
			label.AppendText($"[color=gray]{reason}[/color]");
	}

	/// <summary>✔（緑）／✖（赤）の短い判定表示。</summary>
	private static string Check(bool ok, string okText, string ngText) =>
		ok ? $"[color=#4ade80]✔ {okText}[/color]" : $"[color=#f87171]✖ {ngText}[/color]";

	/// <summary>🏃 潜行 → ⚔️ 討伐：走破の見立て（進める階層・損耗）と、扉前に着いた後の討伐火力 ／ 要求火力。</summary>
	private CardVerdict FillTraversalPreview(FloorBoss? boss, Party party)
	{
		var sb = new StringBuilder("[b]🏃 潜行 → ⚔️ 討伐[/b]");
		if (_selectedField == null || boss == null)
		{
			sb.Append("\n[color=gray]挑むべき階層ボスがいない[/color]");
			_traversalPreviewLabel.AppendText(sb.ToString());
			return CardVerdict.Unavailable;
		}

		double score = DungeonTraversalResolver.CalculateTraversalScore(party, _state);
		double requirement = DungeonTraversalResolver.FloorRequirement(1);
		double ratio = requirement <= 0 ? double.MaxValue : score / requirement;
		int baseFloors = DungeonTraversalResolver.CalculateBaseFloors(ratio);
		var rank = DungeonTraversalResolver.RankFromFloors(baseFloors);
		int predictedReach = DungeonTraversalResolver.PredictFloorAfter(_selectedField, 1, baseFloors);
		var (rankLossMin, rankLossMax) = DungeonTraversalResolver.RankHpLossRange(rank);
		// 夜目（→ 03 §4.5.3）の保有者がいれば、未踏破の損耗レンジを実際の解決と同じ倍率で縮めて見せる。
		double unexploredMul = DungeonTraversalResolver.UnexploredLossMultiplier(party);
		string unexploredLoss = unexploredMul < 1.0
			? $"{DungeonBalance.UnexploredHpLossPctMin * unexploredMul:0.#}〜{DungeonBalance.UnexploredHpLossPctMax * unexploredMul:0.#}%（夜目）"
			: $"{DungeonBalance.UnexploredHpLossPctMin}〜{DungeonBalance.UnexploredHpLossPctMax}%";
		var segments = DungeonTraversalResolver.DescribeSegments(_selectedField, 1, boss.Floor, _selectedField.ReachedFloor);

		double power = DungeonResolver.CalculateBossPower(party, boss);
		double requiredPower = DungeonResolver.RequiredPower(boss);

		sb.Append($"\n目標：第{boss.Floor}層「{boss.Name}」扉前");
		sb.Append($"\n走破力 [b]{score:F0}[/b] ／ 要求 {requirement:F0}（×{FormatRatio(ratio)}）");
		sb.Append($"\n見立て：[color=cyan]{TraversalRankLabel(rank)}[/color] → 予測 第{predictedReach}層（+{predictedReach - 1}）");
		sb.Append($"\n[color=gray]損耗 既踏{rankLossMin}〜{rankLossMax}%・未踏破{unexploredLoss}[/color]");
		sb.Append($"\n討伐火力 [b]{power:F0}[/b] ／ 要求 {requiredPower:F0}　{Check(power >= requiredPower, "撃破見込み", "火力不足")}");
		_traversalPreviewLabel.AppendText(sb.ToString());

		string tooltip =
			$"区間別の走破倍率：{SegmentPreviewText(segments)}\n" +
			$"1階層ごとに 1÷区間倍率 の予算を消費して進む（区間倍率＝1.0＋区間担当ボスの解析率×{DungeonTraversalBalance.IntelSpeedBonusPerIntel:0.#}）。\n" +
			$"損耗は階層ごとに積み上げる：その階層の基礎率（既踏／未踏破）÷歩いた階層数×区間の被ダメ倍率（完全解析区間は×{DungeonTraversalBalance.FullIntelDamageMultiplier:0.0#}）。\n" +
			$"走破力（Σ(VIT×{DungeonTraversalBalance.WeightVit:0.#}＋MND×{DungeonTraversalBalance.WeightMnd:0.#})＋部隊長LDR補正、研究・参謀込み）：{score:F0}\n" +
			$"討伐火力：扉前でボスに挑んだときの部隊火力（完全解析+{DungeonBalance.FullIntelDamageBonus * 100:F0}%・巨獣狩り込み）。要求＝ボス階層×{DungeonBalance.PartyPowerRequirementPerFloor:0.#}。\n" +
			"道中進軍は低リスク：HPは減っても強制除籍にはならない。\n" +
			"道中で拾った素材・ゴールドは、ギルドへ帰還した時点で格納される。";
		_traversalPreviewLabel.TooltipText = tooltip;
		_scoutingButton.TooltipText = tooltip;
		return power >= requiredPower ? CardVerdict.Good : CardVerdict.Bad;
	}

	/// <summary>🔍 迷宮調査：護衛 ／ 要求 → 護衛評価、隠密 ／ 要求、解析 ／ 要求 → 成果。</summary>
	private CardVerdict FillSurveyPreview(FloorBoss? boss, Party party, bool fullyAnalyzed)
	{
		var sb = new StringBuilder("[b]🔍 迷宮調査[/b]");
		if (boss == null)
		{
			sb.Append("\n[color=gray]調べる階層ボスがいない[/color]");
			_surveyPreviewLabel.AppendText(sb.ToString());
			return CardVerdict.Unavailable;
		}
		if (fullyAnalyzed)
		{
			sb.Append($"\n第{boss.Floor}層「{boss.Name}」\n[color=gold]完全解析済み（調査不要）[/color]");
			_surveyPreviewLabel.AppendText(sb.ToString());
			return CardVerdict.Unavailable;
		}

		var tier = ScoutingResolver.PreviewGuardTier(party, boss);
		double guardPower = ScoutingResolver.CalculateGuardPower(party);
		double reqGuard = ScoutingResolver.RequiredGuardPower(boss);
		var (carrier, carrierStat, carrierValue) = ScoutingResolver.FindGuardCarrier(party);
		double guardSupport = ScoutingResolver.CalculateGuardSupportPower(party);
		double stealth = ScoutingResolver.CalculateStealthScore(party);
		double stealthReq = ScoutingResolver.StealthRequirement(boss);
		double analysis = ScoutingResolver.CalculateAnalysisScore(party);
		double analysisReq = ScoutingResolver.AnalysisRequirement(boss);
		double analysisRatio = analysisReq <= 0 ? double.MaxValue : analysis / analysisReq;

		sb.Append($"\n護衛 [b]{guardPower:F0}[/b] ／ 要求 {reqGuard:F0} → [color={GuardTierColor(tier)}][b]{GuardTierLabel(tier)}[/b][/color]");
		sb.Append($"\n[color=gray]{carrier?.Name} {carrierStat}{carrierValue:F0}{(guardSupport > 0 ? $"＋支援{guardSupport:F0}" : "")}・解析×{ScoutingResolver.GuardIntelMultiplier(tier):0.0#}・HP−{ScoutingResolver.GuardHpLossPercent(tier)}%[/color]");
		sb.Append($"\n隠密 [b]{stealth:F0}[/b] ／ 要求 {stealthReq:F0}　{Check(stealth >= stealthReq, "潜入可", "発見→成果1段階低下")}");
		sb.Append($"\n解析 [b]{analysis:F0}[/b] ／ 要求 {analysisReq:F0} → {SurveyOutcomeLabel(ScoutingResolver.ClassifyAnalysis(analysisRatio))}");
		_surveyPreviewLabel.AppendText(sb.ToString());

		string tooltip =
			$"護衛力（主護衛＝隊員の中で最も高いSTR・VIT・INT ＋ 他の隊員それぞれのSTR・VIT・INTの最大値×{ScoutingBalance.GuardSupportRatio:0.##}）：{guardPower:F0}\n" +
			$"要求護衛値＝{ScoutingBalance.BaseRequiredGuardPower}×{boss.Floor}F÷10。人数が多いほど護衛力は上がるが、隠密は下がる（大勢で守るか、少数で潜むか）。\n" +
			$"護衛評価：{GuardTierLabel(tier)}（{GuardTierDescription(tier)}。解析成果 ×{ScoutingResolver.GuardIntelMultiplier(tier):F2}、" +
			$"各員のHP消費 最大HPの{ScoutingResolver.GuardHpLossPercent(tier)}%）\n" +
			"調査は低リスク：HPは減っても強制除籍にはならない。";
		if (tier == GuardTier.Deficient)
		{
			tooltip = "⚠ 護衛役（戦士・騎士・魔導士等）が不足しており危険。魔物の残党に強襲されて潰走し、" +
				"解析の成果を持ち帰れないうえ大きな手傷を負う。\n" + tooltip;
			_surveyButton.AddThemeColorOverride("font_color", new Color(1f, 0.45f, 0.3f));
		}
		_surveyPreviewLabel.TooltipText = tooltip;
		if (_surveyButton.TooltipText.Length == 0)
			_surveyButton.TooltipText = tooltip;

		if (tier == GuardTier.Deficient) return CardVerdict.Bad;
		bool analysisFails = ScoutingResolver.ClassifyAnalysis(analysisRatio) == SurveyOutcome.Failure;
		return stealth < stealthReq || analysisFails || tier == GuardTier.Marginal ? CardVerdict.Caution : CardVerdict.Good;
	}

	/// <summary>🌿 探索（採取）：採取スコアと持ち帰る素材の枠・遺物発見率・換金・損耗。</summary>
	private CardVerdict FillGatheringPreview(Party party)
	{
		var sb = new StringBuilder("[b]🌿 探索（採取）[/b]");
		if (_selectedField == null)
		{
			sb.Append("\n[color=gray]ダンジョンが選択されていない[/color]");
			_gatheringPreviewLabel.AppendText(sb.ToString());
			return CardVerdict.Unavailable;
		}

		double gatheringScore = GatheringResolver.CalculateGatheringScore(party);
		var breakdown = GatheringResolver.BreakDownGatheringScore(party);
		var eligible = MaterialBalance.GetEligibleMaterials(_selectedField.Id, _selectedField.ReachedFloor);
		string baseYieldText = eligible.Count == 0
			? "0"
			: eligible.Min(m => m.BaseYield) == eligible.Max(m => m.BaseYield)
				? $"{eligible[0].BaseYield}"
				: $"{eligible.Min(m => m.BaseYield)}〜{eligible.Max(m => m.BaseYield)}";
		int scoreYield = GatheringResolver.ScoreYield(gatheringScore);
		int floorYield = GatheringResolver.FloorYield(_selectedField);
		int researchYield = GatheringResolver.ResearchYield(_state);
		int relicPct = RelicBalance.GetGatheringDropPercent(gatheringScore, _selectedField.ReachedFloor);

		sb.Append($"\n採取 [b]{gatheringScore:F0}[/b]{(breakdown.RuralTotal > 0 ? $"（田舎育ち+{breakdown.RuralTotal:F0}）" : "")}");
		sb.Append(eligible.Count == 0
			? "\n[color=#f87171]✖ この到達階層で採れる素材なし[/color]"
			: $"\n素材の枠：基礎{baseYieldText}＋スコア{scoreYield}＋階層{floorYield}＋研究{researchYield}");
		sb.Append($"\n遺物発見 {relicPct}%・換金 {Math.Round(gatheringScore * GatheringBalance.GoldPerScore):F0}G");
		sb.Append($"\n[color=gray]損耗 {GatheringBalance.HpLossPctMin}〜{GatheringBalance.HpLossPctMax}%[/color]");
		_gatheringPreviewLabel.AppendText(sb.ToString());

		string tooltip =
			$"採取スコア（AGI+DEX合計＋部隊長LDR補正、部隊のHP比率で減衰）：{gatheringScore:F0}〔{GatheringBreakdownText(breakdown)}〕\n" +
			$"素材の枠：素材ごとの基礎＋スコア枠（{gatheringScore:F0}÷{GatheringBalance.MaterialYieldDivisor}）＋階層枠（{_selectedField.ReachedFloor}F÷{GatheringBalance.ReachedFloorDivisor}）＋研究\n" +
			"探索は低リスク：HPは減っても強制除籍にはならない。";
		_gatheringPreviewLabel.TooltipText = tooltip;
		_gatheringButton.TooltipText = tooltip;
		return eligible.Count == 0 ? CardVerdict.Bad : CardVerdict.Good;
	}

	private string? GetDispatchBlockedReason(FloorBoss? boss, SavedParty? saved, Party party)
	{
		if (boss == null) return "挑むべき階層ボスがいない。";
		if (_state.DefeatReason != null) return "ギルドは既に解散した。";
		if (saved == null) return "出撃部隊が選択されていない。上の部隊ボタンから選ぶこと。";
		if (_selectedField != null && !_selectedField.IsUnlocked)
			return $"「{_selectedField.Name}」はまだ開放されていない。";
		if (boss.IsDefeated) return $"第{boss.Floor}層のボスは既に撃破済み。";
		if (party.IsEmpty)
		{
			var unavailable = PartyFormationSystem.GetUnavailableMembers(_state, saved.MemberIds);
			return unavailable.Count > 0
				? $"この部隊は全員出撃できない状態（重傷・派遣中など）：{string.Join("、", unavailable.Select(m => m.Name))}"
				: "この部隊には出撃できるメンバーがいない（編成が空）。";
		}
		if (!DungeonExpeditionSystem.CanDispatch(_state))
			return $"同時出撃枠（{_state.UnlockedSquadSlots}枠）がすべて埋まっている。帰還を待つか、出撃予定を取り消すこと。";
		return null;
	}

	private string? GetGatheringBlockedReason(SavedParty? saved, Party party)
	{
		if (_selectedField == null) return "挑戦するダンジョンがない。";
		if (_state.DefeatReason != null) return "ギルドは既に解散した。";
		if (saved == null) return "出撃部隊が選択されていない。上の部隊ボタンから選ぶこと。";
		if (!_selectedField.IsUnlocked) return $"「{_selectedField.Name}」はまだ開放されていない。";
		if (party.IsEmpty)
		{
			var unavailable = PartyFormationSystem.GetUnavailableMembers(_state, saved.MemberIds);
			return unavailable.Count > 0
				? $"この部隊は全員出撃できない状態（重傷・派遣中など）：{string.Join("、", unavailable.Select(m => m.Name))}"
				: "この部隊には出撃できるメンバーがいない（編成が空）。";
		}
		if (!DungeonExpeditionSystem.CanDispatch(_state))
			return $"同時出撃枠（{_state.UnlockedSquadSlots}枠）がすべて埋まっている。帰還を待つか、出撃予定を取り消すこと。";
		return null;
	}

	private void ShowDispatchFailure(string reason)
	{
		_dispatchStatusLabel.Clear();
		_dispatchStatusLabel.AppendText($"[color=orange][b]⚠ 出撃できなかった：{reason}[/b][/color]");
	}

	private void OnDispatchPressed()
	{
		var boss = _selectedField?.GetNextActiveBoss();
		var saved = SelectedSavedParty();
		if (_expeditionSystem == null)
			return;

		if (_selectedField == null || boss == null || saved == null)
		{
			ShowDispatchFailure(_selectedField == null
				? "挑戦するダンジョン（フィールド）が選択されていない。"
				: boss == null
					? "このフィールドは完全制覇済みで、挑むべき階層ボスがいない。"
					: "出撃部隊が選択されていない。上の部隊ボタンから選ぶこと。");
			return;
		}

		var party = PartyFormationSystem.BuildDispatchParty(_state, saved.MemberIds);
		string? blockedReason = GetDispatchBlockedReason(boss, saved, party);
		if (blockedReason != null || !_expeditionSystem.TryDispatch(_state, party, boss, DungeonMissionType.Scouting))
		{
			ShowDispatchFailure(blockedReason ?? "出撃条件を満たしていない（同時出撃枠・部隊の状態・フィールドの開放状況を確認）。");
			return;
		}

		string members = string.Join("・", party.Members.Select(m => m.Name));
		LogRequested.Invoke($"[color=cyan]{GameCalendar.Format(_state.WeekNumber)}：「{saved.Name}」（{members}）が{_selectedField.Name}の" +
			$"第1層から潜行を開始する（目標：第{boss.Floor}層「{boss.Name}」の扉前）。[/color]");

		_selectedPartyId = null;
		StateChanged.Invoke();
	}

	private void OnSurveyDispatchPressed()
	{
		var boss = _selectedField?.GetNextActiveBoss();
		var saved = SelectedSavedParty();
		if (_expeditionSystem == null)
			return;

		if (_selectedField == null || boss == null || saved == null)
		{
			ShowDispatchFailure(_selectedField == null
				? "調査するダンジョン（フィールド）が選択されていない。"
				: boss == null
					? "このフィールドは完全制覇済みで、調査すべき階層ボスがいない。"
					: "出撃部隊が選択されていない。上の部隊ボタンから選ぶこと。");
			return;
		}

		var party = PartyFormationSystem.BuildDispatchParty(_state, saved.MemberIds);
		string? blockedReason = GetDispatchBlockedReason(boss, saved, party);
		if (blockedReason == null && ScoutingResolver.GetTier(boss.IntelRate) == IntelTier.Complete)
			blockedReason = "対象ボスは完全解析済み。これ以上の調査は不要。";
		if (blockedReason != null || !_expeditionSystem.TryDispatchSurvey(_state, party, boss))
		{
			ShowDispatchFailure(blockedReason ?? "出撃条件を満たしていない（同時出撃枠・部隊の状態・フィールドの開放状況を確認）。");
			return;
		}

		var tier = ScoutingResolver.PreviewGuardTier(party, boss);
		string members = string.Join("・", party.Members.Select(m => m.Name));
		LogRequested.Invoke($"[color=cyan]{GameCalendar.Format(_state.WeekNumber)}：「{saved.Name}」（{members}）が{_selectedField.Name} 第{boss.Floor}層" +
			$"「{boss.Name}」の迷宮調査へ出発する（護衛評価：{GuardTierLabel(tier)}、次週の決算で帰還）。[/color]");

		_selectedPartyId = null;
		StateChanged.Invoke();
	}

	private void OnGatheringDispatchPressed()
	{
		var saved = SelectedSavedParty();
		if (_expeditionSystem == null)
			return;

		if (_selectedField == null || saved == null)
		{
			ShowDispatchFailure(_selectedField == null
				? "採取に向かうダンジョン（フィールド）が選択されていない。"
				: "出撃部隊が選択されていない。上の部隊ボタンから選ぶこと。");
			return;
		}

		var party = PartyFormationSystem.BuildDispatchParty(_state, saved.MemberIds);
		string? blockedReason = GetGatheringBlockedReason(saved, party);
		if (blockedReason != null || !_expeditionSystem.TryDispatchGathering(_state, party, _selectedField))
		{
			ShowDispatchFailure(blockedReason ?? "出撃条件を満たしていない（同時出撃枠・部隊の状態・フィールドの開放状況を確認）。");
			return;
		}

		string members = string.Join("・", party.Members.Select(m => m.Name));
		LogRequested.Invoke($"[color=cyan]{GameCalendar.Format(_state.WeekNumber)}：「{saved.Name}」（{members}）が{_selectedField.Name}へ" +
			"探索（採取）任務へ出発する（次週の決算で帰還）。[/color]");

		_selectedPartyId = null;
		StateChanged.Invoke();
	}

	// ==================== 潜行中の部隊・状況排他コマンド ====================

	private void RefreshPendingMissions()
	{
		_pendingMissionList.Clear();
		foreach (var mission in _state.ActiveDungeonMissions)
		{
			string members = string.Join("・", mission.Party.Members.Select(m => m.Name));
			_pendingMissionList.AddItem($"{MissionLabel(mission)}：{members}");
		}

		if (_state.ActiveDungeonMissions.Count == 0)
			_pendingMissionList.AddItem("（潜行中の部隊はいない）", null, false);

		_cancelMissionButton.Disabled = true;
	}

	private static string MissionLabel(ActiveDungeonMission mission)
	{
		if (mission.MissionType == DungeonMissionType.Gathering)
			return $"【探索】{mission.Field.Name}";
		if (mission.MissionType == DungeonMissionType.Survey)
			return $"【迷宮調査】{mission.Field.Name} 第{mission.Boss?.Floor}層「{mission.Boss?.Name}」";

		var boss = mission.TargetedBoss ?? mission.Boss;
		string bossName = boss == null ? "" : $"「{boss.Name}」";
		string loot = LootSummary(mission);
		return mission.Status switch
		{
			ExpeditionStatus.AwaitingBossDecision => $"【扉前・指令待ち】{mission.Field.Name} {mission.CurrentFloor}F{bossName}　{loot}",
			ExpeditionStatus.EngagingBoss => $"【討伐突入】{mission.Field.Name} {mission.CurrentFloor}F{bossName}　{loot}",
			ExpeditionStatus.Retreating => $"【撤退中】{mission.Field.Name}　{loot}",
			_ => mission.WeeksElapsed == 0
				? $"【出発準備】{mission.Field.Name} 1Fから潜行"
				: $"【進軍中】{mission.Field.Name} {mission.CurrentFloor}F　{loot}",
		};
	}

	private static string LootSummary(ActiveDungeonMission mission) =>
		$"拾得 {mission.CarriedGold}G・素材{mission.CarriedMaterials.Values.Sum()}個";

	private ActiveDungeonMission? SelectedPendingMission()
	{
		var selected = _pendingMissionList.GetSelectedItems();
		if (selected.Length == 0)
			return null;
		int index = selected[0];
		return index >= 0 && index < _state.ActiveDungeonMissions.Count ? _state.ActiveDungeonMissions[index] : null;
	}

	private void OnPendingMissionSelected()
	{
		var mission = SelectedPendingMission();
		_cancelMissionButton.Disabled = mission == null || mission.WeeksElapsed > 0;
		UpdateCommandAreaVisibility();
	}

	private ActiveDungeonMission? DecisionTarget()
	{
		var selected = SelectedPendingMission();
		if (selected != null && IsRetreatable(selected))
			return selected;
		return _state.ActiveDungeonMissions.FirstOrDefault(m => m.Status == ExpeditionStatus.AwaitingBossDecision);
	}

	private static bool IsRetreatable(ActiveDungeonMission mission) =>
		mission.MissionType == DungeonMissionType.Scouting &&
		(mission.Status == ExpeditionStatus.AwaitingBossDecision ||
		 (mission.Status == ExpeditionStatus.Advancing && mission.WeeksElapsed > 0));

	private void UpdateCommandAreaVisibility()
	{
		var selectedMission = SelectedPendingMission();

		if (selectedMission != null)
		{
			if (selectedMission.Status == ExpeditionStatus.AwaitingBossDecision)
			{
				_commandAreaBossDecision.Visible = true;
				_commandAreaAdvancing.Visible = false;
				_commandAreaStandby.Visible = false;
				RefreshBossDecision();
				return;
			}
			else if (selectedMission.Status == ExpeditionStatus.Advancing)
			{
				_commandAreaAdvancing.Visible = true;
				_commandAreaBossDecision.Visible = false;
				_commandAreaStandby.Visible = false;
				RefreshAdvancingStatus(selectedMission);
				return;
			}
		}

		// 未選択時でも扉前到達部隊が存在すれば優先表示
		var awaitingMission = _state?.ActiveDungeonMissions.FirstOrDefault(m => m.Status == ExpeditionStatus.AwaitingBossDecision);
		if (awaitingMission != null && _selectedPartyId == null)
		{
			_commandAreaBossDecision.Visible = true;
			_commandAreaAdvancing.Visible = false;
			_commandAreaStandby.Visible = false;
			RefreshBossDecision();
			return;
		}

		// 通常待機出撃コマンド
		_commandAreaStandby.Visible = true;
		_commandAreaAdvancing.Visible = false;
		_commandAreaBossDecision.Visible = false;
	}

	private void RefreshAdvancingStatus(ActiveDungeonMission mission)
	{
		_advancingStatusLabel.Clear();
		string members = string.Join("・", mission.Party.Members.Select(m => $"{m.Name}(HP{m.CurrentHP}/{m.MaxHP})"));
		string loot = CarriedLootText(mission);
		bool canRetreat = IsRetreatable(mission);

		var sb = new StringBuilder();
		sb.AppendLine($"[b]【進軍中】{mission.Field.Name} 第{mission.CurrentFloor}層[/b]（経過 {mission.WeeksElapsed}週）");
		sb.AppendLine($"部隊：{members}");
		sb.AppendLine($"道中拾得：{loot}");
		if (canRetreat)
		{
			sb.Append("[color=cyan]次週さらに奥へ進軍します。途中で切り上げる場合は下の「緊急帰還指示」を実行してください。[/color]");
		}
		else
		{
			sb.Append("[color=gray]出発準備中です。取り消す場合は右上の「出撃取り消し」ボタンを押してください。[/color]");
		}

		_advancingStatusLabel.AppendText(sb.ToString());
		_emergencyRetreatButton.Disabled = !canRetreat;
	}

	private void RefreshBossDecision()
	{
		_bossDecisionLabel.Clear();
		_engageBossButton.TooltipText = "";

		var mission = DecisionTarget();
		if (mission == null)
		{
			_engageBossButton.Disabled = true;
			_retreatButton.Disabled = true;
			return;
		}

		string members = string.Join("・", mission.Party.Members.Select(m => $"{m.Name}(HP{m.CurrentHP}/{m.MaxHP})"));
		string loot = CarriedLootText(mission);
		_retreatButton.Disabled = !IsRetreatable(mission);

		if (mission.Status != ExpeditionStatus.AwaitingBossDecision || mission.TargetedBoss == null)
		{
			_bossDecisionLabel.AppendText($"[b]{mission.Field.Name} 第{mission.CurrentFloor}層を進軍中[/b]：{members}\n" +
				$"持ち帰り予定：{loot}");
			_engageBossButton.Disabled = true;
			_engageBossButton.TooltipText = "ボスの扉前に到達してから挑める。";
			return;
		}

		var boss = mission.TargetedBoss;
		var preview = new Party();
		foreach (var member in mission.Party.Members)
			preview.TryAdd(member);
		foreach (var itemId in GetSelectedPouchItemIds())
			preview.TryAddConsumable(itemId);
		int pouchCost = DungeonExpeditionSystem.CalculateConsumableCost(preview.ConsumableItemIds);
		bool insufficient = pouchCost > 0 && _state.Gold < pouchCost;

		_bossDecisionLabel.AppendText(
			$"[color=gold][b]【扉前到達】第{boss.Floor}層「{boss.Name}」の扉前で指令を待っている[/b][/color]\n" +
			$"部隊：{members}\n持ち帰り予定：{loot}\n" +
			BuildCountermeasureText(boss, preview, out string tooltip) +
			"\n[color=gray]指令を出さずに週を越すと、扉前でボスの偵察を続ける（解析率が上がる）。[/color]");
		if (insufficient)
		{
			_bossDecisionLabel.AppendText(
				$"\n[color=red][b]⚠ ポーチ代金が不足しています（必要 {pouchCost}G／所持金 {_state.Gold}G）。" +
				"ポーチの選択を減らすか、撤退してください。[/b][/color]");
		}

		_engageBossButton.Text = pouchCost > 0 ? $"⚔️ ボス討伐に挑む（ポーチ代 {pouchCost}G）" : "⚔️ ボス討伐に挑む";
		_engageBossButton.Disabled = insufficient || _state.DefeatReason != null;
		_engageBossButton.TooltipText = insufficient
			? $"ポーチ代金が不足しています（必要 {pouchCost}G／所持金 {_state.Gold}G）。"
			: tooltip + "\n次週の決算で決戦判定。勝敗にかかわらず決着後はギルドへ帰還する。";
	}

	private static string CarriedLootText(ActiveDungeonMission mission)
	{
		var parts = new List<string> { $"{mission.CarriedGold}G" };
		parts.AddRange(mission.CarriedMaterials.Select(kv => $"{MaterialBalance.GetName(kv.Key)}×{kv.Value}"));
		return string.Join("、", parts);
	}

	private static string BuildCountermeasureText(FloorBoss boss, Party party, out string tooltip)
	{
		var tier = ScoutingResolver.GetTier(boss.IntelRate);
		var sb = new StringBuilder();
		sb.Append("[b]対策の充足状況[/b]\n");

		if (tier < IntelTier.Hazards)
		{
			sb.Append("[color=red][b]⚠ ギミックが未解析のため、何が待ち受けているか分からない。" +
				"このまま挑めば被害が跳ね上がり、HPが尽きた者はギルド登録を強制抹消される（壊滅の恐れ）。[/b][/color]");
			tooltip = "ギミック未解析：扉前で待機させれば偵察して解析率を上げられる。";
			return sb.ToString();
		}

		var uncountered = new List<BossGimmick>();
		foreach (var gimmick in boss.Gimmicks)
		{
			bool countered = DungeonResolver.IsCountered(gimmick, party);
			if (!countered) uncountered.Add(gimmick);
			sb.Append(countered
				? $"[color=lime]✔ {GimmickLabel(gimmick.Type)}：充足[/color]\n"
				: $"[color=red]✖ {GimmickLabel(gimmick.Type)}：未対策[/color]\n");
		}

		if (boss.Gimmicks.Count == 0)
			sb.Append("[color=lime]対策が必要な能力は無い。[/color]\n");

		if (uncountered.Count > 0)
		{
			sb.Append("[color=red][b]⚠ 未対策のギミックがある。被害が跳ね上がり、HPが尽きた者は秘薬で一命を取り留めても" +
				"ギルド登録を強制抹消される（恒久ロスト）。[/b][/color]");
			if (uncountered.Any(g => g.Type == BossGimmickType.InstantKill))
				sb.Append("\n[color=red][b]☠ 即死級攻撃が未対策：部隊の壊滅は避けられない。[/b][/color]");
			if (tier < IntelTier.Countermeasures)
				sb.Append("\n[color=gray]（対策の方法は解析率75%で判明する）[/color]");
		}
		else if (tier == IntelTier.Complete)
		{
			sb.Append("[color=gold]すべて対策済み・完全解析の弱点看破も乗る。万全の布陣だ。[/color]");
		}
		else
		{
			sb.Append("[color=cyan]判明しているギミックはすべて対策済み。[/color]");
		}

		tooltip = uncountered.Count > 0
			? $"未対策 {uncountered.Count}件：強制除籍・壊滅のリスクあり"
			: "判明しているギミックはすべて対策済み";
		return sb.ToString().TrimEnd('\n');
	}

	private void OnEngageBossPressed()
	{
		var mission = DecisionTarget();
		if (mission == null || _expeditionSystem == null)
			return;

		var boss = mission.TargetedBoss;
		var items = GetSelectedPouchItemIds();
		if (!_expeditionSystem.TryEngageBoss(_state, mission, items))
		{
			ShowDispatchFailure("討伐を指令できなかった（扉前で待機中か、携行ポーチの代金が足りているかを確認）。");
			return;
		}

		int pouchCost = DungeonExpeditionSystem.CalculateConsumableCost(mission.Party.ConsumableItemIds);
		string members = string.Join("・", mission.Party.Members.Select(m => m.Name));
		string pouchNote = pouchCost > 0 ? $"　携行ポーチ代 {pouchCost}Gを支払った。" : "";
		LogRequested.Invoke($"[color=gold][b]⚔ {GameCalendar.Format(_state.WeekNumber)}：{members}が第{boss!.Floor}層「{boss.Name}」の扉を開き、" +
			$"討伐へ突入する（次週の決算で決着）。[/b]{pouchNote}[/color]");
		ResetPouchSelection();
		StateChanged.Invoke();
	}

	private void OnRetreatPressed()
	{
		var mission = DecisionTarget();
		if (mission == null || _expeditionSystem == null)
			return;

		var resolution = _expeditionSystem.TryRetreat(_state, mission);
		if (resolution == null)
		{
			ShowDispatchFailure("撤退できなかった（出発前の部隊は「取り消す」を使うこと）。");
			return;
		}

		string members = string.Join("・", mission.Party.Members.Select(m => m.Name));
		var loot = new List<string> { $"{resolution.DepositedGold}G" };
		loot.AddRange(resolution.DepositedMaterials.Select(kv => $"{MaterialBalance.GetName(kv.Key)}×{kv.Value}"));
		LogRequested.Invoke($"[color=cyan]🏃 {GameCalendar.Format(_state.WeekNumber)}：{members}は{mission.Field.Name} 第{mission.CurrentFloor}層から撤退し、" +
			$"ギルドへ帰還した。道中の拾得物（{string.Join("、", loot)}）を格納した。次回は第1層から潜り直す。[/color]");
		StateChanged.Invoke();
	}

	private void OnCancelMissionPressed()
	{
		var mission = SelectedPendingMission();
		if (mission == null || _expeditionSystem == null)
			return;

		if (!_expeditionSystem.TryCancel(_state, mission))
		{
			ShowDispatchFailure("既に潜行を始めた部隊は取り消せない。呼び戻すには「撤退・帰還する」を使うこと。");
			return;
		}

		string missionName = mission.MissionType switch
		{
			DungeonMissionType.Gathering => "探索出撃",
			DungeonMissionType.Survey => "迷宮調査",
			_ => "潜行",
		};
		LogRequested.Invoke($"[color=gray]{mission.Field.Name}への{missionName}を取り消した。部隊は待機に戻った。[/color]");
		StateChanged.Invoke();
	}

	// ==================== 表示文言・ヘルパー ====================

	public static string GimmickLabel(BossGimmickType type) => type switch
	{
		BossGimmickType.Poison => "猛毒",
		BossGimmickType.HeavyArmor => "重装甲",
		BossGimmickType.Flying => "飛行",
		BossGimmickType.InstantKill => "即死級攻撃",
		_ => type.ToString(),
	};

	private static string GimmickDescription(BossGimmickType type) => type switch
	{
		BossGimmickType.Poison => "体力を蝕む毒を撒き散らし、長引くほど部隊が削られる。",
		BossGimmickType.HeavyArmor => "分厚い装甲に覆われ、生半可な攻撃は通らない。",
		BossGimmickType.Flying => "空を舞い、地上からの攻撃がなかなか届かない。",
		BossGimmickType.InstantKill => "対策なしで受ければ、一撃で戦闘不能域まで追い込まれる。",
		_ => "",
	};

	private static string DangerStars(int dangerLevel)
	{
		int filled = Math.Clamp(dangerLevel, 0, 5);
		return new string('★', filled) + new string('☆', 5 - filled);
	}

	public static string TierLabel(IntelTier tier) => tier switch
	{
		IntelTier.Unknown => "未調査",
		IntelTier.Basic => "基本情報",
		IntelTier.Hazards => "危険情報",
		IntelTier.Countermeasures => "対策情報",
		IntelTier.Complete => "完全解析",
		_ => tier.ToString(),
	};

	private static string TierColor(IntelTier tier) => tier switch
	{
		IntelTier.Unknown => "gray",
		IntelTier.Basic => "white",
		IntelTier.Hazards => "yellow",
		IntelTier.Countermeasures => "cyan",
		IntelTier.Complete => "gold",
		_ => "white",
	};

	private static string? NextTierHint(IntelTier tier) => tier switch
	{
		IntelTier.Unknown => $"解析率{ScoutingBalance.IntelTierBasic * 100:F0}%で規模が判明",
		IntelTier.Basic => $"解析率{ScoutingBalance.IntelTierHazards * 100:F0}%で危険ギミックが判明",
		IntelTier.Hazards => $"解析率{ScoutingBalance.IntelTierCountermeasures * 100:F0}%で対策が判明",
		IntelTier.Countermeasures => $"解析率{ScoutingBalance.IntelTierComplete * 100:F0}%で弱点を看破",
		_ => null,
	};

	public static string JobLabel(JobClass job) => job switch
	{
		JobClass.Warrior => "重戦士",
		JobClass.Ranger => "斥候",
		JobClass.Mage => "魔導士",
		JobClass.Cleric => "神官",
		JobClass.Knight => "騎士",
		JobClass.Thief => "盗賊",
		JobClass.Scholar => "学者",
		_ => job.ToString(),
	};

	// ---- 判定数値の開示用の書式（→ 03 §4.2.3「開発・バランス調整期間の特記事項」） ----

	public static string FormatRatio(double ratio) => ratio == double.MaxValue ? "∞" : ratio.ToString("F2");

	public static string TraversalRankLabel(TraversalRank rank) => rank switch
	{
		TraversalRank.Godspeed => "神速進軍",
		TraversalRank.Gale => "疾風進軍",
		TraversalRank.Lightning => "電撃進軍",
		TraversalRank.Swift => "迅速進軍",
		TraversalRank.Normal => "通常進軍",
		_ => "苦戦進軍",
	};

	public static string SurveyOutcomeLabel(SurveyOutcome outcome) => outcome switch
	{
		SurveyOutcome.GreatSuccess => "大成功",
		SurveyOutcome.Success => "成功",
		_ => "失敗",
	};


	/// <summary>
	/// 出撃前の区間別倍率（例：1〜10F×3.0〔解析100%・既踏・被ダメ×0.3〕 → 10〜20F×1.0〔解析0%・未踏破〕）。
	/// 倍率・被ダメ・踏破状況が同じ隣接区間は1つにまとめる。maxCount を超えた分は「ほかN区間」と省略する。
	/// </summary>
	private static string SegmentPreviewText(List<TraversalSegmentDetail> segments, int maxCount = int.MaxValue)
	{
		if (segments.Count == 0)
			return "―";

		var merged = new List<(int From, int To, double Speed, double Damage, bool Unexplored, string Intel)>();
		foreach (var s in segments)
		{
			string intel = $"{s.IntelRate * 100:F0}%";
			if (merged.Count > 0)
			{
				var last = merged[^1];
				if (last.Speed == s.SpeedMultiplier && last.Damage == s.DamageMultiplier && last.Unexplored == s.IsUnexplored)
				{
					merged[^1] = (last.From, s.ToFloor, last.Speed, last.Damage, last.Unexplored, last.Intel);
					continue;
				}
			}
			merged.Add((s.FromFloor, s.ToFloor, s.SpeedMultiplier, s.DamageMultiplier, s.IsUnexplored, intel));
		}

		var shown = merged.Take(maxCount).Select(m =>
			$"{m.From}〜{m.To}F×{m.Speed:F1}〔解析{m.Intel}・{(m.Unexplored ? "未踏破" : "既踏")}" +
			$"{(m.Damage < 1.0 ? $"・被ダメ×{m.Damage:0.0#}" : "")}〕");
		string text = string.Join(" → ", shown);
		return merged.Count > maxCount ? $"{text} → …ほか{merged.Count - maxCount}区間（ボタンのツールチップに全区間）" : text;
	}

	/// <summary>採取スコアの内訳（例：AGI62＋DEX54＋LDR20＋職業10＋田舎育ち23＝169×HP比率0.95）。</summary>
	public static string GatheringBreakdownText(GatheringScoreBreakdown b) =>
		$"AGI{b.AgiPart:F0}＋DEX{b.DexPart:F0}＋LDR{b.LdrPart:F0}＋職業{b.ClassBonus:F0}＋田舎育ち{b.RuralBonus:F0}＝{b.RawScore:F0}×HP比率{b.HpRatio:F2}";

	public static string GuardTierLabel(GuardTier tier) => tier switch
	{
		GuardTier.Abundant => "余裕",
		GuardTier.Sufficient => "十分",
		GuardTier.Marginal => "充足",
		_ => "不足",
	};

	private static string GuardTierColor(GuardTier tier) => tier switch
	{
		GuardTier.Abundant => "lime",
		GuardTier.Sufficient => "cyan",
		GuardTier.Marginal => "orange",
		_ => "red",
	};

	private static string GuardTierDescription(GuardTier tier) => tier switch
	{
		GuardTier.Abundant => "[color=gray]護衛が残党を寄せ付けず、じっくり調べられそうだ。[/color]",
		GuardTier.Sufficient => "[color=gray]護衛は十分。多少の手傷で済みそうだ。[/color]",
		GuardTier.Marginal => "[color=orange]護衛の手が回らず、被弾しながらの調査になりそうだ。[/color]",
		_ => "[color=red]護衛役（戦士・騎士・魔導士等）が不足しており危険。調査隊が潰走しかねない。[/color]",
	};
}
