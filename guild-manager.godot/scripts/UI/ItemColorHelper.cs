#nullable enable
using Godot;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

/// <summary>
/// 装備の個体（EquipmentItem）の表示色を一元管理する（→ 03 §9「アイテムの表示色」、2026年9月・§0.40）。
/// 保管庫・鑑定結果・冒険者詳細の装備欄・装備ダイアログが同じ規則で色を付けるための入口。
///
/// 現在の規則（アフィックスの付与数で決まる。希少度〈銅・銀・金・虹〉の色とは別系統）：
///  - 無銘・アフィックスなし … 淡灰白 <see cref="NormalHex"/>（既定のUIテキスト色）
///  - アフィックス1枠（接頭辞または接尾辞のみ） … 水色 <see cref="OneAffixHex"/>
///  - アフィックス2枠（接頭辞＋接尾辞＝当たり個体） … 黄緑 <see cref="TwoAffixHex"/>
///
///  - 固定アーティファクト（固有武具、→ uniques.csv Grade=Artifact） … 紫 <see cref="ArtifactHex"/>
///  - 伝説級（固有武具、→ uniques.csv Grade=Legendary） … 金 <see cref="UniqueHex"/>
/// 固有武具の2段は2026年9月・§0.45（ハクスラ Step 3）で使い始めた。固有武具にアフィックスは付かないため、判定は固有が優先。
/// CSVから消えた固有Id（定義を引けない個体）は通常の個体として扱う。
/// </summary>
public static class ItemColorHelper
{
	public const string NormalHex = "#e2e8f0";
	public const string OneAffixHex = "#38bdf8";
	public const string TwoAffixHex = "#4ade80";

	/// <summary>固定アーティファクト（固有武具・紫）。</summary>
	public const string ArtifactHex = "#c084fc";

	/// <summary>伝説級（固有武具・金）。</summary>
	public const string UniqueHex = "#fbbf24";

	/// <summary>表示色の段。値の大きいほど格上。</summary>
	public enum ItemColorTier
	{
		Normal,
		OneAffix,
		TwoAffix,
		Artifact,
		Unique,
	}

	/// <summary>個体の表示色の段（null＝通常）。</summary>
	public static ItemColorTier GetTier(EquipmentItem? item)
	{
		if (item == null) return ItemColorTier.Normal;
		switch (item.GetUniqueDefinition()?.Grade)
		{
			case UniqueGrade.Legendary: return ItemColorTier.Unique;
			case UniqueGrade.Artifact: return ItemColorTier.Artifact;
		}
		int affixCount = (string.IsNullOrEmpty(item.PrefixId) ? 0 : 1) + (string.IsNullOrEmpty(item.SuffixId) ? 0 : 1);
		return affixCount switch
		{
			2 => ItemColorTier.TwoAffix,
			1 => ItemColorTier.OneAffix,
			_ => ItemColorTier.Normal,
		};
	}

	/// <summary>段の色（#rrggbb）。</summary>
	public static string GetHex(ItemColorTier tier) => tier switch
	{
		ItemColorTier.TwoAffix => TwoAffixHex,
		ItemColorTier.OneAffix => OneAffixHex,
		ItemColorTier.Artifact => ArtifactHex,
		ItemColorTier.Unique => UniqueHex,
		_ => NormalHex,
	};

	/// <summary>個体の描画色。item が null なら通常色。</summary>
	public static Color GetItemColor(EquipmentItem? item) => new(GetHex(GetTier(item)));

	/// <summary>
	/// RichTextLabel 用に色を付けた文字列 <c>[color=#xxxxxx]…[/color]</c>。
	/// textOverride が null なら個体の表示名（EquipmentItem.DisplayName。item も null なら空文字）。
	/// </summary>
	public static string GetColoredBBCode(EquipmentItem? item, string? textOverride = null) =>
		$"[color={GetHex(GetTier(item))}]{textOverride ?? item?.DisplayName ?? ""}[/color]";
}
