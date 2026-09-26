#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 装備の換装・購入ポップアップ（仕様書 03 §4.2.2）。
///
/// FacilityPopupと同様、意思決定を強制しない（いつでも自由に開いたり閉じたりできる）。
/// 対象は常に「冒険者人事パネルに表示中の冒険者」（→ AdventurerPanel.EquipmentRequested）。
///
/// 2026年9月改訂（ギルド保管庫からの着脱、→ §4.2.2・§4.7）：
///  - 上段：現在の装備4枠と「外す」ボタン。外した武具はギルド保管庫（GameState.Armory）へ戻る。
///  - 中段：**ギルド保管庫の在庫**一覧。遺物の鑑定や押し出しで溜まった現物をここから装備する。
///  - 下段：従来のカタログ購入（即時購入して装備。押し出された旧装備は保管庫へ戻る）。
/// 職業制限に反するものはどちらの一覧でも選択できないようグレーアウトする。
/// 出撃中の冒険者は着脱不可（→ EquipmentSystem.CanChangeEquipment。AdventurerPanel側でも
/// ボタンをDisabledにしているが、こちらでも独立してガードする）。
/// </summary>
public partial class EquipmentPopup : PopupPanel
{
	private Label _titleLabel = null!;
	private ItemList _itemList = null!;
	private ItemList _armoryList = null!;
	private RichTextLabel _statusLabel = null!;
	private Button _purchaseButton = null!;
	private Button _equipButton = null!;
	private Button _unequipWeaponButton = null!;
	private Button _unequipArmorButton = null!;
	private Button _unequipAccessory1Button = null!;
	private Button _unequipAccessory2Button = null!;

	private GameState _state = null!;
	private Adventurer _adventurer = null!;
	private EquipmentSystem _equipmentSystem = null!;

	/// <summary>カタログ一覧の行番号 → カタログ定義。</summary>
	private List<Item> _catalogOrder = new();

	/// <summary>保管庫一覧の行番号 → 在庫の個体（→ GameState.Armory）。</summary>
	private List<EquipmentItem> _armoryOrder = new();

	/// <summary>直近の操作結果メッセージ（再描画をまたいで表示を保つ）。</summary>
	private string _message = "";

	/// <summary>ポップアップが閉じたことを通知する（所持金・装備の変化をUI側に反映させるため）。</summary>
	public event Action Closed = delegate { };

	public override void _Ready()
	{
		_titleLabel = GetNode<Label>("%TitleLabel");
		_itemList = GetNode<ItemList>("%ItemList");
		_armoryList = GetNode<ItemList>("%ArmoryList");
		_statusLabel = GetNode<RichTextLabel>("%EquipmentStatusLabel");
		_purchaseButton = GetNode<Button>("%PurchaseButton");
		_equipButton = GetNode<Button>("%EquipButton");
		_unequipWeaponButton = GetNode<Button>("%UnequipWeaponButton");
		_unequipArmorButton = GetNode<Button>("%UnequipArmorButton");
		_unequipAccessory1Button = GetNode<Button>("%UnequipAccessory1Button");
		_unequipAccessory2Button = GetNode<Button>("%UnequipAccessory2Button");

		_purchaseButton.Pressed += OnPurchasePressed;
		_equipButton.Pressed += OnEquipPressed;
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
		_message = "";

		_titleLabel.Text = $"{adventurer.Name}（{AdventurerPanel.JobLabel(adventurer.JobClass)}）の装備";
		RefreshAll();
		PopupCentered();
	}

	private void RefreshAll()
	{
		RefreshArmoryList();
		RefreshCatalogList();
		RefreshStatus();
		RefreshButtonGuards();
	}

