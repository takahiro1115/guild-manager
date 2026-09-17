using Godot;
using System;
using System.Linq;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 中央ペイン「編成」タブ（仕様書 03 §4.0.2・§5.3.1。旧 PartyFormationPopup をタブ埋め込み型へ移行）。
///
/// 左：保存済みパーティー一覧（作成・改名・削除）／中央：選択中パーティーのメンバー
/// （最大4名、外せる）／右：未編成の冒険者一覧（追加できる）／下：選択中パーティー内の
/// 相性険悪ペアの警告表示（→ 03 §5.3.1。表示のみで編成をブロックしない）。
/// 編成の変更は大迷宮タブの出撃部隊一覧にも影響するため、StateChanged で MainDashboard に
/// 全体の再描画を依頼する（DungeonPanel・ResearchPanel と同じ流儀）。
/// </summary>
public partial class PartyFormationPanel : VBoxContainer
{
	private ItemList _partyList = null!;
	private LineEdit _nameEdit = null!;
	private Button _createButton = null!;
	private Button _renameButton = null!;
	private Button _deleteButton = null!;
	private ItemList _memberList = null!;
	private Button _removeMemberButton = null!;
	private ItemList _unassignedList = null!;
	private Button _addMemberButton = null!;
	private RichTextLabel _compatibilityLabel = null!;
	private Label _statusLabel = null!;

	private GameState _state = null!;
	private PartyFormationSystem _partyFormationSystem = null!;

	/// <summary>
	/// 現在選択中のパーティー（未選択ならnull）。ポップアップ時代と違いタブは週送りのたびに
	/// 再描画されるため、同じゲーム状態である限り選択を維持する。
	/// </summary>
	private SavedParty _selectedParty;

	/// <summary>編成の変更でゲーム状態が変わったことを通知する。</summary>
	public event Action StateChanged = delegate { };

	public override void _Ready()
	{
		_partyList = GetNode<ItemList>("%PartyList");
		_nameEdit = GetNode<LineEdit>("%NameEdit");
		_createButton = GetNode<Button>("%CreateButton");
		_renameButton = GetNode<Button>("%RenameButton");
		_deleteButton = GetNode<Button>("%DeleteButton");
		_memberList = GetNode<ItemList>("%MemberList");
		_removeMemberButton = GetNode<Button>("%RemoveMemberButton");
		_unassignedList = GetNode<ItemList>("%UnassignedList");
		_addMemberButton = GetNode<Button>("%AddMemberButton");
		_compatibilityLabel = GetNode<RichTextLabel>("%CompatibilityLabel");
		_statusLabel = GetNode<Label>("%StatusLabel");

		_partyList.ItemSelected += OnPartySelected;
		_createButton.Pressed += OnCreatePressed;
		_renameButton.Pressed += OnRenamePressed;
		_deleteButton.Pressed += OnDeletePressed;
		_removeMemberButton.Pressed += OnRemoveMemberPressed;
		_addMemberButton.Pressed += OnAddMemberPressed;
	}

	public void Initialize(PartyFormationSystem partyFormationSystem)
	{
		_partyFormationSystem = partyFormationSystem;
	}

	/// <summary>最新のゲーム状態でタブ全体を再描画する（MainDashboard.RefreshAllから毎回呼ぶ）。</summary>
	public void Refresh(GameState state)
	{
		// 新規ゲーム・ロードでゲーム状態自体が差し替わった場合は、旧状態の選択を持ち越さない。
		if (!ReferenceEquals(_state, state))
		{
			_selectedParty = null;
			_nameEdit.Text = "";
			_statusLabel.Text = "";
		}

		_state = state;
		RefreshPartyList();
		RefreshMemberAndUnassignedLists();
		RefreshCompatibilityWarnings();
	}

	private void RefreshPartyList()
	{
		_partyList.Clear();
		foreach (var party in _state.SavedParties)
		{
			int memberCount = party.MemberIds.Count(id => _state.Adventurers.Any(a => a.Id == id));
			_partyList.AddItem($"{party.Name}（{memberCount}/4名）");
		}

		if (_selectedParty != null)
		{
			int index = _state.SavedParties.IndexOf(_selectedParty);
			if (index >= 0) _partyList.Select(index);
			else _selectedParty = null; // 削除済み
		}
	}

	private void RefreshMemberAndUnassignedLists()
	{
		_memberList.Clear();
		if (_selectedParty != null)
		{
			foreach (var id in _selectedParty.MemberIds)
			{
				var member = _state.Adventurers.FirstOrDefault(a => a.Id == id);
				_memberList.AddItem(member != null
					? $"{member.Name}（{member.JobClass}） HP{member.CurrentHP}/{member.MaxHP}"
					: "（不明・現役ロースターに存在しません）");
			}
		}

		_unassignedList.Clear();
		foreach (var adventurer in PartyFormationSystem.GetUnassignedAdventurers(_state))
		{
			string status = adventurer.Injury == InjurySeverity.Severe ? "【重傷】" : adventurer.IsDispatched ? "【派遣中】" : "";
			_unassignedList.AddItem($"{adventurer.Name}（{adventurer.JobClass}） HP{adventurer.CurrentHP}/{adventurer.MaxHP} {status}");
		}
	}

