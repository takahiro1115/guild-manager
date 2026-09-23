using Godot;
using System;

/// <summary>
/// 7大能力値の3層バー（→ 03 §9「部隊・冒険者」画面、2026年9月新設）。
/// バーの全幅を常に能力値100（上限）に固定し、冒険者どうしを見比べても同じ物差しで読めるようにする。
///
/// 3層の重なり（奥から手前へ）：
///  - 黒：上限100までの未到達枠
///  - 灰：潜在能力 PA（到達しうる伸びしろ）
///  - 白：現在の実効値（いま発揮される実力）
/// 旧来の ProgressBar は MaxValue を PA に合わせていたため、PAの低い冒険者でもバーが満タンに見え、
/// 冒険者間の比較にも育成余地の判断にも使いにくかった。
/// </summary>
public partial class TripleStatBar : Control
{
	/// <summary>バー全幅が表す能力値（上限）。</summary>
	public const int MaxStatValue = 100;

	private static readonly Color CapColor = new(0.07f, 0.07f, 0.09f);
	private static readonly Color PotentialColor = new(0.46f, 0.47f, 0.52f);
	private static readonly Color CurrentColor = new(0.95f, 0.95f, 0.95f);
	private static readonly Color BorderColor = new(0.3f, 0.3f, 0.35f);

	/// <summary>現在の実効値（白）。</summary>
	public int Current { get; private set; }

	/// <summary>潜在能力 PA（灰）。</summary>
	public int Potential { get; private set; }

	/// <summary>値を設定して再描画する。どちらも 0〜100 にクランプする。</summary>
	public void SetValues(int current, int potential)
	{
		Current = Math.Clamp(current, 0, MaxStatValue);
		Potential = Math.Clamp(potential, 0, MaxStatValue);
		TooltipText = $"現在値 {Current} ／ PA {Potential} ／ 上限 {MaxStatValue}";
		QueueRedraw();
	}

	public override void _Draw()
	{
		var size = Size;
		DrawRect(new Rect2(Vector2.Zero, size), CapColor);
		DrawRect(new Rect2(Vector2.Zero, new Vector2(size.X * Potential / MaxStatValue, size.Y)), PotentialColor);
		DrawRect(new Rect2(Vector2.Zero, new Vector2(size.X * Current / MaxStatValue, size.Y)), CurrentColor);
		DrawRect(new Rect2(Vector2.Zero, size), BorderColor, filled: false, width: 1f);
	}
}
