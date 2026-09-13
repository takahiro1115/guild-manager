using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// バランス値CSVの読み込みに失敗した場合の例外。ファイル欠損・キー欠損・パース失敗の
    /// いずれもこの例外にする（→ 03 §10.1）。コード側にデフォルト値のフォールバックは
    /// 持たない設計のため、この例外はそのまま起動失敗として上位に伝播させる想定。
    /// </summary>
    public class BalanceDataException : Exception
    {
        public BalanceDataException(string message) : base(message) { }
        public BalanceDataException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// バランス値CSV（正本は docs/04_バランス表/。実行時は実行ファイルと同じディレクトリの
    /// 「04_バランス表」フォルダから読み込む）のローダー。仕様書 03 §10.1（項目58）参照。
    ///
    /// 対応する2形式：
    ///  - key,value,unit,note 形式（大半のファイル）→ GetInt/GetDouble/GetString
    ///  - テーブル形式（quest_templates.csv・growth_job_weights.csv・guild_rank.csv）
    ///    → GetTable（ヘッダー行と各データ行をそのまま返し、変換は呼び出し側のBalanceクラスが行う）
    ///
    /// ファイル欠損・キー欠損・パース失敗はいずれも BalanceDataException とする。
    /// コード側にデフォルト値を残すフォールバックは実装しない（値の二重管理を避けるため。
    /// →「CSVを直したのに変わらない」事故の防止）。数値のパースはカルチャ非依存
    /// （InvariantCulture）で行う。
    ///
    /// 各Balanceクラスは静的フィールドの初期化子（static readonly = BalanceData.GetXxx(...)）
    /// からこのクラスを呼び出す想定。読み込んだファイルはプロセス内でキャッシュする
    /// （同じCSVを複数のBalanceクラスが参照しても、ファイルI/O・パースは1回だけ）。
    /// </summary>
    public static class BalanceData
    {
        private const string FolderName = "04_バランス表";

        private static readonly Lazy<string> DirectoryPath = new(ResolveDirectory);

        // ConcurrentDictionaryを使う理由：各Balanceクラスの静的フィールドはCLRにより
        // 「そのクラスに初めて触れたスレッドが」初期化する。xUnitのテストは既定で
        // クラス単位に並列実行されるため、異なるBalanceクラスの静的初期化が別スレッドで
        // 同時に走り得る（実運用でも将来的な並列アクセスに備える意味で妥当）。
        // 素のDictionaryだと「キャッシュへの書き込みが競合し内部状態が破損する」実害が
        // 実際に発生した（テストで検出済み）。
        private static readonly ConcurrentDictionary<string, Dictionary<string, string>> KeyValueCache = new();
        private static readonly ConcurrentDictionary<string, (string[] Header, List<string[]> Rows)> TableCache = new();

        /// <summary>
        /// CSVフォルダの場所を解決する。「実行ファイルの隣に配置する」（→ 03 §10.1）という
        /// 決定に従い、AppContext.BaseDirectory（dotnet run/test・Godotエディタ実行・
        /// エクスポート後の実行ファイル、いずれの場合もアセンブリの読み込み元ディレクトリを
        /// 指す）を基準にする。見つからない場合は例外にする（フォールバックしない）。
        /// </summary>
        private static string ResolveDirectory()
        {
            string candidate = Path.Combine(AppContext.BaseDirectory, FolderName);
            if (Directory.Exists(candidate))
                return candidate;

            throw new BalanceDataException(
                $"バランス値CSVのフォルダが見つかりません: {candidate}\n" +
                $"実行ファイルと同じ場所に「{FolderName}」フォルダ（CSV一式）を配置してください。" +
                $"（.csprojのContent/CopyToOutputDirectoryで自動配置される想定。→ 03 §10.1）");
        }

        /// <summary>key,value,unit,note 形式のファイルから整数値を読む。</summary>
        public static int GetInt(string fileName, string key)
        {
            string raw = GetRaw(fileName, key);
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                return value;
            throw new BalanceDataException($"{fileName} のキー「{key}」の値「{raw}」を整数として解釈できません。");
        }

        /// <summary>key,value,unit,note 形式のファイルから小数値を読む。</summary>
        public static double GetDouble(string fileName, string key)
        {
            string raw = GetRaw(fileName, key);
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                return value;
            throw new BalanceDataException($"{fileName} のキー「{key}」の値「{raw}」を数値として解釈できません。");
        }

        /// <summary>key,value,unit,note 形式のファイルから文字列値をそのまま読む。</summary>
        public static string GetString(string fileName, string key) => GetRaw(fileName, key);

        private static string GetRaw(string fileName, string key)
        {
            var table = LoadKeyValueFile(fileName);
            if (!table.TryGetValue(key, out var value))
                throw new BalanceDataException($"{fileName} に必須キー「{key}」がありません。");
            return value;
        }

        private static Dictionary<string, string> LoadKeyValueFile(string fileName)
        {
            // GetOrAddのファクトリは競合時に複数回呼ばれ得るが、CSVの読み込み・パースは
            // 副作用の無い純粋な処理のため問題ない（無駄な二重読み込みが起きる程度）。
            return KeyValueCache.GetOrAdd(fileName, name =>
            {
                var lines = ReadNonEmptyLines(name);
                if (lines.Count == 0)
                    throw new BalanceDataException($"{name} が空です（ヘッダー行がありません）。");

                var result = new Dictionary<string, string>();
                // 1行目はヘッダー(key,value,unit,note)なのでスキップする。
                for (int i = 1; i < lines.Count; i++)
                {
                    var fields = ParseCsvLine(lines[i]);
                    if (fields.Length < 2)
                        throw new BalanceDataException($"{name} の{i + 1}行目の形式が不正です（key,valueが読み取れません）: {lines[i]}");
                    result[fields[0]] = fields[1];
                }

                return result;
            });
        }

        /// <summary>
        /// テーブル形式ファイル（quest_templates.csv・growth_job_weights.csv・guild_rank.csv）の
        /// ヘッダーと全データ行を返す。列への意味付け・型変換は呼び出し側のBalanceクラスが行う。
        /// </summary>
        public static (string[] Header, List<string[]> Rows) GetTable(string fileName)
        {
            return TableCache.GetOrAdd(fileName, name =>
            {
                var lines = ReadNonEmptyLines(name);
                if (lines.Count == 0)
                    throw new BalanceDataException($"{name} が空です（ヘッダー行がありません）。");

                var header = ParseCsvLine(lines[0]);
                var rows = new List<string[]>();
                for (int i = 1; i < lines.Count; i++)
                {
                    var fields = ParseCsvLine(lines[i]);
                    if (fields.Length != header.Length)
                        throw new BalanceDataException(
                            $"{name} の{i + 1}行目の列数（{fields.Length}）がヘッダー（{header.Length}列）と一致しません: {lines[i]}");
                    rows.Add(fields);
                }

                return (header, rows);
            });
        }

        private static List<string> ReadNonEmptyLines(string fileName)
        {
            string path = Path.Combine(DirectoryPath.Value, fileName);
            if (!File.Exists(path))
                throw new BalanceDataException($"バランス値CSVファイルが見つかりません: {path}");

            try
            {
                return File.ReadAllLines(path)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToList();
            }
            catch (Exception ex) when (ex is not BalanceDataException)
            {
                throw new BalanceDataException($"バランス値CSVファイルの読み込みに失敗しました: {path}", ex);
            }
        }

        /// <summary>
        /// 簡易CSV1行パーサー。ダブルクォート囲み（内部カンマ・""エスケープ）に対応する
        /// （facility.csvのnote列がmax(a,b)形式のカンマを含むため、"..."で囲まれている）。
        /// </summary>
        private static string[] ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                        inQuotes = true;
                    else if (c == ',')
                    {
                        fields.Add(current.ToString());
                        current.Clear();
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
            }
            fields.Add(current.ToString());
            return fields.ToArray();
        }
    }
}
