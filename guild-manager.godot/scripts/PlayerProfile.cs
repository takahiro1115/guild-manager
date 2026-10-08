#nullable enable
using Godot;
using System.Text.Json;

/// <summary>
/// セーブとは別に残すプレイヤーの記録（2026年10月・§0.88）：一度クリアしたか。セーブを消しても残る（user://profile.json）。
/// クリアしたことがあれば、新しいゲームを始めるときに「物語と手ほどき あり／なし」を選べる。
/// </summary>
public static class PlayerProfile
{
	private const string Path = "user://profile.json";

	private sealed class Data
	{
		public bool ClearedOnce { get; set; }
	}

	private static Data? _data;

	private static Data Current => _data ??= Load();

	/// <summary>一度クリアしたか（エンディングを見た、またはクリア済みのセーブを読み込んだ）。</summary>
	public static bool ClearedOnce => Current.ClearedOnce;

	/// <summary>クリアしたことを記録する（何度呼んでもよい）。</summary>
	public static void MarkCleared()
	{
		if (Current.ClearedOnce) return;
		Current.ClearedOnce = true;
		using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
		file?.StoreString(JsonSerializer.Serialize(Current));
	}

	private static Data Load()
	{
		if (!FileAccess.FileExists(Path)) return new Data();
		using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
		try
		{
			return file == null ? new Data() : JsonSerializer.Deserialize<Data>(file.GetAsText()) ?? new Data();
		}
		catch (JsonException)
		{
			return new Data(); // 壊れていたら記録なしとして扱う（次にクリアしたときに書き直す）
		}
	}
}
