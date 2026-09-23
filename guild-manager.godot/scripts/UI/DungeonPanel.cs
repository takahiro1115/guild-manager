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
/// 3ゾーン構造（Zone A: 進軍モニター, Zone B: ボス警戒・ポーチ, Zone C: 部隊状況・コマンド）により、
/// 潜行・調査・採取の3大任務および扉前決断（討伐・撤退）の全動線を視覚的かつ堅牢に集約する。
///
/// 週報ログへの書き込み・画面全体の再描画は MainDashboard の責務のため、
/// LogRequested・StateChanged イベントで依頼する。
/// </summary>
public partial class DungeonPanel : ScrollContainer
{
	// ==================== Zone A: 進軍モニター ====================
	private OptionButton _fieldSelector = null!;
	private ProgressBar _dungeonProgressBar = null!;
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
	private GridContainer _gimmickContainer = null!;
	private OptionButton _pouchSlot1 = null!;
	private OptionButton _pouchSlot2 = null!;
	private RichTextLabel _pouchCostLabel = null!;

	// ==================== Zone C: 部隊状況・コマンド ====================
	private OptionButton _squadSelector = null!;
	private HBoxContainer _squadMemberContainer = null!;
	private RichTextLabel _missionPreviewLabel = null!;
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
	/// 選択中の出撃部隊（SavedParty.Id）。週送り・再描画でドロップダウンを作り直しても
	/// 選択が外れないよう、インデックスではなくIdで記憶。
	/// </summary>
	private Guid? _selectedPartyId;

	/// <summary>選択中のダンジョン（フィールド）のId。</summary>
	private string? _selectedFieldId;

	/// <summary>選択中のフィールドの実体キャッシュ。</summary>
	private DungeonField? _selectedField;

	/// <summary>待機中メンバーを1人以上含む出撃部隊が存在するか。</summary>
	private bool _hasSelectableParty;

	/// <summary>週報ログ（右ペイン）への追記を依頼する（BBCode文字列）。</summary>
	public event Action<string> LogRequested = delegate { };

	/// <summary>出撃・取り消し等でゲーム状態が変わったことを通知する。</summary>
	public event Action StateChanged = delegate { };

	public override void _Ready()
	{
		// Zone A
		_fieldSelector = GetNode<OptionButton>("%FieldSelector");
		_dungeonProgressBar = GetNode<ProgressBar>("%DungeonProgressBar");
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
		_squadSelector = GetNode<OptionButton>("%SquadSelector");
		_squadMemberContainer = GetNode<HBoxContainer>("%SquadMemberContainer");
		_missionPreviewLabel = GetNode<RichTextLabel>("%MissionPreviewLabel");
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

		_dungeonProgressBar.MinValue = 0;
		_dungeonProgressBar.MaxValue = DungeonField.MaxFloor;

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
		_squadSelector.ItemSelected += OnPartySelected;
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
		RefreshBossInfo(boss);
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

		var boss = _selectedField.GetNextActiveBoss();
		RefreshProgress(boss);
		RefreshExpeditionLoot();
		RefreshMaterialsInfo();
		RefreshBossInfo(boss);
		RefreshDispatchSection(boss);
	}