	/// <summary>
	/// ギルド保管庫の在庫一覧（→ GameState.Armory）。装備枠ごとに並べ、職業制限に反するものは
	/// グレーアウトする（所持していること自体は見えるようにしたいため、非表示にはしない）。
	/// §0.40：同じ「鉄の剣」が複数あってもどの個体か見分けられるよう、行はアフィックス込みの表示名と
	/// 内訳込みの性能（→ EquipmentItem.DisplayName・DescribeEffects）で書き、文字色をアフィックスの付与数で変える
	/// （→ ItemColorHelper。1枠＝水色・2枠＝黄緑）。同じ枠の中ではアフィックスの多い個体を上に並べる。
	/// </summary>
	private void RefreshArmoryList()
	{
		_armoryList.Clear();
		_armoryOrder = new List<EquipmentItem>();

		// カタログから引けない個体（カタログから消えた武具の旧データ）は末尾にまとめる。
		var ordered = _state.Armory
			.OrderBy(e => e.GetSlot().HasValue ? (int)e.GetSlot()!.Value : int.MaxValue)
			.ThenByDescending(e => ItemColorHelper.GetTier(e))
			.ThenBy(e => e.DisplayName, StringComparer.Ordinal)
			.ToList();

		if (ordered.Count == 0)
		{
			int emptyIndex = _armoryList.AddItem("保管庫は空。遺物の鑑定（倉庫・遺物タブ）や装備の付け替えで在庫が溜まる。");
			_armoryList.SetItemDisabled(emptyIndex, true);
			return;
		}

		foreach (var entry in ordered)
		{
			var definition = entry.GetDefinition();
			_armoryOrder.Add(entry);
			int index = _armoryList.ItemCount;

			if (definition == null)
			{
				_armoryList.AddItem($"[不明] {entry.Name}（カタログ定義が見つからない旧データ）");
				_armoryList.SetItemDisabled(index, true);
				continue;
			}

			string acquired = entry.AcquiredAtWeek > 0 ? $"　{GameCalendar.Format(entry.AcquiredAtWeek)} {entry.AcquiredFrom}" : "";
			_armoryList.AddItem($"[{SlotLabel(definition.Slot)}] {entry.DisplayName}　{entry.DescribeEffects()}{acquired}");
			_armoryList.SetItemCustomFgColor(index, ItemColorHelper.GetItemColor(entry));
			string detail = $"{entry.DisplayName}\n{entry.DescribeEffects()}";

			if (!definition.IsAllowedFor(_adventurer.JobClass))
			{
				_armoryList.SetItemDisabled(index, true);
				_armoryList.SetItemTooltip(index, $"{AdventurerPanel.JobLabel(_adventurer.JobClass)}は装備できません。\n{detail}");
			}
			else
			{
				_armoryList.SetItemTooltip(index, detail);
			}
		}
	}

	private void RefreshCatalogList()
	{
		_itemList.Clear();
		_catalogOrder = new List<Item>();

		foreach (var slot in Adventurer.AllSlots)
		{
			foreach (var item in ItemCatalog.GetBySlot(slot))
			{
				_catalogOrder.Add(item);
				int index = _itemList.ItemCount;
				string equippedMark = _adventurer.GetEquippedId(slot) == item.Id ? "　【装備中】" : "";
				_itemList.AddItem($"[{SlotLabel(slot)}] {item.Name}　{EffectText(item)}　{item.Price}G{equippedMark}");

				if (!item.IsAllowedFor(_adventurer.JobClass))
				{
					_itemList.SetItemDisabled(index, true);
					_itemList.SetItemTooltip(index, $"{AdventurerPanel.JobLabel(_adventurer.JobClass)}は装備できません。");
				}
				else if (_state.Gold < item.Price)
				{
					_itemList.SetItemTooltip(index, $"資金不足（必要 {item.Price}G、所持 {_state.Gold}G）。");
				}
			}
		}
	}

	private void RefreshStatus()
	{
		_statusLabel.Clear();
		_statusLabel.AppendText($"[b]所持金[/b]：{_state.Gold} G　／　[b]最大HP[/b]：{_adventurer.CurrentHP}/{_adventurer.MaxHP}" +
			$"　／　[b]装備補正[/b]：最大HP +{_adventurer.GetEquipmentHpBonus()}、能力値 {EquippedStatText()}\n");

		var parts = Adventurer.AllSlots.Select(slot =>
		{
			var equipped = _adventurer.GetEquipped(slot);
			return $"{SlotLabel(slot)}:{(equipped == null ? "[color=gray]なし[/color]" : ItemColorHelper.GetColoredBBCode(equipped))}";
		});
		_statusLabel.AppendText($"[b]現在の装備[/b]　{string.Join("　", parts)}");

		if (!string.IsNullOrEmpty(_message))
			_statusLabel.AppendText($"\n{_message}");
	}

	/// <summary>出撃中は着脱不可、空きスロットは「外す」不可（→ EquipmentSystem.CanChangeEquipment）。</summary>
	private void RefreshButtonGuards()
	{
		bool changeable = EquipmentSystem.CanChangeEquipment(_adventurer);

		_equipButton.Disabled = !changeable || _armoryOrder.Count == 0;
		_purchaseButton.Disabled = !changeable;
		_unequipWeaponButton.Disabled = !changeable || _adventurer.EquippedWeapon == null;
		_unequipArmorButton.Disabled = !changeable || _adventurer.EquippedArmor == null;
		_unequipAccessory1Button.Disabled = !changeable || _adventurer.EquippedAccessory1 == null;
		_unequipAccessory2Button.Disabled = !changeable || _adventurer.EquippedAccessory2 == null;

		if (!changeable)
			_message = "[color=orange]出撃中は装備を変更できません。帰還を待つこと。[/color]";
	}

