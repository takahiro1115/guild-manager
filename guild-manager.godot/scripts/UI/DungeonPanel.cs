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
/// Core層の調査任務（ScoutingResolver）・階層ボス討伐（DungeonResolver）を、
/// DungeonExpeditionSystem 経由でゲーム進行へ接続するビュー。表示するのは以下の3つ：
///  - ボス情報：解析率（IntelRate）に応じて段階的に開示する（→ IntelTier）。
///  - 出撃フロー：部隊を選び、1階層からの潜行（道中進軍）または探索（採取）へ送り出す。
///  - 潜行中の部隊の一覧と、扉前に着いた部隊への指令（ボス討伐に挑む／撤退・帰還する）。
///    （→ 毎回1Fリセット・複数週潜行型、ExpeditionStatus）
///
/// 週報ログへの書き込み・画面全体の再描画は MainDashboard の責務のため、
/// LogRequested・StateChanged イベントで依頼する（ポップアップ群の Closed/Applied と同じ流儀）。
///
/// 情報公開の原則（→ コミットd7c7f39）：判定用の要求値・閾値は表示しない。部隊の隠密・解析の
/// 総合値（＝メンバーの能力値の合計で、プレイヤーが既に見られる値）と、斥候の見立て（定性表現）だけを出す。
/// </summary>
public partial class DungeonPanel : ScrollContainer
{
	private OptionButton _fieldOptionButton = null!;
	private RichTextLabel _progressLabel = null!;
	private RichTextLabel _materialsLabel = null!;
	private RichTextLabel _bossHeaderLabel = null!;
	private ProgressBar _intelProgressBar = null!;
	private Label _intelPercentLabel = null!;
	private RichTextLabel _intelTierLabel = null!;
	private RichTextLabel _completeBadgeLabel = null!;
	private VBoxContainer _gimmickCards = null!;
	private OptionButton _partyOptionButton = null!;
	private RichTextLabel _partyMembersLabel = null!;
	private RichTextLabel _scoutingForecastLabel = null!;
	private Button _scoutingButton = null!;
	private RichTextLabel _countermeasureLabel = null!;
	private OptionButton _pouchSlot1OptionButton = null!;
	private OptionButton _pouchSlot2OptionButton = null!;
	private RichTextLabel _pouchCostLabel = null!;
	private PanelContainer _bossDecisionPanel = null!;
	private RichTextLabel _bossDecisionLabel = null!;
	private Button _engageBossButton = null!;
	private Button _retreatButton = null!;
	private Button _gatheringButton = null!;
	private RichTextLabel _dispatchStatusLabel = null!;
	private ItemList _pendingMissionList = null!;
	private Button _cancelMissionButton = null!;

	private GameState _state;
	private DungeonExpeditionSystem _expeditionSystem;

	/// <summary>
	/// 選択中の出撃部隊（SavedParty.Id）。週送り・再描画でドロップダウンを作り直しても
	/// 選択が外れないよう、インデックスではなくIdで覚えておく。
	/// </summary>
	private Guid? _selectedPartyId;

	/// <summary>
	/// 選択中のダンジョン（フィールド）のId（→ 大迷宮フィールド選択UI仕様）。
	/// _selectedPartyIdと同じ理由でIdで覚えておく。nullは「未選択（初回表示前）」を表し、
	/// その場合はRefreshFieldOptionsがアクティブフィールド（GetActiveField）を既定選択にする。
	/// </summary>
	private string _selectedFieldId;

	/// <summary>選択中のフィールドの実体（_selectedFieldIdから毎回引き直す代わりにキャッシュしておく）。</summary>
	private DungeonField _selectedField;

	/// <summary>待機中メンバーを1人以上含む（＝選択可能な）出撃部隊が存在するか（→ RefreshPartyOptionsで再評価）。</summary>
	private bool _hasSelectableParty;

	/// <summary>週報ログ（右ペイン）への追記を依頼する（BBCode文字列）。</summary>
	public event Action<string> LogRequested = delegate { };

	/// <summary>出撃・取り消しでゲーム状態が変わったことを通知する（MainDashboardが全体を再描画する）。</summary>
	public event Action StateChanged = delegate { };

