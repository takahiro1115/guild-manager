#nullable enable
using Godot;
using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 中央ペイン「研究室（Lab）」タブ（→ アルベールの研究室、素材投資システム）。
///
/// 探索（採取）任務（→ DungeonPanel）で集めた素材とゴールドを投じて、恒久的な
/// インフラバフ（→ ResearchEffectType）を獲得する。ResearchSystemは乱数・週次決算を
/// 持たない純粋な判定・状態変更のため、DungeonPanelと違って「出撃→週送りで結果判明」
/// ではなく、ボタンを押した瞬間に即座に完了する。
///
/// 週報ログへの書き込み・画面全体の再描画は MainDashboard の責務のため、
/// LogRequested・StateChanged イベントで依頼する（DungeonPanel等と同じ流儀）。
/// </summary>
public partial class ResearchPanel : ScrollContainer
{
	private RichTextLabel _resourcesLabel = null!;
	private VBoxContainer _researchCards = null!;

	private GameState _state = null!;

	// ---- 培養槽（魂魄融和の秘薬、→ 03 §5.4・§0.58） ----
	private SoulFusionSystem? _soulFusionSystem;
	private int _selectedPairIndex;
	private int _selectedJobIndex;
	private string? _selectedCatalystId;

	/// <summary>週報ログ（右ペイン）への追記を依頼する（BBCode文字列）。</summary>
	public event Action<string> LogRequested = delegate { };

	/// <summary>研究完了でゲーム状態が変わったことを通知する（MainDashboardが全体を再描画する）。</summary>
	public event Action StateChanged = delegate { };

	public override void _Ready()
	{
		_resourcesLabel = GetNode<RichTextLabel>("%ResourcesLabel");
		_researchCards = GetNode<VBoxContainer>("%ResearchCards");
		BuildSplitLayout();
		}

		/// <summary>右半分に培養槽の欄（誕生した娘の履歴つき）。左は研究と霊薬。画面が横に広すぎて間延びするため、左右に分ける。</summary>
		private VBoxContainer _cultureColumn = null!;

