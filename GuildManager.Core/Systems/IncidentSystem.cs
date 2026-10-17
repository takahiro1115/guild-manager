using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>出来事・事故の種類（§0.102）。</summary>
    public enum IncidentKind
    {
        Quarrel, Gloom, Waste, Fret, Poach,
        Mentor, Insight, Treat, Pumped,
        Bond, Brawl,
        Boycott, Refuse, Charge, Flee,
        PoachLeft,
        /// <summary>打ち解けた（§0.151。セーブの値がずれないよう最後に足した）。</summary>
        Warm,
    }

    /// <summary>
    /// 出来事・事故の1件（月報の主な出来事に出す）。Good はプラスの出来事。
    /// Who・Other・Detail は出来事の報告（§0.141、→ EventReportSystem）で当人に話させるためのもの：当人・相手（後輩・口論の相手など）・
    /// 中身（無駄づかいの額・引き抜きのギルド・ひらめきで伸びた能力）。
    /// </summary>
    public sealed record IncidentEvent(IncidentKind Kind, string Text, bool Good, Guid? Who = null, Guid? Other = null, string Detail = "");

    /// <summary>引き抜きの話への答え。</summary>
    public enum PoachChoice
    {
        /// <summary>昇給（週給×1.5）して引き留める。</summary>
        Raise,
        /// <summary>ボーナス（週給×8）で引き留める。</summary>
        Bonus,
        /// <summary>送り出す（移籍する）。</summary>
        Release,
    }

    /// <summary>
    /// 出来事と事故（段4c、2026年10月・§0.102。→ docs/検討中_経営と育成.md 第2部 §6.5・§7.3・§8）。
    /// 冒険者が自分の主義主張で動いているように見せ、わがままや不満をなだめることをゲームの一部にする。
    ///  - 満足度の出来事（週ごと・子ごと）：上機嫌はプラス（後輩の面倒・ひらめき・差し入れ・張り切り）、不満・限界はマイナス
    ///    （口論・ふさぎ込み・無駄づかい・焦り、限界だけ引き抜きの話）。どれが起きやすいかは志向・気質・特性で変わる。
    ///  - 相性の出来事（週ごと・同じ部隊の2人ごと）：相棒は休日の絆、険悪は宿舎の衝突（素地の帯で起きやすさが変わる）。
    ///  - 事故（見立てで読み切れない）：方針の出撃のボイコット・編成の拒否（険悪は確率、犬猿は必ず）、扉前の命令無視。
    ///    起きるのは見える悪い状態（不満・限界・険悪・気質と構えの食い違い）のときだけで、危険は ⚠ で見せる（→ Risks）。
    /// 乱数は出来事・事故それぞれ呼び出し側が渡す専用の乱数から引く（ほかの抽選の順を変えない）。
    /// </summary>
    public static class IncidentSystem
    {
        // ==================== 満足度・相性の出来事（週の決算） ====================

        /// <summary>
        /// 週の決算の終わり（満足度の段階を決め直したあと）に呼ぶ。ふさぎ込みの週を減らし、先週出た子の焦り・張り切りを消し、
        /// 期限の過ぎた引き抜きの話で移籍させ、出来事を判定する。新しい引き抜きの話が来たら poachOffered を立てる。
        /// </summary>
        public static List<IncidentEvent> ProcessWeek(GameState state, int week, IReadOnlyList<ActiveDungeonMission> missionsAtStart, IRng rng, out bool poachOffered)
        {
            var events = new List<IncidentEvent>();
            poachOffered = false;

            foreach (var a in state.Adventurers.Where(a => a.GloomWeeksRemaining > 0))
                a.GloomWeeksRemaining--;
            foreach (var a in missionsAtStart.SelectMany(m => m.Party.Members))
            {
                a.Fretful = false;
                a.Pumped = false;
            }

            // 返事の無いまま期限を過ぎた引き抜きの話：移籍する
            foreach (var offer in state.PoachOffers.Where(o => o.DeadlineWeek <= week).ToList())
            {
                state.PoachOffers.Remove(offer);
                if (state.FindAdventurer(offer.AdventurerId) is { } leaver && !leaver.IsRetired)
                {
                    SatisfactionSystem.Terminate(state, leaver);
                    events.Add(new IncidentEvent(IncidentKind.PoachLeft, $"{leaver.Name}が{offer.GuildName}へ移籍した（引き抜きの話に応えなかった）", false, leaver.Id, null, offer.GuildName));
                }
            }

            foreach (var a in state.Adventurers.Where(a => !a.IsRetired && !a.IsOnLoan && !a.NeverRetires).ToList())
            {
                int chance = a.MoraleStage switch
                {
                    MoraleStage.Cheerful => PersonalityBalance.EventChanceCheerful,
                    MoraleStage.Discontent => PersonalityBalance.EventChanceDiscontent,
                    MoraleStage.Breaking => PersonalityBalance.EventChanceBreaking,
                    _ => PersonalityBalance.EventChanceNormal,
                };
                if (rng.NextInt(1, 100) > chance) continue;
                bool good = a.MoraleStage switch
                {
                    MoraleStage.Cheerful => true,
                    MoraleStage.Normal => rng.NextInt(1, 2) == 1,
                    _ => false,
                };
                var mates = SquadMates(state, a);
                var ev = good ? PositiveEvent(state, a, mates, rng) : NegativeEvent(state, a, mates, week, rng, ref poachOffered);
                if (ev != null) events.Add(ev);
            }

            // 相性の出来事：同じ部隊の2人ごと
            foreach (var party in state.SavedParties)
            {
                var members = party.MemberIds.Select(state.FindAdventurer).OfType<Adventurer>().Where(m => !m.IsRetired && !m.IsOnLoan).ToList();
                for (int i = 0; i < members.Count; i++)
                    for (int j = i + 1; j < members.Count; j++)
                    {
                        var x = members[i];
                        var y = members[j];
                        int compat = CompatibilitySystem.GetCompatibility(state, x.Id, y.Id);
                        if (compat >= PersonalityBalance.BuddyThreshold)
                        {
                            if (rng.NextInt(1, 100) > PersonalityBalance.BondChancePercent) continue;
                            ChangeCompat(state, x, y, PersonalityBalance.BondCompatGain);
                            SatisfactionSystem.Adjust(x, PersonalityBalance.BondSatisfaction);
                            SatisfactionSystem.Adjust(y, PersonalityBalance.BondSatisfaction);
                            events.Add(new IncidentEvent(IncidentKind.Bond, $"🌸 {x.Name}と{y.Name}が休日に街へ出かけ、絆を深めた（相性+{PersonalityBalance.BondCompatGain}）", true, x.Id, y.Id));
                        }
                        else if (compat >= CompatibilityBalance.HostileThreshold && PersonalityBalance.WarmChancePercent > 0) // 0なら乱数も引かない（§0.151 の案は保留）
                        {
                            // 打ち解けた（§0.151）：一緒の任務では相性が上がらないので、ふだんの相性はこの出来事で上がる（素地の帯で上がり方が変わる）
                            if (rng.NextInt(1, 100) > PersonalityBalance.WarmChancePercent) continue;
                            int gain = (int)Math.Round(PersonalityBalance.WarmCompatGain * MoraleSystem.GainMultiplier(MoraleSystem.BandOf(x, y)), MidpointRounding.AwayFromZero);
                            ChangeCompat(state, x, y, gain);
                            SatisfactionSystem.Adjust(x, PersonalityBalance.WarmSatisfaction);
                            SatisfactionSystem.Adjust(y, PersonalityBalance.WarmSatisfaction);
                            events.Add(new IncidentEvent(IncidentKind.Warm, $"☕ {x.Name}と{y.Name}が、野営の夜に打ち解けた（相性+{gain}）", true, x.Id, y.Id));
                        }
                        else if (compat < CompatibilityBalance.HostileThreshold)
                        {
                            double c = PersonalityBalance.BrawlChancePercent * MoraleSystem.LossMultiplier(MoraleSystem.BandOf(x, y));
                            if (rng.NextInt(1, 100) > (int)Math.Round(c)) continue;
                            int loss = (int)Math.Round(PersonalityBalance.BrawlCompatLoss * MoraleSystem.LossMultiplier(MoraleSystem.BandOf(x, y)), MidpointRounding.AwayFromZero);
                            ChangeCompat(state, x, y, -loss);
                            Bruise(x);
                            Bruise(y);
                            events.Add(new IncidentEvent(IncidentKind.Brawl, $"⚡ 宿舎で{x.Name}と{y.Name}が取っ組み合いになった（2人とも軽傷・相性−{loss}）", false, x.Id, y.Id));
                        }
                    }
            }
            return events;
        }

        /// <summary>同じ部隊（保存した部隊）の仲間。</summary>
        private static List<Adventurer> SquadMates(GameState state, Adventurer a) =>
            state.SavedParties.FirstOrDefault(p => p.MemberIds.Contains(a.Id))?.MemberIds
                .Where(id => id != a.Id).Select(state.FindAdventurer).OfType<Adventurer>().Where(m => !m.IsRetired && !m.IsOnLoan).ToList()
            ?? new List<Adventurer>();

        private static void ChangeCompat(GameState state, Adventurer x, Adventurer y, int delta)
        {
            var key = CompatibilitySystem.NormalizeKey(x.Id, y.Id);
            int current = CompatibilitySystem.GetCompatibility(state, x.Id, y.Id);
            state.Compatibility[key] = Math.Clamp(current + delta, CompatibilityBalance.MinValue, CompatibilityBalance.MaxValue);
        }

        /// <summary>宿舎の衝突の軽傷（1週。すでに負傷中なら重ねない）。</summary>
        private static void Bruise(Adventurer a)
        {
            if (a.Injury != InjurySeverity.None) return;
            a.Injury = InjurySeverity.Light;
            a.InjuryWeeksRemaining = 1;
        }

        private static bool Has(Adventurer a, Drive d) => a.Personality is { } p && (p.Main == d || p.Sub == d);

        private static T Pick<T>(IReadOnlyList<(T Item, int Weight)> items, IRng rng)
        {
            int total = items.Sum(i => i.Weight);
            int roll = rng.NextInt(1, Math.Max(1, total));
            foreach (var (item, weight) in items)
            {
                roll -= weight;
                if (roll <= 0) return item;
            }
            return items[^1].Item;
        }

        private static int W(bool pull) => PersonalityBalance.EventWeightBase + (pull ? PersonalityBalance.EventWeightPull : 0);

        private static IncidentEvent? NegativeEvent(GameState state, Adventurer a, List<Adventurer> mates, int week, IRng rng, ref bool poachOffered)
        {
            int t = a.Personality?.Temperament ?? 0;
            var options = new List<(IncidentKind, int)>();
            if (mates.Count > 0)
                options.Add((IncidentKind.Quarrel, W(mates.Any(m => MoraleSystem.BandOf(a, m) == AffinityBand.Dissonant || CompatibilitySystem.IsHostile(state, a.Id, m.Id)) || Has(a, Drive.Comrades))));
            options.Add((IncidentKind.Gloom, W(t < 0 || a.HasTrait(TraitCatalog.TraumaId))));
            options.Add((IncidentKind.Waste, W(Has(a, Drive.Money) || a.HasTrait(TraitCatalog.SpendthriftId))));
            options.Add((IncidentKind.Fret, W(Has(a, Drive.Fame) || t > 0)));
            if (a.MoraleStage == MoraleStage.Breaking && state.RivalGuilds.Count > 0 && state.PoachOffers.All(o => o.AdventurerId != a.Id))
                options.Add((IncidentKind.Poach, W(Has(a, Drive.Money) || Has(a, Drive.Fame))));

            switch (Pick(options, rng))
            {
                case IncidentKind.Quarrel:
                {
                    var other = mates.OrderBy(m => CompatibilitySystem.GetCompatibility(state, a.Id, m.Id)).First();
                    int loss = (int)Math.Round(PersonalityBalance.QuarrelCompatLoss * MoraleSystem.LossMultiplier(MoraleSystem.BandOf(a, other)), MidpointRounding.AwayFromZero);
                    ChangeCompat(state, a, other, -loss);
                    SatisfactionSystem.Adjust(a, PersonalityBalance.QuarrelSatisfaction);
                    SatisfactionSystem.Adjust(other, PersonalityBalance.QuarrelSatisfaction);
                    return new IncidentEvent(IncidentKind.Quarrel, $"⚡ {a.Name}が{other.Name}と口論した（相性−{loss}）", false, a.Id, other.Id);
                }
                case IncidentKind.Gloom:
                {
                    int weeks = rng.NextInt(PersonalityBalance.GloomWeeksMin, PersonalityBalance.GloomWeeksMax);
                    if (state.GetFacilityLevel(FacilityType.Infirmary) >= 3) weeks--;
                    a.GloomWeeksRemaining = Math.Max(1, weeks);
                    return new IncidentEvent(IncidentKind.Gloom, $"😞 {a.Name}が部屋にこもってしまった（{a.GloomWeeksRemaining}週、出撃も訓練もしない）", false, a.Id);
                }
                case IncidentKind.Waste:
                {
                    int gold = a.WeeklyWage * PersonalityBalance.WasteWageWeeks;
                    state.Gold -= gold;
                    return new IncidentEvent(IncidentKind.Waste, $"💸 {a.Name}がギルドのお金で勝手に買い物をした（−{gold}G）", false, a.Id, null, gold.ToString());
                }
                case IncidentKind.Fret:
                    a.Fretful = true;
                    return new IncidentEvent(IncidentKind.Fret, $"😣 {a.Name}は手柄の無さに焦っている（次の決戦でけがをしやすい）", false, a.Id);
                default:
                {
                    var guild = state.RivalGuilds[rng.NextInt(0, state.RivalGuilds.Count - 1)];
                    state.PoachOffers.Add(new PoachOffer
                    {
                        AdventurerId = a.Id, AdventurerName = a.Name, GuildName = guild.Name,
                        DeadlineWeek = week + PersonalityBalance.PoachDeadlineWeeks,
                    });
                    poachOffered = true;
                    return new IncidentEvent(IncidentKind.Poach, $"📨 {a.Name}に{guild.Name}から引き抜きの話が来た（{PersonalityBalance.PoachDeadlineWeeks}週のうちに応えないと移籍する）", false, a.Id, null, guild.Name);
                }
            }
        }

        private static IncidentEvent? PositiveEvent(GameState state, Adventurer a, List<Adventurer> mates, IRng rng)
        {
            int t = a.Personality?.Temperament ?? 0;
            var younger = mates.Where(m => m.Age < a.Age).OrderBy(m => m.Age).FirstOrDefault();
            var options = new List<(IncidentKind, int)>();
            if (younger != null)
                options.Add((IncidentKind.Mentor, W(Has(a, Drive.Comrades) || a.HasTrait(TraitCatalog.MentorId))));
            options.Add((IncidentKind.Insight, W(a.Personality?.Main == Drive.Training || a.HasTrait(TraitCatalog.GeniusId))));
            if (mates.Count > 0)
                options.Add((IncidentKind.Treat, W(Has(a, Drive.Comrades) || a.HasTrait(TraitCatalog.CheerfulId))));
            options.Add((IncidentKind.Pumped, W(Has(a, Drive.Fame) || t > 0)));

            switch (Pick(options, rng))
            {
                case IncidentKind.Mentor:
                    ChangeCompat(state, a, younger!, PersonalityBalance.MentorCompatGain);
                    SatisfactionSystem.Adjust(younger!, PersonalityBalance.MentorSatisfaction);
                    return new IncidentEvent(IncidentKind.Mentor, $"🤝 {a.Name}が後輩の{younger!.Name}の面倒を見た（相性+{PersonalityBalance.MentorCompatGain}）", true, a.Id, younger.Id);
                case IncidentKind.Insight:
                {
                    string stat = GrowthBalance.PickJobWeightedStat(a.JobClass, rng);
                    int before = AdventurerStatAccessor.GetStat(a, stat);
                    int after = Math.Min(AdventurerStatAccessor.GetPa(a, stat), before + 1);
                    AdventurerStatAccessor.SetStat(a, stat, after);
                    return new IncidentEvent(IncidentKind.Insight, after > before
                        ? $"💡 {a.Name}が稽古でこつをつかんだ（{stat}+1）"
                        : $"💡 {a.Name}が稽古でこつをつかんだ（もう伸びしろは無かった）", true, a.Id, null, after > before ? stat : "");
                }
                case IncidentKind.Treat:
                    foreach (var m in mates) SatisfactionSystem.Adjust(m, PersonalityBalance.TreatSatisfaction);
                    return new IncidentEvent(IncidentKind.Treat, $"🍰 {a.Name}が部隊のみんなに差し入れをした（仲間の満足度+{PersonalityBalance.TreatSatisfaction}）", true, a.Id);
                default:
                    a.Pumped = true;
                    return new IncidentEvent(IncidentKind.Pumped, $"🔥 {a.Name}が張り切っている（次の出撃で力+{PersonalityBalance.PumpedPowerBonus * 100:0}%）", true, a.Id);
            }
        }

        // ==================== 引き抜きの話への答え ====================

        /// <summary>
        /// 引き抜きの話に答える。昇給（週給×1.5、満足度+PoachRaiseSatisfaction）か、ボーナス（週給×8、満足度+PoachBonusSatisfaction）で引き留めるか、送り出す（移籍）。
        /// 話が無ければ false。
        /// </summary>
        public static bool ResolvePoach(GameState state, PoachOffer offer, PoachChoice choice, SatisfactionSystem satisfaction)
        {
            if (!state.PoachOffers.Remove(offer)) return false;
            var a = state.FindAdventurer(offer.AdventurerId);
            if (a == null || a.IsRetired) return true;
            switch (choice)
            {
                case PoachChoice.Raise:
                    satisfaction.RaiseWage(a, SatisfactionBalance.MinRaiseMultiplier);
                    SatisfactionSystem.Adjust(a, PersonalityBalance.PoachRaiseSatisfaction);
                    break;
                case PoachChoice.Bonus:
                    satisfaction.PayBonus(state, a);
                    SatisfactionSystem.Adjust(a, PersonalityBalance.PoachBonusSatisfaction);
                    break;
                default:
                    SatisfactionSystem.Terminate(state, a);
                    break;
            }
            MoraleSystem.UpdateStage(a);
            return true;
        }

        // ==================== 事故：ボイコット・編成の拒否 ====================

        /// <summary>犬猿（相性が FeudThreshold 未満）か。</summary>
        public static bool IsFeud(GameState state, Guid a, Guid b) =>
            CompatibilitySystem.GetCompatibility(state, a, b) < PersonalityBalance.FeudThreshold;

        /// <summary>この子を部隊に入れられない理由（犬猿の相手がいれば。入れられるなら null）。</summary>
        public static string? AssignBlockReason(GameState state, SavedParty party, Guid adventurerId)
        {
            var feud = party.MemberIds.Where(id => id != adventurerId && IsFeud(state, adventurerId, id)).Select(state.FindAdventurer).OfType<Adventurer>().FirstOrDefault();
            if (feud == null) return null;
            string name = state.FindAdventurer(adventurerId)?.Name ?? "？";
            return $"{name}は「{feud.Name}と一緒なら出ない」と言っている（犬猿の仲）";
        }

        /// <summary>
        /// 方針の出撃のとき、加わらない子を決めて部隊から外す（ボイコット・編成の拒否）。外した子と理由を返す。
        /// 犬猿の相手が同じ部隊にいれば、満足度の低い方が必ず拒む。険悪な相手がいれば RefuseChanceHostile、不満・限界ならボイコットの確率
        /// （苦手な仕事なら＋、主志向の仕事・相棒が同じ部隊なら−）。乱数は判定する子ごとに1回。
        /// </summary>
        public static List<(Adventurer Adventurer, IncidentKind Kind, string Reason, Adventurer? Other)> RollBoycotts(GameState state, Party party, Drive activity, IRng rng)
        {
            var refused = new List<(Adventurer, IncidentKind, string, Adventurer?)>();
            var members = party.Members.ToList();
            foreach (var a in members)
            {
                var others = members.Where(m => m.Id != a.Id && refused.All(r => r.Item1.Id != m.Id)).ToList();
                var feud = others.FirstOrDefault(m => IsFeud(state, a.Id, m.Id) && a.Satisfaction <= m.Satisfaction);
                if (feud != null)
                {
                    refused.Add((a, IncidentKind.Refuse, $"{feud.Name}と一緒なら出ない", feud));
                    continue;
                }
                int chance = 0;
                IncidentKind kind = IncidentKind.Boycott;
                string reason = a.MoraleStage == MoraleStage.Breaking ? "部屋から出てこない" : "仮病を使った";
                if (MoraleSystem.IsLow(a))
                {
                    chance = a.MoraleStage == MoraleStage.Breaking ? PersonalityBalance.BoycottChanceBreaking : PersonalityBalance.BoycottChanceDiscontent;
                    var attitude = a.Personality?.AttitudeOf(activity);
                    if (attitude == DriveAttitude.Disliked) chance += PersonalityBalance.BoycottDriveModifier;
                    if (attitude == DriveAttitude.Main) chance -= PersonalityBalance.BoycottDriveModifier;
                    if (others.Any(m => MoraleSystem.IsBuddy(state, a.Id, m.Id))) chance -= PersonalityBalance.BoycottDriveModifier;
                }
                var foe = others.FirstOrDefault(m => CompatibilitySystem.IsHostile(state, a.Id, m.Id));
                if (foe != null)
                {
                    chance += PersonalityBalance.RefuseChanceHostile;
                    kind = IncidentKind.Refuse;
                    reason = $"「{foe.Name}とは出たくない」と言った";
                }
                if (chance <= 0) continue;
                if (rng.NextInt(1, 100) <= chance)
                    refused.Add((a, kind, reason, foe));
            }
            return refused;
        }

        // ==================== 事故：命令無視 ====================

        /// <summary>
        /// 扉前の判断（go＝挑む）に、気質が反対の向きの子が従わないか（不満・限界か、リーダーと険悪な子だけ）。
        /// 挑む判断に慎重寄りの子が逃げ出せば撤退、撤退の判断に大胆寄りの子が突っ込めば決戦。リーダーの LDR が高いほど起きにくく、
        /// カリスマのリーダーなら起きない。乱数は候補の子ごとに1回。起きなければ null。
        /// </summary>
        public static (Adventurer Who, bool Charge)? RollDisobey(GameState state, Party party, bool go, IRng rng)
        {
            if (party.Members.Count < 2) return null;
            var leader = party.Members[0];
            if (leader.HasTrait(TraitCatalog.CharismaId)) return null;
            double leaderFactor = Math.Max(0.5, 1.0 - leader.GetEffectiveStat("LDR") / (double)PersonalityBalance.DisobeyLeaderLdrDivisor);
            foreach (var a in party.Members.Skip(1))
            {
                int t = a.Personality?.Temperament ?? 0;
                if (t == 0 || (go ? t > 0 : t < 0)) continue; // 判断と同じ向きの気質なら従う
                bool hostileToLeader = CompatibilitySystem.IsHostile(state, a.Id, leader.Id);
                if (!MoraleSystem.IsLow(a) && !hostileToLeader) continue;
                int chance = a.MoraleStage == MoraleStage.Breaking ? PersonalityBalance.DisobeyChanceBreaking : PersonalityBalance.DisobeyChanceDiscontent;
                if (Math.Abs(t) >= 2) chance += PersonalityBalance.DisobeyVeryModifier;
                if (rng.NextInt(1, 100) <= (int)Math.Round(chance * leaderFactor))
                    return (a, !go);
            }
            return null;
        }

        /// <summary>逃げ出した子の後始末：本人の満足度と、残った仲間との相性が下がる。</summary>
        public static void ApplyFlee(GameState state, Party party, Adventurer who)
        {
            SatisfactionSystem.Adjust(who, PersonalityBalance.DisobeyFleeSatisfaction);
            foreach (var m in party.Members.Where(m => m.Id != who.Id))
                ChangeCompat(state, who, m, -PersonalityBalance.DisobeyFleeCompatLoss);
        }

        // ==================== 危険の表示（⚠） ====================

        public static bool ShowWarnings => PersonalityBalance.ShowAccidentWarnings != 0;

        /// <summary>この顔ぶれで起きうる事故の危険（⚠ の行。表示しない設定なら空）。起きるかどうかは分からない。</summary>
        public static List<string> Risks(GameState state, IReadOnlyList<Adventurer> members)
        {
            var risks = new List<string>();
            if (!ShowWarnings) return risks;
            foreach (var a in members.Where(MoraleSystem.IsLow))
                risks.Add($"{a.Name}は{MoraleSystem.StageLabel(a.MoraleStage)}：出撃しない・扉前の構えに従わないことがある");
            for (int i = 0; i < members.Count; i++)
                for (int j = i + 1; j < members.Count; j++)
                {
                    if (IsFeud(state, members[i].Id, members[j].Id))
                        risks.Add($"{members[i].Name}と{members[j].Name}は犬猿の仲：どちらかが必ず出撃を拒む");
                    else if (CompatibilitySystem.IsHostile(state, members[i].Id, members[j].Id))
                        risks.Add($"{members[i].Name}と{members[j].Name}は険悪：一緒に出たがらないことがある");
                }
            return risks;
        }
    }
}