	public override void _Ready()
	{
		_fieldOptionButton = GetNode<OptionButton>("%FieldOptionButton");
		_progressLabel = GetNode<RichTextLabel>("%ProgressLabel");
		_materialsLabel = GetNode<RichTextLabel>("%MaterialsLabel");
		_bossHeaderLabel = GetNode<RichTextLabel>("%BossHeaderLabel");
		_intelProgressBar = GetNode<ProgressBar>("%IntelProgressBar");
		_intelPercentLabel = GetNode<Label>("%IntelPercentLabel");
		_intelTierLabel = GetNode<RichTextLabel>("%IntelTierLabel");
		_completeBadgeLabel = GetNode<RichTextLabel>("%CompleteBadgeLabel");
		_gimmickCards = GetNode<VBoxContainer>("%GimmickCards");
		_partyOptionButton = GetNode<OptionButton>("%PartyOptionButton");
		_partyMembersLabel = GetNode<RichTextLabel>("%PartyMembersLabel");
		_scoutingForecastLabel = GetNode<RichTextLabel>("%ScoutingForecastLabel");
		_scoutingButton = GetNode<Button>("%ScoutingButton");
		_countermeasureLabel = GetNode<RichTextLabel>("%CountermeasureLabel");
		_pouchSlot1OptionButton = GetNode<OptionButton>("%PouchSlot1OptionButton");
		_pouchSlot2OptionButton = GetNode<OptionButton>("%PouchSlot2OptionButton");
		_pouchCostLabel = GetNode<RichTextLabel>("%PouchCostLabel");
		_bossDecisionPanel = GetNode<PanelContainer>("%BossDecisionPanel");
		_bossDecisionLabel = GetNode<RichTextLabel>("%BossDecisionLabel");
		_engageBossButton = GetNode<Button>("%EngageBossButton");
		_retreatButton = GetNode<Button>("%RetreatButton");
		_gatheringButton = GetNode<Button>("%GatheringButton");
		_dispatchStatusLabel = GetNode<RichTextLabel>("%DispatchStatusLabel");
		_pendingMissionList = GetNode<ItemList>("%PendingMissionList");
		_cancelMissionButton = GetNode<Button>("%CancelMissionButton");

		_intelProgressBar.MinValue = 0;
		_intelProgressBar.MaxValue = 100;

		_fieldOptionButton.ItemSelected += OnFieldSelected;
		_partyOptionButton.ItemSelected += OnPartySelected;
		_scoutingButton.Pressed += OnDispatchPressed;
		_engageBossButton.Pressed += OnEngageBossPressed;
		_retreatButton.Pressed += OnRetreatPressed;
		_gatheringButton.Pressed += OnGatheringDispatchPressed;
		_pendingMissionList.ItemSelected += _ => OnPendingMissionSelected();
		_cancelMissionButton.Pressed += OnCancelMissionPressed;
		// タブを表示した時点でも、待機中メンバーの状態から出撃可能な部隊一覧を再評価する
		// （週次決算で帰還した部隊がドロップダウンに反映されないまま残るのを防ぐ）。
		VisibilityChanged += () =>
		{
			if (IsVisibleInTree() && _state != null)
				Refresh(_state);
		};

		PopulatePouchSlot(_pouchSlot1OptionButton);
		PopulatePouchSlot(_pouchSlot2OptionButton);
		_pouchSlot1OptionButton.ItemSelected += _ => OnPouchChanged();
		_pouchSlot2OptionButton.ItemSelected += _ => OnPouchChanged();
	}

	/// <summary>ポーチの選択変更：出撃前の下見（対策の充足状況）と扉前の指令エリアの両方を描き直す。</summary>
	private void OnPouchChanged()
	{
		RefreshDispatchSection(_selectedField?.GetNextActiveBoss());
		RefreshBossDecision();
	}

	/// <summary>
	/// ボス討伐専用の携行ポーチ（2026年9月新設、→ パーティ携行アイテムポーチ仕様）。
	/// 大迷宮ボスのギミック対策口（→ BossGimmick.RequiredItemId）を持つ4種のみを選択肢にする
	/// （煙幕弾・高品質傷薬・携帯糧食など、通常クエスト向けの効果アイテムはここでは扱わない）。
	/// </summary>
	private static readonly string[] PouchItemIds =
	{
		ConsumableCatalog.AntidoteId, ConsumableCatalog.AcidFlaskId,
		ConsumableCatalog.NetId, ConsumableCatalog.CharmId,
	};

	private void PopulatePouchSlot(OptionButton slot)
	{
		slot.Clear();
		slot.AddItem("なし (0G)");
		foreach (var itemId in PouchItemIds)
		{
			var item = ConsumableCatalog.FindById(itemId)!;
			slot.AddItem($"{item.Name} ({item.Price}G) [{GimmickLabel(GimmickCounteredBy(itemId))}対策]");
		}
	}

	/// <summary>指定アイテムが対策口になっているギミック種別（→ Data.SampleData.CreateGimmick）。表示ラベル用。</summary>
	private static BossGimmickType GimmickCounteredBy(string itemId) => itemId switch
	{
		ConsumableCatalog.AntidoteId => BossGimmickType.Poison,
		ConsumableCatalog.AcidFlaskId => BossGimmickType.HeavyArmor,
		ConsumableCatalog.NetId => BossGimmickType.Flying,
		_ => BossGimmickType.InstantKill, // Charm
	};

	/// <summary>現在選択中のポーチ2枠のアイテムId一覧（「なし」は含めない）。</summary>
	private List<string> GetSelectedPouchItemIds()
	{
		var ids = new List<string>();
		foreach (var slot in new[] { _pouchSlot1OptionButton, _pouchSlot2OptionButton })
		{
			int index = slot.Selected;
			if (index >= 1 && index - 1 < PouchItemIds.Length)
				ids.Add(PouchItemIds[index - 1]);
		}
		return ids;
	}

