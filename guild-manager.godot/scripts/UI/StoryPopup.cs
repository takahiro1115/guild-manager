#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 物語の会話の小窓（2026年10月・§0.86、→ Core の StorySystem・StoryBalance）。出す場面を順に、台詞・地の文を1行ずつ見せる。
/// 最後のコマのあとに、行き先の案内とギルドの手引きの行を1コマ添える。
/// 場面を見せ始めたら StorySystem.MarkSeen で記録する（物語の記録から見返すときは記録せず、案内と手引きも出さない、§0.92）。シーンは使わずコードで組む。
/// 舞台（§0.107、物語帳の舞台と同じ作り）：背景を窓いっぱいに、話す人の立ち絵を1枚だけ左に、下に文の枠と名札。
/// 背景と立ち絵は StorySystem.StepsOf が決め、res://assets/story/ にファイルがあるときだけ出す（無ければ背景は無地、立ち絵は出さない）。
/// </summary>
public partial class StoryPopup : Window
{
	/// <summary>全部の場面を見終えて閉じた。</summary>
	public event Action Closed = delegate { };

	/// <summary>「報告をとばす」で閉じたか（部隊長の報告を区切って見せるとき、残りの区切りも飛ばす。§0.151）。開くたびに false に戻る。</summary>
	public bool SkippedAll { get; private set; }

	/// <summary>【画面へ】の行き先が今見ている画面か（そうなら案内を出さない。MainDashboard が決める、§0.108）。</summary>
	public Func<string, bool> IsCurrentScreen { get; init; } = _ => false;

	private const string ImageFolder = "res://assets/story/";

	private RichTextLabel _text = null!;
	private ScrollContainer _scroll = null!;
	private Label _backgroundLabel = null!;
	private TextureRect _backgroundImage = null!;
	private TextureRect _portrait = null!;
	private PanelContainer _namePlate = null!;
	private Label _nameLabel = null!;
	private Button _nextButton = null!;
	private GameState _state = null!;
	private readonly List<StoryShowing> _scenes = new();
	private IReadOnlyList<StoryStep> _steps = Array.Empty<StoryStep>();
	private string _guide = "";
	private int _sceneIndex;
	private int _stepIndex;
	private bool _replay;
	/// <summary>部隊長の報告（§0.116）を見せているときのコマ（物語の場面の代わりに、そのまま並べる）。物語の場面なら null。</summary>
	private IReadOnlyList<StoryStep>? _presetSteps;
	/// <summary>用意したコマを見せるとき（部隊長の報告・支配者戦）の、最後のボタンの文言。</summary>
	private string _presetLastText = "月報を見る ▶";
	private Button _skipAllButton = null!;

	public override void _Ready()
	{
		Title = "📖 物語";
		Exclusive = true;
		Size = new Vector2I(1280, 720);
		Theme = GD.Load<Theme>("res://themes/dungeon_theme.tres");
		CloseRequested += SkipStep;

		var stage = new Control();
		stage.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		stage.GuiInput += e =>
		{
			// 舞台のどこを押しても次へ（物語帳の「次へ」と同じ）
			if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
				OnNext();
		};
		AddChild(stage);
		var ground = new ColorRect { Color = new Color(0.06f, 0.05f, 0.08f), MouseFilter = Control.MouseFilterEnum.Ignore };
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

		// 立ち絵：話す人を1枚だけ左に、下端をそろえて（腰から上の絵なので、文の枠に腰が隠れる）
		_portrait = new TextureRect
		{
			AnchorLeft = 0.06f,
			AnchorRight = 0.45f,
			AnchorTop = 0.08f,
			AnchorBottom = 1f,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		stage.AddChild(_portrait);

		_backgroundLabel = new Label { Position = new Vector2(16, 10), Modulate = new Color(0.85f, 0.85f, 0.9f), MouseFilter = Control.MouseFilterEnum.Ignore };
		_backgroundLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
		stage.AddChild(_backgroundLabel);

		// 文の枠：下に横いっぱい、半透明の濃い紫。名札は枠の左上に重ねる
		var boxArea = new Control { AnchorLeft = 0.03f, AnchorRight = 0.97f, AnchorTop = 0.68f, AnchorBottom = 0.96f, MouseFilter = Control.MouseFilterEnum.Ignore };
		stage.AddChild(boxArea);
		var box = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		var boxStyle = new StyleBoxFlat { BgColor = new Color(22 / 255f, 18 / 255f, 32 / 255f, 0.86f) };
		boxStyle.SetCornerRadiusAll(8);
		boxStyle.ContentMarginLeft = 22;
		boxStyle.ContentMarginRight = 22;
		boxStyle.ContentMarginTop = 22;
		boxStyle.ContentMarginBottom = 12;
		box.AddThemeStyleboxOverride("panel", boxStyle);
		boxArea.AddChild(box);
		var root = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		root.AddThemeConstantOverride("separation", 4);
		box.AddChild(root);

		_scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = Control.MouseFilterEnum.Pass };
		_text = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Pass };
		_text.AddThemeColorOverride("default_color", new Color(0.953f, 0.933f, 0.984f));
		_text.AddThemeFontSizeOverride("normal_font_size", 20);
		_text.AddThemeFontSizeOverride("bold_font_size", 20);
		_scroll.AddChild(_text);
		root.AddChild(_scroll);

