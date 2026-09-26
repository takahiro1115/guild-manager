#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 部隊編成パネル（仕様書 03 §4.0.2・§5.3.1・§9）。
///
/// 解放出撃枠（UnlockedSquadSlots）と連動した最大4部隊の切り替え、
/// 4枠メンバーカード（前後衛自動決定バッジ表示・除名・リーダー選出・出撃中ガード）、
/// 冒険者一覧（能力値の表・見出しクリックの並べ替え・任務別の貢献列・配属）、
/// および任務別の部隊指標（進軍→討伐・解析・採取・健全度・相性）を提供する。
///
/// 2026年9月（→ 03 §9「部隊・冒険者」画面）：個人詳細ペイン（AdventurerPanel）と左右に並べて常設する。
/// 編成スロット・候補行のクリックで AdventurerSelected を発し、選択中の行・スロットを枠線で強調する。
///
/// 2026年9月・§0.42（編成UIの改善）：
///  - 指標は任務ごとのカードに分けた（進軍カードに討伐火力も載せる。進軍の先にあるのが討伐のため）。
///  - リーダー以外のスロットに「👑 リーダーに選出」（→ PartyFormationSystem.TryPromoteToLeader）。
///  - 冒険者一覧に HP・7大能力値（実効値）・任務別の貢献の列を置き、見出しクリックで並べ替える。
///  - 候補の行・リーダー選出ボタンにマウスを重ねると、その編成にした場合の指標を先に表示する（増減プレビュー）。
/// 指標・貢献値の式はすべて Core（PartyFormationSystem.PreviewMetrics・各 Resolver の Get*Value）を呼び、ここでは持たない。
/// </summary>
public partial class PartyFormationPanel : VBoxContainer
{
	private readonly Button[] _squadButtons = new Button[4];
	private LineEdit _squadNameEdit = null!;
	private Button _squadRenameButton = null!;
	private Control _dispatchedBadge = null!;
	private Label _statusLabel = null!;

	// 任務別の部隊指標（§0.42）
	private RichTextLabel _previewLabel = null!;
	private RichTextLabel _traversalBody = null!;
	private RichTextLabel _surveyBody = null!;
	private RichTextLabel _gatheringBody = null!;
	private Label _averageHpLabel = null!;
	private ProgressBar _averageHpBar = null!;
	private RichTextLabel _compatibilityWarningLabel = null!;

	// メンバー4枠スロット
	private readonly Control[] _emptySlotContainers = new Control[4];
	private readonly Control[] _filledSlotContainers = new Control[4];
	private readonly Label[] _slotRoleLabels = new Label[4];
	private readonly Label[] _slotPositionBadges = new Label[4];
	private readonly TextureRect[] _slotPortraits = new TextureRect[4];
	private readonly Label[] _slotNameLabels = new Label[4];
	private readonly Label[] _slotJobAgeLabels = new Label[4];
	private readonly Label[] _slotHpLabels = new Label[4];
	private readonly ProgressBar[] _slotHpBars = new ProgressBar[4];
	private readonly Button[] _slotRemoveButtons = new Button[4];
	private readonly Button[] _slotPromoteButtons = new Button[4];

	// 冒険者一覧（表）
	private VBoxContainer _candidateListContainer = null!;
	private ScrollContainer _candidateScroll = null!;
	private MarginContainer _candidateHeaderMargin = null!;
	private readonly Dictionary<SortKey, Button> _sortButtons = new();
	private CheckBox _availableOnlyCheck = null!;
	private OptionButton _contributionOption = null!;
	private Button _resetSortButton = null!;
	private SortKey _sortKey = SortKey.None;
	private bool _sortDescending = true;
	private ContributionKind _contributionKind = ContributionKind.Traversal;

	// 個人詳細ペインとの選択連動（→ 03 §9「部隊・冒険者」画面、2026年9月統合）
	private readonly Control[] _slotPanels = new Control[4];
	private readonly Dictionary<Guid, PanelContainer> _candidateRows = new();
	private Guid? _selectedAdventurerId;

	// 増減プレビュー（§0.42）：表示中の仮の編成と、その説明・きっかけになった行／ボタン
	private List<Guid>? _previewIds;
	private string _previewCaption = "";
	private Control? _previewSource;

	private GameState? _state;
	private PartyFormationSystem _partyFormationSystem = null!;
	private int _selectedSquadIndex = 0;

	/// <summary>一覧の並べ替えの鍵。None＝ロースター順。</summary>
	private enum SortKey { None, Name, Hp, STR, VIT, AGI, DEX, INT, MND, LDR, Contribution }

	/// <summary>一覧の「貢献」列に出す任務別の値（→ Core の各 Resolver の隊員1名ぶんの値）。</summary>
	private enum ContributionKind { Traversal, Power, Guard, Stealth, Analysis, Gathering }

	private static readonly (ContributionKind Kind, string Label, string Tooltip)[] ContributionKinds =
	{
		(ContributionKind.Traversal, "走破", "進軍の走破力への寄与＝VIT×重み＋MND×重み（隊長LDR・研究・参謀の加算は別）"),
		(ContributionKind.Power, "火力", "討伐火力への寄与（→ 各能力値×重み×HP比率。ボスを特定しない試算）"),
		(ContributionKind.Guard, "護衛", "迷宮調査の護衛値＝max(STR, VIT, INT)。部隊で最大の人が主護衛、他は×支援係数で加わる"),
		(ContributionKind.Stealth, "隠密", "隠密の素点＝AGI＋DEX（部隊ではこの平均×人数倍率。斥候・盗賊・重装の補正は別）"),
		(ContributionKind.Analysis, "解析", "迷宮調査の解析への寄与＝INT×係数（部隊では合計）"),
		(ContributionKind.Gathering, "採取", "採取スコアへの寄与＝AGI×係数＋DEX×係数（隊長LDR・斥候/盗賊・田舎育ち・HP比率は別）"),
	};

	private static readonly string[] StatColumns = { "STR", "VIT", "AGI", "DEX", "INT", "MND", "LDR" };

	// 一覧の列幅（見出しと各行で共有して縦に揃える）
	private const int BadgeWidth = 44;
	private const int NameMinWidth = 150;
	private const int HpWidth = 66;
	private const int StatWidth = 38;
	private const int ContributionWidth = 48;
	private const int StatusWidth = 96;
	private const int AssignWidth = 64;
	private const int ColumnSeparation = 6;
	private const int RowMargin = 8;

	private static readonly Color TopValueColor = new(1.0f, 0.84f, 0.3f);
	private static readonly Color StatValueColor = new(0.85f, 0.87f, 0.9f);

	/// <summary>編成の変更でゲーム状態が変わったことを通知する。</summary>
	public event Action StateChanged = delegate { };

	/// <summary>
	/// 編成スロットまたは候補行がクリックされ、個人詳細ペインに表示する冒険者が選ばれた（→ MainDashboard が AdventurerPanel.ShowAdventurer へ中継）。
	/// </summary>
	public event Action<Guid> AdventurerSelected = delegate { };

