using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 台詞（2026年10月・§0.86、→ docs/04_バランス表/story/*.txt、Systems.StorySystem）。正本はアーティファクト「ギルド物語帳」で、
    /// そこから書き出したテキストを読む。書式はファイルの先頭の注記のとおり。
    /// </summary>
    public static class StoryBalance
    {
        public const string Folder = "story";

        private static readonly Lazy<IReadOnlyDictionary<string, StoryScene>> SceneTable = new(() =>
        {
            var scenes = new Dictionary<string, StoryScene>();
            foreach (var (file, lines) in BalanceData.ReadTextFiles(Folder, "*.txt"))
                foreach (var scene in Parse(lines, file))
                {
                    if (!scenes.TryAdd(scene.Id, scene))
                        throw new BalanceDataException($"{Folder}/{file} の場面「{scene.Id}」が重複しています。");
                }
            return scenes;
        });

        /// <summary>すべての場面（Id→場面）。</summary>
        public static IReadOnlyDictionary<string, StoryScene> Scenes => SceneTable.Value;

        /// <summary>指定Idの場面。無ければ例外（場面のIdはコードと台詞ファイルで揃える）。</summary>
        public static StoryScene Get(string id) =>
            Scenes.TryGetValue(id, out var scene) ? scene : throw new BalanceDataException($"台詞ファイルに場面「{id}」がありません（{Folder}/*.txt）。");

        private static readonly Regex SpeechPattern = new(@"^(?<name>[^「〔【]+?)(（(?<expr>[^）]*)）)?「(?<text>.*)」$");

        /// <summary>台詞ファイル1つを場面に分ける（テストからも呼ぶ）。</summary>
        public static List<StoryScene> Parse(IEnumerable<string> lines, string fileName = "")
        {
            var result = new List<StoryScene>();
            string? id = null, title = "";
            bool variants = false;
            var pages = new List<List<StoryLine>>();
            int lineNo = 0;

            void Flush()
            {
                if (id == null) return;
                pages.RemoveAll(p => p.Count == 0);
                if (pages.Count == 0)
                    throw new BalanceDataException($"{Folder}/{fileName} の場面「{id}」に台詞がありません。");
                result.Add(new StoryScene { Id = id, Title = title, Pages = pages, Variants = variants });
            }

            foreach (var raw in lines)
            {
                lineNo++;
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";")) continue;
                if (line.StartsWith("## "))
                {
                    Flush();
                    string head = line[3..].Trim();
                    int sep = head.IndexOfAny(new[] { '　', ' ' });
                    id = sep < 0 ? head : head[..sep];
                    title = sep < 0 ? "" : head[(sep + 1)..].Trim();
                    variants = false;
                    pages = new List<List<StoryLine>> { new() };
                    continue;
                }
                if (id == null)
                    throw new BalanceDataException($"{Folder}/{fileName} の{lineNo}行目：「## 場面のId」より前に台詞があります。");
                if (line == "@variants") { variants = true; continue; }
                if (line.StartsWith("▼")) { pages.Add(new List<StoryLine>()); continue; }
                pages[^1].Add(ParseLine(line, fileName, lineNo));
            }
            Flush();
            return result;
        }

        private static StoryLine ParseLine(string line, string fileName, int lineNo)
        {
            if (line.StartsWith("〔背景：") && line.EndsWith("〕"))
                return new StoryLine(StoryLineKind.Background, line[4..^1].Trim());
            if (line.StartsWith("〔") && line.EndsWith("〕"))
                return new StoryLine(StoryLineKind.Narration, line[1..^1]);
            if (line.StartsWith("【画面へ】"))
                return new StoryLine(StoryLineKind.Jump, line["【画面へ】".Length..].Trim());
            if (line.StartsWith("【手引き（説明）】"))
                return new StoryLine(StoryLineKind.GuideNote, line["【手引き（説明）】".Length..].Trim());
            if (line.StartsWith("【手引き】"))
                return new StoryLine(StoryLineKind.Guide, line["【手引き】".Length..].TrimStart('□', ' ', '　').Trim());
            var m = SpeechPattern.Match(line);
            if (m.Success)
                return new StoryLine(StoryLineKind.Speech, m.Groups["text"].Value, m.Groups["name"].Value.Trim(), m.Groups["expr"].Value);
            throw new BalanceDataException($"{Folder}/{fileName} の{lineNo}行目を読めません（名前「…」・〔…〕・【画面へ】・【手引き】のどれでもない）：{line}");
        }
    }
}
