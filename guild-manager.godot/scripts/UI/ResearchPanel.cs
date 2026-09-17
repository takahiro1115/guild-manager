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

	/// <summary>週報ログ（右ペイン）への追記を依頼する（BBCode文字列）。</summary>
	public event Action<string> LogRequested = delegate { };

	/// <summary>研究完了でゲーム状態が変わったことを通知する（MainDashboardが全体を再描画する）。</summary>
	public event Action StateChanged = delegate { };

	public override void _Ready()
	{
		_resourcesLabel = GetNode<RichTextLabel>("%ResourcesLabel");
		_researchCards = GetNode<VBoxContainer>("%ResearchCards");
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
			return $"{MaterialCatalog.GetName(id)}×{stock}";
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

		foreach (var research in ResearchBalance.GetAll())
			_researchCards.AddChild(BuildResearchCard(research));
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
		titleLabel.AppendText($"[font_size=16][b]{research.Name}[/b][/font_size]" +
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
			string part = $"{MaterialCatalog.GetName(kv.Key)}: {have}/{kv.Value}";
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
