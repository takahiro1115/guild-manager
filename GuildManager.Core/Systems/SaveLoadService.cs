using System;
using System.IO;
using System.Text.Json;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// セーブ/ロード処理（JSON・単一スロット）。仕様書 03 §12 参照。
    ///
    /// - 保存形式：JSON（System.Text.Json）。GameState.ToSaveData()で変換してから書き出す。
    /// - セーブスロット：単一（1ファイルへの上書きのみ）。複数スロットはpost-MVP（→ §11）。
    /// - GuildManager.Core はGodotに依存しない方針（→ 05技術メモ）のため、保存先ディレクトリは
    ///   コンストラクタ引数として外部から注入する（Godot側は OS.GetUserDataDir() を渡す想定）。
    /// - ロード失敗（ファイル破損・バージョン不一致・列挙型パース失敗）はすべて null を返す形で
    ///   統一し、呼び出し側（UI）が「新規ゲームのみ提示」等のフォールバックを行える設計とする。
    /// </summary>
    public class SaveLoadService
    {
        private const string SaveFileName = "savegame.json";

        /// <summary>
        /// 現在サポートしているセーブフォーマットのバージョン。SaveData.SaveVersionと
        /// 一致しない場合はロード失敗として扱う（マイグレーション処理は未実装、→ §11）。
        /// </summary>
        public const int CurrentSaveVersion = 1;

        private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

        private readonly string _saveDirectory;

        public SaveLoadService(string saveDirectory)
        {
            _saveDirectory = saveDirectory;
        }

        /// <summary>現在の状態をセーブファイルへ書き出す（自動保存・手動保存共通、単一スロットへの上書き）。</summary>
        public void Save(GameState state)
        {
            var saveData = state.ToSaveData();
            var json = JsonSerializer.Serialize(saveData, SerializerOptions);
            Directory.CreateDirectory(_saveDirectory);
            File.WriteAllText(GetSavePath(), json);
        }

        /// <summary>
        /// セーブファイルを読み込み、GameStateとして復元する。ファイルが存在しない・
        /// 破損している・バージョンが一致しない・列挙型のパースに失敗する、のいずれかに
        /// 該当する場合は null を返す（既存セーブは上書きしない：呼び出し側の責務）。
        /// </summary>
        public GameState? Load()
        {
            var path = GetSavePath();
            if (!File.Exists(path))
                return null;

            try
            {
                var json = File.ReadAllText(path);
                var saveData = JsonSerializer.Deserialize<SaveData>(json);
                if (saveData == null)
                    return null;

                if (saveData.SaveVersion != CurrentSaveVersion)
                    return null; // バージョン不一致：現時点ではマイグレーション未実装のためロード失敗扱い

                return GameState.FromSaveData(saveData);
            }
            catch (Exception)
            {
                // JSON構文エラー・列挙型パース失敗（GameState.FromSaveDataがFormatExceptionを
                // 投げる）等、あらゆる破損パターンをまとめてロード失敗として扱う。
                return null;
            }
        }

        /// <summary>セーブファイルが存在するかどうか（起動時の「続きから」提示に使う）。</summary>
        public bool SaveFileExists() => File.Exists(GetSavePath());

        private string GetSavePath() => Path.Combine(_saveDirectory, SaveFileName);
    }
}
