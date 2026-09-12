using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Models;

/// <summary>
/// 派遣直前の一時的な入れ替え画面（仕様書 03 §4.0.2）。
///
/// 保存されているパーティー編成（SavedParty.MemberIds）は一切変更しない。今回だけの
/// 出撃メンバー一覧（プレーンなGuidのリスト）をこのポップアップ内だけで組み立て、
/// 「この編成で出撃する」が押されたら Applied イベントで呼び出し元へ結果を渡す。
/// 交代候補は「未編成の冒険者」または「他パーティーに所属中の冒険者」（一時的に
/// 借りてくる想定。SavedParty側からは何も除去しない）。
/// </summary>
public partial class TemporarySwapPopup : PopupPanel
{
	private ItemList _lineupList = null!;
	private Button _removeButton = null!;
	private ItemList _candidateList = null!;
	private Button _addButton = null!;
	private Label _statusLabel = null!;
	private Button _applyButton = null!;

	private GameState _state = null!;
	private List<Guid> _lineup = new();

	/// <summary>「この編成で出撃する」が押された時に、最終的な出撃メンバーId一覧を渡す。</summary>
	public event Action<List<Guid>> Applied = delegate { };

	/// <summary>ポップアップが閉じたことを通知する（適用の有無を問わない）。</summary>
	public event Action Closed = delegate { };

	public override void _Ready()
	{
		_lineupList = GetNode<ItemList>("%LineupList");
		_removeButton = GetNode<Button>("%RemoveButton");
		_candidateList = GetNode<ItemList>("%CandidateList");
		_addButton = GetNode<Button>("%AddButton");
		_statusLabel = GetNode<Label>("%StatusLabel");
		_applyButton = GetNode<Button>("%ApplyButton");

		_removeButton.Pressed += OnRemovePressed;
		_addButton.Pressed += OnAddPressed;
		_applyButton.Pressed += OnApplyPressed;
		PopupHide += () => Closed.Invoke();
	}

	/// <summary>
	/// 一時編成画面を開く。初期の出撃メンバー一覧（通常はSavedParty.MemberIdsのうち
	/// 現役ロースターに現存するもの）を渡す。
	/// </summary>
	public void Open(GameState state, IEnumerable<Guid> initialMemberIds)
	{
		_state = state;
		_lineup = initialMemberIds.Where(id => _state.Adventurers.Any(a => a.Id == id)).ToList();

		RefreshLists();
		PopupCentered();
	}

	private void RefreshLists()
	{
		_lineupList.Clear();
		foreach (var id in _lineup)
		{
			var member = _state.Adventurers.First(a => a.Id == id);
			string status = member.Injury == InjurySeverity.Severe ? "【重傷・出撃不可】" : member.IsDispatched ? "【派遣中・出撃不可】" : "";
			_lineupList.AddItem($"{member.Name}（{member.JobClass}） HP{member.CurrentHP}/{member.MaxHP} {status}");
		}

		_candidateList.Clear();
		foreach (var adventurer in _state.Adventurers.Where(a => !_lineup.Contains(a.Id)))
		{
			string status = adventurer.Injury == InjurySeverity.Severe ? "【重傷】" : adventurer.IsDispatched ? "【派遣中】" : "";
			_candidateList.AddItem($"{adventurer.Name}（{adventurer.JobClass}） HP{adventurer.CurrentHP}/{adventurer.MaxHP} {status}");
		}
	}

	private void OnRemovePressed()
	{
		var selected = _lineupList.GetSelectedItems();
		if (selected.Length == 0)
		{
			_statusLabel.Text = "外すメンバーを選択してください。";
			return;
		}

		_lineup.RemoveAt(selected[0]);
		RefreshLists();
	}

	private void OnAddPressed()
	{
		var selected = _candidateList.GetSelectedItems();
		if (selected.Length == 0)
		{
			_statusLabel.Text = "追加する冒険者を選択してください。";
			return;
		}

		if (_lineup.Count >= Party.MaxSlots)
		{
			_statusLabel.Text = "出撃メンバーは最大4名までです。先に誰かを外してください。";
			return;
		}

		var candidates = _state.Adventurers.Where(a => !_lineup.Contains(a.Id)).ToList();
		_lineup.Add(candidates[selected[0]].Id);
		RefreshLists();
	}

	private void OnApplyPressed()
	{
		Applied.Invoke(new List<Guid>(_lineup));
		Hide();
	}
}
