using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 年末のギルドの順位表（2026年10月・§0.94、大会と育成の栄光 段3-2、→ RivalBalance の Standing*）。
    /// 自分のギルドと3つのライバルギルドを、その年の大会（交流戦は除く）の順位と迷宮の撃破から栄誉点で並べる。
    /// 同点なら G1 の勝ち数 → 賞金 → 自分のギルドが上。年の最後の週の決算で確定し、1位・2位にご褒美。
    /// 自分のギルドは大会の結果（TournamentResult.Placings）から、ライバルは戦績（RivalMember.Records）から数える。
    /// </summary>
    public static class GuildStandingSystem
    {
        /// <summary>自分のギルドの名前（順位表の表示）。</summary>
        public const string OurGuildName = "当ギルド";

        /// <summary>
        /// 週の処理：年の最後の週で、ライバルの名簿があれば、その年の順位表を確定してご褒美を与える。確定した表を返す（ほかの週は null）。
        /// 同じ年を二度確定しない。
        /// </summary>
        public static GuildStanding? ProcessWeek(GameState state)
        {
            if (!GameCalendar.IsLastWeekOfYear(state.WeekNumber) || state.RivalGuilds.Count == 0)
                return null;
            int year = GameCalendar.YearOf(state.WeekNumber);
            if (state.GuildStandings.Any(s => s.Year == year))
                return null;
            var standing = Compute(state, year);
            if (standing.Rows.FirstOrDefault(r => r.Ours) is { } ours)
            {
                var (gold, mood) = RivalBalance.StandingReward(ours.Rank);
                ours.RewardGold = gold;
                ours.RewardMood = mood;
                state.Gold += gold;
                if (mood != 0) MasterMoodSystem.Adjust(state, mood);
            }
            state.GuildStandings.Add(standing);
            return standing;
        }

        /// <summary>その年の順位表（途中経過にも使う。ご褒美は付けない）。</summary>
        public static GuildStanding Compute(GameState state, int year)
        {
            var rows = new List<GuildStandingRow>();

            var ours = new GuildStandingRow { Name = OurGuildName, Ours = true };
            foreach (var ev in state.TournamentEvents.Where(e => e.Year == year && e.Kind != TournamentKind.Exchange && e.Result != null))
                foreach (var p in ev.Result!.Placings)
                    Add(ours, ev.Kind, ev.Grade, p.Placing, p.Prize);
            ours.BossKills = state.BossKillsByYear.GetValueOrDefault(year);
            ours.Points += ours.BossKills * RivalBalance.StandingPointsPerBoss;
            rows.Add(ours);

            foreach (var guild in state.RivalGuilds)
            {
                var row = new GuildStandingRow { GuildId = guild.Id, Name = guild.Name };
                var members = guild.Members.Concat(state.RetiredRivals.Where(m => state.RetiredRivalGuilds.GetValueOrDefault(m.Id) == guild.Id));
                foreach (var r in members.SelectMany(m => m.Records).Where(r => r.Year == year && r.Kind != TournamentKind.Exchange))
                    Add(row, r.Kind, r.Grade, r.Placing, PrizeOf(r.DefinitionId, r.Placing));
                rows.Add(row);
            }

            var ordered = rows.OrderByDescending(r => r.Points).ThenByDescending(r => r.G1Wins).ThenByDescending(r => r.Prize).ThenByDescending(r => r.Ours).ToList();
            for (int i = 0; i < ordered.Count; i++)
                ordered[i].Rank = i + 1;
            return new GuildStanding { Year = year, Rows = ordered };
        }

        private static void Add(GuildStandingRow row, TournamentKind kind, TournamentGrade grade, int placing, int prize)
        {
            row.Points += PlacingPoints(kind, grade, placing);
            row.Prize += prize;
            if (placing == 1)
            {
                row.Wins++;
                if (grade == TournamentGrade.G1) row.G1Wins++;
            }
        }

        /// <summary>
        /// 大会の順位の栄誉点：優勝は 王都最強決定戦15・G1 10・G2と招待4・G3 2、準優勝はその半分、ベスト4は4分の1（どちらも切り上げ）。
        /// </summary>
        public static int PlacingPoints(TournamentKind kind, TournamentGrade grade, int placing)
        {
            int win = kind == TournamentKind.Final ? RivalBalance.StandingPointsFinal
                : grade switch
                {
                    TournamentGrade.G1 => RivalBalance.StandingPointsG1,
                    TournamentGrade.G2 or TournamentGrade.Special => RivalBalance.StandingPointsG2,
                    _ => RivalBalance.StandingPointsG3,
                };
            return placing switch
            {
                1 => win,
                2 => (int)Math.Ceiling(win * RivalBalance.StandingRunnerUpRatio),
                <= 4 => (int)Math.Ceiling(win * RivalBalance.StandingTop4Ratio),
                _ => 0,
            };
        }

        /// <summary>ライバルの賞金（大会の定義の、その順位の賞金）。</summary>
        private static int PrizeOf(string definitionId, int placing)
        {
            var def = TournamentBalance.Find(definitionId);
            return def == null ? 0 : placing switch { 1 => def.Prize1, 2 => def.Prize2, 4 => def.Prize4, _ => 0 };
        }
    }
}
