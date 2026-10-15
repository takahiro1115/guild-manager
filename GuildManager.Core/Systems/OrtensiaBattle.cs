using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>1つの部隊の1回の攻め（オルテンシア）。</summary>
    public sealed class CorridorAssault
    {
        public string SquadName { get; init; } = "";
        public CorridorTarget Target { get; init; }
        /// <summary>介入「転進」で回ってきた攻め。</summary>
        public bool Redirected { get; init; }
        /// <summary>行き先に相手がいなかった（封印がもう破れている・玉座が閉じている）。</summary>
        public bool Idle { get; set; }
        /// <summary>合い具合（→ OrtensiaBattle.SquadFit）。</summary>
        public double Fit { get; set; }
        public bool Won { get; set; }
        /// <summary>削った封印の強さ、または玉座で削った体力。</summary>
        public int Damage { get; set; }
        /// <summary>攻めたあとの封印の強さ（玉座なら支配者の体力）。</summary>
        public int After { get; set; }
        /// <summary>この攻めで封印が破れた。</summary>
        public bool BrokeSeal { get; set; }
        /// <summary>この攻めで3つの封印がそろって破れ、玉座が開いた。</summary>
        public bool ThroneOpened { get; set; }
        /// <summary>惑わされた（幻惑の間）・動けなかった（玉座の威圧）子。</summary>
        public List<string> Excluded { get; } = new();
        public List<StoryLine> LossLines { get; } = new();
    }

    /// <summary>
    /// オルテンシアとの戦い（玉座への回廊・間を攻める、§0.130、設計書 §3-8）。部隊を最大3つ出し、部隊ごとに行き先（3つの間か玉座）を決める。
    /// 3つの間の封印（強さ100、削った分は月をまたいで残る）を破ると玉座が開く。玉座では威圧（LDR）で動けない子が出る（二つ名を持つ子は受けない。
    /// ギルドのG1の優勝が多いほど威圧は軽い）。オルテンシアは3か月ごとに破れた封印を1つ張り直す。
    /// 介入は「転進」（封印を破った・相手のいなかった部隊を別の行き先へ回す）と「援軍」（張り直しのとき、部隊を守りに回して防ぐ）。
    /// </summary>
    public sealed class OrtensiaBattle : RulerBattle
    {
        public const int HallCount = 3;
        private readonly List<(SavedParty Squad, List<Adventurer> Members)> _squads;
        private readonly List<(int Squad, CorridorTarget Target, bool Redirected)> _queue = new();
        private bool _resealHandled;
        private int _redirectSquad = -1;
        private int _linesShown = -1;

        public List<CorridorAssault> Assaults { get; } = new();
        public override string Background => "忘却の古代廃墟・玉座の回廊";
        /// <summary>古王の封蝋を持ち帰った数。</summary>
        public int SealsGained => MaterialsGained;
        /// <summary>このたびの戦いのはじめに張り直された間（無ければ null）。</summary>
        public CorridorTarget? Resealed { get; private set; }
        /// <summary>援軍で守りに回った部隊の名前（無ければ null）。</summary>
        public string? GuardSquadName { get; private set; }
        /// <summary>介入「転進」を待っている部隊（転進のとき）。</summary>
        public SavedParty? PendingSquad => _redirectSquad >= 0 ? _squads[_redirectSquad].Squad : null;

        internal OrtensiaBattle(GameState state, RulerState ruler, FloorBoss boss, List<(SavedParty Squad, List<Adventurer> Members)> squads, List<CorridorTarget> targets, IRng rng, int interventions)
            : base(state, ruler, boss, squads.SelectMany(s => s.Members), rng, interventions)
        {
            _squads = squads;
            EnsureSeals(ruler);
            // 間へ向かう部隊が先、玉座へ向かう部隊はあと（間で封印が破れれば、同じ月のうちに玉座へ入れる）
            var order = Enumerable.Range(0, squads.Count).Select(i => (Squad: i, Target: i < targets.Count ? targets[i] : CorridorTarget.Skeleton)).ToList();
            foreach (var x in order.Where(x => x.Target != CorridorTarget.Throne)) _queue.Add((x.Squad, x.Target, false));
            foreach (var x in order.Where(x => x.Target == CorridorTarget.Throne)) _queue.Add((x.Squad, x.Target, false));
        }

        // ==================== 封印 ====================

        /// <summary>封印の強さを補う（旧セーブ・新しい階）。</summary>
        public static void EnsureSeals(RulerState ruler)
        {
            ruler.SealHp ??= new List<int>();
            if (ruler.SealHp.Count != HallCount)
                ruler.SealHp = Enumerable.Repeat(RulerBalance.OrtensiaSealMax, HallCount).ToList();
        }

        /// <summary>封印を張り直したて（新しい階）に戻す。</summary>
        public static void ResetSeals(RulerState ruler)
        {
            ruler.SealHp = Enumerable.Repeat(RulerBalance.OrtensiaSealMax, HallCount).ToList();
            ruler.ResealCounter = 0;
            ruler.ResealPending = false;
        }

        /// <summary>玉座が開いているか（3つの封印がすべて破れている）。</summary>
        public static bool ThroneOpen(RulerState ruler) => ruler.SealHp.Count == HallCount && ruler.SealHp.All(h => h <= 0);

        /// <summary>破れた封印があるか。</summary>
        public static bool AnyBroken(RulerState ruler) => ruler.SealHp.Any(h => h <= 0);

        /// <summary>張り直す間（破れた封印のうち、骸骨兵・騎士王・幻惑の順で最初）。無ければ null。</summary>
        public static CorridorTarget? NextReseal(RulerState ruler)
        {
            int i = ruler.SealHp.FindIndex(h => h <= 0);
            return i < 0 ? null : (CorridorTarget)i;
        }

        /// <summary>破れた封印を1つ張り直す（張り直した間を返す。無ければ null）。</summary>
        public static CorridorTarget? ApplyReseal(RulerState ruler)
        {
            ruler.ResealPending = false;
            if (NextReseal(ruler) is not { } hall) return null;
            ruler.SealHp[(int)hall] = RulerBalance.OrtensiaSealMax;
            return hall;
        }

        /// <summary>張り直しまでの月数（月のはじめに数える。0なら次の戦いのはじめ）。</summary>
        public static int MonthsUntilReseal(RulerState ruler) => Math.Max(0, RulerBalance.OrtensiaResealMonths - ruler.ResealCounter);

        // ==================== 見立て ====================

        /// <summary>行き先の名前。</summary>
        public static string TargetLabel(CorridorTarget t) => t switch
        {
            CorridorTarget.Skeleton => "骸骨兵の間",
            CorridorTarget.KnightKing => "騎士王の間",
            CorridorTarget.Illusion => "幻惑の間",
            _ => "玉座",
        };

        /// <summary>行き先で見る能力。</summary>
        public static string TargetStatLabel(CorridorTarget t) => t switch
        {
            CorridorTarget.Skeleton => "全員の STR・VIT の合計",
            CorridorTarget.KnightKing => "いちばん強い1人の STR・LDR",
            CorridorTarget.Illusion => "INT・MND の平均",
            _ => "全員の STR・DEX・INT のいちばん高いものの合計・威圧〈LDR〉",
        };

        /// <summary>閾値（前哨戦60・決着80）。</summary>
        public static int Threshold(bool outpost) => outpost ? RulerBalance.OrtensiaThresholdOutpost : RulerBalance.OrtensiaThresholdFinal;

        /// <summary>1人の、その行き先に合う能力（装備込み）。</summary>
        public static double MemberValue(Adventurer a, CorridorTarget t) => t switch
        {
            CorridorTarget.Skeleton => (a.GetEffectiveStat("STR") + a.GetEffectiveStat("VIT")) / 2.0,
            CorridorTarget.KnightKing => (a.GetEffectiveStat("STR") + a.GetEffectiveStat("LDR")) / 2.0,
            CorridorTarget.Illusion => (a.GetEffectiveStat("INT") + a.GetEffectiveStat("MND")) / 2.0,
            _ => Math.Max(a.GetEffectiveStat("STR"), Math.Max(a.GetEffectiveStat("DEX"), a.GetEffectiveStat("INT"))),
        };

        /// <summary>ギルドのG1の優勝の数（引退・除籍した子を含む）。</summary>
        public static int GuildG1Wins(GameState state) =>
            state.Adventurers.Concat(state.RetiredAdventurers).Concat(state.FallenAdventurers).DistinctBy(a => a.Id)
                .Sum(a => a.TournamentRecords.Count(r => r.Placing == 1 && r.Grade == TournamentGrade.G1));

        /// <summary>威圧の閾値：閾値からG1の優勝1回につき1下げる（最大15）。</summary>
        public static int AweThreshold(GameState state, bool outpost) =>
            Threshold(outpost) - Math.Min(RulerBalance.OrtensiaAweFameMax, GuildG1Wins(state) * RulerBalance.OrtensiaAweFamePerG1);

        /// <summary>二つ名（誉以上の称号）を持つか。威圧を受けない。</summary>
        public static bool HasEpithet(GameState state, Adventurer a) => HonorSystem.GetTitles(state, a).Any(t => t.Rank >= TitleRank.Honor);

        /// <summary>その行き先で外れる子か（幻惑の間で惑わされる・玉座で威圧に動けない）。</summary>
        public static bool IsExcluded(GameState state, Adventurer a, CorridorTarget t, bool outpost) => t switch
        {
            CorridorTarget.Illusion => MemberValue(a, t) < Threshold(outpost) * RulerBalance.OrtensiaIllusionRate,
            CorridorTarget.Throne => !HasEpithet(state, a) && a.GetEffectiveStat("LDR") < AweThreshold(state, outpost) * RulerBalance.OrtensiaAweRate,
            _ => false,
        };

        /// <summary>
        /// 部隊の合い具合：骸骨兵の間と玉座は合計÷（閾値×4）、騎士王の間はいちばん強い1人÷閾値、幻惑の間は惑わされない子の平均÷閾値。
        /// 外れる子は数えない（全員外れれば0）。
        /// </summary>
        public static double SquadFit(GameState state, IReadOnlyList<Adventurer> members, CorridorTarget t, bool outpost)
        {
            var fighting = members.Where(a => !IsExcluded(state, a, t, outpost)).ToList();
            if (fighting.Count == 0) return 0;
            double threshold = Threshold(outpost);
            return t switch
            {
                CorridorTarget.Skeleton or CorridorTarget.Throne => fighting.Sum(a => MemberValue(a, t)) / (threshold * RulerBalance.OrtensiaFullSquadSize),
                CorridorTarget.KnightKing => fighting.Max(a => MemberValue(a, t)) / threshold,
                _ => fighting.Average(a => MemberValue(a, t)) / threshold,
            };
        }

        /// <summary>援軍の守りの合い具合（VIT の平均÷閾値）。</summary>
        public static double GuardFit(IReadOnlyList<Adventurer> members, bool outpost) =>
            members.Count == 0 ? 0 : members.Average(a => a.GetEffectiveStat("VIT")) / Threshold(outpost);

        private List<Adventurer> Alive(int squad) => _squads[squad].Members.Where(a => State.Adventurers.Contains(a) && a.CurrentHP > 1 && a.Injury != InjurySeverity.Severe).ToList();

        /// <summary>行き先に相手がいるか（封印が残っている間・開いた玉座）。</summary>
        private bool HasFoe(CorridorTarget t) => t == CorridorTarget.Throne ? ThroneOpen(Ruler) : Ruler.SealHp[(int)t] > 0;

        // ==================== 進め方 ====================

        public override void Advance()
        {
            Prompt = DuelPromptKind.None;
            while (!IsOver)
            {
                if (OverByHp) { IsOver = true; break; }

                // 張り直し：戦いのはじめに1回。援軍を出せるなら聞く
                if (!_resealHandled)
                {
                    _resealHandled = true;
                    if (Ruler.ResealPending && AnyBroken(Ruler))
                    {
                        if (InterventionsLeft > 0 && GuardCandidates().Count > 0)
                        {
                            Prompt = DuelPromptKind.Reinforce;
                            return;
                        }
                        Resealed = ApplyReseal(Ruler);
                    }
                    Ruler.ResealPending = false;
                    continue;
                }

                if (_queue.Count == 0) { IsOver = true; break; }
                var (squad, target, redirected) = _queue[0];
                _queue.RemoveAt(0);
                if (Alive(squad).Count == 0) continue;
                var assault = Attack(squad, target, redirected);

                // 転進：封印を破った・相手のいなかった部隊を、同じ月のうちに別の行き先へ回す
                if ((assault.BrokeSeal || assault.Idle) && InterventionsLeft > 0 && Alive(squad).Count > 0 && RedirectCandidates(squad).Count > 0)
                {
                    _redirectSquad = squad;
                    Prompt = DuelPromptKind.Redirect;
                    return;
                }
            }
        }

        /// <summary>援軍に回せる部隊（守りの合い具合が届く部隊、合う順）。</summary>
        public List<(SavedParty Squad, double Fit)> GuardCandidates() =>
            Enumerable.Range(0, _squads.Count)
                .Select(i => (Index: i, Fit: GuardFit(Alive(i), IsOutpost)))
                .Where(x => x.Fit >= RulerBalance.OrtensiaGuardRate)
                .OrderByDescending(x => x.Fit)
                .Select(x => (_squads[x.Index].Squad, x.Fit)).ToList();

        /// <summary>転進できる行き先（相手がいて、合い具合が届く行き先、合う順）。</summary>
        public List<(CorridorTarget Target, double Fit)> RedirectCandidates(int squad) =>
            Enum.GetValues<CorridorTarget>().Where(HasFoe)
                .Select(t => (Target: t, Fit: SquadFit(State, Alive(squad), t, IsOutpost)))
                .Where(x => x.Fit >= RulerBalance.OrtensiaBreakRate)
                .OrderByDescending(x => x.Target == CorridorTarget.Throne).ThenByDescending(x => x.Fit)
                .ToList();

        /// <summary>行き先を介入の選択肢の Id にする（Guid の先頭に行き先の番号）。</summary>
        public static Guid TargetChoiceId(CorridorTarget t) => new((int)t + 1, 0, 0, new byte[8]);

        public override List<PromptChoice> PromptChoices() => Prompt switch
        {
            DuelPromptKind.Reinforce => GuardCandidates().Select(c => new PromptChoice(c.Squad.Id, $"{c.Squad.Name}（守り {c.Fit * 100:F0}%）")).ToList(),
            DuelPromptKind.Redirect when _redirectSquad >= 0 => RedirectCandidates(_redirectSquad)
                .Select(c => new PromptChoice(TargetChoiceId(c.Target), $"{TargetLabel(c.Target)}（合い具合 {c.Fit * 100:F0}%）")).ToList(),
            _ => new List<PromptChoice>(),
        };

        public override void Answer(bool use, Guid? choice)
        {
            if (Prompt == DuelPromptKind.Reinforce)
            {
                Prompt = DuelPromptKind.None;
                int guard = use && choice is Guid id ? _squads.FindIndex(s => s.Squad.Id == id) : -1;
                if (guard < 0)
                {
                    Resealed = ApplyReseal(Ruler);
                    return;
                }
                InterventionsLeft--;
                Ruler.ResealPending = false;
                GuardSquadName = _squads[guard].Squad.Name;
                _queue.RemoveAll(q => q.Squad == guard);
                foreach (var a in Alive(guard))
                {
                    var loss = ApplyLoss(a, Rng.NextInt(RulerBalance.OrtensiaWinHpLossPctMin, RulerBalance.OrtensiaWinHpLossPctMax));
                    _guardLossLines.AddRange(RulerDuel.LossLines(a.Name, loss.Guard, loss.SixthSense, loss.NearDeath, loss.Fallen));
                }
            }
            else if (Prompt == DuelPromptKind.Redirect)
            {
                Prompt = DuelPromptKind.None;
                int squad = _redirectSquad;
                _redirectSquad = -1;
                var target = Enum.GetValues<CorridorTarget>().Cast<CorridorTarget?>().FirstOrDefault(t => choice == TargetChoiceId(t!.Value));
                if (!use || target is not { } to || squad < 0 || RedirectCandidates(squad).All(c => c.Target != to)) return;
                InterventionsLeft--;
                // 間へ回るなら玉座へ向かう部隊より先に（封印が破れれば、玉座の部隊が入れる）
                int at = to == CorridorTarget.Throne ? _queue.Count : _queue.FindIndex(q => q.Target == CorridorTarget.Throne);
                _queue.Insert(at < 0 ? _queue.Count : at, (squad, to, true));
            }
        }

        private readonly List<StoryLine> _guardLossLines = new();

        private CorridorAssault Attack(int squadIndex, CorridorTarget target, bool redirected)
        {
            var members = Alive(squadIndex);
            var a = new CorridorAssault { SquadName = _squads[squadIndex].Squad.Name, Target = target, Redirected = redirected };
            Assaults.Add(a);
            if (!HasFoe(target))
            {
                a.Idle = true;
                return a;
            }
            var excluded = members.Where(m => IsExcluded(State, m, target, IsOutpost)).ToList();
            a.Excluded.AddRange(excluded.Select(m => m.Name));
            a.Fit = SquadFit(State, members, target, IsOutpost);
            a.Won = a.Fit >= RulerBalance.OrtensiaBreakRate;
            if (target == CorridorTarget.Throne)
            {
                a.Damage = a.Won
                    ? Math.Clamp((int)Math.Round(RulerBalance.OrtensiaThroneDamageBase * a.Fit), RulerBalance.OrtensiaThroneDamageMin, RulerBalance.OrtensiaThroneDamageMax)
                    : 0;
                Ruler.Hp = Math.Max(0, Ruler.Hp - a.Damage);
                a.After = Ruler.Hp;
            }
            else
            {
                int i = (int)target;
                a.Damage = a.Won ? Math.Min(Ruler.SealHp[i], (int)Math.Round(RulerBalance.OrtensiaSealDamageBase * a.Fit)) : 0;
                Ruler.SealHp[i] -= a.Damage;
                a.After = Ruler.SealHp[i];
                a.BrokeSeal = a.Won && Ruler.SealHp[i] <= 0;
                a.ThroneOpened = a.BrokeSeal && ThroneOpen(Ruler);
            }

            foreach (var m in members)
            {
                bool lost = !a.Won || excluded.Contains(m);
                int pct = lost
                    ? (int)Math.Round(RulerBalance.OrtensiaLossHpLossPctMin + (RulerBalance.OrtensiaLossHpLossPctMax - RulerBalance.OrtensiaLossHpLossPctMin)
                        * (excluded.Contains(m) ? 1.0 : Math.Clamp((RulerBalance.OrtensiaBreakRate - a.Fit) / 0.35, 0, 1)))
                    : Rng.NextInt(RulerBalance.OrtensiaWinHpLossPctMin, RulerBalance.OrtensiaWinHpLossPctMax);
                var loss = ApplyLoss(m, pct);
                a.LossLines.AddRange(RulerDuel.LossLines(m.Name, loss.Guard, loss.SixthSense, loss.NearDeath, loss.Fallen));
            }

            if (a.BrokeSeal || (target == CorridorTarget.Throne && a.Won))
            {
                State.AddMaterial(RulerSystem.SealMaterialId, 1); // 古王の封蝋（§0.130）
                MaterialsGained++;
            }
            return a;
        }

        // ==================== 語り ====================

        public override List<StoryLine> TakeNewLines()
        {
            var lines = new List<StoryLine>();
            if (_linesShown < 0)
            {
                lines.Add(new(StoryLineKind.Narration, $"{(IsOutpost ? "廃墟の70階" : "廃墟の100階")}、玉座への回廊。オルテンシアの体力 {HpBefore}/{RulerBalance.MaxHp}" +
                    (IsOutpost ? $"（{RulerBalance.OutpostRetreatHp}を切ると退く）" : "")));
                lines.Add(new(StoryLineKind.Narration, "封印　" + string.Join("・", Enumerable.Range(0, HallCount).Select(i =>
                    $"{TargetLabel((CorridorTarget)i)} {(Ruler.SealHp[i] > 0 ? Ruler.SealHp[i].ToString() : "破れている")}"))));
                lines.Add(new(StoryLineKind.Speech, IsOutpost ? "妾の回廊へよう来た。……玉座まで辿り着けるものなら、辿り着いてみせよ" : "ここは古き都の正しき玉座。そなたらの足で汚してよい床ではない", RulerSystem.OrtensiaName));
                _linesShown = 0;
            }
            if (_resealLinesShown == false && (Resealed != null || GuardSquadName != null))
            {
                _resealLinesShown = true;
                if (GuardSquadName != null)
                {
                    lines.Add(new(StoryLineKind.Narration, $"護符の声で、{GuardSquadName}が封印の祭壇を守りに回った。オルテンシアの張り直しを食い止めた！"));
                    lines.AddRange(_guardLossLines);
                }
                else if (Resealed is { } hall)
                    lines.Add(new(StoryLineKind.Narration, $"オルテンシアが杖を掲げる。{TargetLabel(hall)}の封印が張り直された（{RulerBalance.OrtensiaSealMax}）"));
            }
            foreach (var a in Assaults.Skip(_linesShown))
            {
                string head = a.Redirected ? $"護符の声で、{a.SquadName}が{TargetLabel(a.Target)}へ転進する" : $"{a.SquadName}が{TargetLabel(a.Target)}へ向かう";
                if (a.Idle)
                {
                    lines.Add(new(StoryLineKind.Narration, a.Target == CorridorTarget.Throne
                        ? $"{head}……玉座の扉は、まだ閉ざされている"
                        : $"{head}……封印はもう破れていて、相手はいない"));
                    continue;
                }
                lines.Add(new(StoryLineKind.Narration, $"{head}（{TargetStatLabel(a.Target)}）"));
                foreach (var name in a.Excluded)
                    lines.Add(new(StoryLineKind.Narration, a.Target == CorridorTarget.Throne
                        ? $"オルテンシアの威圧に、{name}が膝をついて動けなくなった"
                        : $"幻惑に、{name}が惑わされて仲間を見失った"));
                if (a.Target == CorridorTarget.Throne)
                    lines.Add(new(StoryLineKind.Narration, a.Won
                        ? $"{a.SquadName}の刃が玉座に届いた！（合い具合 {a.Fit * 100:F0}%）　オルテンシアの体力 {a.After}（−{a.Damage}）"
                        : $"{a.SquadName}は玉座の前で押し返された（合い具合 {a.Fit * 100:F0}%）"));
                else
                    lines.Add(new(StoryLineKind.Narration, !a.Won
                        ? $"{a.SquadName}は押し返された（合い具合 {a.Fit * 100:F0}%）"
                        : a.BrokeSeal
                            ? $"{a.SquadName}が{TargetLabel(a.Target)}の封印を破った！（合い具合 {a.Fit * 100:F0}%）"
                            : $"{a.SquadName}が封印を削った（合い具合 {a.Fit * 100:F0}%）　{TargetLabel(a.Target)}の封印 {a.After}（−{a.Damage}）"));
                lines.AddRange(a.LossLines);
                if (a.BrokeSeal || (a.Target == CorridorTarget.Throne && a.Won))
                    lines.Add(new(StoryLineKind.Narration, "砕けた封蝋の欠片を拾った（古王の封蝋）"));
                if (a.ThroneOpened)
                {
                    lines.Add(new(StoryLineKind.Narration, "3つの封印がすべて破れ、玉座の扉が開いた"));
                    lines.Add(new(StoryLineKind.Speech, "……ほう。妾の前に立つか", RulerSystem.OrtensiaName));
                }
            }
            _linesShown = Assaults.Count;
            return lines;
        }

        private bool _resealLinesShown;

        public override List<StoryLine> PromptLines() => Prompt switch
        {
            DuelPromptKind.Reinforce when NextReseal(Ruler) is { } hall => new()
            {
                new(StoryLineKind.Narration, $"オルテンシアが杖を掲げた。{TargetLabel(hall)}の封印を張り直すつもりだ……"),
                new(StoryLineKind.Narration, "【護符】援軍を送れば、部隊を守りに回して止められる"),
            },
            DuelPromptKind.Redirect => new()
            {
                new(StoryLineKind.Narration, $"{PendingSquad?.Name}の手が空いた。まだ戦える"),
                new(StoryLineKind.Narration, "【護符】転進を伝えれば、別の間や玉座へ回せる"),
            },
            _ => new List<StoryLine>(),
        };

        public override (string Text, string Ok, string Cancel) PromptDialog()
        {
            string left = $"[color=gray]声が届くのは、この戦いであと{InterventionsLeft}回。[/color]";
            if (Prompt == DuelPromptKind.Reinforce && NextReseal(Ruler) is { } hall)
                return ($"オルテンシアが[b]{TargetLabel(hall)}[/b]の封印を張り直そうとしている。\n援軍を送れば、選んだ部隊が守りに回って張り直しを防ぐ（その部隊はこの月、攻めない）。\n{left}", "援軍を送る", "送らない");
            return ($"{PendingSquad?.Name}の手が空いた。\n転進を伝えて、同じ月のうちに別の行き先へ回しますか？（いまの HP のまま戦う）\n{left}", "転進を伝える", "伝えない");
        }

        public override List<StoryLine> ClosingLines()
        {
            var lines = new List<StoryLine>();
            int dealt = HpBefore - Ruler.Hp;
            if (Retreated)
            {
                lines.Add(new(StoryLineKind.Speech, "……興が削がれた。今日のところは下がってやろう。妾の玉座は、まだ奥にある", RulerSystem.OrtensiaName));
                lines.Add(new(StoryLineKind.Narration, "オルテンシアは崩れた回廊の奥へ消えた。廃墟の70階の先へ進めるようになった"));
            }
            else if (Defeated)
            {
                lines.Add(new(StoryLineKind.Speech, "……妾の都は、もう、どこにも無いのだな", RulerSystem.OrtensiaName));
                lines.Add(new(StoryLineKind.Narration, "折れた王冠が床に転がった。廃墟の100階を制した"));
            }
            if (LegendaryFound is { } shroud)
                lines.Add(new(StoryLineKind.Narration, $"玉座に残された「{shroud.Name}」を手に入れた（伝説級。持つ子が部隊にいれば、主の群れを対策済みにする）"));
            if (Defeated)
                lines.Add(new(StoryLineKind.Narration, "古王の寝所が開いた（採用試験に毎回1人、名のある家の娘が加わる）"));
            if (SealsGained > 0)
                lines.Add(new(StoryLineKind.Narration, $"古王の封蝋を{SealsGained}つ持ち帰った（売ることもできる）"));
            if (!Retreated && !Defeated)
            {
                string seals = string.Join("・", Enumerable.Range(0, HallCount).Select(i => $"{TargetLabel((CorridorTarget)i).Replace("の間", "")} {(Ruler.SealHp[i] > 0 ? Ruler.SealHp[i].ToString() : "破")}"));
                lines.Add(new(StoryLineKind.Speech, dealt > 0 ? "下賤の刃が、妾に届くとはな。……次は無いと思え" : ThroneOpen(Ruler) ? "玉座の前で震えておるがよい" : "回廊も越えられぬか。出直せ", RulerSystem.OrtensiaName));
                lines.Add(new(StoryLineKind.Narration, $"回廊の戦いは終わった。封印　{seals}。オルテンシアの体力 {Ruler.Hp}/{RulerBalance.MaxHp}" +
                    (dealt > 0 ? $"（この戦いで−{dealt}）" : "") + $"。破れた封印は{RulerBalance.OrtensiaResealMonths}か月ごとに1つ張り直される"));
            }
            return lines;
        }
    }
}
