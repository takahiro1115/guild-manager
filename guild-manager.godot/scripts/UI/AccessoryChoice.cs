#nullable enable
using Godot;
using System;
using GuildManager.Core.Models;

/// <summary>
/// 装飾品を着けようとして、装飾枠が2つとも埋まっているときに「どちらと入れ替えるか」を選ばせる小さなダイアログ（§0.67）。
/// 装飾品は1と2の区別が無いので、枠の番号ではなく、今着けている品の名前で選ぶ。やめる、も選べる。
/// </summary>
public static class AccessoryChoice
{
	/// <summary>
	/// ダイアログを開く。選ぶと chosen に、入れ替える側の装飾枠（Accessory1／Accessory2）を渡す。やめたときは何も呼ばない。
	/// host は、ダイアログを子として置くノード（ポップアップや画面のパネル）。
	/// </summary>
	public static void Ask(Node host, Adventurer adventurer, string newItemName, Action<EquipmentSlot> chosen)
	{
		string first = adventurer.EquippedAccessory1?.DisplayName ?? "装飾品";
		string second = adventurer.EquippedAccessory2?.DisplayName ?? "装飾品";

		var dialog = new AcceptDialog
		{
			Title = "装飾品の入れ替え",
			DialogText = $"{adventurer.Name}の装飾品は2つとも埋まっている。\n「{newItemName}」を着けるには、どちらと入れ替える？\n（外した方は保管庫へ戻る）",
			OkButtonText = $"「{first}」と入れ替える",
		};
		dialog.AddButton($"「{second}」と入れ替える", false, "second");
		dialog.AddCancelButton("やめる");

		bool decided = false;
		dialog.Confirmed += () => { decided = true; chosen(EquipmentSlot.Accessory1); };
		dialog.CustomAction += action =>
		{
			if (action != "second") return;
			decided = true;
			dialog.Hide();
			chosen(EquipmentSlot.Accessory2);
		};
		dialog.Canceled += () => { if (!decided) dialog.QueueFree(); };
		dialog.VisibilityChanged += () =>
		{
			if (!dialog.Visible && GodotObject.IsInstanceValid(dialog))
				dialog.QueueFree();
		};

		host.AddChild(dialog);
		dialog.PopupCentered();
	}
}
