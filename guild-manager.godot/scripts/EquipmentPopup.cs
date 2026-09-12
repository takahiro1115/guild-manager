using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 装備の購入・着脱ポップアップ（仕様書 03 §4.2.2）。
///
/// FacilityPopupと同様、意思決定を強制しない（いつでも自由に開いたり閉じたりできる）。
/// 対象は常に「ステータス詳細パネルに表示中の冒険者」（MainDashboard.CurrentDetailAdventurer）。
/// カタログ全アイテムを一覧表示し、職業制限に反するものは選択できないようグレーアウトする。
/// </summary>
public partial class EquipmentPopup : PopupPanel
{
	private Label _titleLabel = null!;
	private ItemList _itemList = null!;
	private Label _statusLabel = null!;
	private Button _purchaseButton = null!;
	private Button _unequipWeaponButton = null!;
	private Button _unequipArmorButton = null!;
	private Button _unequipAccessory1Button = null!;
	private Button _unequipAccessory2Button = null!;

	private GameState _state = null!;
	private Adventurer _adventurer = null!;
	private EquipmentSystem _equipmentSystem = null!;
	private List<Item> _catalogOrder = new();

	/// <summary>ポップアップが閉じたことを通知する（所持金・装備の変化をUI側に反映させるため）。</summary>
	public event Action Closed = delegate { };

	public override void _Ready()
	{
		_titleLabel = GetNode<Label>("%TitleLabel");
		_itemList = GetNode<ItemList>("%ItemList");
		_statusLabel = GetNode<Label>("%EquipmentStatusLabel");
		_purchaseButton = GetNode<Button>("%PurchaseButton");
		_unequipWeaponButton = GetNode<Button>("%UnequipWeaponButton");
		_unequipArmorButton = GetNode<Button>("%UnequipArmorButton");
		_unequipAccessory1Button = GetNode<Button>("%UnequipAccessory1Button");
		_unequipAccessory2Button = GetNode<Button>("%UnequipAccessory2Button");

		_purchaseButton.Pressed += OnPurchasePressed;
		_unequipWeaponButton.Pressed += () => OnUnequipPressed(EquipmentSlot.Weapon);
		_unequipArmorButton.Pressed += () => OnUnequipPressed(EquipmentSlot.Armor);
		_unequipAccessory1Button.Pressed += () => OnUnequipPressed(EquipmentSlot.Accessory1);
		_unequipAccessory2Button.Pressed += () => OnUnequipPressed(EquipmentSlot.Accessory2);
		PopupHide += () => Closed.Invoke();
	}

	public void Open(GameState state, Adventurer adventurer, EquipmentSystem equipmentSystem)
	{
		_state = state;
		_adventurer = adventurer;
		_equipmentSystem = equipmentSystem;

		_titleLabel.Text = $"{adventurer.Name}（{adventurer.JobClass}）の装備";
		RefreshList();
		PopupCentered();
	}

	private void RefreshList()
	{
		_itemList.Clear();
		_catalogOrder = new List<Item>();

		foreach (var slot in new[] { EquipmentSlot.Weapon, EquipmentSlot.Armor, EquipmentSlot.Accessory1, EquipmentSlot.Accessory2 })
		{
			foreach (var item in ItemCatalog.GetBySlot(slot))
			{
				_catalogOrder.Add(item);
				bool allowed = item.IsAllowedFor(_adventurer.JobClass);
				string effect = item.EffectType == EquipmentEffectType.PersonalCpBonus
					? $"個人CP+{item.EffectValue}"
					: $"最大HP+{item.EffectValue}";
				string equippedMark = _adventurer.GetEquippedId(slot) == item.Id ? "【装備中】" : "";

				int index = _itemList.ItemCount;
				_itemList.AddItem($"[{SlotLabel(slot)}] {item.Name}　{effect}　{item.Price}G {equippedMark}");
				if (!allowed)
				{
					_itemList.SetItemDisabled(index, true);
					_itemList.SetItemTooltip(index, $"{_adventurer.JobClass}は装備できません。");
				}
			}
		}

		RefreshStatus();
	}

	private void RefreshStatus()
	{
		_statusLabel.Text = $"所持金: {_state.Gold} G　　現在の装備 " +
			$"武器:{EquipmentName(_adventurer.EquippedWeaponId)} " +
			$"防具:{EquipmentName(_adventurer.EquippedArmorId)} " +
			$"アクセ1:{EquipmentName(_adventurer.EquippedAccessory1Id)} " +
			$"アクセ2:{EquipmentName(_adventurer.EquippedAccessory2Id)}";
	}

	private void OnPurchasePressed()
	{
		var selected = _itemList.GetSelectedItems();
		if (selected.Length == 0)
		{
			_statusLabel.Text = "購入・装備するアイテムを選択してください。";
			return;
		}

		var item = _catalogOrder[selected[0]];
		if (!item.IsAllowedFor(_adventurer.JobClass))
		{
			_statusLabel.Text = $"{_adventurer.JobClass}は「{item.Name}」を装備できません。";
			return;
		}
		if (_state.Gold < item.Price)
		{
			_statusLabel.Text = $"資金が足りません（必要 {item.Price}G、所持 {_state.Gold}G）。";
			return;
		}

		_equipmentSystem.TryPurchaseAndEquip(_state, _adventurer, item.Id);
		RefreshList();
	}

	private void OnUnequipPressed(EquipmentSlot slot)
	{
		_equipmentSystem.Unequip(_adventurer, slot);
		RefreshList();
	}

	private static string EquipmentName(string itemId) => itemId == null ? "なし" : (ItemCatalog.FindById(itemId)?.Name ?? "（不明）");

	private static string SlotLabel(EquipmentSlot slot) => slot switch
	{
		EquipmentSlot.Weapon => "武器",
		EquipmentSlot.Armor => "防具",
		EquipmentSlot.Accessory1 => "アクセ1",
		EquipmentSlot.Accessory2 => "アクセ2",
		_ => slot.ToString()
	};
}
