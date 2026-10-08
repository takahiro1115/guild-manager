using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// エンディングのあと（2026年10月・§0.90、→ 物語帳「ゲームの決まりが変わる点」）：
    ///  - 倒した主が記憶から蘇る：クリアのあと、倒してから RevivalWeeks（1年）で蘇る（HPは満タン、解析は残る）。クリアの前に倒した主はクリアの週から数える。
    ///  - 迷宮の異変を穏やかにする：クリアのあとは PostClearAnomalySeasons（春と秋）だけ予告する（→ CommissionSystem.ProcessNewWeek）。
    ///  - ルミナが冒険者の名簿に加わる：エンディングの「初出撃」を見たら（クリア済みの旧セーブも）。引退しない斥候で、強さは抑えない。
    ///  - 50年目の立ち絵：クリアしていて、ゲーム開始から LuminaGrownYear 年目になったら、ルミナの立ち絵を「少し成長した姿」に替える。
    /// </summary>
    public static class PostGameSystem
    {
        public const string LuminaId = "lumina";

        /// <summary>ルミナ（名簿・除籍者のどこかにいれば。いなければ null）。</summary>
        public static Adventurer? Lumina(GameState state) =>
            state.Adventurers.Concat(state.FallenAdventurers).FirstOrDefault(a => a.StoryCharacterId == LuminaId);

        /// <summary>
        /// 週の決算のあと（新しい週）：蘇る主を蘇らせ、ルミナを加え、ルミナの立ち絵を年に合わせる。蘇った主を返す。
        /// </summary>
        public static List<FloorBoss> ProcessWeek(GameState state)
        {
            var revived = new List<FloorBoss>();
            if (!state.IsGameCleared)
                return revived;

            int clearedAt = state.ClearedAtWeek ?? state.WeekNumber;
            foreach (var boss in state.DungeonFields.SelectMany(f => f.Bosses).Where(b => b.IsDefeated))
            {
                int from = Math.Max(boss.LastDefeatedWeek ?? clearedAt, clearedAt);
                if (state.WeekNumber < from + PostGameBalance.RevivalWeeks)
                    continue;
                boss.DefeatCount = System.Math.Max(1, boss.DefeatCount); // 回数の記録の無い旧セーブの主も「一度倒した」を残す
                boss.IsDefeated = false;
                boss.CurrentHp = boss.MaxHp;
                revived.Add(boss);
            }

            EnsureLumina(state);
            if (Lumina(state) is { } lumina)
                lumina.PortraitId = LuminaPortraitId(state);
            return revived;
        }

        /// <summary>クリアのあと、異変を予告してよい季節か（§0.90：春と秋だけ）。クリアの前はいつでも。</summary>
        public static bool AnomalySeasonAllowed(GameState state, int week) =>
            !state.IsGameCleared || PostGameBalance.PostClearAnomalySeasons.Contains(GameCalendar.SeasonOf(week));

        /// <summary>ルミナの立ち絵（クリアしていて50年目からは少し成長した姿）。</summary>
        public static string LuminaPortraitId(GameState state) =>
            state.IsGameCleared && GameCalendar.YearOf(state.WeekNumber) >= PostGameBalance.LuminaGrownYear
                ? PostGameBalance.LuminaGrownPortraitId
                : PostGameBalance.LuminaPortraitId;

        /// <summary>クリアしていて、エンディングの「初出撃」を見ていれば、ルミナを名簿に加える（まだいなければ）。加えたら true。</summary>
        public static bool EnsureLumina(GameState state)
        {
            if (!state.IsGameCleared || !StorySystem.Seen(state, "s04_sortie") || Lumina(state) != null)
                return false;
            state.Adventurers.Add(CreateLumina(state));
            return true;
        }

        /// <summary>
        /// ルミナを作る：斥候、能力は今の現役で各能力がいちばん高い子の値（強さは抑えない）、PAは LuminaPa、特性は夜目・鷹の目・危機察知。
        /// 年齢は救出のときの14歳に救出からの年数を足す。週給0・満足度100。
        /// </summary>
        public static Adventurer CreateLumina(GameState state)
        {
            int rescued = state.StorySeenWeeks.TryGetValue("s01_lumina", out int w) && w > 0 ? w : 1;
            var lumina = new Adventurer
            {
                Name = PostGameBalance.LuminaName,
                JobClass = PostGameBalance.LuminaJob,
                Age = PostGameBalance.LuminaRescueAge + Math.Max(0, state.WeekNumber - rescued) / GameCalendar.WeeksPerYear,
                JoinedYear = GameCalendar.YearOf(state.WeekNumber),
                StoryCharacterId = LuminaId,
                WeeklyWage = 0,
                Satisfaction = 100,
                PortraitId = LuminaPortraitId(state),
            };
            var active = state.Adventurers.Where(a => !a.IsRetired).ToList();
            foreach (var stat in AdventurerStatAccessor.AllStatNames)
            {
                int best = active.Select(a => AdventurerStatAccessor.GetStat(a, stat)).DefaultIfEmpty(50).Max();
                AdventurerStatAccessor.SetPa(lumina, stat, Math.Max(PostGameBalance.LuminaPa, best));
                AdventurerStatAccessor.SetStat(lumina, stat, best);
            }
            foreach (var trait in PostGameBalance.LuminaTraits.Where(t => TraitCatalog.FindById(t) != null))
                lumina.TryAddTrait(trait);
            lumina.CurrentHP = lumina.MaxHP;
            return lumina;
        }
    }
}
