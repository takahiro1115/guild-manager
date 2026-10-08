using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 迷宮の異変（→ DungeonAnomaly・GameState.Anomaly、03 §4.10・§0.64）。作業感をなくす構想の②。
    ///
    /// 季節の AnomalyAnnounceWeekOfSeason 週目に1つ予告し、翌週から AnomalyDurationWeeks 週だけ、
    /// 1つのフィールドの計算に倍率をかける。新しい計算は作らず、既存の計算に倍率を差し込む：
    ///  - 瘴気：道中（→ DungeonTraversalResolver）・討伐（→ DungeonResolver）・採取（→ GatheringResolver）のHP消費、採取の素材の個数
    ///  - 遺物の鉱脈：採取の遺物の出る確率（→ GatheringResolver）
    ///  - 霧が晴れる：解析の上がり幅（→ ScoutingResolver。迷宮調査と扉前の偵察）
    ///  - 主の衰え・主の猛り：対象ボスの要求火力（→ DungeonResolver.RequiredPower）、猛りは撃破報酬のゴールドも
    ///    （→ DungeonExpeditionSystem.ApplyFieldProgression）
    ///
    /// 倍率の問い合わせは static（state が null なら常に1.0＝異変なし。既存の呼び出し・テストとの互換）。
    /// 効いているかは state.WeekNumber で判定する（週次決算は WeekNumber を進める前に大迷宮を解決するため、
    /// 出撃した週と決算の週が一致する）。
    /// </summary>
    public static class DungeonAnomalySystem
    {
        /// <summary>そのフィールドで今週効いている異変。無ければnull。</summary>
        public static DungeonAnomaly? GetActive(GameState? state, string fieldId) =>
            state?.Anomaly is { } a && a.FieldId == fieldId && a.IsActive(state.WeekNumber) ? a : null;

        private static bool IsActive(GameState? state, string? fieldId, DungeonAnomalyType type) =>
            fieldId != null && GetActive(state, fieldId)?.Type == type;

        private static string? FieldIdOf(GameState? state, FloorBoss boss) =>
            state?.DungeonFields.FirstOrDefault(f => f.Bosses.Contains(boss))?.Id;

        /// <summary>そのフィールドのHP消費の倍率（瘴気）。</summary>
        public static double DamageMultiplier(GameState? state, string? fieldId) =>
            IsActive(state, fieldId, DungeonAnomalyType.Miasma) ? CommissionBalance.MiasmaDamageMultiplier : 1.0;

        /// <summary>そのボスとの決戦のHP消費の倍率（ボスの所属フィールドの瘴気）。</summary>
        public static double DamageMultiplier(GameState? state, FloorBoss boss) => DamageMultiplier(state, FieldIdOf(state, boss));

        /// <summary>そのフィールドの採取の素材の個数の倍率（瘴気）。</summary>
        public static double MaterialMultiplier(GameState? state, string? fieldId) =>
            IsActive(state, fieldId, DungeonAnomalyType.Miasma) ? CommissionBalance.MiasmaMaterialMultiplier : 1.0;

        /// <summary>そのフィールドの採取の遺物の出る確率の倍率（遺物の鉱脈）。</summary>
        public static double RelicDropMultiplier(GameState? state, string? fieldId) =>
            IsActive(state, fieldId, DungeonAnomalyType.RelicVein) ? CommissionBalance.RelicVeinDropMultiplier : 1.0;

        /// <summary>そのボスの解析の上がり幅の倍率（所属フィールドの霧が晴れる）。</summary>
        public static double IntelMultiplier(GameState? state, FloorBoss boss) =>
            IsActive(state, FieldIdOf(state, boss), DungeonAnomalyType.ClearMist) ? CommissionBalance.ClearMistIntelMultiplier : 1.0;

        /// <summary>そのボスの要求火力の倍率（主の衰え・主の猛りの対象なら）。</summary>
        public static double RequirementMultiplier(GameState? state, FloorBoss boss)
        {
            var a = TargetingAnomaly(state, boss);
            return a?.Type switch
            {
                DungeonAnomalyType.WeakenedLord => CommissionBalance.WeakenedLordRequirementMultiplier,
                DungeonAnomalyType.EnragedLord => CommissionBalance.EnragedLordRequirementMultiplier,
                _ => 1.0,
            };
        }

        /// <summary>そのボスの撃破報酬ゴールドの倍率（主の猛りの対象なら）。</summary>
        public static double BossRewardMultiplier(GameState? state, FloorBoss boss) =>
            TargetingAnomaly(state, boss)?.Type == DungeonAnomalyType.EnragedLord ? CommissionBalance.EnragedLordRewardMultiplier : 1.0;

        private static DungeonAnomaly? TargetingAnomaly(GameState? state, FloorBoss boss)
        {
            var fieldId = FieldIdOf(state, boss);
            var a = fieldId == null ? null : GetActive(state, fieldId);
            return a != null && a.BossId == boss.Id ? a : null;
        }

        // ==================== 予告 ====================

        /// <summary>異変を予告する週か（FirstAnomalyWeek 以降の、季節の AnomalyAnnounceWeekOfSeason 週目）。</summary>
        public static bool IsAnnounceWeek(int week) =>
            week >= CommissionBalance.FirstAnomalyWeek && GameCalendar.WeekOfSeason(week) == CommissionBalance.AnomalyAnnounceWeekOfSeason;

        /// <summary>
        /// 新しい異変を予告して GameState.Anomaly に置く（翌週から効く）。終わった異変はここで置き換わる。
        /// 対象は開いていて、まだ倒していないボスが残っているフィールド。そのようなフィールドが無ければ何もせず null。
        /// 主の衰え・主の猛りは、予告の時点のそのフィールドの次のボスを対象にする。
        /// </summary>
        public static DungeonAnomaly? Announce(GameState state, IRng rng)
        {
            var fields = state.DungeonFields.Where(f => f.IsUnlocked && f.GetNextActiveBoss() != null).OrderBy(f => f.Order).ToList();
            if (fields.Count == 0)
            {
                state.Anomaly = null;
                return null;
            }

            var field = fields[rng.NextInt(0, fields.Count - 1)];
            var types = System.Enum.GetValues<DungeonAnomalyType>();
            var type = types[rng.NextInt(0, types.Length - 1)];
            bool targetsBoss = type is DungeonAnomalyType.WeakenedLord or DungeonAnomalyType.EnragedLord;

            int week = state.WeekNumber;
            var anomaly = new DungeonAnomaly
            {
                Type = type,
                FieldId = field.Id,
                BossId = targetsBoss ? field.GetNextActiveBoss()!.Id : null,
                AnnouncedWeek = week,
                StartWeek = week + 1,
                EndWeek = week + CommissionBalance.AnomalyDurationWeeks,
            };
            state.Anomaly = anomaly;
            return anomaly;
        }

        /// <summary>終わった異変（EndWeek を過ぎた）を片付ける。片付けたら true。</summary>
        public static bool ClearExpired(GameState state)
        {
            if (state.Anomaly == null || state.Anomaly.EndWeek >= state.WeekNumber)
                return false;
            state.Anomaly = null;
            return true;
        }

        // ==================== 表示 ====================

        /// <summary>異変の名前（→ dungeon_anomalies.csv の Name）。</summary>
        public static string GetName(DungeonAnomaly anomaly) => CommissionBalance.GetAnomaly(anomaly.Type).Name;

        /// <summary>予告文（→ dungeon_anomalies.csv の Text に対象を差し込んだもの）。</summary>
        public static string Describe(GameState state, DungeonAnomaly anomaly)
        {
            var field = state.DungeonFields.FirstOrDefault(f => f.Id == anomaly.FieldId);
            var boss = anomaly.BossId == null ? null : field?.Bosses.FirstOrDefault(b => b.Id == anomaly.BossId);
            return Fill(CommissionBalance.GetAnomaly(anomaly.Type).Text, new Dictionary<string, string>
            {
                ["field"] = field?.Name ?? anomaly.FieldId,
                ["boss"] = boss?.Name ?? "",
                ["floor"] = boss?.Floor.ToString() ?? "",
                ["weeks"] = (anomaly.EndWeek - anomaly.StartWeek + 1).ToString(),
            });
        }

        /// <summary>今の状態の短い表記（例：「瘴気（翠緑の原生林、残り3週）」「主の衰え（予告：来週から4週）」）。</summary>
        public static string DescribeStatus(GameState state, DungeonAnomaly anomaly)
        {
            var field = state.DungeonFields.FirstOrDefault(f => f.Id == anomaly.FieldId);
            string where = field?.Name ?? anomaly.FieldId;
            if (anomaly.BossId != null && field?.Bosses.FirstOrDefault(b => b.Id == anomaly.BossId) is { } boss)
                where += $" {boss.Floor}F「{boss.Name}」";
            int week = state.WeekNumber;
            string when = anomaly.IsUpcoming(week)
                ? $"{anomaly.StartWeek - week}週後から{anomaly.EndWeek - anomaly.StartWeek + 1}週"
                : $"残り{anomaly.EndWeek - week + 1}週";
            return $"{GetName(anomaly)}（{where}、{when}）";
        }

        /// <summary>文例の {key} を差し込む（依頼文と共通）。</summary>
        internal static string Fill(string template, IReadOnlyDictionary<string, string> values)
        {
            string text = template;
            foreach (var kv in values)
                text = text.Replace("{" + kv.Key + "}", kv.Value);
            return text;
        }
    }
}
