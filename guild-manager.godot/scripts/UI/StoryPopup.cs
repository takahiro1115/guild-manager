#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 物語の会話の小窓（2026年10月・§0.86、→ Core の StorySystem・StoryBalance）。出す場面を順に、▼で区切ったページごとに見せる。
/// 最後のページには【画面へ】のボタンと、ギルドの手引きの行（③-2でチェックリストにする。今は小窓の最後に出す）を添える。
/// 場面を見せ始めたら StorySystem.MarkSeen で記録する。立ち絵はまだ無いので名前だけ出す。シーンは使わずコードで組む。
/// </summary>
public partial class StoryPopup : Window
{
	/// <summary>全部の場面を見終えて閉じた。</summary>
	public event Action Closed = delegate { };

	/// <summary>【画面へ】が押された（ボタンの文言。MainDashboard が画面を切り替える）。</summary>
	public event Action<string> JumpRequested = delegate { };

	private RichTextLabel _text = null!;
	private Label _backgroundLabel = null!;
	private Button _jumpButton = null!;
	private Button _nextButton = null!;
	private GameState _state = null!;
	private readonly List<StoryShowing> _scenes = new();
	private int _sceneIndex;
	private int _pageIndex;
	private string _background = "";

	public override void _Ready()
	{
		Title = "📖 物語";
		Exclusive = true;
		Size = new Vector2I(1000, 560);
		Theme = GD.Load<Theme>("res://themes/dungeon_theme.tres");
		CloseRequested += SkipPage;

		var panel = new PanelContainer();
		panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(panel);
		var margin = new MarginContainer();
		foreach (var side in new[] { "left", "top", "right", "bottom" })
			margin.AddThemeConstantOverride($"margin_{side}", 18);
		panel.AddChild(margin);
		var root = new VBoxContainer();
		root.AddThemeConstantOverride("separation", 10);
		margin.AddChild(root);

		_backgroundLabel = new Label { Modulate = new Color(0.7f, 0.7f, 0.7f) };
		root.AddChild(_backgroundLabel);
		var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		_text = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		scroll.AddChild(_text);
		root.AddChild(scroll);

		var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		buttons.AddThemeConstantOverride("separation", 10);
		_jumpButton = new Button { CustomMinimumSize = new Vector2(240, 38) };
		_jumpButton.Pressed += () => JumpRequested.Invoke(_jumpButton.Text.Replace("▶ ", ""));
		_nextButton = new Button { CustomMinimumSize = new Vector2(200, 38) };
		_nextButton.Pressed += OnNext;
		buttons.AddChild(_jumpButton);
		buttons.AddChild(_nextButton);
		root.AddChild(buttons);
	}

	/// <summary>場面を順に見せる。1つも無ければ何もせず Closed を呼ぶ。</summary>
	public void ShowScenes(GameState state, IEnumerable<StoryShowing> scenes)
	{
		_state = state;
		_scenes.Clear();
		_scenes.AddRange(scenes);
		_sceneIndex = 0;
		_pageIndex = 0;
		if (_scenes.Count == 0)
		{
			Closed.Invoke();
			return;
		}
		StorySystem.MarkSeen(_state, _scenes[0].SceneId);
		Render();
		PopupCentered(Size);
		_nextButton.GrabFocus();
	}

	private StoryShowing Current => _scenes[_sceneIndex];
	private bool LastPage => _pageIndex + 1 >= Current.Pages.Count;

	private void OnNext()
	{
		if (!LastPage)
		{
			_pageIndex++;
			Render();
			return;
		}
		if (_sceneIndex + 1 < _scenes.Count)
		{
			_sceneIndex++;
			_pageIndex = 0;
			StorySystem.MarkSeen(_state, Current.SceneId);
			Render();
			return;
		}
		Hide();
		Closed.Invoke();
	}

	/// <summary>窓の ✖：次のページへ（読み飛ばしは「次へ」を押すのと同じ。場面は最後まで見たことになる）。</summary>
	private void SkipPage() => OnNext();

	private void Render()
	{
		var page = Current.Pages[_pageIndex];
		var sb = new StringBuilder();
		foreach (var line in page)
		{
			switch (line.Kind)
			{
				case StoryLineKind.Background:
					_background = line.Text;
					break;
				case StoryLineKind.Narration:
					sb.Append($"[color=#b8b8c8]{Escape(line.Text)}[/color]\n\n");
					break;
				case StoryLineKind.Speech:
					string expression = line.Expression.Length > 0 ? $"[color=gray]（{Escape(line.Expression)}）[/color]" : "";
					sb.Append($"[color={SpeakerColor(line.Speaker)}]{Escape(line.Speaker)}[/color]{expression}「{Escape(line.Text)}」\n");
					break;
			}
		}

		var jump = LastPage ? Current.Pages.SelectMany(p => p).FirstOrDefault(l => l.Kind == StoryLineKind.Jump) : null;
		if (LastPage)
		{
			var guides = Current.Pages.SelectMany(p => p).Where(l => l.Kind is StoryLineKind.Guide or StoryLineKind.GuideNote).ToList();
			if (guides.Count > 0)
			{
				sb.Append("\n[color=khaki]📝 ギルドの手引き[/color]\n");
				foreach (var g in guides)
					sb.Append(g.Kind == StoryLineKind.Guide ? $"[color=khaki]□ {Escape(g.Text)}[/color]\n" : $"[color=gray]{Escape(g.Text)}[/color]\n");
			}
		}

		_text.Text = sb.ToString();
		_backgroundLabel.Text = _background.Length > 0 ? $"― {_background} ―" : "";
		_jumpButton.Visible = jump != null;
		_jumpButton.Text = jump != null ? $"▶ {jump.Text}" : "";
		bool last = LastPage && _sceneIndex + 1 >= _scenes.Count;
		_nextButton.Text = LastPage ? (last ? "閉じる" : "次の場面へ ▶") : "次へ ▼";
	}

	private static string Escape(string text) => text.Replace("[", "[lb]");

	private static string SpeakerColor(string speaker) => speaker switch
	{
		"アルベール" => "#e0707a",
		"ルミナ" => "#8fd18f",
		"イザベラ" => "#f0d060",
		"ユーフェミア" => "#a0d8c8",
		"マルグリット" or "セシリア" or "ブリジット" or "ニナ" => "#e8c8f0",
		_ => "#d8d8e8",
	};
}
