#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// ギルドのホール（2026年10月・§0.95、→ Core の HallBalance・HallSystem）。ホールの絵を敷き、押せる場所（扉・家具・テーブル）を重ねる。
/// テーブルの椅子には部隊の子の顔を丸く出し、縁の色でHPを示す。まだ使えない場所は暗くして、押しても移らない。
/// 押せる場所と椅子の位置は hall.csv（絵の幅・高さに対する割合）。絵を差し替えたら表の位置だけ合わせる。シーンは使わずコードで組む。
/// </summary>
public partial class GuildHallPanel : Control
{
	/// <summary>押せる場所が押された（行き先と、テーブルなら部隊の番号）。MainDashboard が画面を切り替える。</summary>
	public event Action<HallTarget, int> AreaPressed = delegate { };

	private const string ImagePath = "res://assets/ui/hall/hall.jpg";

	/// <summary>顔グラフィックの中で顔を切り抜く範囲（中心と一辺。画像の幅に対する割合。腰から上の立ち絵なので上寄り）。</summary>
	private const float FaceCenterX = 0.5f, FaceCenterY = 0.27f, FaceSize = 0.42f;

	private static readonly Dictionary<string, Texture2D> FaceCache = new();

	private TextureRect _background = null!;
	private Control _overlay = null!;
	private GameState? _state;
	private Vector2 _imageSize = new(1920, 1071);

	private readonly List<(HallArea Area, Button Button, ColorRect Shade)> _areas = new();
	private readonly Dictionary<int, Label> _squadBadges = new();
	private readonly List<(HallSeat Seat, Panel Ring, TextureRect Face)> _seats = new();

