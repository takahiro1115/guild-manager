#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 中央ペイン「倉庫・遺物（Warehouse）」タブ（→ 03 §4.7、未鑑定遺物と鑑定システム）。
///
/// ギルドが抱える3種類の在庫を1画面に集約する：
///  1. 📦 未鑑定遺物：探索（採取）任務の副産物・階層ボス撃破の確定ドロップ。左に一覧、右に詳細と
///     アルベールの鑑定コメント、鑑定実行ボタンを置く2ペイン構成。
///  2. 🌿 採取素材一覧：全5フィールドの素材の在庫数と用途（→ Balance.MaterialBalance・研究レシピ）。
///  3. ⚔️ ギルド保管庫：まだ誰にも装備させていない武具の現物（→ GameState.Armory）。
///
/// 鑑定は ResearchPanel と同じく「ボタンを押した瞬間に完結する」即時処理（週送りを介さない）。
/// 週報ログへの書き込み・画面全体の再描画は MainDashboard の責務のため、
/// LogRequested・StateChanged イベントで依頼する（DungeonPanel・ResearchPanel と同じ流儀）。
/// </summary>
public partial class InventoryPanel : VBoxContainer
{
	private TabContainer _tabs = null!;
	private RichTextLabel _relicSummaryLabel = null!;
	private ItemList _relicList = null!;
	private RichTextLabel _relicDetailLabel = null!;
	private RichTextLabel _albertCommentLabel = null!;
	private Button _appraiseButton = null!;
	private VBoxContainer _materialListBox = null!;
	private VBoxContainer _armoryListBox = null!;

	private GameState _state = null!;
	private AppraisalSystem _appraisalSystem = null!;
	private EconomySystem _economySystem = null!;

	/// <summary>現在リストに並んでいる未鑑定品（表示順。→ AppraisalSystem.SortForDisplay）。</summary>
	private List<UnidentifiedItem> _displayedRelics = new();

	/// <summary>選択中の未鑑定品のId。再描画をまたいで選択を維持するため、行番号ではなくIdで覚える。</summary>
	private string? _selectedRelicId;

	/// <summary>
	/// 直近の鑑定結果の演出テキスト（BBCode）。鑑定実行 → StateChanged → Refresh という流れで
	/// 画面が作り直されるため、結果表示が消えないようフィールドに保持して毎回描き直す。
	/// </summary>
	private string _lastAppraisalText = "";

	/// <summary>週報ログ（右ペイン）への追記を依頼する（BBCode文字列）。</summary>
	public event Action<string> LogRequested = delegate { };

	/// <summary>鑑定でゲーム状態が変わったことを通知する（MainDashboardが全体を再描画する）。</summary>
	public event Action StateChanged = delegate { };

	public override void _Ready()
	{
		_tabs = GetNode<TabContainer>("%Tabs");
		_relicSummaryLabel = GetNode<RichTextLabel>("%RelicSummaryLabel");
		_relicList = GetNode<ItemList>("%RelicList");
		_relicDetailLabel = GetNode<RichTextLabel>("%RelicDetailLabel");
		_albertCommentLabel = GetNode<RichTextLabel>("%AlbertCommentLabel");
		_appraiseButton = GetNode<Button>("%AppraiseButton");
		_materialListBox = GetNode<VBoxContainer>("%MaterialListBox");
		_armoryListBox = GetNode<VBoxContainer>("%ArmoryListBox");

		// タブ見出しは絵文字入りのため、ノード名（Godotの命名規則の制約を受ける）ではなく
		// コード側から設定する。
		_tabs.SetTabTitle(0, "📦 未鑑定遺物");
		_tabs.SetTabTitle(1, "🌿 採取素材一覧");
		_tabs.SetTabTitle(2, "⚔️ ギルド保管庫（装備）");

		_relicList.ItemSelected += OnRelicSelected;
		_appraiseButton.Pressed += OnAppraiseButtonPressed;
	}

	/// <summary>
	/// MainDashboardから鑑定エンジンと資金処理を注入する（→ FacilityPanel.Initializeと同じ流儀）。
	/// EconomySystemは素材の換金（→ 03 §4.8）に使う。
	/// </summary>
	public void Initialize(AppraisalSystem appraisalSystem, EconomySystem economySystem)
	{
		_appraisalSystem = appraisalSystem;
		_economySystem = economySystem;
	}

