#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 「お抱え商」の画面（2026年10月・§0.67）。武器・防具・装飾品を、冒険者を選んで買う。
///
/// 左ペイン＝商品一覧（武器／防具／装飾品の切り替え）、右上ペイン＝選んだ冒険者の能力値と今の装備、右下ペイン＝冒険者の一覧（選ぶ）。
/// 買うとその冒険者がその場で装備する（同じ枠に今の装備があれば保管庫へ戻る、→ EquipmentSystem.TryPurchaseAndEquip）。
/// 装飾品は1と2を区別せず、空いている枠に着ける。出撃中の冒険者は装備を変えられない。
/// 式や在庫の判定はすべて Core（EquipmentSystem・ItemCatalog）から取る。
/// </summary>
public partial class ShopPanel : HBoxContainer
{
	private enum Category { Weapon, Armor, Accessory }

	private static readonly (Category Category, string Label)[] Categories =
	{
		(Category.Weapon, "⚔ 武器"), (Category.Armor, "🛡 防具"), (Category.Accessory, "💍 装飾品"),
	};

	private static readonly string[] StatNames = { "STR", "VIT", "AGI", "DEX", "INT", "MND", "LDR" };

	private GameState _state = null!;
	private EquipmentSystem _equipmentSystem = null!;

	private Category _category = Category.Weapon;
	private Guid? _selectedId;

	private VBoxContainer _productList = null!;
	private RichTextLabel _infoLabel = null!;
	private ItemList _adventurerList = null!;
	private List<Adventurer> _adventurerOrder = new();
	private bool _refreshingList;

	/// <summary>週報ログへの追記を依頼する（BBCode文字列）。</summary>
	public event Action<string> LogRequested = delegate { };

	/// <summary>購入でゲーム状態が変わったことを通知する（MainDashboard が全体を再描画する）。</summary>
	public event Action StateChanged = delegate { };

	public ShopPanel()
	{
		Name = "ShopTab";
		SizeFlagsHorizontal = SizeFlags.ExpandFill;
		SizeFlagsVertical = SizeFlags.ExpandFill;
		AddThemeConstantOverride("separation", 8);
		BuildLayout();
	}

	public void Initialize(EquipmentSystem equipmentSystem) => _equipmentSystem = equipmentSystem;

	// ==================== 画面の組み立て ====================

	private void BuildLayout()
	{
		// 左：商品一覧
		var left = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 3f };
		var leftBox = new VBoxContainer();
		leftBox.AddThemeConstantOverride("separation", 8);
		left.AddChild(leftBox);

		leftBox.AddChild(new Label { Text = "🛒 お抱え商：武具を買う（買うと、右で選んだ冒険者がその場で装備する）" });
		var tabs = new HBoxContainer();
		tabs.AddThemeConstantOverride("separation", 6);
		var group = new ButtonGroup();
		foreach (var (category, label) in Categories)
		{
			var button = new Button { Text = label, ToggleMode = true, ButtonGroup = group, CustomMinimumSize = new Vector2(120, 32), ButtonPressed = category == _category };
			UiStyles.ApplySelectedToggleStyle(button);
			var captured = category;
			button.Pressed += () => { _category = captured; RebuildProducts(); };
			tabs.AddChild(button);
		}
		leftBox.AddChild(tabs);

		var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		_productList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_productList.AddThemeConstantOverride("separation", 6);
		scroll.AddChild(_productList);
		leftBox.AddChild(scroll);
		AddChild(left);

