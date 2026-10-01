using Godot;
using System;

/// <summary>
/// 中央ペイン「システム」タブ（→ 03 §9・§12）。ナビゲーション行の最右端「⚙️ システム」から開く。
///
/// 旧ヘッダー右肩のセーブボタンをここへ移設し、将来のロード・環境設定（サウンド・画面）の
/// 受け皿となるUI骨格を置く（ロード・環境設定は現状 Disabled のプレースホルダー）。
///
/// セーブの実行（SaveLoadService）と週報ログへの通知は MainDashboard の責務のため、
/// SaveRequested イベントで依頼する（他パネルの LogRequested・StateChanged と同じ流儀）。
/// </summary>
public partial class SystemPanel : ScrollContainer
{
	private Button _saveDataBtn = null!;
	private Label _saveFeedbackLabel = null!;
	private Button _windowedBtn = null!;
	private Button _maximizedBtn = null!;
	private Button _fullscreenBtn = null!;

	/// <summary>「進行状況を保存」の押下を通知する。MainDashboard がセーブを実行し、成否を返す。</summary>
	public event Func<bool> SaveRequested = () => false;

	/// <summary>デバッグ用「所持金 +10,000 G」の押下を通知する（MainDashboard が所持金を増やして表示を更新する）。</summary>
	public event Action DebugAddGoldRequested = () => { };

	/// <summary>「エンディングを見る」の押下を通知する（クリア後だけ表示、→ 03 §0.59。MainDashboard がエンディングを開く）。</summary>
	public event Action EndingRequested = () => { };

	private Button _endingBtn = null!;

	public override void _Ready()
	{
		_saveDataBtn = GetNode<Button>("%SaveDataBtn");
		_saveFeedbackLabel = GetNode<Label>("%SaveFeedbackLabel");
		_saveDataBtn.Pressed += OnSaveDataPressed;
		// エンディングを見直すボタン（クリア後だけ表示）。シーンを変えずに、保存ボタンと同じ並びへコードで足す。
		_endingBtn = new Button { Text = "📜 エンディングを見る", Visible = false };
		_endingBtn.Pressed += () => EndingRequested();
		_saveDataBtn.GetParent().AddChild(_endingBtn);
		// デバッグボタンはエディタ実行・デバッグ版の書き出しでだけ出す（リリース版の書き出しでは隠す）
		var debugAddGoldBtn = GetNode<Button>("%DebugAddGoldBtn");
		debugAddGoldBtn.Visible = OS.IsDebugBuild();
		debugAddGoldBtn.Pressed += () => DebugAddGoldRequested();
		_windowedBtn = GetNode<Button>("%WindowedBtn");
		_maximizedBtn = GetNode<Button>("%MaximizedBtn");
		_fullscreenBtn = GetNode<Button>("%FullscreenBtn");
		var group = new ButtonGroup();
		foreach (var b in new[] { _windowedBtn, _maximizedBtn, _fullscreenBtn })
			b.ButtonGroup = group;
		_windowedBtn.Pressed += () => SetDisplayMode(Window.ModeEnum.Windowed);
		_maximizedBtn.Pressed += () => SetDisplayMode(Window.ModeEnum.Maximized);
		_fullscreenBtn.Pressed += () => SetDisplayMode(Window.ModeEnum.Fullscreen);
		UpdateDisplayModeButtons();
		VisibilityChanged += UpdateDisplayModeButtons;
	}

	/// <summary>窓表示の大きさ：基準解像度（1920×1080）を上限に、タイトルバー・タスクバー分を除いた作業領域へ収める。</summary>
	private static readonly Vector2I WindowedMargin = new(16, 56);

	/// <summary>窓表示にして、画面の作業領域に収まる大きさ・中央の位置へ整える（起動時と、最大化からの復帰時に呼ぶ）。</summary>
	public static void FitWindowedToScreen(Window window)
	{
		window.Mode = Window.ModeEnum.Windowed;
		var usable = DisplayServer.ScreenGetUsableRect(window.CurrentScreen);
		var size = new Vector2I(
			Math.Min(1920, usable.Size.X - WindowedMargin.X),
			Math.Min(1080, usable.Size.Y - WindowedMargin.Y));
		window.Size = size;
		window.Position = usable.Position + new Vector2I(
			(usable.Size.X - size.X) / 2,
			WindowedMargin.Y - 16 + (usable.Size.Y - WindowedMargin.Y - size.Y) / 2);
	}

	private void SetDisplayMode(Window.ModeEnum mode)
	{
		var window = GetWindow();
		if (mode == Window.ModeEnum.Windowed)
			FitWindowedToScreen(window);
		else
			window.Mode = mode;
		UpdateDisplayModeButtons();
	}

	private void UpdateDisplayModeButtons()
	{
		var mode = GetWindow().Mode;
		_windowedBtn.SetPressedNoSignal(mode == Window.ModeEnum.Windowed);
		_maximizedBtn.SetPressedNoSignal(mode == Window.ModeEnum.Maximized);
		_fullscreenBtn.SetPressedNoSignal(mode is Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen);
	}

	/// <summary>クリア済みなら「エンディングを見る」を出す（MainDashboard.RefreshAll から呼ぶ）。</summary>
	public void SetEndingAvailable(bool available) => _endingBtn.Visible = available;

	private void OnSaveDataPressed()
	{
		bool saved = SaveRequested();
		string time = Time.GetTimeStringFromSystem();
		_saveFeedbackLabel.Text = saved
			? $"✔ 進行状況を保存しました（{time}）"
			: $"✕ 保存に失敗しました（{time}）";
		_saveFeedbackLabel.AddThemeColorOverride("font_color",
			saved ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.45f, 0.45f));
	}
}
