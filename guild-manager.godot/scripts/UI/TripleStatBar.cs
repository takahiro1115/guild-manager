using Godot;
using System;

/// <summary>
/// 7大能力値のバー（→ 03 §9「部隊・冒険者」画面。2026年9月新設、§0.30で4層、§0.36で特性補正を分離）。
/// バーの全幅を常に能力値100（上限）に固定し、冒険者どうしを見比べても同じ物差しで読めるようにする。
///
/// 値は3段で渡す：素の値（raw）→ 特性補正後（traitAdjusted）→ 実効値（effective＝特性補正後＋装備補正）。
/// 重なり（奥から手前へ）：
///  - 黒：上限100までの未到達枠
///  - 灰：潜在能力 PA（素の能力値が到達しうる伸びしろ）
///  - 白：素の能力値（成長・訓練で伸びる本人の実力）。特性で下がっていれば特性補正後の値で止める
///  - 緑：特性によるプラス補正（素の値 → 特性補正後）
///  - くすんだ赤：特性によるマイナス補正で失われた区間（特性補正後 → 素の値。古傷・トラウマ）
///  - 水色：装備補正（特性補正後 → 実効値）。PAを超えれば灰の外の黒い領域まで伸びる＝名剣・鎧による「限界突破」
///
/// 3層時代は白に実効値を描いていたため、装備補正で実効値がPAを超えると白が灰を突き抜け、
/// 「伸びしろ以上に育っている」ように見えていた（→ §0.30）。§0.30の4層では特性と装備の補正を
/// 差し引きした1本でしか見えなかったため、§0.36で特性（緑／赤）と装備（水色）を分けた。
/// クラス名は既存シーンの参照を保つため据え置いている。
/// </summary>
public partial class TripleStatBar : Control
{
	/// <summary>バー全幅が表す能力値（上限）。</summary>
	public const int MaxStatValue = 100;

	private static readonly Color CapColor = new(0.07f, 0.07f, 0.09f);
	private static readonly Color PotentialColor = new(0.46f, 0.47f, 0.52f);
	private static readonly Color RawColor = new(0.95f, 0.95f, 0.95f);
	private static readonly Color TraitBonusColor = new("#4ade80");
	private static readonly Color TraitPenaltyColor = new(0.55f, 0.22f, 0.22f);
	private static readonly Color EquipmentBonusColor = new("#38bdf8");
	private static readonly Color BorderColor = new(0.3f, 0.3f, 0.35f);

	/// <summary>素の能力値（白）。</summary>
	public int Raw { get; private set; }

	/// <summary>特性補正後の値（素の値に特性の割合補正を掛けたもの。装備補正は含まない）。</summary>
	public int TraitAdjusted { get; private set; }

	/// <summary>実効値（特性・装備込み）。</summary>
	public int Effective { get; private set; }

	/// <summary>潜在能力 PA（灰）。</summary>
	public int Potential { get; private set; }

	/// <summary>
	/// 値を設定して再描画する。いずれも 0〜100 にクランプして描く（100を超える実効値は右端で止まる）。
	/// </summary>
	public void SetValues(int raw, int traitAdjusted, int effective, int potential)
	{
		Raw = Math.Clamp(raw, 0, MaxStatValue);
		TraitAdjusted = Math.Clamp(traitAdjusted, 0, MaxStatValue);
		Effective = Math.Clamp(effective, 0, MaxStatValue);
		Potential = Math.Clamp(potential, 0, MaxStatValue);
		TooltipText = $"素の値 {raw} ／ 特性 {FormatSigned(traitAdjusted - raw)} ／ 装備 {FormatSigned(effective - traitAdjusted)} ／ " +
			$"実効値 {effective} ／ PA {potential} ／ 上限 {MaxStatValue}";
		QueueRedraw();
	}

	/// <summary>
	/// バー右端の数値ラベルの書式（特性補正＝特性補正後 − 素の値、装備補正＝実効値 − 特性補正後）：
	///  - 補正なし：「素の値 / PA」（例：40 / 50）
	///  - 装備補正のみ：「実効値 (+装備) / PA」（例：56 (+6) / 50。§0.30と同じ）
	///  - 特性補正のみ：「実効値 (特性-6) / PA」
	///  - 両方：「実効値 (特性-15 装備+5) / PA」
	/// </summary>
	public static string FormatLabel(int raw, int traitAdjusted, int effective, int potential)
	{
		int trait = traitAdjusted - raw;
		int equipment = effective - traitAdjusted;
		if (trait == 0 && equipment == 0) return $"{raw} / {potential}";
		if (trait == 0) return $"{effective} ({FormatSigned(equipment)}) / {potential}";
		if (equipment == 0) return $"{effective} (特性{FormatSigned(trait)}) / {potential}";
		return $"{effective} (特性{FormatSigned(trait)} 装備{FormatSigned(equipment)}) / {potential}";
	}

	private static string FormatSigned(int value) => value > 0 ? $"+{value}" : value.ToString();

	public override void _Draw()
	{
		var size = Size;
		float W(int value) => size.X * value / MaxStatValue;
		void Segment(int from, int to, Color color)
		{
			if (to > from)
				DrawRect(new Rect2(W(from), 0, W(to) - W(from), size.Y), color);
		}

		DrawRect(new Rect2(Vector2.Zero, size), CapColor);
		Segment(0, Potential, PotentialColor);

		// 素の値（特性で下がっていれば下がった値まで）
		Segment(0, Math.Min(Raw, TraitAdjusted), RawColor);
		// 特性補正：プラスは緑、マイナスは失われた区間をくすんだ赤
		Segment(Raw, TraitAdjusted, TraitBonusColor);
		Segment(TraitAdjusted, Raw, TraitPenaltyColor);
		// 装備補正：特性補正後から実効値まで（特性で失われた区間に重なっても上に描く）
		Segment(TraitAdjusted, Effective, EquipmentBonusColor);

		DrawRect(new Rect2(Vector2.Zero, size), BorderColor, filled: false, width: 1f);
	}
}