		// 右：上＝能力値と装備、下＝冒険者の一覧
		var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 2f };
		right.AddThemeConstantOverride("separation", 8);

		var infoPanel = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1f };
		var infoScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		_infoLabel = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		infoScroll.AddChild(_infoLabel);
		infoPanel.AddChild(infoScroll);
		right.AddChild(infoPanel);

		var listPanel = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1f };
		var listBox = new VBoxContainer();
		listBox.AddChild(new Label { Text = "冒険者（クリックで選ぶ）" });
		_adventurerList = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill };
		_adventurerList.ItemSelected += OnAdventurerSelected;
		listBox.AddChild(_adventurerList);
		listPanel.AddChild(listBox);
		right.AddChild(listPanel);
		AddChild(right);

		UiStyles.ApplyPanelArt(left, innerMargin: 0);
		UiStyles.ApplyPanelArt(infoPanel, innerMargin: 0);
		UiStyles.ApplyPanelArt(listPanel, innerMargin: 0);
	}

	// ==================== 再描画 ====================

	public void Refresh(GameState state)
	{
		_state = state;

		// 選択中の冒険者が離れた（引退など）なら、先頭の人に替える
		if (!_state.Adventurers.Any(a => a.Id == _selectedId))
			_selectedId = _state.Adventurers.FirstOrDefault()?.Id;

		RebuildAdventurerList();
		RebuildInfo();
		RebuildProducts();
	}

	private Adventurer? Selected() => _selectedId == null ? null : _state.Adventurers.FirstOrDefault(a => a.Id == _selectedId);

	private void RebuildAdventurerList()
	{
		_refreshingList = true;
		_adventurerList.Clear();
		_adventurerOrder = _state.Adventurers.ToList();
		int selectedIndex = -1;
		for (int i = 0; i < _adventurerOrder.Count; i++)
		{
			var a = _adventurerOrder[i];
			string status = a.IsDispatched ? "　【出撃中】" : a.Injury != InjurySeverity.None ? "　【負傷】" : "";
			_adventurerList.AddItem($"{a.Name}（{AdventurerPanel.JobLabel(a.JobClass)}・{a.Age}歳）　HP {a.CurrentHP}/{a.MaxHP}{status}");
			if (a.Id == _selectedId) selectedIndex = i;
		}
		if (selectedIndex >= 0)
			_adventurerList.Select(selectedIndex);
		_refreshingList = false;
	}

	private void OnAdventurerSelected(long index)
	{
		if (_refreshingList || index < 0 || index >= _adventurerOrder.Count) return;
		_selectedId = _adventurerOrder[(int)index].Id;
		RebuildInfo();
		RebuildProducts();
	}

	private static int RawStat(Adventurer a, string stat) => stat switch
	{
		"STR" => a.STR, "VIT" => a.VIT, "AGI" => a.AGI, "DEX" => a.DEX, "INT" => a.INT, "MND" => a.MND, _ => a.LDR,
	};

	/// <summary>右上：選んだ冒険者の能力値（装備込みの実効値と、装備の補正）と、今の装備。</summary>
	private void RebuildInfo()
	{
		_infoLabel.Clear();
		var a = Selected();
		if (a == null)
		{
			_infoLabel.AppendText("[color=gray]冒険者がいない。[/color]");
			return;
		}

		int hpEquipment = a.GetEquipmentHpBonus();
		_infoLabel.AppendText(
			$"[font_size=18][b]{a.Name}[/b][/font_size]　[color=#fbbf24]{AdventurerPanel.JobLabel(a.JobClass)}[/color] {a.Age}歳\n" +
			$"HP {a.CurrentHP} / {a.MaxHP}" + (hpEquipment != 0 ? $"　[color=#38bdf8]（装備 {hpEquipment:+0;-0}）[/color]" : "") +
			(a.IsDispatched ? "　[color=orange]出撃中は装備を変えられない[/color]" : "") + "\n\n");

		_infoLabel.AppendText("[b]能力値[/b]（実効値／素の値　装備の補正）\n");
		foreach (var stat in StatNames)
		{
			int effective = (int)Math.Round(a.GetEffectiveStat(stat));
			int equip = a.GetEquipmentStatBonus(stat);
			_infoLabel.AppendText($"　{stat}　[b]{effective}[/b] / 素 {RawStat(a, stat)}" + (equip != 0 ? $"　[color=#38bdf8]装備 {equip:+0;-0}[/color]" : "") + "\n");
		}

		_infoLabel.AppendText("\n[b]今の装備[/b]\n");
		string Line(string label, EquipmentItem? item) =>
			$"　{label}：{(item == null ? "[color=gray]なし[/color]" : $"{ItemColorHelper.GetColoredBBCode(item)}　[color=gray]{item.DescribeEffects()}[/color]")}\n";
		_infoLabel.AppendText(Line("武器", a.EquippedWeapon));
		_infoLabel.AppendText(Line("防具", a.EquippedArmor));
		_infoLabel.AppendText(Line("装飾品", a.EquippedAccessory1));
		_infoLabel.AppendText(Line("装飾品", a.EquippedAccessory2));
	}

	/// <summary>左：選んだカテゴリの商品。冒険者を選んでいれば、その人の今の装備と、買えない理由を添える。</summary>
	private void RebuildProducts()
	{
		foreach (var child in _productList.GetChildren())
		{
			_productList.RemoveChild(child);
			child.QueueFree();
		}

		var buyer = Selected();
		var items = _category switch
		{
			Category.Weapon => ItemCatalog.GetBySlot(EquipmentSlot.Weapon),
			Category.Armor => ItemCatalog.GetBySlot(EquipmentSlot.Armor),
			_ => ItemCatalog.GetBySlot(EquipmentSlot.Accessory1).Concat(ItemCatalog.GetBySlot(EquipmentSlot.Accessory2)),
		};

		foreach (var item in items.OrderBy(i => i.ShopTier).ThenBy(i => i.Price))
			_productList.AddChild(BuildProductRow(item, buyer));
	}

	private Control BuildProductRow(Item item, Adventurer? buyer)
	{
		bool inShop = EquipmentSystem.IsInShop(_state, item);
		string? blocked = null;
		if (!inShop)
			blocked = $"未入荷：倒した階層ボスが{ProgressionBalance.ShopTierUnlockBosses[item.ShopTier - 1]}体になると並ぶ（現在 {EquipmentSystem.CountDefeatedBosses(_state)}体）";
		else if (buyer == null)
			blocked = "冒険者を右の一覧から選ぶ";
		else if (buyer.IsDispatched)
			blocked = $"{buyer.Name}は出撃中で装備を変えられない";
		else if (!item.IsAllowedFor(buyer.JobClass))
			blocked = $"{AdventurerPanel.JobLabel(buyer.JobClass)}は装備できない";
		else if (_state.Gold < item.Price)
			blocked = $"資金不足（必要 {item.Price}G、所持 {_state.Gold}G）";

		var panel = new PanelContainer();
		var style = new StyleBoxFlat { BgColor = new Color(1, 1, 1, inShop ? 0.05f : 0.02f) };
		style.SetCornerRadiusAll(3);
		style.ContentMarginLeft = 10;
		style.ContentMarginRight = 10;
		style.ContentMarginTop = 6;
		style.ContentMarginBottom = 6;
		panel.AddThemeStyleboxOverride("panel", style);

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);
		panel.AddChild(row);

		var text = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
		string nameColor = inShop ? "#e2e8f0" : "#6b7280";
		// 今の装備は右上のペインに出ているので、商品の行には出さない（未入荷のときだけ、並ぶ条件を添える）
		text.AppendText($"[b][color={nameColor}]{item.Name}[/color][/b]　[color=gray]{item.DescribeEffects()}[/color]" +
			(inShop ? "" : $"\n[color=gray]{blocked}[/color]"));
		row.AddChild(text);

		var button = new Button { Text = $"購入 {item.Price}G", CustomMinimumSize = new Vector2(120, 34), SizeFlagsVertical = SizeFlags.ShrinkCenter, Disabled = blocked != null };
		if (blocked != null) button.TooltipText = blocked;
		button.Pressed += () => OnPurchase(item);
		row.AddChild(button);
		return panel;
	}

	private void OnPurchase(Item item)
	{
		var buyer = Selected();
		if (buyer == null) return;

		// 装飾枠が2つとも埋まっているときは、どちらと入れ替えるかを選ばせる
		if (EquipmentSystem.NeedsAccessoryChoice(buyer, item.Slot))
		{
			AccessoryChoice.Ask(this, buyer, item.Name, chosen => Purchase(buyer, item, chosen));
			return;
		}
		Purchase(buyer, item, null);
	}

	private void Purchase(Adventurer buyer, Item item, EquipmentSlot? accessorySlot)
	{
		var slot = accessorySlot ?? EquipmentSystem.ResolveSlot(buyer, item.Slot);
		var previous = buyer.GetEquipped(slot);
		if (!_equipmentSystem.TryPurchaseAndEquip(_state, buyer, item.Id, accessorySlot))
			return;

		LogRequested.Invoke($"[color=gold]🛒 お抱え商：{buyer.Name}が「{item.Name}」を買って装備した（-{item.Price}G）。[/color]" +
			(previous == null ? "" : $"[color=gray]（「{previous.DisplayName}」は保管庫へ戻した）[/color]"));
		StateChanged.Invoke();
	}
}
