using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 大迷宮の支配者（2026年10月・§0.127〜、→ docs/検討中_大迷宮の支配者.md）。
    /// 支配者は各フィールドの70F（前哨戦）と100F（決着）の主の代わりに待ち、主の決戦では倒せない（扉前で必ず引き返す）。
    /// 支配者とは支配者戦（支配者ごとに戦い方が違う、→ RulerBattle）で戦う。
    ///  - ヴェスペラ（森、§0.127）：代表を最大5人選ぶ勝ち抜きの一騎打ち（→ RulerDuel、設計書 §3-5）。
    ///  - メルジーネ（洞窟、§0.129）：部隊を最大3つ出す調べの読み合い（→ MelusineBattle、設計書 §3-7）。
    ///  - オルテンシア（廃墟、§0.130）：部隊を最大3つ、行き先を決めて出す玉座への回廊（→ OrtensiaBattle、設計書 §3-8）。
    ///  - イグニア（峡谷、§0.131）：部隊を最大3つ順番に出す3幕の消耗戦（→ IgniaBattle、設計書 §3-9）。
    ///  - ネフェリス（深淵、§0.132）：部隊を最大3つ出す4つの段のまとめ。倒していない支配者の仕掛けが乗る（→ NepherisBattle、設計書 §3-10）。決着でクリア。
    /// 共通：60F（90F）の主を倒すと挑める。出る子・部隊は月のはじめに決め（翌月も残る）、その月はほかに出ない。
    /// 月を過ごし終えたら月報のあとに戦う。体力は月をまたいで残り、毎月回復する。前哨戦は体力50を切ると退き（70Fを倒したことになる）、
    /// 決着は0で撃破。介入は部隊の護符の多頭通信術式。HPが0になった子は、低い確率で強制除籍（§0.125 と同じ決まり）。
    /// </summary>
    public static class RulerSystem
    {
        public const string VesperaId = "vespera";
        public const string VesperaName = "ヴェスペラ";
        public const string MelusineId = "melusine";
        public const string MelusineName = "メルジーネ";
        public const string OrtensiaId = "ortensia";
        public const string OrtensiaName = "オルテンシア";
        public const string IgniaId = "ignia";
        public const string IgniaName = "イグニア";
        public const string NepherisId = "nepheris";
        public const string NepherisName = "ネフェリス";
        public const string ForestId = "forest";
        public const string CaveId = "cave";
        public const string RuinsId = "ruins";
        public const string CanyonId = "canyon";
        public const string AbyssId = "abyss";
        public const int OutpostFloor = 70;
        public const int FinalFloor = 100;

        /// <summary>支配者とフィールドの組（作った順）。</summary>
        private static readonly (string RulerId, string FieldId, string Name)[] Rulers =
        {
            (VesperaId, ForestId, VesperaName),
            (MelusineId, CaveId, MelusineName),
            (OrtensiaId, RuinsId, OrtensiaName),
            (IgniaId, CanyonId, IgniaName),
            (NepherisId, AbyssId, NepherisName),
        };

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
            foreach (var (id, _, _) in Rulers)
                if (state.Rulers.All(r => r.RulerId != id))
                    state.Rulers.Add(new RulerState { RulerId = id, Hp = RulerBalance.MaxHp });
            foreach (var ruler in state.Rulers)
            {
                ruler.RepresentativeIds ??= new List<Guid>();
                ruler.SquadIds ??= new List<Guid>();
                ruler.SongOrder ??= new List<SongMode>();
                ruler.SquadTargets ??= new List<CorridorTarget>();
                ruler.SealHp ??= new List<int>();
                if (ruler.RulerId == OrtensiaId) OrtensiaBattle.EnsureSeals(ruler);
                Refresh(state, ruler);
                if (ruler.RulerId == MelusineId && ruler.SongOrder.Count != MelusineBattle.SongCount)
                    ruler.SongOrder = MelusineBattle.ShuffleSongs(state.WeekNumber);
                if (ruler.RulerId == NepherisId && ruler.SongOrder.Count != 1)
                    NepherisBattle.Reroll(ruler, state.WeekNumber);
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
        public static string NameOf(string rulerId) => Rulers.FirstOrDefault(r => r.RulerId == rulerId).Name ?? rulerId;

        /// <summary>出るのが部隊か（メルジーネ）。そうでなければ代表の子（ヴェスペラ）。</summary>
        public static bool UsesSquads(RulerState ruler) => ruler.RulerId is MelusineId or OrtensiaId or IgniaId or NepherisId;

        /// <summary>出す部隊の上限（メルジーネ・オルテンシア）。</summary>
        public static int MaxSquads(RulerState ruler) => ruler.RulerId switch
        {
            OrtensiaId => RulerBalance.OrtensiaMaxSquads,
            IgniaId => RulerBalance.IgniaMaxSquads,
            NepherisId => RulerBalance.NepherisMaxSquads,
            _ => RulerBalance.MelusineMaxSquads,
        };

        /// <summary>毎月の回復量（イグニアは速い、§0.131）。</summary>
        public static int RegenOf(RulerState ruler) => ruler.RulerId == IgniaId ? RulerBalance.IgniaRegenPerMonth : RulerBalance.RegenPerMonth;

        /// <summary>その支配者が待っている主（まだ倒していない70F、なければ100F）。両方倒していれば null。</summary>
        public static FloorBoss? BossOf(GameState state, RulerState ruler)
        {
            var field = FieldOf(state, ruler);
            if (field == null) return null;
            return field.Bosses.Where(b => (b.Floor == OutpostFloor || b.Floor == FinalFloor) && !b.IsDefeated)
                .OrderBy(b => b.Floor).FirstOrDefault();
        }

        /// <summary>その支配者のフィールド。</summary>
        public static DungeonField? FieldOf(GameState state, RulerState ruler)
        {
            string? fieldId = Rulers.FirstOrDefault(r => r.RulerId == ruler.RulerId).FieldId;
            return fieldId == null ? null : state.DungeonFields.FirstOrDefault(f => f.Id == fieldId);
        }

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
            if (ruler.RulerId == OrtensiaId) OrtensiaBattle.ResetSeals(ruler); // 決着の階では封印も張り直したて
        }

        /// <summary>
        /// 新しい週の処理（週の決算のあと、→ WeekProcessingSystem）：支配者の状態を補い、月のはじめなら体力を回復する。
        /// 出る子・部隊がこの月を過ごし終えていれば、月報のあとの決闘を待つ印を付ける。メルジーネは戦わない月に調べの並びを入れ替える。
        /// </summary>
        public static void ProcessNewWeek(GameState state)
        {
            EnsureRulers(state);
            if (!GameCalendar.IsFirstWeekOfMonth(state.WeekNumber)) return;
            foreach (var ruler in state.Rulers.Where(r => BossOf(state, r) != null))
            {
                ruler.Hp = Math.Min(RulerBalance.MaxHp, ruler.Hp + RegenOf(ruler));
                bool battle = CanChallenge(state, ruler) && HasParticipants(state, ruler);
                if (ruler.RulerId == OrtensiaId && CanChallenge(state, ruler))
                    CountReseal(ruler, battle);
                if (battle)
                    ruler.PendingDuelWeek = state.WeekNumber;
                else if (ruler.RulerId == MelusineId)
                    ruler.SongOrder = MelusineBattle.ShuffleSongs(state.WeekNumber); // 戦う月は、戦い終えてから入れ替える
                else if (ruler.RulerId == NepherisId)
                    NepherisBattle.Reroll(ruler, state.WeekNumber);
            }
        }

        /// <summary>
        /// オルテンシアの張り直しを数える（挑める月のはじめ）。ResealMonths に届いたら、破れた封印があれば張り直す：
        /// この月に戦うなら戦いのはじめ（援軍で防げる）、戦わないならすぐ。前の月に残った張り直しも、戦わないならここで済ませる。
        /// </summary>
        private static void CountReseal(RulerState ruler, bool battle)
        {
            OrtensiaBattle.EnsureSeals(ruler);
            if (ruler.ResealPending && !battle) OrtensiaBattle.ApplyReseal(ruler);
            if (!OrtensiaBattle.AnyBroken(ruler))
            {
                ruler.ResealCounter = 0; // 破れた封印が無いうちは数えない
                return;
            }
            ruler.ResealCounter++;
            if (ruler.ResealCounter < RulerBalance.OrtensiaResealMonths) return;
            ruler.ResealCounter = 0;
            if (battle) ruler.ResealPending = true;
            else OrtensiaBattle.ApplyReseal(ruler);
        }

        // ==================== 出る子・部隊 ====================

        /// <summary>今月、支配者戦に出る（代表の子、または出す部隊のメンバー）か。その月は出撃・訓練・大会に出ない。</summary>
        public static bool IsRepresentative(GameState state, Guid adventurerId) =>
            state.Rulers.Any(r => CanChallenge(state, r) && (UsesSquads(r)
                ? r.SquadIds.Any(sid => state.SavedParties.FirstOrDefault(p => p.Id == sid)?.MemberIds.Contains(adventurerId) == true)
                : r.RepresentativeIds.Contains(adventurerId)));

        /// <summary>出る子・部隊を変えられるのは月のはじめだけ（訓練の割り振り・大会の出場と同じ、§0.70）。</summary>
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
        /// 代表を決める（ヴェスペラ。順番つき、上限 VesperaMaxRepresentatives。出られない子は外す）。月のはじめでなければ false。
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

        /// <summary>
        /// 出す部隊を決める（メルジーネ。順番つき、上限 MelusineMaxSquads。メンバーのいない部隊は外す）。月のはじめでなければ false。
        /// メンバーは訓練所から外す。
        /// </summary>
        public static bool TrySetSquads(GameState state, RulerState ruler, IEnumerable<Guid> squadIds, IEnumerable<CorridorTarget>? targets = null)
        {
            if (!CanChangeRepresentatives(state)) return false;
            var targetList = targets?.ToList() ?? new List<CorridorTarget>();
            var pairs = squadIds.Select((id, i) => (Id: id, Target: i < targetList.Count ? targetList[i] : CorridorTarget.Skeleton))
                .DistinctBy(x => x.Id)
                .Where(x => state.SavedParties.FirstOrDefault(p => p.Id == x.Id) is { MemberIds.Count: > 0 })
                .Take(MaxSquads(ruler))
                .ToList();
            var list = pairs.Select(x => x.Id).ToList();
            ruler.SquadIds = list;
            ruler.SquadTargets = ruler.RulerId == OrtensiaId ? pairs.Select(x => x.Target).ToList() : new List<CorridorTarget>();
            foreach (var p in state.SavedParties.Where(p => list.Contains(p.Id)))
                foreach (var id in p.MemberIds)
                    state.TrainingAssignments.Remove(id);
            return true;
        }

        /// <summary>出す部隊の行き先（オルテンシア。決めていなければ骸骨兵の間）。</summary>
        public static CorridorTarget TargetOf(RulerState ruler, Guid squadId)
        {
            int i = ruler.SquadIds.IndexOf(squadId);
            return i >= 0 && i < ruler.SquadTargets.Count ? ruler.SquadTargets[i] : CorridorTarget.Skeleton;
        }

        /// <summary>代表のうち、いま戦える子（順番どおり）。</summary>
        public static List<Adventurer> AvailableRepresentatives(GameState state, RulerState ruler) =>
            ruler.RepresentativeIds.Select(id => state.Adventurers.FirstOrDefault(a => a.Id == id)).Where(CanFight).Select(a => a!).ToList();

        /// <summary>出す部隊のうち、戦える子が1人以上いる部隊（順番どおり。メンバーは戦える子だけ）。</summary>
        public static List<(SavedParty Squad, List<Adventurer> Members)> AvailableSquads(GameState state, RulerState ruler) =>
            ruler.SquadIds
                .Select(id => state.SavedParties.FirstOrDefault(p => p.Id == id))
                .Where(p => p != null)
                .Select(p => (p!, p!.MemberIds.Select(id => state.Adventurers.FirstOrDefault(a => a.Id == id)).Where(CanFight).Select(a => a!).ToList()))
                .Where(x => x.Item2.Count > 0)
                .ToList();

        private static bool CanFight(Adventurer? a) =>
            a != null && !a.IsRetired && a.Injury != InjurySeverity.Severe && !a.IsOnLoan && a.CurrentHP > 0;

        /// <summary>戦える子・部隊がいるか。</summary>
        public static bool HasParticipants(GameState state, RulerState ruler) =>
            UsesSquads(ruler) ? AvailableSquads(state, ruler).Count > 0 : AvailableRepresentatives(state, ruler).Count > 0;

        // ==================== 決闘の段取り ====================

        /// <summary>
        /// 月を終えたあとの決闘を待っているか：出る子・部隊が月を過ごし終えた印（PendingDuelWeek）が今の週で、挑めて、戦える子・部隊がいる
        /// （1か月に1回。月を進めた直後〈新しい月の第1週〉に画面が決闘を始める。月のはじめに選んだだけでは決闘にならない）。
        /// </summary>
        public static bool IsDuelDue(GameState state, RulerState ruler) =>
            ruler.PendingDuelWeek == state.WeekNumber && CanChallenge(state, ruler) && HasParticipants(state, ruler);

        /// <summary>決闘を待っている支配者（無ければ null）。</summary>
        public static RulerState? DueDuel(GameState state) => state.Rulers.FirstOrDefault(r => IsDuelDue(state, r));

        /// <summary>この支配者戦で使える介入の回数：基本＋研究「多頭通信術式の強化」（§0.128）。</summary>
        public static int InterventionsPerBattle(GameState state) =>
            RulerBalance.InterventionsBase + (int)Math.Round(ResearchBalance.GetTotalEffectValue(state, ResearchEffectType.InterventionBonus));

        /// <summary>支配者戦を始める（挑めない・出る子がいなければ null）。支配者ごとの戦い方で作る。</summary>
        public static RulerBattle? StartBattle(GameState state, RulerState ruler, IRng rng)
        {
            Refresh(state, ruler);
            if (!CanChallenge(state, ruler) || !HasParticipants(state, ruler)) return null;
            var boss = BossOf(state, ruler)!;
            int interventions = InterventionsPerBattle(state);
            if (ruler.RulerId == OrtensiaId)
            {
                // 戦える部隊だけに絞るので、行き先も同じ部隊のものを拾い直す
                var squads = AvailableSquads(state, ruler);
                var targets = squads.Select(s => TargetOf(ruler, s.Squad.Id)).ToList();
                return new OrtensiaBattle(state, ruler, boss, squads, targets, rng, interventions);
            }
            if (ruler.RulerId == NepherisId)
                return new NepherisBattle(state, ruler, boss, AvailableSquads(state, ruler), rng, interventions);
            if (ruler.RulerId == IgniaId)
                return new IgniaBattle(state, ruler, boss, AvailableSquads(state, ruler), rng, interventions);
            return UsesSquads(ruler)
                ? new MelusineBattle(state, ruler, boss, AvailableSquads(state, ruler), rng, interventions)
                : new RulerDuel(state, ruler, boss, AvailableRepresentatives(state, ruler), rng, interventions);
        }

        /// <summary>ヴェスペラとの決闘を始める（ヴェスペラでない・挑めなければ null。テスト用）。</summary>
        public static RulerDuel? StartDuel(GameState state, RulerState ruler, IRng rng) => StartBattle(state, ruler, rng) as RulerDuel;

        /// <summary>
        /// 支配者戦を最後まで自動で行う（シミュレーター・テスト用。画面は1段ずつ進めて介入を聞く）。
        /// useInterventions なら、それぞれの戦い方のいちばん良い手で介入する（→ RulerBattle.AutoAnswer）。
        /// </summary>
        public static RulerBattle? ResolveAuto(GameState state, RulerState ruler, IRng rng, bool useInterventions)
        {
            var battle = StartBattle(state, ruler, rng);
            if (battle == null) return null;
            while (!battle.IsOver)
            {
                battle.Advance();
                if (battle.Prompt != DuelPromptKind.None)
                    battle.AutoAnswer(useInterventions);
            }
            battle.Finish();
            return battle;
        }

        // ==================== 見返り（§0.128・§0.129） ====================

        /// <summary>茨の欠片（ヴェスペラとの試合に勝つたびに1つ。研究「多頭通信術式の強化」の材料、→ materials.csv）。</summary>
        public const string ThornMaterialId = "mat_ruler_thorn";

        /// <summary>水妖の真珠（メルジーネの調べを打ち破るたびに1つ、→ materials.csv）。</summary>
        public const string PearlMaterialId = "mat_ruler_pearl";

        /// <summary>
        /// 一対一の稽古場（ヴェスペラとの決着の見返り）：森の100Fを倒していれば開いている。個人特訓を同時に1人多くつけられる（→ GuildPolicySystem.CoachingSlots）。
        /// </summary>
        public static bool IsTrainingHallOpen(GameState state) => FinalDefeated(state, ForestId);

        /// <summary>
        /// 静寂の泉（メルジーネとの決着の見返り、§0.129）：洞窟の100Fを倒していれば開いている。待機中の過ごし方に「瞑想」が増える（→ IdleActivitySystem）。
        /// </summary>
        public static bool IsMeditationOpen(GameState state) => FinalDefeated(state, CaveId);

        /// <summary>古王の封蝋（オルテンシアの封印を破るたび・玉座で削るたびに1つ、→ materials.csv）。</summary>
        public const string SealMaterialId = "mat_ruler_seal";

        /// <summary>
        /// 古王の寝所（オルテンシアとの決着の見返り、§0.130）：廃墟の100Fを倒していれば開いている。採用試験に毎回1人、名のある家の娘が加わる
        /// （→ RecruitmentSystem.GenerateCandidates）。
        /// </summary>
        public static bool IsNobleQuartersOpen(GameState state) => FinalDefeated(state, RuinsId);

        /// <summary>焔の鱗（イグニアとの戦いで幕を破るたびに1つ。イグニアの炉の材料、→ materials.csv）。</summary>
        public const string ScaleMaterialId = "mat_ruler_scale";

        /// <summary>
        /// イグニアの炉（イグニアとの決着の見返り、§0.131）：峡谷の100Fを倒していれば開いている。保管庫の武具のアフィックスを1段強くできる（→ ForgeSystem）。
        /// </summary>
        public static bool IsForgeOpen(GameState state) => FinalDefeated(state, CanyonId);

        /// <summary>心臓のかけら（ネフェリスとの戦いで段を破るたびに1つ、→ materials.csv）。</summary>
        public const string ShardMaterialId = "mat_ruler_shard";

        /// <summary>その支配者を決着（100F）で倒したか。</summary>
        public static bool IsRulerDefeated(GameState state, string rulerId) =>
            Rulers.FirstOrDefault(r => r.RulerId == rulerId).FieldId is { } fieldId && FinalDefeated(state, fieldId);

        private static bool FinalDefeated(GameState state, string fieldId) =>
            state.DungeonFields.FirstOrDefault(f => f.Id == fieldId)?.Bosses.FirstOrDefault(b => b.Floor == FinalFloor)?.EverDefeated == true;

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

        // ==================== 決闘の語り（テスト・画面の互換。中身は各戦い方が持つ） ====================

        /// <summary>決闘の小窓の背景（ヴェスペラ）。</summary>
        public const string DuelBackground = "翠緑の原生林・主の巣";

        public static List<StoryLine> DuelOpeningLines(RulerDuel duel) => duel.OpeningLines();
        public static List<StoryLine> BoutLines(DuelBout b) => RulerDuel.LinesOf(b);
        public static List<StoryLine> PromptLines(RulerBattle battle) => battle.PromptLines();
        public static List<StoryLine> DuelClosingLines(RulerBattle battle) => battle.ClosingLines();

        // ==================== 強制除籍 ====================

        /// <summary>
        /// 支配者戦で強制除籍になった子の後始末（→ DungeonExpeditionSystem.ApplyForcedRetirements と同じ手順）：
        /// 居合わせた子の満足度・相性の余波、装備を保管庫へ、記録へ移す。マスターの激怒も。
        /// 居合わせた仲間は、除籍された子を先頭にした4人まで（部隊の大きさ）で数える。
        /// </summary>
        internal static void ApplyForcedRetirements(GameState state, IReadOnlyList<Adventurer> present, IReadOnlyCollection<Guid> fallenIds, IRng rng, RulerBattle battle)
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
                battle.TraitGrants.AddRange(compatibility.ApplyDeathAftermath(state, party, id, fallenIds));

                EquipmentSystem.UnequipAllToArmory(state, fallen, $"{fallen.Name}（除籍）から返還");
                fallen.FellAtWeek = state.WeekNumber;
                state.TrainingAssignments.Remove(fallen.Id);
                state.Adventurers.Remove(fallen);
                state.FallenAdventurers.Add(fallen);
                foreach (var r in state.Rulers)
                    r.RepresentativeIds.Remove(fallen.Id);
            }
            battle.MoodApplied = MasterMoodSystem.ApplyForcedRetirementFury(state, fallenIds.Count);
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

    /// <summary>支配者戦の途中で聞く介入（無ければ None）。</summary>
    public enum DuelPromptKind
    {
        None,
        /// <summary>見切りを伝える（ヴェスペラ）：次の試合の前、次に出る代表を入れ替えるか。</summary>
        Foresight,
        /// <summary>名を呼ぶ（ヴェスペラ）：代表が魅了されたとき、呼びかけるか。</summary>
        CallName,
        /// <summary>ルミナが歌を聞き取る（メルジーネ）：調べの分からない曲の前、聞き取ってもらうか。</summary>
        LuminaListen,
        /// <summary>交代（メルジーネ）：前に出た部隊が調べに呑まれかけたとき、ほかの部隊と入れ替えるか。</summary>
        Swap,
        /// <summary>転進（オルテンシア）：封印を破った・相手のいなかった部隊を、別の間や玉座へ回すか。</summary>
        Redirect,
        /// <summary>援軍（オルテンシア）：封印の張り直しのとき、部隊を守りに回して防ぐか。</summary>
        Reinforce,
        /// <summary>押し切れ（イグニア）：合い具合の足りない幕のはじめ、合い具合を上げて押し切るか（灼熱と強制除籍が重くなる）。</summary>
        Push,
        /// <summary>入れ替え（イグニア）：子が戦線を離れるとき、後の部隊の子と入れ替えるか。</summary>
        Reserve,
        /// <summary>退け（イグニア）：押し切っても届かない幕のはじめ、戦いを打ち切るか（残りの灼熱を受けない）。</summary>
        Withdraw,
        /// <summary>都の声（ネフェリス）：届かない段の前、ルミナが姉の声を呼んで打ち消すか（護符の回数とは別に1回）。</summary>
        CityVoice,
    }

    /// <summary>介入の選択肢（入れ替える相手など）。</summary>
    public sealed record PromptChoice(Guid Id, string Label);

    /// <summary>1人分の損耗の結果（→ RulerBattle.ApplyLoss）。</summary>
    public sealed class LossOutcome
    {
        public int HpLost { get; set; }
        public bool SixthSense { get; set; }
        public bool NearDeath { get; set; }
        public bool Fallen { get; set; }
        public GuardEvent? Guard { get; set; }
    }

    /// <summary>
    /// 支配者戦1回の共通の土台（§0.127〜）。<see cref="Advance"/> で次の介入か終わりまで進み、介入（<see cref="Prompt"/>）があれば
    /// <see cref="Answer"/> で答えてまた進める。終わったら <see cref="Finish"/> で締める（戦った記録、強制除籍の後始末、
    /// 前哨戦で退いた・決着で倒したら主を倒したことにしてフィールドを進める、伝説級の装備）。
    /// 語り（<see cref="TakeNewLines"/>・<see cref="PromptLines"/>・<see cref="ClosingLines"/>）は画面の小窓が段で見せる。
    /// 乱数は支配者戦専用（勝ち負け・損耗・かばい合い・強制除籍・介入）。
    /// </summary>
    public abstract class RulerBattle
    {
        protected readonly GameState State;
        protected readonly IRng Rng;
        /// <summary>居合わせた子（強制除籍の余波・かばい合いの相手）。</summary>
        protected readonly List<Adventurer> Present;
        private bool _finished;

        public RulerState Ruler { get; }
        public FloorBoss Boss { get; }
        public bool IsOutpost { get; }
        public int HpBefore { get; }
        /// <summary>戦い終えたときの体力（前哨戦で退いたあとは決着の体力に戻るので、それより前の値）。</summary>
        public int HpAfter { get; private set; }
        public int InterventionsLeft { get; protected set; }
        public DuelPromptKind Prompt { get; protected set; }
        public bool IsOver { get; protected set; }
        /// <summary>前哨戦で退いた（70Fを倒したことになる）。</summary>
        public bool Retreated { get; private set; }
        /// <summary>決着で倒した（100Fを倒したことになる）。</summary>
        public bool Defeated { get; private set; }
        public List<Guid> FallenIds { get; } = new();
        public List<Guid> NearDeathIds { get; } = new();
        public List<InjuryEvent> InjuryEvents { get; } = new();
        public List<TraitGrantEvent> TraitGrants { get; } = new();
        public int MoodApplied { get; internal set; }
        /// <summary>前哨戦で退かせて手に入れた伝説級の装備（無ければ null）。</summary>
        public EquipmentItem? LegendaryFound { get; private set; }

        /// <summary>支配者の名前。</summary>
        public string RulerName => RulerSystem.NameOf(Ruler.RulerId);
        /// <summary>小窓の背景。</summary>
        public abstract string Background { get; }

        protected RulerBattle(GameState state, RulerState ruler, FloorBoss boss, IEnumerable<Adventurer> present, IRng rng, int interventions)
        {
            State = state;
            Rng = rng;
            Ruler = ruler;
            Boss = boss;
            IsOutpost = boss.Floor == RulerSystem.OutpostFloor;
            HpBefore = ruler.Hp;
            Present = present.ToList();
            InterventionsLeft = interventions;
        }

        /// <summary>支配者の体力が線を切ったか（前哨戦は50未満、決着は0）。</summary>
        protected bool OverByHp => IsOutpost ? Ruler.Hp < RulerBalance.OutpostRetreatHp : Ruler.Hp <= 0;

        /// <summary>次の介入か終わりまで進める。</summary>
        public abstract void Advance();

        /// <summary>介入の選択肢（入れ替える相手。はい／いいえだけなら空）。</summary>
        public abstract List<PromptChoice> PromptChoices();

        /// <summary>介入に答える（use＝使う。選択肢があるなら choice で選ぶ）。</summary>
        public abstract void Answer(bool use, Guid? choice);

        /// <summary>自動で答える（シミュレーター・テスト）：useInterventions ならいちばん良い手で使う。</summary>
        public void AutoAnswer(bool useInterventions)
        {
            var choices = PromptChoices();
            bool needsChoice = Prompt is DuelPromptKind.Foresight or DuelPromptKind.Swap or DuelPromptKind.Redirect
                or DuelPromptKind.Reinforce or DuelPromptKind.Reserve;
            Answer(useInterventions && (!needsChoice || choices.Count > 0) && AutoWorthUsing(), choices.FirstOrDefault()?.Id);
        }

        /// <summary>自動で答えるとき、いま聞いている介入を使う値打ちがあるか（既定は使う。イグニアの「退け」は残りの幕に望みが無いときだけ）。</summary>
        protected virtual bool AutoWorthUsing() => true;

        /// <summary>介入の小窓の説明（BBCode）とボタンの文言。</summary>
        public abstract (string Text, string Ok, string Cancel) PromptDialog();

        /// <summary>前回から増えた語り（はじめの語りと、済んだ試合・曲の語り）。</summary>
        public abstract List<StoryLine> TakeNewLines();

        /// <summary>介入を待つ山場の語り。</summary>
        public abstract List<StoryLine> PromptLines();

        /// <summary>おわりの語り（<see cref="Finish"/> のあと）。</summary>
        public abstract List<StoryLine> ClosingLines();

        /// <summary>試合・曲に勝つたびに手に入る素材の数。</summary>
        public int MaterialsGained { get; protected set; }

        /// <summary>
        /// 1人分の損耗（最大HPに対する pct %）。HP0 になりそうなら、居合わせた相棒のかばい合い・危機察知・強制除籍の判定（§0.125）を順に通す。
        /// 重傷の判定もする。
        /// </summary>
        protected LossOutcome ApplyLoss(Adventurer a, int pct, int fatalMultiplier = 1)
        {
            var result = new LossOutcome();
            int loss = a.MaxHP * pct / 100;
            int newHp = a.CurrentHP - loss;
            if (newHp <= 0
                && MoraleSystem.FindGuardian(State, a, Present.Where(p => State.Adventurers.Contains(p))) is { } guardian
                && Rng.NextInt(1, 100) <= PersonalityBalance.GuardChancePercent)
            {
                int share = Math.Min((int)Math.Round(loss * PersonalityBalance.GuardShareRate), guardian.CurrentHP - 1);
                guardian.CurrentHP -= share;
                loss -= share;
                newHp = a.CurrentHP - loss;
                result.Guard = new GuardEvent(guardian.Id, guardian.Name, a.Id, a.Name, share);
            }
            if (newHp <= 0 && a.HasTrait(TraitCatalog.SixthSenseId))
            {
                newHp = 1; // 危機察知：倒れる前に自分から下がる
                result.SixthSense = true;
            }
            if (newHp <= 0)
            {
                if (Rng.NextInt(1, 100) <= CombatBalance.FatalChancePercent * fatalMultiplier)
                {
                    newHp = 0;
                    result.Fallen = true;
                    FallenIds.Add(a.Id);
                }
                else
                {
                    newHp = 1;
                    result.NearDeath = true;
                    NearDeathIds.Add(a.Id);
                    if (CriticalInjury.RollOldWound(a, Rng) is { } grant) TraitGrants.Add(grant);
                }
            }
            result.HpLost = a.CurrentHP - Math.Max(0, newHp);
            a.CurrentHP = Math.Max(0, newHp);
            if (!result.Fallen && CriticalInjury.TryInflictSevere(a, Rng) is { } injury)
                InjuryEvents.Add(injury);
            return result;
        }

        /// <summary>戦い終えたあとの、支配者ごとの後片付け（メルジーネの調べの並びの入れ替えなど）。</summary>
        protected virtual void OnFinished() { }

        /// <summary>締める（1回だけ）。</summary>
        public void Finish()
        {
            if (_finished) return;
            _finished = true;
            IsOver = true;
            Prompt = DuelPromptKind.None;
            Ruler.DuelCount++;
            Ruler.LastDuelWeek = State.WeekNumber;
            Ruler.PendingDuelWeek = null;
            HpAfter = Ruler.Hp;

            RulerSystem.ApplyForcedRetirements(State, Present, FallenIds, Rng, this);

            if (OverByHp)
            {
                if (IsOutpost) Retreated = true; else Defeated = true;
                Boss.CurrentHp = 0;
                Boss.IsDefeated = true;
                DungeonExpeditionSystem.ApplyFieldProgression(State, Boss);
                // 見返り：前哨戦で退かせたら、その支配者の伝説級（uniques.csv のそのフィールドの70F）。決着の見返りは100Fを倒したことで開く
                if (RulerSystem.FieldOf(State, Ruler) is { } field)
                    LegendaryFound = UniqueItemSystem.TryGrantBossDrop(State, field, Boss);
                RulerSystem.Refresh(State, Ruler);
            }
            OnFinished();
        }
    }

    /// <summary>決闘の1試合（ヴェスペラ）。</summary>
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
    /// ヴェスペラとの決闘（勝ち抜きの一騎打ち、§0.127、設計書 §3-5）。介入は「見切りを伝える」（<see cref="AnswerForesight"/>）と
    /// 「名を呼ぶ」（<see cref="AnswerCallName"/>）。
    /// </summary>
    public sealed class RulerDuel : RulerBattle
    {
        private readonly List<Adventurer> _queue;
        private Adventurer? _fighter;
        private bool _fighterIsFresh;
        private int _foresightAskedForBout = -1;
        private DuelCharm? _pendingCharm;
        private int _linesShown = -1;

        public List<DuelBout> Bouts { get; } = new();
        /// <summary>介入を待っている試合の部門（Prompt があるとき）。</summary>
        public TournamentDiscipline PendingDiscipline { get; private set; }
        /// <summary>介入を待っている今の代表（Prompt があるとき）。</summary>
        public Adventurer? CurrentFighter => _fighter;
        /// <summary>この決闘で持ち帰った茨の欠片（勝った試合の数）。</summary>
        public int ThornsGained => MaterialsGained;
        public override string Background => RulerSystem.DuelBackground;

        internal RulerDuel(GameState state, RulerState ruler, FloorBoss boss, List<Adventurer> reps, IRng rng, int interventions)
            : base(state, ruler, boss, reps, rng, interventions)
        {
            _queue = reps.ToList();
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

        public override void Advance()
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
                    && RulerSystem.IntelOf(State, Ruler) >= RulerBalance.VesperaIntelWeaponReveal
                    && ForesightCandidates().Count > 0)
                {
                    _foresightAskedForBout = boutIndex;
                    Prompt = DuelPromptKind.Foresight;
                    return;
                }

                // 魅了の判定。魅了されたら、名を呼ぶかを聞く（介入が残っていれば）
                if (_pendingCharm == null)
                {
                    var charm = RulerSystem.JudgeCharm(State, _fighter, IsOutpost);
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

        public override List<PromptChoice> PromptChoices() =>
            Prompt == DuelPromptKind.Foresight
                ? ForesightCandidates().Select(a => new PromptChoice(a.Id, $"{a.Name}（{TournamentSystem.DisciplineLabel(PendingDiscipline)}の強さ {TournamentSystem.MatchStrength(a, PendingDiscipline):F0}）")).ToList()
                : new List<PromptChoice>();

        public override void Answer(bool use, Guid? choice)
        {
            if (Prompt == DuelPromptKind.Foresight) AnswerForesight(use ? choice : null);
            else if (Prompt == DuelPromptKind.CallName) AnswerCallName(use);
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
            bool reached = Rng.NextInt(1, 100) <= RulerSystem.CallNameChance(State, _fighter);
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

            bout.Won = charm != DuelCharm.Charmed && Rng.NextInt(0, 999_999) / 1_000_000.0 < IsabellaSystem.BoutWinChance(ours, ruler);
            bout.Damage = bout.Won
                ? Math.Clamp((int)Math.Round(RulerBalance.VesperaWinDamageBase * ours / Math.Max(1, ruler)), RulerBalance.VesperaWinDamageMin, RulerBalance.VesperaWinDamageMax)
                : charm == DuelCharm.Charmed ? 0 : RulerBalance.VesperaLossDamage;
            Ruler.Hp = Math.Max(0, Ruler.Hp - bout.Damage);
            bout.RulerHpAfter = Ruler.Hp;

            int pct;
            if (bout.Won)
                pct = Rng.NextInt(RulerBalance.VesperaWinHpLossPctMin, RulerBalance.VesperaWinHpLossPctMax);
            else
            {
                // 強さの差が大きいほど重い（ヴェスペラが代表の1.5倍以上で上限）
                double gap = Math.Clamp((ruler / Math.Max(1, ours) - 1.0) / 0.5, 0, 1);
                pct = (int)Math.Round(RulerBalance.VesperaLossHpLossPctMin + (RulerBalance.VesperaLossHpLossPctMax - RulerBalance.VesperaLossHpLossPctMin) * gap);
            }
            var loss = ApplyLoss(a, pct);
            bout.HpLost = loss.HpLost;
            bout.SixthSense = loss.SixthSense;
            bout.NearDeath = loss.NearDeath;
            bout.Fallen = loss.Fallen;
            bout.Guard = loss.Guard;

            Bouts.Add(bout);
            if (bout.Won)
            {
                State.AddMaterial(RulerSystem.ThornMaterialId, 1); // 茨の欠片（§0.128）
                MaterialsGained++;
            }
            Ruler.WeaponIndex = (Ruler.WeaponIndex + 1) % RulerSystem.VesperaWeapons.Length;

            // 勝った代表は残る。負けた・倒れた代表は下がる
            if (!bout.Won || bout.Fallen || a.CurrentHP <= 1) _fighter = null;
            else _fighterIsFresh = false;
        }

        // ---- 語り ----

        /// <summary>はじめの語り。</summary>
        public List<StoryLine> OpeningLines() => new()
        {
            new(StoryLineKind.Narration, $"{(IsOutpost ? "森の70階" : "森の100階")}、茨の決闘場。ヴェスペラの体力 {HpBefore}/{RulerBalance.MaxHp}" +
                (IsOutpost ? $"（{RulerBalance.OutpostRetreatHp}を切ると退く）" : "")),
            new(StoryLineKind.Speech, IsOutpost ? "来たか。……一人ずつだ。順に来い" : "ここはわたしの家だ。……今度は、退かん", RulerSystem.VesperaName),
        };

        /// <summary>1試合の語り（地の文の段）。</summary>
        public static List<StoryLine> LinesOf(DuelBout b)
        {
            var lines = new List<StoryLine>
            {
                new(StoryLineKind.Narration, $"第{b.Number}試合　ヴェスペラの{RulerSystem.WeaponLabel(b.Discipline)}（{TournamentSystem.DisciplineLabel(b.Discipline)}）。{b.Name}が前へ出る"),
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
            lines.AddRange(LossLines(b.Name, b.Guard, b.SixthSense, b.NearDeath, b.Fallen));
            if (b.Won)
                lines.Add(new(StoryLineKind.Narration, "折れた茨の棘が1つ残った（茨の欠片）"));
            return lines;
        }

        /// <summary>損耗の語り（かばい合い・危機察知・一命・強制除籍）。</summary>
        internal static IEnumerable<StoryLine> LossLines(string name, GuardEvent? guard, bool sixthSense, bool nearDeath, bool fallen)
        {
            if (guard is { } g)
                yield return new(StoryLineKind.Narration, $"{g.GuardianName}が飛び込み、{name}をかばった（HP{g.Share}を引き受けた）");
            if (sixthSense)
                yield return new(StoryLineKind.Narration, $"{name}は危機を察し、倒れる寸前で自ら退いた");
            if (nearDeath)
                yield return new(StoryLineKind.Narration, $"{name}は致命傷を負った。……アルベールの秘薬で、一命を取り留めた（重傷）");
            if (fallen)
                yield return new(StoryLineKind.Narration, $"{name}は致命傷を負った。命は助かったが、もう二度と剣は握れない（強制除籍）");
        }

        public override List<StoryLine> TakeNewLines()
        {
            var lines = new List<StoryLine>();
            if (_linesShown < 0)
            {
                lines.AddRange(OpeningLines());
                _linesShown = 0;
            }
            foreach (var b in Bouts.Skip(_linesShown))
                lines.AddRange(LinesOf(b));
            _linesShown = Bouts.Count;
            return lines;
        }

        public override List<StoryLine> PromptLines()
        {
            if (_fighter is not { } a) return new List<StoryLine>();
            return Prompt switch
            {
                DuelPromptKind.Foresight => new()
                {
                    new(StoryLineKind.Narration, $"次はヴェスペラの{RulerSystem.WeaponLabel(PendingDiscipline)}（{TournamentSystem.DisciplineLabel(PendingDiscipline)}）。{a.Name}が構える"),
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

        public override (string Text, string Ok, string Cancel) PromptDialog()
        {
            string left = $"[color=gray]声が届くのは、この決闘であと{InterventionsLeft}回。[/color]";
            if (Prompt == DuelPromptKind.Foresight && _fighter != null)
            {
                string label = TournamentSystem.DisciplineLabel(PendingDiscipline);
                return ($"次はヴェスペラの[b]{RulerSystem.WeaponLabel(PendingDiscipline)}[/b]（{label}）。\n今の代表：{_fighter.Name}（{label}の強さ {TournamentSystem.MatchStrength(_fighter, PendingDiscipline):F0}）\n" +
                        $"見切りを伝えて、次に出る代表を入れ替えますか？\n{left}", "見切りを伝える", "伝えない");
            }
            if (_fighter != null)
                return ($"{_fighter.Name}がヴェスペラの囁きに魅入られかけている。\n名を呼べば、届く見込み [b]{RulerSystem.CallNameChance(State, _fighter)}%[/b]（相性と満足度が高いほど届く）。\n{left}", "名を呼ぶ", "呼ばない");
            return ("", "使う", "使わない");
        }

        public override List<StoryLine> ClosingLines()
        {
            var lines = new List<StoryLine>();
            int dealt = HpBefore - Ruler.Hp;
            if (Retreated)
            {
                lines.Add(new(StoryLineKind.Speech, "……ふん。今日のところは、退いてやる", RulerSystem.VesperaName));
                lines.Add(new(StoryLineKind.Narration, "ヴェスペラは茨の奥へ退いた。森の70階の先へ進めるようになった"));
            }
            else if (Defeated)
            {
                lines.Add(new(StoryLineKind.Speech, "……見事だ", RulerSystem.VesperaName));
                lines.Add(new(StoryLineKind.Narration, "ヴェスペラの最後の刃が折れた。森の100階を制した"));
            }
            if (LegendaryFound is { } blades)
                lines.Add(new(StoryLineKind.Narration, $"ヴェスペラが置いていった「{blades.Name}」を手に入れた（伝説級。持つ子が部隊にいれば、主の魅了を対策済みにする）"));
            if (Defeated)
                lines.Add(new(StoryLineKind.Narration, "一対一の稽古場が開いた（個人特訓を同時に2人までつけられる）"));
            if (ThornsGained > 0)
                lines.Add(new(StoryLineKind.Narration, $"茨の欠片を{ThornsGained}つ持ち帰った（研究「多頭通信術式の強化」の材料。売ることもできる）"));
            if (!Retreated && !Defeated)
            {
                lines.Add(new(StoryLineKind.Speech, dealt > 0 ? "……悪くない。次は、もっと強い者を寄越せ" : "その程度か。出直せ", RulerSystem.VesperaName));
                lines.Add(new(StoryLineKind.Narration, $"決闘は終わった。ヴェスペラの体力 {Ruler.Hp}/{RulerBalance.MaxHp}（この決闘で−{dealt}。毎月{RulerBalance.RegenPerMonth}ずつ回復する）"));
            }
            return lines;
        }
    }
}
