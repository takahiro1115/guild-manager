using Godot;

/// <summary>画面共通の見た目の部品（部隊タブ・部隊ボタンの「選択中」の色など）。</summary>
public static class UiStyles
{
	private static StyleBoxFlat Make(Color bg, Color border)
	{
		var style = new StyleBoxFlat { BgColor = bg, BorderColor = border };
		style.SetBorderWidthAll(2);
		style.SetCornerRadiusAll(4);
		style.ContentMarginLeft = 8;
		style.ContentMarginRight = 8;
		style.ContentMarginTop = 4;
		style.ContentMarginBottom = 4;
		return style;
	}

	/// <summary>
	/// 大きなパネルの枠のアート（assets/ui/panel_*.png、509×264＝元の絵の55%、角の飾りが約44px）。9スライスで伸ばして使う。
	/// 小さな箱（部隊メンバーのカードなど）には使わない（角の飾りが中身を潰すため）。variant：normal・info・warn・danger。
	/// </summary>
	public static StyleBoxTexture PanelArt(string variant = "normal")
	{
		var style = new StyleBoxTexture { Texture = GD.Load<Texture2D>($"res://assets/ui/panel_{variant}.png") };
		style.SetTextureMarginAll(44);
		style.ContentMarginLeft = 28;
		style.ContentMarginRight = 28;
		style.ContentMarginTop = 36;
		style.ContentMarginBottom = 34;
		style.DrawCenter = true;
		return style;
	}

	/// <summary>パネル（PanelContainer）にアートの枠をかぶせる。中の MarginContainer の余白は、枠の分があるので小さく詰める。</summary>
	public static void ApplyPanelArt(Control panel, string variant = "normal", int innerMargin = 2)
	{
		panel.AddThemeStyleboxOverride("panel", PanelArt(variant));
		foreach (var child in panel.GetChildren())
		{
			if (child is MarginContainer margin)
				foreach (var side in new[] { "left", "top", "right", "bottom" })
					margin.AddThemeConstantOverride($"margin_{side}", innerMargin);
		}
	}

	/// <summary>トグルボタンの「押されている（選択中）」を、黒ではなく青にする。</summary>
	public static void ApplySelectedToggleStyle(Button button)
	{
		button.AddThemeStyleboxOverride("pressed", Make(new Color(0.16f, 0.32f, 0.55f), new Color(0.5f, 0.78f, 1.0f)));
		button.AddThemeStyleboxOverride("hover_pressed", Make(new Color(0.2f, 0.38f, 0.62f), new Color(0.6f, 0.85f, 1.0f)));
		button.AddThemeColorOverride("font_pressed_color", Colors.White);
		button.AddThemeColorOverride("font_hover_pressed_color", Colors.White);
	}
}
