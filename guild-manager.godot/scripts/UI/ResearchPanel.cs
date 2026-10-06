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
		BuildLayout();
	}

	/// <summary>培養槽の欄（誕生した娘の履歴つき）。ツリーの下の右半分。</summary>
	private VBoxContainer _cultureColumn = null!;

	/// <summary>研究のツリー（§0.77）：札（Button）を置き、前提の線は Draw で描く。</summary>
	private Control _treeCanvas = null!;

	/// <summary>ツリーの右（狭いときは下）に出す、選んだ研究の詳細。</summary>
	private VBoxContainer _detailBox = null!;

	/// <summary>選んでいる研究のId（無ければ着手できるものから選び直す）。</summary>
	private string? _selectedResearchId;

	/// <summary>札の位置（研究Id→矩形）。線を引くのに使う。</summary>
	private readonly System.Collections.Generic.Dictionary<string, Rect2> _nodeRects = new();

	// ---- ツリーの寸法 ----
	private const float TreeLabelWidth = 64f;
	private const float TreeColumnWidth = 168f;
	private const float TreeNodeWidth = 148f;
	private const float TreeNodeHeight = 34f;
	private const float TreeRowHeight = 44f;
	private const float TreeHeaderHeight = 26f;
	private const int TreeColumns = 5;

	/// <summary>
	/// 上：題・所持金と素材・研究のツリーと詳細（§0.77）。下：左に霊薬のカード、右に培養槽の欄。
	/// 研究は素材のフィールドを列、系統を行にしたツリーで見せる。
	/// </summary>
	private void BuildLayout()
	{
		var top = GetNode<VBoxContainer>("VBox");
		RemoveChild(top);
		top.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		var outer = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		outer.AddThemeConstantOverride("separation", 12);
		outer.AddChild(top);

		// ツリーと詳細を横に並べる。画面が狭ければ詳細は下へ回る
		var treeRow = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		treeRow.AddThemeConstantOverride("h_separation", 16);
		treeRow.AddThemeConstantOverride("v_separation", 12);
		_treeCanvas = new Control();
		_treeCanvas.Draw += DrawTreeLines;
		treeRow.AddChild(_treeCanvas);
		var detailCard = NewCard(new Color("#fbbf24"));
		detailCard.CustomMinimumSize = new Vector2(360, 0);
		detailCard.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_detailBox = new VBoxContainer();
		_detailBox.AddThemeConstantOverride("separation", 6);
		detailCard.AddChild(_detailBox);
		treeRow.AddChild(detailCard);
		top.AddChild(treeRow);
		top.MoveChild(treeRow, _researchCards.GetIndex());

		// 下：霊薬（左）と培養槽（右）
		top.RemoveChild(_researchCards);
		var split = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		split.AddThemeConstantOverride("separation", 16);
		_researchCards.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_researchCards.SizeFlagsVertical = SizeFlags.ShrinkBegin;
		split.AddChild(_researchCards);
		_cultureColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkBegin, SizeFlagsStretchRatio = 1f };
		_cultureColumn.AddThemeConstantOverride("separation", 8);
		split.AddChild(_cultureColumn);
		outer.AddChild(split);
		AddChild(outer);
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
		_resourcesLabel.AppendText($"[b]所持金[/b]：{_state.Gold} G" +
			// 研究の手伝い（§0.73）：待機中に研究を手伝った冒険者がいると貯まり、研究費から割り引く
			$"　[b]研究の手伝い[/b]：{_state.ResearchCredit} G（研究費の{TrainingBalance.ResearchCreditMaxDiscountRate * 100:0}%まで割り引く。上限 {TrainingBalance.ResearchCreditMax} G）\n");

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

	/// <summary>研究のツリーと詳細・霊薬・培養槽を組み立て直す（§0.77）。</summary>
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
		else
		{
			var locked = NewCard(new Color("#6b7280"));
			locked.AddChild(MakeRichLabel("[font_size=18][b]⚗ 霊薬の調合[/b][/font_size]\n[color=gray]研究「霊薬の調合法」を済ませると、ここで霊薬を調合して飲ませられる。[/color]"));
			_researchCards.AddChild(locked);
		}

		RefreshTree();
		RefreshDetail();
	}

	// ==================== 研究のツリー（§0.77） ====================

	/// <summary>系統ごとの段の数（その系統の研究の Lane の最大＋1）を、系統の並び順で。</summary>
	private static System.Collections.Generic.List<(ResearchBranch Branch, int Lanes)> BranchRows() =>
		Enum.GetValues<ResearchBranch>()
			.Select(b => (b, ResearchBalance.GetAll().Where(r => r.Branch == b).Select(r => r.Lane + 1).DefaultIfEmpty(0).Max()))
			.Where(x => x.Item2 > 0)
			.ToList();

	/// <summary>札を置き直す。列＝素材のフィールド（森〜深淵）、行＝系統と段。</summary>
	private void RefreshTree()
	{
		foreach (var child in _treeCanvas.GetChildren())
		{
			_treeCanvas.RemoveChild(child);
			child.QueueFree();
		}
		_nodeRects.Clear();

		var all = ResearchBalance.GetAll();
		if (_selectedResearchId == null || all.All(r => r.Id != _selectedResearchId))
			_selectedResearchId = (all.FirstOrDefault(r => ResearchSystem.CanStartResearch(_state, r))
				?? all.FirstOrDefault(r => !_state.IsResearchCompleted(r.Id) && ResearchSystem.IsRevealed(_state, r))
				?? all[0]).Id;

		// 列の見出し（フィールド名。まだ入っていないフィールドは「？」）
		for (int col = 1; col <= TreeColumns; col++)
		{
			var field = _state.DungeonFields.FirstOrDefault(f => f.Order == col);
			string name = field == null ? $"第{col}" : field.IsUnlocked ? field.Name : "？";
			_treeCanvas.AddChild(new Label
			{
				Text = name,
				Position = new Vector2(TreeLabelWidth + (col - 1) * TreeColumnWidth, 0),
				Size = new Vector2(TreeNodeWidth, TreeHeaderHeight),
				HorizontalAlignment = HorizontalAlignment.Center,
				Modulate = new Color(0.75f, 0.8f, 0.9f),
				ClipText = true,
			});
		}

		int rowOffset = 0;
		foreach (var (branch, lanes) in BranchRows())
		{
			float branchY = TreeHeaderHeight + rowOffset * TreeRowHeight;
			_treeCanvas.AddChild(new Label
			{
				Text = BranchLabel(branch),
				Position = new Vector2(0, branchY),
				Size = new Vector2(TreeLabelWidth - 6, TreeNodeHeight),
				VerticalAlignment = VerticalAlignment.Center,
			});
			foreach (var research in all.Where(r => r.Branch == branch))
			{
				int col = Math.Clamp(ResearchSystem.GetFieldOrder(_state, research), 1, TreeColumns);
				var rect = new Rect2(TreeLabelWidth + (col - 1) * TreeColumnWidth, branchY + research.Lane * TreeRowHeight, TreeNodeWidth, TreeNodeHeight);
				_nodeRects[research.Id] = rect;
				_treeCanvas.AddChild(BuildTreeNode(research, rect));
			}
			rowOffset += lanes;
		}

		_treeCanvas.CustomMinimumSize = new Vector2(TreeLabelWidth + TreeColumns * TreeColumnWidth, TreeHeaderHeight + rowOffset * TreeRowHeight);
		_treeCanvas.QueueRedraw();
	}

	/// <summary>
	/// 研究の札：済み＝緑、着手できる＝黄、前提は済んだが素材・費用が足りない＝灰の枠、前提がまだ＝薄く、
	/// まだ入っていないフィールドの研究＝「？」。選んでいる札は枠を太くする。押すと詳細に出す。
	/// </summary>
	private Button BuildTreeNode(ResearchDefinition research, Rect2 rect)
	{
		bool revealed = ResearchSystem.IsRevealed(_state, research);
		var (fill, border) = NodeColors(research);
		bool selected = research.Id == _selectedResearchId;

		var button = new Button
		{
			Text = revealed ? research.Name : "？",
			TooltipText = revealed ? $"{research.Name}\n{research.Description}" : "まだ入っていないフィールドの素材が要る研究",
			Position = rect.Position,
			Size = rect.Size,
			ClipText = true,
			FocusMode = FocusModeEnum.None,
		};
		button.AddThemeFontSizeOverride("font_size", 14);
		foreach (var (name, lighten) in new[] { ("normal", 0f), ("hover", 0.12f), ("pressed", 0.2f), ("disabled", 0f) })
		{
			var style = new StyleBoxFlat { BgColor = fill.Lightened(lighten), BorderColor = selected ? new Color("#fde68a") : border };
			style.SetBorderWidthAll(selected ? 3 : 1);
			style.SetCornerRadiusAll(4);
			style.ContentMarginLeft = 6;
			style.ContentMarginRight = 6;
			button.AddThemeStyleboxOverride(name, style);
		}
		if (!revealed || !ResearchSystem.IsPrerequisiteMet(_state, research))
			button.Modulate = new Color(1, 1, 1, 0.6f);
		button.Pressed += () =>
		{
			_selectedResearchId = research.Id;
			RefreshDeferred();
		};
		return button;
	}

	/// <summary>札の塗りと枠の色（状態ごと）。</summary>
	private (Color Fill, Color Border) NodeColors(ResearchDefinition research)
	{
		if (_state.IsResearchCompleted(research.Id))
			return (new Color("#1f4d2e"), new Color("#4ade80"));
		if (ResearchSystem.CanStartResearch(_state, research))
			return (new Color("#4d3b12"), new Color("#fbbf24"));
		return (new Color("#262a33"), new Color("#6b7280"));
	}

	/// <summary>前提の線：親の札の右から子の札の左へ（同じ列なら親の下から子の上へ）。済んだ前提からの線は緑。</summary>
	private void DrawTreeLines()
	{
		if (_state == null) return;
		foreach (var research in ResearchBalance.GetAll())
		{
			if (research.PrerequisiteId == null
				|| !_nodeRects.TryGetValue(research.Id, out var child)
				|| !_nodeRects.TryGetValue(research.PrerequisiteId, out var parent))
				continue;

			var color = _state.IsResearchCompleted(research.PrerequisiteId) ? new Color("#4ade80") : new Color("#8a8f99");
			Vector2 end;
			Vector2[] points;
			if (child.Position.X > parent.End.X)
			{
				var start = new Vector2(parent.End.X, parent.GetCenter().Y);
				end = new Vector2(child.Position.X, child.GetCenter().Y);
				float midX = child.Position.X - 10;
				points = start.Y == end.Y
					? new[] { start, end }
					: new[] { start, new Vector2(midX, start.Y), new Vector2(midX, end.Y), end };
				_treeCanvas.DrawPolyline(points, color, 2f);
				_treeCanvas.DrawColoredPolygon(new[] { end, end + new Vector2(-8, -5), end + new Vector2(-8, 5) }, color);
			}
			else
			{
				var start = new Vector2(parent.GetCenter().X, parent.End.Y);
				end = new Vector2(parent.GetCenter().X, child.Position.Y);
				_treeCanvas.DrawLine(start, end, color, 2f);
				_treeCanvas.DrawColoredPolygon(new[] { end, end + new Vector2(-5, -8), end + new Vector2(5, -8) }, color);
			}
		}
	}

	private static string BranchLabel(ResearchBranch branch) => branch switch
	{
		ResearchBranch.Recovery => "療養",
		ResearchBranch.Dungeon => "迷宮",
		ResearchBranch.Gathering => "採取",
		ResearchBranch.Growth => "育成",
		ResearchBranch.Talent => "人材",
		ResearchBranch.Trade => "商い",
		ResearchBranch.SoulFusion => "秘薬",
		_ => branch.ToString(),
	};

	/// <summary>選んだ研究の詳細：名前・系統・説明・前提・必要資材・研究ボタン。まだ入っていないフィールドの研究は「？」。</summary>
	private void RefreshDetail()
	{
		foreach (var child in _detailBox.GetChildren())
		{
			_detailBox.RemoveChild(child);
			child.QueueFree();
		}
		var research = _selectedResearchId == null ? null : ResearchBalance.Find(_selectedResearchId);
		if (research == null)
			return;

		bool completed = _state.IsResearchCompleted(research.Id);
		if (!ResearchSystem.IsRevealed(_state, research))
		{
			var lockedFields = research.RequiredMaterials.Keys
				.Select(id => MaterialBalance.Find(id)?.FieldId)
				.Select(fid => _state.DungeonFields.FirstOrDefault(f => f.Id == fid))
				.Where(f => f != null && !f.IsUnlocked)
				.Select(f => f!.Order)
				.Distinct()
				.OrderBy(o => o);
			_detailBox.AddChild(MakeRichLabel($"[font_size=18][b]？？？[/b][/font_size]　[color=gray]{BranchLabel(research.Branch)}[/color]"));
			_detailBox.AddChild(MakeRichLabel($"[color=gray]まだ入っていないフィールド（第{string.Join("・第", lockedFields)}フィールド）の素材が要る研究。そのフィールドに入ると、名前と効果が分かる。[/color]"));
			return;
		}

		_detailBox.AddChild(MakeRichLabel($"[font_size=18][b]{research.Name}[/b][/font_size]　[color=gray]{BranchLabel(research.Branch)}[/color]" +
			(completed ? "　[bgcolor=#2a5a2a][color=lime] ✔ 研究完了 [/color][/bgcolor]" : "")));
		_detailBox.AddChild(MakeRichLabel($"[color=gray]{research.Description}[/color]"));

		if (research.PrerequisiteId != null)
		{
			string prereqName = ResearchBalance.Find(research.PrerequisiteId)?.Name ?? research.PrerequisiteId;
			_detailBox.AddChild(MakeRichLabel(ResearchSystem.IsPrerequisiteMet(_state, research)
				? $"前提：{prereqName} ✔"
				: $"[color=red]前提：「{prereqName}」を先に済ませる[/color]"));
		}
		var next = ResearchBalance.GetAll().Where(r => r.PrerequisiteId == research.Id).ToList();
		if (next.Count > 0)
			_detailBox.AddChild(MakeRichLabel($"[color=gray]この先：{string.Join("、", next.Select(r => ResearchSystem.IsRevealed(_state, r) ? r.Name : "？"))}[/color]"));

		if (completed)
			return;

		_detailBox.AddChild(MakeRichLabel($"必要資材：{BuildCostLine(research)}"));
		var button = new Button { Text = "研究する", Disabled = !ResearchSystem.CanStartResearch(_state, research) };
		button.Pressed += () => OnResearchButtonPressed(research);
		_detailBox.AddChild(button);
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

	/// <summary>「月光草: 3/5」のような充足状況の文字列。不足している項目は赤字にする。</summary>
	private string BuildCostLine(ResearchDefinition research)
	{
		// 研究の手伝い（§0.73）で割り引ける分があれば、払う額と割引を並べる
		int discount = ResearchSystem.GetDiscount(_state, research);
		int pay = research.RequiredGold - discount;
		string goldPart = discount > 0
			? $"{_state.Gold}/{pay}G（{research.RequiredGold}G − 研究の手伝い {discount}G）"
			: $"{_state.Gold}/{research.RequiredGold}G";
		if (_state.Gold < pay)
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
		int discount = ResearchSystem.GetDiscount(_state, research);
		if (!ResearchSystem.CompleteResearch(_state, research))
			return;

		LogRequested.Invoke($"[color=gold][b]🔬 アルベールの研究室で「{research.Name}」が完了した！[/b][/color]\n" +
			$"[color=gray]{research.Description}[/color]" +
			(discount > 0 ? $"\n[color=lime]研究の手伝いで {discount} G 割り引いた（払ったのは {research.RequiredGold - discount} G）。[/color]" : ""));
		StateChanged.Invoke();
	}
}