	private void RefreshProgress(FloorBoss? boss)
	{
		_floorDisplay.Clear();
		if (_selectedField == null)
		{
			_dungeonProgressBar.Value = 0;
			_floorDisplay.AppendText("[color=gray]ダンジョン情報がありません。[/color]");
			return;
		}

		_dungeonProgressBar.Value = _selectedField.ReachedFloor;

		var allBosses = _state.DungeonFields.SelectMany(f => f.Bosses).ToList();
		int totalCleared = allBosses.Count(b => b.IsDefeated);
		int totalBosses = allBosses.Count;

		var sb = new StringBuilder();
		if (boss == null)
		{
			sb.AppendLine($"[color=gold][b]🏆 {_selectedField.Name} 完全踏破！（到達 {DungeonField.MaxFloor}F）[/b][/color]");
			sb.AppendLine($"大迷宮全体：{totalCleared}/{totalBosses}層 踏破完了");
		}
		else
		{
			sb.AppendLine($"[b]{_selectedField.Name}[/b] 最高到達：[color=cyan][b]{_selectedField.ReachedFloor}[/b][/color]/100F　全体：{totalCleared}/{totalBosses}層");
			sb.AppendLine($"現在目標：第{boss.Floor}層 主「[color=orange]{boss.Name}[/color]」");
		}

		// 潜行中アクティブ部隊のピン位置
		var activeMission = _state.ActiveDungeonMissions
			.FirstOrDefault(m => m.Field.Id == _selectedField.Id && m.MissionType == DungeonMissionType.Scouting);
		if (activeMission != null)
		{
			string statusText = activeMission.Status switch
			{
				ExpeditionStatus.AwaitingBossDecision => "[color=gold]【扉前待機】[/color]",
				ExpeditionStatus.EngagingBoss => "[color=red]【討伐突入】[/color]",
				ExpeditionStatus.Retreating => "[color=gray]【撤退中】[/color]",
				_ => "[color=cyan]【進軍中】[/color]",
			};
			sb.AppendLine($"📍 部隊現在地：{statusText} 第{activeMission.CurrentFloor}層");
		}

		// ボスチェックポイント可視化（到達・撃破済み表示）
		var defeatedCheckpoints = _selectedField.Bosses.Where(b => b.IsDefeated).Select(b => $"{b.Floor}F✔").ToList();
		string checkpointStr = defeatedCheckpoints.Count > 0 ? string.Join(" ", defeatedCheckpoints) : "なし";
		sb.AppendLine($"[color=gray]制覇区間：{checkpointStr}[/color]");

		// 電撃進軍倍率：潜行中は部隊の現在階層、待機中は出発階層（毎回1Fから潜る）の区間担当ボスで求める。
		int speedFloor = activeMission?.CurrentFloor ?? 1;
		var segmentBoss = _selectedField.GetSegmentBoss(speedFloor);
		double speed = DungeonTraversalResolver.IntelSpeedMultiplier(segmentBoss);
		string segmentText = segmentBoss == null
			? "区間担当ボスなし"
			: $"{speedFloor}F→{segmentBoss.Floor}F区間・「{segmentBoss.Name}」解析{segmentBoss.IntelRate * 100:F0}%";
		sb.Append($"[bgcolor=#1e3f20][color=lime][b] ⚡ 進軍速度倍率：×{speed:F1} Speed [/b][/color][/bgcolor] " +
			$"[color=gray]（{(activeMission != null ? "現在地" : "出発地")} {segmentText}：1.0＋解析率×{DungeonTraversalBalance.IntelSpeedBonusPerIntel:0.#}）[/color]");

		_floorDisplay.AppendText(sb.ToString());
	}

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
			slot.AddItem($"{item.Name} ({item.Price}G) [{GimmickLabel(item.TargetGimmick)}対策]");
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
		RefreshBossInfo(boss);
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

	private static readonly BossGimmickType[] MajorGimmickTypes = new[]
	{
		BossGimmickType.Poison,
		BossGimmickType.HeavyArmor,
		BossGimmickType.Flying,
		BossGimmickType.InstantKill,
	};

	private void RefreshBossInfo(FloorBoss? boss)
	{
		ClearGimmickCards();
		_bossHeaderLabel.Clear();
		_intelTierLabel.Clear();
		_completeBadgeLabel.Clear();

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
			_bossHpBar.Value = 0;
			_bossHpLabel.Text = "-";
			_intelProgressBar.Value = 0;
			_intelPercentLabel.Text = "-";
			_completeBadgeLabel.Visible = false;
			PopulateGimmickCards(null, IntelTier.Unknown, previewParty);
			return;
		}

		var tier = ScoutingResolver.GetTier(boss.IntelRate);

