using Godot;
using System;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 施設Lv投資のポップアップ（仕様書 03 §6・§6.1）。
///
/// 新春採用試験ポップアップ（RecruitmentPopup）とは異なり、意思決定を強制しない
/// （いつでも自由に開いたり閉じたりできる）。着工は同時1件のみで、他の施設が
/// 工事中の間は着工ボタンが失敗する（理由は状況ラベルに表示）。
/// </summary>
public partial class FacilityPopup : PopupPanel
{
	private ItemList _facilityList = null!;
	private Button _startConstructionButton = null!;
	private Label _statusLabel = null!;

	private GameState _state = null!;
	private FacilitySystem _facilitySystem = null!;

	/// <summary>ポップアップが閉じたことを通知する（着工・所持金の変化をUI側に反映させるため）。</summary>
	public event Action Closed = delegate { };

	public override void _Ready()
	{
		_facilityList = GetNode<ItemList>("%FacilityList");
		_startConstructionButton = GetNode<Button>("%StartConstructionButton");
		_statusLabel = GetNode<Label>("%FacilityStatusLabel");

		_startConstructionButton.Pressed += OnStartConstructionPressed;
		PopupHide += () => Closed.Invoke();
	}

	public void Open(GameState state, FacilitySystem facilitySystem)
	{
		_state = state;
		_facilitySystem = facilitySystem;

		RefreshList();
		PopupCentered();
	}

	private void RefreshList()
	{
		_facilityList.Clear();
		foreach (var facility in _state.Facilities)
		{
			string queueStatus = _state.UnderConstruction != null && _state.UnderConstruction.Type == facility.Type
				? $"（工事中：残り{_state.UnderConstruction.WeeksRemaining}週）"
				: facility.CurrentLevel >= FacilityBalance.MaxLevel
					? "（最大Lv到達）"
					: $"（着工費{FacilityBalance.GetUpgradeCost(facility.Type, facility.CurrentLevel)}G・" +
					  $"工期{FacilityBalance.GetConstructionWeeks(facility.Type, facility.CurrentLevel)}週）";

			_facilityList.AddItem($"{FacilityLabel(facility.Type)} Lv{facility.CurrentLevel} {queueStatus}");
		}

		_statusLabel.Text = _state.UnderConstruction != null
			? $"{FacilityLabel(_state.UnderConstruction.Type)}が工事中です（残り{_state.UnderConstruction.WeeksRemaining}週）。他の施設は着工できません。"
			: $"所持金: {_state.Gold} G";
	}

	private void OnStartConstructionPressed()
	{
		var selected = _facilityList.GetSelectedItems();
		if (selected.Length == 0)
		{
			_statusLabel.Text = "着工する施設を選択してください。";
			return;
		}

		var facility = _state.Facilities[selected[0]];
		if (_facilitySystem.TryStartConstruction(_state, facility.Type))
		{
			_statusLabel.Text = $"{FacilityLabel(facility.Type)}の着工を開始した。";
			RefreshList();
		}
		else if (_state.UnderConstruction != null)
		{
			_statusLabel.Text = "他の施設が工事中のため着工できません。";
		}
		else if (facility.CurrentLevel >= FacilityBalance.MaxLevel)
		{
			_statusLabel.Text = $"{FacilityLabel(facility.Type)}は既に最大Lvです。";
		}
		else
		{
			_statusLabel.Text = "資金が足りません。";
		}
	}

	private static string FacilityLabel(FacilityType type) => type switch
	{
		FacilityType.Dormitory => "宿舎",
		FacilityType.Infirmary => "医務室",
		FacilityType.WarRoom => "作戦資料室",
		FacilityType.Tavern => "ギルド酒場",
		FacilityType.WarriorHall => "戦士訓練所",
		FacilityType.Church => "教会",
		FacilityType.MageLab => "魔法研究所",
		FacilityType.ScoutPost => "斥候所",
		FacilityType.RecruitmentOffice => "冒険者支援室",
		_ => type.ToString()
	};
}
