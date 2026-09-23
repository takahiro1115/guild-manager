#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 部隊編成パネル（仕様書 03 §4.0.2・§5.3.1）。
///
/// 解放出撃枠（UnlockedSquadSlots）と連動した最大4部隊の切り替え、
/// 4枠メンバーカード（前後衛自動決定バッジ表示・除名・出撃中ガード）、
/// 待機冒険者のアサインUI（重複配属防止・ステータスバッジ）、
/// および部隊総合力サマリー（走破力予測・隠密解析・健全度・相性）を提供する。
/// </summary>
public partial class PartyFormationPanel : VBoxContainer
{
	private readonly Button[] _squadButtons = new Button[4];
	private LineEdit _squadNameEdit = null!;
	private Button _squadRenameButton = null!;
	private Control _dispatchedBadge = null!;
	private Label _statusLabel = null!;

	// 部隊総合力サマリー
	private Label _traversalScoreLabel = null!;
	private Label _traversalDetailLabel = null!;
	private Label _stealthScoreLabel = null!;
	private Label _stealthDetailLabel = null!;
	private Label _analysisScoreLabel = null!;
	private Label _averageHpLabel = null!;
	private ProgressBar _averageHpBar = null!;
	private RichTextLabel _compatibilityWarningLabel = null!;

	// メンバー4枠スロット
	private readonly Control[] _emptySlotContainers = new Control[4];
	private readonly Control[] _filledSlotContainers = new Control[4];
	private readonly Label[] _slotPositionBadges = new Label[4];
	private readonly TextureRect[] _slotPortraits = new TextureRect[4];
	private readonly Label[] _slotNameLabels = new Label[4];
	private readonly Label[] _slotJobAgeLabels = new Label[4];
	private readonly Label[] _slotHpLabels = new Label[4];
	private readonly ProgressBar[] _slotHpBars = new ProgressBar[4];
	private readonly Button[] _slotRemoveButtons = new Button[4];

	// 候補冒険者リスト
	private VBoxContainer _candidateListContainer = null!;

	private GameState? _state;
	private PartyFormationSystem _partyFormationSystem = null!;
	private int _selectedSquadIndex = 0;

	/// <summary>編成の変更でゲーム状態が変わったことを通知する。</summary>
	public event Action StateChanged = delegate { };

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

		_traversalScoreLabel = GetNode<Label>("%TraversalScoreLabel");
		_traversalDetailLabel = GetNode<Label>("%TraversalDetailLabel");
		_stealthScoreLabel = GetNode<Label>("%StealthScoreLabel");
		_stealthDetailLabel = GetNode<Label>("%StealthDetailLabel");
		_analysisScoreLabel = GetNode<Label>("%AnalysisScoreLabel");
		_averageHpLabel = GetNode<Label>("%AverageHpLabel");
		_averageHpBar = GetNode<ProgressBar>("%AverageHpBar");
		_compatibilityWarningLabel = GetNode<RichTextLabel>("%CompatibilityWarningLabel");

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

