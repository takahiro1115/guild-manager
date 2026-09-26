#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Systems;

/// <summary>
/// 大迷宮の探索状況を 1F〜100F の縦バーで描く（→ 03 §9・§4.5.6、2026年9月・§0.43）。上が1F、下が100F（深く潜る向き）。
///
/// 1階層ずつ4色＋濃淡で塗る。区間（10F単位）の解析度は、その区間を担当するボス（→ DungeonField.GetSegmentBoss）の
/// 解析段階（→ ScoutingResolver.GetTier）で決まり、進軍速度（1.0＋解析率×係数）と対応する：
///  - 未踏破・未解析 … 黒
///  - 未踏破・解析済み … 暗い青（段階が進むほど明るく）
///  - 踏破済み・未解析 … 白
///  - 踏破済み・解析済み … 青（段階が進むほど濃く）
/// ボスの階層には目盛りと印（✔ 撃破済み／⚔ 現在の目標／・ その他）と名前・解析率、到達階層の線（▶）、潜行中の部隊（📍）を重ねる。
/// 区間にマウスを重ねるとツールチップに区間の情報、クリックでその区間のボスを「閲覧」する（→ BossClicked）。
/// 値はすべて Core から引き、ここでは描画と入力変換だけを行う。
/// </summary>
public partial class DungeonDepthBar : Control
{
	private const float TopPad = 8f;
	private const float BottomPad = 8f;
	private const float FloorLabelWidth = 40f;
	private const float BarWidth = 34f;
	private const float MarkerGap = 6f;
	private const int FontSize = 12;

	/// <summary>段階ごとの色（添字：IntelTier の Basic=1 〜 Complete=4）。</summary>
	private static readonly Color[] ReachedAnalyzed =
	{
		new("#e5e7eb"), new("#bfdbfe"), new("#93c5fd"), new("#60a5fa"), new("#2563eb"),
	};

	private static readonly Color[] UnreachedAnalyzed =
	{
		new("#0b0b0e"), new("#1e2f5e"), new("#25407f"), new("#2d52a3"), new("#3764c8"),
	};

	private static readonly Color TickColor = new(0.35f, 0.35f, 0.4f);
	private static readonly Color BorderColor = new(0.45f, 0.45f, 0.5f);
	private static readonly Color ReachedLineColor = new("#22d3ee");
	private static readonly Color ViewedOutlineColor = new("#fbbf24");
	private static readonly Color TextColor = new(0.85f, 0.87f, 0.9f);
	private static readonly Color MutedTextColor = new(0.55f, 0.57f, 0.62f);
	private static readonly Color DefeatedColor = new("#4ade80");
	private static readonly Color TargetColor = new("#fb923c");
	private static readonly Color PinColor = new("#f472b6");

	private DungeonField? _field;
	private Guid? _targetBossId;
	private Guid? _viewedBossId;
	private List<(int Floor, string Label)> _pins = new();

	/// <summary>区間（のボス）がクリックされた（→ DungeonPanel が階層ボス欄にそのボスを表示する）。</summary>
	public event Action<Guid> BossClicked = delegate { };

	public override void _Ready()
	{
		CustomMinimumSize = new Vector2(0, 360);
		MouseFilter = MouseFilterEnum.Stop;
		MouseDefaultCursorShape = CursorShape.PointingHand;
		TooltipText = " "; // 空だとツールチップ自体が出ないため（内容は _GetTooltip で返す）
		Resized += QueueRedraw;
	}

	/// <summary>表示する迷宮と、目標・閲覧中のボス、潜行中の部隊の位置（階層と表示名）を渡して再描画する。</summary>
	public void SetData(DungeonField? field, Guid? targetBossId, Guid? viewedBossId, IEnumerable<(int Floor, string Label)> pins)
	{
		_field = field;
		_targetBossId = targetBossId;
		_viewedBossId = viewedBossId;
		_pins = pins.ToList();
		QueueRedraw();
	}

	/// <summary>階層の塗り色（踏破の有無 × 区間の解析段階）。</summary>
	public static Color FloorColor(DungeonField field, int floor)
	{
		var segmentBoss = field.GetSegmentBoss(floor - 1);
		int tier = segmentBoss == null ? 0 : (int)ScoutingResolver.GetTier(segmentBoss.IntelRate);
		tier = Math.Clamp(tier, 0, ReachedAnalyzed.Length - 1);
		bool reached = floor <= field.ReachedFloor;
		return reached ? ReachedAnalyzed[tier] : UnreachedAnalyzed[tier];
	}

	private float RowHeight => Math.Max(1f, (Size.Y - TopPad - BottomPad) / DungeonField.MaxFloor);

	/// <summary>その階層の行の上端のy座標。</summary>
	private float FloorTop(int floor) => TopPad + (floor - 1) * RowHeight;

	private int FloorAt(float y) => Math.Clamp((int)((y - TopPad) / RowHeight) + 1, 1, DungeonField.MaxFloor);