		_bossHeaderLabel.AppendText(
			$"[font_size=15][b]第{boss.Floor}層の主「{boss.Name}」[/b][/font_size]\n" +
			$"出現階層：第{boss.Floor}層");

		if (tier < IntelTier.Basic)
		{
			_bossHpBar.MinValue = 0;
			_bossHpBar.MaxValue = 100;
			_bossHpBar.Value = 0;
			_bossHpLabel.Text = "？？？ / ？？？";
		}
		else
		{
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

	private void PopulateGimmickCards(FloorBoss? boss, IntelTier tier, Party previewParty)
	{
		ClearGimmickCards();

		foreach (var type in MajorGimmickTypes)
		{
			string title = GimmickLabel(type);
			// カードの高さはここで決め打ちする（→ 03 §9。Zone B 全体を約340px以内に収め、
			// Zone C の出撃コマンドをスクロール無しで画面内に残すため）。
			var card = new PanelContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill,
				CustomMinimumSize = new Vector2(160, 58),
			};
			var margin = new MarginContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill,
			};
			margin.AddThemeConstantOverride("margin_left", 6);
			margin.AddThemeConstantOverride("margin_top", 2);
			margin.AddThemeConstantOverride("margin_right", 6);
			margin.AddThemeConstantOverride("margin_bottom", 2);
			card.AddChild(margin);

			// FitContent は使わない（2026年9月のレイアウト適正化、→ 03 §9）。
			// RichTextLabel の最小幅は0のため、FitContent=true だと Godot は「幅ほぼ0で折り返した
			// 場合の高さ」を最小高さとして報告する。4枚のカードでこれが積み上がり、Zone B の
			// 最小高さが 1100px を超えて Zone C（出撃コマンド）を画面外へ押し出していた。
			// 代わりにカード側の固定高さ（下の CustomMinimumSize）に収め、はみ出す分は
			// スクロールせずに切り詰める（ScrollActive=false）。
			var label = new RichTextLabel
			{
				BbcodeEnabled = true,
				FitContent = false,
				ScrollActive = false,
				CustomMinimumSize = new Vector2(150, 0),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill,
			};
			label.AddThemeFontSizeOverride("normal_font_size", 11);

			var sb = new StringBuilder();
			sb.Append($"[b]【{title}】[/b] ");

			if (boss == null)
			{
				sb.Append("[color=gray]対象なし[/color]\n");
				sb.Append("[color=lime]✔ 安全[/color]");
			}
			else if (tier < IntelTier.Hazards)
			{
				sb.Append("[color=gray]危険度 ？？？[/color]\n");
				sb.Append("[color=yellow]？ 未解析（解析率50%で開示）[/color]");
			}
			else
			{
				var gimmick = boss.Gimmicks.FirstOrDefault(g => g.Type == type);
				if (gimmick != null)
				{
					sb.Append($"危険度 [color=orange]{DangerStars(gimmick.DangerLevel)}[/color]\n");

					if (tier >= IntelTier.Countermeasures)
					{
						var routes = CounterRoutes(gimmick);
						string routeStr = routes.Count > 0 ? string.Join("／", routes) : "有効な対策なし";
						sb.Append($"[color=cyan]対策：{routeStr}[/color]\n");
					}
					else
					{
						sb.Append("[color=gray]対策条件：？？？（解析率75%で開示）[/color]\n");
					}

					bool countered = DungeonResolver.IsCountered(gimmick, previewParty);
					if (countered)
					{
						sb.Append("[color=lime][b]✔ 対策充足[/b][/color]");
					}
					else
					{
						if (type == BossGimmickType.InstantKill)
							sb.Append("[color=red][b]☠ 即死級 未対策（壊滅リスク）[/b][/color]");
						else if (type == BossGimmickType.HeavyArmor)
							sb.Append("[color=red][b]✖ 重装甲 未対策（被ダメ増）[/b][/color]");
						else
							sb.Append("[color=red][b]✖ 未対策[/b][/color]");
					}
				}
				else
				{
					sb.Append("[color=gray]能力なし[/color]\n");
					sb.Append("[color=lime][b]✔ 安全（この能力は不所持）[/b][/color]");
				}
			}

			label.AppendText(sb.ToString());
			margin.AddChild(label);
			_gimmickContainer.AddChild(card);
		}
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

