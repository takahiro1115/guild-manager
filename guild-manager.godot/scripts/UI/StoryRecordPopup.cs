#nullable enable
using Godot;
using System;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 物語の記録（2026年10月・§0.92、→ Core の StorySystem.Record）。ヘッダーの「📖 物語」から開く小窓。
/// 見た場面を章ごとに見た順で並べ、選ぶと会話の小窓で頭から見返す（見たことの記録は変えない）。シーンは使わずコードで組む。
/// </summary>
public partial class StoryRecordPopup : Window
{
	/// <summary>場面が選ばれた（場面のId。MainDashboard が会話の小窓で見返す）。</summary>
	public event Action<string> SceneRequested = delegate { };

	private VBoxContainer _list = null!;

	public override void _Ready()
	{
		Title = "📖 物語の記録";
		Exclusive = false;
		Size = new Vector2I(680, 640);
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

		root.AddChild(new Label { Text = "これまでに見た場面です。選ぶと、もう一度読めます。", Modulate = new Color(0.75f, 0.75f, 0.8f) });
		var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		_list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_list.AddThemeConstantOverride("separation", 4);
		scroll.AddChild(_list);
		root.AddChild(scroll);

		var close = new Button { Text = "閉じる", CustomMinimumSize = new Vector2(160, 36), SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
		close.Pressed += Hide;
		root.AddChild(close);
	}

	/// <summary>今の記録で一覧を作り直して開く。</summary>
	public void Open(GameState state)
	{
		foreach (var child in _list.GetChildren())
			child.QueueFree();

		var chapters = StorySystem.Record(state);
		if (chapters.Count == 0)
			_list.AddChild(new Label { Text = "まだ物語の場面を見ていません。", Modulate = new Color(0.7f, 0.7f, 0.7f) });
		foreach (var chapter in chapters)
		{
			var heading = new Label { Text = chapter.Title, Modulate = new Color(0.95f, 0.85f, 0.55f) };
			heading.AddThemeFontSizeOverride("font_size", 18);
			_list.AddChild(heading);
			foreach (var entry in chapter.Entries)
			{
				var row = new HBoxContainer();
				var button = new Button
				{
					Text = $"▶ {entry.Title}",
					Flat = true,
					Alignment = HorizontalAlignment.Left,
					SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				};
				string id = entry.SceneId;
				button.Pressed += () => SceneRequested.Invoke(id);
				row.AddChild(button);
				row.AddChild(new Label { Text = entry.SeenWeek > 0 ? GameCalendar.Format(entry.SeenWeek) : "", Modulate = new Color(0.65f, 0.65f, 0.7f) });
				_list.AddChild(row);
			}
			_list.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
		}
		PopupCentered(Size);
	}
}
