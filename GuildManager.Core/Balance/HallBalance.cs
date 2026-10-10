using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GuildManager.Core.Balance
{
    /// <summary>ギルドのホールで押せる場所の行き先（→ hall.csv の Target、Systems.HallSystem）。</summary>
    public enum HallTarget
    {
        Squad,
        Dungeon,
        Commission,
        WarRoom,
        Research,
        Infirmary,
        Training,
        Support,
        Tavern,
        Shop,
        Dorm,
        Ledger,
    }

    /// <summary>押せる場所1つ（X・Y・W・H は絵の幅・高さに対する割合。左上が0,0）。Squad は部隊のテーブルのときの部隊の番号（1〜）。</summary>
    public sealed record HallArea(string Id, HallTarget Target, string Label, double X, double Y, double W, double H, int Squad);

    /// <summary>椅子1脚（顔アイコンの中心 X・Y と直径 Size。絵の幅に対する割合）。部隊の番号（1〜）と席の番号（1〜）。</summary>
    public sealed record HallSeat(string Id, double X, double Y, double Size, int Squad, int Seat);

    /// <summary>
    /// ギルドのホール（2026年10月・§0.95、→ docs/04_バランス表/hall.csv、Systems.HallSystem）。
    /// ホールの絵の上の押せる場所（扉・家具・テーブル）と、部隊の子の顔を出す椅子の位置。絵を差し替えたら、この表の位置だけ合わせ直す。
    /// </summary>
    public static class HallBalance
    {
        public const string FileName = "hall.csv";

        private static readonly Lazy<(IReadOnlyList<HallArea> Areas, IReadOnlyList<HallSeat> Seats)> Table = new(() =>
        {
            var (header, rows) = BalanceData.GetTable(FileName);
            return Parse(header, rows);
        });

        public static IReadOnlyList<HallArea> Areas => Table.Value.Areas;
        public static IReadOnlyList<HallSeat> Seats => Table.Value.Seats;

        private static readonly Dictionary<string, HallTarget> Targets = new()
        {
            ["squad"] = HallTarget.Squad, ["dungeon"] = HallTarget.Dungeon, ["commission"] = HallTarget.Commission,
            ["warroom"] = HallTarget.WarRoom, ["research"] = HallTarget.Research, ["infirmary"] = HallTarget.Infirmary,
            ["training"] = HallTarget.Training, ["support"] = HallTarget.Support, ["tavern"] = HallTarget.Tavern,
            ["shop"] = HallTarget.Shop, ["dorm"] = HallTarget.Dorm, ["ledger"] = HallTarget.Ledger,
        };

        /// <summary>hall.csv を読む。書式違反（列・種類・行き先・割合の範囲・部隊と席の番号・重複）は BalanceDataException。</summary>
        public static (IReadOnlyList<HallArea> Areas, IReadOnlyList<HallSeat> Seats) Parse(string[] header, IReadOnlyList<string[]> rows)
        {
            int Col(string name)
            {
                int i = Array.IndexOf(header, name);
                if (i < 0) throw new BalanceDataException($"{FileName} に必須列「{name}」がありません。");
                return i;
            }
            int id = Col("Id"), kind = Col("Kind"), target = Col("Target"), label = Col("Label"),
                x = Col("X"), y = Col("Y"), w = Col("W"), h = Col("H"), squad = Col("Squad"), seat = Col("Seat");

            var areas = new List<HallArea>();
            var seats = new List<HallSeat>();
            var ids = new HashSet<string>();
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                int line = r + 2;
                string rowId = row[id].Trim();
                if (rowId.Length == 0 || !ids.Add(rowId))
                    throw new BalanceDataException($"{FileName} の{line}行目の Id「{rowId}」が空か重複しています。");
                double Num(int col, string name, double max = 1)
                {
                    if (!double.TryParse(row[col].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v < 0 || v > max)
                        throw new BalanceDataException($"{FileName} の{line}行目の {name}「{row[col]}」は0〜{max}の数にしてください。");
                    return v;
                }
                int Int(int col, string name)
                {
                    if (!int.TryParse(row[col].Trim(), out int v) || v < 0)
                        throw new BalanceDataException($"{FileName} の{line}行目の {name}「{row[col]}」は0以上の整数にしてください。");
                    return v;
                }

                switch (row[kind].Trim())
                {
                    case "area":
                        if (!Targets.TryGetValue(row[target].Trim(), out var t))
                            throw new BalanceDataException($"{FileName} の{line}行目の Target「{row[target]}」は {string.Join("・", Targets.Keys)} のどれかにしてください。");
                        double ax = Num(x, "X"), ay = Num(y, "Y"), aw = Num(w, "W"), ah = Num(h, "H");
                        if (ax + aw > 1.0001 || ay + ah > 1.0001 || aw <= 0 || ah <= 0)
                            throw new BalanceDataException($"{FileName} の{line}行目の範囲が絵の外にはみ出しているか、大きさが0です。");
                        int sq = Int(squad, "Squad");
                        if ((t == HallTarget.Squad) != (sq > 0))
                            throw new BalanceDataException($"{FileName} の{line}行目：部隊のテーブル（Target squad）だけ Squad に1以上を書いてください。");
                        areas.Add(new HallArea(rowId, t, row[label].Trim(), ax, ay, aw, ah, sq));
                        break;
                    case "seat":
                        int s = Int(squad, "Squad"), seatNo = Int(seat, "Seat");
                        if (s == 0 || seatNo == 0)
                            throw new BalanceDataException($"{FileName} の{line}行目：椅子には Squad と Seat に1以上を書いてください。");
                        if (seats.Any(e => e.Squad == s && e.Seat == seatNo))
                            throw new BalanceDataException($"{FileName} の{line}行目：部隊{s}の席{seatNo}が重複しています。");
                        double size = Num(w, "W");
                        if (size <= 0)
                            throw new BalanceDataException($"{FileName} の{line}行目：椅子の W（顔の直径）は0より大きくしてください。");
                        seats.Add(new HallSeat(rowId, Num(x, "X"), Num(y, "Y"), size, s, seatNo));
                        break;
                    default:
                        throw new BalanceDataException($"{FileName} の{line}行目の Kind「{row[kind]}」は area か seat にしてください。");
                }
            }
            return (areas, seats.OrderBy(e => e.Squad).ThenBy(e => e.Seat).ToList());
        }
    }
}
