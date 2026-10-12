using Godot;
using System;

/// <summary>
/// 「まとめて」の操作（§0.99：まとめて鑑定・おすすめを着せる・まとめて売る・おすすめの申し込み）の確かめの小窓。
/// 何がどう変わるか（Core の見込み）を本文に出し、【実行】で onOk を呼ぶ。本文が長ければスクロールする。
/// 小窓は別ウィンドウなので、ゲームのテーマを直接当てる（当てないと既定の小さな文字になる）。
/// </summary>
public static class ChoreConfirm
{
	public static void Show(Node parent, string title, string bbcode, string okText, Action onOk)
	{
		var dialog = new ConfirmationDialog
		{
			Title = title,
			OkButtonText = okText,
			CancelButtonText = "やめる",
			MinSize = new Vector2I(760, 420),
			Theme = GD.Load<Theme>("res://themes/dungeon_theme.tres"),
		};
		var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(720, 320), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		var text = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Text = bbcode };
		scroll.AddChild(text);
		dialog.AddChild(scroll);
		dialog.GetOkButton().CustomMinimumSize = new Vector2(150, 40);
		dialog.GetCancelButton().CustomMinimumSize = new Vector2(150, 40);
		dialog.Confirmed += () =>
		{
			dialog.QueueFree();
			onOk();
		};
		dialog.Canceled += dialog.QueueFree;
		parent.AddChild(dialog);
		dialog.PopupCentered();
	}
}
