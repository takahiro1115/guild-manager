using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 月報の小窓（2026年10月・§0.71、→ Core の MonthlyReport）。「次の月へ」で月を進めるたびに開き、どの画面にいても月の結果を見せる。
/// §0.81：タブで分け、部隊・冒険者が何をしてどうなったかを見せる。
///  - 概要：見出し（月・所持金と機嫌の増減）、主な出来事、来月への注意（項目ごとに、その画面へ移るボタン）
///  - 部隊：部隊ごとの方針・メンバー・週ごとの行動（出撃の結果・待機の理由）・月の結果・今のHP
///  - 冒険者：1人1行の表（所属・過ごし方の内訳・成長・今の状態）
///  - 週ごとの記録：計算の内訳つきの細かい記録（その月を進めた直後だけ。セーブには残さない）
///  - 【閉じる】【閉じて次の月へ】（割り込み〈採用試験・依頼の到着・クリア・敗北〉がある月は次の月へ進めない）
///  - 画面上部の【📅 月報】から開くと、過去の月報（直近12か月、→ GameState.MonthlyReports）を選んで見返せる
/// シーンは使わずコードで組む（MainDashboard が1つ作って使い回す）。
/// </summary>
public partial class MonthlyReportPopup : Window
{
	/// <summary>閉じた（移動・次の月へを含む）。</summary>
	public event Action Closed = delegate { };

	/// <summary>【閉じて次の月へ】が押された（閉じたあとに呼ぶ）。</summary>
	public event Action NextMonthRequested = delegate { };

	/// <summary>注意の項目から画面へ移る（閉じたあとに呼ぶ）。</summary>
	public event Action<MonthlyNoteTarget> JumpRequested = delegate { };

	private OptionButton _historyOption = null!;
	private TabContainer _tabs = null!;
	private RichTextLabel _summary = null!;
	private VBoxContainer _notesBox = null!;
	private RichTextLabel _squads = null!;
	private RichTextLabel _adventurers = null!;
	private RichTextLabel _weekly = null!;
	private Button _closeButton = null!;
	private Button _nextButton = null!;

	private const int WeeklyTab = 3;

	private List<MonthlyReport> _history = new();

	public override void _Ready()
	{
		Title = "📅 月報";
		Exclusive = true;
		Unresizable = false;
		Size = new Vector2I(1180, 820);
		Theme = GD.Load<Theme>("res://themes/dungeon_theme.tres");
		CloseRequested += () => CloseWith(null);

		var panel = new PanelContainer { AnchorRight = 1, AnchorBottom = 1 };
		panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(panel);
		var margin = new MarginContainer();
		foreach (var side in new[] { "left", "top", "right", "bottom" })
			margin.AddThemeConstantOverride($"margin_{side}", 14);
		panel.AddChild(margin);
		var root = new VBoxContainer();
		root.AddThemeConstantOverride("separation", 8);
		margin.AddChild(root);

		_historyOption = new OptionButton { Visible = false };
		_historyOption.ItemSelected += index => ShowEntry(_history[_history.Count - 1 - (int)index], null);
		root.AddChild(_historyOption);

		_tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		root.AddChild(_tabs);

		// 概要：見出し・主な出来事 ＋ 来月への注意（画面へ移るボタンつき）
		var overview = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		overview.AddThemeConstantOverride("separation", 8);
		_summary = NewText();
		overview.AddChild(_summary);
		_notesBox = new VBoxContainer();
		_notesBox.AddThemeConstantOverride("separation", 4);
		overview.AddChild(_notesBox);
		AddTab("概要", overview);

		_squads = NewText();
		AddTab("部隊", _squads);
		_adventurers = NewText();
		AddTab("冒険者", _adventurers);
		_weekly = NewText();
		AddTab("週ごとの記録", _weekly);

		var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		buttons.AddThemeConstantOverride("separation", 10);
		_closeButton = new Button { Text = "閉じる", CustomMinimumSize = new Vector2(140, 38) };
		_closeButton.Pressed += () => CloseWith(null);
		_nextButton = new Button { Text = "📅 閉じて次の月へ", CustomMinimumSize = new Vector2(220, 38) };
		_nextButton.Pressed += () => CloseWith(() => NextMonthRequested.Invoke());
		buttons.AddChild(_closeButton);
		buttons.AddChild(_nextButton);
		root.AddChild(buttons);
	}

