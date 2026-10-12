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

	/// <summary>「ゲームを終了」の押下を通知する（MainDashboard が確認を出して終了する。§0.80でナビの「閉じる」から移した）。</summary>
	public event Action QuitRequested = () => { };

	private Button _endingBtn = null!;
	private CheckButton _autoBonusCheck = null!;
	/// <summary>ギルドの方針（§0.105）：訓練の強さ・大会の賞金の分け方。</summary>
	private OptionButton _trainingPolicyOption = null!;
	private OptionButton _prizePolicyOption = null!;
	private GuildManager.Core.Models.GameState _state = null!;

	public override void _Ready()
	{
		_saveDataBtn = GetNode<Button>("%SaveDataBtn");
		_saveFeedbackLabel = GetNode<Label>("%SaveFeedbackLabel");
		_saveDataBtn.Pressed += OnSaveDataPressed;
		// エンディングを見直すボタン（クリア後だけ表示）。シーンを変えずに、保存ボタンと同じ並びへコードで足す。
		_endingBtn = new Button { Text = "📜 エンディングを見る", Visible = false };
		_endingBtn.Pressed += () => EndingRequested();
		_saveDataBtn.GetParent().AddChild(_endingBtn);
		// ゲームの終了（§0.80：ナビのタブと並んでいて押し間違えやすかったので、ここへ移した）。保存ボタンと同じ並びの最後に置く。
		var quitBtn = new Button { Text = "✖ ゲームを終了" };
		quitBtn.Pressed += () => QuitRequested();
		_saveDataBtn.GetParent().AddChild(quitBtn);
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
		BuildAutoBonusToggle();
		BuildPolicyOptions();
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

	/// <summary>
	/// 交渉の自動（§0.99）：環境設定の欄の画面表示の下に、「契約の不満にボーナスで自動で応える」を置く（シーンは変えずにコードで組む）。
	/// 選んだ値はセーブに残る（GameState.AutoNegotiationBonus）。払うかどうかの判定は Core（ChoreSystem.ProcessAutoBonus）。
	/// </summary>
	private void BuildAutoBonusToggle()
	{
		var displayRow = _windowedBtn.GetParent<Control>();
		var box = displayRow.GetParent();
		var label = new Label { Text = "🤝 契約交渉" };
		_autoBonusCheck = new CheckButton
		{
			Text = "契約の不満が出たら、ボーナスで自動で応える",
			TooltipText = $"満足度が下がって契約の不満が出た子へ、週給×{GuildManager.Core.Balance.SatisfactionBalance.BonusWeeksEquivalent}のボーナスを自動で払う。" +
				$"所持金に週給の{GuildManager.Core.Balance.EconomyBalance.ChoreReserveWageWeeks}週分を残せないときは払わず、今までどおり月を止めて知らせる。払ったことは月報の主な出来事に出る",
		};
		_autoBonusCheck.Toggled += on =>
		{
			if (_state != null)
				_state.AutoNegotiationBonus = on;
		};
		box.AddChild(label);
		box.MoveChild(label, displayRow.GetIndex() + 1);
		box.AddChild(_autoBonusCheck);
		box.MoveChild(_autoBonusCheck, label.GetIndex() + 1);
	}

	/// <summary>
	/// ギルドの方針（§0.105）：契約交渉の下に「訓練の強さ」「大会の賞金の分け方」を置く（シーンは変えずにコードで組む）。
	/// 選んだ値はセーブに残る（GameState.TrainingPolicy・PrizePolicy）。効き目は Core（GuildPolicySystem）。
	/// </summary>
	private void BuildPolicyOptions()
	{
		var box = _autoBonusCheck.GetParent();
		int index = _autoBonusCheck.GetIndex() + 1;
		var label = new Label { Text = "📜 ギルドの方針" };
		box.AddChild(label);
		box.MoveChild(label, index++);

		_trainingPolicyOption = new OptionButton();
		var policies = new[] { GuildManager.Core.Models.TrainingPolicy.Normal, GuildManager.Core.Models.TrainingPolicy.Hard, GuildManager.Core.Models.TrainingPolicy.Gentle };
		foreach (var p in policies)
			_trainingPolicyOption.AddItem($"訓練：{GuildManager.Core.Systems.GuildPolicySystem.TrainingPolicyLabel(p)}", (int)p);
		_trainingPolicyOption.TooltipText =
			$"厳しく：訓練の成長×{GuildManager.Core.Balance.PolicyBalance.TrainingHardGrowthMultiplier}、訓練生が毎週{GuildManager.Core.Balance.PolicyBalance.TrainingHardInjuryChance:P0}で軽傷・満足度{GuildManager.Core.Balance.PolicyBalance.TrainingHardSatisfaction}（訓練が好きな子は下がらない）\n" +
			$"穏やかに：訓練の成長×{GuildManager.Core.Balance.PolicyBalance.TrainingGentleGrowthMultiplier}、訓練生の満足度が毎週+{GuildManager.Core.Balance.PolicyBalance.TrainingGentleSatisfaction}";
		_trainingPolicyOption.ItemSelected += i =>
		{
			if (_state != null) _state.TrainingPolicy = (GuildManager.Core.Models.TrainingPolicy)_trainingPolicyOption.GetItemId((int)i);
		};
		box.AddChild(_trainingPolicyOption);
		box.MoveChild(_trainingPolicyOption, index++);

		_prizePolicyOption = new OptionButton();
		foreach (var p in new[] { GuildManager.Core.Models.PrizePolicy.Guild, GuildManager.Core.Models.PrizePolicy.Share })
			_prizePolicyOption.AddItem($"大会の賞金：{GuildManager.Core.Systems.GuildPolicySystem.PrizePolicyLabel(p)}", (int)p);
		_prizePolicyOption.TooltipText =
			$"本人に分ける：賞金の{GuildManager.Core.Balance.PolicyBalance.PrizeShareRate:P0}を入賞した本人へ渡し、満足度+{GuildManager.Core.Balance.PolicyBalance.PrizeShareSatisfaction}（志向が大会・お金の子はさらに+{GuildManager.Core.Balance.PolicyBalance.PrizeShareDriveBonus}）。ギルドに入るのは残りだけ";
		_prizePolicyOption.ItemSelected += i =>
		{
			if (_state != null) _state.PrizePolicy = (GuildManager.Core.Models.PrizePolicy)_prizePolicyOption.GetItemId((int)i);
		};
		box.AddChild(_prizePolicyOption);
		box.MoveChild(_prizePolicyOption, index);
	}

	/// <summary>今のゲームの設定を映す（MainDashboard.RefreshAll から呼ぶ）。</summary>
	public void Refresh(GuildManager.Core.Models.GameState state)
	{
		_state = state;
		_autoBonusCheck.SetPressedNoSignal(state.AutoNegotiationBonus);
		_trainingPolicyOption.Select(_trainingPolicyOption.GetItemIndex((int)state.TrainingPolicy));
		_prizePolicyOption.Select(_prizePolicyOption.GetItemIndex((int)state.PrizePolicy));
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
