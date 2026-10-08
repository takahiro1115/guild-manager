#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 大会の結果の小窓（2026年10月・§0.82、→ docs/検討中_大会と育成の栄光.md §3）。大会があった月を進めたあと、月報より先に開く。
/// トーナメント表を1回戦→準決勝→決勝と1回戦ずつ見せる（ギルドの者を強調）。優勝したら大きく祝う（アルベールの一言）。
/// 大会が2つ以上あれば順に見せ、最後にその月に開いた施設の知らせ（3通りの演出）を続けて出す。
/// シーンは使わずコードで組む（MainDashboard が1つ作って使い回す）。
/// </summary>
public partial class TournamentResultPopup : Window
{
	/// <summary>全部見終えて閉じた。</summary>
	public event Action Closed = delegate { };

	private RichTextLabel _text = null!;
	private Button _nextButton = null!;
	private Button _skipButton = null!;

	private readonly List<TournamentEvent> _events = new();
	private readonly List<FacilityUnlockNotice> _notices = new();
	private int _eventIndex;
	private int _shownRounds;
	private bool _showingNotices;

	public override void _Ready()
	{
		Title = "🏆 大会の結果";
		Exclusive = true;
		Size = new Vector2I(980, 720);
		Theme = GD.Load<Theme>("res://themes/dungeon_theme.tres");
		CloseRequested += Finish;

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

		var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		_text = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		scroll.AddChild(_text);
		root.AddChild(scroll);

		var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		buttons.AddThemeConstantOverride("separation", 10);
		_skipButton = new Button { Text = "結果まで飛ばす", CustomMinimumSize = new Vector2(170, 38) };
		_skipButton.Pressed += () => { _shownRounds = int.MaxValue; Render(); };
		_nextButton = new Button { Text = "次の試合へ", CustomMinimumSize = new Vector2(200, 38) };
		_nextButton.Pressed += OnNext;
		buttons.AddChild(_skipButton);
		buttons.AddChild(_nextButton);
		root.AddChild(buttons);
	}

	/// <summary>その月に行った大会（結果つき）と、開いた施設の知らせを見せる。どちらも無ければ何もせず Closed を呼ぶ。</summary>
	public void ShowResults(IEnumerable<TournamentEvent> events, IEnumerable<FacilityUnlockNotice> notices)
	{
		_events.Clear();
		_events.AddRange(events.Where(e => e.Result != null && e.Result.Placings.Count > 0)); // ギルドが出た大会だけ
		_notices.Clear();
		_notices.AddRange(notices);
		_eventIndex = 0;
		_shownRounds = 0;
		_showingNotices = _events.Count == 0;
		if (_events.Count == 0 && _notices.Count == 0)
		{
			Closed.Invoke();
			return;
		}
		Render();
		PopupCentered(Size);
		_nextButton.GrabFocus();
	}

	private int RoundCount(TournamentEvent ev) => ev.Result!.Matches.Select(m => m.Round).DefaultIfEmpty(0).Max();

	private void OnNext()
	{
		if (_showingNotices)
		{
			Finish();
			return;
		}
		var ev = _events[_eventIndex];
		if (_shownRounds < RoundCount(ev))
		{
			_shownRounds++;
		}
		else if (_eventIndex + 1 < _events.Count)
		{
			_eventIndex++;
			_shownRounds = 0;
		}
		else if (_notices.Count > 0)
		{
			_showingNotices = true;
		}
		else
		{
			Finish();
			return;
		}
		Render();
	}

	private void Finish()
	{
		Hide();
		Closed.Invoke();
	}

	private void Render()
	{
		if (_showingNotices)
		{
			_text.Text = NoticesText();
			_nextButton.Text = "閉じる";
			_skipButton.Visible = false;
			return;
		}
		var ev = _events[_eventIndex];
		int rounds = RoundCount(ev);
		_shownRounds = Math.Min(_shownRounds, rounds);
		_text.Text = BracketText(ev, _shownRounds);
		bool done = _shownRounds >= rounds;
		_skipButton.Visible = !done;
		_nextButton.Text = !done ? RoundName(_shownRounds + 1, rounds) + "へ"
			: _eventIndex + 1 < _events.Count ? "次の大会へ"
			: _notices.Count > 0 ? "施設の知らせへ" : "閉じる";
	}