	private void ResetPouchSelection()
	{
		_pouchSlot1OptionButton.Select(0);
		_pouchSlot2OptionButton.Select(0);
	}

	/// <summary>出撃の派遣・取り消しに使うシステムを受け取る（MainDashboard._Readyから1回だけ呼ぶ）。</summary>
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
		RefreshMaterialsInfo();
		RefreshBossInfo(boss);
		RefreshPartyOptions();
		RefreshDispatchSection(boss);
		RefreshPendingMissions();
	}

	// ==================== ダンジョン（フィールド）選択 ====================

	/// <summary>
	/// ダンジョン選択ドロップダウン。state.DungeonFieldsをOrder順に並べ、未開放のフィールドは
	/// 「？？？（未開放）」と表示して選択不可にする。既定選択はアクティブフィールド
	/// （開放済みかつ未制覇の最も若いOrder、→ GameState.GetActiveField）。
	/// 一度プレイヤーが選び直したフィールドは、そのIdが有効な限り週送りをまたいで維持する
	/// （_selectedPartyIdと同じ流儀）。
	/// </summary>
	private void RefreshFieldOptions()
	{
		var fields = _state.DungeonFields.OrderBy(f => f.Order).ToList();
		_fieldOptionButton.Clear();

		if (fields.Count == 0)
		{
			_selectedField = null;
			_selectedFieldId = null;
			return;
		}

		// 全フィールド制覇済み（GetActiveFieldがnull）の場合は、最も深いフィールドを既定にする
		// （踏破済みの様子を確認できるようにするため）。
		string defaultFieldId = _state.GetActiveField()?.Id
			?? fields.LastOrDefault(f => f.IsUnlocked)?.Id
			?? fields[0].Id;

		int selectIndex = 0;
		for (int i = 0; i < fields.Count; i++)
		{
			var field = fields[i];
			_fieldOptionButton.AddItem(field.IsUnlocked
				? $"{field.Name}（到達: {field.ReachedFloor}/{DungeonField.MaxFloor}F）"
				: "？？？（未開放）");
			_fieldOptionButton.SetItemDisabled(i, !field.IsUnlocked);

			bool isKeptSelection = _selectedFieldId != null && field.Id == _selectedFieldId && field.IsUnlocked;
			bool isDefaultSelection = _selectedFieldId == null && field.Id == defaultFieldId;
			if (isKeptSelection || isDefaultSelection)
				selectIndex = i;
		}

		_fieldOptionButton.Select(selectIndex);
		_selectedField = fields[selectIndex];
		_selectedFieldId = _selectedField.Id;
	}

	private void OnFieldSelected(long index)
	{
		var fields = _state.DungeonFields.OrderBy(f => f.Order).ToList();
		if (index < 0 || index >= fields.Count || !fields[(int)index].IsUnlocked)
			return; // 未開放の項目はSetItemDisabledで選択自体をブロックしているが、念のため防御しておく

		_selectedField = fields[(int)index];
		_selectedFieldId = _selectedField.Id;

		var boss = _selectedField.GetNextActiveBoss();
		RefreshProgress(boss);
		RefreshMaterialsInfo();
		RefreshBossInfo(boss);
		RefreshDispatchSection(boss);
	}

	/// <summary>
	/// 選択中フィールドで獲得可能な素材と、ギルドの現在の在庫数を表示する
	/// （→ Balance.MaterialBalance、第3の任務「探索（採取）」仕様）。
	/// </summary>
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

	// ==================== 表示：踏破状況・ボス情報 ====================

	private void RefreshProgress(FloorBoss boss)
	{
		var allBosses = _state.DungeonFields.SelectMany(f => f.Bosses).ToList();
		int total = allBosses.Count;
		int cleared = allBosses.Count(b => b.IsDefeated);

		_progressLabel.Clear();
		if (total == 0)
			_progressLabel.AppendText("[color=gray]大迷宮の情報がまだ届いていない。[/color]");
		else if (_selectedField == null)
			_progressLabel.AppendText("[color=gray]表示できるダンジョンがない。[/color]");
		else if (boss == null)
			_progressLabel.AppendText($"[color=gold][b]🏆 {_selectedField.Name}は完全踏破された！" +
				$"（{_selectedField.ReachedFloor}/{DungeonField.MaxFloor}F）[/b][/color]　全体 {cleared}/{total}層踏破");
		else
			_progressLabel.AppendText($"[b]{_selectedField.Name}[/b]：第{boss.Floor}層に挑戦中／" +
				$"到達{_selectedField.ReachedFloor}/{DungeonField.MaxFloor}層　全体 {cleared}/{total}層踏破");
	}

	/// <summary>
	/// ボス情報エリア。解析率の段階（→ ScoutingResolver.GetTier）に応じて開示内容を出し分ける：
	///  - 基本情報未満：名前と階層のみ。最大HP・概要は「？？？」。
	///  - 基本情報：最大HPが判明。危険の正体はまだ分からない。
	///  - 危険情報：ギミック種別（猛毒・重装甲など）が判明。
	///  - 対策情報：各ギミックの対策（職業・能力・携行品）まで判明。
	///  - 完全解析：弱点看破のバッジを表示（討伐時の与ダメージ補正）。
	/// </summary>
	private void RefreshBossInfo(FloorBoss boss)
	{
		ClearGimmickCards();
		_bossHeaderLabel.Clear();
		_intelTierLabel.Clear();
		_completeBadgeLabel.Clear();

		if (boss == null)
		{
			_bossHeaderLabel.AppendText(_selectedField != null
				? $"[color=gold][b]🏆 このダンジョンは完全踏破されました（{_selectedField.ReachedFloor}/{DungeonField.MaxFloor}F）。[/b][/color]"
				: "[color=gray]挑むべき階層ボスはいない。[/color]");
			_intelProgressBar.Value = 0;
			_intelPercentLabel.Text = "-";
			_completeBadgeLabel.Visible = false;
			return;
		}

		var tier = ScoutingResolver.GetTier(boss.IntelRate);
		string maxHp = tier >= IntelTier.Basic ? boss.MaxHp.ToString() : "？？？";

		_bossHeaderLabel.AppendText(
			$"[font_size=18][b]第{boss.Floor}層の主「{boss.Name}」[/b][/font_size]\n" +
			$"出現階層：第{boss.Floor}層　　最大HP：{maxHp}");

		_intelProgressBar.Value = Math.Round(boss.IntelRate * 100);
		_intelPercentLabel.Text = $"{boss.IntelRate * 100:F0}%";

		_intelTierLabel.AppendText($"解析段階：[color={TierColor(tier)}][b]{TierLabel(tier)}[/b][/color]");
		string nextHint = NextTierHint(tier);
		if (nextHint != null)
			_intelTierLabel.AppendText($"　[color=gray]（{nextHint}）[/color]");

		_completeBadgeLabel.Visible = tier == IntelTier.Complete;
		if (tier == IntelTier.Complete)
		{
			_completeBadgeLabel.AppendText(
				$"[bgcolor=#5a4500][color=gold][b] ◆ 完全解析済み：弱点看破により討伐時の与ダメージ+{DungeonBalance.FullIntelDamageBonus * 100:F0}% ボーナス中 [/b][/color][/bgcolor]");
		}

		if (tier < IntelTier.Hazards)
		{
			string overview = tier == IntelTier.Unknown
				? "ボス概要：？？？"
				: "ボス概要：何らかの危険な能力を持っている気配がある。正体はまだ掴めていない。";
			AddGimmickCard($"[color=gray]{overview}[/color]");
			return;
		}

		if (boss.Gimmicks.Count == 0)
		{
			AddGimmickCard("[color=lime]特筆すべき危険な能力は見当たらない。[/color]");
			return;
		}

		foreach (var gimmick in boss.Gimmicks)
			AddGimmickCard(BuildGimmickCardText(gimmick, tier));
	}

	private static string BuildGimmickCardText(BossGimmick gimmick, IntelTier tier)
	{
		var sb = new StringBuilder();
		sb.Append($"[b]【{GimmickLabel(gimmick.Type)}】[/b]　危険度 [color=orange]{DangerStars(gimmick.DangerLevel)}[/color]\n");
		sb.Append($"{GimmickDescription(gimmick.Type)}");

		if (tier >= IntelTier.Countermeasures)
		{
			var routes = CounterRoutes(gimmick);
			sb.Append("\n[color=cyan]対策：");
			sb.Append(routes.Count > 0 ? string.Join("／", routes) + "（いずれか1つで成立）" : "有効な対策が見つからない");
			sb.Append("[/color]");
		}
		else
		{
			sb.Append("\n[color=gray]対策：？？？（解析率を上げれば判明する）[/color]");
		}

		return sb.ToString();
	}

	/// <summary>対策口の一覧（職業・能力合算・携行品）。判定用の閾値の数値は出さない（→ 情報公開の原則）。</summary>
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

	private void AddGimmickCard(string bbcode)
	{
		var card = new PanelContainer();
		var label = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = true,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		label.AppendText(bbcode);
		card.AddChild(label);
		_gimmickCards.AddChild(card);
	}

	private void ClearGimmickCards()
	{
		foreach (var child in _gimmickCards.GetChildren())
		{
			_gimmickCards.RemoveChild(child);
			child.QueueFree();
		}
	}

	// ==================== 出撃フロー ====================

	/// <summary>
	/// 出撃部隊のドロップダウン。保存済み編成（→ GameState.SavedParties）を並べ、
	/// 待機中のメンバーが1人もいない編成は選べないようにする。
	/// </summary>
	private void RefreshPartyOptions()
	{
		_partyOptionButton.Clear();
		_partyOptionButton.AddItem("（出撃部隊を選択）");

		_hasSelectableParty = false;
		int selectIndex = 0;
		for (int i = 0; i < _state.SavedParties.Count; i++)
		{
			var saved = _state.SavedParties[i];
			int available = PartyFormationSystem.BuildDispatchParty(_state, saved.MemberIds).Members.Count;
			int total = saved.MemberIds.Count(id => _state.Adventurers.Any(a => a.Id == id));

			int itemIndex = i + 1;
			_partyOptionButton.AddItem($"{saved.Name}（待機中 {available}/{total}名）");
			_partyOptionButton.SetItemDisabled(itemIndex, available == 0);
			if (available > 0)
				_hasSelectableParty = true;

			if (saved.Id == _selectedPartyId && available > 0)
				selectIndex = itemIndex;
		}

		_partyOptionButton.Select(selectIndex);
		if (selectIndex == 0)
			_selectedPartyId = null;
	}

	private void OnPartySelected(long index)
	{
		_selectedPartyId = index >= 1 && index - 1 < _state.SavedParties.Count
			? _state.SavedParties[(int)index - 1].Id
			: null;

		RefreshDispatchSection(_selectedField?.GetNextActiveBoss());
	}

	private SavedParty SelectedSavedParty() =>
		_selectedPartyId.HasValue ? _state.SavedParties.FirstOrDefault(p => p.Id == _selectedPartyId.Value) : null;

	/// <summary>
	/// 選択中の部隊のメンバー・道中の見立て・対策の充足状況（下見）と、出撃ボタンの可否を更新する。
	///
	/// 毎回1Fリセット・複数週潜行型（2026年9月改訂）：大迷宮への出撃は常に「1階層から潜行」の1種類。
	/// ボスへの突入は出撃時ではなく、扉前に到達した部隊へ個別に指令する（→ RefreshBossDecision）。
	/// ここでの対策充足状況は、この部隊が扉前に着いた場合の下見（ポーチ選択も反映）として表示する。
	/// </summary>
	private void RefreshDispatchSection(FloorBoss boss)
	{
		_partyMembersLabel.Clear();
		_scoutingForecastLabel.Clear();
		_countermeasureLabel.Clear();
		_dispatchStatusLabel.Clear();
		_scoutingButton.TooltipText = "";
		_gatheringButton.TooltipText = "";

		var saved = SelectedSavedParty();
		var party = saved == null ? new Party() : PartyFormationSystem.BuildDispatchParty(_state, saved.MemberIds);

		// 防御的フォールバック：出撃させる部隊が決まっていない（未選択、または選択中の部隊に
		// 待機中メンバーが残っていない）場合は、見立て等の部隊依存の表示を一切出さず、
		// 出撃ボタンをすべて無効化する（主語の欠けた見立て文が残るのを防ぐ）。
		if (saved == null || party.IsEmpty)
		{
			_scoutingButton.Disabled = true;
			_gatheringButton.Disabled = true;

			string reason = !_hasSelectableParty
				? "出撃可能な待機部隊がありません"
				: GetDispatchBlockedReason(boss, saved, party) ?? GetGatheringBlockedReason(saved, party)
					?? "出撃部隊が選択されていない。上の一覧から出撃させる編成を選ぶこと。";
			_dispatchStatusLabel.AppendText($"[color=gray]{reason}[/color]");
			return;
		}

		string blockedReason = GetDispatchBlockedReason(boss, saved, party);
		_scoutingButton.Disabled = blockedReason != null;

		// 探索（採取）はボスの有無・到達状況に関係なく、フィールドが選ばれてさえいれば出撃できる
		// （完全踏破後のフィールドでも素材採取だけは続けられる）。
		string gatheringBlockedReason = GetGatheringBlockedReason(saved, party);
		_gatheringButton.Disabled = gatheringBlockedReason != null;

		if (blockedReason != null)
			_dispatchStatusLabel.AppendText($"[color=gray]{blockedReason}[/color]");

		_partyMembersLabel.AppendText("出撃メンバー：" +
			string.Join("、", party.Members.Select(m => $"{m.Name}（{JobLabel(m.JobClass)} HP{m.CurrentHP}/{m.MaxHP}）")));
		var waiting = PartyFormationSystem.GetUnavailableMembers(_state, saved.MemberIds);
		if (waiting.Count > 0)
			_partyMembersLabel.AppendText($"\n[color=gray]出撃できない：{string.Join("、", waiting.Select(m => m.Name))}[/color]");

		if (_selectedField != null)
			RefreshGatheringForecast(_selectedField, party);

		if (boss == null || _selectedField == null)
			return;

		RefreshTraversalForecast(_selectedField, boss, party);

		// 扉前に着いた場合の下見：ポーチで選択中のアイテムも積んだ想定で対策の充足状況を見せる。
		foreach (var itemId in GetSelectedPouchItemIds())
			party.TryAddConsumable(itemId);
		_countermeasureLabel.AppendText("[color=gray]（扉前に着いた場合の下見）[/color]\n" + BuildCountermeasureText(boss, party, out _));
	}

	/// <summary>
	/// 出撃できない理由（出撃できるならnull）。ボス（調査・討伐）を対象にする出撃向け。
	///
	/// Core側（DungeonExpeditionSystem.TryDispatch）の失敗条件をすべて網羅し、
	/// 「出撃条件を満たしていない」という曖昧な表示に落ちないようにする（→ 2026年9月改訂）。
	/// </summary>
	private string GetDispatchBlockedReason(FloorBoss boss, SavedParty saved, Party party)
	{
		if (boss == null) return "挑むべき階層ボスがいない。";
		if (_state.DefeatReason != null) return "ギルドは既に解散した。";
		if (saved == null) return "出撃部隊が選択されていない。上の一覧から出撃させる編成を選ぶこと。";
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
		if (!QuestDispatchSystem.CanDispatch(_state))
			return $"同時出撃枠（{_state.UnlockedSquadSlots}枠）がすべて埋まっている。帰還を待つか、出撃予定を取り消すこと。";
		return null;
	}

	/// <summary>出撃失敗の理由を出撃状況ラベルへ表示する（→ 具体的な失敗理由の提示、2026年9月改訂）。</summary>
	private void ShowDispatchFailure(string reason)
	{
		_dispatchStatusLabel.Clear();
		_dispatchStatusLabel.AppendText($"[color=orange][b]⚠ 出撃できなかった：{reason}[/b][/color]");
	}

	/// <summary>
	/// 出撃できない理由（出撃できるならnull）。探索（採取）向け：ボス（GetDispatchBlockedReason）
	/// と違い、対象ボスの有無は問わない（フィールドそのものが対象のため）。
	/// </summary>
	private string GetGatheringBlockedReason(SavedParty saved, Party party)
	{
		if (_selectedField == null) return "挑戦するダンジョンがない。";
		if (_state.DefeatReason != null) return "ギルドは既に解散した。";
		if (saved == null) return "出撃部隊が選択されていない。上の一覧から出撃させる編成を選ぶこと。";
		if (!_selectedField.IsUnlocked) return $"「{_selectedField.Name}」はまだ開放されていない。";
		if (party.IsEmpty)
		{
			var unavailable = PartyFormationSystem.GetUnavailableMembers(_state, saved.MemberIds);
			return unavailable.Count > 0
				? $"この部隊は全員出撃できない状態（重傷・派遣中など）：{string.Join("、", unavailable.Select(m => m.Name))}"
				: "この部隊には出撃できるメンバーがいない（編成が空）。";
		}
		if (!QuestDispatchSystem.CanDispatch(_state))
			return $"同時出撃枠（{_state.UnlockedSquadSlots}枠）がすべて埋まっている。帰還を待つか、出撃予定を取り消すこと。";
		return null;
	}

	/// <summary>
	/// 道中進軍の見立て。部隊の走破力（AGI+DEX）の総合値と、調査度連動の走破倍率
	/// （→ DungeonTraversalResolver.IntelSpeedMultiplier）を示し、進み具合の見込みは定性表現に
	/// とどめる（要求値は出さない）。出撃は毎回1階層から始まるため、1階層からの見立てを出す。
	/// </summary>
	private void RefreshTraversalForecast(DungeonField field, FloorBoss boss, Party party)
	{
		double score = DungeonTraversalResolver.CalculateTraversalScore(party, _state);
		double requirement = DungeonTraversalResolver.FloorRequirement(1);
		double ratio = requirement <= 0 ? double.MaxValue : score / requirement;
		var rank = DungeonTraversalResolver.ClassifyRatio(ratio);
		double speed = DungeonTraversalResolver.IntelSpeedMultiplier(field.GetSegmentBoss(1));

		string rankView = rank switch
		{
			TraversalRank.Lightning => "[color=lime]電撃的に奥まで進めそうだ[/color]",
			TraversalRank.Swift => "[color=cyan]迅速に奥へ進めそうだ[/color]",
			TraversalRank.Normal => "[color=cyan]着実に奥へ進めそうだ[/color]",
			_ => "[color=orange]手こずりながらも、少しは奥へ進めるだろう[/color]",
		};

		_scoutingForecastLabel.AppendText(
			$"[b]道中進軍の見立て[/b]　走破力（AGI+DEX）：{score:F0}　→ この部隊なら{rankView}\n" +
			$"[color=gray]1階層から潜行し、第{boss.Floor}層「{boss.Name}」の扉前で進軍を止めて指令を待つ。" +
			$"解析済みの区間ほど速く安全に抜けられる（現在の区間 走破倍率 ×{speed:F1}）。[/color]");

		_scoutingButton.TooltipText =
			$"走破力の総合値（AGI+DEX合計＋部隊長LDR補正）：{score:F0}\n" +
			"道中進軍は低リスク：HPは減っても強制除籍にはならない。\n" +
			"道中で拾った素材・ゴールドは、ギルドへ帰還した時点で格納される。";
	}

	/// <summary>
	/// 探索（採取）の見立て。部隊の採取スコア（AGI+DEX＋部隊長LDR、HP比率で減衰）を
	/// ツールチップに表示する（→ GatheringResolver）。要求値相当のものは存在しないため
	/// 定性表現の見立てラベルは無く、数値そのものだけを見せる。
	/// </summary>
	private void RefreshGatheringForecast(DungeonField field, Party party)
	{
		double score = GatheringResolver.CalculateGatheringScore(party);
		_gatheringButton.TooltipText =
			$"採取の総合値（AGI+DEX合計＋部隊長LDR補正、部隊のHP比率で減衰）：{score:F0}\n" +
			"探索は低リスク：HPは減っても強制除籍にはならない。";
	}

	/// <summary>
	/// ボス討伐の対策充足状況（BBCode）。解析済み（＝種別が判明している）ギミックごとに充足／未対策を示し、
	/// 未対策が残っていれば赤字で強制除籍・壊滅のリスクを警告する。tooltip には討伐ボタン向けの要約を返す。
	/// </summary>
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

	/// <summary>「大迷宮へ潜行」ボタン。選択中フィールドへ、1階層から潜る道中進軍として出撃させる。</summary>
	private void OnDispatchPressed()
	{
		// 現在パネルで選択中のフィールドを対象に派遣する（→ 大迷宮フィールド選択UI仕様）。
		var boss = _selectedField?.GetNextActiveBoss();
		var saved = SelectedSavedParty();
		if (_expeditionSystem == null)
			return;

		// 失敗理由は必ず具体的に出す（以前はここで無言returnしており、ボタンを押しても
		// 何も起きないように見えていた。→ 2026年9月改訂）。
		if (_selectedField == null || boss == null || saved == null)
		{
			ShowDispatchFailure(_selectedField == null
				? "挑戦するダンジョン（フィールド）が選択されていない。"
				: boss == null
					? "このフィールドは完全制覇済みで、挑むべき階層ボスがいない。"
					: "出撃部隊が選択されていない。上の一覧から出撃させる編成を選ぶこと。");
			return;
		}

		var party = PartyFormationSystem.BuildDispatchParty(_state, saved.MemberIds);
		string blockedReason = GetDispatchBlockedReason(boss, saved, party);
		if (blockedReason != null || !_expeditionSystem.TryDispatch(_state, party, boss, DungeonMissionType.Scouting))
		{
			ShowDispatchFailure(blockedReason ?? "出撃条件を満たしていない（同時出撃枠・部隊の状態・フィールドの開放状況を確認）。");
			return;
		}

		string members = string.Join("・", party.Members.Select(m => m.Name));
		LogRequested.Invoke($"[color=cyan]第{_state.WeekNumber}週：「{saved.Name}」（{members}）が{_selectedField.Name}の" +
			$"第1層から潜行を開始する（目標：第{boss.Floor}層「{boss.Name}」の扉前）。[/color]");

		_selectedPartyId = null; // 出撃した部隊は待機中でなくなるため、選択を解除する
		StateChanged.Invoke();
	}

	/// <summary>「探索出撃（素材採取）」ボタン。選択中フィールドへ、ボスを介さず直接派遣する。</summary>
	private void OnGatheringDispatchPressed()
	{
		var saved = SelectedSavedParty();
		if (_expeditionSystem == null)
			return;

		// 調査・討伐と同じく、押しても何も起きない状態を作らず必ず理由を出す（→ 2026年9月改訂）。
		if (_selectedField == null || saved == null)
		{
			ShowDispatchFailure(_selectedField == null
				? "採取に向かうダンジョン（フィールド）が選択されていない。"
				: "出撃部隊が選択されていない。上の一覧から出撃させる編成を選ぶこと。");
			return;
		}

		var party = PartyFormationSystem.BuildDispatchParty(_state, saved.MemberIds);
		string blockedReason = GetGatheringBlockedReason(saved, party);
		if (blockedReason != null || !_expeditionSystem.TryDispatchGathering(_state, party, _selectedField))
		{
			ShowDispatchFailure(blockedReason ?? "出撃条件を満たしていない（同時出撃枠・部隊の状態・フィールドの開放状況を確認）。");
			return;
		}

		string members = string.Join("・", party.Members.Select(m => m.Name));
		LogRequested.Invoke($"[color=cyan]第{_state.WeekNumber}週：「{saved.Name}」（{members}）が{_selectedField.Name}へ" +
			"探索（採取）任務へ出発する（次週の決算で帰還）。[/color]");

		_selectedPartyId = null; // 出撃した部隊は待機中でなくなるため、選択を解除する
		StateChanged.Invoke();
	}

	// ==================== 潜行中の部隊・扉前の指令 ====================

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
		RefreshBossDecision();
	}

	/// <summary>潜行中一覧の1行分のラベル。遠征状態（→ ExpeditionStatus）と現在階層を示す。</summary>
	private static string MissionLabel(ActiveDungeonMission mission)
	{
		if (mission.MissionType == DungeonMissionType.Gathering)
			return $"【探索】{mission.Field.Name}";

		var boss = mission.TargetedBoss ?? mission.Boss;
		string bossName = boss == null ? "" : $"「{boss.Name}」";
		return mission.Status switch
		{
			ExpeditionStatus.AwaitingBossDecision => $"【扉前・指令待ち】{mission.Field.Name} 第{mission.CurrentFloor}層{bossName}",
			ExpeditionStatus.EngagingBoss => $"【討伐へ突入】{mission.Field.Name} 第{mission.CurrentFloor}層{bossName}",
			ExpeditionStatus.Retreating => $"【撤退中】{mission.Field.Name}",
			_ => mission.WeeksElapsed == 0
				? $"【出発準備】{mission.Field.Name} 第1層から潜行"
				: $"【進軍中】{mission.Field.Name} 第{mission.CurrentFloor}層",
		};
	}

	/// <summary>潜行中一覧で選択されている出撃（無ければnull）。</summary>
	private ActiveDungeonMission SelectedPendingMission()
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
		RefreshBossDecision();
	}

	/// <summary>
	/// 扉前の指令の対象。潜行中一覧で選んだ出撃が撤退可能ならそれを、そうでなければ
	/// 扉前で指令を待っている最初の部隊を対象にする。
	/// </summary>
	private ActiveDungeonMission DecisionTarget()
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

	/// <summary>
	/// 扉前の指令エリア（→ 【⚔️ ボス討伐に挑む】【🏃 撤退・帰還する】）。扉前で判断待ちの部隊
	/// （または一覧で選んだ進軍中の部隊）がいる時だけ表示する。挑む場合は携行ポーチの選択を
	/// その部隊に積んだ想定で、対策の充足状況と代金を示す。
	/// </summary>
	private void RefreshBossDecision()
	{
		_bossDecisionLabel.Clear();
		_pouchCostLabel.Clear();
		_engageBossButton.TooltipText = "";

		var mission = DecisionTarget();
		_bossDecisionPanel.Visible = mission != null;
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

		if (pouchCost > 0)
		{
			_pouchCostLabel.AppendText(insufficient
				? $"[color=red]ポーチ代金：{pouchCost}G（所持金不足：{_state.Gold}G）[/color]"
				: $"ポーチ代金：{pouchCost}G（挑む指令の時点で支払う）");
		}

		_engageBossButton.Text = pouchCost > 0 ? $"⚔️ ボス討伐に挑む（ポーチ代 {pouchCost}G）" : "⚔️ ボス討伐に挑む";
		_engageBossButton.Disabled = insufficient || _state.DefeatReason != null;
		_engageBossButton.TooltipText = insufficient
			? $"携行ポーチの代金（{pouchCost}G）が足りない（所持金 {_state.Gold}G）。"
			: tooltip + "\n次週の決算で決戦判定。勝敗にかかわらず決着後はギルドへ帰還する。";
	}

	private static string CarriedLootText(ActiveDungeonMission mission)
	{
		var parts = new List<string> { $"{mission.CarriedGold}G" };
		parts.AddRange(mission.CarriedMaterials.Select(kv => $"{MaterialBalance.GetName(kv.Key)}×{kv.Value}"));
		return string.Join("、", parts);
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
		ResetPouchSelection(); // 積み込んだポーチは使用予定。次の指令に備えて選択を戻す。
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

		LogRequested.Invoke(mission.MissionType == DungeonMissionType.Gathering
			? $"[color=gray]{mission.Field.Name}への探索出撃を取り消した。部隊は待機に戻った。[/color]"
			: $"[color=gray]{mission.Field.Name}への潜行を取り消した。部隊は待機に戻った。[/color]");
		StateChanged.Invoke();
	}

	// ==================== 表示文言（Core側は列挙子のみを持つ。→ 05技術メモ） ====================

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

	/// <summary>次の開示段階の案内（完全解析済みならnull）。閾値はバランス表（scouting.csv）から引く。</summary>
	private static string NextTierHint(IntelTier tier) => tier switch
	{
		IntelTier.Unknown => $"解析率{ScoutingBalance.IntelTierBasic * 100:F0}%で規模が判明",
		IntelTier.Basic => $"解析率{ScoutingBalance.IntelTierHazards * 100:F0}%で危険ギミックが判明",
		IntelTier.Hazards => $"解析率{ScoutingBalance.IntelTierCountermeasures * 100:F0}%で対策が判明",
		IntelTier.Countermeasures => $"解析率{ScoutingBalance.IntelTierComplete * 100:F0}%で弱点を看破",
		_ => null,
	};

	private static string JobLabel(JobClass job) => job switch
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
}