	/// <summary>最新のゲーム状態でタブ全体を再描画する（MainDashboard.RefreshAllから毎回呼ぶ）。</summary>
	public void Refresh(GameState state)
	{
		_state = state;
		RefreshRelicTab();
		RefreshMaterialTab();
		RefreshArmoryTab();
	}

	// ==================== 📦 未鑑定遺物タブ ====================

	private void RefreshRelicTab()
	{
		_displayedRelics = AppraisalSystem.SortForDisplay(_state).ToList();

		// 選択中の遺物が鑑定済み・消滅していたら選択を解除する。
		if (_selectedRelicId != null && _displayedRelics.All(r => r.Id != _selectedRelicId))
			_selectedRelicId = null;

		RefreshRelicSummary();
		RefreshRelicList();
		RefreshRelicDetail();
		RefreshAlbertComment();
	}

	private void RefreshRelicSummary()
	{
		_relicSummaryLabel.Clear();
		_relicSummaryLabel.AppendText($"[b]所持金[/b]：{_state.Gold} G　／　[b]未鑑定の遺物[/b]：{_displayedRelics.Count} 個\n");

		if (_displayedRelics.Count == 0)
			return;

		// 希少度ごとの内訳（銅3 / 銀1 …）。何をどれだけ抱えているかを一目で掴めるようにする。
		var parts = RelicBalance.GetAll()
			.Select(p => (Profile: p, Count: _displayedRelics.Count(r => r.Rarity == p.Rarity)))
			.Where(x => x.Count > 0)
			.Select(x => $"[color={x.Profile.ColorName}]{x.Profile.Label}{x.Count}[/color]");

		_relicSummaryLabel.AppendText($"[color=gray]内訳：{string.Join(" / ", parts)}[/color]");
	}

	private void RefreshRelicList()
	{
		_relicList.Clear();

		for (int i = 0; i < _displayedRelics.Count; i++)
		{
			var relic = _displayedRelics[i];
			var profile = RelicBalance.Get(relic.Rarity);
			bool affordable = _state.Gold >= relic.AppraisalCost;

			_relicList.AddItem($"[{profile.Label}] {relic.Name}　― {FieldName(relic.OriginFieldId)} 第{relic.OriginFloor}層" +
				$"　／ 鑑定費用 {relic.AppraisalCost}G{(affordable ? "" : "（資金不足）")}");
			_relicList.SetItemCustomFgColor(i, RarityColor(relic.Rarity));

			if (relic.Id == _selectedRelicId)
				_relicList.Select(i);
		}
	}

	private void RefreshRelicDetail()
	{
		_relicDetailLabel.Clear();

		var relic = SelectedRelic();
		if (relic == null)
		{
			_relicDetailLabel.AppendText(_displayedRelics.Count == 0
				? "[color=gray]未鑑定の遺物はまだ1つも無い。\n大迷宮の探索（採取）任務の副産物か、階層ボスの撃破で手に入る（→ 大迷宮タブ）。[/color]"
				: "[color=gray]左の一覧から鑑定したい遺物を選ぶこと。[/color]");
			_appraiseButton.Disabled = true;
			_appraiseButton.Text = "🔬 アルベールに鑑定させる";
			return;
		}

		var profile = RelicBalance.Get(relic.Rarity);
		_relicDetailLabel.AppendText($"[font_size=18][b][color={profile.ColorName}]{relic.Name}[/color][/b][/font_size]\n");
		_relicDetailLabel.AppendText($"希少度：[color={profile.ColorName}]{profile.Label}（{relic.Rarity}）[/color]\n");
		_relicDetailLabel.AppendText($"出土地：{FieldName(relic.OriginFieldId)} 第{relic.OriginFloor}層\n");
		_relicDetailLabel.AppendText($"鑑定費用：{relic.AppraisalCost} G（所持金 {_state.Gold} G）\n\n");
		_relicDetailLabel.AppendText($"[color=gray]中身の見込み：武具 {profile.EquipmentRate}% ／ 素材 {profile.MaterialRate}% ／ 換金 {profile.GoldRate}%[/color]\n");
		_relicDetailLabel.AppendText($"[color=gray]素材だった場合は {FieldName(relic.OriginFieldId)} 第{relic.OriginFloor}層で採れる物が " +
			$"{profile.MaterialCountMin}〜{profile.MaterialCountMax} 個、換金だった場合は {profile.GoldRewardMin}〜{profile.GoldRewardMax} G。[/color]");

		bool affordable = _state.Gold >= relic.AppraisalCost;
		_appraiseButton.Disabled = !affordable;
		_appraiseButton.Text = affordable
			? $"🔬 アルベールに鑑定させる（{relic.AppraisalCost} G）"
			: $"🔬 鑑定費用が足りない（{relic.AppraisalCost} G 必要）";
	}

