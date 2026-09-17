using Godot;
using System;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 中央ペイン「施設」タブ（仕様書 03 §6・§6.1。旧 FacilityPopup をタブ埋め込み型へ移行）。
///
/// 着工は同時1件のみで、他の施設が工事中の間は着工ボタンが失敗する（理由は状況ラベルに表示）。
/// 所持金等の画面全体の再描画は MainDashboard の責務のため、StateChanged イベントで依頼する
/// （DungeonPanel・ResearchPanel と同じ流儀）。
/// </summary>
public partial class FacilityPanel : VBoxContainer
{
	private ItemList _facilityList = null!;
	private Button _startConstructionButton = null!;
	private Label _statusLabel = null!;

	private GameState _state = null!;
	private FacilitySystem _facilitySystem = null!;

	/// <summary>着工でゲーム状態（所持金・建設キュー）が変わったことを通知する。</summary>
	public event Action StateChanged = delegate { };

	public override void _Ready()
	{
		_facilityList = GetNode<ItemList>("%FacilityList");
		_startConstructionButton = GetNode<Button>("%StartConstructionButton");
		_statusLabel = GetNode<Label>("%FacilityStatusLabel");

		_startConstructionButton.Pressed += OnStartConstructionPressed;
	}

	public void Initialize(FacilitySystem facilitySystem)
	{
		_facilitySystem = facilitySystem;
	}

	/// <summary>最新のゲーム状態でタブ全体を再描画する（MainDashboard.RefreshAllから毎回呼ぶ）。</summary>
	public void Refresh(GameState state)
	{
		_state = state;
		RefreshList();
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
			StateChanged.Invoke();
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
