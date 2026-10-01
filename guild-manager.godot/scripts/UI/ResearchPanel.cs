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

		if (SoulFusionSystem.IsUnlocked(_state))
			_researchCards.AddChild(BuildCultureTankCard());

		foreach (var research in ResearchBalance.GetAll())
			_researchCards.AddChild(BuildResearchCard(research));
	}

	// ==================== 培養槽（魂魄融和の秘薬） ====================

	/// <summary>
	/// 培養槽のカード（研究「魂魄融和の秘薬」の完了後、研究一覧の先頭に出す）。培養中の子の様子と、
	/// 空きがあれば処方の欄（相性100のペア・子の職業・触媒・費用・処方ボタン）を並べる。
	/// </summary>
	private Control BuildCultureTankCard()
	{
		var card = new PanelContainer();
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

		var card = new PanelContainer();
		var vbox = new VBoxContainer();
		card.AddChild(vbox);

		var titleLabel = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		titleLabel.AppendText($"[font_size=18][b]{research.Name}[/b][/font_size]" +
			(completed ? "　[bgcolor=#2a5a2a][color=lime] ✔ 研究完了 [/color][/bgcolor]" : ""));
		vbox.AddChild(titleLabel);

		var descLabel = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		descLabel.AppendText($"[color=gray]{research.Description}[/color]");
		vbox.AddChild(descLabel);

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
