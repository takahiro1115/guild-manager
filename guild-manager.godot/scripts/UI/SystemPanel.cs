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

	/// <summary>「進行状況を保存」の押下を通知する。MainDashboard がセーブを実行し、成否を返す。</summary>
	public event Func<bool> SaveRequested = () => false;

	public override void _Ready()
	{
		_saveDataBtn = GetNode<Button>("%SaveDataBtn");
		_saveFeedbackLabel = GetNode<Label>("%SaveFeedbackLabel");
		_saveDataBtn.Pressed += OnSaveDataPressed;
	}

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
