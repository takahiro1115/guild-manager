using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>
    /// 固有武具の格（→ uniques.csv `Grade`、03 §4.7.5、2026年9月・§0.45）。
    /// どちらも固有名・固定性能で、1つのセーブにつき1本しか手に入らない（→ GameState.ObtainedUniqueIds）。
    /// </summary>
    public enum UniqueGrade
    {
        /// <summary>固定アーティファクト（紫）。金・虹の遺物の鑑定で武具が出たとき、低確率でこれに化ける。売却できる。</summary>
        Artifact,
        /// <summary>伝説級（金）。特定の階層ボスの初回撃破で確定入手。ボスギミックの対策効果を持ちうる。売却できない。</summary>
        Legendary,
    }

    /// <summary>
    /// 固有武具1種の定義（→ uniques.csv の1行）。
    /// BaseItemId はカタログの武具（→ ItemCatalog）で、装備枠・職業制限・基本性能はそこから引き継ぐ。
    /// StatBonuses／HpBonus は基本性能に**加算**される固有の補正（アフィックスと同じく装備補正として実効値へ入る）。
    /// </summary>
    public sealed record UniqueDefinition(
        string Id,
        UniqueGrade Grade,
        string Name,
        string BaseItemId,
        int HpBonus,
        IReadOnlyDictionary<string, int> StatBonuses,
        BossGimmickType? CounterGimmick,
        string? DropFieldId,
        int DropFloor,
        int SellPrice)
    {
        /// <summary>指定能力値への固有補正（無ければ0）。</summary>
        public int GetStatBonus(string statName) => StatBonuses.TryGetValue(statName, out int v) ? v : 0;

        /// <summary>
        /// 固有補正の短い表記（例：「STR+5・DEX+5・AGI+2」「最大HP+30・VIT+6」）。能力値は7大能力値の表示順。
        /// </summary>
        public string DescribeBonuses()
        {
            var parts = new List<string>();
            if (HpBonus != 0) parts.Add($"最大HP{HpBonus:+0;-0}");
            foreach (var stat in UniqueBalance.StatOrder)
            {
                int v = GetStatBonus(stat);
                if (v != 0) parts.Add($"{stat}{v:+0;-0}");
            }
            return string.Join("・", parts);
        }
    }

    /// <summary>
    /// 固有武具（固定アーティファクト・伝説級）のバランス値（→ 03 §4.7.5、2026年9月・§0.45、ハクスラ Step 3）。
    ///
    ///  - 一覧：docs/04_バランス表/uniques.csv（テーブル形式、列 `Id,Grade,Name,BaseItemId,HpBonus,BonusStr〜BonusLdr,
    ///    CounterGimmick,DropFieldId,DropFloor,SellPrice`）
    ///  - 鑑定で固定アーティファクトへ化ける確率：relic.csv の `ArtifactRate*`（→ RelicBalance.RarityProfile.ArtifactRate）
    ///
    /// 読み込み時に以下を検出し、BalanceDataException で起動失敗にする（フォールバックしない）：
    /// 必須列の欠損・Idの重複・Grade／CounterGimmick の不正値・カタログに無い BaseItemId・数値のパース失敗・
    /// 伝説級なのに入手元（DropFieldId・DropFloor）が無い／階層がボス階層（10刻み・10〜100）でない／
    /// 同じボスに2本割り当てている・固定アーティファクトなのに入手元がある・
    /// 伝説級の SellPrice が0でない（売却不可）・固定アーティファクトの SellPrice が1未満。
    ///
    /// 列はヘッダー名で引く（→ AffixBalance と同じ流儀）。解析処理は <see cref="Parse"/> に分けてあり、
    /// テストから不正な表を直接渡して例外を検証できる。
    /// </summary>
    public static class UniqueBalance
    {
        public const string FileName = "uniques.csv";

        /// <summary>能力値の列と表示順（列名 → 能力値キー）。</summary>
        internal static readonly string[] StatOrder = { "STR", "VIT", "AGI", "DEX", "INT", "MND", "LDR" };

        private static readonly string[] RequiredColumns =
        {
            "Id", "Grade", "Name", "BaseItemId", "HpBonus",
            "BonusStr", "BonusVit", "BonusAgi", "BonusDex", "BonusInt", "BonusMnd", "BonusLdr",
            "CounterGimmick", "DropFieldId", "DropFloor", "SellPrice",
        };

        private static readonly Lazy<IReadOnlyList<UniqueDefinition>> AllDefinitions =
            new(() => { var (header, rows) = BalanceData.GetTable(FileName); return Parse(header, rows); });
        private static readonly Lazy<Dictionary<string, UniqueDefinition>> DefinitionsById =
            new(() => All.ToDictionary(d => d.Id));

        /// <summary>uniques.csv の全固有武具（CSVの行順）。</summary>
        public static IReadOnlyList<UniqueDefinition> All => AllDefinitions.Value;

        /// <summary>指定Idの固有武具。存在しなければnull（CSVから削除された旧セーブの個体など）。</summary>
        public static UniqueDefinition? FindById(string? id) =>
            id != null && DefinitionsById.Value.TryGetValue(id, out var def) ? def : null;

        /// <summary>指定フィールド・階層のボスを初めて倒したときに入手する伝説級。割り当てが無ければnull。</summary>
        public static UniqueDefinition? FindBossDrop(string fieldId, int floor) =>
            All.FirstOrDefault(d => d.Grade == UniqueGrade.Legendary && d.DropFieldId == fieldId && d.DropFloor == floor);

        /// <summary>固定アーティファクトの全件（鑑定の抽選の母集団。CSVの行順）。</summary>
        public static IReadOnlyList<UniqueDefinition> Artifacts => All.Where(d => d.Grade == UniqueGrade.Artifact).ToList();

        // ==================== 読み込み ====================

        /// <summary>uniques.csv のヘッダーと行を定義の一覧へ変換する。書式違反は BalanceDataException。</summary>
        public static IReadOnlyList<UniqueDefinition> Parse(string[] header, IReadOnlyList<string[]> rows)
        {
            var col = RequiredColumns.ToDictionary(name => name, name => RequireColumn(header, name));

            var result = new List<UniqueDefinition>();
            var ids = new HashSet<string>();
            var bossSlots = new HashSet<(string, int)>();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                int line = i + 2;
                if (row.Length != header.Length)
                    throw new BalanceDataException($"{FileName} の{line}行目の列数（{row.Length}）がヘッダー（{header.Length}列）と一致しません。");

                string id = row[col["Id"]].Trim();
                if (id.Length == 0)
                    throw new BalanceDataException($"{FileName} の{line}行目の Id が空です。");
                if (!ids.Add(id))
                    throw new BalanceDataException($"{FileName} のId「{id}」が重複しています（{line}行目）。");

                string rawGrade = row[col["Grade"]].Trim();
                if (!Enum.TryParse<UniqueGrade>(rawGrade, ignoreCase: false, out var grade) || !Enum.IsDefined(grade))
                    throw new BalanceDataException($"{FileName} の{line}行目の Grade「{rawGrade}」は Artifact / Legendary のいずれかにしてください。");

                string name = row[col["Name"]].Trim();
                if (name.Length == 0)
                    throw new BalanceDataException($"{FileName} の{line}行目の Name が空です。");

                string baseItemId = row[col["BaseItemId"]].Trim();
                if (ItemCatalog.FindById(baseItemId) == null)
                    throw new BalanceDataException($"{FileName} の{line}行目の BaseItemId「{baseItemId}」がカタログ（equipment.csv）にありません。");

                int hp = ParseInt(row[col["HpBonus"]], line, "HpBonus");
                var stats = new Dictionary<string, int>();
                foreach (var stat in StatOrder)
                {
                    string column = "Bonus" + stat[0] + stat.Substring(1).ToLowerInvariant();
                    int v = ParseInt(row[col[column]], line, column);
                    if (v != 0) stats[stat] = v;
                }

                BossGimmickType? counter = null;
                string rawCounter = row[col["CounterGimmick"]].Trim();
                if (rawCounter.Length > 0)
                {
                    if (!Enum.TryParse<BossGimmickType>(rawCounter, ignoreCase: false, out var g) || !Enum.IsDefined(g))
                        throw new BalanceDataException(
                            $"{FileName} の{line}行目の CounterGimmick「{rawCounter}」は空欄または {string.Join(" / ", Enum.GetNames<BossGimmickType>())} にしてください。");
                    counter = g;
                }

                string fieldId = row[col["DropFieldId"]].Trim();
                int floor = ParseInt(row[col["DropFloor"]], line, "DropFloor");
                int sellPrice = ParseInt(row[col["SellPrice"]], line, "SellPrice");

                if (grade == UniqueGrade.Legendary)
                {
                    if (fieldId.Length == 0)
                        throw new BalanceDataException($"{FileName} の{line}行目：伝説級には入手元の DropFieldId が必要です。");
                    if (floor < DungeonField.BossInterval || floor > DungeonField.MaxFloor || floor % DungeonField.BossInterval != 0)
                        throw new BalanceDataException(
                            $"{FileName} の{line}行目の DropFloor（{floor}）はボス階層（{DungeonField.BossInterval}刻みで{DungeonField.BossInterval}〜{DungeonField.MaxFloor}）にしてください。");
                    if (!bossSlots.Add((fieldId, floor)))
                        throw new BalanceDataException($"{FileName} の{line}行目：{fieldId} {floor}F のボスには既に伝説級が割り当てられています。");
                    if (sellPrice != 0)
                        throw new BalanceDataException($"{FileName} の{line}行目：伝説級は売却できないため SellPrice は0にしてください。");
                }
                else
                {
                    if (fieldId.Length > 0 || floor != 0)
                        throw new BalanceDataException($"{FileName} の{line}行目：固定アーティファクトは鑑定でのみ出るため DropFieldId は空欄・DropFloor は0にしてください。");
                    if (sellPrice < 1)
                        throw new BalanceDataException($"{FileName} の{line}行目：固定アーティファクトの SellPrice（{sellPrice}）は1以上にしてください。");
                }

                result.Add(new UniqueDefinition(
                    id, grade, name, baseItemId, hp, stats, counter,
                    fieldId.Length == 0 ? null : fieldId, floor, sellPrice));
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

        private static int ParseInt(string raw, int line, string columnName)
        {
            if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                return value;
            throw new BalanceDataException($"{FileName} の{line}行目の{columnName}「{raw}」を整数として解釈できません。");
        }
    }
}
