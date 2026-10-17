using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>イグニアとの戦いの幕（§0.131）。</summary>
    public enum IgniaAct
    {
        /// <summary>炎の眷属（群れ。部隊全員の STR・AGI の合計）。</summary>
        Swarm,
        /// <summary>炎の術（部隊の MND の平均）。</summary>
        Spell,
        /// <summary>イグニア本体（部隊全員の STR・DEX・INT のいちばん高いものの合計）。</summary>
        Body,
    }

    /// <summary>1幕の結果（イグニア）。</summary>
    public sealed class IgniaActResult
    {
        public int Number { get; init; }
        public IgniaAct Act { get; init; }
        public string SquadName { get; init; } = "";
        /// <summary>前の部隊が崩れて、この幕から前に出た（先鋒でない）。</summary>
        public bool NewFront { get; init; }
        /// <summary>介入「押し切れ」を使った。</summary>
        public bool Pushed { get; init; }
        /// <summary>合い具合（押し切れの倍率込み）。</summary>
        public double Fit { get; init; }
        public bool Won { get; set; }
        public int Damage { get; set; }
        public int RulerHpAfter { get; set; }
        /// <summary>灼熱で削れた HP（最大HPに対する%。部隊でいちばん重かった子）。</summary>
        public int HeatPct { get; set; }
        /// <summary>戦線を離れた子。</summary>
        public List<string> Leavers { get; } = new();
        /// <summary>入れ替え（離れた子 → 入った子）。</summary>
        public (string Out, string In)? Swap { get; set; }
        public List<StoryLine> LossLines { get; } = new();
    }

    /// <summary>
    /// イグニアとの戦い（灼熱の底・消耗戦、§0.131、設計書 §3-9）。部隊を最大3つ、順番つきで出す（先鋒・本隊・後詰め）。
    /// 1回の戦いは3幕（炎の眷属・炎の術・本体）。幕ごとに前の部隊が戦い、合い具合が届けば体力を削る。灼熱で前の部隊の全員の HP が削れ、
    /// HP が3割を切った子は戦線を離れる。戦える子が2人を切ると次の部隊が前に出る。イグニアは毎月20回復する（続けて挑まないと削りきれない）。
    /// 介入は「押し切れ」（合い具合×1.3、灼熱×1.5、強制除籍×2）・「入れ替え」（離れる子を後の部隊の子と替える）・「退け」（届かない幕の前に打ち切る）。
    /// </summary>
    public sealed class IgniaBattle : RulerBattle
    {
        public const int ActCount = 3;
        private readonly List<(SavedParty Squad, List<Adventurer> Members)> _squads;
        private int _front;
        private int _act;
        private int _askedAct = -1;
        private bool _pushed;
        private int _lastFront = -1;
        private readonly List<Adventurer> _leavers = new();
        private int _linesShown = -1;

        public List<IgniaActResult> Acts { get; } = new();
        public override string Background => "焦熱の峡谷・灼熱の底";
        /// <summary>焔の鱗を持ち帰った数（破った幕の数）。</summary>
        public int ScalesGained => MaterialsGained;
        /// <summary>介入「退け」で打ち切った。</summary>
        public bool Withdrew { get; private set; }

        internal IgniaBattle(GameState state, RulerState ruler, FloorBoss boss, List<(SavedParty Squad, List<Adventurer> Members)> squads, IRng rng, int interventions)
            : base(state, ruler, boss, squads.SelectMany(s => s.Members), rng, interventions)
        {
            _squads = squads.Select(s => (s.Squad, s.Members.ToList())).ToList();
        }

        // ==================== 見立て ====================

        /// <summary>幕の名前。</summary>
        public static string ActLabel(IgniaAct act) => act switch
        {
            IgniaAct.Swarm => "炎の眷属",
            IgniaAct.Spell => "炎の術",
            _ => "イグニア本体",
        };

        /// <summary>幕で見る能力。</summary>
        public static string ActStatLabel(IgniaAct act) => act switch
        {
            IgniaAct.Swarm => "全員の STR・AGI の合計",
            IgniaAct.Spell => "MND の平均",
            _ => "全員の STR・DEX・INT のいちばん高いものの合計",
        };

        /// <summary>閾値（前哨戦60・決着80）。</summary>
        public static int Threshold(bool outpost) => outpost ? RulerBalance.IgniaThresholdOutpost : RulerBalance.IgniaThresholdFinal;

        /// <summary>1人の、その幕に合う能力（装備込み）。</summary>
        public static double MemberValue(Adventurer a, IgniaAct act) => act switch
        {
            IgniaAct.Swarm => (a.GetEffectiveStat("STR") + a.GetEffectiveStat("AGI")) / 2.0,
            IgniaAct.Spell => a.GetEffectiveStat("MND"),
            _ => Math.Max(a.GetEffectiveStat("STR"), Math.Max(a.GetEffectiveStat("DEX"), a.GetEffectiveStat("INT"))),
        };

        /// <summary>部隊の合い具合：眷属と本体は合計÷（閾値×4）、術は平均÷閾値。戦える子がいなければ0。</summary>
        public static double SquadFit(IReadOnlyList<Adventurer> fighters, IgniaAct act, bool outpost)
        {
            if (fighters.Count == 0) return 0;
            double threshold = Threshold(outpost);
            return act == IgniaAct.Spell
                ? fighters.Average(a => MemberValue(a, act)) / threshold
                : fighters.Sum(a => MemberValue(a, act)) / (threshold * RulerBalance.IgniaFullSquadSize);
        }

        /// <summary>灼熱で1幕に削れる HP（最大HPに対する%）：15%×閾値÷VIT（5〜25%）。押し切れのときは×1.5。</summary>
        public static int HeatPct(Adventurer a, bool outpost, bool pushed = false)
        {
            int pct = Math.Clamp((int)Math.Round(RulerBalance.IgniaHeatPct * Threshold(outpost) / Math.Max(1.0, a.GetEffectiveStat("VIT"))),
                RulerBalance.IgniaHeatPctMin, RulerBalance.IgniaHeatPctMax);
            return pushed ? (int)Math.Round(pct * RulerBalance.IgniaPushHeatRate) : pct;
        }

        /// <summary>戦える子か（HP が3割以上で、重傷でない）。</summary>
        public static bool CanStand(Adventurer a) =>
            a.Injury != InjurySeverity.Severe && a.CurrentHP >= a.MaxHP * RulerBalance.IgniaLeaveHpRate;

        private List<Adventurer> Fighters(int squad) => _squads[squad].Members.Where(a => State.Adventurers.Contains(a) && CanStand(a)).ToList();

        private bool Collapsed(int squad) => Fighters(squad).Count < Math.Min(RulerBalance.IgniaMinFighters, _squads[squad].Members.Count);

        /// <summary>介入を待っている幕。</summary>
        public IgniaAct PendingAct => (IgniaAct)Math.Min(_act, ActCount - 1);

        /// <summary>前に出ている部隊（介入を待っているとき）。</summary>
        public SavedParty? FrontSquad => _front < _squads.Count ? _squads[_front].Squad : null;

        /// <summary>前に出ている部隊の、介入を待っている幕の合い具合。</summary>
        public double PendingFit => _front < _squads.Count ? SquadFit(Fighters(_front), PendingAct, IsOutpost) : 0;

        // ==================== 進め方 ====================

        public override void Advance()
        {
            Prompt = DuelPromptKind.None;
            while (!IsOver)
            {
                if (OverByHp || _act >= ActCount) { IsOver = true; break; }
                while (_front < _squads.Count && Collapsed(_front)) _front++;
                if (_front >= _squads.Count) { IsOver = true; break; }

                var act = (IgniaAct)_act;
                double fit = SquadFit(Fighters(_front), act, IsOutpost);
                // 合い具合の足りない幕のはじめ：押し切れば届くなら「押し切れ」、届かないなら「退け」を聞く（1幕に1回）
                if (_askedAct != _act && InterventionsLeft > 0 && fit < RulerBalance.IgniaBreakRate)
                {
                    _askedAct = _act;
                    Prompt = fit * RulerBalance.IgniaPushFitRate >= RulerBalance.IgniaBreakRate ? DuelPromptKind.Push : DuelPromptKind.Withdraw;
                    return;
                }

                FightAct(act, fit);
                // 入れ替え：戦線を離れる子がいて、後の部隊に替われる子がいるとき（1幕に1回）
                if (_leavers.Count > 0 && InterventionsLeft > 0 && ReserveCandidates().Count > 0)
                {
                    Prompt = DuelPromptKind.Reserve;
                    return;
                }
                _leavers.Clear();
            }
        }

        /// <summary>入れ替えで入れる子（後の部隊の、戦える子。HP の多い順）。</summary>
        public List<Adventurer> ReserveCandidates() =>
            Enumerable.Range(_front + 1, Math.Max(0, _squads.Count - _front - 1))
                .SelectMany(Fighters)
                .OrderByDescending(a => (double)a.CurrentHP / Math.Max(1, a.MaxHP))
                .ToList();

        /// <summary>入れ替えで外れる子（戦線を離れた子の最初の1人）。</summary>
        public Adventurer? Leaver => _leavers.FirstOrDefault();

        public override List<PromptChoice> PromptChoices() =>
            Prompt == DuelPromptKind.Reserve
                ? ReserveCandidates().Select(a => new PromptChoice(a.Id, $"{a.Name}（HP {a.CurrentHP}/{a.MaxHP}・VIT {a.GetEffectiveStat("VIT"):F0}）")).ToList()
                : new List<PromptChoice>();

        public override void Answer(bool use, Guid? choice)
        {
            var prompt = Prompt;
            Prompt = DuelPromptKind.None;
            switch (prompt)
            {
                case DuelPromptKind.Push:
                    if (!use) return;
                    InterventionsLeft--;
                    _pushed = true;
                    break;
                case DuelPromptKind.Withdraw:
                    if (!use) return;
                    InterventionsLeft--;
                    Withdrew = true;
                    IsOver = true;
                    break;
                case DuelPromptKind.Reserve:
                    var leaver = Leaver;
                    _leavers.Clear();
                    var reserve = choice is Guid id ? ReserveCandidates().FirstOrDefault(a => a.Id == id) : null;
                    if (!use || leaver == null || reserve == null) return;
                    InterventionsLeft--;
                    foreach (var s in _squads) s.Members.Remove(reserve);
                    var front = _squads[_front].Members;
                    front[front.IndexOf(leaver)] = reserve;
                    Acts[^1].Swap = (leaver.Name, reserve.Name);
                    break;
            }
        }

        /// <summary>「退け」は、残りの幕のどれも、まだ崩れていない部隊が押し切っても届かないときだけ使う（シミュレーター・テストの自動の答え）。</summary>
        protected override bool AutoWorthUsing() =>
            Prompt != DuelPromptKind.Withdraw
            || !Enumerable.Range(_act, ActCount - _act).Any(i => Enumerable.Range(_front, _squads.Count - _front)
                .Any(s => SquadFit(Fighters(s), (IgniaAct)i, IsOutpost) * RulerBalance.IgniaPushFitRate >= RulerBalance.IgniaBreakRate));

        private void FightAct(IgniaAct act, double fit)
        {
            var fighters = Fighters(_front);
            bool pushed = _pushed;
            _pushed = false;
            double eff = pushed ? fit * RulerBalance.IgniaPushFitRate : fit;
            var result = new IgniaActResult
            {
                Number = _act + 1, Act = act, SquadName = _squads[_front].Squad.Name,
                NewFront = _lastFront >= 0 && _lastFront != _front, Pushed = pushed, Fit = eff,
            };
            _lastFront = _front;
            result.Won = eff >= RulerBalance.IgniaBreakRate;
            result.Damage = result.Won
                ? Math.Clamp((int)Math.Round(RulerBalance.IgniaDamageBase * eff), RulerBalance.IgniaDamageMin, RulerBalance.IgniaDamageMax)
                : 0;
            Ruler.Hp = Math.Max(0, Ruler.Hp - result.Damage);
            result.RulerHpAfter = Ruler.Hp;

            foreach (var a in fighters)
            {
                int pct = HeatPct(a, IsOutpost, pushed) + (result.Won ? 0 : RulerBalance.IgniaFailExtraPct);
                result.HeatPct = Math.Max(result.HeatPct, pct);
                var loss = ApplyLoss(a, pct, pushed ? RulerBalance.IgniaPushFatalRate : 1);
                result.LossLines.AddRange(RulerDuel.LossLines(a.Name, loss.Guard, loss.SixthSense, loss.NearDeath, loss.Fallen));
                if (!loss.Fallen && !CanStand(a))
                {
                    result.Leavers.Add(a.Name);
                    _leavers.Add(a);
                }
            }
            if (result.Won)
            {
                State.AddMaterial(RulerSystem.ScaleMaterialId, 1); // 焔の鱗（§0.131）
                MaterialsGained++;
            }
            Acts.Add(result);
            _act++;
        }

        // ==================== 語り ====================

        public override List<StoryLine> TakeNewLines()
        {
            var lines = new List<StoryLine>();
            if (_linesShown < 0)
            {
                lines.Add(new(StoryLineKind.Narration, $"{(IsOutpost ? "峡谷の70階" : "峡谷の100階")}、灼熱の底。イグニアの体力 {HpBefore}/{RulerBalance.MaxHp}" +
                    (IsOutpost ? $"（{RulerBalance.OutpostRetreatHp}を切ると退く）" : "")));
                lines.Add(new(StoryLineKind.Speech, IsOutpost ? "ちっ、また来たのかい。……いいさ、焼けるまで付き合ってやるよ" : "あんたら、どこまでもしつこいね。……その匂い、四百年前にも嗅いだよ", RulerSystem.IgniaName));
                _linesShown = 0;
            }
            foreach (var r in Acts.Skip(_linesShown))
            {
                if (r.NewFront)
                    lines.Add(new(StoryLineKind.Narration, $"前の部隊が崩れた。{r.SquadName}が前に出る"));
                lines.Add(new(StoryLineKind.Narration, $"第{r.Number}波　{ActLabel(r.Act)}（{ActStatLabel(r.Act)}）。{r.SquadName}が迎え撃つ"));
                if (r.Pushed)
                    lines.Add(new(StoryLineKind.Narration, $"護符から「押し切れ！」の声。{r.SquadName}が灼熱の中へ踏み込む"));
                lines.Add(new(StoryLineKind.Narration, r.Won
                    ? $"{r.SquadName}が炎を破った！（合い具合 {r.Fit * 100:F0}%）　イグニアの体力 {r.RulerHpAfter}（−{r.Damage}）"
                    : $"{r.SquadName}は炎に押し返された（合い具合 {r.Fit * 100:F0}%）"));
                lines.Add(new(StoryLineKind.Narration, $"灼熱が{r.SquadName}を灼く（HP −{r.HeatPct}%まで）"));
                lines.AddRange(r.LossLines);
                foreach (var name in r.Leavers)
                    lines.Add(new(StoryLineKind.Narration, $"{name}は熱に耐えきれず、戦線を離れた"));
                if (r.Swap is { } swap)
                    lines.Add(new(StoryLineKind.Narration, $"護符の声で、{swap.In}が{swap.Out}と入れ替わった"));
                if (r.Won)
                    lines.Add(new(StoryLineKind.Narration, "灼けた鱗が1枚、足もとに落ちた（焔の鱗）"));
            }
            _linesShown = Acts.Count;
            return lines;
        }

        public override List<StoryLine> PromptLines() => Prompt switch
        {
            DuelPromptKind.Push => new()
            {
                new(StoryLineKind.Narration, $"第{_act + 1}波　{ActLabel(PendingAct)}。{FrontSquad?.Name}の手が、あと少し届かない……"),
                new(StoryLineKind.Narration, "【護符】押し切れと伝えれば、届くかもしれない（灼熱は重くなる）"),
            },
            DuelPromptKind.Withdraw => new()
            {
                new(StoryLineKind.Narration, $"第{_act + 1}波　{ActLabel(PendingAct)}。{FrontSquad?.Name}では届きそうにない……"),
                new(StoryLineKind.Narration, "【護符】退けと伝えれば、ここで打ち切れる"),
            },
            DuelPromptKind.Reserve => new()
            {
                new(StoryLineKind.Narration, $"{Leaver?.Name}が熱に倒れかけている……"),
                new(StoryLineKind.Narration, "【護符】入れ替えを伝えれば、後の部隊の子と替えられる"),
            },
            _ => new List<StoryLine>(),
        };

        public override (string Text, string Ok, string Cancel) PromptDialog()
        {
            string left = $"[color=gray]声が届くのは、この戦いであと{InterventionsLeft}回。[/color]";
            return Prompt switch
            {
                DuelPromptKind.Push => ($"第{_act + 1}波は[b]{ActLabel(PendingAct)}[/b]（{ActStatLabel(PendingAct)}）。\n{FrontSquad?.Name}の合い具合は {PendingFit * 100:F0}%（{RulerBalance.IgniaBreakRate * 100:F0}%に届かないと削れない）。\n" +
                    $"押し切れと伝えれば合い具合×{RulerBalance.IgniaPushFitRate}。ただし灼熱は×{RulerBalance.IgniaPushHeatRate}、HP0での強制除籍は×{RulerBalance.IgniaPushFatalRate}。\n{left}", "押し切れと伝える", "伝えない"),
                DuelPromptKind.Withdraw => ($"第{_act + 1}波は[b]{ActLabel(PendingAct)}[/b]（{ActStatLabel(PendingAct)}）。\n{FrontSquad?.Name}の合い具合は {PendingFit * 100:F0}%。押し切っても届かない。\n" +
                    $"退けと伝えれば、ここで戦いを打ち切る（残りの灼熱を受けない）。\n{left}", "退けと伝える", "伝えない"),
                _ => ($"{Leaver?.Name}が熱に耐えきれず、戦線を離れようとしている。\n入れ替えを伝えれば、後の部隊の子と替えて、次の波を戦わせる。\n{left}", "入れ替える", "入れ替えない"),
            };
        }

        public override List<StoryLine> ClosingLines()
        {
            var lines = new List<StoryLine>();
            int dealt = HpBefore - Ruler.Hp;
            // 退いた・倒したときのイグニアの台詞は、すぐあとの物語の場面（台詞 05 の決闘）に任せ、ここは結果の地の文だけ（§0.144）
            if (Withdrew)
                lines.Add(new(StoryLineKind.Narration, "護符の声で、部隊は灼熱の底から退いた"));
            if (Retreated)
            {
                lines.Add(new(StoryLineKind.Narration, "イグニアは炎の中へ沈んでいった。峡谷の70階の先へ進めるようになった"));
            }
            else if (Defeated)
            {
                lines.Add(new(StoryLineKind.Narration, "灼熱が引いていく。峡谷の100階を制した"));
            }
            if (LegendaryFound is { } mail)
                lines.Add(new(StoryLineKind.Narration, $"焼け跡に残された「{mail.Name}」を手に入れた（伝説級）"));
            if (Defeated)
                lines.Add(new(StoryLineKind.Narration, "イグニアの炉が残った（焔の鱗で、保管庫の武具のアフィックスを1段強くできる）"));
            if (ScalesGained > 0)
                lines.Add(new(StoryLineKind.Narration, $"焔の鱗を{ScalesGained}枚持ち帰った（売ることもできる）"));
            if (!Retreated && !Defeated)
            {
                lines.Add(new(StoryLineKind.Speech, dealt > 0 ? "へえ、ちっとは熱かったよ。……でも、すぐ治っちまうからね" : "ぬるいね。出直しな", RulerSystem.IgniaName));
                lines.Add(new(StoryLineKind.Narration, $"灼熱の底を出た。イグニアの体力 {Ruler.Hp}/{RulerBalance.MaxHp}" +
                    (dealt > 0 ? $"（この戦いで−{dealt}）" : "") + $"。毎月{RulerBalance.IgniaRegenPerMonth}ずつ回復する"));
            }
            return lines;
        }
    }
}
