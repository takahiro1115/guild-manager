using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>ネフェリスとの戦いの段（§0.132）。4人の支配者の戦い方を1段ずつ。</summary>
    public enum NepherisStage
    {
        /// <summary>決闘（茨の影）：部隊でいちばん強い1人の力。</summary>
        Duel,
        /// <summary>調べ（水の影）：その月の調べに合う能力の平均。</summary>
        Song,
        /// <summary>間（王の影）：その月の間の見方。</summary>
        Hall,
        /// <summary>灼熱（炎の影）：全員の力の合計。灼熱で HP が削れる。</summary>
        Heat,
    }

    /// <summary>1段の結果（ネフェリス）。</summary>
    public sealed class NepherisStageResult
    {
        public NepherisStage Stage { get; init; }
        public string SquadName { get; init; } = "";
        /// <summary>調べが分からず、順番どおりの部隊が受け持った。</summary>
        public bool Blind { get; init; }
        public bool Pushed { get; init; }
        /// <summary>都の声で打ち消した（届かなかった段を、削ったことにした）。</summary>
        public bool Voice { get; init; }
        public double Fit { get; init; }
        public bool Won { get; set; }
        public int Damage { get; set; }
        public int RulerHpAfter { get; set; }
        /// <summary>倒していない支配者の仕掛けで外れた子。</summary>
        public List<string> Excluded { get; } = new();
        /// <summary>この段で灼熱を受けた。</summary>
        public bool Burned { get; set; }
        public List<StoryLine> LossLines { get; } = new();
    }

    /// <summary>
    /// ネフェリスとの戦い（心臓の間・まとめ、§0.132、設計書 §3-10）。部隊を最大3つ出し、1回の戦いで4つの段（決闘・調べ・間・灼熱）を戦う。
    /// 各段はいちばん合う部隊が受け持つ（調べが分からない段は順番どおり）。合い具合が届けば体力を削る。
    /// **まだ倒していない支配者がいると、その段が重くなる**：ヴェスペラ＝決闘で魅了（MND）、メルジーネ＝調べが分からない・甘い調べで魅了（INT）、
    /// オルテンシア＝間で威圧（LDR）、イグニア＝灼熱が4つの段すべてにかかる。決着（深淵の100F）を倒すとクリア。
    /// 介入は「押し切れ」（護符。合い具合×1.3、灼熱×1.5）と「都の声」（真相のあと。護符とは別に1回：届かない段を打ち消す）。
    /// </summary>
    public sealed class NepherisBattle : RulerBattle
    {
        public const int StageCount = 4;
        private readonly List<(SavedParty Squad, List<Adventurer> Members)> _squads;
        private int _stage;
        private int _askedPush = -1;
        private int _askedVoice = -1;
        private bool _pushed;
        private bool _voice;
        private bool _voiceUsed;
        private int _linesShown = -1;

        public List<NepherisStageResult> Stages { get; } = new();
        public override string Background => "深淵の特異点・心臓の間";
        /// <summary>心臓のかけらを持ち帰った数。</summary>
        public int ShardsGained => MaterialsGained;

        internal NepherisBattle(GameState state, RulerState ruler, FloorBoss boss, List<(SavedParty Squad, List<Adventurer> Members)> squads, IRng rng, int interventions)
            : base(state, ruler, boss, squads.SelectMany(s => s.Members), rng, interventions)
        {
            _squads = squads;
            if (ruler.SongOrder.Count != 1) Reroll(ruler, state.WeekNumber);
        }

        // ==================== 月ごとの調べと間 ====================

        /// <summary>その月の調べと間を引き直す（週から決まるくじ。乱数の流れを変えない）。</summary>
        public static void Reroll(RulerState ruler, int week)
        {
            ruler.SongOrder = MelusineBattle.ShuffleSongs(week * 3 + 1).Take(1).ToList();
            ruler.StageHall = (CorridorTarget)new SeededRng(week * 197 + 3).NextInt(0, OrtensiaBattle.HallCount - 1);
        }

        /// <summary>その月の調べ。</summary>
        public static SongMode SongOf(RulerState ruler) => ruler.SongOrder.Count > 0 ? ruler.SongOrder[0] : SongMode.Fierce;

        protected override void OnFinished() => Reroll(Ruler, State.WeekNumber + 1);

        // ==================== 見立て ====================

        /// <summary>段の名前。</summary>
        public static string StageLabel(NepherisStage s) => s switch
        {
            NepherisStage.Duel => "決闘（茨の影）",
            NepherisStage.Song => "調べ（水の影）",
            NepherisStage.Hall => "間（王の影）",
            _ => "灼熱（炎の影）",
        };

        /// <summary>段に重さを足す、まだ倒していない支配者。</summary>
        public static string ShadowOf(NepherisStage s) => s switch
        {
            NepherisStage.Duel => RulerSystem.VesperaId,
            NepherisStage.Song => RulerSystem.MelusineId,
            NepherisStage.Hall => RulerSystem.OrtensiaId,
            _ => RulerSystem.IgniaId,
        };

        /// <summary>その段の仕掛けが乗っているか（その支配者をまだ決着で倒していない）。</summary>
        public static bool Shadowed(GameState state, NepherisStage s) => !RulerSystem.IsRulerDefeated(state, ShadowOf(s));

        /// <summary>閾値（前哨戦75・決着95）。</summary>
        public static int Threshold(bool outpost) => outpost ? RulerBalance.NepherisThresholdOutpost : RulerBalance.NepherisThresholdFinal;

        /// <summary>その月の調べが分かるか：メルジーネを倒していて、解析が届いている。</summary>
        public static bool SongKnown(GameState state, RulerState ruler) =>
            !Shadowed(state, NepherisStage.Song) && RulerSystem.IntelOf(state, ruler) >= RulerBalance.NepherisIntelSongReveal;

        /// <summary>都の声が使えるか（真相を見たあと）。</summary>
        public static bool CityVoiceReady(GameState state) => StorySystem.Seen(state, "s04_truth");

        private static double Power(Adventurer a) => Math.Max(a.GetEffectiveStat("STR"), Math.Max(a.GetEffectiveStat("DEX"), a.GetEffectiveStat("INT")));

        /// <summary>その段で外れる子（倒していない支配者の仕掛け）。</summary>
        public static bool IsExcluded(GameState state, RulerState ruler, Adventurer a, NepherisStage s, bool outpost)
        {
            double line = Threshold(outpost) * RulerBalance.NepherisCharmRate;
            return s switch
            {
                NepherisStage.Duel => Shadowed(state, s) && a.GetEffectiveStat("MND") < line,
                NepherisStage.Song => Shadowed(state, s) && SongOf(ruler) == SongMode.Sweet && a.GetEffectiveStat("INT") < line,
                NepherisStage.Hall => (ruler.StageHall == CorridorTarget.Illusion && (a.GetEffectiveStat("INT") + a.GetEffectiveStat("MND")) / 2.0 < line)
                    || (Shadowed(state, s) && !OrtensiaBattle.HasEpithet(state, a)
                        && a.GetEffectiveStat("LDR") < (Threshold(outpost) - Math.Min(RulerBalance.OrtensiaAweFameMax, OrtensiaBattle.GuildG1Wins(state) * RulerBalance.OrtensiaAweFamePerG1)) * RulerBalance.NepherisCharmRate),
                _ => false,
            };
        }

        /// <summary>部隊の、その段の合い具合（外れた子は数えない。全員外れれば0）。</summary>
        public static double SquadFit(GameState state, RulerState ruler, IReadOnlyList<Adventurer> members, NepherisStage s, bool outpost)
        {
            var fighting = members.Where(a => !IsExcluded(state, ruler, a, s, outpost)).ToList();
            if (fighting.Count == 0) return 0;
            double t = Threshold(outpost);
            int full = RulerBalance.OrtensiaFullSquadSize;
            return s switch
            {
                NepherisStage.Duel => fighting.Max(Power) / t,
                NepherisStage.Song => fighting.Average(a => MelusineBattle.MemberFit(a, SongOf(ruler))) / t,
                NepherisStage.Hall => ruler.StageHall switch
                {
                    CorridorTarget.Skeleton => fighting.Sum(a => OrtensiaBattle.MemberValue(a, CorridorTarget.Skeleton)) / (t * full),
                    CorridorTarget.KnightKing => fighting.Max(a => OrtensiaBattle.MemberValue(a, CorridorTarget.KnightKing)) / t,
                    _ => fighting.Average(a => OrtensiaBattle.MemberValue(a, CorridorTarget.Illusion)) / t,
                },
                _ => fighting.Sum(Power) / (t * full),
            };
        }

        /// <summary>この段で灼熱を受けるか（灼熱の段、イグニアを倒していなければすべての段）。</summary>
        public bool Burns(NepherisStage s) => s == NepherisStage.Heat || Shadowed(State, NepherisStage.Heat);

        /// <summary>灼熱の HP の減り（%）：15%×閾値÷VIT（5〜25%）。押し切れのときは×1.5。</summary>
        public static int HeatPct(Adventurer a, bool outpost, bool pushed)
        {
            int pct = Math.Clamp((int)Math.Round(RulerBalance.IgniaHeatPct * Threshold(outpost) / Math.Max(1.0, a.GetEffectiveStat("VIT"))),
                RulerBalance.IgniaHeatPctMin, RulerBalance.IgniaHeatPctMax);
            return pushed ? (int)Math.Round(pct * RulerBalance.IgniaPushHeatRate) : pct;
        }

        private List<Adventurer> Standing(int squad) => _squads[squad].Members.Where(a => State.Adventurers.Contains(a) && IgniaBattle.CanStand(a)).ToList();

        /// <summary>段を受け持つ部隊：調べが分からない段は順番どおり（倒れていれば次）、ほかはいちばん合う部隊。誰も立てなければ -1。</summary>
        private int PickSquad(NepherisStage s)
        {
            var alive = Enumerable.Range(0, _squads.Count).Where(i => Standing(i).Count > 0).ToList();
            if (alive.Count == 0) return -1;
            if (s == NepherisStage.Song && !SongKnown(State, Ruler))
                return alive.FirstOrDefault(i => i >= (int)s % _squads.Count, alive[0]);
            return alive.OrderByDescending(i => SquadFit(State, Ruler, Standing(i), s, IsOutpost)).First();
        }

        /// <summary>介入を待っている段。</summary>
        public NepherisStage PendingStage => (NepherisStage)Math.Min(_stage, StageCount - 1);

        /// <summary>介入を待っている段を受け持つ部隊と、その合い具合。</summary>
        public (SavedParty? Squad, double Fit) PendingSquad()
        {
            int i = PickSquad(PendingStage);
            return i < 0 ? (null, 0) : (_squads[i].Squad, SquadFit(State, Ruler, Standing(i), PendingStage, IsOutpost));
        }

        // ==================== 進め方 ====================

        public override void Advance()
        {
            Prompt = DuelPromptKind.None;
            while (!IsOver)
            {
                if (OverByHp || _stage >= StageCount) { IsOver = true; break; }
                var s = (NepherisStage)_stage;
                int squad = PickSquad(s);
                if (squad < 0) { IsOver = true; break; }
                double fit = SquadFit(State, Ruler, Standing(squad), s, IsOutpost);
                if (fit < RulerBalance.NepherisBreakRate && !_pushed && !_voice)
                {
                    // 押し切れば届くなら「押し切れ」、届かないなら（真相のあと）「都の声」を聞く
                    if (_askedPush != _stage && InterventionsLeft > 0 && fit * RulerBalance.IgniaPushFitRate >= RulerBalance.NepherisBreakRate)
                    {
                        _askedPush = _stage;
                        Prompt = DuelPromptKind.Push;
                        return;
                    }
                    if (_askedVoice != _stage && !_voiceUsed && CityVoiceReady(State))
                    {
                        _askedVoice = _stage;
                        Prompt = DuelPromptKind.CityVoice;
                        return;
                    }
                }
                FightStage(s, squad, fit);
            }
        }

        public override List<PromptChoice> PromptChoices() => new();

        public override void Answer(bool use, Guid? choice)
        {
            var prompt = Prompt;
            Prompt = DuelPromptKind.None;
            if (!use) return;
            if (prompt == DuelPromptKind.Push)
            {
                InterventionsLeft--;
                _pushed = true;
            }
            else if (prompt == DuelPromptKind.CityVoice)
            {
                _voiceUsed = true;
                _voice = true;
            }
        }

        private void FightStage(NepherisStage s, int squad, double fit)
        {
            var members = Standing(squad);
            bool pushed = _pushed, voice = _voice;
            _pushed = _voice = false;
            double eff = voice ? Math.Max(fit, 1.0) : pushed ? fit * RulerBalance.IgniaPushFitRate : fit;
            var excluded = members.Where(a => IsExcluded(State, Ruler, a, s, IsOutpost)).ToList();
            var r = new NepherisStageResult
            {
                Stage = s, SquadName = _squads[squad].Squad.Name, Pushed = pushed, Voice = voice, Fit = eff,
                Blind = s == NepherisStage.Song && !SongKnown(State, Ruler),
            };
            r.Excluded.AddRange(excluded.Select(a => a.Name));
            r.Won = eff >= RulerBalance.NepherisBreakRate;
            r.Damage = r.Won ? Math.Clamp((int)Math.Round(RulerBalance.NepherisDamageBase * eff), RulerBalance.NepherisDamageMin, RulerBalance.NepherisDamageMax) : 0;
            Ruler.Hp = Math.Max(0, Ruler.Hp - r.Damage);
            r.RulerHpAfter = Ruler.Hp;
            r.Burned = Burns(s);

            foreach (var a in members)
            {
                bool lost = !r.Won || excluded.Contains(a);
                int pct;
                if (r.Burned)
                    pct = HeatPct(a, IsOutpost, pushed) + (lost ? RulerBalance.IgniaFailExtraPct : 0);
                else if (!lost)
                    pct = Rng.NextInt(RulerBalance.MelusineWinHpLossPctMin, RulerBalance.MelusineWinHpLossPctMax);
                else
                    pct = (int)Math.Round(RulerBalance.MelusineLossHpLossPctMin + (RulerBalance.MelusineLossHpLossPctMax - RulerBalance.MelusineLossHpLossPctMin)
                        * (excluded.Contains(a) ? 1.0 : Math.Clamp((RulerBalance.NepherisBreakRate - eff) / 0.35, 0, 1)));
                var loss = ApplyLoss(a, pct, pushed ? RulerBalance.IgniaPushFatalRate : 1);
                r.LossLines.AddRange(RulerDuel.LossLines(a.Name, loss.Guard, loss.SixthSense, loss.NearDeath, loss.Fallen));
            }
            if (r.Won)
            {
                State.AddMaterial(RulerSystem.ShardMaterialId, 1); // 心臓のかけら（§0.132）
                MaterialsGained++;
            }
            Stages.Add(r);
            _stage++;
        }

        // ==================== 語り ====================

        /// <summary>段の中身の短い説明（その月の調べ・間を含む）。</summary>
        private string StageDetail(NepherisStage s, bool blind) => s switch
        {
            NepherisStage.Duel => "部隊でいちばん強い1人の力",
            NepherisStage.Song => blind ? "調べはまだ分からない" : $"{MelusineBattle.ModeLabel(SongOf(Ruler))}（{MelusineBattle.ModeStatLabel(SongOf(Ruler))}）",
            NepherisStage.Hall => $"{OrtensiaBattle.TargetLabel(Ruler.StageHall)}（{OrtensiaBattle.TargetStatLabel(Ruler.StageHall)}）",
            _ => "全員の STR・DEX・INT のいちばん高いものの合計",
        };

        private string ExcludedLine(NepherisStage s, string name) => s switch
        {
            NepherisStage.Duel => $"茨の囁きに、{name}が魅入られて刃を下ろした",
            NepherisStage.Song => $"甘い調べに、{name}が聞き惚れて動けなくなった",
            _ => Ruler.StageHall == CorridorTarget.Illusion ? $"{name}が幻惑に惑わされた" : $"王の威圧に、{name}が膝をついた",
        };

        public override List<StoryLine> TakeNewLines()
        {
            var lines = new List<StoryLine>();
            if (_linesShown < 0)
            {
                lines.Add(new(StoryLineKind.Narration, $"{(IsOutpost ? "深淵の70階" : "深淵の100階")}、心臓の間。ネフェリスの体力 {HpBefore}/{RulerBalance.MaxHp}" +
                    (IsOutpost ? $"（{RulerBalance.OutpostRetreatHp}を切ると退く）" : "")));
                var shadows = Enum.GetValues<NepherisStage>().Where(s => Shadowed(State, s)).Select(s => RulerSystem.NameOf(ShadowOf(s))).ToList();
                if (shadows.Count > 0)
                    lines.Add(new(StoryLineKind.Narration, $"ネフェリスの背に、{string.Join("・", shadows)}の影が揺れている"));
                lines.Add(new(StoryLineKind.Speech, IsOutpost ? "ようこそ。小さな王たちを越えて、よく来たわね。……少しだけ、遊んであげましょうね" : "いい子。都の子を連れてきてくれたのね。……でも、ここから先は私のものよ", RulerSystem.NepherisName));
                _linesShown = 0;
            }
            foreach (var r in Stages.Skip(_linesShown))
            {
                lines.Add(new(StoryLineKind.Narration, $"{StageLabel(r.Stage)}　{StageDetail(r.Stage, r.Blind)}。{r.SquadName}が受け持つ"));
                if (r.Pushed)
                    lines.Add(new(StoryLineKind.Narration, $"護符から「押し切れ！」の声。{r.SquadName}が踏み込む"));
                if (r.Voice)
                {
                    lines.Add(new(StoryLineKind.Speech, "……お姉さま。お願い", "ルミナ"));
                    lines.Add(new(StoryLineKind.Narration, "心臓の奥から、やさしい声が響いた。ネフェリスの術がほどけていく"));
                }
                foreach (var name in r.Excluded)
                    lines.Add(new(StoryLineKind.Narration, ExcludedLine(r.Stage, name)));
                lines.Add(new(StoryLineKind.Narration, r.Won
                    ? $"{r.SquadName}が影を破った！（合い具合 {r.Fit * 100:F0}%）　ネフェリスの体力 {r.RulerHpAfter}（−{r.Damage}）"
                    : $"{r.SquadName}は影に押し返された（合い具合 {r.Fit * 100:F0}%）"));
                if (r.Burned)
                    lines.Add(new(StoryLineKind.Narration, $"灼熱が{r.SquadName}を灼く"));
                lines.AddRange(r.LossLines);
                if (r.Won)
                    lines.Add(new(StoryLineKind.Narration, "淡く脈打つかけらが残った（心臓のかけら）"));
            }
            _linesShown = Stages.Count;
            return lines;
        }

        public override List<StoryLine> PromptLines()
        {
            var (squad, _) = PendingSquad();
            return Prompt switch
            {
                DuelPromptKind.Push => new()
                {
                    new(StoryLineKind.Narration, $"{StageLabel(PendingStage)}。{squad?.Name}の手が、あと少し届かない……"),
                    new(StoryLineKind.Narration, "【護符】押し切れと伝えれば、届くかもしれない（灼熱は重くなる）"),
                },
                DuelPromptKind.CityVoice => new()
                {
                    new(StoryLineKind.Narration, $"{StageLabel(PendingStage)}。{squad?.Name}では届かない……"),
                    new(StoryLineKind.Speech, "副官さま……心臓の奥で、誰かが呼んでいます", "ルミナ"),
                },
                _ => new List<StoryLine>(),
            };
        }

        public override (string Text, string Ok, string Cancel) PromptDialog()
        {
            var (squad, fit) = PendingSquad();
            if (Prompt == DuelPromptKind.CityVoice)
                return ($"[b]{StageLabel(PendingStage)}[/b]。{squad?.Name}の合い具合は {fit * 100:F0}%（{RulerBalance.NepherisBreakRate * 100:F0}%に届かない）。\n" +
                        "ルミナに、心臓に残る都の声を呼んでもらえば、この戦いの術を打ち消して削ったことにできる。\n[color=gray]都の声は1回の戦いで1回だけ（護符の回数とは別）。[/color]", "都の声を呼ぶ", "呼ばない");
            return ($"[b]{StageLabel(PendingStage)}[/b]。{squad?.Name}の合い具合は {fit * 100:F0}%（{RulerBalance.NepherisBreakRate * 100:F0}%に届かないと削れない）。\n" +
                    $"押し切れと伝えれば合い具合×{RulerBalance.IgniaPushFitRate}。ただし灼熱は×{RulerBalance.IgniaPushHeatRate}、HP0での強制除籍は×{RulerBalance.IgniaPushFatalRate}。\n" +
                    $"[color=gray]声が届くのは、この戦いであと{InterventionsLeft}回。[/color]", "押し切れと伝える", "伝えない");
        }

        public override List<StoryLine> ClosingLines()
        {
            var lines = new List<StoryLine>();
            int dealt = HpBefore - HpAfter;
            if (Retreated)
            {
                lines.Add(new(StoryLineKind.Speech, "……そう。少しは、やるのね。続きは、心臓の前でしましょうね", RulerSystem.NepherisName));
                lines.Add(new(StoryLineKind.Narration, "ネフェリスは影の中へ溶けていった。深淵の70階の先へ進めるようになった"));
            }
            else if (Defeated)
            {
                lines.Add(new(StoryLineKind.Speech, "……どうして。どうして、いつも姉さまなの", RulerSystem.NepherisName));
                lines.Add(new(StoryLineKind.Narration, "ネフェリスの姿がほどけ、心臓の間に静けさが戻った。深淵の100階を制した"));
            }
            if (LegendaryFound is { } crown)
                lines.Add(new(StoryLineKind.Narration, $"床に残された「{crown.Name}」を手に入れた（伝説級）"));
            if (ShardsGained > 0)
                lines.Add(new(StoryLineKind.Narration, $"心臓のかけらを{ShardsGained}つ持ち帰った（売ることもできる）"));
            if (!Retreated && !Defeated)
            {
                lines.Add(new(StoryLineKind.Speech, dealt > 0 ? "痛いわ。……でも、急がなくていいのよ。あなたたちは、いずれここへ来るのだから" : "かわいい子たち。また来てね", RulerSystem.NepherisName));
                lines.Add(new(StoryLineKind.Narration, $"心臓の間を出た。ネフェリスの体力 {Ruler.Hp}/{RulerBalance.MaxHp}" +
                    (dealt > 0 ? $"（この戦いで−{dealt}）" : "") + $"。毎月{RulerBalance.RegenPerMonth}ずつ回復する"));
            }
            return lines;
        }
    }
}
