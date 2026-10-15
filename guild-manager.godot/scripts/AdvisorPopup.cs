using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 顧問（教官・参謀・スカウト）の役職割り当てポップアップ（仕様書 03 §7）。
///
/// FacilityPopupと同様、意思決定を強制しない（いつでも自由に開いたり閉じたりできる）。
/// 最小限のUI：引退済み一覧から候補を選び、5つのボタン（教官×3施設・参謀・スカウト。§0.75で訓練施設を3つにした）で
/// その候補を該当スロットへ任命する（詳細な作り込みはPhase 4）。
/// </summary>
public partial class AdvisorPopup : PopupPanel
{
	private ItemList _candidateList = null!;
	private Button _assignDrillHallButton = null!;
	private Button _assignAcademyButton = null!;
	private Button _assignSkillHallButton = null!;
	private Button _assignAdvisorButton = null!;
	private Button _assignScoutMasterButton = null!;
	private Label _statusLabel = null!;

	private GameState _state = null!;
	/// <summary>一覧に出している候補（ギルドを去った子は出さない、§0.105）。並びは _candidateList と同じ。</summary>
	private readonly List<Adventurer> _candidates = new();
	private AdvisorSystem _advisorSystem = null!;

	/// <summary>ポップアップが閉じたことを通知する（役職割り当ての変化をUI側に反映させるため）。</summary>
	public event Action Closed = delegate { };

	public override void _Ready()
	{
		_candidateList = GetNode<ItemList>("%CandidateList");
		_assignDrillHallButton = GetNode<Button>("%AssignDrillHallButton");
		_assignAcademyButton = GetNode<Button>("%AssignAcademyButton");
		_assignSkillHallButton = GetNode<Button>("%AssignSkillHallButton");
		_assignAdvisorButton = GetNode<Button>("%AssignAdvisorButton");
		_assignScoutMasterButton = GetNode<Button>("%AssignScoutMasterButton");
		_statusLabel = GetNode<Label>("%StatusLabel");

		_assignDrillHallButton.Pressed += () => OnAssignTrainerPressed(FacilityType.DrillHall);
		_assignAcademyButton.Pressed += () => OnAssignTrainerPressed(FacilityType.Academy);
		_assignSkillHallButton.Pressed += () => OnAssignTrainerPressed(FacilityType.SkillHall);
		_assignAdvisorButton.Pressed += OnAssignAdvisorPressed;
		_assignScoutMasterButton.Pressed += OnAssignScoutMasterPressed;
		// 背景が透けて読みにくいため、他のポップアップと同じ不透明な面にする
		var panelStyle = new StyleBoxFlat { BgColor = new Color(0.1f, 0.1f, 0.12f), BorderColor = new Color(0.35f, 0.35f, 0.42f) };
		panelStyle.SetBorderWidthAll(2);
		panelStyle.SetCornerRadiusAll(6);
		panelStyle.SetContentMarginAll(10);
		AddThemeStyleboxOverride("panel", panelStyle);
		PopupHide += () => Closed.Invoke();

		// ギルドの店（§0.134）：ボタンはコードで足す（スカウトのボタンの隣）
		var parent = _assignScoutMasterButton.GetParent();
		foreach (var kind in Enum.GetValues<ShopKind>())
		{
			var k = kind;
			var button = new Button { Text = kind == ShopKind.ItemShop ? "🏪 道具屋で働く" : "🍺 酒場で働く" };
			button.Pressed += () => OnAssignShopPressed(k);
			parent.AddChild(button);
		}
		var leave = new Button { Text = "店から外す" };
		leave.Pressed += OnLeaveShopPressed;
		parent.AddChild(leave);
	}

	/// <summary>
	/// focus を渡すと（施設カードの「任命」から開いたとき）、その施設の役職のボタンにフォーカスを置き、
	/// 状態欄で「どの役職に誰を任命するか」を案内する。
	/// </summary>
	public void Open(GameState state, AdvisorSystem advisorSystem, FacilityType? focus = null)
	{
		_state = state;
		_advisorSystem = advisorSystem;

		RefreshList();
		PopupCentered();

		if (focus is { } facility)
		{
			var button = facility switch
			{
				FacilityType.DrillHall => _assignDrillHallButton,
				FacilityType.Academy => _assignAcademyButton,
				FacilityType.SkillHall => _assignSkillHallButton,
				FacilityType.WarRoom => _assignAdvisorButton,
				FacilityType.RecruitmentOffice => _assignScoutMasterButton,
				_ => null,
			};
			if (button != null)
			{
				button.GrabFocus();
				_statusLabel.Text = $"▶ 候補を選んで、枠のついた「{button.Text}」を押す。\n" + _statusLabel.Text;
			}
		}
	}

