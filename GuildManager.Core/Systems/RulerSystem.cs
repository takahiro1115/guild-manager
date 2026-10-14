using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 大迷宮の支配者（2026年10月・§0.127、→ docs/検討中_大迷宮の支配者.md）。
    /// 支配者は各フィールドの70F（前哨戦）と100F（決着）の主の代わりに待ち、主の決戦では倒せない（扉前で必ず引き返す）。
    /// 支配者とは支配者戦（支配者ごとに戦い方が違う）で戦う。今はヴェスペラ（森、勝ち抜きの一騎打ち）だけ。
    ///
    /// ヴェスペラの一騎打ち（設計書 §3-5）：
    ///  - 60F（90F）の主を倒すと、70F（100F）でヴェスペラに挑める。代表を最大5人、順番つきで選ぶ（月のはじめだけ変えられる。翌月も残る）。
    ///    代表はその月、出撃・訓練・大会に出ない。
    ///  - 決闘は月を終えたあと（月報のあと）に1回。最大5試合の勝ち抜き。勝った代表は残って次の試合へ、負けた代表は下がる。
    ///  - ヴェスペラの武器は試合ごとに剣→魔→技と替わり、並びは月をまたいで続く。試合の前に MND で魅了の判定。
    ///  - 体力は月をまたいで残り、毎月回復する。前哨戦は体力50を切ると退き（70Fを倒したことになる）、決着は0で撃破。
    ///  - 介入（部隊の護符の多頭通信術式）：見切りを伝える（次の代表を入れ替える）・名を呼ぶ（魅了をやり直す）。
    ///  - HPが0になった代表は、低い確率で強制除籍（§0.125 と同じ決まり）。ほかは HP1 で重傷。
    /// </summary>
    public static class RulerSystem
    {
        public const string VesperaId = "vespera";
        public const string VesperaName = "ヴェスペラ";
        public const string ForestId = "forest";
        public const int OutpostFloor = 70;
        public const int FinalFloor = 100;

        /// <summary>ヴェスペラの武器の並び（剣→魔→技）。</summary>
        public static readonly TournamentDiscipline[] VesperaWeapons =
            { TournamentDiscipline.Sword, TournamentDiscipline.Magic, TournamentDiscipline.Skill };

        /// <summary>武器の表示名（物語の言葉）。</summary>
        public static string WeaponLabel(TournamentDiscipline d) => d switch
        {
            TournamentDiscipline.Sword => "双剣",
            TournamentDiscipline.Magic => "闇の術",
            _ => "茨の罠",
        };

        // ==================== 状態 ====================

        /// <summary>支配者の状態が無ければ足す（新しいゲーム・旧セーブ。何度呼んでもよい）。</summary>
        public static void EnsureRulers(GameState state)
        {
            if (state.Rulers.All(r => r.RulerId != VesperaId))
                state.Rulers.Add(new RulerState { RulerId = VesperaId, Hp = RulerBalance.MaxHp });
            foreach (var ruler in state.Rulers)
            {
                Refresh(state, ruler);
                // 支配者の待つ主（まだ倒していない70F・100F）の名前を支配者の名前にする（深さの目盛り・目標・調査の表示がそろう）
                if (FieldOf(state, ruler) is { } field)
                    foreach (var boss in field.Bosses.Where(b => (b.Floor == OutpostFloor || b.Floor == FinalFloor) && !b.IsDefeated))
                        boss.Name = DisplayName(ruler.RulerId);
            }
        }

        /// <summary>支配者の待つ主の名前（「支配者ヴェスペラ」）。</summary>
        public static string DisplayName(string rulerId) => $"支配者{NameOf(rulerId)}";

        /// <summary>その支配者の状態（無ければ null）。</summary>
        public static RulerState? Find(GameState state, string rulerId) => state.Rulers.FirstOrDefault(r => r.RulerId == rulerId);

        /// <summary>支配者の名前。</summary>
        public static string NameOf(string rulerId) => rulerId == VesperaId ? VesperaName : rulerId;

        /// <summary>その支配者が待っている主（まだ倒していない70F、なければ100F）。両方倒していれば null。</summary>
        public static FloorBoss? BossOf(GameState state, RulerState ruler)
        {
            var field = FieldOf(state, ruler);
            if (field == null) return null;
            return field.Bosses.Where(b => (b.Floor == OutpostFloor || b.Floor == FinalFloor) && !b.IsDefeated)
                .OrderBy(b => b.Floor).FirstOrDefault();
        }

        /// <summary>その支配者のフィールド（今はヴェスペラ＝森だけ）。</summary>
        public static DungeonField? FieldOf(GameState state, RulerState ruler) =>
            ruler.RulerId == VesperaId ? state.DungeonFields.FirstOrDefault(f => f.Id == ForestId) : null;

        /// <summary>この主の代わりに支配者が待っているか（待っていればその支配者）。主の決戦では倒せない。</summary>
        public static RulerState? RulerAt(GameState state, FloorBoss boss)
        {
            if (boss.IsDefeated || (boss.Floor != OutpostFloor && boss.Floor != FinalFloor)) return null;
            return state.Rulers.FirstOrDefault(r => BossOf(state, r) == boss);
        }

        /// <summary>前哨戦（70F）か。</summary>
        public static bool IsOutpost(GameState state, RulerState ruler) => BossOf(state, ruler)?.Floor == OutpostFloor;

        /// <summary>
        /// 今挑めるか：フィールドが開いていて、支配者の待つ主の手前の主（60F・90F）までを倒している（＝次に挑む主が支配者の主）。
        /// </summary>
        public static bool CanChallenge(GameState state, RulerState ruler)
        {
            var field = FieldOf(state, ruler);
            var boss = BossOf(state, ruler);
            return field != null && boss != null && field.IsUnlocked && field.GetNextActiveBoss() == boss;
        }

        /// <summary>
        /// 体力の持ち主の階を合わせる（前哨戦で退いたあと、決着では満タンに戻る）。両方倒していれば何もしない。
        /// </summary>
        public static void Refresh(GameState state, RulerState ruler)
        {
            var boss = BossOf(state, ruler);
            if (boss == null || ruler.HpFloor == boss.Floor) return;
            ruler.HpFloor = boss.Floor;
            ruler.Hp = RulerBalance.MaxHp;
        }

        /// <summary>
        /// 新しい週の処理（週の決算のあと、→ WeekProcessingSystem）：支配者の状態を補い、月のはじめなら体力を回復する。
        /// </summary>
        public static void ProcessNewWeek(GameState state)
        {
            EnsureRulers(state);
            if (!GameCalendar.IsFirstWeekOfMonth(state.WeekNumber)) return;
            foreach (var ruler in state.Rulers.Where(r => BossOf(state, r) != null))
            {
                ruler.Hp = Math.Min(RulerBalance.MaxHp, ruler.Hp + RulerBalance.RegenPerMonth);
                // 代表がこの月を過ごし終えた：月報のあとに決闘する
                if (CanChallenge(state, ruler) && AvailableRepresentatives(state, ruler).Count > 0)
                    ruler.PendingDuelWeek = state.WeekNumber;
            }
        }

        // ==================== 代表 ====================

        /// <summary>今月、支配者戦の代表になっている（挑める支配者の代表に入っている）か。代表はその月、出撃・訓練・大会に出ない。</summary>
        public static bool IsRepresentative(GameState state, Guid adventurerId) =>
            state.Rulers.Any(r => r.RepresentativeIds.Contains(adventurerId) && CanChallenge(state, r));

        /// <summary>代表を変えられるのは月のはじめだけ（訓練の割り振り・大会の出場と同じ、§0.70）。</summary>
        public static bool CanChangeRepresentatives(GameState state) => GameCalendar.IsFirstWeekOfMonth(state.WeekNumber);

        /// <summary>その冒険者を代表にできない理由（できるなら null）。</summary>
        public static string? RepresentativeBlockReason(GameState state, Adventurer a)
        {
            if (a.IsRetired) return "引退している";
            if (a.Injury == InjurySeverity.Severe) return "重傷で休んでいる";
            if (a.IsOnLoan) return "派遣に出ている";
            if (a.IsDispatched) return "出撃中";
            if (TournamentSystem.IsEntered(state, a.Id)) return "今月は大会に出る";
            return null;
        }

        /// <summary>
        /// 代表を決める（順番つき、上限 VesperaMaxRepresentatives。出られない子は外す）。月のはじめでなければ false。
        /// 代表に入った子は訓練所から外す。
        /// </summary>
        public static bool TrySetRepresentatives(GameState state, RulerState ruler, IEnumerable<Guid> ids)
        {
            if (!CanChangeRepresentatives(state)) return false;
            var list = ids.Distinct()
                .Select(id => state.Adventurers.FirstOrDefault(a => a.Id == id))
                .Where(a => a != null && RepresentativeBlockReason(state, a) == null)
                .Select(a => a!.Id)
                .Take(RulerBalance.VesperaMaxRepresentatives)
                .ToList();
            ruler.RepresentativeIds = list;
            foreach (var id in list)
                state.TrainingAssignments.Remove(id);
            return true;
        }

        /// <summary>代表のうち、いま戦える子（順番どおり）。</summary>
        public static List<Adventurer> AvailableRepresentatives(GameState state, RulerState ruler) =>
            ruler.RepresentativeIds
                .Select(id => state.Adventurers.FirstOrDefault(a => a.Id == id))
                .Where(a => a != null && !a.IsRetired && a.Injury != InjurySeverity.Severe && !a.IsOnLoan && a.CurrentHP > 0)
                .Select(a => a!)
                .ToList();

        // ==================== 決闘の段取り ====================

        /// <summary>
        /// 月を終えたあとの決闘を待っているか：代表が月を過ごし終えた印（PendingDuelWeek）が今の週で、挑めて、戦える代表がいる
        /// （1か月に1回。月を進めた直後〈新しい月の第1週〉に画面が決闘を始める。月のはじめに代表を選んだだけでは決闘にならない）。
        /// </summary>
        public static bool IsDuelDue(GameState state, RulerState ruler) =>
            ruler.PendingDuelWeek == state.WeekNumber
            && CanChallenge(state, ruler)
            && AvailableRepresentatives(state, ruler).Count > 0;

        /// <summary>決闘を待っている支配者（無ければ null）。</summary>
        public static RulerState? DueDuel(GameState state) => state.Rulers.FirstOrDefault(r => IsDuelDue(state, r));

        /// <summary>この支配者戦で使える介入の回数（研究で増える。研究はまだ無い）。</summary>
        public static int InterventionsPerBattle(GameState state) => RulerBalance.InterventionsBase;

        // ==================== ヴェスペラの見立て ====================

        /// <summary>今のヴェスペラの強さ（その部門。疲れを除く）：前哨戦・決着の基準×大会の部門の倍率（魔は0.75）。</summary>
        public static double VesperaStrength(bool outpost, TournamentDiscipline d) =>
            (outpost ? RulerBalance.VesperaStrengthOutpost : RulerBalance.VesperaStrengthFinal) * TournamentBalance.DisciplineFactor(d);

        /// <summary>魅了の閾値（孤独の囁き込み）：前哨戦60・決着80、ギルドに相棒のいない子は+10。</summary>
        public static int CharmThreshold(GameState state, Adventurer a, bool outpost)
        {
            int t = outpost ? RulerBalance.VesperaCharmThresholdOutpost : RulerBalance.VesperaCharmThresholdFinal;
            return HasBuddy(state, a) ? t : t + RulerBalance.VesperaLonelyThresholdBonus;
        }

        /// <summary>ギルドに相棒（相性 BuddyThreshold 以上）がいるか。</summary>
        public static bool HasBuddy(GameState state, Adventurer a) =>
            state.Adventurers.Any(b => b.Id != a.Id && !b.IsRetired && MoraleSystem.IsBuddy(state, a.Id, b.Id));

        /// <summary>魅了の判定（MND は装備込みの実効値）。</summary>
        public static DuelCharm JudgeCharm(GameState state, Adventurer a, bool outpost)
        {
            double mnd = a.GetEffectiveStat("MND");
            int threshold = CharmThreshold(state, a, outpost);
            if (mnd >= threshold) return DuelCharm.Resisted;
            return mnd >= threshold * RulerBalance.VesperaWaverRate ? DuelCharm.Wavered : DuelCharm.Charmed;
        }

        /// <summary>介入「名を呼ぶ」が届く見込み（%）：基礎＋いちばん高い相性÷4＋満足度÷4、上限90。</summary>
        public static int CallNameChance(GameState state, Adventurer a)
        {
            int bestCompat = state.Adventurers.Where(b => b.Id != a.Id && !b.IsRetired)
                .Select(b => CompatibilitySystem.GetCompatibility(state, a.Id, b.Id)).DefaultIfEmpty(0).Max();
            int chance = RulerBalance.CallNameBaseChance
                + Math.Max(0, bestCompat) / RulerBalance.CallNameCompatibilityDivisor
                + Math.Max(0, a.Satisfaction) / RulerBalance.CallNameMoraleDivisor;
            return Math.Min(RulerBalance.CallNameMaxChance, chance);
        }

        /// <summary>今の解析率（支配者の待つ主の解析。迷宮調査で上がる）。</summary>
        public static double IntelOf(GameState state, RulerState ruler) => BossOf(state, ruler)?.IntelRate ?? 0;

        /// <summary>武器の並び（この先 count 試合分）。</summary>
        public static List<TournamentDiscipline> UpcomingWeapons(RulerState ruler, int count) =>
            Enumerable.Range(0, count).Select(i => VesperaWeapons[(ruler.WeaponIndex + i) % VesperaWeapons.Length]).ToList();

        /// <summary>決闘を始める（挑めなければ null）。結果は試合ごとに状態へ反映し、最後に <see cref="RulerDuel.Finish"/> で締める。</summary>
        public static RulerDuel? StartDuel(GameState state, RulerState ruler, IRng rng)
        {
            Refresh(state, ruler);
            if (!CanChallenge(state, ruler)) return null;
            var reps = AvailableRepresentatives(state, ruler);
            if (reps.Count == 0) return null;
            return new RulerDuel(state, ruler, BossOf(state, ruler)!, reps, rng, InterventionsPerBattle(state));
        }

        /// <summary>
        /// 決闘を最後まで自動で行う（シミュレーター・テスト用。画面は1試合ずつ進めて介入を聞く）。
        /// useInterventions なら、見切りは「次の武器でもっと強い代表がいれば入れ替える」、名を呼ぶは「いつも呼ぶ」で使う。
        /// </summary>
        public static RulerDuel? ResolveAuto(GameState state, RulerState ruler, IRng rng, bool useInterventions)
        {
            var duel = StartDuel(state, ruler, rng);
            if (duel == null) return null;
            while (!duel.IsOver)
            {
                duel.Advance();
                switch (duel.Prompt)
                {
                    case DuelPromptKind.Foresight:
                        duel.AnswerForesight(useInterventions ? duel.BestForesightCandidate() : null);
                        break;
                    case DuelPromptKind.CallName:
                        duel.AnswerCallName(useInterventions);
                        break;
                }
            }
            duel.Finish();
            return duel;
        }

        // ==================== 決闘の語り（画面の小窓、→ RulerDuel） ====================

        /// <summary>決闘の小窓の背景。</summary>
        public const string DuelBackground = "翠緑の原生林・主の巣";

        /// <summary>決闘のはじめの語り。</summary>
        public static List<StoryLine> DuelOpeningLines(RulerDuel duel) => new()
        {
            new(StoryLineKind.Narration, $"{(duel.IsOutpost ? "森の70階" : "森の100階")}、茨の決闘場。ヴェスペラの体力 {duel.HpBefore}/{RulerBalance.MaxHp}" +
                (duel.IsOutpost ? $"（{RulerBalance.OutpostRetreatHp}を切ると退く）" : "")),
            new(StoryLineKind.Speech, duel.IsOutpost ? "来たか。……一人ずつだ。順に来い" : "ここはわたしの家だ。……今度は、退かん", VesperaName),
        };

        /// <summary>1試合の語り（地の文の段）。</summary>
        public static List<StoryLine> BoutLines(DuelBout b)
        {
            var lines = new List<StoryLine>
            {
                new(StoryLineKind.Narration, $"第{b.Number}試合　ヴェスペラの{WeaponLabel(b.Discipline)}（{TournamentSystem.DisciplineLabel(b.Discipline)}）。{b.Name}が前へ出る"),
            };
            if (b.CalledName)
                lines.Add(new(StoryLineKind.Narration, b.CallNameReached
                    ? $"護符から、{b.Name}の名を呼ぶ声が届いた。{b.Name}の目に、光が戻る"
                    : $"護符から、{b.Name}の名を呼ぶ声が響いた。……けれど、届かなかった"));
            lines.Add(new(StoryLineKind.Narration, b.Charm switch
            {
                DuelCharm.Charmed => $"{b.Name}はヴェスペラの囁きに魅入られ、刃を下ろした",
                DuelCharm.Wavered => $"ヴェスペラの囁きに、{b.Name}の足がわずかに揺らいだ",
                _ => $"{b.Name}は、ヴェスペラの囁きを振り払った",
            }));
            if (b.Charm != DuelCharm.Charmed)
                lines.Add(new(StoryLineKind.Narration, b.Won
                    ? $"{b.Name}の一撃が届いた！　ヴェスペラの体力 {b.RulerHpAfter}（−{b.Damage}）"
                    : $"{b.Name}は押し返された。ヴェスペラの体力 {b.RulerHpAfter}（−{b.Damage}）"));
            if (b.Guard is { } g)
                lines.Add(new(StoryLineKind.Narration, $"{g.GuardianName}が飛び込み、{b.Name}をかばった（HP{g.Share}を引き受けた）"));
            if (b.SixthSense)
                lines.Add(new(StoryLineKind.Narration, $"{b.Name}は危機を察し、倒れる寸前で自ら退いた"));
            if (b.NearDeath)
                lines.Add(new(StoryLineKind.Narration, $"{b.Name}は致命傷を負った。……アルベールの秘薬で、一命を取り留めた（重傷）"));
            if (b.Fallen)
                lines.Add(new(StoryLineKind.Narration, $"{b.Name}は致命傷を負った。命は助かったが、もう二度と剣は握れない（強制除籍）"));
            return lines;
        }

        /// <summary>介入を待つ山場の語り。</summary>
        public static List<StoryLine> PromptLines(RulerDuel duel)
        {
            if (duel.CurrentFighter is not { } a) return new List<StoryLine>();
            return duel.Prompt switch
            {
                DuelPromptKind.Foresight => new()
                {
                    new(StoryLineKind.Narration, $"次はヴェスペラの{WeaponLabel(duel.PendingDiscipline)}（{TournamentSystem.DisciplineLabel(duel.PendingDiscipline)}）。{a.Name}が構える"),
                    new(StoryLineKind.Narration, "【護符】見切りを伝えれば、次に出る代表を入れ替えられる"),
                },
                DuelPromptKind.CallName => new()
                {
                    new(StoryLineKind.Narration, $"ヴェスペラの囁きに、{a.Name}の目がうつろになっていく……"),
                    new(StoryLineKind.Narration, "【護符】名を呼べば、引き戻せるかもしれない"),
                },
                _ => new List<StoryLine>(),
            };
        }

        /// <summary>決闘のおわりの語り（<see cref="RulerDuel.Finish"/> のあと）。</summary>
        public static List<StoryLine> DuelClosingLines(RulerDuel duel)
        {
            var lines = new List<StoryLine>();
            int dealt = duel.HpBefore - duel.Ruler.Hp;
            if (duel.Retreated)
            {
                lines.Add(new(StoryLineKind.Speech, "……ふん。今日のところは、退いてやる", VesperaName));
                lines.Add(new(StoryLineKind.Narration, "ヴェスペラは茨の奥へ退いた。森の70階の先へ進めるようになった"));
            }
            else if (duel.Defeated)
            {
                lines.Add(new(StoryLineKind.Speech, "……見事だ", VesperaName));
                lines.Add(new(StoryLineKind.Narration, "ヴェスペラの最後の刃が折れた。森の100階を制した"));
            }
            else
            {
                lines.Add(new(StoryLineKind.Speech, dealt > 0 ? "……悪くない。次は、もっと強い者を寄越せ" : "その程度か。出直せ", VesperaName));
                lines.Add(new(StoryLineKind.Narration, $"決闘は終わった。ヴェスペラの体力 {duel.Ruler.Hp}/{RulerBalance.MaxHp}（この決闘で−{dealt}。毎月{RulerBalance.RegenPerMonth}ずつ回復する）"));
            }
            return lines;
        }

        // ==================== 強制除籍 ====================

        /// <summary>
        /// 支配者戦で強制除籍になった子の後始末（→ DungeonExpeditionSystem.ApplyForcedRetirements と同じ手順）：
        /// 居合わせた代表の満足度・相性の余波、装備を保管庫へ、記録へ移す。マスターの激怒も。
        /// 居合わせた仲間は、除籍された子を先頭にした代表の4人まで（部隊の大きさ）で数える。
        /// </summary>
        internal static void ApplyForcedRetirements(GameState state, IReadOnlyList<Adventurer> present, IReadOnlyCollection<Guid> fallenIds, IRng rng, RulerDuel duel)
        {
            if (fallenIds.Count == 0) return;
            var satisfaction = new SatisfactionSystem(rng);
            var compatibility = new CompatibilitySystem(rng);
            foreach (var id in fallenIds)
            {
                var fallen = present.FirstOrDefault(a => a.Id == id);
                if (fallen == null) continue;
                var party = new Party();
                party.TryAdd(fallen);
                foreach (var other in present.Where(a => a.Id != id))
                    party.TryAdd(other);
                satisfaction.ApplyPartyLossPenalty(party, id);
                duel.TraitGrants.AddRange(compatibility.ApplyDeathAftermath(state, party, id, fallenIds));

                EquipmentSystem.UnequipAllToArmory(state, fallen, $"{fallen.Name}（除籍）から返還");
                fallen.FellAtWeek = state.WeekNumber;
                state.TrainingAssignments.Remove(fallen.Id);
                state.Adventurers.Remove(fallen);
                state.FallenAdventurers.Add(fallen);
                foreach (var r in state.Rulers)
                    r.RepresentativeIds.Remove(fallen.Id);
            }
            duel.MoodApplied = MasterMoodSystem.ApplyForcedRetirementFury(state, fallenIds.Count);
        }
    }

    /// <summary>魅了の判定の結果。</summary>
    public enum DuelCharm
    {
        /// <summary>耐えた。</summary>
        Resisted,
        /// <summary>揺らいだ（試合の強さ×0.7）。</summary>
        Wavered,
        /// <summary>魅了された（戦わずに負ける）。</summary>
        Charmed,
    }

    /// <summary>決闘の途中で聞く介入（無ければ None）。</summary>
    public enum DuelPromptKind
    {
        None,
        /// <summary>見切りを伝える：次の試合の前、次に出る代表を入れ替えるか。</summary>
        Foresight,
        /// <summary>名を呼ぶ：代表が魅了されたとき、呼びかけるか。</summary>
        CallName,
    }

    /// <summary>決闘の1試合。</summary>
    public sealed class DuelBout
    {
        public int Number { get; init; }
        public TournamentDiscipline Discipline { get; init; }
        public Guid AdventurerId { get; init; }
        public string Name { get; init; } = "";
        public DuelCharm Charm { get; set; }
        /// <summary>介入「名を呼ぶ」を使った（届いたかは <see cref="CallNameReached"/>）。</summary>
        public bool CalledName { get; set; }
        public bool CallNameReached { get; set; }
        public bool Won { get; set; }
        public double OurStrength { get; set; }
        public double RulerStrength { get; set; }
        /// <summary>削った支配者の体力。</summary>
        public int Damage { get; set; }
        public int RulerHpAfter { get; set; }
        public int HpLost { get; set; }
        /// <summary>危機察知で HP0 の手前で下がった。</summary>
        public bool SixthSense { get; set; }
        /// <summary>HP0から一命を取り留めた（§0.125）。</summary>
        public bool NearDeath { get; set; }
        /// <summary>HP0で強制除籍になった。</summary>
        public bool Fallen { get; set; }
        public GuardEvent? Guard { get; set; }
    }

    /// <summary>
    /// ヴェスペラとの決闘1回（→ RulerSystem.StartDuel）。<see cref="Advance"/> で次の介入か終わりまで進み、
    /// 介入（<see cref="Prompt"/>）があれば <see cref="AnswerForesight"/>・<see cref="AnswerCallName"/> で答えてまた進める。
    /// 終わったら <see cref="Finish"/> で締める（退いた・倒した主の進行、強制除籍、戦った記録）。
    /// 乱数は決闘専用（勝ち負け・損耗・かばい合い・強制除籍・名を呼ぶ）。
    /// </summary>
    public sealed class RulerDuel
    {
        private readonly GameState _state;
        private readonly IRng _rng;
        private readonly List<Adventurer> _queue;
        private readonly List<Adventurer> _present;
        private Adventurer? _fighter;
        private bool _fighterIsFresh;
        private int _foresightAskedForBout = -1;
        private DuelCharm? _pendingCharm;
        private bool _finished;

        public RulerState Ruler { get; }
        public FloorBoss Boss { get; }
        public bool IsOutpost { get; }
        public int HpBefore { get; }
        public List<DuelBout> Bouts { get; } = new();
        public int InterventionsLeft { get; private set; }
        public DuelPromptKind Prompt { get; private set; }
        /// <summary>介入を待っている試合の部門（Prompt があるとき）。</summary>
        public TournamentDiscipline PendingDiscipline { get; private set; }
        /// <summary>介入を待っている今の代表（Prompt があるとき）。</summary>
        public Adventurer? CurrentFighter => _fighter;
        public bool IsOver { get; private set; }
        /// <summary>前哨戦で退いた（70Fを倒したことになる）。</summary>
        public bool Retreated { get; private set; }
        /// <summary>決着で倒した（100Fを倒したことになる）。</summary>
        public bool Defeated { get; private set; }
        public List<Guid> FallenIds { get; } = new();
        public List<Guid> NearDeathIds { get; } = new();
        public List<InjuryEvent> InjuryEvents { get; } = new();
        public List<TraitGrantEvent> TraitGrants { get; } = new();
        public int MoodApplied { get; internal set; }

        internal RulerDuel(GameState state, RulerState ruler, FloorBoss boss, List<Adventurer> reps, IRng rng, int interventions)
        {
            _state = state;
            _rng = rng;
            Ruler = ruler;
            Boss = boss;
            IsOutpost = boss.Floor == RulerSystem.OutpostFloor;
            HpBefore = ruler.Hp;
            _queue = reps.ToList();
            _present = reps.ToList();
            InterventionsLeft = interventions;
        }

        /// <summary>まだ出ていない代表（順番どおり）。</summary>
        public IReadOnlyList<Adventurer> Waiting => _queue;

        /// <summary>見切りで入れ替えられる代表（次の部門で今の代表より強い子、強い順）。</summary>
        public List<Adventurer> ForesightCandidates()
        {
            if (_fighter == null) return new List<Adventurer>();
            double current = TournamentSystem.MatchStrength(_fighter, PendingDiscipline);
            return _queue.Where(a => TournamentSystem.MatchStrength(a, PendingDiscipline) > current)
                .OrderByDescending(a => TournamentSystem.MatchStrength(a, PendingDiscipline)).ToList();
        }

        /// <summary>見切りでいちばん良い入れ替え（無ければ null）。</summary>
        public Guid? BestForesightCandidate() => ForesightCandidates().FirstOrDefault()?.Id;

        private bool OverByHp => IsOutpost ? Ruler.Hp < RulerBalance.OutpostRetreatHp : Ruler.Hp <= 0;

        /// <summary>次の介入か終わりまで進める。</summary>
        public void Advance()
        {
            Prompt = DuelPromptKind.None;
            while (!IsOver)
            {
                if (OverByHp || Bouts.Count >= RulerBalance.VesperaMaxBouts) { IsOver = true; break; }
                if (_fighter == null)
                {
                    if (_queue.Count == 0) { IsOver = true; break; }
                    _fighter = _queue[0];
                    _queue.RemoveAt(0);
                    _fighterIsFresh = true;
                }

                int boutIndex = Bouts.Count;
                PendingDiscipline = RulerSystem.VesperaWeapons[Ruler.WeaponIndex % RulerSystem.VesperaWeapons.Length];

                // 見切りを伝える：次の武器が解析で分かっていて、もっと強い代表が待っているとき（1試合に1回だけ聞く）
                if (_pendingCharm == null && _foresightAskedForBout != boutIndex && InterventionsLeft > 0
                    && RulerSystem.IntelOf(_state, Ruler) >= RulerBalance.VesperaIntelWeaponReveal
                    && ForesightCandidates().Count > 0)
                {
                    _foresightAskedForBout = boutIndex;
                    Prompt = DuelPromptKind.Foresight;
                    return;
                }

                // 魅了の判定。魅了されたら、名を呼ぶかを聞く（介入が残っていれば）
                if (_pendingCharm == null)
                {
                    var charm = RulerSystem.JudgeCharm(_state, _fighter, IsOutpost);
                    if (charm == DuelCharm.Charmed && InterventionsLeft > 0)
                    {
                        _pendingCharm = charm;
                        Prompt = DuelPromptKind.CallName;
                        return;
                    }
                    _pendingCharm = charm;
                }

                FightBout(_pendingCharm.Value, calledName: false, reached: false);
            }
        }

        /// <summary>見切りを伝える：入れ替える代表（null なら使わない）。勝ち残っている代表は HP を残して下がる。</summary>
        public void AnswerForesight(Guid? swapTo)
        {
            if (Prompt != DuelPromptKind.Foresight) return;
            Prompt = DuelPromptKind.None;
            var next = swapTo is Guid id ? _queue.FirstOrDefault(a => a.Id == id) : null;
            if (next == null || _fighter == null) return;
            InterventionsLeft--;
            int at = _queue.IndexOf(next);
            _queue.RemoveAt(at);
            if (_fighterIsFresh)
                _queue.Insert(at, _fighter); // まだ戦っていない子は、入れ替えた子の順番へ戻る
            _fighter = next;
            _fighterIsFresh = true;
        }

        /// <summary>名を呼ぶ（use＝呼ぶ）。届けば「揺らいだ」で戦う。届かなければ魅了されたまま負ける。</summary>
        public void AnswerCallName(bool use)
        {
            if (Prompt != DuelPromptKind.CallName || _fighter == null) return;
            Prompt = DuelPromptKind.None;
            if (!use) { FightBout(DuelCharm.Charmed, false, false); return; }
            InterventionsLeft--;
            bool reached = _rng.NextInt(1, 100) <= RulerSystem.CallNameChance(_state, _fighter);
            FightBout(reached ? DuelCharm.Wavered : DuelCharm.Charmed, calledName: true, reached);
        }

        private void FightBout(DuelCharm charm, bool calledName, bool reached)
        {
            var a = _fighter!;
            _pendingCharm = null;
            var d = PendingDiscipline;
            double fatigue = Math.Max(0, 1 - RulerBalance.VesperaFatiguePerBout * Bouts.Count);
            double ruler = RulerSystem.VesperaStrength(IsOutpost, d) * fatigue;
            double ours = TournamentSystem.MatchStrength(a, d) * (charm == DuelCharm.Wavered ? RulerBalance.VesperaWaverStrengthFactor : 1.0);
            var bout = new DuelBout
            {
                Number = Bouts.Count + 1, Discipline = d, AdventurerId = a.Id, Name = a.Name,
                Charm = charm, CalledName = calledName, CallNameReached = reached,
                OurStrength = ours, RulerStrength = ruler,
            };

            bout.Won = charm != DuelCharm.Charmed && _rng.NextInt(0, 999_999) / 1_000_000.0 < IsabellaSystem.BoutWinChance(ours, ruler);
            bout.Damage = bout.Won
                ? Math.Clamp((int)Math.Round(RulerBalance.VesperaWinDamageBase * ours / Math.Max(1, ruler)), RulerBalance.VesperaWinDamageMin, RulerBalance.VesperaWinDamageMax)
                : charm == DuelCharm.Charmed ? 0 : RulerBalance.VesperaLossDamage;
            Ruler.Hp = Math.Max(0, Ruler.Hp - bout.Damage);
            bout.RulerHpAfter = Ruler.Hp;

            ApplyHpLoss(a, bout, ruler, ours);
            Bouts.Add(bout);
            Ruler.WeaponIndex = (Ruler.WeaponIndex + 1) % RulerSystem.VesperaWeapons.Length;

            // 勝った代表は残る。負けた・倒れた代表は下がる
            if (!bout.Won || bout.Fallen || a.CurrentHP <= 1) _fighter = null;
            else _fighterIsFresh = false;
        }

        private void ApplyHpLoss(Adventurer a, DuelBout bout, double ruler, double ours)
        {
            int pct;
            if (bout.Won)
            {
                pct = _rng.NextInt(RulerBalance.VesperaWinHpLossPctMin, RulerBalance.VesperaWinHpLossPctMax);
            }
            else
            {
                // 強さの差が大きいほど重い（ヴェスペラが代表の1.5倍以上で上限）
                double gap = Math.Clamp((ruler / Math.Max(1, ours) - 1.0) / 0.5, 0, 1);
                pct = (int)Math.Round(RulerBalance.VesperaLossHpLossPctMin + (RulerBalance.VesperaLossHpLossPctMax - RulerBalance.VesperaLossHpLossPctMin) * gap);
            }
            int loss = a.MaxHP * pct / 100;
            int newHp = a.CurrentHP - loss;

            if (newHp <= 0)
            {
                // かばい合い（§0.101）：代表の中の相棒が、損耗の半分を引き受けることがある
                if (MoraleSystem.FindGuardian(_state, a, _present.Where(p => _state.Adventurers.Contains(p))) is { } guardian
                    && _rng.NextInt(1, 100) <= PersonalityBalance.GuardChancePercent)
                {
                    int share = Math.Min((int)Math.Round(loss * PersonalityBalance.GuardShareRate), guardian.CurrentHP - 1);
                    guardian.CurrentHP -= share;
                    loss -= share;
                    newHp = a.CurrentHP - loss;
                    bout.Guard = new GuardEvent(guardian.Id, guardian.Name, a.Id, a.Name, share);
                }
            }
            if (newHp <= 0 && a.HasTrait(TraitCatalog.SixthSenseId))
            {
                newHp = 1; // 危機察知：倒れる前に自分から下がる
                bout.SixthSense = true;
            }
            if (newHp <= 0)
            {
                // HP0（§0.125）：低い確率で強制除籍、外れれば HP1 で一命を取り留める
                if (_rng.NextInt(1, 100) <= CombatBalance.FatalChancePercent)
                {
                    newHp = 0;
                    bout.Fallen = true;
                    FallenIds.Add(a.Id);
                }
                else
                {
                    newHp = 1;
                    bout.NearDeath = true;
                    NearDeathIds.Add(a.Id);
                    if (CriticalInjury.RollOldWound(a, _rng) is { } grant) TraitGrants.Add(grant);
                }
            }
            bout.HpLost = a.CurrentHP - Math.Max(0, newHp);
            a.CurrentHP = Math.Max(0, newHp);
            if (!bout.Fallen && CriticalInjury.TryInflictSevere(a, _rng) is { } injury)
                InjuryEvents.Add(injury);
        }

        /// <summary>
        /// 決闘を締める（1回だけ）：戦った記録、強制除籍の後始末、前哨戦で退いた・決着で倒したら主を倒したことにしてフィールドを進める。
        /// </summary>
        public void Finish()
        {
            if (_finished) return;
            _finished = true;
            IsOver = true;
            Prompt = DuelPromptKind.None;
            Ruler.DuelCount++;
            Ruler.LastDuelWeek = _state.WeekNumber;
            Ruler.PendingDuelWeek = null;

            RulerSystem.ApplyForcedRetirements(_state, _present, FallenIds, _rng, this);

            if (IsOutpost ? Ruler.Hp < RulerBalance.OutpostRetreatHp : Ruler.Hp <= 0)
            {
                if (IsOutpost) Retreated = true; else Defeated = true;
                Boss.CurrentHp = 0;
                Boss.IsDefeated = true;
                DungeonExpeditionSystem.ApplyFieldProgression(_state, Boss);
                RulerSystem.Refresh(_state, Ruler);
            }
        }
    }
}
