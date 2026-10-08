#nullable enable
using Godot;
using System;
using System.Linq;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// ギルドの手引き（2026年10月・§0.87、チュートリアル③-2、→ Core の GuideSystem）。ヘッダーの「📝 手引き」から開く小窓。
/// 見た場面ごとに、やること（済んだら ☑）・説明・画面へのボタンを並べる。開いたまま画面を切り替えられる（排他にしない）。
/// シーンは使わずコードで組む。
/// </summary>
public partial class GuidePopup : Window
{
	/// <summary>画面へのボタンが押された（【画面へ】の文言。MainDashboard が画面を切り替える）。</summary>
	public event Action<string> JumpRequested = delegate { };

	private VBoxContainer _list = null!;
	private GameState? _state;

	public override void _Ready()
	{
		Title = "📝 ギルドの手引き";
		Exclusive = false;
		Size = new Vector2I(760, 620);
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

		var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		_list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_list.AddThemeConstantOverride("separation", 14);
		scroll.AddChild(_list);
		root.AddChild(scroll);

		var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		var close = new Button { Text = "閉じる", CustomMinimumSize = new Vector2(160, 36) };
		close.Pressed += Hide;
		buttons.AddChild(close);
		root.AddChild(buttons);
	}

	/// <summary>手引きを開く（開いていれば中身を新しくする）。</summary>
	public void Open(GameState state)
	{
		_state = state;
		Refresh();
		if (!Visible)
			PopupCentered(Size);
	}

	/// <summary>開いているときだけ中身を新しくする（画面の再描画のたびに呼ぶ）。</summary>
	public void Refresh()
	{
		if (_state == null || _list == null) return;
		foreach (var child in _list.GetChildren())
			child.QueueFree();

		var groups = GuideSystem.Groups(_state);
		if (groups.Count == 0)
		{
			_list.AddChild(new Label { Text = "今やることはありません。", Modulate = new Color(0.7f, 0.7f, 0.7f) });
			return;
		}
		foreach (var g in groups)
		{
			var box = new VBoxContainer();
			box.AddThemeConstantOverride("separation", 4);
			var text = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			text.Text = string.Join("\n", g.Items.Select(i => i.Done
				? $"[color=gray]☑ {Escape(i.Text)}[/color]"
				: $"[color=khaki]□ {Escape(i.Text)}[/color]"))
				+ (g.Notes.Count > 0 ? "\n" + string.Join("\n", g.Notes.Select(n => $"[color=#a0a0b0]　{Escape(n)}[/color]")) : "");
			box.AddChild(text);
			if (g.Jump != null && !g.AllDone)
			{
				var jump = new Button { Text = $"▶ {g.Jump}", SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
				string label = g.Jump;
				jump.Pressed += () => JumpRequested.Invoke(label);
				box.AddChild(jump);
			}
			_list.AddChild(box);
			_list.AddChild(new HSeparator());
		}
	}

	private static string Escape(string text) => text.Replace("[", "[lb]");
}
