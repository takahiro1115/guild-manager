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

	/// <summary>トグルボタンの「押されている（選択中）」を、黒ではなく青にする。</summary>
	public static void ApplySelectedToggleStyle(Button button)
	{
		button.AddThemeStyleboxOverride("pressed", Make(new Color(0.16f, 0.32f, 0.55f), new Color(0.5f, 0.78f, 1.0f)));
		button.AddThemeStyleboxOverride("hover_pressed", Make(new Color(0.2f, 0.38f, 0.62f), new Color(0.6f, 0.85f, 1.0f)));
		button.AddThemeColorOverride("font_pressed_color", Colors.White);
		button.AddThemeColorOverride("font_hover_pressed_color", Colors.White);
	}
}
