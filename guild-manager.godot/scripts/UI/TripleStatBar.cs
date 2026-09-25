using Godot;
using System;

/// <summary>
/// 7大能力値のバー（→ 03 §9「部隊・冒険者」画面。2026年9月新設、§0.30で4層へ拡張）。
/// バーの全幅を常に能力値100（上限）に固定し、冒険者どうしを見比べても同じ物差しで読めるようにする。
///
/// 4層の重なり（奥から手前へ）：
///  - 黒：上限100までの未到達枠
///  - 灰：潜在能力 PA（素の能力値が到達しうる伸びしろ）
///  - 白：素の能力値（成長・訓練で伸びる本人の実力）
///  - 水色：装備補正（素の値 → 実効値の区間）。PAを超えれば灰の外の黒い領域まで伸びる＝名剣・鎧による「限界突破」
/// 特性（古傷など）で実効値が素の値を下回る場合は、白を実効値で止め、失われた区間（実効値 → 素の値）を
/// くすんだ赤で示す。
///
/// 3層時代は白に実効値を描いていたため、装備補正で実効値がPAを超えると白が灰を突き抜け、
/// 「伸びしろ以上に育っている」ように見えていた（→ §0.30）。
/// クラス名は既存シーンの参照を保つため据え置いている。
/// </summary>
public partial class TripleStatBar : Control
{
	/// <summary>バー全幅が表す能力値（上限）。</summary>
	public const int MaxStatValue = 100;

	private static readonly Color CapColor = new(0.07f, 0.07f, 0.09f);
	private static readonly Color PotentialColor = new(0.46f, 0.47f, 0.52f);
	private static readonly Color RawColor = new(0.95f, 0.95f, 0.95f);
	private static readonly Color BonusColor = new("#38bdf8");
	private static readonly Color PenaltyColor = new(0.55f, 0.22f, 0.22f);
	private static readonly Color BorderColor = new(0.3f, 0.3f, 0.35f);

	/// <summary>素の能力値（白）。</summary>
	public int Raw { get; private set; }

	/// <summary>実効値（特性・装備込み。白＋水色の右端）。</summary>
	public int Effective { get; private set; }

	/// <summary>潜在能力 PA（灰）。</summary>
	public int Potential { get; private set; }

	/// <summary>
	/// 値を設定して再描画する。いずれも 0〜100 にクランプして描く（100を超える実効値は右端で止まる）。
	/// </summary>
	public void SetValues(int raw, int effective, int potential)
	{
		Raw = Math.Clamp(raw, 0, MaxStatValue);
		Effective = Math.Clamp(effective, 0, MaxStatValue);
		Potential = Math.Clamp(potential, 0, MaxStatValue);
		TooltipText = $"素の値 {raw} ／ 補正 {FormatSigned(effective - raw)} ／ 実効値 {effective} ／ PA {potential} ／ 上限 {MaxStatValue}";
		QueueRedraw();
	}

	/// <summary>
	/// バー右端の数値ラベルの書式。補正なし＝「素の値 / PA」、補正あり＝「実効値 (+補正) / PA」
	/// （マイナス補正は「実効値 (-補正) / PA」）。補正＝実効値 − 素の値。
	/// </summary>
	public static string FormatLabel(int raw, int effective, int potential)
	{
		int bonus = effective - raw;
		return bonus == 0 ? $"{raw} / {potential}" : $"{effective} ({FormatSigned(bonus)}) / {potential}";
	}

	private static string FormatSigned(int value) => value > 0 ? $"+{value}" : value.ToString();

	public override void _Draw()
	{
		var size = Size;
		float W(int value) => size.X * value / MaxStatValue;

		DrawRect(new Rect2(Vector2.Zero, size), CapColor);
		DrawRect(new Rect2(Vector2.Zero, new Vector2(W(Potential), size.Y)), PotentialColor);

		int whiteEnd = Math.Min(Raw, Effective);
		DrawRect(new Rect2(Vector2.Zero, new Vector2(W(whiteEnd), size.Y)), RawColor);

		if (Effective > Raw)
			DrawRect(new Rect2(W(Raw), 0, W(Effective) - W(Raw), size.Y), BonusColor);
		else if (Effective < Raw)
			DrawRect(new Rect2(W(Effective), 0, W(Raw) - W(Effective), size.Y), PenaltyColor);

		DrawRect(new Rect2(Vector2.Zero, size), BorderColor, filled: false, width: 1f);
	}
}
