using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 施設のご褒美による開放（2026年10月・§0.82。§0.79の「倒したボスの数で全施設の上限Lv」を置き換える）。
    /// 施設ごとに「建ててよいLv」（GameState.FacilityUnlockedLevels）を持ち、大会などの成績で開く。建てるには今までどおり改築費と工期が要る。
    ///  - 鍛錬所・学問所・技巧所（部門）：新設＝その部門のG3以上でベスト4（新人戦は得意な部門として数える）／Lv2＝G3以上で優勝／Lv3＝G2以上で優勝／Lv4＝G1でベスト4／Lv5＝G1で優勝（または準優勝2回）
    ///  - 作戦資料室：迷宮踏破杯に出場／ベスト4／準優勝／優勝／2勝（建てるには引退者も要る、§0.78）
    ///  - 冒険者支援室：新人戦でベスト4／準優勝／優勝1〜3回（建てるには引退者も要る）
    ///  - 宿舎：入賞の累計、医務室：倒したボスの数、ギルド酒場：賞金の累計（初めからLv1）
    /// 条件はギルド全体の記録で数える（誰が取っても、何年目でもよい）。一度開いたLvは閉じない。
    /// 開いたときは演出の型（アルベールのひらめき／イザベラの助言／王都からの褒賞）と台詞つきの知らせを返す。
    /// 最初の訓練所は、イザベラとの最初の交流戦のあとに開く（§0.84、→ IsabellaSystem。2年目のはじめの副官の救済は撤去した）。
    /// </summary>
    public static class FacilityUnlockSystem
    {
        public static readonly FacilityType[] TrainingFacilities = { FacilityType.DrillHall, FacilityType.Academy, FacilityType.SkillHall };

        /// <summary>訓練施設が扱う部門。</summary>
        public static TournamentDiscipline DisciplineOf(FacilityType type) => type switch
        {
            FacilityType.DrillHall => TournamentDiscipline.Sword,
            FacilityType.Academy => TournamentDiscipline.Magic,
            _ => TournamentDiscipline.Skill,
        };

        /// <summary>部門の訓練施設。</summary>
        public static FacilityType FacilityOf(TournamentDiscipline d) => d switch
        {
            TournamentDiscipline.Sword => FacilityType.DrillHall,
            TournamentDiscipline.Magic => FacilityType.Academy,
            _ => FacilityType.SkillHall,
        };

        /// <summary>初めから開いているLv（宿舎・医務室・酒場は1、ほかは0）。</summary>
        public static int InitialLevel(FacilityType type) =>
            type is FacilityType.Dormitory or FacilityType.Infirmary or FacilityType.Tavern ? 1 : 0;

        /// <summary>今「建ててよい」Lv。</summary>
        public static int GetUnlockedLevel(GameState state, FacilityType type) =>
            Math.Max(InitialLevel(type), state.FacilityUnlockedLevels.TryGetValue(type, out var level) ? level : 0);

        private static IEnumerable<TournamentRecord> AllRecords(GameState state) =>
            state.Adventurers.Concat(state.RetiredAdventurers).Concat(state.FallenAdventurers).SelectMany(a => a.TournamentRecords);

        /// <summary>記録から求めた、その施設を開いてよいLv（0〜5）。</summary>
        public static int ComputeLevel(GameState state, FacilityType type)
        {
            var records = AllRecords(state).ToList();
            switch (type)
            {
                case FacilityType.DrillHall:
                case FacilityType.Academy:
                case FacilityType.SkillHall:
                {
                    var d = DisciplineOf(type);
                    var mine = records.Where(r => r.Discipline == d).ToList();
                    bool Any(int minRank, int maxPlacing) => mine.Any(r => TournamentSystem.GradeRank(r.Grade) >= minRank && r.Placing <= maxPlacing);
                    if (Any(3, 1) || mine.Count(r => TournamentSystem.GradeRank(r.Grade) >= 3 && r.Placing <= 2) >= 2) return 5;
                    if (Any(3, 4)) return 4;
                    if (Any(2, 1)) return 3;
                    if (Any(1, 1)) return 2;
                    if (Any(1, 4)) return 1;
                    return 0;
                }
                case FacilityType.WarRoom:
                {
                    var events = records.Where(r => r.Discipline == TournamentDiscipline.Party)
                        .GroupBy(r => (r.Year, r.DefinitionId)).Select(g => g.Min(r => r.Placing)).ToList();
                    int wins = events.Count(p => p == 1);
                    if (wins >= 2) return 5;
                    if (wins >= 1) return 4;
                    if (events.Any(p => p <= 2)) return 3;
                    if (events.Any(p => p <= 4)) return 2;
                    return events.Count > 0 ? 1 : 0;
                }
                case FacilityType.RecruitmentOffice:
                {
                    var rookie = records.Where(r => r.Kind == TournamentKind.Rookie).GroupBy(r => r.Year).Select(g => g.Min(r => r.Placing)).ToList();
                    int wins = rookie.Count(p => p == 1);
                    if (wins >= 1) return Math.Min(5, 2 + wins);
                    if (rookie.Any(p => p <= 2)) return 2;
                    return rookie.Any(p => p <= 4) ? 1 : 0;
                }
                case FacilityType.Dormitory:
                    return 1 + TournamentBalance.DormPlacings.Count(t => state.TournamentPlacingsTotal >= t);
                case FacilityType.Tavern:
                    return 1 + TournamentBalance.TavernPrize.Count(t => state.TournamentPrizeTotal >= t);
                case FacilityType.Infirmary:
                    return FacilityBalance.GetLevelCap(EquipmentSystem.CountDefeatedBosses(state));
                default:
                    return InitialLevel(type);
            }
        }

        /// <summary>開いたLvを記録と照らして上げ、上がった施設の知らせを返す（週次決算の最後に呼ぶ）。</summary>
        public static List<FacilityUnlockNotice> Evaluate(GameState state)
        {
            var notices = new List<FacilityUnlockNotice>();
            foreach (var type in Enum.GetValues<FacilityType>())
            {
                int before = GetUnlockedLevel(state, type);
                int target = Math.Min(FacilityBalance.MaxLevel, ComputeLevel(state, type));
                if (target <= before) continue;
                state.FacilityUnlockedLevels[type] = target;
                notices.Add(Notice(state, type, target));
            }
            return notices;
        }

        /// <summary>最初の交流戦のあとに開いた訓練所の知らせ（イザベラの助言。台詞は facility_unlock_lines.csv の Isabella・FirstTraining）。</summary>
        public static FacilityUnlockNotice FirstTrainingNotice(FacilityType type)
        {
            var lines = TournamentBalance.UnlockLines.Where(l => l.Style == "Isabella" && l.Facility == "FirstTraining").Select(l => l.Text).ToList();
            string text = lines.Count > 0 ? lines[(int)type % lines.Count] : "「{facility}から建てなさいな」（イザベラ）";
            return new FacilityUnlockNotice
            {
                Facility = type, Level = 1, Style = "Isabella",
                Line = text.Replace("{facility}", FacilityName(type)).Replace("{discipline}", TournamentSystem.DisciplineLabel(DisciplineOf(type))),
            };
        }

        /// <summary>知らせ：型（訓練所のLv5・作戦資料室のLv4以上＝王都の褒賞、訓練所・医務室＝アルベール、ほか＝イザベラの助言）と台詞。</summary>
        private static FacilityUnlockNotice Notice(GameState state, FacilityType type, int level)
        {
            bool training = TrainingFacilities.Contains(type);
            string style = (training && level == 5) || (type == FacilityType.WarRoom && level >= 4) ? "Royal"
                : training || type == FacilityType.Infirmary ? "Albert" : "Isabella";
            string group = style == "Royal" ? "Any" : training ? "Training" : type.ToString();
            var lines = TournamentBalance.UnlockLines.Where(l => l.Style == style && l.Facility == group).Select(l => l.Text).ToList();
            if (lines.Count == 0)
                lines.Add("「{facility}を{level}まで建てられるようになった」");
            string text = lines[(level + (int)type + state.FacilityUnlockedLevels.Values.Sum()) % lines.Count];
            string eventName = AllRecords(state).Where(r => r.Placing == 1 && r.Grade == TournamentGrade.G1)
                .OrderByDescending(r => r.Year).Select(r => r.Name).FirstOrDefault() ?? "大会";
            string discipline = training ? DisciplineOf(type) switch
            {
                TournamentDiscipline.Sword => "剣筋",
                TournamentDiscipline.Magic => "魔力",
                _ => "技",
            } : "";
            text = text.Replace("{facility}", FacilityName(type)).Replace("{level}", level == 1 ? "新設" : $"Lv{level}")
                .Replace("{discipline}", discipline).Replace("{event}", eventName);
            return new FacilityUnlockNotice { Facility = type, Level = level, Style = style, Line = text };
        }

        /// <summary>開いた知らせの見出し：「学問所を建てられるようになった」「学問所をLv2まで建てられるようになった」（未建設から2段開いたときも読めるように「建てる」で言う）。</summary>
        public static string DescribeUnlock(FacilityUnlockNotice notice) =>
            $"{FacilityName(notice.Facility)}を{(notice.Level == 1 ? "" : $"Lv{notice.Level}まで")}建てられるようになった";

        /// <summary>次のLvを開く条件（施設画面の案内）。開ききっていれば null。</summary>
        public static string? DescribeNext(GameState state, FacilityType type)
        {
            int next = GetUnlockedLevel(state, type) + 1;
            if (next > FacilityBalance.MaxLevel) return null;
            string lv = next == 1 ? "新設" : $"Lv{next}";
            switch (type)
            {
                case FacilityType.DrillHall:
                case FacilityType.Academy:
                case FacilityType.SkillHall:
                {
                    string d = TournamentSystem.DisciplineLabel(DisciplineOf(type));
                    if (next == 1 && !IsabellaSystem.TournamentsOpen(state))
                        return $"{lv}：イザベラとの最初の交流戦のあと、いちばん善戦した部門の訓練所が1つ開く（森の{IsabellaBalance.VisitFloor}Fのボスを倒すとイザベラが来る）";
                    return next switch
                    {
                        1 => $"{lv}：{d}の大会（G3以上・新人戦は得意な部門）でベスト4",
                        2 => $"{lv}：{d}のG3以上で優勝",
                        3 => $"{lv}：{d}のG2以上で優勝",
                        4 => $"{lv}：{d}のG1でベスト4",
                        _ => $"{lv}：{d}のG1で優勝（または準優勝を2回）",
                    };
                }
                case FacilityType.WarRoom:
                    return next switch
                    {
                        1 => $"{lv}：迷宮踏破杯に出場",
                        2 => $"{lv}：迷宮踏破杯でベスト4",
                        3 => $"{lv}：迷宮踏破杯で準優勝",
                        4 => $"{lv}：迷宮踏破杯で優勝",
                        _ => $"{lv}：迷宮踏破杯で2勝",
                    };
                case FacilityType.RecruitmentOffice:
                    return next switch { 1 => $"{lv}：新人戦でベスト4", 2 => $"{lv}：新人戦で準優勝", _ => $"{lv}：新人戦で{next - 2}勝" };
                case FacilityType.Dormitory:
                    return $"{lv}：大会の入賞（ベスト4以上）累計{TournamentBalance.DormPlacings[next - 2]}回（今{state.TournamentPlacingsTotal}回）";
                case FacilityType.Tavern:
                    return $"{lv}：大会の賞金の累計{TournamentBalance.TavernPrize[next - 2]}G（今{state.TournamentPrizeTotal}G）";
                case FacilityType.Infirmary:
                    return $"{lv}：迷宮のボスを{FacilityBalance.GetBossesRequiredForLevel(next)}体倒す（今{EquipmentSystem.CountDefeatedBosses(state)}体）";
                default:
                    return null;
            }
        }

        public static string FacilityName(FacilityType type) => type switch
        {
            FacilityType.Dormitory => "宿舎",
            FacilityType.Infirmary => "医務室",
            FacilityType.Tavern => "ギルド酒場",
            FacilityType.WarRoom => "作戦資料室",
            FacilityType.DrillHall => "鍛錬所",
            FacilityType.Academy => "学問所",
            FacilityType.SkillHall => "技巧所",
            FacilityType.RecruitmentOffice => "冒険者支援室",
            _ => type.ToString(),
        };
    }
}