	public override void _Ready()
	{
		for (int i = 0; i < 4; i++)
		{
			_squadButtons[i] = GetNode<Button>($"%SquadButton{i}");
			int index = i;
			_squadButtons[i].Pressed += () => OnSquadSelected(index);
		}

		_squadNameEdit = GetNode<LineEdit>("%SquadNameEdit");
		_squadRenameButton = GetNode<Button>("%SquadRenameButton");
		_dispatchedBadge = GetNode<Control>("%DispatchedBadge");
		_statusLabel = GetNode<Label>("%StatusLabel");

		_previewLabel = GetNode<RichTextLabel>("%PreviewLabel");
		_traversalBody = GetNode<RichTextLabel>("%TraversalBody");
		_surveyBody = GetNode<RichTextLabel>("%SurveyBody");
		_gatheringBody = GetNode<RichTextLabel>("%GatheringBody");
		_averageHpLabel = GetNode<Label>("%AverageHpLabel");
		_averageHpBar = GetNode<ProgressBar>("%AverageHpBar");
		_compatibilityWarningLabel = GetNode<RichTextLabel>("%CompatibilityWarningLabel");
		SetMetricTooltips();

		for (int i = 0; i < 4; i++)
		{
			_emptySlotContainers[i] = GetNode<Control>($"%EmptySlotContainer{i}");
			_filledSlotContainers[i] = GetNode<Control>($"%FilledSlotContainer{i}");
			_slotPositionBadges[i] = GetNode<Label>($"%SlotPositionBadge{i}");
			_slotPortraits[i] = GetNode<TextureRect>($"%SlotPortrait{i}");
			_slotNameLabels[i] = GetNode<Label>($"%SlotNameLabel{i}");
			_slotJobAgeLabels[i] = GetNode<Label>($"%SlotJobAgeLabel{i}");
			_slotHpLabels[i] = GetNode<Label>($"%SlotHpLabel{i}");
			_slotHpBars[i] = GetNode<ProgressBar>($"%SlotHpBar{i}");
			_slotRemoveButtons[i] = GetNode<Button>($"%SlotRemoveButton{i}");

			_slotPanels[i] = GetNode<Control>($"MemberSlotsPanel/Margin/VBox/MemberSlotsContainer/MemberSlot{i}");
			_slotRoleLabels[i] = _slotPanels[i].GetNode<Label>($"Margin/FilledSlotContainer{i}/HeaderHBox/RoleLabel");

			int slotIndex = i;
			_slotRemoveButtons[i].Pressed += () => OnRemoveMemberClicked(slotIndex);
			BuildPromoteButton(slotIndex);

			// スロットのクリックで個人詳細ペインへ表示（ボタン以外の子はクリックを素通しさせる）。
			IgnoreMouseExceptButtons(_slotPanels[i]);
			_slotPanels[i].MouseDefaultCursorShape = CursorShape.PointingHand;
			_slotPanels[i].GuiInput += e => OnSlotGuiInput(e, slotIndex);
		}

		_candidateListContainer = GetNode<VBoxContainer>("%CandidateListContainer");
		_candidateScroll = GetNode<ScrollContainer>("%CandidateScroll");
		BuildCandidateToolbarAndHeader();
		_candidateScroll.Resized += AlignCandidateHeader;
		_squadRenameButton.Pressed += OnRenamePressed;
	}

	public void Initialize(PartyFormationSystem partyFormationSystem)
	{
		_partyFormationSystem = partyFormationSystem;
	}

	/// <summary>最新のゲーム状態でパネル全体を再描画する。</summary>
	public void Refresh(GameState state)
	{
		_state = state;
		EnsureSquads();

		int unlocked = Math.Max(1, _state.UnlockedSquadSlots);
		if (_selectedSquadIndex >= unlocked)
		{
			_selectedSquadIndex = 0;
		}

		// 選択中の冒険者がロースターを離れた（引退・除籍・退団）場合は選択を解く（暗黙の再選択はしない）。
		if (_selectedAdventurerId.HasValue && !_state.Adventurers.Any(a => a.Id == _selectedAdventurerId.Value))
			_selectedAdventurerId = null;

		// 編成が変わったらプレビューは前提ごと無効（行・ボタンも作り直される）。
		ClearPreviewState();

		RefreshSquadButtons();
		RefreshSelectedSquadView();
		RefreshCandidateList();
		ApplySelectionHighlight();
	}

	/// <summary>
	/// 部隊1〜4（SavedParties）の存在を保証する（不足があれば自動補完）。
	/// </summary>
	private void EnsureSquads()
	{
		if (_state == null) return;
		while (_state.SavedParties.Count < 4)
		{
			int nextIndex = _state.SavedParties.Count + 1;
			_partyFormationSystem.CreateParty(_state, $"第{nextIndex}部隊");
		}
	}

	private void OnSquadSelected(int index)
	{
		if (_state == null) return;
		int unlocked = Math.Max(1, _state.UnlockedSquadSlots);
		if (index >= unlocked) return;

		_selectedSquadIndex = index;
		ClearPreviewState();
		RefreshSquadButtons();
		RefreshSelectedSquadView();
		RefreshCandidateList();
		ApplySelectionHighlight();
	}

	private void RefreshSquadButtons()
	{
		if (_state == null) return;
		int unlocked = Math.Max(1, _state.UnlockedSquadSlots);

		for (int i = 0; i < 4; i++)
		{
			var btn = _squadButtons[i];
			if (i >= unlocked)
			{
				btn.Disabled = true;
				btn.ButtonPressed = false;
				btn.Text = $"第{i + 1}部隊 (未開放)";
			}
			else
			{
				btn.Disabled = false;
				btn.ButtonPressed = (i == _selectedSquadIndex);

				var party = _state.SavedParties[i];
				int memberCount = party.MemberIds.Count(id => _state.Adventurers.Any(a => a.Id == id));
				bool dispatched = IsPartyDispatched(party);
				string tag = dispatched ? "出撃中" : $"{memberCount}/4名";
				btn.Text = $"{party.Name} ({tag})";
			}
		}
	}

	private SavedParty? GetCurrentParty()
	{
		if (_state == null || _state.SavedParties.Count <= _selectedSquadIndex)
			return null;
		return _state.SavedParties[_selectedSquadIndex];
	}

	private bool IsPartyDispatched(SavedParty party)
	{
		if (_state == null) return false;
		if (party.MemberIds.Any(id => _state.Adventurers.Any(a => a.Id == id && a.IsDispatched)))
			return true;
		if (_state.ActiveDungeonMissions.Any(m => m.Party.Members.Any(mem => party.MemberIds.Contains(mem.Id))))
			return true;
		return false;
	}