	private static string RoundName(int round, int rounds) =>
		round == rounds ? "決勝" : round == rounds - 1 ? "準決勝" : $"{round}回戦";

	private string BracketText(TournamentEvent ev, int shownRounds)
	{
		var r = ev.Result!;
		int rounds = RoundCount(ev);
		var sb = new StringBuilder();
		sb.Append($"[font_size=22][b]{ev.Name}[/b][/font_size]　{TournamentSystem.GradeLabel(ev.Grade)}・{TournamentSystem.DisciplineLabel(ev.Discipline)}　{ev.Year}年目\n");
		if (shownRounds == 0)
		{
			var first = r.Matches.Where(m => m.Round == 1).ToList();
			sb.Append("\n[b]組み合わせ[/b]");
			foreach (var m in first)
				sb.Append($"\n　{Who(m.NameA, m.OursA)}　対　{Who(m.NameB, m.OursB)}");
			sb.Append("\n\n[color=gray]「1回戦へ」で試合が始まる。[/color]");
			return sb.ToString();
		}
		for (int round = 1; round <= shownRounds; round++)
		{
			sb.Append($"\n[b]{RoundName(round, rounds)}[/b]");
			foreach (var m in r.Matches.Where(m => m.Round == round))
			{
				string winner = m.AWon ? Who(m.NameA, m.OursA) : Who(m.NameB, m.OursB);
				string loser = m.AWon ? Who(m.NameB, m.OursB) : Who(m.NameA, m.OursA);
				bool oursInvolved = m.OursA || m.OursB;
				bool oursWon = m.AWon ? m.OursA : m.OursB;
				string mark = !oursInvolved ? "" : oursWon ? "　[color=lime]○[/color]" : "　[color=salmon]●[/color]";
				sb.Append($"\n　{winner}　が　{loser}　に勝った{mark}");
			}
		}
		if (shownRounds >= rounds)
		{
			sb.Append("\n\n");
			if (r.WinnerIsOurs)
			{
				sb.Append($"[font_size=26][color=gold][b]🏆 優勝：{r.WinnerName}！[/b][/color][/font_size]\n");
				sb.Append($"[color=gold]アルベール「{CheerLine(ev, r.WinnerName)}」[/color]\n");
			}
			else
			{
				sb.Append($"[b]優勝：{r.WinnerName}[/b]\n");
			}
			foreach (var p in r.Placings)
				sb.Append($"\n{p.Name}：{TournamentSystem.PlacingLabel(p.Placing)}" + (p.Prize > 0 ? $"　賞金 {p.Prize}G" : "") + (p.Mood > 0 ? $"　マスターの機嫌 +{p.Mood}" : ""));
			if (r.Placings.Count == 0)
				sb.Append("\n[color=gray]ギルドからの出場は無かった。[/color]");
		}
		return sb.ToString();
	}

	private static string Who(string name, bool ours) => ours ? $"[color=lime][b]{name}[/b][/color]" : name;

	/// <summary>優勝したときのアルベールの一言（格で変える）。</summary>
	private static string CheerLine(TournamentEvent ev, string winner) => ev.Grade switch
	{
		TournamentGrade.G1 => $"見たか！ {winner}が王国の頂に立ったぞ！ このギルドの名は、今日から王都中に響く！",
		TournamentGrade.G2 => $"王都の舞台で勝ち切るとは……{winner}、よくぞここまで育ったものだ",
		TournamentGrade.Special => $"招かれた舞台で応えてみせたな、{winner}。誇らしいぞ",
		_ => $"よし、まずは一つ。{winner}の名を覚えておけ、いずれ大舞台に立つ",
	};

	private string NoticesText()
	{
		var sb = new StringBuilder();
		sb.Append("[font_size=22][b]🏗 施設の知らせ[/b][/font_size]\n");
		foreach (var n in _notices)
		{
			string head = n.Style switch
			{
				"Royal" => "👑 王都からの褒賞",
				"Albert" => "💡 アルベールのひらめき",
				"Isabella" => "✉ イザベラの助言",
				_ => "📋 施設の知らせ",
			};
			sb.Append($"\n[b]{head}[/b]\n{n.Line}\n[color=gold]→ {FacilityUnlockSystem.DescribeUnlock(n)}（施設管理で建てる）[/color]\n");
		}
		return sb.ToString();
	}
}