	private void RefreshAlbertComment()
	{
		_albertCommentLabel.Clear();
		_albertCommentLabel.AppendText(string.IsNullOrEmpty(_lastAppraisalText)
			? "[color=gray]『……ふむ、持ってきたか。金さえ積んでくれれば、中身は私が見てやろう。』[/color]"
			: _lastAppraisalText);
	}

	private void OnRelicSelected(long index)
	{
		if (index < 0 || index >= _displayedRelics.Count)
			return;

		_selectedRelicId = _displayedRelics[(int)index].Id;
		RefreshRelicDetail();
	}

	private void OnAppraiseButtonPressed()
	{
		var relic = SelectedRelic();
		if (relic == null)
			return;

		// 鑑定前に控えておく（Appraiseが在庫から取り除くため、後から参照できなくなる）。
		string relicName = relic.Name;
		string origin = $"{FieldName(relic.OriginFieldId)} 第{relic.OriginFloor}層";
		var profile = RelicBalance.Get(relic.Rarity);

		var result = _appraisalSystem.Appraise(_state, relic.Id);
		if (result == null)
			return;

		_selectedRelicId = null;
		_lastAppraisalText = BuildAppraisalText(result, relicName, profile);

		LogRequested.Invoke(
			$"[color=gold][b]🔬 鑑定：{relicName}（{origin}、{profile.Label}）[/b][/color]\n" +
			$"[color={profile.ColorName}]→ {AppraisalSystem.BuildResultSummary(result)}[/color]" +
			$"　[color=gray]（鑑定費用 -{result.AppraisalCost}G）[/color]\n" +
			$"[color=cyan]アルベール『{result.FlavorText}』[/color]");

		// 所持金・素材・保管庫が動くため、画面全体（ヘッダーの素材サマリー等）を作り直す。
		StateChanged.Invoke();
	}

	/// <summary>鑑定結果の演出テキスト（アルベールのコメント欄に出すBBCode）。</summary>
	private static string BuildAppraisalText(AppraisalResult result, string relicName, RelicBalance.RarityProfile profile)
	{
		string gain = result.Type switch
		{
			AppraisalResultType.Equipment => $"⚔️ {result.ItemName} を獲得！ ギルド保管庫へ納めた。",
			AppraisalResultType.Material => $"🌿 {result.ItemName} ×{result.ResultMaterialCount} を獲得！ 研究室へ回した。",
			_ => $"💰 {result.ItemName} を換金し、{result.ResultGold} G を得た。",
		};

		return $"[font_size=17][b]{relicName} の封を解いた……[/b][/font_size]\n" +
			$"[color={profile.ColorName}][font_size=18][b]{gain}[/b][/font_size][/color]\n" +
			$"[color=gray]（鑑定費用 -{result.AppraisalCost} G）[/color]\n\n" +
			$"[color=cyan]アルベール『{result.FlavorText}』[/color]";
	}

	private UnidentifiedItem? SelectedRelic() =>
		_selectedRelicId == null ? null : _displayedRelics.FirstOrDefault(r => r.Id == _selectedRelicId);

	// ==================== 🌿 採取素材一覧タブ ====================

	/// <summary>
	/// 全5フィールドの素材を、フィールドごとにまとめて在庫数・採取可能階層・用途とともに並べる
	/// （→ Balance.MaterialBalance・ResearchBalance）。未開放フィールドの素材も「まだ見ぬ素材」として
	/// 一覧には出す（どこへ潜れば何が採れるかの見通しを与えるため）。
	/// </summary>
	private void RefreshMaterialTab()
	{
		ClearChildren(_materialListBox);

		var byField = MaterialBalance.GetAll().GroupBy(m => m.FieldId);
		foreach (var group in byField)
		{
			var header = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			bool unlocked = _state.DungeonFields.Any(f => f.Id == group.Key && f.IsUnlocked);
			header.AppendText($"[font_size=16][b]{FieldName(group.Key)}[/b][/font_size]" +
				(unlocked ? "" : "　[color=gray]（未開放）[/color]"));
			_materialListBox.AddChild(header);

			foreach (var material in group)
				_materialListBox.AddChild(BuildMaterialRow(material));

			_materialListBox.AddChild(new HSeparator());
		}
	}

