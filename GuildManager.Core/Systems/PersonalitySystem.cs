using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>見立てで志向・気質が分かった1件（→ PersonalitySystem.ProcessWeek）。Key は志向の名前か "Temperament"。</summary>
    public sealed record PersonalityDiscovery(Adventurer Adventurer, string Key, PersonalityKnowledge Level);

    /// <summary>
    /// 冒険者の個性（段4a、2026年10月・§0.100。→ docs/検討中_経営と育成.md 第2部）。
    ///  - 志向：主志向1つ（必ず）・副志向0〜1つ・苦手0〜1つ（苦手は仕事の5つから）。気質（とても慎重〜とても大胆の5段）。相性の素地（0〜255、循環、隠す）。
    ///  - 効き目（段4a）は満足度だけ：好きな仕事をした週・動機の喜ぶきっかけで上がり、苦手な仕事・動機の嫌うきっかけで下がる。
    ///    気質は力に効かず、扉前の構え（プレイヤーの判断）が気質に合うか合わないかで満足度が動く。
    ///  - 見立て：きっかけを見た回数（Personality.Exposure）が RevealLikely で「〜らしい」、RevealSure で確か。目利き（→ PotentialEstimateSystem.GetEye）で減り、
    ///    魂魄融和の娘は半分。相性の素地は分からない（効き目は段4b から）。
    /// 乱数は個性を決めるとき（Assign・AssignChild）だけ使う。週の判定に乱数は使わない。
    /// </summary>
    public static class PersonalitySystem
    {
        /// <summary>仕事の5つ（苦手はここからだけ選ぶ）。</summary>
        public static readonly Drive[] WorkDrives = { Drive.Dive, Drive.Survey, Drive.Gather, Drive.Tournament, Drive.Training };

        public static readonly Drive[] AllDrives = (Drive[])Enum.GetValues(typeof(Drive));

        public const string TemperamentKey = "Temperament";

        public const int AffinityRange = 256;

        // ==================== 決める ====================

        /// <summary>
        /// 個性を決める（採用の候補・魂魄融和の娘）。乱数を引く順：主志向 → 副志向の有無（→ 選ぶ）→ 苦手の有無（→ 選ぶ）→ 気質 → 相性の素地 → 採用の一言。
        /// 特性を決めたあとに呼ぶ（特性に合う志向・気質へ寄せるため）。
        /// </summary>
        public static Personality Assign(GameState state, Adventurer a, IRng rng)
        {
            var p = new Personality { LastFameWeek = state.WeekNumber };

            p.Main = PickWeighted(AllDrives, d => DriveWeight(a, d), rng);
            if (rng.NextInt(1, 100) <= PersonalityBalance.SubDriveChancePercent)
                p.Sub = PickWeighted(AllDrives.Where(d => d != p.Main).ToArray(), d => DriveWeight(a, d), rng);
            if (rng.NextInt(1, 100) <= PersonalityBalance.DislikeChancePercent)
            {
                var pool = WorkDrives.Where(d => d != p.Main && d != p.Sub).ToArray();
                p.Disliked = PickWeighted(pool, d => d == Drive.Training && a.HasTrait(TraitCatalog.FickleId) ? PersonalityBalance.TraitPullWeight : 1, rng);
            }

            int t = PickWeighted(new[] { -2, -1, 0, 1, 2 }, v => PersonalityBalance.TemperamentWeights[v + 2], rng);
            if (a.HasTrait(TraitCatalog.RecklessId)) t = Math.Max(t, 1);
            if (a.HasTrait(TraitCatalog.CowardId)) t = Math.Min(t, -1);
            p.Temperament = t;

            p.Affinity = rng.NextInt(0, AffinityRange - 1);

            p.HintDrive = rng.NextInt(1, 100) <= PersonalityBalance.HintTruthPercent
                ? p.Main
                : AllDrives.Where(d => d != p.Main).ElementAt(rng.NextInt(0, AllDrives.Length - 2));

            a.Personality = p;
            return p;
        }

        /// <summary>魂魄融和の娘：採用と同じく決めてから、相性の素地を両親の円の上の中点±AffinityInheritSpread にする（乱数を1回足して引く）。</summary>
        public static Personality AssignChild(GameState state, Adventurer child, Adventurer a, Adventurer b, IRng rng)
        {
            var p = Assign(state, child, rng);
            int spread = rng.NextInt(-PersonalityBalance.AffinityInheritSpread, PersonalityBalance.AffinityInheritSpread);
            if (a.Personality != null && b.Personality != null)
                p.Affinity = Wrap(CircularMidpoint(a.Personality.Affinity, b.Personality.Affinity) + spread);
            return p;
        }

        /// <summary>特性に合う志向は選ばれやすい（勤勉→訓練、浪費家→お金、カリスマ→仲間）。</summary>
        private static int DriveWeight(Adventurer a, Drive d) => d switch
        {
            Drive.Training when a.HasTrait(TraitCatalog.DiligentId) => PersonalityBalance.TraitPullWeight,
            Drive.Money when a.HasTrait(TraitCatalog.SpendthriftId) => PersonalityBalance.TraitPullWeight,
            Drive.Comrades when a.HasTrait(TraitCatalog.CharismaId) => PersonalityBalance.TraitPullWeight,
            _ => 1,
        };

        private static T PickWeighted<T>(IReadOnlyList<T> items, Func<T, int> weight, IRng rng)
        {
            int total = items.Sum(i => Math.Max(0, weight(i)));
            int roll = rng.NextInt(1, Math.Max(1, total));
            foreach (var item in items)
            {
                roll -= Math.Max(0, weight(item));
                if (roll <= 0) return item;
            }
            return items[^1];
        }

        // ==================== 相性の素地（円の上） ====================

        public static int Wrap(int value) => ((value % AffinityRange) + AffinityRange) % AffinityRange;

        /// <summary>2人の素地の隔たり（0〜128）。</summary>
        public static int AffinityDistance(int a, int b)
        {
            int d = Math.Abs(Wrap(a) - Wrap(b));
            return Math.Min(d, AffinityRange - d);
        }

        /// <summary>円の上の近い側の中点。ちょうど反対（隔たり128）のときは a から増える向きの中点。</summary>
        public static int CircularMidpoint(int a, int b)
        {
            int forward = Wrap(b - a);
            return forward <= AffinityRange / 2 ? Wrap(a + forward / 2) : Wrap(a - (AffinityRange - forward) / 2);
        }

        // ==================== 見立て ====================

        /// <summary>「〜らしい」「確か」になる回数（目利きで減り、魂魄融和の娘は半分。1回より少なくはならない）。</summary>
        public static (int Likely, int Sure) RevealThresholds(GameState state, Adventurer a)
        {
            double factor = (1 - PotentialEstimateSystem.GetEye(state)) * (a.ParentIds.Count == 2 ? 0.5 : 1.0);
            int likely = Math.Max(1, (int)Math.Round(PersonalityBalance.RevealLikely * factor));
            int sure = Math.Max(likely, (int)Math.Round(PersonalityBalance.RevealSure * factor));
            return (likely, sure);
        }

        public static PersonalityKnowledge Knowledge(GameState state, Adventurer a, string key)
        {
            if (a.Personality == null) return PersonalityKnowledge.Unknown;
            return LevelOf(a.Personality.Exposure.GetValueOrDefault(key), RevealThresholds(state, a));
        }

        public static PersonalityKnowledge Knowledge(GameState state, Adventurer a, Drive drive) => Knowledge(state, a, drive.ToString());

        private static PersonalityKnowledge LevelOf(int count, (int Likely, int Sure) t) =>
            count >= t.Sure ? PersonalityKnowledge.Sure : count >= t.Likely ? PersonalityKnowledge.Likely : PersonalityKnowledge.Unknown;

        // ==================== 週の判定（満足度と見立て） ====================

        // ==================== 副官の注目（§0.117） ====================

        /// <summary>同時に注目できる人数：FocusSlots（倒した階層ボスが FocusExtraBosses 体以上なら FocusSlotsExtra を足す）。</summary>
        public static int FocusSlots(GameState state) =>
            PersonalityBalance.FocusSlots
            + (state.DungeonFields.Sum(f => f.Bosses.Count(b => b.EverDefeated)) >= PersonalityBalance.FocusExtraBosses ? PersonalityBalance.FocusSlotsExtra : 0);

        public static bool IsFocused(GameState state, Adventurer a) => state.FocusedAdventurerIds.Contains(a.Id);

        /// <summary>注目を付ける・外すことができない理由（できるなら null）。変えられるのは月のはじめ（月の第1週）だけ。</summary>
        public static string? FocusBlockReason(GameState state, Adventurer a)
        {
            if (a.Personality == null || a.IsRetired) return "この子の志向・気質は見立てられない";
            if (!GameCalendar.IsFirstWeekOfMonth(state.WeekNumber)) return "注目は月のはじめだけ変えられる";
            if (!IsFocused(state, a) && state.FocusedAdventurerIds.Count >= FocusSlots(state)) return $"注目できるのは{FocusSlots(state)}人まで";
            return null;
        }

        /// <summary>注目を付ける（付いていれば外す）。できなければ false。</summary>
        public static bool ToggleFocus(GameState state, Adventurer a)
        {
            if (FocusBlockReason(state, a) != null) return false;
            if (!state.FocusedAdventurerIds.Remove(a.Id))
                state.FocusedAdventurerIds.Add(a.Id);
            return true;
        }

        /// <summary>月の最後の週の見立てで数える項目の順：主志向・副志向・苦手・気質（中庸なら数えない）。確かになったものは飛ばす。</summary>
        private static IEnumerable<string> FocusKeys(Personality p)
        {
            yield return p.Main.ToString();
            if (p.Sub is { } sub) yield return sub.ToString();
            if (p.Disliked is { } disliked) yield return disliked.ToString();
            if (p.Temperament != 0) yield return TemperamentKey;
        }

        /// <summary>昇給・ボーナスを受けた（次の週の決算で「お金」の喜ぶきっかけにする）。</summary>
        public static void NotifyMoneyJoy(Adventurer a)
        {
            if (a.Personality != null) a.Personality.PendingMoneyJoy++;
        }

        /// <summary>
        /// 週の決算で1回呼ぶ（大迷宮・大会のあと、満足度の週の変動の前）。missionsAtStart は週のはじめに出撃していた任務（今週出発した分を含む）。
        /// 子ごとに満足度の増減を合計して1回だけ反映し、見立てで新しく分かったことを返す。
        /// </summary>
        public static List<PersonalityDiscovery> ProcessWeek(GameState state, int week, IReadOnlyList<ActiveDungeonMission> missionsAtStart,
            IReadOnlyList<DungeonMissionResolution> resolutions, IReadOnlyDictionary<Guid, WeekActivity> activities)
        {
            var discoveries = new List<PersonalityDiscovery>();
            var missionOf = new Dictionary<Guid, ActiveDungeonMission>();
            foreach (var m in missionsAtStart)
                foreach (var member in m.Party.Members)
                    missionOf[member.Id] = m;

            var victors = resolutions.Where(r => r.DungeonResult?.Outcome == DungeonOutcome.Victory)
                .SelectMany(r => r.Party.Members.Select(m => m.Id)).ToHashSet();
            var severe = resolutions.SelectMany(r => r.InjuryEvents).Where(e => e.Severity == InjurySeverity.Severe).Select(e => e.AdventurerId).ToHashSet();

            // 副官の注目（§0.117）：ギルドを去った子・引退した子は外す
            state.FocusedAdventurerIds.RemoveAll(id => state.Adventurers.All(x => x.Id != id || x.IsRetired));

            foreach (var a in state.Adventurers.Where(a => a.Personality != null && !a.IsRetired && !a.IsOnLoan).ToList())
            {
                var p = a.Personality!;
                var thresholds = RevealThresholds(state, a);
                int delta = 0;
                bool focused = IsFocused(state, a);

                void See(string key)
                {
                    var before = LevelOf(p.Exposure.GetValueOrDefault(key), thresholds);
                    p.Exposure[key] = p.Exposure.GetValueOrDefault(key) + (focused ? PersonalityBalance.FocusExposureMultiplier : 1);
                    var after = LevelOf(p.Exposure[key], thresholds);
                    if (after != before)
                        discoveries.Add(new PersonalityDiscovery(a, key, after));
                }

                void Joy(Drive drive)
                {
                    See(drive.ToString());
                    delta += p.AttitudeOf(drive) switch
                    {
                        DriveAttitude.Main => PersonalityBalance.SatisfactionMain,
                        DriveAttitude.Sub => PersonalityBalance.SatisfactionSub,
                        DriveAttitude.Disliked => PersonalityBalance.SatisfactionDisliked,
                        _ => 0,
                    };
                }

                void Grief(Drive drive)
                {
                    See(drive.ToString());
                    delta += p.AttitudeOf(drive) switch
                    {
                        DriveAttitude.Main => PersonalityBalance.MotiveNegativeMain,
                        DriveAttitude.Sub => PersonalityBalance.MotiveNegativeSub,
                        _ => 0,
                    };
                }

                // ---- 仕事 ----
                missionOf.TryGetValue(a.Id, out var mission);
                if (mission != null)
                    Joy(MoraleSystem.DriveFor(mission.MissionType));
                var records = a.TournamentRecords.Where(r => r.Week == week).ToList();
                if (records.Count > 0)
                    Joy(Drive.Tournament);
                if (activities.TryGetValue(a.Id, out var activity) && activity is WeekActivity.Training or WeekActivity.SelfTraining)
                    Joy(Drive.Training);

                // ---- 動機 ----
                if (mission != null)
                {
                    var mates = mission.Party.Members.Where(m => m.Id != a.Id).ToList();
                    if (mates.Any(m => CompatibilitySystem.GetCompatibility(state, a.Id, m.Id) >= PersonalityBalance.ComradesFriendThreshold))
                        Joy(Drive.Comrades);
                    if (mates.Any(m => CompatibilitySystem.IsHostile(state, a.Id, m.Id)))
                        Grief(Drive.Comrades);
                }

                bool prize = records.Any(r => TournamentBalance.Find(r.DefinitionId) is { } def
                    && r.Placing switch { 1 => def.Prize1, 2 => def.Prize2, 4 => def.Prize4, _ => 0 } > 0);
                if (p.PendingMoneyJoy > 0 || prize)
                {
                    p.PendingMoneyJoy = 0;
                    Joy(Drive.Money);
                }
                if (GameCalendar.IsLastWeekOfMonth(week) && a.WeeklyWage < SatisfactionSystem.GetAppropriateWage(a) * SatisfactionBalance.WageAdequacyRatio)
                    Grief(Drive.Money);

                bool fame = victors.Contains(a.Id)
                    || records.Any(r => r.Placing == 1 || (r.Placing <= 4 && r.Grade is TournamentGrade.G1 or TournamentGrade.Special));
                if (fame)
                {
                    p.LastFameWeek = week;
                    Joy(Drive.Fame);
                }
                else
                {
                    int drought = week - p.LastFameWeek - PersonalityBalance.FameDroughtWeeks;
                    if (drought >= 0 && drought % GameCalendar.WeeksPerSeason == 0)
                        Grief(Drive.Fame);
                }

                // ---- 気質（扉前の構えが合うか） ----
                var door = resolutions.FirstOrDefault(r => r.ArrivedAtBossDoor && r.Party.Members.Any(m => m.Id == a.Id));
                if (door != null)
                {
                    See(TemperamentKey);
                    var stance = StanceOf(state, missionsAtStart, door);
                    int dir = stance switch { DoorStance.Cautious => -1, DoorStance.Bold => 1, _ => 0 };
                    if (dir != 0 && p.Temperament != 0)
                    {
                        bool match = Math.Sign(p.Temperament) == dir;
                        delta += match ? PersonalityBalance.TemperamentMatch : PersonalityBalance.TemperamentMismatch * Math.Abs(p.Temperament);
                    }
                    if (stance == DoorStance.Bold && door.DungeonResult != null && p.Temperament < 0 && severe.Contains(a.Id))
                        delta += PersonalityBalance.TemperamentBoldInjury;
                }

                // ---- 副官の注目（§0.117）：月の最後の週に、まだ確かでない項目を数える（1回ぶん。注目の倍率はかけない） ----
                if (focused && GameCalendar.IsLastWeekOfMonth(week))
                    foreach (var key in FocusKeys(p).Where(k => LevelOf(p.Exposure.GetValueOrDefault(k), thresholds) != PersonalityKnowledge.Sure).Take(PersonalityBalance.FocusMonthlyReveals))
                    {
                        focused = false;
                        See(key);
                        focused = true;
                    }

                if (delta != 0)
                    SatisfactionSystem.Adjust(a, delta);
            }
            return discoveries;
        }

        /// <summary>扉前に着いた部隊の構え（方針の部隊ならその構え、手動の出撃・分からなければ標準）。</summary>
        private static DoorStance StanceOf(GameState state, IReadOnlyList<ActiveDungeonMission> missions, DungeonMissionResolution r)
        {
            var mission = missions.FirstOrDefault(m => ReferenceEquals(m.Party, r.Party))
                ?? missions.FirstOrDefault(m => m.Party.Members.Any(x => r.Party.Members.Any(y => y.Id == x.Id)));
            var saved = mission?.SavedPartyId is Guid id ? state.SavedParties.FirstOrDefault(s => s.Id == id) : null;
            return saved?.Stance ?? DoorStance.Standard;
        }

        // ==================== 表示の言葉（演出の文言はコードに置く） ====================

        public static string DriveLabel(Drive d) => d switch
        {
            Drive.Dive => "潜行",
            Drive.Survey => "調査",
            Drive.Gather => "採取",
            Drive.Tournament => "大会",
            Drive.Training => "訓練",
            Drive.Comrades => "仲間",
            Drive.Money => "お金",
            _ => "名声",
        };

        /// <summary>主志向の呼び名（「武闘派」など）。</summary>
        public static string DriveTitle(Drive d) => d switch
        {
            Drive.Dive => "武闘派",
            Drive.Survey => "探求派",
            Drive.Gather => "収穫派",
            Drive.Tournament => "栄光派",
            Drive.Training => "求道派",
            Drive.Comrades => "仲間思い",
            Drive.Money => "稼ぎ屋",
            _ => "功名派",
        };

        public static string DriveIcon(Drive d) => d switch
        {
            Drive.Dive => "⚔",
            Drive.Survey => "🔍",
            Drive.Gather => "🌿",
            Drive.Tournament => "🏆",
            Drive.Training => "📖",
            Drive.Comrades => "🤝",
            Drive.Money => "💰",
            _ => "🎖",
        };

        public static string TemperamentLabel(int t) => t switch
        {
            <= -2 => "とても慎重",
            -1 => "慎重",
            0 => "中庸",
            1 => "大胆",
            _ => "とても大胆",
        };

        /// <summary>採用の一言（志願者の言葉。主志向とは限らない）。</summary>
        public static string HintText(Drive d) => d switch
        {
            Drive.Dive => "「強い番人と戦ってみたいんです。迷宮の奥まで行かせてください」",
            Drive.Survey => "「迷宮の謎を解き明かしたくて。調べ物なら任せてください」",
            Drive.Gather => "「野に咲く薬草に目がありません。採るのは得意なんです」",
            Drive.Tournament => "「いつか王都の大会で優勝するのが夢です」",
            Drive.Training => "「毎日の稽古が好きなんです。もっと強くなりたい」",
            Drive.Comrades => "「仲間と一緒なら、どこへだって行けます」",
            Drive.Money => "「家に仕送りがしたくて。お給金のいいギルドを探していました」",
            _ => "「名を上げたいんです。いつか二つ名で呼ばれるくらいに」",
        };

        /// <summary>見立てで分かったことの一文（月報）。ふつう（好きでも苦手でもない）と分かったときは null（知らせない）。</summary>
        public static string? DiscoveryText(PersonalityDiscovery d)
        {
            var p = d.Adventurer.Personality;
            if (p == null) return null;
            string sure = d.Level == PersonalityKnowledge.Sure ? "だと分かった" : "らしい";
            if (d.Key == TemperamentKey)
                return p.Temperament == 0 ? null : $"{d.Adventurer.Name}は{TemperamentLabel(p.Temperament)}な気質{(d.Level == PersonalityKnowledge.Sure ? "だと分かった" : "らしい")}";
            if (!Enum.TryParse<Drive>(d.Key, out var drive)) return null;
            return p.AttitudeOf(drive) switch
            {
                DriveAttitude.Main => $"{d.Adventurer.Name}は{DriveLabel(drive)}がいちばん好き{sure}（{DriveTitle(drive)}）",
                DriveAttitude.Sub => $"{d.Adventurer.Name}は{DriveLabel(drive)}も好き{sure}",
                DriveAttitude.Disliked => $"{d.Adventurer.Name}は{DriveLabel(drive)}が苦手{sure}",
                _ => null,
            };
        }
    }
}
