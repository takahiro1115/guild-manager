#nullable enable
using Godot;
using System;
using System.Linq;
using System.Text;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 戦績の頁（2026年10月・大会と育成の栄光 段2、→ docs/検討中_大会と育成の栄光.md §4.1）。冒険者の詳細の【📜 戦績】・殿堂・引退式から開く。
/// 見出し（顔・名前・二つ名・殿堂）、大会（格ごとの優勝と入賞・勝ち鞍）、迷宮（撃破・主役・いちばん深い撃破）、能力（ピークと今・見立て）、
/// 二つ名の一覧、系譜と関係（母・娘・関係の深い相手とタグ）、観測日誌（年表）を1枚に出す。名前を押すとその者の頁へ移る。
/// 値はすべて Core（HonorSystem）から取る。シーンは使わずコードで組む（MainDashboard が1つ作って使い回す）。
/// </summary>
public partial class HonorRecordPopup : Window
{
	private GameState _state = null!;
	private TextureRect _portrait = null!;
	private RichTextLabel _header = null!;
	private RichTextLabel _body = null!;
	private ScrollContainer _scroll = null!;

	public override void _Ready()
	{
		Title = "📜 戦績";
		Exclusive = true;
		Size = new Vector2I(940, 760);
		Theme = GD.Load<Theme>("res://themes/dungeon_theme.tres");
		CloseRequested += Hide;

		var panel = new PanelContainer();
		panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(panel);
		var margin = new MarginContainer();
		foreach (var side in new[] { "left", "top", "right", "bottom" })
			margin.AddThemeConstantOverride($"margin_{side}", 16);
		panel.AddChild(margin);
		var root = new VBoxContainer();
		root.AddThemeConstantOverride("separation", 10);
		margin.AddChild(root);

		var head = new HBoxContainer();
		head.AddThemeConstantOverride("separation", 16);
		_portrait = new TextureRect
		{
			CustomMinimumSize = new Vector2(120, 150),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
		};
		head.AddChild(_portrait);
		_header = NewText();
		_header.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		head.AddChild(_header);
		root.AddChild(head);
		root.AddChild(new HSeparator());

		_scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		_body = NewText();
		_body.MetaClicked += meta => { if (Guid.TryParse(meta.AsString(), out var id) && _state.FindAdventurer(id) is { } other) Render(other); };
		_scroll.AddChild(_body);
		root.AddChild(_scroll);

		var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		var close = new Button { Text = "閉じる", CustomMinimumSize = new Vector2(160, 38) };
		close.Pressed += Hide;
		buttons.AddChild(close);
		root.AddChild(buttons);
	}