	private void RefreshSelectedSquadView()
	{
		var currentParty = GetCurrentParty();
		if (currentParty == null || _state == null) return;

		bool isDispatched = IsPartyDispatched(currentParty);
		_dispatchedBadge.Visible = isDispatched;
		_squadNameEdit.Text = currentParty.Name;
		_squadNameEdit.Editable = !isDispatched;
		_squadRenameButton.Disabled = isDispatched;

		// メンバー4枠スロットの描画
		for (int i = 0; i < 4; i++)
		{
			if (i < currentParty.MemberIds.Count)
			{
				var id = currentParty.MemberIds[i];
				var adventurer = _state.Adventurers.FirstOrDefault(a => a.Id == id);
				if (adventurer != null)
				{
					_emptySlotContainers[i].Visible = false;
					_filledSlotContainers[i].Visible = true;

					// リーダー（先頭の枠＝部隊長）の表示（§0.42）
					bool isLeader = i == 0;
					_slotRoleLabels[i].Text = isLeader ? "👑 リーダー" : $"隊員{i + 1}";
					_slotRoleLabels[i].AddThemeColorOverride("font_color", isLeader
						? new Color(1f, 0.85f, 0.4f)
						: new Color(0.8f, 0.8f, 0.8f));

					// 前後衛判定バッジ
					var placement = PlacementRules.GetDefault(adventurer.JobClass);
					if (placement == Placement.Front)
					{
						_slotPositionBadges[i].Text = "【前衛】";
						_slotPositionBadges[i].AddThemeColorOverride("font_color", new Color(0.4f, 0.9f, 1.0f));
					}
					else
					{
						_slotPositionBadges[i].Text = "【後衛】";
						_slotPositionBadges[i].AddThemeColorOverride("font_color", new Color(0.85f, 0.65f, 1.0f));
					}

					// ポートレート
					LoadPortraitTexture(_slotPortraits[i], adventurer.PortraitId);

					// 氏名・職業・年齢
					_slotNameLabels[i].Text = adventurer.Name;
					_slotJobAgeLabels[i].Text = $"{JobLabel(adventurer.JobClass)} ({adventurer.Age}歳)";

					// HP
					_slotHpLabels[i].Text = $"HP {adventurer.CurrentHP}/{adventurer.MaxHP}";
					_slotHpBars[i].MaxValue = Math.Max(1, adventurer.MaxHP);
					_slotHpBars[i].Value = adventurer.CurrentHP;

					// 解除・リーダー選出ボタン
					_slotRemoveButtons[i].Disabled = isDispatched;
					_slotPromoteButtons[i].Visible = !isLeader;
					_slotPromoteButtons[i].Disabled = isDispatched;
					_slotPromoteButtons[i].TooltipText = isDispatched
						? "出撃中の部隊はリーダーを変えられません。"
						: $"{adventurer.Name} をリーダー（一番左）にする。他の隊員は順番を保って右へずれる。\n" +
						  "リーダーのLDRは走破力・隠密・採取に効く（マウスを重ねると変化を上に表示）。";
					continue;
				}
			}

			// 空き枠
			_emptySlotContainers[i].Visible = true;
			_filledSlotContainers[i].Visible = false;
		}

		// 部隊総合力サマリー更新
		RefreshSquadSummary(currentParty);
	}

	// ==================== 任務別の部隊指標（§0.42） ====================

	/// <summary>
	/// 任務別の指標カードを描く。増減プレビュー中（_previewIds あり）は、各値の横に仮の編成での値と差分を並べる。
	/// 値はすべて Core（PartyFormationSystem.PreviewMetrics）から取る。
	/// </summary>
	private void RefreshSquadSummary(SavedParty currentParty)
	{
		if (_state == null) return;

		var party = PartyFormationSystem.BuildDispatchParty(_state, currentParty.MemberIds);
		var m = PartyFormationSystem.CalculateMetrics(party, _state);
		var p = _previewIds == null ? null : PartyFormationSystem.PreviewMetrics(_state, _previewIds);

		_previewLabel.Clear();
		_previewLabel.AppendText(p == null
			? "[color=gray]部隊の任務別の指標（出撃できない隊員は除く）。候補の行や「👑 リーダーに選出」にマウスを重ねると、その編成にした場合の値を → で並べて表示する。[/color]"
			: $"[color=#fbbf24]👁 {_previewCaption}[/color]　[color=gray]（→ の右が変更後の値。緑＝上がる／赤＝下がる）[/color]");

		// 🏃 進軍 → ⚔️ 討伐
		SetBody(_traversalBody,
			Line("走破力", m.TraversalPower, p?.TraversalPower),
			Note($"VIT{W(DungeonTraversalBalance.WeightVit)}＋MND{W(DungeonTraversalBalance.WeightMnd)}＋隊長LDR{W(DungeonTraversalBalance.WeightLdr)}（研究・参謀込み）"),
			Line("討伐火力", m.BossPower, p?.BossPower),
			Note("ボスを問わない試算"));

		// 🔍 解析（迷宮調査）
		string guardDetail = party.IsEmpty
			? ""
			: $"{m.GuardCarrierName} {m.GuardCarrierStat}{m.GuardCarrierValue:F0}" + (m.GuardSupportPower > 0 ? $" ＋支援{m.GuardSupportPower:F0}" : "");
		string stealthDetail = party.IsEmpty
			? ""
			: $"平均{m.BaseStealthScore:F0}×{m.StealthPartySizeMultiplier:0.00}" +
			  $" ＋隊長{m.StealthLeaderBonus:F0}" +
			  (m.StealthSpecialistBonus > 0 ? $" ＋斥候/盗賊{m.StealthSpecialistBonus:F0}" : "") +
			  (m.StealthHeavyPenalty > 0 ? $" −重装{m.StealthHeavyPenalty:F0}" : "");
		SetBody(_surveyBody,
			Line("護衛", m.GuardPower, p?.GuardPower, guardDetail),
			Line("隠密", m.StealthScore, p?.StealthScore, stealthDetail),
			Line("解析", m.AnalysisScore, p?.AnalysisScore, party.IsEmpty ? "" : "INTの合計"));

		// 🌿 採取
		var breakdown = GatheringResolver.BreakDownGatheringScore(party);
		SetBody(_gatheringBody,
			Line("採取", m.GatheringScore, p?.GatheringScore),
			Note(party.IsEmpty
				? "AGI・DEX＋隊長LDR＋斥候/盗賊"
				: $"AGI{breakdown.AgiPart:F0}＋DEX{breakdown.DexPart:F0}＋隊長{breakdown.LdrPart:F0}" +
				  (breakdown.ClassBonus > 0 ? $"＋斥候/盗賊{breakdown.ClassBonus:F0}" : "") +
				  (breakdown.RuralBonus > 0 ? $"＋田舎育ち{breakdown.RuralBonus:F0}" : "") +
				  $"　×HP{breakdown.HpRatio * 100:F0}%"));

		// 平均HP割合
		if (party.IsEmpty)
		{
			_averageHpLabel.Text = "0%";
			_averageHpBar.Value = 0;
		}
		else
		{
			int curTotal = party.Members.Sum(a => a.CurrentHP);
			int maxTotal = party.Members.Sum(a => a.MaxHP);
			int pct = maxTotal > 0 ? (curTotal * 100 / maxTotal) : 0;
			_averageHpLabel.Text = $"{pct}%";
			_averageHpBar.Value = pct;
		}

		// 相性警告
		RefreshCompatibilityWarnings(currentParty);
	}