	private void RefreshList()
	{
		_candidateList.Clear();
		_candidates.Clear();
		if (_state?.RetiredAdventurers != null)
		{
			foreach (var candidate in _state.RetiredAdventurers.Where(r => !r.LeftGuild)) // ギルドを去った子は任命できない（§0.105）
			{
				_candidates.Add(candidate);
				int? leave = GuildPolicySystem.LeaveWeek(_state, candidate);
				_candidateList.AddItem(
					(candidate.IsGuest
						? $"{candidate.Name}（白百合の杖から派遣・教官だけ・残り{Math.Max(0, (_state.GuestTrainerUntilWeek ?? _state.WeekNumber) - _state.WeekNumber)}週）"
						: $"{candidate.Name}（{candidate.JobClass}） 引退時{candidate.RetiredAtAge}歳") +
					(leave is int w ? $"　⚠ 任命しないと{GameCalendar.FormatMonth(w)} 第{GameCalendar.WeekOfMonth(w)}週に去る" : "") + // §0.105
					$"　STR{candidate.STR}/VIT{candidate.VIT}/AGI{candidate.AGI}" +
					$"/DEX{candidate.DEX}/MND{candidate.MND}/INT{candidate.INT}/LDR{candidate.LDR}");
			}
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

		string ShopText(ShopKind kind)
		{
			var workers = GuildShopSystem.Workers(_state, kind);
			var names = workers.Select(id => _state.RetiredAdventurers.FirstOrDefault(a => a.Id == id)).OfType<Adventurer>()
				.Select(a => $"{a.Name}（月{GuildShopSystem.SalesOf(_state, a, kind)}G）");
			return $"{GuildShopSystem.Label(kind)}［{workers.Count}/{GuildShopSystem.Slots(_state, kind)}］:{(workers.Count == 0 ? "なし" : string.Join("・", names))}";
		}

		return $"現在の配置　鍛錬所:{TrainerName(FacilityType.DrillHall)}　学問所:{TrainerName(FacilityType.Academy)}　技巧所:{TrainerName(FacilityType.SkillHall)}\n" +
			$"作戦資料室:{advisorName}　冒険者支援室:{scoutMasterName}\n" +
			$"{ShopText(ShopKind.ItemShop)}　{ShopText(ShopKind.Tavern)}\n" +
			"（道具屋は DEX・INT と倉庫の在庫、酒場は LDR・MND と二つ名・殿堂入りで売上が伸びる。働き手には手当を払う）";
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

		candidateId = _candidates[selected[0]].Id;
		return true;
	}

	private void OnAssignTrainerPressed(FacilityType facility)
	{
		if (!TryGetSelectedCandidateId(out var candidateId)) return;

		if (_state.GetFacilityLevel(facility) < 1)
		{
			_statusLabel.Text = $"{FacilityLabel(facility)}が未建設（Lv0）のため配置できません。施設投資で建設してください。";
			return;
		}

		_advisorSystem.TryAssignTrainer(_state, facility, candidateId);
		RefreshList();
	}

	private void OnAssignAdvisorPressed()
	{
		if (!TryGetSelectedCandidateId(out var candidateId)) return;

		if (_state.GetFacilityLevel(FacilityType.WarRoom) < 1)
		{
			_statusLabel.Text = "作戦資料室が未建設（Lv0）のため参謀を配置できません。施設投資で建設してください。";
			return;
		}

		_advisorSystem.TryAssignAdvisor(_state, candidateId);
		RefreshList();
	}

	private void OnAssignScoutMasterPressed()
	{
		if (!TryGetSelectedCandidateId(out var candidateId)) return;

		if (_state.GetFacilityLevel(FacilityType.RecruitmentOffice) < 1)
		{
			_statusLabel.Text = "冒険者支援室が未建設（Lv0）のためスカウトを配置できません。施設投資で建設してください。";
			return;
		}

		_advisorSystem.TryAssignScoutMaster(_state, candidateId);
		RefreshList();
	}

	private void OnAssignShopPressed(ShopKind kind)
	{
		if (!TryGetSelectedCandidateId(out var candidateId)) return;
		var candidate = _candidates.First(c => c.Id == candidateId);
		if (candidate.IsGuest)
		{
			_statusLabel.Text = "白百合の杖から派遣された教官は、店では働けません。";
			return;
		}
		if (!GuildShopSystem.TryAssign(_state, kind, candidateId))
		{
			_statusLabel.Text = $"{GuildShopSystem.Label(kind)}は満員です（{GuildShopSystem.Slots(_state, kind)}人まで" +
				(kind == ShopKind.Tavern ? "。酒場のLvを上げると増える" : "") + "）。\n" + BuildStatusText();
			return;
		}
		RefreshList();
	}

	private void OnLeaveShopPressed()
	{
		if (!TryGetSelectedCandidateId(out var candidateId)) return;
		GuildShopSystem.Unassign(_state, candidateId);
		RefreshList();
	}

	private static string FacilityLabel(FacilityType type) => type switch
	{
		FacilityType.DrillHall => "鍛錬所",
		FacilityType.Academy => "学問所",
		FacilityType.SkillHall => "技巧所",
		_ => type.ToString()
	};
}