		var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End, MouseFilter = Control.MouseFilterEnum.Ignore };
		// 部隊長の報告（§0.116）だけ：残りをまとめてとばして月報へ
		_skipAllButton = new Button { Text = "報告をとばす ⏭", CustomMinimumSize = new Vector2(160, 34), Flat = true, Visible = false };
		_skipAllButton.AddThemeFontSizeOverride("font_size", 18);
		_skipAllButton.Pressed += () =>
		{
			SkippedAll = true;
			Hide();
			Closed.Invoke();
		};
		buttons.AddChild(_skipAllButton);
		_nextButton = new Button { CustomMinimumSize = new Vector2(160, 34), Flat = true };
		_nextButton.AddThemeFontSizeOverride("font_size", 18);
		_nextButton.Pressed += OnNext;
		buttons.AddChild(_nextButton);
		root.AddChild(buttons);

		_namePlate = new PanelContainer { Position = new Vector2(16, -16), MouseFilter = Control.MouseFilterEnum.Ignore };
		var plateStyle = new StyleBoxFlat { BgColor = new Color(0x7d / 255f, 0x5f / 255f, 0xb5 / 255f) };
		plateStyle.SetCornerRadiusAll(16);
		plateStyle.ContentMarginLeft = 14;
		plateStyle.ContentMarginRight = 14;
		plateStyle.ContentMarginTop = 2;
		plateStyle.ContentMarginBottom = 3;
		_namePlate.AddThemeStyleboxOverride("panel", plateStyle);
		_nameLabel = new Label();
		_nameLabel.AddThemeColorOverride("font_color", Colors.White);
		_nameLabel.AddThemeFontSizeOverride("font_size", 15);
		_namePlate.AddChild(_nameLabel);
		boxArea.AddChild(_namePlate);
	}

	/// <summary>assets/story/ の画像（ファイルがまだ無ければ null）。</summary>
	private static Texture2D? LoadImage(string? file)
	{
		if (file == null) return null;
		string path = file.StartsWith("res://") ? file : ImageFolder + file; // 冒険者の顔（assets/portraits）は res:// から渡す（§0.116）
		return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
	}

	/// <summary>
	/// 部隊長の報告（§0.116）を見せる：コマ（話し手の名札・立ち絵・背景つき）を順に。見たことは記録しない。「報告をとばす」で残りを飛ばせる。
	/// コマが無ければ何もせず Closed を呼ぶ。
	/// </summary>
	/// <summary>月報の前の報告（部隊長の報告・出来事の報告、§0.116・§0.141）。見出しは呼ぶ側が決める（出来事の報告があれば「今月の報告」）。</summary>
	public void ShowReport(GameState state, IReadOnlyList<StoryStep> steps, string title = "📣 部隊長の報告", string lastText = "月報を見る ▶")
	{
		Title = title;
		_skipAllButton.Text = "報告をとばす ⏭";
		_skipAllButton.Visible = true;
		_presetSteps = steps;
		_presetLastText = lastText;
		Open(state, new[] { new StoryShowing { SceneId = "squad_report" } }, replay: true);
	}

	/// <summary>
	/// 用意したコマ（支配者戦の段など、§0.127）を見せる。見たことは記録しない。skipText があれば、残りをとばすボタンを出す。
	/// コマが無ければ何もせず Closed を呼ぶ。
	/// </summary>
	public void ShowSteps(GameState state, string title, IReadOnlyList<StoryStep> steps, string lastText, string? skipText = null)
	{
		Title = title;
		_skipAllButton.Text = skipText ?? "";
		_skipAllButton.Visible = skipText != null;
		_presetSteps = steps;
		_presetLastText = lastText;
		Open(state, new[] { new StoryShowing { SceneId = "preset_steps" } }, replay: true);
	}

	/// <summary>場面を順に見せる。1つも無ければ何もせず Closed を呼ぶ。replay は物語の記録からの見返し（見たことを記録しない）。</summary>
	public void ShowScenes(GameState state, IEnumerable<StoryShowing> scenes, bool replay = false)
	{
		Title = "📖 物語";
		_skipAllButton.Visible = false;
		_presetSteps = null;
		Open(state, scenes, replay);
	}

	private void Open(GameState state, IEnumerable<StoryShowing> scenes, bool replay)
	{
		SkippedAll = false;
		_state = state;
		_replay = replay;
		_scenes.Clear();
		_scenes.AddRange(scenes);
		_sceneIndex = -1;
		if (!OpenNextScene())
		{
			Closed.Invoke();
			return;
		}
		Render();
		PopupCentered(Size);
		_nextButton.GrabFocus();
	}

	/// <summary>添える案内と手引きのコマがあるか。</summary>
	private bool HasGuide => _guide.Length > 0;
	private int StepCount => _steps.Count + (HasGuide ? 1 : 0);
	private bool LastStep => _stepIndex + 1 >= StepCount;

	/// <summary>次の場面を開く（見せるコマの無い場面は記録だけして飛ばす）。もう場面が無ければ false。</summary>
	private bool OpenNextScene()
	{
		while (++_sceneIndex < _scenes.Count)
		{
			var scene = _scenes[_sceneIndex];
			if (!_replay) StorySystem.MarkSeen(_state, scene.SceneId);
			_steps = _presetSteps ?? StorySystem.StepsOf(scene);
			_guide = _replay ? "" : GuideText(scene);
			_stepIndex = 0;
			if (StepCount > 0) return true;
		}
		return false;
	}

	private void OnNext()
	{
		if (!Visible) return;
		if (!LastStep)
		{
			_stepIndex++;
			Render();
			return;
		}
		if (OpenNextScene())
		{
			Render();
			return;
		}
		Hide();
		Closed.Invoke();
	}

	/// <summary>窓の ✖：次のコマへ（読み飛ばしは「次へ」を押すのと同じ。場面は最後まで見たことになる）。</summary>
	private void SkipStep() => OnNext();

	private void Render()
	{
		// 案内のコマは、最後の台詞の背景のまま
		var step = _stepIndex < _steps.Count ? _steps[_stepIndex] : null;
		var stageStep = step ?? _steps.LastOrDefault();
		string speaker = step?.Line.Kind == StoryLineKind.Speech ? step.Line.Speaker : "";

		_text.Text = step == null ? _guide
			: step.Line.Kind == StoryLineKind.Speech ? $"「{Escape(step.Line.Text)}」"
			: $"[color=#d9d2ea]{Escape(step.Line.Text)}[/color]";
		_nameLabel.Text = speaker;
		_namePlate.Visible = speaker.Length > 0;

		var backgroundImage = LoadImage(stageStep?.BackgroundFile);
		_backgroundImage.Texture = backgroundImage;
		// 背景の絵がまだ無い場面は、場所の名前を出す
		string background = stageStep?.Background ?? "";
		_backgroundLabel.Text = background.Length > 0 && backgroundImage == null ? $"― {background} ―" : "";
		_portrait.Texture = LoadImage(step?.PortraitFile);
		_portrait.Visible = _portrait.Texture != null;

		_scroll.ScrollVertical = 0;
		bool last = LastStep && _sceneIndex + 1 >= _scenes.Count;
		_nextButton.Text = LastStep ? (last ? (_presetSteps != null ? _presetLastText : "閉じる") : "次の場面へ ▶") : "次へ ▼";
		_skipAllButton.Visible = _presetSteps != null && !last && _skipAllButton.Text.Length > 0;
	}

	/// <summary>場面の最後に添える行き先の案内とギルドの手引き（無ければ空）。行き先が今の画面なら案内は省く。</summary>
	private string GuideText(StoryShowing scene)
	{
		var lines = scene.Pages.SelectMany(p => p).ToList();
		var sb = new StringBuilder();
		// 【画面へ】の行は、押すと画面が飛ぶボタンだったが、読み終えた途端に画面が切り替わって迷子になるので、行き先の案内の文にした（§0.97、提案書 UI-04）
		var jump = lines.FirstOrDefault(l => l.Kind == StoryLineKind.Jump);
		if (jump != null && !IsCurrentScreen(jump.Text))
		{
			var nav = Escape(NavName(jump.Text));
			var action = JumpAction(jump.Text);
			sb.Append(action != null
				? $"[color=#7dd3fc]📍 次は上の「{nav}」の画面で、{Escape(action)}。[/color]\n"
				: $"[color=#7dd3fc]📍 次は上の「{nav}」の画面で：{Escape(jump.Text)}[/color]\n");
		}
		var guides = lines.Where(l => l.Kind is StoryLineKind.Guide or StoryLineKind.GuideNote).ToList();
		if (guides.Count > 0)
		{
			if (sb.Length > 0) sb.Append('\n');
			sb.Append("[color=khaki]📝 ギルドの手引き[/color]\n");
			foreach (var g in guides)
				sb.Append(g.Kind == StoryLineKind.Guide ? $"[color=khaki]□ {Escape(g.Text)}[/color]\n" : $"[color=#b8b8c8]{Escape(g.Text)}[/color]\n");
		}
		return sb.ToString().TrimEnd('\n');
	}

	private static string Escape(string text) => text.Replace("[", "[lb]");

	/// <summary>行き先の案内の文（出来事の報告の最後のコマ、§0.141）。【画面へ】の文と同じ言い方。</summary>
	public static string GuideLine(string label)
	{
		var action = JumpAction(label);
		return action != null ? $"📍 次は上の「{NavName(label)}」の画面で、{action}。" : $"📍 次は上の「{NavName(label)}」の画面で：{label}";
	}

	/// <summary>行き先の案内に添える「そこで何をするか」の文（画面の名前だけの行き先は、読める文に言い換える。分からなければ null）。</summary>
	private static string? JumpAction(string label)
	{
		if (label == "部隊・冒険者") return "冒険者を選んで部隊を編制する";
		if (label == "部隊の方針") return "部隊に方針を付ける";
		int open = label.IndexOf('（');
		return open >= 0 && label.EndsWith('）') ? label[(open + 1)..^1] : null;
	}

	/// <summary>【画面へ】の行き先に対応するナビのボタンの名前（行き先の文から決める）。</summary>
	private static string NavName(string label) => label switch
	{
		_ when label.Contains("大会") => "🏆 大会",
		_ when label.Contains("施設") => "🏥 施設管理",
		_ when label.Contains("研究室") => "🔬 研究室",
		_ when label.Contains("倉庫") => "📦 倉庫・遺物",
		_ when label.Contains("大迷宮") || label.Contains("方針") => "🏰 大迷宮",
		_ when label.Contains("部隊") || label.Contains("冒険者") => "📋 部隊・冒険者",
		_ when label.Contains("資金") => "💰 資金", // §0.140：イザベラの帳簿・スポンサーの話
		_ => label,
	};
}