	public override void _Draw()
	{
		if (_field == null) return;

		var font = GetThemeDefaultFont();
		float barX = FloorLabelWidth;
		float markerX = barX + BarWidth + MarkerGap;
		float row = RowHeight;

		// 階層ごとの塗り（隣接する同色の行はまとめて1つの矩形にする）
		int start = 1;
		var startColor = FloorColor(_field, 1);
		for (int floor = 2; floor <= DungeonField.MaxFloor + 1; floor++)
		{
			var color = floor <= DungeonField.MaxFloor ? FloorColor(_field, floor) : default;
			if (floor <= DungeonField.MaxFloor && color == startColor) continue;
			DrawRect(new Rect2(barX, FloorTop(start), BarWidth, (floor - start) * row), startColor);
			start = floor;
			startColor = color;
		}
		DrawRect(new Rect2(barX, TopPad, BarWidth, DungeonField.MaxFloor * row), BorderColor, filled: false, width: 1f);

		// 閲覧中のボスの区間を枠で囲む
		var viewed = _field.Bosses.FirstOrDefault(b => b.Id == _viewedBossId);
		if (viewed != null)
		{
			int segStart = _field.Bosses.Where(b => b.Floor < viewed.Floor).Select(b => b.Floor).DefaultIfEmpty(0).Max() + 1;
			DrawRect(new Rect2(barX - 2, FloorTop(segStart), BarWidth + 4, (viewed.Floor - segStart + 1) * row),
				ViewedOutlineColor, filled: false, width: 2f);
		}

		// ボスの目盛り・印・名前
		float ascent = font.GetAscent(FontSize);
		foreach (var boss in _field.Bosses.OrderBy(b => b.Floor))
		{
			float y = FloorTop(boss.Floor) + row; // ボス階層の行の下端
			DrawLine(new Vector2(barX - 4, y), new Vector2(barX + BarWidth + 4, y), TickColor, 1f);
			DrawString(font, new Vector2(0, y + ascent / 2 - 1), $"{boss.Floor}F", HorizontalAlignment.Right, FloorLabelWidth - 6, FontSize, MutedTextColor);

			string icon;
			Color color;
			if (boss.IsDefeated) { icon = "✔"; color = DefeatedColor; }
			else if (boss.Id == _targetBossId) { icon = "⚔"; color = TargetColor; }
			else { icon = "・"; color = MutedTextColor; }

			bool known = boss.IsDefeated || boss.Id == _targetBossId || boss.IntelRate > 0;
			string name = known ? boss.Name : "？？？";
			string intel = boss.IntelRate > 0 ? $" 解析{boss.IntelRate * 100:F0}%" : "";
			DrawString(font, new Vector2(markerX, y + ascent / 2 - 1), $"{icon} {name}{intel}",
				HorizontalAlignment.Left, Size.X - markerX, FontSize, boss.Id == _viewedBossId ? ViewedOutlineColor : color);
		}

		// 到達階層の線
		float reachedY = FloorTop(_field.ReachedFloor) + row;
		DrawLine(new Vector2(barX - 6, reachedY), new Vector2(barX + BarWidth + 6, reachedY), ReachedLineColor, 2f);
		DrawString(font, new Vector2(0, reachedY + ascent / 2 - 1), $"▶{_field.ReachedFloor}F",
			HorizontalAlignment.Right, FloorLabelWidth - 6, FontSize, ReachedLineColor);

		// 潜行中の部隊（ボス名の行と重ならないよう、バーの左の階層ラベル欄の内側に📍だけ描き、名前はツールチップへ）
		foreach (var (floor, _) in _pins)
		{
			float y = FloorTop(floor) + row / 2;
			DrawCircle(new Vector2(barX + BarWidth / 2, y), Math.Max(3f, Math.Min(6f, row * 1.5f)), PinColor);
		}
	}

	public override string _GetTooltip(Vector2 atPosition)
	{
		if (_field == null) return "";
		int floor = FloorAt(atPosition.Y);
		var boss = _field.GetSegmentBoss(floor - 1);
		var lines = new List<string>
		{
			$"{floor}F：{(floor <= _field.ReachedFloor ? "踏破済み" : "未踏破")}",
		};
		if (boss != null)
		{
			var tier = ScoutingResolver.GetTier(boss.IntelRate);
			bool known = boss.IsDefeated || boss.Id == _targetBossId || boss.IntelRate > 0;
			lines.Add($"区間の主：第{boss.Floor}層「{(known ? boss.Name : "？？？")}」{(boss.IsDefeated ? "（撃破済み）" : boss.Id == _targetBossId ? "（現在の目標）" : "")}");
			lines.Add($"解析率 {boss.IntelRate * 100:F0}%（{DungeonPanel.TierLabel(tier)}）");
			lines.Add($"進軍速度 ×{DungeonTraversalResolver.IntelSpeedMultiplier(boss):0.0#}（1.0＋解析率×{DungeonTraversalBalance.IntelSpeedBonusPerIntel:0.#}）");
		}
		foreach (var (pinFloor, label) in _pins.Where(p => Math.Abs(p.Floor - floor) <= 1))
			lines.Add($"📍 {label}（{pinFloor}F）");
		lines.Add("クリックでこの区間のボスを表示");
		return string.Join("\n", lines);
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (_field == null) return;
		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
		{
			var boss = _field.GetSegmentBoss(FloorAt(click.Position.Y) - 1);
			if (boss != null)
			{
				BossClicked.Invoke(boss.Id);
				AcceptEvent();
			}
		}
	}
}