	private static void SetBody(RichTextLabel label, params string[] lines)
	{
		label.Clear();
		label.AppendText(string.Join("\n", lines.Where(l => l.Length > 0)));
	}

	/// <summary>
	/// 指標1行：「名前 値」（プレビュー中は「→ 変更後 (±差)」を緑／赤で続ける）＋任意の内訳（灰色）。
	/// 差が0.5未満なら変化なしとして「→」を出さない。
	/// </summary>
	private static string Line(string name, double current, double? preview, string detail = "")
	{
		string text = $"{name} [b]{current:F0}[/b]";
		if (preview is double after && Math.Abs(after - current) >= 0.5)
		{
			double diff = after - current;
			string color = diff > 0 ? "#4ade80" : "#f87171";
			text += $" → [color={color}][b]{after:F0}[/b]（{(diff > 0 ? "+" : "−")}{Math.Abs(diff):F0}）[/color]";
		}
		if (detail.Length > 0)
			text += $"\n  [color=gray]{detail}[/color]";
		return text;
	}

	private static string Note(string text) => $"  [color=gray]{text}[/color]";

	/// <summary>係数の短い表記（1なら省く。例：×0.8）。</summary>
	private static string W(double weight) => Math.Abs(weight - 1.0) < 1e-9 ? "" : $"×{weight:0.##}";

	/// <summary>カードのツールチップ（式の説明）。値は Core の係数を引いて出す。</summary>
	private void SetMetricTooltips()
	{
		_traversalBody.TooltipText =
			"走破力：大迷宮へ潜行（進軍）したときに1週で進める階層数を決める。\n" +
			$"＝Σ(VIT×{DungeonTraversalBalance.WeightVit:0.#}＋MND×{DungeonTraversalBalance.WeightMnd:0.#})＋リーダーのLDR×{DungeonTraversalBalance.WeightLdr:0.#}＋研究・参謀の加算\n" +
			"討伐火力：扉前でボスに挑んだときの部隊火力（各隊員の能力値×重み×HP比率の合計）。\n" +
			"完全解析なら+20%、重装甲ボスには巨獣狩りの保有者が+15%（この試算には含まない）。";
		_surveyBody.TooltipText =
			$"護衛：主護衛（隊員で最も高い STR・VIT・INT）＋他の隊員それぞれの最大値×{ScoutingBalance.GuardSupportRatio:0.##}。要求を下回ると解析成果が減りHPも削られる。\n" +
			$"隠密：隊員の AGI＋DEX の平均×人数倍率（1名1.10〜4名0.70）＋リーダーLDR×{ScoutingBalance.StealthWeightLdr:0.##}" +
			$"＋斥候/盗賊1名{ScoutingBalance.StealthBonusRangerThief:F0}−重装1名{ScoutingBalance.StealthHeavyArmorPenalty:F0}。見つかると解析成果が1段階下がる。\n" +
			"解析：INT の合計。要求との比で大成功／成功／失敗。\n" +
			"人数が多いほど護衛は上がるが隠密は下がる（大勢で守るか、少数で潜むか）。";
		_gatheringBody.TooltipText =
			"採取スコア：探索（採取）任務で持ち帰る素材の数・遺物の出やすさを決める。\n" +
			"＝(Σ(AGI×係数＋DEX×係数)＋リーダーのLDR×係数＋斥候/盗賊ボーナス＋田舎育ち)×部隊の平均HP比率";
		foreach (var body in new[] { _traversalBody, _surveyBody, _gatheringBody })
			body.MouseFilter = MouseFilterEnum.Pass;
	}

	private void RefreshCompatibilityWarnings(SavedParty currentParty)
	{
		_compatibilityWarningLabel.Text = "";
		if (_state == null || currentParty.MemberIds.Count < 2)
		{
			_compatibilityWarningLabel.AppendText("[color=gray]メンバー2名以上で相性判定[/color]");
			return;
		}

		var pairs = PartyFormationSystem.GetCompatibilityPairs(_state, currentParty.MemberIds);
		var hostilePairs = pairs.Where(p => p.IsHostile).ToList();

		if (hostilePairs.Count == 0)
		{
			_compatibilityWarningLabel.AppendText("[color=lime]相性良好（険悪ペアなし）[/color]");
			return;
		}

		foreach (var pair in hostilePairs)
		{
			string nameA = _state.Adventurers.FirstOrDefault(a => a.Id == pair.IdA)?.Name ?? "（不明）";
			string nameB = _state.Adventurers.FirstOrDefault(a => a.Id == pair.IdB)?.Name ?? "（不明）";
			_compatibilityWarningLabel.AppendText($"[color=red]⚠ {nameA} と {nameB} は険悪です。[/color]\n");
		}
	}

	// ==================== 増減プレビュー（§0.42） ====================

	/// <summary>仮の編成を指標カードに重ねて表示する（source＝きっかけの行・ボタン。マウスが離れたら解除する）。</summary>
	private void ShowPreview(List<Guid> ids, string caption, Control source)
	{
		var currentParty = GetCurrentParty();
		if (currentParty == null) return;
		_previewIds = ids;
		_previewCaption = caption;
		_previewSource = source;
		RefreshSquadSummary(currentParty);
	}

	/// <summary>
	/// source からマウスが離れたらプレビューを解く。子のボタンへ移っただけ（まだ source の矩形内）なら解かない
	/// （行の上の「配属」ボタンへ移るたびに表示がちらつかないようにするため）。判定は1フレーム後に行う。
	/// </summary>
	private void ClearPreviewLater(Control source)
	{
		Callable.From(() =>
		{
			if (_previewSource != source) return;
			if (IsInstanceValid(source) && source.IsVisibleInTree()
				&& source.GetGlobalRect().HasPoint(source.GetGlobalMousePosition()))
				return;
			ClearPreviewState();
			var currentParty = GetCurrentParty();
			if (currentParty != null) RefreshSquadSummary(currentParty);
		}).CallDeferred();
	}

	private void ClearPreviewState()
	{
		_previewIds = null;
		_previewCaption = "";
		_previewSource = null;
	}