	private static RichTextLabel NewText() =>
		new() { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

	/// <summary>タブを1つ足す（中身はスクロールできるようにする）。</summary>
	private void AddTab(string title, Control content)
	{
		var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		var pad = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		foreach (var side in new[] { "left", "top", "right", "bottom" })
			pad.AddThemeConstantOverride($"margin_{side}", 8);
		pad.AddChild(content);
		scroll.AddChild(pad);
		_tabs.AddChild(scroll);
		_tabs.SetTabTitle(_tabs.GetTabCount() - 1, title);
	}

	/// <summary>月を進めた直後の月報を開く。weeklyBbcode は週ごとの記録（null ならそのタブを隠す）。</summary>
	public void ShowMonth(MonthlyReport report, string weeklyBbcode, bool allowNext)
	{
		_historyOption.Visible = false;
		ShowEntry(report, weeklyBbcode);
		_nextButton.Visible = allowNext;
		PopupCentered(Size);
		(allowNext ? _nextButton : _closeButton).GrabFocus();
	}

	/// <summary>過去の月報（古い順の一覧）を見返す。最新から表示し、上の選択で切り替える。</summary>
	public void ShowHistory(IReadOnlyList<MonthlyReport> reports)
	{
		_history = reports.ToList();
		_historyOption.Clear();
		for (int i = _history.Count - 1; i >= 0; i--)
			_historyOption.AddItem($"{GameCalendar.FormatMonth(_history[i].LastWeek)}{(_history[i].MonthCompleted ? "" : "（途中経過）")}");
		_historyOption.Visible = _history.Count > 0;
		if (_history.Count > 0)
		{
			_historyOption.Selected = 0;
			ShowEntry(_history[^1], null);
		}
		else
		{
			_summary.Clear();
			_summary.AppendText("[color=gray]まだ月報は無い。「次の月へ」で月を進めると、ここに直近12か月分が残る。[/color]");
			ClearNotes();
			_squads.Clear();
			_adventurers.Clear();
			_weekly.Clear();
			_tabs.SetTabHidden(WeeklyTab, true);
			_tabs.CurrentTab = 0;
		}
		_nextButton.Visible = false;
		PopupCentered(Size);
		_closeButton.GrabFocus();
	}

	private void ShowEntry(MonthlyReport report, string weeklyBbcode)
	{
		Title = report.MonthCompleted ? "📅 月報" : "📅 途中経過";
		_tabs.CurrentTab = 0;
		_summary.Clear();
		_summary.AppendText(SummaryText(report));

		ClearNotes();
		if (report.Notes.Count > 0)
		{
			_notesBox.AddChild(new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, Text = "[b]📌 来月への注意[/b]" });
			foreach (var note in report.Notes)
			{
				var row = new HBoxContainer();
				row.AddThemeConstantOverride("separation", 8);
				row.AddChild(new Label { Text = $"・{note.Text}", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
				if (note.Target != MonthlyNoteTarget.None)
				{
					var target = note.Target;
					var jump = new Button { Text = TargetLabel(target), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
					jump.Pressed += () => CloseWith(() => JumpRequested.Invoke(target));
					row.AddChild(jump);
				}
				_notesBox.AddChild(row);
			}
		}

		_squads.Clear();
		_squads.AppendText(SquadsText(report));
		_adventurers.Clear();
		_adventurers.AppendText(AdventurersText(report));

		bool hasWeekly = !string.IsNullOrEmpty(weeklyBbcode);
		_tabs.SetTabHidden(WeeklyTab, !hasWeekly);
		_weekly.Clear();
		if (hasWeekly)
			_weekly.AppendText(weeklyBbcode);
	}

	private void ClearNotes()
	{
		foreach (var child in _notesBox.GetChildren())
		{
			_notesBox.RemoveChild(child);
			child.QueueFree();
		}
	}

	private void CloseWith(Action after)
	{
		Hide();
		Closed.Invoke();
		after?.Invoke();
	}

	private static string TargetLabel(MonthlyNoteTarget target) => target switch
	{
		MonthlyNoteTarget.Dungeon => "🏰 大迷宮へ",
		MonthlyNoteTarget.Facility => "🏥 施設へ",
		MonthlyNoteTarget.Adventurer => "📋 冒険者へ",
		_ => "",
	};

	private static string ToneColor(MonthlyTone tone) => tone switch
	{
		MonthlyTone.Good => "lime",
		MonthlyTone.Warning => "orange",
		MonthlyTone.Bad => "#ff6b6b",
		_ => "#e5e7eb",
	};

	private static string Colored(MonthlyLine line) => $"[color={ToneColor(line.Tone)}]{line.Text}[/color]";

	private static string HpColor(int percent) => percent >= 70 ? "lime" : percent >= 40 ? "orange" : "#ff6b6b";

	private static string GainsText(Dictionary<string, int> gains) =>
		gains.Values.Sum() > 0
			? string.Join(" ", gains.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}+{kv.Value}"))
			: "";

	/// <summary>概要：見出し・お金と機嫌・研究の手伝い・主な出来事。</summary>
	public static string SummaryText(MonthlyReport report)
	{
		var sb = new StringBuilder();
		string title = report.MonthCompleted
			? $"{GameCalendar.FormatMonth(report.LastWeek)}の月報"
			: $"{GameCalendar.FormatMonth(report.LastWeek)}の途中経過（第{GameCalendar.WeekOfMonth(report.LastWeek)}週まで）";
		sb.AppendLine($"[font_size=20][b]{title}[/b][/font_size]");
		if (report.StopReasons.Count > 0)
			sb.AppendLine($"[color=orange][b]月の途中で止まった：{string.Join("・", report.StopReasons)}。[/b]「次の月へ」で、この月の残りを進める。[/color]");

		int goldDelta = report.GoldAfter - report.GoldBefore;
		int moodDelta = report.MoodAfter - report.MoodBefore;
		sb.AppendLine($"所持金 {report.GoldBefore}G → [b]{report.GoldAfter}G[/b]（[color={(goldDelta >= 0 ? "lime" : "orange")}]{goldDelta:+0;-0}G[/color]）" +
			$"　マスターの機嫌 {report.MoodBefore} → [b]{report.MoodAfter}[/b]（[color={(moodDelta >= 0 ? "lime" : "orange")}]{moodDelta:+0;-0}[/color]）");
		// 研究の手伝い（§0.73）：待機中にアルベールの研究を手伝って貯まった分。次の研究費から割り引く。
		if (report.ResearchCreditGained > 0 || report.ResearchCreditAfter > 0)
			sb.AppendLine($"研究の手伝い [color=lime]+{report.ResearchCreditGained}G[/color]（貯まり {report.ResearchCreditAfter}G。次の研究費の{GuildManager.Core.Balance.TrainingBalance.ResearchCreditMaxDiscountRate * 100:0}%まで割り引く）");

		sb.AppendLine();
		sb.AppendLine("[b]📰 主な出来事[/b]");
		if (report.Highlights.Count == 0)
			sb.AppendLine("[color=gray]　目立った出来事は無かった。部隊・冒険者のタブで、それぞれの1か月を見られる。[/color]");
		foreach (var line in report.Highlights)
			sb.AppendLine($"　・{Colored(line)}");
		// 年末のギルドの順位表（§0.94）：年の最後の月報だけ
		if (report.YearStanding != null)
		{
			sb.AppendLine();
			sb.AppendLine(TournamentPanel.StandingText(report.YearStanding, $"🏅 {report.YearStanding.Year}年目　年末のギルドの順位表"));
		}
		return sb.ToString().TrimEnd('\n');
	}

	/// <summary>部隊：部隊ごとに方針・メンバー・週ごとの行動・月の結果・今のHP。</summary>
	private static string SquadsText(MonthlyReport report)
	{
		if (report.Squads.Count == 0)
			return "[color=gray]メンバーのいる部隊は無い（部隊・冒険者の画面で部隊を組む）。[/color]";
		var sb = new StringBuilder();
		foreach (var squad in report.Squads)
		{
			string order = squad.Order == SquadOrder.None
				? "[color=orange]方針なし[/color]"
				: $"{DungeonPanel.OrderIcon(squad.Order)}{DungeonPanel.OrderName(squad.Order)}" + (squad.FieldName.Length > 0 ? $"（{squad.FieldName}）" : "");
			sb.AppendLine($"[font_size=18][b]{squad.Name}[/b][/font_size]　{order}　[color=gray]HP[/color] [color={HpColor(squad.HpPercent)}]{squad.HpPercent}%[/color]");
			sb.AppendLine($"[color=gray]{string.Join("・", squad.Members)}[/color]");
			foreach (var week in squad.Weeks)
				sb.AppendLine($"　[color=gray]第{week.WeekOfMonth}週[/color]　{Colored(week)}");
			sb.AppendLine($"　[b]結果[/b]：{squad.Result}");
			sb.AppendLine();
		}
		return sb.ToString().TrimEnd('\n');
	}

	/// <summary>冒険者：1人1行の表（名前・所属・過ごし方・成長・今の状態）。</summary>
	private static string AdventurersText(MonthlyReport report)
	{
		if (report.Adventurers.Count == 0)
			return "[color=gray]この月報には冒険者ごとの記録が無い。[/color]";
		var sb = new StringBuilder();
		sb.Append("[table=5]");
		foreach (var head in new[] { "名前", "所属", "過ごし方", "成長", "今の状態" })
			sb.Append($"[cell][color=gray]{head}　[/color][/cell]");
		foreach (var a in report.Adventurers.OrderBy(a => a.Squad.Length == 0).ThenBy(a => a.Squad))
		{
			string gains = GainsText(a.Gains);
			string status = $"HP [color={HpColor(a.HpPercent)}]{a.HpPercent}%[/color]" +
				(a.Status.Count > 0 ? "　" + string.Join("　", a.Status.Select(Colored)) : "");
			sb.Append($"[cell][b]{a.Name}[/b]　[color=gray]{AdventurerPanel.JobLabel(a.Job)}・{a.Age}歳[/color]　[/cell]");
			sb.Append($"[cell]{(a.Squad.Length > 0 ? a.Squad : "[color=gray]—[/color]")}　[/cell]");
			sb.Append($"[cell]{a.Activities}　[/cell]");
			sb.Append($"[cell]{(gains.Length > 0 ? $"[color=lime]{gains}[/color]" : "[color=gray]—[/color]")}　[/cell]");
			sb.Append($"[cell]{status}[/cell]");
		}
		sb.Append("[/table]");
		return sb.ToString();
	}
}
