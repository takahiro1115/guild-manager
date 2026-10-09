using System;
using System.Collections.Generic;
using System.Linq;

namespace GuildManager.Core.Balance
{
    /// <summary>物語の画像の種類（→ story_images.csv の Kind）。</summary>
    public enum StoryImageKind
    {
        /// <summary>背景（Name は台詞の〔背景：名前〕＝物語帳の場面の名前）。</summary>
        Background,
        /// <summary>立ち絵（Name は台詞の話者名。| で別名を並べる）。</summary>
        Portrait,
    }

    /// <summary>物語の画像1枚（→ story_images.csv の1行）。File は res://assets/story/ からの相対パス。</summary>
    public sealed record StoryImageDefinition(StoryImageKind Kind, IReadOnlyList<string> Names, string Expression, string File);

    /// <summary>
    /// 物語の会話の小窓に出す背景と立ち絵（2026年10月・§0.91、→ docs/04_バランス表/story_images.csv、Systems.StorySystem.StageOf）。
    /// 正本は物語帳の「場面」と「画像」。行は画像がまだ無くても置いておき、Godot 側はファイルがあるときだけ出す
    /// （画像を作ったら、決めてあるファイル名で assets/story/ に置くだけで出る）。
    /// 立ち絵の表情が無いときは「通常」を使う。
    /// </summary>
    public static class StoryImageBalance
    {
        public const string FileName = "story_images.csv";
        public const string NormalExpression = "通常";

        private static readonly string[] RequiredColumns = { "Kind", "Name", "Expression", "File" };

        private static readonly Lazy<IReadOnlyList<StoryImageDefinition>> AllDefinitions =
            new(() => { var (header, rows) = BalanceData.GetTable(FileName); return Parse(header, rows); });

        /// <summary>story_images.csv の全行（CSVの行順）。</summary>
        public static IReadOnlyList<StoryImageDefinition> All => AllDefinitions.Value;

        /// <summary>背景の名前に決めてあるファイル（無ければ null）。</summary>
        public static string? BackgroundFile(string name) =>
            All.FirstOrDefault(d => d.Kind == StoryImageKind.Background && d.Names.Contains(name))?.File;

        /// <summary>話者の立ち絵のファイル。表情が無ければ「通常」。話者に立ち絵が無ければ null。</summary>
        public static string? PortraitFile(string speaker, string expression = "")
        {
            var own = All.Where(d => d.Kind == StoryImageKind.Portrait && d.Names.Contains(speaker)).ToList();
            string expr = expression.Length > 0 ? expression : NormalExpression;
            return (own.FirstOrDefault(d => d.Expression == expr) ?? own.FirstOrDefault(d => d.Expression == NormalExpression))?.File;
        }

        /// <summary>story_images.csv のヘッダーと行を定義の一覧へ変換する。書式違反は BalanceDataException。</summary>
        public static IReadOnlyList<StoryImageDefinition> Parse(string[] header, IReadOnlyList<string[]> rows)
        {
            var col = RequiredColumns.ToDictionary(name => name, name => RequireColumn(header, name));
            var result = new List<StoryImageDefinition>();
            var keys = new HashSet<string>();
            var files = new HashSet<string>();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                int line = i + 2;
                if (row.Length != header.Length)
                    throw new BalanceDataException($"{FileName} の{line}行目の列数（{row.Length}）がヘッダー（{header.Length}列）と一致しません。");

                var kind = row[col["Kind"]].Trim() switch
                {
                    "background" => StoryImageKind.Background,
                    "portrait" => StoryImageKind.Portrait,
                    var k => throw new BalanceDataException($"{FileName} の{line}行目の Kind「{k}」は background か portrait にしてください。"),
                };
                var names = row[col["Name"]].Split('|').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
                if (names.Count == 0)
                    throw new BalanceDataException($"{FileName} の{line}行目の Name が空です。");
                string expression = row[col["Expression"]].Trim();
                if (kind == StoryImageKind.Portrait && expression.Length == 0)
                    expression = NormalExpression;
                if (kind == StoryImageKind.Background && expression.Length > 0)
                    throw new BalanceDataException($"{FileName} の{line}行目：背景に Expression は付けません。");

                string file = row[col["File"]].Trim();
                if (file.Length == 0)
                    throw new BalanceDataException($"{FileName} の{line}行目の File が空です。");
                if (!file.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '/' or '.') || file.StartsWith('/') || file.Contains(".."))
                    throw new BalanceDataException($"{FileName} の{line}行目の File「{file}」は半角英数字・_・-・/・. だけの相対パスにしてください。");
                if (!files.Add(file))
                    throw new BalanceDataException($"{FileName} の File「{file}」が重複しています（{line}行目）。");

                foreach (var name in names)
                    if (!keys.Add($"{kind}|{name}|{expression}"))
                        throw new BalanceDataException($"{FileName} の「{name}」{(expression.Length > 0 ? $"（{expression}）" : "")}が重複しています（{line}行目）。");

                result.Add(new StoryImageDefinition(kind, names, expression, file));
            }
            return result;
        }

        private static int RequireColumn(string[] header, string name)
        {
            int index = Array.IndexOf(header, name);
            if (index < 0)
                throw new BalanceDataException($"{FileName} に必須列「{name}」がありません。");
            return index;
        }
    }
}
