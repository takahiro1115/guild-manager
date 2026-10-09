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
/// 場面を見せ始めたら StorySystem.MarkSeen で記録する。シーンは使わずコードで組む。
/// 舞台（§0.91）：背景を窓いっぱいに暗めに敷き、そのページで話す人の立ち絵を左右に最大2人、下に文の板。
/// 背景と立ち絵は StorySystem.StageOf が決め、res://assets/story/ にファイルがあるときだけ出す（無ければ背景は無地、立ち絵は出さない）。
/// </summary>
public partial class StoryPopup : Window
{
	/// <summary>全部の場面を見終えて閉じた。</summary>
	public event Action Closed = delegate { };

	/// <summary>【画面へ】が押された（ボタンの文言。MainDashboard が画面を切り替える）。</summary>
	public event Action<string> JumpRequested = delegate { };

	private const string ImageFolder = "res://assets/story/";

	private RichTextLabel _text = null!;
	private ScrollContainer _scroll = null!;
	private Label _backgroundLabel = null!;
	private TextureRect _backgroundImage = null!;
	private TextureRect _leftPortrait = null!;
	private TextureRect _rightPortrait = null!;
	private Button _jumpButton = null!;
	private Button _nextButton = null!;
	private GameState _state = null!;
	private readonly List<StoryShowing> _scenes = new();
	private int _sceneIndex;
	private int _pageIndex;

	public override void _Ready()
	{
		Title = "📖 物語";
		Exclusive = true;
		Size = new Vector2I(1280, 720);
		Theme = GD.Load<Theme>("res://themes/dungeon_theme.tres");
		CloseRequested += SkipPage;

		var stage = new Control();
		stage.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(stage);
		var ground = new ColorRect { Color = new Color(0.08f, 0.07f, 0.11f), MouseFilter = Control.MouseFilterEnum.Ignore };
		ground.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		stage.AddChild(ground);
		_backgroundImage = new TextureRect
		{
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_backgroundImage.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		stage.AddChild(_backgroundImage);
		var dim = new ColorRect { Color = new Color(0, 0, 0, 0.3f), MouseFilter = Control.MouseFilterEnum.Ignore };
		dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		stage.AddChild(dim);

		// 立ち絵：下端をそろえて左右に（腰から上の絵なので、文の板に腰が隠れる）
		_leftPortrait = MakePortrait(0.03f, 0.40f);
		_rightPortrait = MakePortrait(0.60f, 0.97f);
		stage.AddChild(_leftPortrait);
		stage.AddChild(_rightPortrait);

		_backgroundLabel = new Label { Position = new Vector2(16, 10), Modulate = new Color(0.85f, 0.85f, 0.9f) };
		_backgroundLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
		stage.AddChild(_backgroundLabel);

		var plate = new PanelContainer { AnchorLeft = 0.03f, AnchorRight = 0.97f, AnchorTop = 0.6f, AnchorBottom = 0.97f };
		var plateStyle = new StyleBoxFlat { BgColor = new Color(0.05f, 0.04f, 0.08f, 0.93f), BorderColor = new Color(0.55f, 0.42f, 0.2f) };
		plateStyle.SetBorderWidthAll(2);
		plateStyle.SetCornerRadiusAll(8);
		plateStyle.SetContentMarginAll(16);
		plate.AddThemeStyleboxOverride("panel", plateStyle);
		stage.AddChild(plate);
		var root = new VBoxContainer();
		root.AddThemeConstantOverride("separation", 8);
		plate.AddChild(root);

		_scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		_text = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_scroll.AddChild(_text);
		root.AddChild(_scroll);

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

	private static TextureRect MakePortrait(float left, float right) => new()
	{
		AnchorLeft = left,
		AnchorRight = right,
		AnchorTop = 0.04f,
		AnchorBottom = 1f,
		ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
		StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
		MouseFilter = Control.MouseFilterEnum.Ignore,
	};

	/// <summary>assets/story/ の画像（ファイルがまだ無ければ null）。</summary>
	private static Texture2D? LoadImage(string? file) =>
		file != null && ResourceLoader.Exists(ImageFolder + file) ? GD.Load<Texture2D>(ImageFolder + file) : null;

	private static void ShowPortrait(TextureRect rect, StoryPortrait? portrait)
	{
		rect.Texture = LoadImage(portrait?.File);
		rect.Visible = rect.Texture != null;
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
		var stage = StorySystem.StageOf(Current, _pageIndex);
		var backgroundImage = LoadImage(stage.BackgroundFile);
		_backgroundImage.Texture = backgroundImage;
		// 背景の絵がまだ無い場面は、場所の名前を出す
		_backgroundLabel.Text = stage.Background.Length > 0 && backgroundImage == null ? $"― {stage.Background} ―" : "";
		ShowPortrait(_leftPortrait, stage.Left);
		ShowPortrait(_rightPortrait, stage.Right);
		_scroll.ScrollVertical = 0;
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