	/// <summary>
	/// 選択中パーティー内の相性険悪ペア（→ 03 §5.3.1、30未満）を警告表示する。
	/// 表示のみで編成自体はブロックしない（→ 仕様どおり）。
	/// </summary>
	private void RefreshCompatibilityWarnings()
	{
		_compatibilityLabel.Text = "";
		if (_selectedParty == null || _selectedParty.MemberIds.Count < 2) return;

		var pairs = PartyFormationSystem.GetCompatibilityPairs(_state, _selectedParty.MemberIds);
		var hostilePairs = pairs.Where(p => p.IsHostile).ToList();

		if (hostilePairs.Count == 0)
		{
			_compatibilityLabel.AppendText("[color=lime]険悪なペアはありません。[/color]");
			return;
		}

		foreach (var pair in hostilePairs)
		{
			string nameA = _state.Adventurers.FirstOrDefault(a => a.Id == pair.IdA)?.Name ?? "（不明）";
			string nameB = _state.Adventurers.FirstOrDefault(a => a.Id == pair.IdB)?.Name ?? "（不明）";
			_compatibilityLabel.AppendText($"[color=red]⚠ {nameA} と {nameB} は険悪です。[/color]\n");
		}
	}

	private void OnPartySelected(long index)
	{
		_selectedParty = _state.SavedParties[(int)index];
		_nameEdit.Text = _selectedParty.Name;
		RefreshMemberAndUnassignedLists();
		RefreshCompatibilityWarnings();
	}

	private void OnCreatePressed()
	{
		string name = _nameEdit.Text.Trim();
		if (name.Length == 0)
		{
			_statusLabel.Text = "パーティー名を入力してください。";
			return;
		}

		_selectedParty = _partyFormationSystem.CreateParty(_state, name);
		_nameEdit.Text = "";
		_statusLabel.Text = $"「{name}」を作成した。";
		StateChanged.Invoke();
	}

	private void OnRenamePressed()
	{
		if (_selectedParty == null)
		{
			_statusLabel.Text = "改名するパーティーを選択してください。";
			return;
		}

		string name = _nameEdit.Text.Trim();
		if (name.Length == 0)
		{
			_statusLabel.Text = "パーティー名を入力してください。";
			return;
		}

		_partyFormationSystem.RenameParty(_selectedParty, name);
		_statusLabel.Text = "名前を変更した。";
		StateChanged.Invoke();
	}

	private void OnDeletePressed()
	{
		if (_selectedParty == null)
		{
			_statusLabel.Text = "削除するパーティーを選択してください。";
			return;
		}

		string name = _selectedParty.Name;
		_partyFormationSystem.DeleteParty(_state, _selectedParty);
		_selectedParty = null;
		_nameEdit.Text = "";
		_statusLabel.Text = $"「{name}」を削除した（未編成に戻った）。";
		StateChanged.Invoke();
	}

	private void OnAddMemberPressed()
	{
		if (_selectedParty == null)
		{
			_statusLabel.Text = "先にパーティーを選択（または新規作成）してください。";
			return;
		}

		var selected = _unassignedList.GetSelectedItems();
		if (selected.Length == 0)
		{
			_statusLabel.Text = "追加する冒険者を選択してください。";
			return;
		}

		var unassigned = PartyFormationSystem.GetUnassignedAdventurers(_state).ToList();
		var adventurer = unassigned[selected[0]];

		if (!_partyFormationSystem.TryAssignMember(_state, _selectedParty, adventurer.Id))
		{
			_statusLabel.Text = $"「{_selectedParty.Name}」は既に4名編成されています。";
			return;
		}

		_statusLabel.Text = $"{adventurer.Name} を「{_selectedParty.Name}」に編成した。";
		StateChanged.Invoke();
	}

	private void OnRemoveMemberPressed()
	{
		if (_selectedParty == null)
		{
			_statusLabel.Text = "パーティーを選択してください。";
			return;
		}

		var selected = _memberList.GetSelectedItems();
		if (selected.Length == 0)
		{
			_statusLabel.Text = "外すメンバーを選択してください。";
			return;
		}

		var memberId = _selectedParty.MemberIds[selected[0]];
		var member = _state.Adventurers.FirstOrDefault(a => a.Id == memberId);
		_partyFormationSystem.RemoveMember(_selectedParty, memberId);
		_statusLabel.Text = $"{member?.Name ?? "（不明）"} を未編成に戻した。";
		StateChanged.Invoke();
	}
}
