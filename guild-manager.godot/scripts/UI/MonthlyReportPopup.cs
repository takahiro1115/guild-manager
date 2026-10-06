using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 月報の小窓（2026年10月・§0.71、→ Core の MonthlyReport）。「次の月へ」で月を進めるたびに開き、どの画面にいても月の結果を見せる。
///  - 見出し（月・所持金と機嫌の増減）、成果（撃破・決戦で退いたボス・扉前の撤退・強制除籍）、冒険者ごとの成長
///  - 来月への注意：項目ごとに、その画面へ移るボタン（大迷宮・施設・冒険者）
///  - 週ごとの記録：最初はたたんでおき、【週ごとの記録を見る】で開く（その月を進めた直後だけ。セーブには残さない）
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
	private RichTextLabel _summary = null!;
	private VBoxContainer _notesBox = null!;
	private Button _weeklyToggle = null!;
	private RichTextLabel _weekly = null!;
	private Button _closeButton = null!;
	private Button _nextButton = null!;

	private List<MonthlyReport> _history = new();

	public override void _Ready()
	{
		Title = "📅 月報";
		Exclusive = true;
		Unresizable = false;
		Size = new Vector2I(980, 800);
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

		var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		root.AddChild(scroll);
		var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		body.AddThemeConstantOverride("separation", 8);
		scroll.AddChild(body);

		_summary = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		body.AddChild(_summary);
		_notesBox = new VBoxContainer();
		_notesBox.AddThemeConstantOverride("separation", 4);
		body.AddChild(_notesBox);
		_weeklyToggle = new Button { Text = "▶ 週ごとの記録を見る", SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
		_weeklyToggle.Pressed += () =>
		{
			_weekly.Visible = !_weekly.Visible;
			_weeklyToggle.Text = _weekly.Visible ? "▼ 週ごとの記録をたたむ" : "▶ 週ごとの記録を見る";
		};
		body.AddChild(_weeklyToggle);
		_weekly = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, Visible = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		body.AddChild(_weekly);

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

	/// <summary>月を進めた直後の月報を開く。weeklyBbcode は週ごとの記録（null ならたたむボタンも出さない）。</summary>
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
			_weeklyToggle.Visible = false;
			_weekly.Visible = false;
		}
		_nextButton.Visible = false;
		PopupCentered(Size);
		_closeButton.GrabFocus();
	}

	private void ShowEntry(MonthlyReport report, string weeklyBbcode)
	{
		Title = report.MonthCompleted ? "📅 月報" : "📅 途中経過";
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

		bool hasWeekly = !string.IsNullOrEmpty(weeklyBbcode);
		_weeklyToggle.Visible = hasWeekly;
		_weekly.Visible = false;
		_weeklyToggle.Text = "▶ 週ごとの記録を見る";
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

	/// <summary>月報の本文（見出し・お金と機嫌・成果・成長）。週報ログの短い行にも見出しの部分を使う。</summary>
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
		sb.AppendLine("[b]⚔ 成果[/b]");
		bool any = false;
		if (report.BossesDefeated.Count > 0) { any = true; sb.AppendLine($"[color=gold]　撃破：{string.Join("、", report.BossesDefeated)}[/color]"); }
		if (report.BossesRepelled.Count > 0) { any = true; sb.AppendLine($"[color=orange]　決戦で退いた：{string.Join("、", report.BossesRepelled)}[/color]"); }
		foreach (var retreat in report.DoorRetreats) { any = true; sb.AppendLine($"[color=orange]　扉前で撤退：{retreat}[/color]"); }
		if (report.ForcedRetired > 0) { any = true; sb.AppendLine($"[color=red][b]　強制除籍：{report.ForcedRetired}名[/b][/color]"); }
		if (!any) sb.AppendLine("[color=gray]　ボスとの戦いは無かった。[/color]");

		sb.AppendLine();
		sb.AppendLine("[b]▲ 成長[/b]");
		if (report.Growth.Count == 0)
			sb.AppendLine("[color=gray]　今月は誰も成長しなかった。[/color]");
		foreach (var g in report.Growth)
			sb.AppendLine($"　{g.Name}：[color=lime]{string.Join("、", g.Gains.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}+{kv.Value}"))}[/color]");
		if (report.SelfTrainingGrowth.Count > 0)
			sb.AppendLine($"[color=gray]　うち自主練：{string.Join("／", report.SelfTrainingGrowth.Select(g => $"{g.Name} {string.Join("、", g.Gains.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}+{kv.Value}"))}"))}[/color]");
		return sb.ToString().TrimEnd('\n');
	}
}
