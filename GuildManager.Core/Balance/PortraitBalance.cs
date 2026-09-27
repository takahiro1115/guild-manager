using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 冒険者の顔グラフィック（ポートレート）1枚の定義（→ portraits.csv の1行）。
    /// Id は画像ファイル名（拡張子なし）で、Godot側は `res://assets/portraits/{Id}.png` を読む（→ Adventurer.PortraitId）。
    /// Jobs はこの絵が似合う職業（採用時の割り当てで優先する）。HairColor・EyeColor は表示・今後の色違い生成用のタグ。
    /// </summary>
    public sealed record PortraitDefinition(string Id, IReadOnlyList<JobClass> Jobs, string HairColor, string EyeColor)
    {
        public bool Suits(JobClass job) => Jobs.Contains(job);
    }

    /// <summary>
    /// 採用で生まれる冒険者に割り当てる顔グラフィックのプール（→ 03 §2.1、2026年9月・§0.46）。
    ///
    ///  - 一覧：docs/04_バランス表/portraits.csv（テーブル形式、列 `Id,Jobs,HairColor,EyeColor`）
    ///  - Jobs は `|` 区切りの職業名（JobClass の名前）または `All`（全職業）
    ///
    /// 初期メンバー（クラウディア・リナ・フィオナ）の専用画像はプールに入れない（→ SampleData が直接指定する）。
    /// 画像を足すときは assets/portraits/ にPNGを置き、この表に1行足すだけでよい。行が0件でも起動は失敗しない
    /// （全員シルエット表示になるだけ）。
    ///
    /// 読み込み時に以下を検出し、BalanceDataException で起動失敗にする：必須列の欠損・列数不一致・Idの空欄と重複・
    /// ファイル名に使えない文字を含むId・Jobs の空欄と不正な職業名。
    /// </summary>
    public static class PortraitBalance
    {
        public const string FileName = "portraits.csv";

        private static readonly string[] RequiredColumns = { "Id", "Jobs", "HairColor", "EyeColor" };

        private static readonly Lazy<IReadOnlyList<PortraitDefinition>> AllDefinitions =
            new(() => { var (header, rows) = BalanceData.GetTable(FileName); return Parse(header, rows); });

        /// <summary>portraits.csv の全画像（CSVの行順）。</summary>
        public static IReadOnlyList<PortraitDefinition> All => AllDefinitions.Value;

        /// <summary>指定Idの画像定義。無ければnull。</summary>
        public static PortraitDefinition? FindById(string? id) =>
            id == null ? null : All.FirstOrDefault(p => p.Id == id);

        /// <summary>portraits.csv のヘッダーと行を定義の一覧へ変換する。書式違反は BalanceDataException。</summary>
        public static IReadOnlyList<PortraitDefinition> Parse(string[] header, IReadOnlyList<string[]> rows)
        {
            var col = RequiredColumns.ToDictionary(name => name, name => RequireColumn(header, name));

            var result = new List<PortraitDefinition>();
            var ids = new HashSet<string>();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                int line = i + 2;
                if (row.Length != header.Length)
                    throw new BalanceDataException($"{FileName} の{line}行目の列数（{row.Length}）がヘッダー（{header.Length}列）と一致しません。");

                string id = row[col["Id"]].Trim();
                if (id.Length == 0)
                    throw new BalanceDataException($"{FileName} の{line}行目の Id が空です。");
                if (!id.All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-'))
                    throw new BalanceDataException($"{FileName} の{line}行目の Id「{id}」は半角英数字・_・- だけにしてください（画像ファイル名になるため）。");
                if (!ids.Add(id))
                    throw new BalanceDataException($"{FileName} のId「{id}」が重複しています（{line}行目）。");

                var jobs = ParseJobs(row[col["Jobs"]], line);
                result.Add(new PortraitDefinition(id, jobs, row[col["HairColor"]].Trim(), row[col["EyeColor"]].Trim()));
            }
            return result;
        }

        private static IReadOnlyList<JobClass> ParseJobs(string raw, int line)
        {
            string trimmed = raw.Trim();
            if (trimmed == "All")
                return Enum.GetValues<JobClass>();

            var jobs = new List<JobClass>();
            foreach (var token in trimmed.Split('|').Select(t => t.Trim()))
            {
                if (!Enum.TryParse<JobClass>(token, ignoreCase: false, out var job) || !Enum.IsDefined(job))
                    throw new BalanceDataException(
                        $"{FileName} の{line}行目の Jobs「{raw}」は All または {string.Join(" / ", Enum.GetNames<JobClass>())} の | 区切りにしてください。");
                jobs.Add(job);
            }
            return jobs.Distinct().ToList();
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
