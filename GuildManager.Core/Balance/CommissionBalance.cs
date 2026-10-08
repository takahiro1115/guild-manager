using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>依頼人1人の定義（→ commission_clients.csv の1行）。</summary>
    public sealed record CommissionClient(
        string Id,
        string Name,
        IReadOnlyList<CommissionType> Types,
        string? BonusMaterialId,
        int BonusMaterialCount,
        string AlbertLine);

    /// <summary>迷宮の異変1種の表示（→ dungeon_anomalies.csv の1行）。</summary>
    public sealed record AnomalyDefinition(DungeonAnomalyType Type, string Name, string Text);

    /// <summary>
    /// 依頼と迷宮の異変（→ Systems.CommissionSystem・DungeonAnomalySystem、03 §4.9・§4.10・§0.64）のバランス値。
    ///
    ///  - 数値：docs/04_バランス表/commissions.csv（key,value,unit,note）
    ///  - 依頼人：commission_clients.csv（Id,Name,Types,BonusMaterialId,BonusMaterialCount,AlbertLine）
    ///  - 依頼文の文例：commission_texts.csv（ClientId,Type,Text。{field}{boss}{floor}{material}{count}{stat}{value}{weeks} を差し込む）
    ///  - 異変の名前と予告文：dungeon_anomalies.csv（Type,Name,Text。{field}{boss}{floor}{weeks} を差し込む）
    /// フォールバックは持たない（キー・列の欠け、不正な種類名、文例の無い依頼人と種類の組は起動失敗）。
    /// </summary>
    public static class CommissionBalance
    {
        private const string FileName = "commissions.csv";
        public const string ClientsFileName = "commission_clients.csv";
        public const string TextsFileName = "commission_texts.csv";
        public const string AnomaliesFileName = "dungeon_anomalies.csv";

        // ==================== 依頼の出方 ====================

        /// <summary>迷宮の異変が起き始める週（1年目の春は遊び方に慣れてもらうため起こさない）。依頼が届き始める週は GameState.CommissionsFromWeek（§0.84）。</summary>
        public static readonly int FirstAnomalyWeek = BalanceData.GetInt(FileName, "FirstAnomalyWeek");

        /// <summary>季節のはじめに届く依頼の数（依頼人は重ならない）。</summary>
        public static readonly int OffersPerSeason = BalanceData.GetInt(FileName, "OffersPerSeason");

        /// <summary>同時に受けられる依頼の数。</summary>
        public static readonly int MaxAccepted = BalanceData.GetInt(FileName, "MaxAccepted");

        /// <summary>期限の残り週数がこの値になった週に、自動スキップを止めて知らせる。</summary>
        public static readonly int DeadlineWarningWeeks = BalanceData.GetInt(FileName, "DeadlineWarningWeeks");

        public static readonly int DeadlineWeeksDefeat = BalanceData.GetInt(FileName, "DeadlineWeeks_Defeat");
        public static readonly int DeadlineWeeksSurvey = BalanceData.GetInt(FileName, "DeadlineWeeks_Survey");
        public static readonly int DeadlineWeeksDeliver = BalanceData.GetInt(FileName, "DeadlineWeeks_Deliver");
        public static readonly int DeadlineWeeksTribute = BalanceData.GetInt(FileName, "DeadlineWeeks_Tribute");

        /// <summary>種類ごとの期限（掲示の週から数えた週数）。</summary>
        public static int GetDeadlineWeeks(CommissionType type) => type switch
        {
            CommissionType.Defeat => DeadlineWeeksDefeat,
            CommissionType.Survey => DeadlineWeeksSurvey,
            CommissionType.Deliver => DeadlineWeeksDeliver,
            _ => DeadlineWeeksTribute,
        };

        // ==================== 条件 ====================

        /// <summary>納品の個数＝DeliverCountBase＋素材の産地の最高到達階層÷DeliverFloorsPerExtra（切り捨て）。</summary>
        public static readonly int DeliverCountBase = BalanceData.GetInt(FileName, "DeliverCountBase");
        public static readonly int DeliverFloorsPerExtra = BalanceData.GetInt(FileName, "DeliverFloorsPerExtra");

        /// <summary>献上の基準：現役の中でその能力が何番目に高い者の値を基準にするか。</summary>
        public static readonly int TributeRank = BalanceData.GetInt(FileName, "TributeRank");

        /// <summary>献上の基準の下限（序盤に低すぎる値にならないように）。</summary>
        public static readonly int TributeMinThreshold = BalanceData.GetInt(FileName, "TributeMinThreshold");

        // ==================== 報酬と罰 ====================

        public static readonly double RewardMultiplierDefeat = BalanceData.GetDouble(FileName, "RewardMultiplier_Defeat");
        public static readonly double RewardMultiplierSurvey = BalanceData.GetDouble(FileName, "RewardMultiplier_Survey");
        public static readonly double RewardMultiplierDeliver = BalanceData.GetDouble(FileName, "RewardMultiplier_Deliver");
        public static readonly double RewardMultiplierTribute = BalanceData.GetDouble(FileName, "RewardMultiplier_Tribute");

        /// <summary>報酬のゴールド＝基準のボスの撃破報酬×この倍率（撃破・解析は対象のボス、納品・献上は攻略の最前線のボス）。</summary>
        public static double GetRewardMultiplier(CommissionType type) => type switch
        {
            CommissionType.Defeat => RewardMultiplierDefeat,
            CommissionType.Survey => RewardMultiplierSurvey,
            CommissionType.Deliver => RewardMultiplierDeliver,
            _ => RewardMultiplierTribute,
        };

        /// <summary>達成したときの機嫌の上昇。</summary>
        public static readonly int CompletionMoodGain = BalanceData.GetInt(FileName, "CompletionMoodGain");

        /// <summary>受けた依頼が期限切れ・達成できなくなったときの機嫌の低下（断った依頼・受けずに流れた依頼は罰なし）。</summary>
        public static readonly int FailureMoodLoss = BalanceData.GetInt(FileName, "FailureMoodLoss");

        /// <summary>報酬の遺物の希少度ロールの上乗せ（→ RelicBalance.RollRarity）。</summary>
        public static readonly int RewardRelicRollBonus = BalanceData.GetInt(FileName, "RewardRelicRollBonus");

        /// <summary>報酬の遺物の最低保証の希少度（Common/Rare/Epic/Legendary）。</summary>
        public static readonly ItemRarity RewardRelicMinRarity = ParseRarity(BalanceData.GetString(FileName, "RewardRelicMinRarity"));

        /// <summary>同じ依頼人の依頼をこの件数達成すると、その依頼人の固有武具（→ uniques.csv の Patron）がもらえる。</summary>
        public static readonly int PatronUniqueCompletions = BalanceData.GetInt(FileName, "PatronUniqueCompletions");

        // ==================== 迷宮の異変 ====================

        /// <summary>季節の何週目に異変を予告するか（翌週から効く）。</summary>
        public static readonly int AnomalyAnnounceWeekOfSeason = BalanceData.GetInt(FileName, "AnomalyAnnounceWeekOfSeason");

        /// <summary>異変の続く週数。</summary>
        public static readonly int AnomalyDurationWeeks = BalanceData.GetInt(FileName, "AnomalyDurationWeeks");

        public static readonly double MiasmaDamageMultiplier = BalanceData.GetDouble(FileName, "Anomaly_MiasmaDamageMultiplier");
        public static readonly double MiasmaMaterialMultiplier = BalanceData.GetDouble(FileName, "Anomaly_MiasmaMaterialMultiplier");
        public static readonly double RelicVeinDropMultiplier = BalanceData.GetDouble(FileName, "Anomaly_RelicVeinDropMultiplier");
        public static readonly double ClearMistIntelMultiplier = BalanceData.GetDouble(FileName, "Anomaly_ClearMistIntelMultiplier");
        public static readonly double WeakenedLordRequirementMultiplier = BalanceData.GetDouble(FileName, "Anomaly_WeakenedLordRequirementMultiplier");
        public static readonly double EnragedLordRequirementMultiplier = BalanceData.GetDouble(FileName, "Anomaly_EnragedLordRequirementMultiplier");
        public static readonly double EnragedLordRewardMultiplier = BalanceData.GetDouble(FileName, "Anomaly_EnragedLordRewardMultiplier");

        // ==================== 表（依頼人・文例・異変） ====================

        private static readonly Lazy<IReadOnlyList<CommissionClient>> ClientList = new(() =>
        {
            var (header, rows) = BalanceData.GetTable(ClientsFileName);
            return ParseClients(header, rows);
        });

        private static readonly Lazy<IReadOnlyDictionary<(string, CommissionType), IReadOnlyList<string>>> TextTable = new(() =>
        {
            var (header, rows) = BalanceData.GetTable(TextsFileName);
            return ParseTexts(header, rows, Clients);
        });

        private static readonly Lazy<IReadOnlyDictionary<DungeonAnomalyType, AnomalyDefinition>> AnomalyTable = new(() =>
        {
            var (header, rows) = BalanceData.GetTable(AnomaliesFileName);
            return ParseAnomalies(header, rows);
        });

        /// <summary>依頼人の一覧（CSVの行順）。</summary>
        public static IReadOnlyList<CommissionClient> Clients => ClientList.Value;

        /// <summary>指定Idの依頼人。無ければnull（CSVから消えた旧セーブの依頼など）。</summary>
        public static CommissionClient? FindClient(string? id) => Clients.FirstOrDefault(c => c.Id == id);

        /// <summary>依頼人と種類の組の文例（1つ以上ある。依頼人が扱う種類には必ず文例がある）。</summary>
        public static IReadOnlyList<string> GetTexts(string clientId, CommissionType type) =>
            TextTable.Value.TryGetValue((clientId, type), out var list) ? list : Array.Empty<string>();

        /// <summary>異変の名前と予告文。</summary>
        public static AnomalyDefinition GetAnomaly(DungeonAnomalyType type) => AnomalyTable.Value[type];

        // ==================== 読み込み ====================

        public static IReadOnlyList<CommissionClient> ParseClients(string[] header, IReadOnlyList<string[]> rows)
        {
            int cId = Require(header, "Id", ClientsFileName), cName = Require(header, "Name", ClientsFileName),
                cTypes = Require(header, "Types", ClientsFileName), cMat = Require(header, "BonusMaterialId", ClientsFileName),
                cCount = Require(header, "BonusMaterialCount", ClientsFileName), cLine = Require(header, "AlbertLine", ClientsFileName);

            var result = new List<CommissionClient>();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                int line = i + 2;
                CheckLength(row, header, line, ClientsFileName);

                string id = row[cId].Trim();
                if (id.Length == 0 || result.Any(c => c.Id == id))
                    throw new BalanceDataException($"{ClientsFileName} の{line}行目の Id「{id}」が空か重複しています。");

                var types = row[cTypes].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(t => ParseType(t, line, ClientsFileName)).Distinct().ToList();
                if (types.Count == 0)
                    throw new BalanceDataException($"{ClientsFileName} の{line}行目の Types が空です（Defeat|Survey|Deliver|Tribute を | で区切る）。");

                string material = row[cMat].Trim();
                int count = ParseInt(row[cCount], line, "BonusMaterialCount", ClientsFileName);
                if (material.Length > 0 && MaterialBalance.Find(material) == null)
                    throw new BalanceDataException($"{ClientsFileName} の{line}行目の BonusMaterialId「{material}」が materials.csv にありません。");

                result.Add(new CommissionClient(id, row[cName].Trim(), types,
                    material.Length == 0 ? null : material, material.Length == 0 ? 0 : count, row[cLine].Trim()));
            }

            if (result.Count == 0)
                throw new BalanceDataException($"{ClientsFileName} に依頼人が1人もいません。");
            return result;
        }

        public static IReadOnlyDictionary<(string, CommissionType), IReadOnlyList<string>> ParseTexts(
            string[] header, IReadOnlyList<string[]> rows, IReadOnlyList<CommissionClient> clients)
        {
            int cClient = Require(header, "ClientId", TextsFileName), cType = Require(header, "Type", TextsFileName),
                cText = Require(header, "Text", TextsFileName);

            var map = new Dictionary<(string, CommissionType), List<string>>();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                int line = i + 2;
                CheckLength(row, header, line, TextsFileName);

                string clientId = row[cClient].Trim();
                if (clients.All(c => c.Id != clientId))
                    throw new BalanceDataException($"{TextsFileName} の{line}行目の ClientId「{clientId}」が {ClientsFileName} にありません。");
                var type = ParseType(row[cType].Trim(), line, TextsFileName);
                string text = row[cText].Trim();
                if (text.Length == 0)
                    throw new BalanceDataException($"{TextsFileName} の{line}行目の Text が空です。");

                if (!map.TryGetValue((clientId, type), out var list))
                    map[(clientId, type)] = list = new List<string>();
                list.Add(text);
            }

            foreach (var client in clients)
                foreach (var type in client.Types)
                    if (!map.ContainsKey((client.Id, type)))
                        throw new BalanceDataException($"{TextsFileName} に依頼人「{client.Id}」の {type} の文例がありません。");

            return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value);
        }

        public static IReadOnlyDictionary<DungeonAnomalyType, AnomalyDefinition> ParseAnomalies(string[] header, IReadOnlyList<string[]> rows)
        {
            int cType = Require(header, "Type", AnomaliesFileName), cName = Require(header, "Name", AnomaliesFileName),
                cText = Require(header, "Text", AnomaliesFileName);

            var map = new Dictionary<DungeonAnomalyType, AnomalyDefinition>();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                int line = i + 2;
                CheckLength(row, header, line, AnomaliesFileName);

                string raw = row[cType].Trim();
                if (!Enum.TryParse<DungeonAnomalyType>(raw, ignoreCase: false, out var type) || !Enum.IsDefined(type))
                    throw new BalanceDataException(
                        $"{AnomaliesFileName} の{line}行目の Type「{raw}」は {string.Join(" / ", Enum.GetNames<DungeonAnomalyType>())} のいずれかにしてください。");
                if (map.ContainsKey(type))
                    throw new BalanceDataException($"{AnomaliesFileName} の{line}行目：{type} が重複しています。");
                map[type] = new AnomalyDefinition(type, row[cName].Trim(), row[cText].Trim());
            }

            foreach (var type in Enum.GetValues<DungeonAnomalyType>())
                if (!map.ContainsKey(type))
                    throw new BalanceDataException($"{AnomaliesFileName} に {type} の行がありません。");
            return map;
        }

        private static CommissionType ParseType(string raw, int line, string fileName)
        {
            if (Enum.TryParse<CommissionType>(raw, ignoreCase: false, out var type) && Enum.IsDefined(type))
                return type;
            throw new BalanceDataException(
                $"{fileName} の{line}行目の種類「{raw}」は {string.Join(" / ", Enum.GetNames<CommissionType>())} のいずれかにしてください。");
        }

        private static ItemRarity ParseRarity(string raw)
        {
            if (Enum.TryParse<ItemRarity>(raw.Trim(), ignoreCase: false, out var rarity) && Enum.IsDefined(rarity))
                return rarity;
            throw new BalanceDataException(
                $"{FileName} の RewardRelicMinRarity「{raw}」は {string.Join(" / ", Enum.GetNames<ItemRarity>())} のいずれかにしてください。");
        }

        private static int Require(string[] header, string name, string fileName)
        {
            int index = Array.IndexOf(header, name);
            if (index < 0)
                throw new BalanceDataException($"{fileName} に必須列「{name}」がありません。");
            return index;
        }

        private static void CheckLength(string[] row, string[] header, int line, string fileName)
        {
            if (row.Length != header.Length)
                throw new BalanceDataException($"{fileName} の{line}行目の列数（{row.Length}）がヘッダー（{header.Length}列）と一致しません。");
        }

        private static int ParseInt(string raw, int line, string column, string fileName)
        {
            if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                return value;
            throw new BalanceDataException($"{fileName} の{line}行目の{column}「{raw}」を整数として解釈できません。");
        }
    }
}