		private void BuildSplitLayout()
		{
			var left = GetNode<VBoxContainer>("VBox");
			RemoveChild(left);

			var split = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			split.AddThemeConstantOverride("separation", 16);
			left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			left.SizeFlagsStretchRatio = 1f;
			split.AddChild(left);

			_cultureColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkBegin, SizeFlagsStretchRatio = 1f };
			_cultureColumn.AddThemeConstantOverride("separation", 8);
			split.AddChild(_cultureColumn);
			AddChild(split);
	}

	/// <summary>秘薬の処方に使う System を受け取る（MainDashboard._Ready から1回呼ぶ）。</summary>
	public void Initialize(SoulFusionSystem soulFusionSystem)
	{
		_soulFusionSystem = soulFusionSystem;
	}

	/// <summary>最新のゲーム状態でタブ全体を再描画する（MainDashboard.RefreshAllから毎回呼ぶ）。</summary>
	public void Refresh(GameState state)
	{
		_state = state;
		RefreshResources();
		RefreshResearchCards();
	}

	/// <summary>上部の所持金・素材在庫の一覧表示。</summary>
	private void RefreshResources()
	{
		_resourcesLabel.Clear();
		_resourcesLabel.AppendText($"[b]所持金[/b]：{_state.Gold} G\n");

		// 研究で使う素材IDと、実際に所持している素材IDの両方を漏れなく出す
		// （まだ研究で使わない素材も、採取済みなら在庫として見えるようにする）。
		var knownIds = ResearchBalance.GetAll()
			.SelectMany(r => r.RequiredMaterials.Keys)
			.Union(SoulFusionSystem.IsUnlocked(_state) ? SoulFusionBalance.Catalysts.Select(c => c.MaterialId) : Enumerable.Empty<string>())
			.Union(_state.Materials.Keys)
			.Distinct()
			.OrderBy(id => id)
			.ToList();

		if (knownIds.Count == 0)
		{
			_resourcesLabel.AppendText("[color=gray]素材在庫：まだ何も採取していない（→ 大迷宮タブの探索出撃）。[/color]");
			return;
		}

		string parts = string.Join("、", knownIds.Select(id =>
		{
			int stock = _state.Materials.TryGetValue(id, out int count) ? count : 0;
			return $"{MaterialBalance.GetName(id)}×{stock}";
		}));
		_resourcesLabel.AppendText($"[b]素材在庫[/b]：{parts}");
	}

	/// <summary>研究プロジェクト一覧カードを組み立て直す（→ ResearchBalance.GetAll）。</summary>
	private void RefreshResearchCards()
	{
		foreach (var child in _researchCards.GetChildren())
		{
			_researchCards.RemoveChild(child);
			child.QueueFree();
		}

		foreach (var child in _cultureColumn.GetChildren())
		{
			_cultureColumn.RemoveChild(child);
			child.QueueFree();
		}
		if (SoulFusionSystem.IsUnlocked(_state))
		{
			_cultureColumn.AddChild(BuildCultureTankCard());
			_cultureColumn.AddChild(BuildBirthHistoryCard());
		}
		else
		{
			var locked = NewCard(new Color("#6b7280"));
			locked.AddChild(MakeRichLabel("[font_size=18][b]🧪 培養槽[/b][/font_size]\n[color=gray]研究「魂魄融和の秘薬」を済ませると、ここに培養槽が開く。培養中の娘と、これまでに生まれた娘（親・使った触媒）を一覧できる。[/color]"));
			_cultureColumn.AddChild(locked);
		}
		if (ElixirSystem.IsUnlocked(_state))
			_researchCards.AddChild(BuildElixirCard());

		// 着手できるもの → まだ足りないもの → 済んだもの（1行）の順に並べる
		var researches = ResearchBalance.GetAll()
			.Select((r, i) => (r, i))
			.OrderBy(x => _state.IsResearchCompleted(x.r.Id) ? 2 : ResearchSystem.CanStartResearch(_state, x.r) ? 0 : 1)
			.ThenBy(x => x.i);
		foreach (var (research, _) in researches)
			_researchCards.AddChild(BuildResearchCard(research));
	}

	// ==================== 培養槽（魂魄融和の秘薬） ====================

	/// <summary>
	/// 培養槽のカード（研究「魂魄融和の秘薬」の完了後、研究一覧の先頭に出す）。培養中の子の様子と、
	/// 空きがあれば処方の欄（相性100のペア・子の職業・触媒・費用・処方ボタン）を並べる。
	/// </summary>
	private Control BuildCultureTankCard()
	{
		var card = NewCard(new Color("#2dd4bf"));
		var vbox = new VBoxContainer();
		card.AddChild(vbox);

		vbox.AddChild(MakeRichLabel("[font_size=18][b]🧪 培養槽（魂魄融和の秘薬）[/b][/font_size]"));
		vbox.AddChild(MakeRichLabel(
			$"[color=gray]相性{SoulFusionBalance.RequiredCompatibility}のペアに秘薬を処方すると、2人の素質を受け継ぐ娘が{SoulFusionBalance.CultureWeeks}週で18歳まで育ち、新人として加わる。" +
			"親になれるのは1人1回まで。引退した者も親になれる。[/color]"));

		foreach (var culture in _state.SoulFusionCultures)
			vbox.AddChild(MakeRichLabel(CultureStatusText(culture)));

		if (!SoulFusionSystem.HasFreeTank(_state))
			return card;

		var pairs = SoulFusionSystem.GetEligiblePairs(_state);
		if (pairs.Count == 0)
		{
			vbox.AddChild(MakeRichLabel($"[color=gray]相性{SoulFusionBalance.RequiredCompatibility}のペアがいない（同じ部隊で任務を達成すると相性が上がる）。[/color]"));
			return card;
		}

		_selectedPairIndex = Math.Clamp(_selectedPairIndex, 0, pairs.Count - 1);
		var (a, b) = pairs[_selectedPairIndex];
		var jobs = new[] { a.JobClass, b.JobClass }.Distinct().ToList();
		_selectedJobIndex = Math.Clamp(_selectedJobIndex, 0, jobs.Count - 1);
		var job = jobs[_selectedJobIndex];
		if (_selectedCatalystId != null && SoulFusionBalance.FindCatalyst(_selectedCatalystId) == null)
			_selectedCatalystId = null;

		// 親のペア
		var pairOption = new OptionButton();
		foreach (var (pa, pb) in pairs)
		{
			var tier = SoulFusionSystem.GetNyxTier(pa.JobClass, pb.JobClass);
			pairOption.AddItem($"{MemberLabel(pa)} × {MemberLabel(pb)}　百合相性：{SoulFusionSystem.GetNyxTierLabel(tier)}");
		}
		pairOption.Selected = _selectedPairIndex;
		pairOption.ItemSelected += index => { _selectedPairIndex = (int)index; _selectedJobIndex = 0; RefreshDeferred(); };
		vbox.AddChild(MakeRow("親", pairOption));

		// 子の職業
		var jobOption = new OptionButton();
		foreach (var j in jobs)
			jobOption.AddItem(AdventurerPanel.JobLabel(j));
		jobOption.Selected = _selectedJobIndex;
		jobOption.ItemSelected += index => { _selectedJobIndex = (int)index; RefreshDeferred(); };
		vbox.AddChild(MakeRow("娘の職業", jobOption));

		// 触媒
		var catalystOption = new OptionButton();
		catalystOption.AddItem("なし");
		foreach (var c in SoulFusionBalance.Catalysts)
		{
			int stock = _state.Materials.TryGetValue(c.MaterialId, out int count) ? count : 0;
			catalystOption.AddItem($"{MaterialBalance.GetName(c.MaterialId)}×{c.Count}（所持{stock}）：{c.Note}");
		}
		int catalystIndex = _selectedCatalystId == null ? 0
			: SoulFusionBalance.Catalysts.ToList().FindIndex(c => c.MaterialId == _selectedCatalystId) + 1;
		catalystOption.Selected = catalystIndex;
		catalystOption.ItemSelected += index =>
		{
			_selectedCatalystId = index == 0 ? null : SoulFusionBalance.Catalysts[(int)index - 1].MaterialId;
			RefreshDeferred();
		};
		vbox.AddChild(MakeRow("触媒", catalystOption));

		var nyx = SoulFusionSystem.GetNyxTier(a.JobClass, b.JobClass);
		string goldPart = $"{_state.Gold}/{SoulFusionBalance.PrescriptionGold}G";
		if (_state.Gold < SoulFusionBalance.PrescriptionGold)
			goldPart = $"[color=red]{goldPart}[/color]";
		vbox.AddChild(MakeRichLabel(
			$"費用：{goldPart}　能力限界突破の確率：{SoulFusionSystem.GetBreakthroughChance(nyx, _selectedCatalystId)}%"));

		var check = SoulFusionSystem.CheckPrescription(_state, a, b, job, _selectedCatalystId);
		var button = new Button { Text = "秘薬を処方する", Disabled = check != SoulFusionCheck.Ok || _soulFusionSystem == null };
		if (check != SoulFusionCheck.Ok)
			button.TooltipText = CheckLabel(check);
		button.Pressed += () => OnPrescribePressed(a, b, job, _selectedCatalystId);
		vbox.AddChild(button);

		return card;
	}

	// ==================== 霊薬（§0.61） ====================

	private int _selectedElixirTargetIndex;
	private int _selectedElixirIndex;

	/// <summary>
	/// 霊薬のカード（研究「霊薬の調合法」の完了後）。飲ませる冒険者と霊薬を選び、調合してその場で飲ませる。
	/// 冒険者は現役で出撃していない者（飲んだ数を併記）。
	/// </summary>
	private Control BuildElixirCard()
	{
		var card = NewCard(new Color("#2dd4bf"));
		var vbox = new VBoxContainer();
		card.AddChild(vbox);
		vbox.AddChild(MakeRichLabel("[font_size=18][b]⚗ 霊薬の調合[/b][/font_size]"));
		vbox.AddChild(MakeRichLabel($"[color=gray]飲ませた能力の潜在能力（PA）が上がり、能力値も少し上がる。1人{ElixirBalance.MaxPerAdventurer}本まで。[/color]"));

		var targets = _state.Adventurers.Where(a => !a.IsRetired && !a.IsDispatched).ToList();
		if (targets.Count == 0)
		{
			vbox.AddChild(MakeRichLabel("[color=gray]飲ませられる冒険者がいない（出撃中を除く）。[/color]"));
			return card;
		}
		_selectedElixirTargetIndex = Math.Clamp(_selectedElixirTargetIndex, 0, targets.Count - 1);
		_selectedElixirIndex = Math.Clamp(_selectedElixirIndex, 0, ElixirBalance.Recipes.Count - 1);

		var targetOption = new OptionButton();
		foreach (var a in targets)
			targetOption.AddItem($"{a.Name}（{AdventurerPanel.JobLabel(a.JobClass)}・{a.Age}歳）　霊薬 {a.ElixirsTaken}/{ElixirBalance.MaxPerAdventurer}");
		targetOption.Selected = _selectedElixirTargetIndex;
		targetOption.ItemSelected += index => { _selectedElixirTargetIndex = (int)index; RefreshDeferred(); };
		vbox.AddChild(MakeRow("飲ませる人", targetOption));

		var elixirOption = new OptionButton();
		foreach (var r in ElixirBalance.Recipes)
			elixirOption.AddItem($"{r.Name}（{string.Join("・", r.TargetStats)}：PA+{r.PaBonus}・能力値+{r.StatBonus}）");
		elixirOption.Selected = _selectedElixirIndex;
		elixirOption.ItemSelected += index => { _selectedElixirIndex = (int)index; RefreshDeferred(); };
		vbox.AddChild(MakeRow("霊薬", elixirOption));

		var target = targets[_selectedElixirTargetIndex];
		var recipe = ElixirBalance.Recipes[_selectedElixirIndex];
		vbox.AddChild(MakeRichLabel($"[color=gray]{recipe.Description}[/color]"));

		string goldPart = $"{_state.Gold}/{recipe.RequiredGold}G";
		if (_state.Gold < recipe.RequiredGold) goldPart = $"[color=red]{goldPart}[/color]";
		var materialParts = recipe.RequiredMaterials.Select(kv =>
		{
			int have = _state.Materials.TryGetValue(kv.Key, out int count) ? count : 0;
			string part = $"{MaterialBalance.GetName(kv.Key)}: {have}/{kv.Value}";
			return have < kv.Value ? $"[color=red]{part}[/color]" : part;
		});
		vbox.AddChild(MakeRichLabel($"必要資材：{string.Join("、", new[] { goldPart }.Concat(materialParts))}"));

		var check = ElixirSystem.Check(_state, target, recipe.Id);
		var button = new Button { Text = "調合して飲ませる", Disabled = check != ElixirCheck.Ok };
		if (check != ElixirCheck.Ok)
			button.TooltipText = check switch
			{
				ElixirCheck.LimitReached => $"もう{ElixirBalance.MaxPerAdventurer}本飲んでいる",
				ElixirCheck.NotEnoughGold => "所持金が足りない",
				ElixirCheck.NotEnoughMaterials => "素材が足りない",
				ElixirCheck.Dispatched => "出撃中",
				_ => "飲ませられない",
			};
		button.Pressed += () =>
		{
			if (!ElixirSystem.TryGive(_state, target, recipe.Id)) return;
			LogRequested.Invoke($"[color=violet][b]⚗ {target.Name}に{recipe.Name}を飲ませた。[/b][/color]" +
				$"[color=gray]（{string.Join("・", recipe.TargetStats)}の潜在能力+{recipe.PaBonus}・能力値+{recipe.StatBonus}、霊薬 {target.ElixirsTaken}/{ElixirBalance.MaxPerAdventurer}本）[/color]");
			StateChanged.Invoke();
		};
		vbox.AddChild(button);
		return card;
	}

	/// <summary>
	/// 生まれた娘の履歴：魂魄融和で生まれた娘（ロースター・引退者）を、親・百合相性・使った触媒・能力限界突破とともに並べる。
	/// 生まれの記録（→ Adventurer.FusionCatalystId ほか）は誕生後も娘に残る。旧セーブの娘は触媒「なし」・突破なしで出る。
	/// </summary>
	private Control BuildBirthHistoryCard()
	{
		var card = NewCard(new Color("#f472b6"));
		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 6);
		card.AddChild(vbox);

		var daughters = _state.Adventurers.Concat(_state.RetiredAdventurers)
			.Where(a => a.ParentIds.Count == 2)
			.ToList();
		vbox.AddChild(MakeRichLabel($"[font_size=18][b]🫧 生まれた娘（{daughters.Count}名）[/b][/font_size]"));
		if (daughters.Count == 0)
		{
			vbox.AddChild(MakeRichLabel("[color=gray]まだ誰も生まれていない。[/color]"));
			return card;
		}

		foreach (var d in daughters)
		{
			string parentA = _state.FindAdventurer(d.ParentIds[0])?.Name ?? "？";
			string parentB = _state.FindAdventurer(d.ParentIds[1])?.Name ?? "？";
			string catalyst = d.FusionCatalystId == null ? "なし" : MaterialBalance.GetName(d.FusionCatalystId);
			string breakthrough = d.FusionBreakthroughStats.Count == 0 ? "" : $"　[color=gold]✨ 能力限界突破：{string.Join("・", d.FusionBreakthroughStats)}[/color]";
			string state = d.IsRetired ? "[color=gray]（引退）[/color]" : "";
			vbox.AddChild(MakeRichLabel(
				$"[b]{d.Name}[/b]（{AdventurerPanel.JobLabel(d.JobClass)}・{d.Age}歳）{state}\n" +
				$"　親：{parentA} × {parentB}　百合相性：{SoulFusionSystem.GetNyxTierLabel(d.FusionNyxTier)}\n" +
				$"　使った触媒：{catalyst}{breakthrough}"));
		}
		return card;
	}

	private string CultureStatusText(SoulFusionCulture culture)
	{
		string parentA = _state.FindAdventurer(culture.ParentAId)?.Name ?? "？";
		string parentB = _state.FindAdventurer(culture.ParentBId)?.Name ?? "？";
		string progress = SoulFusionSystem.IsWaitingForRoom(culture)
			? "[color=orange]育ちきった。宿舎に空きができしだい加わる[/color]"
			: $"誕生まで残り{culture.WeeksRemaining}週";
		return $"🫧 培養中：[b]{culture.Child.Name}[/b]（{AdventurerPanel.JobLabel(culture.Child.JobClass)}）" +
			$"　{parentA}と{parentB}の娘・百合相性：{SoulFusionSystem.GetNyxTierLabel(culture.NyxTier)}　{progress}";
	}

	/// <summary>候補の表示名。引退者は（引退）を付ける。</summary>
	private static string MemberLabel(Adventurer a) =>
		$"{a.Name}（{AdventurerPanel.JobLabel(a.JobClass)}{(a.IsRetired ? "・引退" : "")}）";

	private static string CheckLabel(SoulFusionCheck check) => check switch
	{
		SoulFusionCheck.NotUnlocked => "研究「魂魄融和の秘薬」が必要",
		SoulFusionCheck.TankBusy => "培養槽が使用中",
		SoulFusionCheck.NotEligiblePair => "このペアは親になれない",
		SoulFusionCheck.InvalidJob => "娘の職業はどちらかの親の職業から選ぶ",
		SoulFusionCheck.UnknownCatalyst => "触媒にならない素材",
		SoulFusionCheck.NotEnoughGold => "所持金が足りない",
		SoulFusionCheck.NotEnoughCatalyst => "触媒の素材が足りない",
		_ => "",
	};

	private void OnPrescribePressed(Adventurer a, Adventurer b, JobClass job, string? catalystId)
	{
		var culture = _soulFusionSystem?.TryPrescribe(_state, a, b, job, catalystId);
		if (culture == null)
			return;

		string catalystText = catalystId == null ? "" : $"（触媒：{MaterialBalance.GetName(catalystId)}）";
		LogRequested.Invoke($"[color=violet][b]🧪 {a.Name}と{b.Name}に魂魄融和の秘薬を処方した{catalystText}。[/b][/color]\n" +
			$"[color=gray]培養槽で娘の{culture.Child.Name}が育ちはじめた。誕生まで{culture.WeeksRemaining}週。[/color]");
		_selectedPairIndex = 0;
		_selectedJobIndex = 0;
		_selectedCatalystId = null;
		StateChanged.Invoke();
	}

	/// <summary>OptionButton のシグナルの中でカードを作り直すと発信元を解放してしまうため、次のフレームで描き直す。</summary>
	private void RefreshDeferred() => Callable.From(RefreshResearchCards).CallDeferred();

	/// <summary>カードの枠：余白と左帯の色（緑＝済み、黄＝着手できる、灰＝まだ、青緑＝研究室の設備）。</summary>
	private static PanelContainer NewCard(Color accent)
	{
		var style = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.04f), BorderColor = accent, BorderWidthLeft = 4 };
		style.SetCornerRadiusAll(3);
		style.ContentMarginLeft = 14;
		style.ContentMarginRight = 12;
		style.ContentMarginTop = 8;
		style.ContentMarginBottom = 8;
		var card = new PanelContainer();
		card.AddThemeStyleboxOverride("panel", style);
		return card;
	}

	private static RichTextLabel MakeRichLabel(string bbcode)
	{
		var label = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		label.AppendText(bbcode);
		return label;
	}

	private static HBoxContainer MakeRow(string caption, Control control)
	{
		var row = new HBoxContainer();
		row.AddChild(new Label { Text = caption, CustomMinimumSize = new Vector2(96, 0) });
		control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddChild(control);
		return row;
	}

	/// <summary>
	/// 研究1件分のカード：研究名・説明・必要素材と費用の充足状況（現在/必要）・実行ボタンを
	/// 1つのPanelContainerにまとめる。完了済みは「研究完了」バッジを出し、ボタンを無効化する。
	/// </summary>
	private Control BuildResearchCard(ResearchDefinition research)
	{
		bool completed = _state.IsResearchCompleted(research.Id);
		bool canStart = !completed && ResearchSystem.CanStartResearch(_state, research);

		var card = NewCard(completed ? new Color("#4ade80") : canStart ? new Color("#fbbf24") : new Color("#6b7280"));
		var vbox = new VBoxContainer();
		card.AddChild(vbox);

		// 済んだ研究は1行にたたむ（名前と効果だけ）。数が増えても一覧が長くならないように
		if (completed)
		{
			vbox.AddChild(MakeRichLabel($"[b]{research.Name}[/b]　[bgcolor=#2a5a2a][color=lime] ✔ 研究完了 [/color][/bgcolor]　[color=gray]{research.Description}[/color]"));
			return card;
		}
		if (!ResearchSystem.IsPrerequisiteMet(_state, research))
			card.Modulate = new Color(1, 1, 1, 0.65f);

		var titleLabel = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		titleLabel.AppendText($"[font_size=18][b]{research.Name}[/b][/font_size]" +
			(completed ? "　[bgcolor=#2a5a2a][color=lime] ✔ 研究完了 [/color][/bgcolor]" : ""));
		vbox.AddChild(titleLabel);

		var descLabel = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		descLabel.AppendText($"[color=gray]{research.Description}[/color]");
		vbox.AddChild(descLabel);

		// 段階研究の前提（§0.60）。済んでいなければ赤字。
		if (!completed && research.PrerequisiteId != null)
		{
			string prereqName = ResearchBalance.Find(research.PrerequisiteId)?.Name ?? research.PrerequisiteId;
			vbox.AddChild(MakeRichLabel(ResearchSystem.IsPrerequisiteMet(_state, research)
				? $"前提：{prereqName} ✔"
				: $"[color=red]前提：「{prereqName}」を先に済ませる[/color]"));
		}

		var costLabel = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		costLabel.AppendText($"必要資材：{BuildCostLine(research)}");
		vbox.AddChild(costLabel);

		var button = new Button { Text = completed ? "研究完了" : "研究実行", Disabled = completed || !canStart };
		if (!completed)
			button.Pressed += () => OnResearchButtonPressed(research);
		vbox.AddChild(button);

		return card;
	}

	/// <summary>「月光草: 3/5」のような充足状況の文字列。不足している項目は赤字にする。</summary>
	private string BuildCostLine(ResearchDefinition research)
	{
		string goldPart = $"{_state.Gold}/{research.RequiredGold}G";
		if (_state.Gold < research.RequiredGold)
			goldPart = $"[color=red]{goldPart}[/color]";

		var materialParts = research.RequiredMaterials.Select(kv =>
		{
			int have = _state.Materials.TryGetValue(kv.Key, out int count) ? count : 0;
			string part = $"{MaterialBalance.GetName(kv.Key)}: {have}/{kv.Value}";
			return have < kv.Value ? $"[color=red]{part}[/color]" : part;
		});

		return string.Join("、", new[] { goldPart }.Concat(materialParts));
	}

	private void OnResearchButtonPressed(ResearchDefinition research)
	{
		if (!ResearchSystem.CompleteResearch(_state, research))
			return;

		LogRequested.Invoke($"[color=gold][b]🔬 アルベールの研究室で「{research.Name}」が完了した！[/b][/color]\n" +
			$"[color=gray]{research.Description}[/color]");
		StateChanged.Invoke();
	}
}
