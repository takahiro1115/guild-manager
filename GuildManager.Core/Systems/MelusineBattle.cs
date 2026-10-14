using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>1曲の結果（メルジーネ）。</summary>
    public sealed class SongResult
    {
        public int Number { get; init; }
        public SongMode Mode { get; init; }
        /// <summary>調べが前もって分かっていた（解析・ルミナ）。分からない曲は順番どおりの部隊が出る。</summary>
        public bool WasKnown { get; set; }
        /// <summary>ルミナが歌を聞き取った。</summary>
        public bool LuminaHeard { get; set; }
        /// <summary>交代した（前の部隊の名前）。</summary>
        public string? SwappedFrom { get; set; }
        public string SquadName { get; set; } = "";
        /// <summary>合い具合（合う能力の部隊の平均÷閾値）。</summary>
        public double Fit { get; set; }
        public bool Won { get; set; }
        public int Damage { get; set; }
        public int RulerHpAfter { get; set; }
        /// <summary>甘い調べで魅了されて戦わなかった子。</summary>
        public List<string> Charmed { get; } = new();
        /// <summary>損耗の語り（かばい合い・危機察知・一命・強制除籍）。</summary>
        public List<StoryLine> LossLines { get; } = new();
    }

    /// <summary>
    /// メルジーネとの戦い（調べの読み合い、§0.129、設計書 §3-7）。部隊を最大3つ、順番つきで出す。1回の戦いで4曲。
    /// 曲ごとに調べ（激しい・静か・速い・甘い）があり、合う能力の部隊の平均が閾値に届けば削れる。
    /// 調べが分かった曲（解析・ルミナ）はいちばん合う部隊が自動で前に出る。分からない曲は順番どおり。
    /// 介入は「ルミナが歌を聞き取る」（前兆2を見たあと）と「交代」。
    /// </summary>
    public sealed class MelusineBattle : RulerBattle
    {
        public const int SongCount = 4;
        private readonly List<(SavedParty Squad, List<Adventurer> Members)> _squads;
        private int _songIndex;
        private int _linesShown = -1;
        private bool _luminaAskedForSong = false;
        private bool _luminaHeard;
        private int _performer = -1;
        private string? _swappedFrom;
        private bool _swapAsked;

        public List<SongResult> Songs { get; } = new();
        public override string Background => "嘆きの鍾乳洞";
        /// <summary>水妖の真珠を持ち帰った数（打ち破った曲の数）。</summary>
        public int PearlsGained => MaterialsGained;
        /// <summary>介入を待っている曲の調べ（交代のときだけ分かっている）。</summary>
        public SongMode PendingMode => Ruler.SongOrder[_songIndex];
        /// <summary>介入を待っているときに前に出ている部隊（交代のとき）。</summary>
        public SavedParty? PendingSquad => _performer >= 0 ? _squads[_performer].Squad : null;

        internal MelusineBattle(GameState state, RulerState ruler, FloorBoss boss, List<(SavedParty Squad, List<Adventurer> Members)> squads, IRng rng, int interventions)
            : base(state, ruler, boss, squads.SelectMany(s => s.Members), rng, interventions)
        {
            _squads = squads;
            if (ruler.SongOrder.Count != SongCount) ruler.SongOrder = ShuffleSongs(state.WeekNumber);
        }

        // ==================== 調べ ====================

        /// <summary>調べの並び（4曲。週から決まるくじで入れ替える。乱数の流れを変えないよう、専用の種で引く）。</summary>
        public static List<SongMode> ShuffleSongs(int week)
        {
            var modes = Enum.GetValues<SongMode>().ToList();
            var rng = new SeededRng(week * 131 + 7);
            for (int i = modes.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(0, i);
                (modes[i], modes[j]) = (modes[j], modes[i]);
            }
            return modes;
        }

        /// <summary>調べの名前。</summary>
        public static string ModeLabel(SongMode m) => m switch
        {
            SongMode.Fierce => "激しい調べ",
            SongMode.Calm => "静かな調べ",
            SongMode.Swift => "速い調べ",
            _ => "甘い調べ",
        };

        /// <summary>調べに合う能力の名前。</summary>
        public static string ModeStatLabel(SongMode m) => m switch
        {
            SongMode.Fierce => "STR・VIT",
            SongMode.Calm => "MND",
            SongMode.Swift => "AGI・DEX",
            _ => "INT",
        };

        /// <summary>1人の、その調べに合う能力（装備込み。2つなら平均）。</summary>
        public static double MemberFit(Adventurer a, SongMode m) => m switch
        {
            SongMode.Fierce => (a.GetEffectiveStat("STR") + a.GetEffectiveStat("VIT")) / 2.0,
            SongMode.Calm => a.GetEffectiveStat("MND"),
            SongMode.Swift => (a.GetEffectiveStat("AGI") + a.GetEffectiveStat("DEX")) / 2.0,
            _ => a.GetEffectiveStat("INT"),
        };

        /// <summary>閾値（前哨戦60・決着80）。</summary>
        public static int Threshold(bool outpost) => outpost ? RulerBalance.MelusineThresholdOutpost : RulerBalance.MelusineThresholdFinal;

        /// <summary>甘い調べで魅了されるか（INT が閾値×MelusineSweetCharmRate より下）。</summary>
        public static bool IsCharmedBySweet(Adventurer a, bool outpost) => a.GetEffectiveStat("INT") < Threshold(outpost) * RulerBalance.MelusineSweetCharmRate;

        /// <summary>部隊の合い具合（合う能力の平均÷閾値。甘い調べで魅了される子は平均から外す。全員魅了なら0）。</summary>
        public static double SquadFit(IReadOnlyList<Adventurer> members, SongMode m, bool outpost)
        {
            var fighting = m == SongMode.Sweet ? members.Where(a => !IsCharmedBySweet(a, outpost)).ToList() : members.ToList();
            if (fighting.Count == 0) return 0;
            return fighting.Average(a => MemberFit(a, m)) / Threshold(outpost);
        }

        /// <summary>解析で前もって分かる曲の数（30%で1曲・60%で2曲・100%で全曲）。</summary>
        public static int RevealedSongs(double intel) =>
            intel >= RulerBalance.MelusineIntelRevealAll ? SongCount
            : intel >= RulerBalance.MelusineIntelRevealTwo ? 2
            : intel >= RulerBalance.MelusineIntelRevealOne ? 1 : 0;

        /// <summary>ルミナが歌を聞き取れるか（前兆2「水妖の歌」を見たあと）。</summary>
        public static bool LuminaCanListen(GameState state) => StorySystem.Seen(state, "s04_omen2");

        private bool IsKnown(int song) => song < RevealedSongs(RulerSystem.IntelOf(State, Ruler));

        private int BestSquadFor(SongMode m) =>
            Enumerable.Range(0, _squads.Count).OrderByDescending(i => SquadFit(Alive(i), m, IsOutpost)).First();

        private List<Adventurer> Alive(int squad) => _squads[squad].Members.Where(a => State.Adventurers.Contains(a) && a.CurrentHP > 0 && a.Injury != InjurySeverity.Severe).ToList();

        // ==================== 進め方 ====================

        public override void Advance()
        {
            Prompt = DuelPromptKind.None;
            while (!IsOver)
            {
                if (OverByHp || _songIndex >= SongCount) { IsOver = true; break; }
                var mode = Ruler.SongOrder[_songIndex];
                bool known = IsKnown(_songIndex) || _luminaHeard;

                // ルミナが歌を聞き取る：分からない曲の前（1曲に1回だけ聞く）
                if (!known && !_luminaAskedForSong && InterventionsLeft > 0 && LuminaCanListen(State))
                {
                    _luminaAskedForSong = true;
                    Prompt = DuelPromptKind.LuminaListen;
                    return;
                }

                if (_performer < 0)
                {
                    _performer = known ? BestSquadFor(mode) : _songIndex % _squads.Count;
                    if (Alive(_performer).Count == 0) _performer = BestSquadFor(mode);
                }

                // 交代：前に出た部隊が呑まれかけている（合い具合が足りない）とき、もっと合う部隊がいれば聞く（1曲に1回）
                double fit = SquadFit(Alive(_performer), mode, IsOutpost);
                if (!_swapAsked && fit < RulerBalance.MelusineSwallowRate && InterventionsLeft > 0 && SwapCandidates().Count > 0)
                {
                    _swapAsked = true;
                    Prompt = DuelPromptKind.Swap;
                    return;
                }

                PerformSong(mode, known);
            }
        }

        /// <summary>交代できる部隊（今の部隊より合う部隊、合う順）。</summary>
        public List<(SavedParty Squad, double Fit)> SwapCandidates()
        {
            if (_performer < 0) return new();
            var mode = Ruler.SongOrder[_songIndex];
            double current = SquadFit(Alive(_performer), mode, IsOutpost);
            return Enumerable.Range(0, _squads.Count).Where(i => i != _performer)
                .Select(i => (Index: i, Fit: SquadFit(Alive(i), mode, IsOutpost)))
                .Where(x => x.Fit > current)
                .OrderByDescending(x => x.Fit)
                .Select(x => (_squads[x.Index].Squad, x.Fit)).ToList();
        }

        public override List<PromptChoice> PromptChoices() =>
            Prompt == DuelPromptKind.Swap
                ? SwapCandidates().Select(c => new PromptChoice(c.Squad.Id, $"{c.Squad.Name}（合い具合 {c.Fit * 100:F0}%）")).ToList()
                : new List<PromptChoice>();

        public override void Answer(bool use, Guid? choice)
        {
            if (Prompt == DuelPromptKind.LuminaListen)
            {
                Prompt = DuelPromptKind.None;
                if (!use) return;
                InterventionsLeft--;
                _luminaHeard = true;
            }
            else if (Prompt == DuelPromptKind.Swap)
            {
                Prompt = DuelPromptKind.None;
                int to = choice is Guid id ? _squads.FindIndex(s => s.Squad.Id == id) : -1;
                if (!use || to < 0) return;
                InterventionsLeft--;
                _swappedFrom = _squads[_performer].Squad.Name;
                _performer = to;
            }
        }

        private void PerformSong(SongMode mode, bool known)
        {
            var (squad, _) = _squads[_performer];
            var members = Alive(_performer);
            var song = new SongResult
            {
                Number = _songIndex + 1, Mode = mode, WasKnown = known, LuminaHeard = _luminaHeard,
                SwappedFrom = _swappedFrom, SquadName = squad.Name,
            };
            var charmed = mode == SongMode.Sweet ? members.Where(a => IsCharmedBySweet(a, IsOutpost)).ToList() : new List<Adventurer>();
            song.Charmed.AddRange(charmed.Select(a => a.Name));
            song.Fit = SquadFit(members, mode, IsOutpost);
            song.Won = song.Fit >= RulerBalance.MelusineSwallowRate;
            song.Damage = song.Won
                ? Math.Clamp((int)Math.Round(RulerBalance.MelusineDamageBase * song.Fit), RulerBalance.MelusineDamageMin, RulerBalance.MelusineDamageMax)
                : 0;
            Ruler.Hp = Math.Max(0, Ruler.Hp - song.Damage);
            song.RulerHpAfter = Ruler.Hp;

            foreach (var a in members)
            {
                bool lost = !song.Won || charmed.Contains(a);
                int pct = lost
                    ? (int)Math.Round(RulerBalance.MelusineLossHpLossPctMin + (RulerBalance.MelusineLossHpLossPctMax - RulerBalance.MelusineLossHpLossPctMin)
                        * (charmed.Contains(a) ? 1.0 : Math.Clamp((RulerBalance.MelusineSwallowRate - song.Fit) / 0.35, 0, 1)))
                    : Rng.NextInt(RulerBalance.MelusineWinHpLossPctMin, RulerBalance.MelusineWinHpLossPctMax);
                var loss = ApplyLoss(a, pct);
                song.LossLines.AddRange(RulerDuel.LossLines(a.Name, loss.Guard, loss.SixthSense, loss.NearDeath, loss.Fallen));
            }

            if (song.Won)
            {
                State.AddMaterial(RulerSystem.PearlMaterialId, 1); // 水妖の真珠（§0.129）
                MaterialsGained++;
            }
            Songs.Add(song);
            _songIndex++;
            _luminaAskedForSong = false;
            _luminaHeard = false;
            _performer = -1;
            _swappedFrom = null;
            _swapAsked = false;
        }

        protected override void OnFinished() => Ruler.SongOrder = ShuffleSongs(State.WeekNumber + 1); // 次の月の並び

        // ==================== 語り ====================

        public override List<StoryLine> TakeNewLines()
        {
            var lines = new List<StoryLine>();
            if (_linesShown < 0)
            {
                lines.Add(new(StoryLineKind.Narration, $"{(IsOutpost ? "洞窟の70階" : "洞窟の100階")}、地底湖の歌劇場。メルジーネの体力 {HpBefore}/{RulerBalance.MaxHp}" +
                    (IsOutpost ? $"（{RulerBalance.OutpostRetreatHp}を切ると退く）" : "")));
                lines.Add(new(StoryLineKind.Speech, IsOutpost ? "うふふ、来てくれたのね。四曲、ちゃんと聞いていって" : "最後の演目よ。……今夜は、手加減しないわ", RulerSystem.MelusineName));
                _linesShown = 0;
            }
            foreach (var s in Songs.Skip(_linesShown))
            {
                lines.Add(new(StoryLineKind.Narration, s.WasKnown || s.LuminaHeard
                    ? $"第{s.Number}曲　{ModeLabel(s.Mode)}（{ModeStatLabel(s.Mode)}）。いちばん合う{s.SquadName}が前に出る"
                    : $"第{s.Number}曲　調べはまだ分からない。順番どおり{s.SquadName}が前に出る……{ModeLabel(s.Mode)}（{ModeStatLabel(s.Mode)}）だった"));
                if (s.LuminaHeard)
                    lines.Add(new(StoryLineKind.Speech, $"副官さま、{ModeLabel(s.Mode)}です！", "ルミナ"));
                if (s.SwappedFrom != null)
                    lines.Add(new(StoryLineKind.Narration, $"護符の声で、{s.SwappedFrom}と{s.SquadName}が入れ替わった"));
                foreach (var name in s.Charmed)
                    lines.Add(new(StoryLineKind.Narration, $"甘い調べに、{name}が聞き惚れて動けなくなった"));
                lines.Add(new(StoryLineKind.Narration, s.Won
                    ? $"{s.SquadName}が調べを打ち破った！（合い具合 {s.Fit * 100:F0}%）　メルジーネの体力 {s.RulerHpAfter}（−{s.Damage}）"
                    : $"{s.SquadName}は調べに呑まれた（合い具合 {s.Fit * 100:F0}%）"));
                lines.AddRange(s.LossLines);
                if (s.Won)
                    lines.Add(new(StoryLineKind.Narration, "水面に、真珠が1つ浮かんだ（水妖の真珠）"));
            }
            _linesShown = Songs.Count;
            return lines;
        }

        public override List<StoryLine> PromptLines() => Prompt switch
        {
            DuelPromptKind.LuminaListen => new()
            {
                new(StoryLineKind.Narration, $"第{_songIndex + 1}曲の前奏が始まった。調べはまだ分からない……"),
                new(StoryLineKind.Narration, "【護符】ルミナなら、歌の言葉を聞き取れるかもしれない"),
            },
            DuelPromptKind.Swap => new()
            {
                new(StoryLineKind.Narration, $"第{_songIndex + 1}曲は{ModeLabel(PendingMode)}。{PendingSquad?.Name}が調べに呑まれかけている……"),
                new(StoryLineKind.Narration, "【護符】交代を伝えれば、ほかの部隊と入れ替えられる"),
            },
            _ => new List<StoryLine>(),
        };

        public override (string Text, string Ok, string Cancel) PromptDialog()
        {
            string left = $"[color=gray]声が届くのは、この戦いであと{InterventionsLeft}回。[/color]";
            return Prompt == DuelPromptKind.LuminaListen
                ? ($"第{_songIndex + 1}曲の調べは、まだ分からない。\nルミナに歌の言葉を聞き取ってもらえば、調べが分かり、いちばん合う部隊が前に出る。\n{left}", "聞き取ってもらう", "任せる")
                : ($"第{_songIndex + 1}曲は[b]{ModeLabel(PendingMode)}[/b]（{ModeStatLabel(PendingMode)}）。\n{PendingSquad?.Name}の合い具合は {SquadFit(Alive(_performer), PendingMode, IsOutpost) * 100:F0}%（{RulerBalance.MelusineSwallowRate * 100:F0}%に届かないと呑まれる）。\n交代を伝えて、ほかの部隊と入れ替えますか？\n{left}", "交代を伝える", "伝えない");
        }

        public override List<StoryLine> ClosingLines()
        {
            var lines = new List<StoryLine>();
            int dealt = HpBefore - Ruler.Hp;
            if (Retreated)
            {
                lines.Add(new(StoryLineKind.Speech, "あら、今日はここまで。……楽しかったわ。また聞かせてね", RulerSystem.MelusineName));
                lines.Add(new(StoryLineKind.Narration, "メルジーネは地底湖の底へ沈んでいった。洞窟の70階の先へ進めるようになった"));
            }
            else if (Defeated)
            {
                lines.Add(new(StoryLineKind.Speech, "……もう、歌えないわ。うふふ、あなたたち、いい耳をしてる", RulerSystem.MelusineName));
                lines.Add(new(StoryLineKind.Narration, "最後の調べが途切れた。洞窟の100階を制した"));
            }
            if (LegendaryFound is { } earring)
                lines.Add(new(StoryLineKind.Narration, $"湖のほとりに残された「{earring.Name}」を手に入れた（伝説級。持つ子が部隊にいれば、主の再生を対策済みにする）"));
            if (Defeated)
                lines.Add(new(StoryLineKind.Narration, "静寂の泉が湧いた（待機中の過ごし方に「瞑想」が増える。MND を伸ばす）"));
            if (PearlsGained > 0)
                lines.Add(new(StoryLineKind.Narration, $"水妖の真珠を{PearlsGained}つ持ち帰った（売ることもできる）"));
            if (!Retreated && !Defeated)
            {
                lines.Add(new(StoryLineKind.Speech, dealt > 0 ? "なかなかね。次は、もっと上手に聞いて" : "あら、それだけ？　退屈させないでよ", RulerSystem.MelusineName));
                lines.Add(new(StoryLineKind.Narration, $"演目は終わった。メルジーネの体力 {Ruler.Hp}/{RulerBalance.MaxHp}（この戦いで−{dealt}。毎月{RulerBalance.RegenPerMonth}ずつ回復する）"));
            }
            return lines;
        }
    }
}