	private static RichTextLabel NewText() =>
		new() { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

	/// <summary>冒険者の戦績の頁を開く（現役・引退者・除籍者のどれでもよい）。</summary>
	public void Open(GameState state, Adventurer a)
	{
		_state = state;
		Render(a);
		PopupCentered(Size);
	}

	private void Render(Adventurer a)
	{
		_portrait.Texture = AdventurerPanel.LoadPortraitTexture(a.PortraitId);
		_header.Text = BuildHeader(a);
		_body.Text = BuildBody(a);
		_scroll.ScrollVertical = 0;
	}

	private string BuildHeader(Adventurer a)
	{
		var sb = new StringBuilder();
		sb.Append($"[font_size=22]{a.Name}[/font_size]");
		string title = HonorSystem.DisplayTitle(_state, a);
		if (title.Length > 0)
			sb.Append($"　[color=gold]「{title}」[/color]");
		sb.Append('\n');
		string where = a.IsRetired ? "引退" : _state.FallenAdventurers.Contains(a) ? "除籍" : "現役";
		sb.Append($"{a.Age}歳・在籍{HonorSystem.YearsActive(a)}年（{where}）");
		if (a.HallOfFameYear is int year)
			sb.Append($"\n[color=gold]🏛 殿堂入り（{year}年目・{a.HallOfFameReason}）[/color]");
		else if (!a.IsRetired && HonorSystem.HallOfFameReason(_state, a) is { } reason)
			sb.Append($"\n[color=khaki]🏛 殿堂入りの資格あり（{reason}。引退のときに殿堂入りする）[/color]");
		return sb.ToString();
	}

	private string BuildBody(Adventurer a)
	{
		var sb = new StringBuilder();

		// ---- 大会 ----
		sb.Append("[font_size=18]🏆 大会[/font_size]\n");
		sb.Append("[table=3][cell][color=gray]格[/color]　[/cell][cell][color=gray]優勝[/color]　[/cell][cell][color=gray]入賞（ベスト4以上）[/color][/cell]");
		foreach (var (grade, wins, placings) in HonorSystem.GradeCounts(a))
			sb.Append($"[cell]{grade}[/cell][cell]{wins}[/cell][cell]{placings}[/cell]");
		sb.Append("[/table]\n");
		var winList = a.TournamentRecords.Where(r => r.Placing == 1).OrderBy(r => r.Year).ThenBy(r => r.Week).ToList();
		if (winList.Count > 0)
			sb.Append("勝ち鞍：" + string.Join("、", winList.Select(r => $"{r.Year}年目 {r.Name}（{TournamentSystem.GradeLabel(r.Grade)}）")) + "\n");
		else
			sb.Append("[color=gray]勝ち鞍はまだ無い[/color]\n");

		// ---- 迷宮 ----
		sb.Append("\n[font_size=18]⚔ 迷宮[/font_size]\n");
		sb.Append($"ボス撃破に加わった回数 {a.BossKills.Count}回（うち主役 {a.BossKills.Count(k => k.IsMvp)}回）");
		if (HonorSystem.DeepestKill(_state, a) is { } deepest)
			sb.Append($"　いちばん深い撃破：{deepest.FieldName} 第{deepest.Floor}層「{deepest.BossName}」");
		sb.Append('\n');

		// ---- 能力 ----
		sb.Append("\n[font_size=18]📈 能力（ピーク／今）[/font_size]\n");
		var now = HonorSystem.CurrentStats(a);
		sb.Append(string.Join("　", HonorSystem.StatNames.Select(s => $"{s} {(a.PeakStats.TryGetValue(s, out int p) ? p : now[s])}／{now[s]}")));
		sb.Append('\n');
		if (a.JoinEstimate.Length > 0)
			sb.Append($"副官の見立て：加入時 {a.JoinEstimate}" + (a.IsRetired ? "" : $" → 今 {PotentialEstimateSystem.TotalRankLabel(_state, a)}") + "\n");

		// ---- 二つ名 ----
		var titles = HonorSystem.GetTitles(_state, a);
		sb.Append("\n[font_size=18]🎖 二つ名[/font_size]\n");
		sb.Append(titles.Count == 0 ? "[color=gray]まだ無い[/color]\n"
			: string.Join("、", titles.OrderByDescending(t => t.Rank).Select(t => $"{t.Name}（{RankLabel(t.Rank)}）")) + "\n");

		// ---- 系譜と関係 ----
		sb.Append("\n[font_size=18]🌸 系譜と関係[/font_size]\n");
		foreach (var parentId in a.ParentIds.Distinct())
		{
			if (_state.FindAdventurer(parentId) is not { } mother) continue;
			var motherWins = mother.TournamentRecords.Where(r => r.Placing == 1).Select(r => r.Name).Distinct().ToList();
			sb.Append($"母：{Link(mother)}" + (motherWins.Count > 0 ? $"（勝ち鞍：{string.Join("、", motherWins)}）" : "") + "\n");
		}
		var daughters = _state.Adventurers.Concat(_state.RetiredAdventurers).Concat(_state.FallenAdventurers).Where(o => o.ParentIds.Contains(a.Id)).ToList();
		if (daughters.Count > 0)
			sb.Append("娘：" + string.Join("、", daughters.Select(Link)) + "\n");
		var relations = HonorSystem.GetRelations(_state, a);
		if (relations.Count == 0 && a.ParentIds.Count == 0 && daughters.Count == 0)
			sb.Append("[color=gray]まだ無い[/color]\n");
		foreach (var r in relations)
		{
			string tags = r.Tags.Count > 0 ? "　[color=violet]" + string.Join("・", r.Tags) + "[/color]" : "";
			sb.Append($"{(_state.FindAdventurer(r.OtherId) is { } other ? Link(other) : r.OtherName)}（相性{r.Compatibility}）{tags}\n");
		}

		// ---- 観測日誌 ----
		sb.Append("\n[font_size=18]📖 観測日誌（アルベールの記録）[/font_size]\n");
		if (a.Journal.Count == 0)
			sb.Append("[color=gray]まだ記録が無い（次の決算から書き始める）[/color]\n");
		foreach (var e in a.Journal)
			sb.Append($"[color=gray]{GameCalendar.FormatMonth(e.Week)}[/color]　{e.Text}\n");

		return sb.ToString();
	}

	private static string Link(Adventurer a) => $"[url={a.Id}]{a.Name}[/url]";

	/// <summary>称号の格の表記。</summary>
	public static string RankLabel(TitleRank rank) => rank switch
	{
		TitleRank.Legend => "伝説",
		TitleRank.Name => "名",
		TitleRank.Honor => "誉",
		_ => "並",
	};
}