			int slotIndex = i;
			_slotRemoveButtons[i].Pressed += () => OnRemoveMemberClicked(slotIndex);
		}

		_candidateListContainer = GetNode<VBoxContainer>("%CandidateListContainer");
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

		RefreshSquadButtons();
		RefreshSelectedSquadView();
		RefreshCandidateList();
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
		RefreshSquadButtons();
		RefreshSelectedSquadView();
		RefreshCandidateList();
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

					// 解除ボタン
					_slotRemoveButtons[i].Disabled = isDispatched;
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

	private void RefreshSquadSummary(SavedParty currentParty)
	{
		if (_state == null) return;

		var party = PartyFormationSystem.BuildDispatchParty(_state, currentParty.MemberIds);

		// 指標はCore側で一元的に算出する（→ PartyFormationSystem.CalculateMetrics、03 §4.5.3）。
		// 以前はここで隠密の式を独自に書き直しており、Core側の式を変えてもUIが追随しない
		// 二重管理になっていた（かつ走破力とまったく同じ式で、常に同値が表示されていた）。
		var metrics = PartyFormationSystem.CalculateMetrics(party, _state);

		// 走破力予測（VIT・MND・部隊長LDR＋研究/参謀ボーナス）
		_traversalScoreLabel.Text = $"{metrics.TraversalPower:F0} pt";
		_traversalDetailLabel.Text = party.IsEmpty
			? ""
			: $"VIT×{DungeonTraversalBalance.WeightVit:0.#} ＋ MND×{DungeonTraversalBalance.WeightMnd:0.#} ＋ 隊長LDR";

		// 隠密適性（AGI・DEX・隊長LDR＋斥候/盗賊ボーナス − 人数減衰・重装ペナルティ）
		_stealthScoreLabel.Text = $"{metrics.StealthScore:F0} pt";
		_stealthDetailLabel.Text = party.IsEmpty ? "" : BuildStealthDetail(metrics, party.Members.Count);

		// 解析適性
		_analysisScoreLabel.Text = $"{metrics.AnalysisScore:F0} pt";

		// 平均HP割合
		if (party.IsEmpty)
		{
			_averageHpLabel.Text = "0%";
			_averageHpBar.Value = 0;
		}
		else
		{
			int curTotal = party.Members.Sum(m => m.CurrentHP);
			int maxTotal = party.Members.Sum(m => m.MaxHP);
			int pct = maxTotal > 0 ? (curTotal * 100 / maxTotal) : 0;
			_averageHpLabel.Text = $"{pct}%";
			_averageHpBar.Value = pct;
		}

		// 相性警告
		RefreshCompatibilityWarnings(currentParty);
	}

	/// <summary>
	/// 隠密適性の内訳（素点→人数倍率→重装ペナルティ）を1行で示す（→ 03 §4.5.3）。
	/// 「なぜこの数字なのか」「何を直せば上がるのか」が編成画面で分かるようにするため。
	/// </summary>
	private static string BuildStealthDetail(SquadMetrics metrics, int memberCount)
	{
		var parts = new List<string> { $"素点{metrics.BaseStealthScore:F0}" };

		if (metrics.StealthSpecialistCount > 0)
			parts.Add($"斥候/盗賊{metrics.StealthSpecialistCount}名込");

		parts.Add($"{memberCount}名×{metrics.StealthPartySizeMultiplier:0.00}");

		if (metrics.HeavyMemberCount > 0)
			parts.Add($"重装{metrics.HeavyMemberCount}名 −{metrics.HeavyMemberCount * ScoutingBalance.StealthHeavyArmorPenalty:F0}");

		return string.Join(" / ", parts);
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

	private void RefreshCandidateList()
	{
		if (_state == null) return;

		// 既存のリスト子要素をクリア
		foreach (Node child in _candidateListContainer.GetChildren())
		{
			child.QueueFree();
		}

		var currentParty = GetCurrentParty();
		bool isCurrentPartyDispatched = currentParty != null && IsPartyDispatched(currentParty);
		bool isCurrentPartyFull = currentParty != null && currentParty.MemberIds.Count >= 4;

		foreach (var adventurer in _state.Adventurers)
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

			var row = CreateCandidateRow(adventurer, isDispatched, isInjured, isRetired, isInCurrentParty, isInOtherParty, memberInParty, canAssign);
			_candidateListContainer.AddChild(row);
		}
	}

	private Control CreateCandidateRow(
		Adventurer a,
		bool isDispatched,
		bool isInjured,
		bool isRetired,
		bool isInCurrentParty,
		bool isInOtherParty,
		SavedParty? otherParty,
		bool canAssign)
	{
		var panel = new PanelContainer();
		panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 8);
		margin.AddThemeConstantOverride("margin_top", 3);
		margin.AddThemeConstantOverride("margin_right", 8);
		margin.AddThemeConstantOverride("margin_bottom", 3);
		panel.AddChild(margin);

		var hbox = new HBoxContainer();
		hbox.AddThemeConstantOverride("separation", 10);
		margin.AddChild(hbox);

		// 前後衛バッジ
		var posBadge = new Label();
		var placement = PlacementRules.GetDefault(a.JobClass);
		if (placement == Placement.Front)
		{
			posBadge.Text = "【前衛】";
			posBadge.AddThemeColorOverride("font_color", new Color(0.4f, 0.9f, 1.0f));
		}
		else
		{
			posBadge.Text = "【後衛】";
			posBadge.AddThemeColorOverride("font_color", new Color(0.85f, 0.65f, 1.0f));
		}
		posBadge.AddThemeFontSizeOverride("font_size", 11);
		hbox.AddChild(posBadge);

		// 氏名・職業・年齢
		var nameLabel = new Label();
		nameLabel.Text = $"{a.Name} ({JobLabel(a.JobClass)}・{a.Age}歳)";
		nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		nameLabel.AddThemeFontSizeOverride("font_size", 12);
		hbox.AddChild(nameLabel);

		// HP
		var hpLabel = new Label();
		hpLabel.Text = $"HP {a.CurrentHP}/{a.MaxHP}";
		hpLabel.CustomMinimumSize = new Vector2(76, 0);
		hpLabel.AddThemeFontSizeOverride("font_size", 11);
		hbox.AddChild(hpLabel);

		// 状態バッジ
		var statusBadge = new Label();
		statusBadge.CustomMinimumSize = new Vector2(96, 0);
		statusBadge.AddThemeFontSizeOverride("font_size", 11);

		if (isDispatched)
		{
			statusBadge.Text = "【出撃中】";
			statusBadge.AddThemeColorOverride("font_color", new Color(1.0f, 0.5f, 0.2f));
		}
		else if (a.Injury == InjurySeverity.Severe)
		{
			statusBadge.Text = "【重傷】";
			statusBadge.AddThemeColorOverride("font_color", new Color(1.0f, 0.3f, 0.3f));
		}
		else if (a.Injury == InjurySeverity.Light)
		{
			statusBadge.Text = "【軽傷】";
			statusBadge.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.3f));
		}
		else if (isInCurrentParty)
		{
			statusBadge.Text = "【当部隊配属中】";
			statusBadge.AddThemeColorOverride("font_color", new Color(0.4f, 0.8f, 1.0f));
		}
		else if (isInOtherParty)
		{
			statusBadge.Text = $"【{otherParty?.Name ?? "他部隊"}】";
			statusBadge.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.8f));
		}
		else if (isRetired)
		{
			statusBadge.Text = "【引退】";
			statusBadge.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
		}
		else
		{
			statusBadge.Text = "【待機中】";
			statusBadge.AddThemeColorOverride("font_color", new Color(0.4f, 1.0f, 0.4f));
		}
		hbox.AddChild(statusBadge);

		// 配属ボタン
		var assignButton = new Button();
		assignButton.Text = "配属 ＋";
		assignButton.CustomMinimumSize = new Vector2(76, 24);
		assignButton.AddThemeFontSizeOverride("font_size", 11);
		assignButton.Disabled = !canAssign;
		assignButton.Pressed += () => OnAssignAdventurerClicked(a);
		hbox.AddChild(assignButton);

		return panel;
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
