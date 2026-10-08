using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 依頼（→ GuildCommission・GameState.Commissions、03 §4.9・§0.64）。作業感をなくす構想の②。
    ///
    /// 旧通常クエスト（掲示板・受託依頼、§0.8で撤去）の復活ではなく、大迷宮の任務に乗る形の中目標：
    ///  - 季節のはじめ（依頼が開いた週＝GameState.CommissionsFromWeek 以降の季節の1週目。初めての入賞の次の季節から、§0.84）に、依頼人の違う依頼が OffersPerSeason 件届く。
    ///    受けるのは MaxAccepted 件まで。断っても・受けずに流しても罰は無い。次の季節のはじめに、受けなかった依頼は消える。
    ///  - 撃破・完全解析は週次決算で達成を判定する（大迷宮の解決の直後）。納品・派遣はプレイヤーの操作で即時に達成する（派遣した子はひと季節後に帰ってくる、§0.85）。
    ///  - 受けた依頼が期限切れ（または解析の対象ボスを先に倒して達成できなくなった）なら、機嫌が FailureMoodLoss 下がる。
    ///  - 達成の報酬：ゴールド・金以上の未鑑定遺物1個・機嫌・依頼人ごとのおまけの素材。同じ依頼人の依頼を
    ///    PatronUniqueCompletions 件達成すると、その依頼人の固有武具（→ uniques.csv の Patron）が届く。
    /// </summary>
    public class CommissionSystem
    {
        private const int DefaultSeed = 2718;
        private readonly IRng _rng;

        public CommissionSystem(IRng? rng = null)
        {
            _rng = rng ?? new SeededRng(DefaultSeed);
        }

        // ==================== 週次決算 ====================

        /// <summary>
        /// 週次決算での判定（→ WeekProcessingSystem.ProcessWeek、大迷宮の解決と機嫌の集計の直後）。
        /// state.WeekNumber はまだ決算中の週。受けた撃破・完全解析の達成と、受けた依頼の期限切れ・達成不能を処理し、
        /// 掲示中の依頼のうち期限が過ぎたもの・対象が無くなったものを黙って片付ける。
        /// </summary>
        public CommissionSettlement ProcessSettlement(GameState state)
        {
            var report = new CommissionSettlement();
            int week = state.WeekNumber;

            foreach (var c in state.Commissions.ToList())
            {
                var boss = FindBoss(state, c);
                bool targetsBoss = c.Type is CommissionType.Defeat or CommissionType.Survey;

                if (!c.Accepted)
                {
                    // 掲示中：期限切れ・対象が無くなった・倒されて達成できなくなった依頼は黙って下げる（受けていないので罰なし）。
                    if (c.WeeksLeft(week) <= 1 || (targetsBoss && (boss == null || boss.IsDefeated)))
                        state.Commissions.Remove(c);
                    continue;
                }

                if (targetsBoss && boss == null)
                {
                    state.Commissions.Remove(c); // 壊れたデータ（ボスが見つからない）。罰も報酬も無しで外す
                    continue;
                }

                if (c.Type == CommissionType.Defeat && boss!.IsDefeated)
                {
                    report.Completed.Add(Complete(state, c));
                    continue;
                }

                if (c.Type == CommissionType.Survey)
                {
                    if (ScoutingResolver.GetTier(boss!.IntelRate) == IntelTier.Complete)
                    {
                        report.Completed.Add(Complete(state, c));
                        continue;
                    }
                    if (boss.IsDefeated)
                    {
                        report.Failed.Add(Fail(state, c, "解析の前に倒してしまった"));
                        continue;
                    }
                }

                if (c.WeeksLeft(week) <= 1)
                    report.Failed.Add(Fail(state, c, "期限切れ"));
            }

            return report;
        }

        /// <summary>
        /// 新しい週の始まりの処理（→ WeekProcessingSystem.ProcessWeek、WeekNumber を進めた後）。
        /// 季節のはじめなら依頼を届け、季節の予告の週なら異変を予告し、受けた依頼の期限が近ければ知らせる。
        /// </summary>
        public CommissionArrivals ProcessNewWeek(GameState state)
        {
            var arrivals = new CommissionArrivals();
            int week = state.WeekNumber;

            DungeonAnomalySystem.ClearExpired(state);

            if (IsOfferWeek(state, week))
            {
                state.Commissions.RemoveAll(c => !c.Accepted); // 前の季節の掲示は下げる
                arrivals.Offered.AddRange(GenerateOffers(state));
            }

            if (DungeonAnomalySystem.IsAnnounceWeek(week) && PostGameSystem.AnomalySeasonAllowed(state, week)) // クリアのあとは春と秋だけ（§0.90）
                arrivals.AnnouncedAnomaly = DungeonAnomalySystem.Announce(state, _rng);

            arrivals.DeadlineNear.AddRange(state.Commissions.Where(c =>
                c.Accepted && c.WeeksLeft(week) == CommissionBalance.DeadlineWarningWeeks && !IsAchieved(state, c)));

            return arrivals;
        }

        /// <summary>依頼が届く週か（依頼が開いた週＝CommissionsFromWeek 以降の季節の1週目。開いていなければ届かない、§0.84）。</summary>
        public static bool IsOfferWeek(GameState state, int week) =>
            state.CommissionsFromWeek is int from && week >= from && GameCalendar.WeekOfSeason(week) == 1;

        // ==================== 依頼の生成 ====================

        /// <summary>
        /// 依頼を OffersPerSeason 件まで作って掲示する（依頼人は重ならない。作れる依頼が無い依頼人は飛ばす）。
        /// 攻略の最前線（開いているフィールドの次のボス）が無い＝全フィールド制覇なら何も作らない。
        /// </summary>
        public List<GuildCommission> GenerateOffers(GameState state)
        {
            var offers = new List<GuildCommission>();
            var frontier = GetFrontier(state);
            if (frontier.Count == 0)
                return offers;

            foreach (var client in Shuffle(CommissionBalance.Clients.ToList()))
            {
                if (offers.Count >= CommissionBalance.OffersPerSeason)
                    break;

                foreach (var type in Shuffle(client.Types.ToList()))
                {
                    var c = TryCreate(state, client, type, frontier);
                    if (c == null)
                        continue;
                    state.Commissions.Add(c);
                    offers.Add(c);
                    break;
                }
            }

            return offers;
        }

        /// <summary>
        /// 攻略の最前線：開いているフィールドの次の未撃破ボスを、要求火力の低い順に並べたもの（異変の倍率は含めない）。
        /// 撃破・完全解析の対象と、納品・派遣の報酬の基準に使う。
        /// </summary>
        public static List<(DungeonField Field, FloorBoss Boss)> GetFrontier(GameState state) =>
            state.DungeonFields.Where(f => f.IsUnlocked)
                .Select(f => (Field: f, Boss: f.GetNextActiveBoss()))
                .Where(x => x.Boss != null)
                .Select(x => (x.Field, Boss: x.Boss!))
                .OrderBy(x => DungeonResolver.RequiredPower(x.Boss))
                .ThenBy(x => x.Field.Order)
                .ToList();

        private GuildCommission? TryCreate(GameState state, CommissionClient client, CommissionType type,
            List<(DungeonField Field, FloorBoss Boss)> frontier)
        {
            int week = state.WeekNumber;
            var c = new GuildCommission
            {
                ClientId = client.Id,
                Type = type,
                OfferedWeek = week,
                DeadlineWeek = week + CommissionBalance.GetDeadlineWeeks(type) - 1,
            };
            var values = new Dictionary<string, string> { ["weeks"] = CommissionBalance.GetDeadlineWeeks(type).ToString() };
            FloorBoss referenceBoss = frontier[0].Boss;
            DungeonField referenceField = frontier[0].Field;

            switch (type)
            {
                case CommissionType.Defeat:
                case CommissionType.Survey:
                {
                    // 最前線の低い方から2体のうちどちらか。同じボスを同じ種類で重ねて頼まない。
                    var candidates = frontier
                        .Where(x => type == CommissionType.Defeat || ScoutingResolver.GetTier(x.Boss.IntelRate) != IntelTier.Complete)
                        .Where(x => !state.Commissions.Any(o => o.Type == type && o.BossId == x.Boss.Id))
                        .Take(2).ToList();
                    if (candidates.Count == 0)
                        return null;
                    var (field, boss) = candidates[_rng.NextInt(0, candidates.Count - 1)];
                    c.FieldId = field.Id;
                    c.BossId = boss.Id;
                    referenceBoss = boss;
                    referenceField = field;
                    values["field"] = field.Name;
                    values["boss"] = boss.Name;
                    values["floor"] = boss.Floor.ToString();
                    break;
                }
                case CommissionType.Deliver:
                {
                    var choices = state.DungeonFields.Where(f => f.IsUnlocked)
                        .SelectMany(f => MaterialBalance.GetEligibleMaterials(f.Id, f.ReachedFloor).Select(m => (Field: f, Material: m)))
                        .Where(x => !state.Commissions.Any(o => o.Type == CommissionType.Deliver && o.MaterialId == x.Material.Id))
                        .ToList();
                    if (choices.Count == 0)
                        return null;
                    var (field, material) = choices[_rng.NextInt(0, choices.Count - 1)];
                    c.FieldId = field.Id;
                    c.MaterialId = material.Id;
                    c.Count = CommissionBalance.DeliverCountBase + field.ReachedFloor / Math.Max(1, CommissionBalance.DeliverFloorsPerExtra);
                    values["material"] = material.Name;
                    values["count"] = c.Count.ToString();
                    break;
                }
                case CommissionType.Loan:
                {
                    if (state.Adventurers.Count == 0 || state.Commissions.Any(o => o.Type == CommissionType.Loan))
                        return null;
                    if (!CommissionBalance.LoanOfferSeasons.Contains(GameCalendar.SeasonOf(state.WeekNumber)))
                        return null; // 派遣の依頼は決まった季節だけ届く（§0.85。損の少ない派遣を何度も繰り返せないように）
                    var stats = AdventurerStatAccessor.AllStatNames;
                    string stat = stats[_rng.NextInt(0, stats.Length - 1)];
                    var sorted = state.Adventurers.Select(a => AdventurerStatAccessor.GetStat(a, stat)).OrderByDescending(v => v).ToList();
                    int rankIndex = Math.Min(Math.Max(1, CommissionBalance.LoanRank), sorted.Count) - 1;
                    c.StatName = stat;
                    c.StatThreshold = Math.Max(CommissionBalance.LoanMinThreshold, sorted[rankIndex]);
                    c.FieldId = referenceField.Id;
                    values["stat"] = stat;
                    values["value"] = c.StatThreshold.ToString();
                    break;
                }
            }

            if (type is CommissionType.Deliver or CommissionType.Loan)
                c.FieldId = c.FieldId.Length == 0 ? referenceField.Id : c.FieldId;
            c.RewardGold = (int)Math.Round(referenceBoss.RewardGold * CommissionBalance.GetRewardMultiplier(type), MidpointRounding.AwayFromZero);
            c.RewardRelicFloor = referenceBoss.Floor;

            var texts = CommissionBalance.GetTexts(client.Id, type);
            c.Text = texts.Count == 0 ? "" : DungeonAnomalySystem.Fill(texts[_rng.NextInt(0, texts.Count - 1)], values);
            return c;
        }

        private List<T> Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.NextInt(0, i);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }

        // ==================== 操作（受ける・断る・納める・派遣する） ====================

        /// <summary>受けている依頼の数。</summary>
        public static int AcceptedCount(GameState state) => state.Commissions.Count(c => c.Accepted);

        /// <summary>受けられるか（掲示中・受けている数が上限未満・期限内）。</summary>
        public static bool CanAccept(GameState state, GuildCommission c) =>
            state.Commissions.Contains(c) && !c.Accepted && AcceptedCount(state) < CommissionBalance.MaxAccepted &&
            c.WeeksLeft(state.WeekNumber) >= 1;

        /// <summary>依頼を受ける。受けられなければ false。</summary>
        public static bool TryAccept(GameState state, GuildCommission c)
        {
            if (!CanAccept(state, c))
                return false;
            c.Accepted = true;
            return true;
        }

        /// <summary>掲示中の依頼を断る（罰なし）。受けた依頼は断れない（false）。</summary>
        public static bool Decline(GameState state, GuildCommission c) => !c.Accepted && state.Commissions.Remove(c);

        /// <summary>納品できるか（受けた納品の依頼で、在庫が足りている）。</summary>
        public static bool CanDeliver(GameState state, GuildCommission c) =>
            state.Commissions.Contains(c) && c.Accepted && c.Type == CommissionType.Deliver && c.MaterialId != null &&
            state.Materials.TryGetValue(c.MaterialId, out int stock) && stock >= c.Count;

        /// <summary>素材を納めて依頼を達成する。納められなければ null。</summary>
        public CommissionCompletion? TryDeliver(GameState state, GuildCommission c)
        {
            if (!CanDeliver(state, c))
                return null;
            state.Materials[c.MaterialId!] -= c.Count;
            if (state.Materials[c.MaterialId!] <= 0)
                state.Materials.Remove(c.MaterialId!);
            return Complete(state, c);
        }

        /// <summary>
        /// 派遣の候補（受けた派遣の依頼の条件を満たす現役の冒険者）。条件は素の能力値（装備の補正を含めない）。
        /// 出撃中・派遣中・負傷・毒・今月の大会（交流戦）に出る者は除く（§0.85）。
        /// </summary>
        public static List<Adventurer> GetLoanCandidates(GameState state, GuildCommission c)
        {
            if (c.Type != CommissionType.Loan || c.StatName == null)
                return new List<Adventurer>();
            return state.Adventurers
                .Where(a => !a.IsRetired && !a.IsDispatched && !a.IsOnLoan && a.Injury == InjurySeverity.None && !a.IsPoisoned
                    && !TournamentSystem.IsEntered(state, a.Id)
                    && AdventurerStatAccessor.GetStat(a, c.StatName) >= c.StatThreshold)
                .OrderBy(a => AdventurerStatAccessor.GetStat(a, c.StatName))
                .ToList();
        }

        /// <summary>
        /// 冒険者を依頼人へひと季節（LoanWeeks）派遣して依頼を達成する（§0.85。旧・献上）。本人は名簿に残るが、
        /// 帰ってくるまで出撃・訓練・大会・待機中の過ごし方をしない（部隊と訓練から外れる。装備は持ったまま）。
        /// 受けた派遣の依頼でない・候補でなければ null。
        /// </summary>
        public CommissionCompletion? TryLoan(GameState state, GuildCommission c, Adventurer adventurer)
        {
            if (!state.Commissions.Contains(c) || !c.Accepted || !GetLoanCandidates(state, c).Contains(adventurer))
                return null;

            state.TrainingAssignments.Remove(adventurer.Id);
            foreach (var party in state.SavedParties)
                party.MemberIds.Remove(adventurer.Id);
            adventurer.LoanUntilWeek = state.WeekNumber + CommissionBalance.LoanWeeks - 1; // 送り出した週を含めて LoanWeeks 週の決算のあとに帰る
            adventurer.LoanClientId = c.ClientId;

            var completion = Complete(state, c);
            completion.LoanedAdventurerName = adventurer.Name;
            return completion;
        }

        /// <summary>
        /// 派遣から帰ってくる（週の決算で、帰る週になった者・年度末に満期を迎える者に呼ぶ）：依頼人ごとの能力に成長の抽選を
        /// LoanGrowthRolls 回、LoanTraitChance で依頼人ごとの特性を1つ、HPは満タン。派遣中でなければ null。
        /// </summary>
        public LoanReturn? ReturnFromLoan(GameState state, Adventurer adventurer, GrowthSystem growth)
        {
            if (!adventurer.IsOnLoan)
                return null;
            var client = CommissionBalance.FindClient(adventurer.LoanClientId);
            var result = new LoanReturn(adventurer, client?.Name ?? adventurer.LoanClientId ?? "");
            result.Growth.AddRange(growth.ProcessLoanReturn(state, adventurer, client?.LoanStats ?? Array.Empty<string>(), CommissionBalance.LoanGrowthRolls));

            var traits = (client?.LoanTraits ?? Array.Empty<string>()).Where(t => adventurer.CanAddTrait(t)).ToList();
            if (traits.Count > 0 && _rng.NextInt(1, 100) <= (int)Math.Round(CommissionBalance.LoanTraitChance * 100))
            {
                string trait = traits[_rng.NextInt(0, traits.Count - 1)];
                if (adventurer.TryAddTrait(trait))
                    result.TraitId = trait;
            }

            adventurer.CurrentHP = adventurer.MaxHP;
            adventurer.LoanUntilWeek = null;
            adventurer.LoanClientId = null;
            return result;
        }

        /// <summary>
        /// 週の決算で派遣から帰ってくる者を返す（帰る週になった者。年度末なら、この年度末に満期を迎える者も先に帰る）。
        /// 加齢（満期引退）の前に呼ぶ。
        /// </summary>
        public List<LoanReturn> ProcessLoanReturns(GameState state, GrowthSystem growth)
        {
            bool yearEnd = GameCalendar.IsLastWeekOfYear(state.WeekNumber);
            var returning = state.Adventurers.Where(a => a.IsOnLoan
                && (state.WeekNumber >= a.LoanUntilWeek!.Value || (yearEnd && a.Age + 1 >= AgingSystem.RetirementAge))).ToList();
            return returning.Select(a => ReturnFromLoan(state, a, growth)).OfType<LoanReturn>().ToList();
        }

        // ==================== 達成・失敗 ====================

        /// <summary>今の状態で達成の条件を満たしているか（撃破・解析＝対象の状態、納品＝在庫、派遣＝候補がいる）。</summary>
        public static bool IsAchieved(GameState state, GuildCommission c)
        {
            var boss = FindBoss(state, c);
            return c.Type switch
            {
                CommissionType.Defeat => boss?.IsDefeated == true,
                CommissionType.Survey => boss != null && ScoutingResolver.GetTier(boss.IntelRate) == IntelTier.Complete,
                CommissionType.Deliver => c.MaterialId != null && state.Materials.TryGetValue(c.MaterialId, out int n) && n >= c.Count,
                _ => GetLoanCandidates(state, c).Count > 0,
            };
        }

        private CommissionCompletion Complete(GameState state, GuildCommission c)
        {
            state.Commissions.Remove(c);
            var client = CommissionBalance.FindClient(c.ClientId);
            var result = new CommissionCompletion(c, client?.Name ?? c.ClientId);

            state.Gold += c.RewardGold;
            result.Gold = c.RewardGold;

            var relic = AppraisalSystem.CreateRelic(_rng, c.FieldId, c.RewardRelicFloor,
                CommissionBalance.RewardRelicRollBonus, CommissionBalance.RewardRelicMinRarity);
            state.UnidentifiedItems.Add(relic);
            result.Relic = relic;

            result.MoodApplied = MasterMoodSystem.Adjust(state, CommissionBalance.CompletionMoodGain);

            if (client?.BonusMaterialId != null && client.BonusMaterialCount > 0)
            {
                state.AddMaterial(client.BonusMaterialId, client.BonusMaterialCount);
                result.BonusMaterialId = client.BonusMaterialId;
                result.BonusMaterialCount = client.BonusMaterialCount;
            }

            int count = state.CommissionCompletions.TryGetValue(c.ClientId, out int n) ? n + 1 : 1;
            state.CommissionCompletions[c.ClientId] = count;
            result.ClientCompletions = count;
            if (count >= CommissionBalance.PatronUniqueCompletions && UniqueBalance.FindPatronReward(c.ClientId) is { } unique)
                result.PatronUnique = UniqueItemSystem.Grant(state, unique, $"{result.ClientName}の依頼を{count}件達成した礼");

            return result;
        }

        private static CommissionFailure Fail(GameState state, GuildCommission c, string reason)
        {
            state.Commissions.Remove(c);
            int applied = MasterMoodSystem.Adjust(state, -CommissionBalance.FailureMoodLoss);
            return new CommissionFailure(c, CommissionBalance.FindClient(c.ClientId)?.Name ?? c.ClientId, reason, applied);
        }

        // ==================== 表示 ====================

        /// <summary>撃破・解析の対象ボス（フィールドから Id で引く）。無ければnull。</summary>
        public static FloorBoss? FindBoss(GameState state, GuildCommission c) =>
            c.BossId == null ? null : state.DungeonFields.SelectMany(f => f.Bosses).FirstOrDefault(b => b.Id == c.BossId);

        /// <summary>依頼人の名前（CSVから消えていればId）。</summary>
        public static string ClientName(GuildCommission c) => CommissionBalance.FindClient(c.ClientId)?.Name ?? c.ClientId;

        /// <summary>種類の短い名前。</summary>
        public static string TypeLabel(CommissionType type) => type switch
        {
            CommissionType.Defeat => "撃破",
            CommissionType.Survey => "完全解析",
            CommissionType.Deliver => "納品",
            _ => "派遣",
        };

        /// <summary>条件と今の進み具合の短い表記（例：「嘆きの鍾乳洞 40F「洞窟の古竜」を撃破」「月光草 8個を納品（在庫5個）」）。</summary>
        public static string DescribeCondition(GameState state, GuildCommission c)
        {
            var field = state.DungeonFields.FirstOrDefault(f => f.Id == c.FieldId);
            var boss = FindBoss(state, c);
            string target = boss == null ? "" : $"{field?.Name} {boss.Floor}F「{boss.Name}」";
            return c.Type switch
            {
                CommissionType.Defeat => $"{target}を撃破",
                CommissionType.Survey => $"{target}を完全解析（解析率{(int)Math.Round((boss?.IntelRate ?? 0) * 100)}%）",
                CommissionType.Deliver =>
                    $"{MaterialBalance.Find(c.MaterialId ?? "")?.Name ?? c.MaterialId} {c.Count}個を納品（在庫{(c.MaterialId != null && state.Materials.TryGetValue(c.MaterialId, out int n) ? n : 0)}個）",
                _ => $"{c.StatName}{c.StatThreshold}以上の冒険者を1人、{CommissionBalance.LoanWeeks}週派遣（該当{GetLoanCandidates(state, c).Count}名）",
            };
        }

        /// <summary>報酬の短い表記（例：「1300G・金以上の遺物・機嫌+10・紅玉鉱石×2」）。</summary>
        public static string DescribeReward(GuildCommission c)
        {
            var parts = new List<string> { $"{c.RewardGold}G", "金以上の遺物", $"機嫌+{CommissionBalance.CompletionMoodGain}" };
            var client = CommissionBalance.FindClient(c.ClientId);
            if (client?.BonusMaterialId != null && client.BonusMaterialCount > 0)
                parts.Add($"{MaterialBalance.Find(client.BonusMaterialId)?.Name ?? client.BonusMaterialId}×{client.BonusMaterialCount}");
            return string.Join("・", parts);
        }

        /// <summary>依頼人の固有武具までの道のり（例：「達成2/5件で『騎士団の宝剣』」。入手済み・割り当て無しなら空）。</summary>
        public static string DescribePatronProgress(GameState state, string clientId)
        {
            var unique = UniqueBalance.FindPatronReward(clientId);
            if (unique == null || state.ObtainedUniqueIds.Contains(unique.Id))
                return "";
            int n = state.CommissionCompletions.TryGetValue(clientId, out int v) ? v : 0;
            return $"達成{n}/{CommissionBalance.PatronUniqueCompletions}件で『{unique.Name}』";
        }
    }

    /// <summary>週次決算での依頼の判定結果（→ CommissionSystem.ProcessSettlement）。</summary>
    public class CommissionSettlement
    {
        public List<CommissionCompletion> Completed { get; } = new();
        public List<CommissionFailure> Failed { get; } = new();
    }

    /// <summary>新しい週の始まりに届いたもの（→ CommissionSystem.ProcessNewWeek）。</summary>
    public class CommissionArrivals
    {
        /// <summary>今週届いた依頼（季節のはじめ）。</summary>
        public List<GuildCommission> Offered { get; } = new();

        /// <summary>今週予告された異変（季節の予告の週）。</summary>
        public DungeonAnomaly? AnnouncedAnomaly { get; set; }

        /// <summary>期限が近づいた、まだ達成していない受けた依頼。</summary>
        public List<GuildCommission> DeadlineNear { get; } = new();
    }

    /// <summary>依頼1件の達成と報酬（→ CommissionSystem）。</summary>
    public class CommissionCompletion
    {
        public CommissionCompletion(GuildCommission commission, string clientName)
        {
            Commission = commission;
            ClientName = clientName;
        }

        public GuildCommission Commission { get; }
        public string ClientName { get; }
        public int Gold { get; set; }
        public UnidentifiedItem? Relic { get; set; }
        public int MoodApplied { get; set; }
        public string? BonusMaterialId { get; set; }
        public int BonusMaterialCount { get; set; }

        /// <summary>この依頼人の依頼の達成件数（この達成を含む）。</summary>
        public int ClientCompletions { get; set; }

        /// <summary>今回届いた依頼人の固有武具（届かなければnull）。</summary>
        public EquipmentItem? PatronUnique { get; set; }

        /// <summary>派遣した冒険者の名前（派遣のときだけ）。</summary>
        public string? LoanedAdventurerName { get; set; }
    }

    /// <summary>派遣から帰ってきた冒険者1人（§0.85、→ CommissionSystem.ReturnFromLoan）。</summary>
    public class LoanReturn
    {
        public LoanReturn(Adventurer adventurer, string clientName)
        {
            Adventurer = adventurer;
            ClientName = clientName;
        }

        public Adventurer Adventurer { get; }
        public string ClientName { get; }
        public List<GrowthEvent> Growth { get; } = new();
        /// <summary>派遣先で付いた特性（付かなければ null）。</summary>
        public string? TraitId { get; set; }
    }

    /// <summary>受けた依頼の失敗（期限切れ・達成不能）。MoodApplied はクランプ後に実際に動いた量（0以下）。</summary>
    public record CommissionFailure(GuildCommission Commission, string ClientName, string Reason, int MoodApplied);
}