	/// <summary>保管庫の在庫を、その武具が入る枠へ装備する（→ EquipmentSystem.TryEquip）。</summary>
	private void OnEquipPressed()
	{
		var selected = _armoryList.GetSelectedItems();
		if (selected.Length == 0 || selected[0] >= _armoryOrder.Count)
		{
			_message = "[color=orange]装備する武具を保管庫の一覧から選んでください。[/color]";
			RefreshStatus();
			return;
		}

		var entry = _armoryOrder[selected[0]];
		var slot = entry.GetSlot();
		if (slot == null)
		{
			_message = $"[color=orange]「{ItemColorHelper.GetColoredBBCode(entry)}」はカタログ定義が見つからないため装備できません。[/color]";
			RefreshStatus();
			return;
		}

		var previous = _adventurer.GetEquipped(slot.Value);
		if (!_equipmentSystem.TryEquip(_state, _adventurer, slot.Value, entry))
		{
			_message = $"[color=orange]「{ItemColorHelper.GetColoredBBCode(entry)}」は装備できません" +
				$"（{AdventurerPanel.JobLabel(_adventurer.JobClass)}の職業制限、または出撃中）。[/color]";
			RefreshStatus();
			return;
		}

		_message = $"[color=lime]{SlotLabel(slot.Value)}に「{ItemColorHelper.GetColoredBBCode(entry)}」を装備した。[/color]" +
			(previous == null ? "" : $"[color=gray]（「{ItemColorHelper.GetColoredBBCode(previous)}」は保管庫へ戻した）[/color]");
		RefreshAll();
	}

	private void OnPurchasePressed()
	{
		var selected = _itemList.GetSelectedItems();
		if (selected.Length == 0 || selected[0] >= _catalogOrder.Count)
		{
			_message = "[color=orange]購入・装備するアイテムを選択してください。[/color]";
			RefreshStatus();
			return;
		}

		var item = _catalogOrder[selected[0]];
		if (!item.IsAllowedFor(_adventurer.JobClass))
		{
			_message = $"[color=orange]{AdventurerPanel.JobLabel(_adventurer.JobClass)}は「{item.Name}」を装備できません。[/color]";
			RefreshStatus();
			return;
		}
		if (_state.Gold < item.Price)
		{
			_message = $"[color=orange]資金が足りません（必要 {item.Price}G、所持 {_state.Gold}G）。[/color]";
			RefreshStatus();
			return;
		}

		var previous = _adventurer.GetEquipped(item.Slot);
		if (!_equipmentSystem.TryPurchaseAndEquip(_state, _adventurer, item.Id))
		{
			_message = $"[color=orange]「{item.Name}」を購入・装備できませんでした。[/color]";
			RefreshStatus();
			return;
		}

		_message = $"[color=lime]「{item.Name}」を{item.Price}Gで購入し、{SlotLabel(item.Slot)}に装備した。[/color]" +
			(previous == null ? "" : $"[color=gray]（「{ItemColorHelper.GetColoredBBCode(previous)}」は保管庫へ戻した）[/color]");
		RefreshAll();
	}

	/// <summary>装備を外してギルド保管庫へ戻す（→ EquipmentSystem.TryUnequip）。</summary>
	private void OnUnequipPressed(EquipmentSlot slot)
	{
		var current = _adventurer.GetEquipped(slot);
		if (!_equipmentSystem.TryUnequip(_state, _adventurer, slot))
		{
			_message = current == null
				? $"[color=gray]{SlotLabel(slot)}には何も装備していません。[/color]"
				: "[color=orange]出撃中は装備を外せません。[/color]";
			RefreshStatus();
			return;
		}

		_message = $"[color=cyan]{SlotLabel(slot)}の「{ItemColorHelper.GetColoredBBCode(current)}」を外し、保管庫へ戻した。[/color]";
		RefreshAll();
	}

	private static string EffectText(Item item) => item.DescribeEffects();

	/// <summary>装備中の全枠の能力値補正の合計（例：「STR+7・AGI+2」、無ければ「なし」）。§0.37で個人CPの表示を置き換え。</summary>
	private string EquippedStatText()
	{
		var parts = new[] { "STR", "VIT", "AGI", "DEX", "INT", "MND", "LDR" }
			.Select(stat => (stat, bonus: _adventurer.GetEquipmentStatBonus(stat)))
			.Where(p => p.bonus != 0)
			.Select(p => $"{p.stat}{(p.bonus > 0 ? "+" : "")}{p.bonus}");
		string text = string.Join("・", parts);
		return text.Length == 0 ? "なし" : text;
	}

	private static string SlotLabel(EquipmentSlot slot) => slot switch
	{
		EquipmentSlot.Weapon => "武器",
		EquipmentSlot.Armor => "防具",
		EquipmentSlot.Accessory1 => "装飾1",
		EquipmentSlot.Accessory2 => "装飾2",
		_ => slot.ToString()
	};
}