	/// <summary>
	/// 素材1種の行：在庫・採取可能階層・説明・用途・売却単価の表示と、売却操作部
	/// （1個／全数／数量指定）を横並びにする（→ 03 §4.8）。在庫0の素材は操作部を無効化する。
	/// </summary>
	private Control BuildMaterialRow(MaterialDefinition material)
	{
		int stock = _state.Materials.TryGetValue(material.Id, out int count) ? count : 0;

		var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };

		var info = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		string stockText = stock > 0 ? $"[color=lime]×{stock}[/color]" : "[color=gray]×0[/color]";
		info.AppendText($"　{material.Name}　{stockText}　[color=yellow]{material.SellPrice}G/個[/color]" +
			$"　[color=gray]第{material.MinFloor}層〜／{material.Description}[/color]\n" +
			$"　　[color=gray]用途：{MaterialUsage(material.Id)}[/color]");
		row.AddChild(info);

		row.AddChild(BuildSellControls(
			stock,
			unitPrice: material.SellPrice,
			sell: n => SellMaterial(material, n)));

		return row;
	}

	/// <summary>
	/// 売却操作部（1個／全数／数量指定）。在庫0なら全ボタンをDisabledにする。
	/// 素材・汎用武具のどちらからも使う共通部品（→ 03 §4.8）。
	/// </summary>
	/// <param name="sell">実際の売却処理。引数は売却する個数。</param>
	private static Control BuildSellControls(int stock, int unitPrice, Action<int> sell)
	{
		var box = new HBoxContainer();
		box.AddThemeConstantOverride("separation", 4);

		bool sellable = stock > 0;

		var oneButton = new Button { Text = "💰 1個売却", Disabled = !sellable, TooltipText = $"{unitPrice}G" };
		if (sellable) oneButton.Pressed += () => sell(1);
		box.AddChild(oneButton);

		var allButton = new Button
		{
			Text = $"💰 全売却（{stock}個）",
			Disabled = !sellable,
			TooltipText = $"{unitPrice * stock}G",
		};
		if (sellable) allButton.Pressed += () => sell(stock);
		box.AddChild(allButton);

		// 数量指定：初期値は Math.Min(10, 在庫数)、範囲は 1〜在庫数（在庫0なら操作不可）。
		var spin = new SpinBox
		{
			MinValue = 1,
			MaxValue = Math.Max(1, stock),
			Value = Math.Min(10, Math.Max(1, stock)),
			Step = 1,
			Editable = sellable,
			CustomMinimumSize = new Vector2(80, 0),
		};
		box.AddChild(spin);

		var countButton = new Button { Text = "💰 指定数売却", Disabled = !sellable };
		if (sellable) countButton.Pressed += () => sell((int)spin.Value);
		box.AddChild(countButton);

		return box;
	}

	/// <summary>素材の売却実行（→ EconomySystem.TrySellMaterial）。</summary>
	private void SellMaterial(MaterialDefinition material, int count)
	{
		if (!_economySystem.TrySellMaterial(_state, material.Id, count, out int gold))
			return;

		LogRequested.Invoke($"[color=yellow]💰 {material.Name} ×{count} を売却し、{gold} G を得た。[/color]");
		StateChanged.Invoke();
	}

	/// <summary>この素材を必要とする研究の一覧（→ ResearchBalance）。使い道が無ければその旨を返す。</summary>
	private string MaterialUsage(string materialId)
	{
		var users = ResearchBalance.GetAll()
			.Where(r => r.RequiredMaterials.ContainsKey(materialId))
			.Select(r => $"{r.Name}×{r.RequiredMaterials[materialId]}" + (_state.IsResearchCompleted(r.Id) ? "（研究済）" : ""))
			.ToList();

		return users.Count > 0 ? string.Join("、", users) : "現時点で研究レシピに登場しない（鑑定・将来の研究向けの備蓄）";
	}

	// ==================== ⚔️ ギルド保管庫タブ ====================

	/// <summary>
	/// ギルドが所持している装備品の一覧（→ GameState.Armory）。売却（→ 03 §4.8）の事故を防ぐため、
	/// 2つのカテゴリに分けて表示する：
	///  - **A. 汎用武具**：カタログ品（無銘）と銅・銀の鑑定品。同じカタログId・同じ希少度ごとに
	///    まとめ、1個／全数／数量指定のまとめ売りができる。
	///  - **B. 希少武具**：金・虹の鑑定品。誤って一括で売り飛ばさないようグルーピングせず
	///    1点ずつ独立表示し、1個売却ボタンだけを置く。
	/// </summary>
	private void RefreshArmoryTab()
	{
		ClearChildren(_armoryListBox);

		var summary = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		summary.AppendText($"[b]ギルド保管庫[/b]：{_state.Armory.Count} 点　／　[b]所持金[/b]：{_state.Gold} G\n" +
			"[color=gray]鑑定で掘り当てた武具や、冒険者から外した武具の在庫（＝誰も装備していない現物）。" +
			"装備させるには「冒険者人事」タブで対象を選び、[装備変更]から換装すること（→ 03 §4.2.2）。" +
			"余剰分はここで換金できる（→ 03 §4.8）。[/color]");
		_armoryListBox.AddChild(summary);
		_armoryListBox.AddChild(new HSeparator());

		if (_state.Armory.Count == 0)
		{
			var empty = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			empty.AppendText("[color=gray]保管庫は空だ。遺物を鑑定すれば武具が出ることがある" +
				"（冒険者から外した武具もここへ戻る）。[/color]");
			_armoryListBox.AddChild(empty);
			return;
		}

		var groups = EquipmentSystem.GroupArmoryForSale(_state);
		var common = groups.Where(g => !IsPrecious(g.Key.Rarity)).ToList();
		var precious = groups.Where(g => IsPrecious(g.Key.Rarity)).SelectMany(g => g).ToList();

		if (common.Count > 0)
		{
			_armoryListBox.AddChild(SectionHeader("🔧 汎用武具（まとめ売り可）"));
			foreach (var group in common)
				_armoryListBox.AddChild(BuildBulkArmoryRow(group.ToList()));
		}

		if (precious.Count > 0)
		{
			_armoryListBox.AddChild(new HSeparator());
			_armoryListBox.AddChild(SectionHeader("💎 希少武具（1点ずつ。誤売却防止のためまとめ売り不可）"));
			foreach (var item in precious.OrderByDescending(e => e.Rarity).ThenBy(e => e.Name, StringComparer.Ordinal))
				_armoryListBox.AddChild(BuildPreciousArmoryRow(item));
		}
	}

	/// <summary>まとめ売りの対象外にする希少度（金・虹）。null＝カタログ品は常に汎用扱い。</summary>
	private static bool IsPrecious(ItemRarity? rarity) =>
		rarity is ItemRarity.Epic or ItemRarity.Legendary;

	private static Control SectionHeader(string text)
	{
		var label = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		label.AppendText($"[font_size=16][b]{text}[/b][/font_size]");
		return label;
	}

	/// <summary>汎用武具1群（同じカタログId・同じ希少度）の行。まとめ売りの操作部つき。</summary>
	private Control BuildBulkArmoryRow(List<EquipmentItem> stock)
	{
		var sample = stock[0];
		int unitPrice = EquipmentSystem.GetSellPrice(sample);

		var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		row.AddChild(BuildArmoryInfoLabel(sample, stock.Count, unitPrice));
		row.AddChild(BuildSellControls(
			stock.Count,
			unitPrice,
			sell: n => SellEquipments(stock.Take(n).ToList())));

		return row;
	}

	/// <summary>希少武具1点の行。誤売却を防ぐため、1個売却ボタンのみを置く。</summary>
	private Control BuildPreciousArmoryRow(EquipmentItem item)
	{
		int price = EquipmentSystem.GetSellPrice(item);

		var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		row.AddChild(BuildArmoryInfoLabel(item, count: 1, unitPrice: price));

		var button = new Button { Text = $"💰 売却（{price}G）" };
		button.Pressed += () => SellEquipments(new List<EquipmentItem> { item });
		row.AddChild(button);

		return row;
	}

	/// <summary>保管庫の1行分の説明（名称・希少度・性能・入手履歴・売却単価）。</summary>
	private RichTextLabel BuildArmoryInfoLabel(EquipmentItem sample, int count, int unitPrice)
	{
		var definition = sample.GetDefinition();
		var label = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };

		string effect = definition == null
			? "[color=gray]（カタログ定義が見つからない旧データ）[/color]"
			: $"{SlotLabel(definition.Slot)}／{EffectLabel(definition.EffectType)} +{definition.EffectValue}" +
			  $"／{(definition.AllowedJobs.Count == 0 ? "全職業" : string.Join("・", definition.AllowedJobs.Select(JobLabel)))}";

		string rarityTag = sample.Rarity.HasValue
			? $"　[color={RelicBalance.GetRarityColorName(sample.Rarity.Value)}]" +
			  $"［{RelicBalance.GetRarityLabel(sample.Rarity.Value)}］[/color]"
			: "　[color=gray]［無銘］[/color]";

		var history = _state.Armory
			.Where(e => e.ItemId == sample.ItemId && e.Rarity == sample.Rarity)
			.Select(e => $"第{e.AcquiredAtWeek}週 {e.AcquiredFrom}")
			.Take(3)
			.ToList();

		label.AppendText($"[font_size=16][b]{sample.Name}[/b][/font_size]{rarityTag} ×{count}" +
			$"　[color=yellow]{unitPrice}G/点[/color]\n" +
			$"　[color=gray]{effect}[/color]\n" +
			$"　[color=gray]入手：{string.Join("、", history)}{(count > history.Count ? " ほか" : "")}[/color]");

		return label;
	}

	/// <summary>武具の売却実行（→ EquipmentSystem.TrySellEquipments）。</summary>
	private void SellEquipments(List<EquipmentItem> items)
	{
		if (items.Count == 0)
			return;

		string name = items[0].Name;
		if (!EquipmentSystem.TrySellEquipments(_state, items.Select(e => e.Id.ToString()), out int gold))
			return;

		LogRequested.Invoke($"[color=yellow]💰 {name} ×{items.Count} を売却し、{gold} G を得た。[/color]");
		StateChanged.Invoke();
	}

	// ==================== 表示用ヘルパー ====================

	private static void ClearChildren(Node parent)
	{
		foreach (var child in parent.GetChildren())
		{
			parent.RemoveChild(child);
			child.QueueFree();
		}
	}

	/// <summary>フィールドIdを表示名へ（GameStateに無いIdはそのまま出す＝防御的フォールバック）。</summary>
	private string FieldName(string fieldId) =>
		_state.DungeonFields.FirstOrDefault(f => f.Id == fieldId)?.Name ?? fieldId;

	/// <summary>希少度ごとのItemList行の色（BBCodeのColorNameと同じ色味をColorで持つ）。</summary>
	private static Color RarityColor(ItemRarity rarity) => rarity switch
	{
		ItemRarity.Legendary => new Color(1.00f, 0.45f, 1.00f),
		ItemRarity.Epic => new Color(1.00f, 0.84f, 0.20f),
		ItemRarity.Rare => new Color(0.80f, 0.85f, 0.92f),
		_ => new Color(0.80f, 0.62f, 0.45f),
	};

	private static string SlotLabel(EquipmentSlot slot) => slot switch
	{
		EquipmentSlot.Weapon => "武器",
		EquipmentSlot.Armor => "防具",
		EquipmentSlot.Accessory1 => "装飾1",
		EquipmentSlot.Accessory2 => "装飾2",
		_ => slot.ToString(),
	};

	private static string EffectLabel(EquipmentEffectType effectType) => effectType switch
	{
		EquipmentEffectType.PersonalCpBonus => "個人CP",
		EquipmentEffectType.MaxHpBonus => "最大HP",
		_ => effectType.ToString(),
	};

	private static string JobLabel(JobClass job) => job switch
	{
		JobClass.Warrior => "重戦士",
		JobClass.Knight => "騎士",
		JobClass.Cleric => "神官",
		JobClass.Mage => "魔導士",
		JobClass.Scholar => "学者",
		JobClass.Thief => "盗賊",
		JobClass.Ranger => "斥候",
		_ => job.ToString(),
	};
}
