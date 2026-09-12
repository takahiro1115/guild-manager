using Godot;
using System;
using System.Linq;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 顧問（教官・参謀・スカウト）の役職割り当てポップアップ（仕様書 03 §7）。
///
/// FacilityPopupと同様、意思決定を強制しない（いつでも自由に開いたり閉じたりできる）。
/// 最小限のUI：引退済み一覧から候補を選び、6つのボタン（教官×4施設・参謀・スカウト）で
/// その候補を該当スロットへ任命する（詳細な作り込みはPhase 4）。
/// </summary>
public partial class AdvisorPopup : PopupPanel
{
	private ItemList _candidateList = null!;
	private Button _assignWarriorHallButton = null!;
	private Button _assignChurchButton = null!;
	private Button _assignMageLabButton = null!;
	private Button _assignScoutPostButton = null!;
	private Button _assignAdvisorButton = null!;
	private Button _assignScoutMasterButton = null!;
	private Label _statusLabel = null!;

	private GameState _state = null!;
	private AdvisorSystem _advisorSystem = null!;

	/// <summary>ポップアップが閉じたことを通知する（役職割り当ての変化をUI側に反映させるため）。</summary>
	public event Action Closed = delegate { };

	public override void _Ready()
	{
		_candidateList = GetNode<ItemList>("%CandidateList");
		_assignWarriorHallButton = GetNode<Button>("%AssignWarriorHallButton");
		_assignChurchButton = GetNode<Button>("%AssignChurchButton");
		_assignMageLabButton = GetNode<Button>("%AssignMageLabButton");
		_assignScoutPostButton = GetNode<Button>("%AssignScoutPostButton");
		_assignAdvisorButton = GetNode<Button>("%AssignAdvisorButton");
		_assignScoutMasterButton = GetNode<Button>("%AssignScoutMasterButton");
		_statusLabel = GetNode<Label>("%StatusLabel");

		_assignWarriorHallButton.Pressed += () => OnAssignTrainerPressed(FacilityType.WarriorHall);
		_assignChurchButton.Pressed += () => OnAssignTrainerPressed(FacilityType.Church);
		_assignMageLabButton.Pressed += () => OnAssignTrainerPressed(FacilityType.MageLab);
		_assignScoutPostButton.Pressed += () => OnAssignTrainerPressed(FacilityType.ScoutPost);
		_assignAdvisorButton.Pressed += OnAssignAdvisorPressed;
		_assignScoutMasterButton.Pressed += OnAssignScoutMasterPressed;
		PopupHide += () => Closed.Invoke();
	}

	public void Open(GameState state, AdvisorSystem advisorSystem)
	{
		_state = state;
		_advisorSystem = advisorSystem;

		RefreshList();
		PopupCentered();
	}

	private void RefreshList()
	{
		_candidateList.Clear();
		foreach (var candidate in _state.RetiredAdventurers)
		{
			_candidateList.AddItem(
				$"{candidate.Name}（{candidate.JobClass}） 引退時{candidate.RetiredAtAge}歳" +
				$"　ピークSTR{candidate.PeakSTR}/VIT{candidate.PeakVIT}/AGI{candidate.PeakAGI}" +
				$"/DEX{candidate.PeakDEX}/MND{candidate.PeakMND}/INT{candidate.PeakINT}/LDR{candidate.PeakLDR}");
		}

		_statusLabel.Text = BuildStatusText();
	}

	private string BuildStatusText()
	{
		string TrainerName(FacilityType type) =>
			_state.AssignedTrainers.TryGetValue(type, out var id) && id != null
				? FindName(id.Value)
				: "未配置";

		string advisorName = _state.AssignedAdvisor != null ? FindName(_state.AssignedAdvisor.Value) : "未配置";
		string scoutMasterName = _state.AssignedScoutMaster != null ? FindName(_state.AssignedScoutMaster.Value) : "未任命";

		return $"現在の配置　戦士訓練所:{TrainerName(FacilityType.WarriorHall)}　教会:{TrainerName(FacilityType.Church)}\n" +
			$"魔法研究所:{TrainerName(FacilityType.MageLab)}　斥候所:{TrainerName(FacilityType.ScoutPost)}\n" +
			$"参謀本部:{advisorName}　採用本部:{scoutMasterName}";
	}

	private string FindName(Guid id) =>
		_state.RetiredAdventurers.FirstOrDefault(a => a.Id == id)?.Name ?? "（不明）";

	private bool TryGetSelectedCandidateId(out Guid candidateId)
	{
		var selected = _candidateList.GetSelectedItems();
		if (selected.Length == 0)
		{
			_statusLabel.Text = "候補を選択してください。";
			candidateId = default;
			return false;
		}

		candidateId = _state.RetiredAdventurers[selected[0]].Id;
		return true;
	}

	private void OnAssignTrainerPressed(FacilityType facility)
	{
		if (!TryGetSelectedCandidateId(out var candidateId)) return;

		_advisorSystem.TryAssignTrainer(_state, facility, candidateId);
		RefreshList();
	}

	private void OnAssignAdvisorPressed()
	{
		if (!TryGetSelectedCandidateId(out var candidateId)) return;

		_advisorSystem.TryAssignAdvisor(_state, candidateId);
		RefreshList();
	}

	private void OnAssignScoutMasterPressed()
	{
		if (!TryGetSelectedCandidateId(out var candidateId)) return;

		_advisorSystem.TryAssignScoutMaster(_state, candidateId);
		RefreshList();
	}
}