	public GuildHallPanel()
	{
		Name = "HallTab";
		SizeFlagsHorizontal = SizeFlags.ExpandFill;
		SizeFlagsVertical = SizeFlags.ExpandFill;
		ClipContents = true;

		var ground = new ColorRect { Color = new Color(0.93f, 0.89f, 0.82f), MouseFilter = MouseFilterEnum.Ignore };
		ground.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(ground);
		var texture = GD.Load<Texture2D>(ImagePath);
		if (texture != null) _imageSize = texture.GetSize();
		_background = new TextureRect
		{
			Texture = texture,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		_background.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(_background);
		_overlay = new Control { MouseFilter = MouseFilterEnum.Ignore };
		_overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(_overlay);

		// 押せる場所：テーブルを先に置き、扉・家具はその上（重なっても扉・家具が押せる）
		foreach (var area in HallBalance.Areas.OrderBy(a => a.Target == HallTarget.Squad ? 0 : 1))
		{
			var button = new Button { Flat = true, FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand };
			button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
			button.AddThemeStyleboxOverride("pressed", HoverStyle(0.25f));
			button.AddThemeStyleboxOverride("hover", HoverStyle(0.15f));
			button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
			var shade = new ColorRect { Color = new Color(0.05f, 0.04f, 0.06f, 0.5f), MouseFilter = MouseFilterEnum.Ignore, Visible = false };
			shade.SetAnchorsPreset(LayoutPreset.FullRect);
			button.AddChild(shade);
			var target = area;
			button.Pressed += () => OnAreaPressed(target);
			_overlay.AddChild(button);
			_areas.Add((area, button, shade));
		}

		// 椅子の顔：縁（HPの色）の丸の中に顔を丸く切り抜いて出す
		foreach (var seat in HallBalance.Seats)
		{
			var ring = new Panel { MouseFilter = MouseFilterEnum.Pass };
			var face = new TextureRect
			{
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.Scale,
				MouseFilter = MouseFilterEnum.Ignore,
			};
			ring.AddChild(face);
			_overlay.AddChild(ring);
			_seats.Add((seat, ring, face));
		}

		// 部隊の名札（顔より上に重ねる。短く「第1部隊 🏃」、方針の詳しい名前はテーブルのヒントに）
		foreach (var area in HallBalance.Areas.Where(a => a.Target == HallTarget.Squad))
		{
			var badge = new Label { MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center };
			badge.AddThemeColorOverride("font_color", new Color(1f, 0.97f, 0.88f));
			badge.AddThemeFontSizeOverride("font_size", 15);
			var plate = new StyleBoxFlat { BgColor = new Color(0.16f, 0.1f, 0.05f, 0.78f), BorderColor = new Color(0.85f, 0.65f, 0.3f, 0.9f) };
			plate.SetBorderWidthAll(1);
			plate.SetCornerRadiusAll(8);
			plate.SetContentMarginAll(3);
			badge.AddThemeStyleboxOverride("normal", plate);
			_overlay.AddChild(badge);
			_squadBadges[area.Squad] = badge;
		}

		Resized += Layout;
	}

	private static StyleBoxFlat HoverStyle(float fill)
	{
		var style = new StyleBoxFlat { BgColor = new Color(1f, 0.95f, 0.7f, fill), BorderColor = new Color(0.95f, 0.75f, 0.3f, 0.95f) };
		style.SetBorderWidthAll(3);
		style.SetCornerRadiusAll(10);
		return style;
	}

	/// <summary>今の状態で、使える場所・部隊の名前と方針・椅子の顔を描き直す。</summary>
	public void Refresh(GameState state)
	{
		_state = state;
		foreach (var (area, button, shade) in _areas)
		{
			var (open, reason) = HallSystem.Availability(state, area.Target);
			shade.Visible = !open && area.Target != HallTarget.Squad;
			button.TooltipText = area.Target == HallTarget.Squad ? SquadTooltip(state, area.Squad, HallSystem.Squad(state, area.Squad)) : open ? area.Label : $"{area.Label}\n{reason}";
		}
		foreach (var (squadNo, badge) in _squadBadges)
		{
			var squad = HallSystem.Squad(state, squadNo);
			string name = squad?.Name is { Length: > 0 } n ? n : $"第{squadNo}部隊";
			string order = squad == null || squad.Order == SquadOrder.None ? "方針なし" : $"{DungeonPanel.OrderIcon(squad.Order)}{DungeonPanel.OrderName(squad.Order)}";
			badge.Text = squad == null || squad.Order == SquadOrder.None ? name : $"{name} {DungeonPanel.OrderIcon(squad.Order)}";
			badge.TooltipText = order;
		}
		foreach (var squadNo in _squadBadges.Keys)
		{
			foreach (var (seat, adventurer) in HallSystem.SeatedMembers(state, squadNo))
			{
				var (_, ring, face) = _seats.First(s => s.Seat == seat);
				ring.Visible = adventurer != null;
				if (adventurer == null) continue;
				face.Texture = FaceTexture(adventurer.PortraitId);
				double hp = adventurer.MaxHP > 0 ? (double)adventurer.CurrentHP / adventurer.MaxHP : 1;
				var ringStyle = new StyleBoxFlat { BgColor = new Color(0.12f, 0.1f, 0.08f), BorderColor = HpColor(hp, adventurer) };
				ringStyle.SetBorderWidthAll(3);
				ringStyle.SetCornerRadiusAll(999);
				ring.AddThemeStyleboxOverride("panel", ringStyle);
				ring.TooltipText = $"{adventurer.Name}（{AdventurerPanel.JobLabel(adventurer.JobClass)}・{adventurer.Age}歳）\nHP {adventurer.CurrentHP}/{adventurer.MaxHP}" +
					(adventurer.Injury != InjurySeverity.None ? "\n負傷している" : "");
			}
		}
		Layout();
	}

	private static Color HpColor(double hp, Adventurer a) =>
		a.Injury != InjurySeverity.None ? new Color(0.85f, 0.3f, 0.85f)
		: hp >= 0.7 ? new Color(0.35f, 0.85f, 0.4f)
		: hp >= 0.4 ? new Color(1f, 0.65f, 0.2f)
		: new Color(1f, 0.3f, 0.3f);

	private static string SquadTooltip(GameState state, int squadNo, SavedParty? squad)
	{
		string order = squad == null || squad.Order == SquadOrder.None ? "方針なし" : DungeonPanel.OrderName(squad.Order);
		var members = HallSystem.SeatedMembers(state, squadNo).Where(s => s.Adventurer != null).Select(s => s.Adventurer!.Name).ToList();
		return $"第{squadNo}部隊（{order}）：" + (members.Count > 0 ? string.Join("・", members) : "まだ誰もいない") + "\n押すと大迷宮の画面で方針を決められる";
	}

	private void OnAreaPressed(HallArea area)
	{
		if (_state == null) return;
		if (area.Target != HallTarget.Squad && !HallSystem.Availability(_state, area.Target).Open)
			return;
		AreaPressed.Invoke(area.Target, area.Squad);
	}

	/// <summary>絵が窓に収まる範囲（縦横比を保って中央）に、押せる場所・名札・椅子を合わせる。</summary>
	private void Layout()
	{
		var size = Size;
		if (size.X <= 0 || size.Y <= 0) return;
		float scale = Math.Min(size.X / _imageSize.X, size.Y / _imageSize.Y);
		var shown = _imageSize * scale;
		var origin = (size - shown) / 2;
		Vector2 At(double x, double y) => origin + new Vector2((float)x * shown.X, (float)y * shown.Y);

		foreach (var (area, button, _) in _areas)
		{
			button.Position = At(area.X, area.Y);
			button.Size = new Vector2((float)area.W * shown.X, (float)area.H * shown.Y);
		}
		foreach (var (squadNo, badge) in _squadBadges)
		{
			var table = _areas.First(a => a.Area.Target == HallTarget.Squad && a.Area.Squad == squadNo).Area;
			// 名札はテーブルの範囲の真ん中（天板の上）に
			badge.Size = badge.GetMinimumSize();
			badge.Position = At(table.X + table.W / 2, table.Y + table.H / 2) - badge.Size / 2;
		}
		foreach (var (seat, ring, face) in _seats)
		{
			float d = (float)seat.Size * shown.X;
			ring.Size = new Vector2(d, d);
			ring.Position = At(seat.X, seat.Y) - new Vector2(d / 2, d / 2);
			face.Position = new Vector2(3, 3);
			face.Size = new Vector2(d - 6, d - 6);
		}
	}

	/// <summary>顔グラフィックから顔の部分を丸く切り抜いた絵（顔グラフィックごとに一度だけ作る）。</summary>
	private static Texture2D? FaceTexture(string? portraitId)
	{
		string key = portraitId ?? "";
		if (FaceCache.TryGetValue(key, out var cached))
			return cached;
		var image = AdventurerPanel.LoadPortraitTexture(portraitId).GetImage();
		if (image == null)
			return null;
		if (image.IsCompressed()) image.Decompress();
		image.Convert(Image.Format.Rgba8);
		int side = (int)(image.GetWidth() * FaceSize);
		int left = Math.Clamp((int)(image.GetWidth() * FaceCenterX) - side / 2, 0, image.GetWidth() - side);
		int top = Math.Clamp((int)(image.GetHeight() * FaceCenterY) - side / 2, 0, image.GetHeight() - side);
		var face = image.GetRegion(new Rect2I(left, top, side, side));
		face.Resize(96, 96, Image.Interpolation.Lanczos);
		for (int y = 0; y < 96; y++)
			for (int x = 0; x < 96; x++)
			{
				float dist = new Vector2(x + 0.5f - 48, y + 0.5f - 48).Length();
				var c = face.GetPixel(x, y);
				c.A *= Math.Clamp(48 - dist, 0, 1); // 丸の外を透明に（縁は1ピクセルでなめらかに）
				face.SetPixel(x, y, c);
			}
		var texture = ImageTexture.CreateFromImage(face);
		FaceCache[key] = texture;
		return texture;
	}
}