	/// <summary>候補の行にマウスを重ねたときの仮の編成：空きがあれば追加、満員なら選択中の隊員との入れ替え。</summary>
	private void PreviewCandidate(Adventurer a, Control source)
	{
		var currentParty = GetCurrentParty();
		if (currentParty == null || _state == null) return;
		if (!IsAssignable(a) || IsPartyDispatched(currentParty)) return;

		var ids = currentParty.MemberIds.Where(id => _state.Adventurers.Any(x => x.Id == id)).ToList();
		if (ids.Count < Party.MaxSlots)
		{
			ShowPreview(ids.Append(a.Id).ToList(), $"{a.Name} を配属した場合", source);
			return;
		}

		// 満員：スロットで選択中の隊員がいれば、その人と入れ替えた場合を出す（全員分はツールチップ）。
		int swapIndex = _selectedAdventurerId.HasValue ? ids.IndexOf(_selectedAdventurerId.Value) : -1;
		if (swapIndex < 0) return;
		var outgoing = _state.Adventurers.First(x => x.Id == ids[swapIndex]);
		var swapped = ids.ToList();
		swapped[swapIndex] = a.Id;
		ShowPreview(swapped, $"{outgoing.Name} と {a.Name} を入れ替えた場合", source);
	}

	/// <summary>満員の部隊について、各隊員と入れ替えた場合の主な変化（候補行のツールチップ用）。</summary>
	private string BuildSwapTooltip(Adventurer a)
	{
		var currentParty = GetCurrentParty();
		if (currentParty == null || _state == null) return "";
		var ids = currentParty.MemberIds.Where(id => _state.Adventurers.Any(x => x.Id == id)).ToList();
		if (ids.Count < Party.MaxSlots) return "";

		var now = PartyFormationSystem.PreviewMetrics(_state, ids);
		var lines = new List<string> { "部隊は満員。入れ替えた場合の変化（スロットで隊員を選んでから行に重ねると上にも表示）：" };
		for (int i = 0; i < ids.Count; i++)
		{
			var outgoing = _state.Adventurers.First(x => x.Id == ids[i]);
			var swapped = ids.ToList();
			swapped[i] = a.Id;
			var after = PartyFormationSystem.PreviewMetrics(_state, swapped);
			lines.Add($"・{outgoing.Name}と交代：走破{Signed(after.TraversalPower - now.TraversalPower)} 火力{Signed(after.BossPower - now.BossPower)}" +
				$" 護衛{Signed(after.GuardPower - now.GuardPower)} 隠密{Signed(after.StealthScore - now.StealthScore)}" +
				$" 解析{Signed(after.AnalysisScore - now.AnalysisScore)} 採取{Signed(after.GatheringScore - now.GatheringScore)}");
		}
		return string.Join("\n", lines);
	}

	private static string Signed(double value) =>
		Math.Abs(value) < 0.5 ? "±0" : value > 0 ? $"+{value:F0}" : $"−{Math.Abs(value):F0}";

	// ==================== メンバースロット：リーダー選出（§0.42） ====================

	/// <summary>
	/// 「編成解除」の横に「👑 リーダーに選出」を並べる（シーンのボタンを横並びの行へ移し、コードで1つ足す）。
	/// </summary>
	private void BuildPromoteButton(int slotIndex)
	{
		var remove = _slotRemoveButtons[slotIndex];
		var parent = remove.GetParent();
		int index = remove.GetIndex();

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 4);
		parent.RemoveChild(remove);
		row.AddChild(remove);
		remove.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		var promote = new Button
		{
			Text = "👑 リーダーに選出",
			CustomMinimumSize = new Vector2(0, 24),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			Visible = slotIndex > 0,
		};
		promote.AddThemeFontSizeOverride("font_size", 11);
		promote.Pressed += () => OnPromoteClicked(slotIndex);
		promote.MouseEntered += () => PreviewPromote(slotIndex, promote);
		promote.MouseExited += () => ClearPreviewLater(promote);
		row.AddChild(promote);
		_slotPromoteButtons[slotIndex] = promote;