	private void RefreshPartyOptions()
	{
		_squadSelector.Clear();
		_squadSelector.AddItem("（出撃部隊を選択）");

		_hasSelectableParty = false;
		int selectIndex = 0;
		for (int i = 0; i < _state.SavedParties.Count; i++)
		{
			var saved = _state.SavedParties[i];
			int available = PartyFormationSystem.BuildDispatchParty(_state, saved.MemberIds).Members.Count;
			int total = saved.MemberIds.Count(id => _state.Adventurers.Any(a => a.Id == id));

			int itemIndex = i + 1;
			_squadSelector.AddItem($"{saved.Name}（待機中 {available}/{total}名）");
			_squadSelector.SetItemDisabled(itemIndex, available == 0);
			if (available > 0)
				_hasSelectableParty = true;

			if (saved.Id == _selectedPartyId && available > 0)
				selectIndex = itemIndex;
		}

		_squadSelector.Select(selectIndex);
		if (selectIndex == 0)
			_selectedPartyId = null;
	}

	private void OnPartySelected(long index)
	{
		_selectedPartyId = index >= 1 && index - 1 < _state.SavedParties.Count
			? _state.SavedParties[(int)index - 1].Id
			: null;

		if (_selectedPartyId != null)
		{
			_pendingMissionList.DeselectAll();
			_cancelMissionButton.Disabled = true;
		}

		var boss = _selectedField?.GetNextActiveBoss();
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

		for (int i = 0; i < 4; i++)
		{
			var card = new PanelContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				CustomMinimumSize = new Vector2(90, 64),
			};
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
				nameLabel.AddThemeFontSizeOverride("font_size", 11);
				nameLabel.AddThemeColorOverride("font_color", isAvailable ? new Color(1, 1, 1) : new Color(0.6f, 0.6f, 0.6f));
				vbox.AddChild(nameLabel);

				// 職業・前後衛
				var badgeRow = new HBoxContainer();
				badgeRow.AddThemeConstantOverride("separation", 4);

				var jobLabel = new Label
				{
					Text = JobLabel(adv.JobClass),
				};
				jobLabel.AddThemeFontSizeOverride("font_size", 10);
				jobLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.85f, 0.9f));
				badgeRow.AddChild(jobLabel);

				var posBadge = new Label
				{
					Text = isFront ? "【前衛】" : "【後衛】",
				};
				posBadge.AddThemeFontSizeOverride("font_size", 10);
				posBadge.AddThemeColorOverride("font_color", isFront
					? new Color(0.4f, 0.9f, 1.0f)
					: new Color(0.85f, 0.65f, 1.0f));
				badgeRow.AddChild(posBadge);
				vbox.AddChild(badgeRow);

				// HPバー＆数値
				var hpRow = new HBoxContainer();
				hpRow.AddThemeConstantOverride("separation", 4);
				var hpBar = new ProgressBar
				{
					MinValue = 0,
					MaxValue = Math.Max(1, adv.MaxHP),
					Value = adv.CurrentHP,
					ShowPercentage = false,
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					SizeFlagsVertical = SizeFlags.ShrinkCenter,
					CustomMinimumSize = new Vector2(0, 6),
				};
				hpRow.AddChild(hpBar);

				var hpLabel = new Label
				{
					Text = adv.CurrentHP == 0 ? "重傷" : isDispatched ? "出撃中" : $"{adv.CurrentHP}/{adv.MaxHP}",
				};
				hpLabel.AddThemeFontSizeOverride("font_size", 10);
				hpLabel.AddThemeColorOverride("font_color", adv.CurrentHP == 0 ? new Color(1f, 0.3f, 0.3f) : new Color(0.9f, 0.9f, 0.9f));
				hpRow.AddChild(hpLabel);
				vbox.AddChild(hpRow);
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
				emptyLabel.AddThemeFontSizeOverride("font_size", 10);
				emptyLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
				hbox.AddChild(emptyLabel);
			}

			_squadMemberContainer.AddChild(card);
		}
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

	private void RefreshDispatchSection(FloorBoss? boss)
	{
		RefreshSquadMemberCards();
		_missionPreviewLabel.Clear();
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
					?? "出撃部隊が選択されていません。上のドロップダウンから編成を選択してください。";
			_dispatchStatusLabel.AppendText($"[color=gray]{reason}[/color]");
			_missionPreviewLabel.AppendText("[color=gray]出撃部隊を選択すると、潜行・調査・採取の各見立てが表示されます。[/color]");
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

		if (blockedReason != null)
			_dispatchStatusLabel.AppendText($"[color=gray]{blockedReason}[/color]");

		var sb = new StringBuilder();

		// 1. 潜行見立て
		if (_selectedField != null && boss != null)
		{
			double score = DungeonTraversalResolver.CalculateTraversalScore(party, _state);
			double requirement = DungeonTraversalResolver.FloorRequirement(1);
			double ratio = requirement <= 0 ? double.MaxValue : score / requirement;
			var rank = DungeonTraversalResolver.ClassifyRatio(ratio);
			var (rankLossMin, rankLossMax) = DungeonTraversalResolver.RankHpLossRange(rank);
			var segments = DungeonTraversalResolver.DescribeSegments(_selectedField, 1, boss.Floor, _selectedField.ReachedFloor);

			string rankView = rank switch
			{
				TraversalRank.Lightning => "[color=lime]電撃的に奥まで進めそうだ[/color]",
				TraversalRank.Swift => "[color=cyan]迅速に奥へ進めそうだ[/color]",
				TraversalRank.Normal => "[color=cyan]着実に奥へ進めそうだ[/color]",
				_ => "[color=orange]手こずりながらも、少しは奥へ進めるだろう[/color]",
			};

			// Zone C は縦スクロール不要の収容（→ 03 §0.18）を守るため、潜行の見立ては3行に収める。
			sb.AppendLine($"[b]🏃 大迷宮へ潜行（進軍）[/b]→ 第{boss.Floor}層「{boss.Name}」扉前　見立て：{rankView}");
			sb.AppendLine($"　部隊走破力: [color=cyan]{score:F0}[/color] pt（要求: {requirement:F0}〔1F×{DungeonTraversalBalance.RequirementPerFloor}〕／比率: {FormatRatio(ratio)}）" +
				$"→ {TraversalRankLabel(rank)}：予算{DungeonTraversalResolver.FloorsAdvanced(rank)}階層／既踏の損耗{rankLossMin}〜{rankLossMax}%・未踏破の損耗{DungeonBalance.UnexploredHpLossPctMin}〜{DungeonBalance.UnexploredHpLossPctMax}%");
			sb.AppendLine($"　実効速度（区間別）: {SegmentPreviewText(segments, MaxPreviewSegments)}");

			_scoutingButton.TooltipText =
				$"区間別の走破倍率：{SegmentPreviewText(segments)}\n" +
				$"1階層ごとに 1÷区間倍率 の予算を消費して進む（区間倍率＝1.0＋区間担当ボスの解析率×{DungeonTraversalBalance.IntelSpeedBonusPerIntel:0.#}）。\n" +
				$"損耗は階層ごとに積み上げる：その階層の基礎率（既踏／未踏破）÷歩いた階層数×区間の被ダメ倍率（完全解析区間は×{DungeonTraversalBalance.FullIntelDamageMultiplier:0.0#}）。\n" +
				$"走破力の総合値（Σ(VIT×{DungeonTraversalBalance.WeightVit:0.#}＋MND×{DungeonTraversalBalance.WeightMnd:0.#})＋部隊長LDR補正）：{score:F0}\n" +
				"道中進軍は低リスク：HPは減っても強制除籍にはならない。\n" +
				"道中で拾った素材・ゴールドは、ギルドへ帰還した時点で格納される。";
		}

		// 2. 調査見立て
		if (boss != null && !fullyAnalyzed)
		{
			var tier = ScoutingResolver.PreviewGuardTier(party, boss);
			double guardPower = ScoutingResolver.CalculateGuardPower(party);
			double reqGuard = ScoutingResolver.RequiredGuardPower(boss);
			double guardRatio = reqGuard <= 0 ? double.MaxValue : guardPower / reqGuard;
			var (carrier, carrierStat, carrierValue) = ScoutingResolver.FindGuardCarrier(party);
			double stealth = ScoutingResolver.CalculateStealthScore(party);
			double stealthReq = ScoutingResolver.StealthRequirement(boss);
			double analysis = ScoutingResolver.CalculateAnalysisScore(party);
			double analysisReq = ScoutingResolver.AnalysisRequirement(boss);
			double analysisRatio = analysisReq <= 0 ? double.MaxValue : analysis / analysisReq;

			sb.AppendLine($"[b]🔍 迷宮調査に出撃（解析）[/b]　護衛評価：[color={GuardTierColor(tier)}][b]{GuardTierLabel(tier)}[/b][/color] {GuardTierDescription(tier)}");
			sb.AppendLine($"　部隊護衛力: [color=cyan]{guardPower:F0}[/color]（{carrier?.Name}:{carrierStat}{carrierValue:F0}）／要求: {reqGuard:F1}〔{ScoutingBalance.BaseRequiredGuardPower}×{boss.Floor}F÷10〕" +
				$"（比率: {FormatRatio(guardRatio)}）［{GuardTierLabel(tier)}: 解析×{ScoutingResolver.GuardIntelMultiplier(tier):0.0#} / HP損耗{ScoutingResolver.GuardHpLossPercent(tier)}%］");
			sb.AppendLine($"　隠密: {stealth:F0} / 要求 {stealthReq:F0}（{(stealth >= stealthReq ? "成功見込み" : "発見され解析1段階低下")}）　" +
				$"解析: {analysis:F0} / 要求 {analysisReq:F0}（比率: {FormatRatio(analysisRatio)} → {SurveyOutcomeLabel(ScoutingResolver.ClassifyAnalysis(analysisRatio))}）");

			string tooltip =
				$"護衛力（隊員の中で最も高いSTR・VIT・INT）：{guardPower:F0}\n" +
				$"護衛評価：{GuardTierLabel(tier)}（解析成果 ×{ScoutingResolver.GuardIntelMultiplier(tier):F2}、" +
				$"各員のHP消費 最大HPの{ScoutingResolver.GuardHpLossPercent(tier)}%）\n" +
				"調査は低リスク：HPは減っても強制除籍にはならない。";
			if (tier == GuardTier.Deficient)
			{
				tooltip = "⚠ 護衛役（戦士・騎士・魔導士等）が不足しており危険。魔物の残党に強襲されて潰走し、" +
					"解析の成果を持ち帰れないうえ大きな手傷を負う。\n" + tooltip;
				_surveyButton.AddThemeColorOverride("font_color", new Color(1f, 0.45f, 0.3f));
			}
			_surveyButton.TooltipText = tooltip;
		}

		// 3. 採取見立て
		if (_selectedField != null)
		{
			double gatheringScore = GatheringResolver.CalculateGatheringScore(party);
			var breakdown = GatheringResolver.BreakDownGatheringScore(party);
			var eligible = MaterialBalance.GetEligibleMaterials(_selectedField.Id, _selectedField.ReachedFloor);
			string baseYieldText = eligible.Count == 0
				? "0（採れる素材なし）"
				: eligible.Min(m => m.BaseYield) == eligible.Max(m => m.BaseYield)
					? $"{eligible[0].BaseYield}"
					: $"{eligible.Min(m => m.BaseYield)}〜{eligible.Max(m => m.BaseYield)}";
			int scoreYield = GatheringResolver.ScoreYield(gatheringScore);
			int floorYield = GatheringResolver.FloorYield(_selectedField);
			int researchYield = GatheringResolver.ResearchYield(_state);
			int relicPct = RelicBalance.GetGatheringDropPercent(gatheringScore, _selectedField.ReachedFloor);

			sb.AppendLine("[b]🌿 探索出撃（素材採取）[/b]");
			sb.AppendLine($"　部隊採取力: [color=lime]{gatheringScore:F0}[/color] pt (基礎: {breakdown.BaseTotal:F0} + 田舎育ち: +{breakdown.RuralTotal:F0}pt)〔{GatheringBreakdownText(breakdown)}〕");
			sb.Append($"　基本枠: 素材基礎{baseYieldText}＋スコア枠{scoreYield}〔{gatheringScore:F0}÷{GatheringBalance.MaterialYieldDivisor}〕" +
				$"＋階層枠{floorYield}〔{_selectedField.ReachedFloor}F÷{GatheringBalance.ReachedFloorDivisor}〕＋研究{researchYield}" +
				$" ／ 遺物発見率: {relicPct}% ／ 換金: {Math.Round(gatheringScore * GatheringBalance.GoldPerScore):F0}G ／ HP損耗{GatheringBalance.HpLossPctMin}〜{GatheringBalance.HpLossPctMax}%");
			_gatheringButton.TooltipText =
				$"採取の総合値（AGI+DEX合計＋部隊長LDR補正、部隊のHP比率で減衰）：{gatheringScore:F0}\n" +
				"探索は低リスク：HPは減っても強制除籍にはならない。";
		}

		_missionPreviewLabel.AppendText(sb.ToString());
	}

	private string? GetDispatchBlockedReason(FloorBoss? boss, SavedParty? saved, Party party)
	{
		if (boss == null) return "挑むべき階層ボスがいない。";
		if (_state.DefeatReason != null) return "ギルドは既に解散した。";
		if (saved == null) return "出撃部隊が選択されていない。上のドロップダウンから編成を選ぶこと。";
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
		if (saved == null) return "出撃部隊が選択されていない。上のドロップダウンから編成を選ぶこと。";
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
					: "出撃部隊が選択されていない。上のドロップダウンから編成を選ぶこと。");
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
		LogRequested.Invoke($"[color=cyan]第{_state.WeekNumber}週：「{saved.Name}」（{members}）が{_selectedField.Name}の" +
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
					: "出撃部隊が選択されていない。上のドロップダウンから編成を選ぶこと。");
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
		LogRequested.Invoke($"[color=cyan]第{_state.WeekNumber}週：「{saved.Name}」（{members}）が{_selectedField.Name} 第{boss.Floor}層" +
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
				: "出撃部隊が選択されていない。上のドロップダウンから編成を選ぶこと。");
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
		LogRequested.Invoke($"[color=cyan]第{_state.WeekNumber}週：「{saved.Name}」（{members}）が{_selectedField.Name}へ" +
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
		LogRequested.Invoke($"[color=gold][b]⚔ 第{_state.WeekNumber}週：{members}が第{boss!.Floor}層「{boss.Name}」の扉を開き、" +
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
		LogRequested.Invoke($"[color=cyan]🏃 第{_state.WeekNumber}週：{members}は{mission.Field.Name} 第{mission.CurrentFloor}層から撤退し、" +
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

	/// <summary>Zone C の1行に並べる区間数の上限（超えた分はツールチップへ。縦スクロール不要の収容、→ 03 §0.18）。</summary>
	private const int MaxPreviewSegments = 3;

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
