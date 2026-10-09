using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Models;

namespace GuildManager.Core.Balance
{
    /// <summary>ライバルギルド1つの定義（→ rival_guilds.csv の1行）。</summary>
    public sealed record RivalGuildDefinition(string Id, string Name, TournamentDiscipline Specialty, string Master);

    /// <summary>名簿を作るときに必ず入る看板の子（→ rival_members.csv の1行）。</summary>
    public sealed record RivalMemberDefinition(string GuildId, string Name, TournamentDiscipline Discipline, int Age, double Peak, string Epithet);

    /// <summary>
    /// ライバルギルドのバランス値（2026年10月・§0.93、大会と育成の栄光 段3-1、→ docs/04_バランス表/rivals.csv・rival_guilds.csv・rival_members.csv、
    /// Systems.RivalSystem）。強さは大会の相手の強さ（tournaments.csv の MinStrength・MaxStrength）と同じ尺度。
    /// </summary>
    public static class RivalBalance
    {
        private const string FileName = "rivals.csv";
        public const string GuildsFileName = "rival_guilds.csv";
        public const string MembersFileName = "rival_members.csv";

        public static readonly int RosterSize = BalanceData.GetInt(FileName, "RosterSize");
        public static readonly int SpecialtyCount = BalanceData.GetInt(FileName, "SpecialtyCount");
        public static readonly int StartAgeMin = BalanceData.GetInt(FileName, "StartAgeMin");
        public static readonly int StartAgeMax = BalanceData.GetInt(FileName, "StartAgeMax");
        public static readonly int RookieAge = BalanceData.GetInt(FileName, "RookieAge");
        public static readonly double PeakMin = BalanceData.GetDouble(FileName, "PeakMin");
        public static readonly double PeakMax = BalanceData.GetDouble(FileName, "PeakMax");
        public static readonly int RecordMinPlacing = BalanceData.GetInt(FileName, "RecordMinPlacing");

        /// <summary>引退する年齢（この年齢の年度末で引退。自分のギルドと同じ aging.csv の RetirementAge）。</summary>
        public static int RetirementAge => Systems.AgingSystem.RetirementAge;

        private static readonly Lazy<Dictionary<int, double>> Curve = new(() =>
            Enumerable.Range(RookieAge, RetirementAge - RookieAge + 1)
                .ToDictionary(age => age, age => BalanceData.GetDouble(FileName, $"AgeCurve_{age}")));

        /// <summary>年齢の伸び（全盛＝1）。表より若ければ最初の値、上なら最後の値。</summary>
        public static double AgeCurve(int age)
        {
            var c = Curve.Value;
            return c[Math.Clamp(age, c.Keys.Min(), c.Keys.Max())];
        }

        private static readonly Lazy<Dictionary<TournamentDiscipline, string[]>> EpithetTable = new(() => new()
        {
            [TournamentDiscipline.Sword] = SplitList(BalanceData.GetString(FileName, "Epithets_Sword")),
            [TournamentDiscipline.Magic] = SplitList(BalanceData.GetString(FileName, "Epithets_Magic")),
            [TournamentDiscipline.Skill] = SplitList(BalanceData.GetString(FileName, "Epithets_Skill")),
        });

        /// <summary>初めてG1を勝ったライバルに付ける二つ名の候補（部門ごと）。</summary>
        public static IReadOnlyList<string> Epithets(TournamentDiscipline d) =>
            EpithetTable.Value.TryGetValue(d, out var list) ? list : Array.Empty<string>();

        private static string[] SplitList(string raw) => raw.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

        private static readonly Lazy<IReadOnlyList<RivalGuildDefinition>> GuildTable = new(() =>
        {
            var (header, rows) = BalanceData.GetTable(GuildsFileName);
            return ParseGuilds(header, rows);
        });

        private static readonly Lazy<IReadOnlyList<RivalMemberDefinition>> MemberTable = new(() =>
        {
            var (header, rows) = BalanceData.GetTable(MembersFileName);
            return ParseMembers(header, rows, Guilds);
        });

        /// <summary>ライバルギルド（CSVの行順）。</summary>
        public static IReadOnlyList<RivalGuildDefinition> Guilds => GuildTable.Value;

        /// <summary>看板の子（CSVの行順）。</summary>
        public static IReadOnlyList<RivalMemberDefinition> FixedMembers => MemberTable.Value;

        /// <summary>rival_guilds.csv を読む。書式違反は BalanceDataException。</summary>
        public static IReadOnlyList<RivalGuildDefinition> ParseGuilds(string[] header, IReadOnlyList<string[]> rows)
        {
            int id = Column(header, "Id", GuildsFileName), name = Column(header, "Name", GuildsFileName),
                spec = Column(header, "Specialty", GuildsFileName), master = Column(header, "Master", GuildsFileName);
            var result = new List<RivalGuildDefinition>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                string gid = r[id].Trim();
                if (gid.Length == 0 || r[name].Trim().Length == 0)
                    throw new BalanceDataException($"{GuildsFileName} の{i + 2}行目の Id か Name が空です。");
                if (result.Any(g => g.Id == gid))
                    throw new BalanceDataException($"{GuildsFileName} の Id「{gid}」が重複しています。");
                result.Add(new RivalGuildDefinition(gid, r[name].Trim(), ParseDiscipline(r[spec], GuildsFileName, i + 2), r[master].Trim()));
            }
            return result;
        }

        /// <summary>rival_members.csv を読む。ギルドの Id は rival_guilds.csv にあること。</summary>
        public static IReadOnlyList<RivalMemberDefinition> ParseMembers(string[] header, IReadOnlyList<string[]> rows, IReadOnlyList<RivalGuildDefinition> guilds)
        {
            int gid = Column(header, "GuildId", MembersFileName), name = Column(header, "Name", MembersFileName),
                disc = Column(header, "Discipline", MembersFileName), age = Column(header, "Age", MembersFileName),
                peak = Column(header, "Peak", MembersFileName), epithet = Column(header, "Epithet", MembersFileName);
            var result = new List<RivalMemberDefinition>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                int line = i + 2;
                string g = r[gid].Trim();
                if (guilds.All(x => x.Id != g))
                    throw new BalanceDataException($"{MembersFileName} の{line}行目のギルド「{g}」が {GuildsFileName} にありません。");
                if (!int.TryParse(r[age].Trim(), out int a) || !double.TryParse(r[peak].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double p))
                    throw new BalanceDataException($"{MembersFileName} の{line}行目の Age か Peak が数ではありません。");
                if (r[name].Trim().Length == 0)
                    throw new BalanceDataException($"{MembersFileName} の{line}行目の Name が空です。");
                result.Add(new RivalMemberDefinition(g, r[name].Trim(), ParseDiscipline(r[disc], MembersFileName, line), a, p, r[epithet].Trim()));
            }
            return result;
        }

        private static TournamentDiscipline ParseDiscipline(string raw, string file, int line) => raw.Trim() switch
        {
            "Sword" => TournamentDiscipline.Sword,
            "Magic" => TournamentDiscipline.Magic,
            "Skill" => TournamentDiscipline.Skill,
            var s => throw new BalanceDataException($"{file} の{line}行目の部門「{s}」は Sword・Magic・Skill のどれかにしてください。"),
        };

        private static int Column(string[] header, string name, string file)
        {
            int index = Array.IndexOf(header, name);
            if (index < 0)
                throw new BalanceDataException($"{file} に必須列「{name}」がありません。");
            return index;
        }
    }
}