		parent.AddChild(row);
		parent.MoveChild(row, index);
	}

	private void PreviewPromote(int slotIndex, Control source)
	{
		var currentParty = GetCurrentParty();
		if (currentParty == null || _state == null || slotIndex >= currentParty.MemberIds.Count) return;
		var id = currentParty.MemberIds[slotIndex];
		var member = _state.Adventurers.FirstOrDefault(a => a.Id == id);
		if (member == null) return;
		ShowPreview(PartyFormationSystem.WithLeader(currentParty.MemberIds, id), $"{member.Name} をリーダーにした場合", source);
	}

	private void OnPromoteClicked(int slotIndex)
	{
		var currentParty = GetCurrentParty();
		if (currentParty == null || _state == null || slotIndex >= currentParty.MemberIds.Count) return;

		var id = currentParty.MemberIds[slotIndex];
		var member = _state.Adventurers.FirstOrDefault(a => a.Id == id);
		if (!_partyFormationSystem.TryPromoteToLeader(_state, currentParty, id))
		{
			_statusLabel.Text = IsPartyDispatched(currentParty)
				? "出撃中の部隊はリーダーを変えられません。"
				: "リーダーを変えられませんでした。";
			return;
		}

		_statusLabel.Text = $"{member?.Name ?? "隊員"} を {currentParty.Name} のリーダーにしました。";
		StateChanged.Invoke();
	}

	// ==================== 冒険者一覧（表・並べ替え・貢献列、§0.42） ====================

	/// <summary>
	/// 一覧の上に操作行（配属可能のみ・貢献列の任務）と列見出し（クリックで並べ替え）を置く。
	/// 見出しは各行と同じ列幅・余白で組み、スクロールバーの幅だけ右に余白を足して縦を揃える（→ AlignCandidateHeader）。
	/// </summary>
	private void BuildCandidateToolbarAndHeader()
	{
		var vbox = _candidateScroll.GetParent();
		int scrollIndex = _candidateScroll.GetIndex();

		var toolbar = new HBoxContainer();
		toolbar.AddThemeConstantOverride("separation", 12);
		_availableOnlyCheck = new CheckBox { Text = "配属可能のみ", TooltipText = "出撃中・負傷中・部隊に所属中の冒険者を隠す" };
		_availableOnlyCheck.AddThemeFontSizeOverride("font_size", 11);
		_availableOnlyCheck.Toggled += _ => RefreshCandidateList();
		toolbar.AddChild(_availableOnlyCheck);

		var contributionLabel = new Label { Text = "貢献列：" };
		contributionLabel.AddThemeFontSizeOverride("font_size", 11);
		toolbar.AddChild(contributionLabel);
		_contributionOption = new OptionButton { TooltipText = "「貢献」列に出す任務別の値（隊員1名ぶん）" };
		_contributionOption.AddThemeFontSizeOverride("font_size", 11);
		foreach (var (_, label, _) in ContributionKinds)
			_contributionOption.AddItem(label);
		_contributionOption.ItemSelected += index =>
		{
			_contributionKind = ContributionKinds[(int)index].Kind;
			RefreshCandidateList();
		};
		toolbar.AddChild(_contributionOption);

		// 並び順を既定（ロースター順＝加入順）へ戻す。絞り込み（配属可能のみ）と貢献列の選択は変えない。
		_resetSortButton = new Button
		{
			Text = "↺ 並び順を戻す",
			TooltipText = "見出しクリックの並べ替えを解除し、既定の並び（ロースター順＝加入順）に戻す。\n「配属可能のみ」と貢献列の選択はそのまま。",
			Disabled = true,
		};
		_resetSortButton.AddThemeFontSizeOverride("font_size", 11);
		_resetSortButton.Pressed += () =>
		{
			_sortKey = SortKey.None;
			_sortDescending = true;
			UpdateSortButtonTexts();
			RefreshCandidateList();
		};
		toolbar.AddChild(_resetSortButton);
		vbox.AddChild(toolbar);
		vbox.MoveChild(toolbar, scrollIndex);

		_candidateHeaderMargin = new MarginContainer();
		_candidateHeaderMargin.AddThemeConstantOverride("margin_left", RowMargin);
		_candidateHeaderMargin.AddThemeConstantOverride("margin_right", RowMargin);
		var header = new HBoxContainer();
		header.AddThemeConstantOverride("separation", ColumnSeparation);
		_candidateHeaderMargin.AddChild(header);

		header.AddChild(HeaderSpacer(BadgeWidth));
		header.AddChild(SortButton(SortKey.Name, "氏名（職業・年齢）", NameMinWidth, expand: true, alignRight: false));
		header.AddChild(SortButton(SortKey.Hp, "HP", HpWidth));
		foreach (var stat in StatColumns)
			header.AddChild(SortButton(Enum.Parse<SortKey>(stat), stat, StatWidth));
		header.AddChild(SortButton(SortKey.Contribution, "貢献", ContributionWidth));
		header.AddChild(HeaderSpacer(StatusWidth));
		header.AddChild(HeaderSpacer(AssignWidth));

		vbox.AddChild(_candidateHeaderMargin);
		vbox.MoveChild(_candidateHeaderMargin, scrollIndex + 1);
		UpdateSortButtonTexts();
	}

	private static Control HeaderSpacer(int width) => new Control { CustomMinimumSize = new Vector2(width, 0), MouseFilter = MouseFilterEnum.Ignore };

	private Button SortButton(SortKey key, string text, int width, bool expand = false, bool alignRight = true)
	{
		var button = new Button
		{
			Text = text,
			Flat = true,
			CustomMinimumSize = new Vector2(width, 20),
			SizeFlagsHorizontal = expand ? SizeFlags.ExpandFill : SizeFlags.ShrinkBegin,
			Alignment = alignRight ? HorizontalAlignment.Right : HorizontalAlignment.Left,
			TooltipText = "クリックで並べ替え（もう一度で昇順／降順を切り替え）",
			ClipText = true,
		};
		button.AddThemeFontSizeOverride("font_size", 11);
		button.Pressed += () => OnSortPressed(key);
		_sortButtons[key] = button;
		return button;
	}

	private void OnSortPressed(SortKey key)
	{
		if (_sortKey == key)
		{
			_sortDescending = !_sortDescending;
		}
		else
		{
			_sortKey = key;
			_sortDescending = key != SortKey.Name; // 数値は大きい順、氏名は五十音順から
		}
		UpdateSortButtonTexts();
		RefreshCandidateList();
	}

	private void UpdateSortButtonTexts()
	{
		if (_resetSortButton != null)
			_resetSortButton.Disabled = _sortKey == SortKey.None; // 既定の並びなら押せない（＝並べ替え中かどうかの目印）
		foreach (var (key, button) in _sortButtons)
		{
			string baseText = key switch
			{
				SortKey.Name => "氏名（職業・年齢）",
				SortKey.Hp => "HP",
				SortKey.Contribution => ContributionKinds.First(c => c.Kind == _contributionKind).Label,
				_ => key.ToString(),
			};
			button.Text = key == _sortKey ? $"{baseText}{(_sortDescending ? "▼" : "▲")}" : baseText;
			if (key == SortKey.Contribution)
				button.TooltipText = ContributionKinds.First(c => c.Kind == _contributionKind).Tooltip + "\nクリックで並べ替え";
		}
	}

	/// <summary>スクロールバーの幅だけ見出しの右余白を広げ、行の列と縦を揃える。</summary>
	private void AlignCandidateHeader()
	{
		if (_candidateHeaderMargin == null) return;
		float scrollbar = _candidateScroll.GetVScrollBar().Size.X;
		_candidateHeaderMargin.AddThemeConstantOverride("margin_right", RowMargin + (int)Math.Ceiling(scrollbar));
	}

	private double ContributionValue(Adventurer a) => _contributionKind switch
	{
		ContributionKind.Traversal => DungeonTraversalResolver.GetMemberTraversalValue(a),
		ContributionKind.Power => DungeonPowerCalculator.MemberPower(a),
		ContributionKind.Guard => ScoutingResolver.GetGuardValue(a),
		ContributionKind.Stealth => ScoutingResolver.GetStealthValue(a),
		ContributionKind.Analysis => ScoutingResolver.GetAnalysisValue(a),
		_ => GatheringResolver.GetMemberGatheringValue(a),
	};

	private static int StatValue(Adventurer a, string stat) => (int)Math.Round(a.GetEffectiveStat(stat));

	private double SortValue(Adventurer a) => _sortKey switch
	{
		SortKey.Hp => a.CurrentHP,
		SortKey.Contribution => ContributionValue(a),
		SortKey.None or SortKey.Name => 0,
		_ => StatValue(a, _sortKey.ToString()),
	};

	/// <summary>配属できる状態か（出撃中・負傷・引退でなく、どの部隊にも属していない）。</summary>
	private bool IsAssignable(Adventurer a) =>
		_state != null && !a.IsDispatched && a.Injury == InjurySeverity.None && !a.IsRetired
		&& !_state.SavedParties.Any(p => p.MemberIds.Contains(a.Id));

	private void RefreshCandidateList()
	{
		if (_state == null) return;

		// 既存のリスト子要素をクリア
		foreach (Node child in _candidateListContainer.GetChildren())
		{
			child.QueueFree();
		}
		_candidateRows.Clear();

		var currentParty = GetCurrentParty();
		bool isCurrentPartyDispatched = currentParty != null && IsPartyDispatched(currentParty);
		bool isCurrentPartyFull = currentParty != null && currentParty.MemberIds.Count(id => _state.Adventurers.Any(a => a.Id == id)) >= Party.MaxSlots;

		IEnumerable<Adventurer> rows = _state.Adventurers;
		if (_availableOnlyCheck.ButtonPressed)
			rows = rows.Where(IsAssignable);

		// 並べ替え（安定ソート。None はロースター順のまま）
		rows = _sortKey switch
		{
			SortKey.None => rows,
			SortKey.Name => _sortDescending
				? rows.OrderByDescending(a => a.Name, StringComparer.CurrentCulture)
				: rows.OrderBy(a => a.Name, StringComparer.CurrentCulture),
			_ => _sortDescending ? rows.OrderByDescending(SortValue) : rows.OrderBy(SortValue),
		};
		var list = rows.ToList();

		// 列ごとの最大値（表示中の行の中で）を金色で強調する
		var topStats = StatColumns.ToDictionary(s => s, s => list.Count == 0 ? int.MinValue : list.Max(a => StatValue(a, s)));
		double topContribution = list.Count == 0 ? double.MinValue : list.Max(ContributionValue);

		foreach (var adventurer in list)
		{
			bool isDispatched = adventurer.IsDispatched;
			bool isInjured = adventurer.Injury != InjurySeverity.None;
			bool isRetired = adventurer.IsRetired;
			var memberInParty = _state.SavedParties.FirstOrDefault(p => p.MemberIds.Contains(adventurer.Id));
			bool isInCurrentParty = memberInParty != null && currentParty != null && memberInParty.Id == currentParty.Id;
			bool isInOtherParty = memberInParty != null && currentParty != null && memberInParty.Id != currentParty.Id;

			// 配属可能かどうかの厳格ガード
			bool isAvailable = !isDispatched && !isInjured && !isRetired && !isInCurrentParty && !isInOtherParty;
			bool canAssign = isAvailable && !isCurrentPartyDispatched && !isCurrentPartyFull;

			var row = CreateCandidateRow(adventurer, isDispatched, isInCurrentParty, isInOtherParty, memberInParty,
				canAssign, isAvailable && isCurrentPartyFull && !isCurrentPartyDispatched, topStats, topContribution);
			_candidateListContainer.AddChild(row);
		}

		if (list.Count == 0)
		{
			var empty = new Label { Text = "条件に合う冒険者がいません。" };
			empty.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
			empty.AddThemeFontSizeOverride("font_size", 11);
			_candidateListContainer.AddChild(empty);
		}

		Callable.From(AlignCandidateHeader).CallDeferred();
	}

	private Control CreateCandidateRow(
		Adventurer a,
		bool isDispatched,
		bool isInCurrentParty,
		bool isInOtherParty,
		SavedParty? otherParty,
		bool canAssign,
		bool canSwap,
		IReadOnlyDictionary<string, int> topStats,
		double topContribution)
	{
		var panel = new PanelContainer();
		panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", RowMargin);
		margin.AddThemeConstantOverride("margin_top", 2);
		margin.AddThemeConstantOverride("margin_right", RowMargin);
		margin.AddThemeConstantOverride("margin_bottom", 2);
		panel.AddChild(margin);

		var hbox = new HBoxContainer();
		hbox.AddThemeConstantOverride("separation", ColumnSeparation);
		margin.AddChild(hbox);

		// 前後衛バッジ
		bool isFront = PlacementRules.GetDefault(a.JobClass) == Placement.Front;
		hbox.AddChild(Cell(isFront ? "【前衛】" : "【後衛】", BadgeWidth,
			isFront ? new Color(0.4f, 0.9f, 1.0f) : new Color(0.85f, 0.65f, 1.0f), alignRight: false));

		// 氏名・職業・年齢
		var nameLabel = Cell($"{a.Name} ({JobLabel(a.JobClass)}・{a.Age}歳)", NameMinWidth, null, alignRight: false, fontSize: 12);
		nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		nameLabel.ClipText = true;
		hbox.AddChild(nameLabel);

		// HP
		hbox.AddChild(Cell($"{a.CurrentHP}/{a.MaxHP}", HpWidth, a.CurrentHP < a.MaxHP ? new Color(1f, 0.75f, 0.45f) : StatValueColor));

		// 7大能力値（実効値。列の最大値は金色）
		foreach (var stat in StatColumns)
		{
			int value = StatValue(a, stat);
			hbox.AddChild(Cell(value.ToString(), StatWidth, value == topStats[stat] ? TopValueColor : StatValueColor));
		}

		// 任務別の貢献
		double contribution = ContributionValue(a);
		hbox.AddChild(Cell($"{contribution:F0}", ContributionWidth,
			Math.Abs(contribution - topContribution) < 0.001 ? TopValueColor : new Color(0.55f, 0.85f, 1f)));

		// 状態バッジ
		var (statusText, statusColor) = StatusBadge(a, isDispatched, isInCurrentParty, isInOtherParty, otherParty);
		hbox.AddChild(Cell(statusText, StatusWidth, statusColor, alignRight: false));

		// 配属ボタン
		var assignButton = new Button();
		assignButton.Text = "配属 ＋";
		assignButton.CustomMinimumSize = new Vector2(AssignWidth, 22);
		assignButton.AddThemeFontSizeOverride("font_size", 11);
		assignButton.Disabled = !canAssign;
		assignButton.Pressed += () => OnAssignAdventurerClicked(a);
		assignButton.MouseEntered += () => PreviewCandidate(a, panel);
		hbox.AddChild(assignButton);

		// ツールチップ：能力値の内訳（素の値・PA）と、満員時は入れ替えの試算
		panel.TooltipText = BuildRowTooltip(a) + (canSwap ? "\n\n" + BuildSwapTooltip(a) : "");

		// 行のクリックで個人詳細ペインへ表示（配属ボタン以外の子はクリックを素通しさせる）。
		IgnoreMouseExceptButtons(panel);
		panel.MouseDefaultCursorShape = CursorShape.PointingHand;
		panel.GuiInput += e =>
		{
			if (IsLeftClick(e))
				SelectAdventurer(a.Id);
		};
		panel.MouseEntered += () => PreviewCandidate(a, panel);
		panel.MouseExited += () => ClearPreviewLater(panel);
		_candidateRows[a.Id] = panel;

		return panel;
	}

	private static Label Cell(string text, int width, Color? color, bool alignRight = true, int fontSize = 11)
	{
		var label = new Label
		{
			Text = text,
			CustomMinimumSize = new Vector2(width, 0),
			HorizontalAlignment = alignRight ? HorizontalAlignment.Right : HorizontalAlignment.Left,
		};
		label.AddThemeFontSizeOverride("font_size", fontSize);
		if (color is Color c)
			label.AddThemeColorOverride("font_color", c);
		return label;
	}

	private static (string Text, Color Color) StatusBadge(Adventurer a, bool isDispatched, bool isInCurrentParty, bool isInOtherParty, SavedParty? otherParty)
	{
		if (isDispatched) return ("【出撃中】", new Color(1.0f, 0.5f, 0.2f));
		if (a.Injury == InjurySeverity.Severe) return ("【重傷】", new Color(1.0f, 0.3f, 0.3f));
		if (a.Injury == InjurySeverity.Light) return ("【軽傷】", new Color(1.0f, 0.85f, 0.3f));
		if (isInCurrentParty) return ("【当部隊配属中】", new Color(0.4f, 0.8f, 1.0f));
		if (isInOtherParty) return ($"【{otherParty?.Name ?? "他部隊"}】", new Color(0.7f, 0.7f, 0.8f));
		if (a.IsRetired) return ("【引退】", new Color(0.5f, 0.5f, 0.5f));
		return ("【待機中】", new Color(0.4f, 1.0f, 0.4f));
	}

	/// <summary>行のツールチップ：各能力値の実効値・素の値・PA（装備・特性の補正がどれだけ乗っているか）。</summary>
	private static string BuildRowTooltip(Adventurer a)
	{
		var parts = StatColumns.Select(stat =>
		{
			int effective = StatValue(a, stat);
			int raw = AdventurerStatRaw(a, stat);
			int pa = AdventurerStatPa(a, stat);
			return effective == raw ? $"{stat} {effective}（PA{pa}）" : $"{stat} {effective}（素{raw}／PA{pa}）";
		});
		return $"{a.Name}（{JobLabel(a.JobClass)}）\n{string.Join("　", parts)}\nクリックで右に詳細を表示";
	}

	private static int AdventurerStatRaw(Adventurer a, string stat) => stat switch
	{
		"STR" => a.STR, "VIT" => a.VIT, "AGI" => a.AGI, "DEX" => a.DEX, "INT" => a.INT, "MND" => a.MND, _ => a.LDR,
	};

	private static int AdventurerStatPa(Adventurer a, string stat) => stat switch
	{
		"STR" => a.PA_STR, "VIT" => a.PA_VIT, "AGI" => a.PA_AGI, "DEX" => a.PA_DEX, "INT" => a.PA_INT, "MND" => a.PA_MND, _ => a.PA_LDR,
	};

	// ==== 個人詳細ペインとの選択連動（→ 03 §9「部隊・冒険者」画面、2026年9月統合） ====

	private static bool IsLeftClick(InputEvent e) =>
		e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left };

	/// <summary>編成スロットのクリック。空き枠は何もしない。</summary>
	private void OnSlotGuiInput(InputEvent e, int slotIndex)
	{
		if (!IsLeftClick(e)) return;
		var currentParty = GetCurrentParty();
		if (currentParty == null || slotIndex >= currentParty.MemberIds.Count) return;
		SelectAdventurer(currentParty.MemberIds[slotIndex]);
	}

	/// <summary>冒険者を選択し、個人詳細ペインへの表示を依頼する。</summary>
	private void SelectAdventurer(Guid id)
	{
		_selectedAdventurerId = id;
		ApplySelectionHighlight();
		AdventurerSelected.Invoke(id);
	}

	/// <summary>選択中の冒険者の編成スロット・候補行を枠線で強調する（未選択なら強調なし）。</summary>
	private void ApplySelectionHighlight()
	{
		var currentParty = GetCurrentParty();
		for (int i = 0; i < 4; i++)
		{
			bool selected = _selectedAdventurerId.HasValue && currentParty != null
				&& i < currentParty.MemberIds.Count && currentParty.MemberIds[i] == _selectedAdventurerId.Value;
			SetHighlighted(_slotPanels[i], selected);
		}

		foreach (var (id, row) in _candidateRows)
		{
			if (IsInstanceValid(row))
				SetHighlighted(row, _selectedAdventurerId == id);
		}
	}

	private static readonly StyleBoxFlat SelectedStyle = new()
	{
		BgColor = new Color(0.16f, 0.26f, 0.38f, 0.95f),
		BorderColor = new Color(0.45f, 0.8f, 1.0f),
		BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
		CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
	};

	private static void SetHighlighted(Control panel, bool selected)
	{
		if (selected)
			panel.AddThemeStyleboxOverride("panel", SelectedStyle);
		else
			panel.RemoveThemeStyleboxOverride("panel");
	}

	/// <summary>
	/// パネル配下のボタン以外の子孫をクリック不感（Ignore）にし、クリックをパネル自身へ届かせる。
	/// ボタン（解除・リーダー選出・配属）は従来どおり押せる。
	/// </summary>
	private static void IgnoreMouseExceptButtons(Node root)
	{
		foreach (var child in root.GetChildren())
		{
			if (child is BaseButton)
				continue;
			if (child is Control c)
				c.MouseFilter = MouseFilterEnum.Ignore;
			IgnoreMouseExceptButtons(child);
		}
	}

	private void OnAssignAdventurerClicked(Adventurer a)
	{
		var currentParty = GetCurrentParty();
		if (currentParty == null || _state == null) return;

		if (IsPartyDispatched(currentParty))
		{
			_statusLabel.Text = "出撃中の部隊には配属できません。";
			return;
		}

		if (currentParty.MemberIds.Count >= 4)
		{
			_statusLabel.Text = "部隊は既に4名編成されています。";
			return;
		}

		if (!_partyFormationSystem.TryAssignMember(_state, currentParty, a.Id))
		{
			_statusLabel.Text = $"{a.Name} の配属に失敗しました。";
			return;
		}

		_statusLabel.Text = $"{a.Name} を {currentParty.Name} に配属しました。";
		StateChanged.Invoke();
	}

	private void OnRemoveMemberClicked(int slotIndex)
	{
		var currentParty = GetCurrentParty();
		if (currentParty == null || _state == null) return;

		if (IsPartyDispatched(currentParty))
		{
			_statusLabel.Text = "出撃中の部隊からは除名できません。";
			return;
		}

		if (slotIndex >= currentParty.MemberIds.Count) return;

		var id = currentParty.MemberIds[slotIndex];
		var member = _state.Adventurers.FirstOrDefault(a => a.Id == id);
		_partyFormationSystem.RemoveMember(currentParty, id);

		_statusLabel.Text = $"{member?.Name ?? "メンバー"} を部隊から外しました。";
		StateChanged.Invoke();
	}

	private void OnRenamePressed()
	{
		var currentParty = GetCurrentParty();
		if (currentParty == null) return;

		if (IsPartyDispatched(currentParty))
		{
			_statusLabel.Text = "出撃中の部隊は改名できません。";
			return;
		}

		string newName = _squadNameEdit.Text.Trim();
		if (string.IsNullOrEmpty(newName))
		{
			_statusLabel.Text = "部隊名を入力してください。";
			return;
		}

		_partyFormationSystem.RenameParty(currentParty, newName);
		_statusLabel.Text = $"部隊名を「{newName}」に変更しました。";
		StateChanged.Invoke();
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
			{
				rect.Texture = GD.Load<Texture2D>(fallback);
			}
			else
			{
				rect.Texture = null;
			}
		}
	}

	private static string JobLabel(JobClass job) => job switch
	{
		JobClass.Warrior => "重戦士",
		JobClass.Knight => "騎士",
		JobClass.Ranger => "斥候",
		JobClass.Mage => "魔導士",
		JobClass.Cleric => "神官",
		JobClass.Thief => "盗賊",
		JobClass.Scholar => "学者",
		_ => job.ToString()
	};
}
