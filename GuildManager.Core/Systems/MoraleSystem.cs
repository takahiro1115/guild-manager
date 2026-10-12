using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 満足度と相性の段階（段4b、2026年10月・§0.101。→ docs/検討中_経営と育成.md 第2部 §6・§7）。
    ///  - 満足度の段階：上機嫌（80〜）・ふつう・不満（〜39）・限界（〜19）。のりしろつきで、週の終わりに決め直す（→ UpdateStage）。
    ///    力の倍率（上機嫌×1.05・不満×0.95・限界×0.90）、上機嫌は成長の判定と研究の手伝いが上がり、不満・限界は道中のHPの減りが増え、
    ///    訓練・自主練・研究の手伝いをさぼることがある。限界の子と同じ部隊の子は満足度が下がる（不満の伝染）。
    ///  - 志向の力と成長（段4a の志向）：主志向の仕事に出ているときは力と成長が上がり、苦手なら下がる。
    ///  - 相性の段階：相棒（80〜）が同じ部隊にいれば力が上がり（決戦では除籍・重傷をかばうことがある → DungeonResolver）、
    ///    険悪な相手がいれば力が下がり道中のHPの減りが増える。
    ///  - 相性の素地の帯：共鳴・調和・ふつう・不協和で、相性の上がり方・下がり方・はじめの値が変わる（カリスマは1つ近く、気難しいは1つ遠く）。
    /// 力の倍率のうち、その子だけで決まるもの（満足度の段階・志向）は1人分の値（DungeonPowerCalculator.MemberPower など）に、
    /// 仲間との相性で決まるもの（相棒・険悪）は部隊の合計（state を渡したとき）に掛ける。乱数はさぼりの判定（RollSlackers）だけ。
    /// </summary>
    public static class MoraleSystem
    {
        // ==================== 満足度の段階 ====================

        /// <summary>今の段階と満足度から、次の段階を決める（のりしろ：抜けるには少し余分に戻す）。</summary>
        public static MoraleStage NextStage(MoraleStage current, int satisfaction)
        {
            if (satisfaction < PersonalityBalance.MoraleBreakingEnter
                || (current == MoraleStage.Breaking && satisfaction < PersonalityBalance.MoraleBreakingExit))
                return MoraleStage.Breaking;
            if (satisfaction < PersonalityBalance.MoraleDiscontentEnter
                || (current is MoraleStage.Discontent or MoraleStage.Breaking && satisfaction < PersonalityBalance.MoraleDiscontentExit))
                return MoraleStage.Discontent;
            if (satisfaction >= PersonalityBalance.MoraleCheerfulEnter
                || (current == MoraleStage.Cheerful && satisfaction >= PersonalityBalance.MoraleCheerfulExit))
                return MoraleStage.Cheerful;
            return MoraleStage.Normal;
        }

        public static void UpdateStage(Adventurer a) => a.MoraleStage = NextStage(a.MoraleStage, a.Satisfaction);

        /// <summary>週の終わりに全員の段階を決め直す（満足度の週の変動・契約交渉のあと）。</summary>
        public static void UpdateStages(GameState state)
        {
            foreach (var a in state.Adventurers)
                UpdateStage(a);
        }

        public static double StageMultiplier(Adventurer a) => a.MoraleStage switch
        {
            MoraleStage.Cheerful => PersonalityBalance.MoraleMultiplierCheerful,
            MoraleStage.Discontent => PersonalityBalance.MoraleMultiplierDiscontent,
            MoraleStage.Breaking => PersonalityBalance.MoraleMultiplierBreaking,
            _ => 1.0,
        };

        public static bool IsLow(Adventurer a) => a.MoraleStage is MoraleStage.Discontent or MoraleStage.Breaking;

        public static string StageLabel(MoraleStage stage) => stage switch
        {
            MoraleStage.Cheerful => "😊 上機嫌",
            MoraleStage.Discontent => "😒 不満",
            MoraleStage.Breaking => "😠 限界",
            _ => "ふつう",
        };

        // ==================== 志向の力と成長 ====================

        /// <summary>任務の種類に当たる志向（迷宮調査 Survey＝調査、採取＝採取、潜行 Scouting・決戦＝潜行）。</summary>
        public static Drive DriveFor(DungeonMissionType type) => type switch
        {
            DungeonMissionType.Survey => Drive.Survey,
            DungeonMissionType.Gathering => Drive.Gather,
            _ => Drive.Dive, // 潜行（Scouting は旧い名前で、1階から潜る潜行）と決戦
        };

        /// <summary>その仕事での志向の力の上乗せ（主+6%・副+3%・苦手−5%、個性が無ければ0）。</summary>
        public static double DrivePower(Adventurer a, Drive activity) => a.Personality?.AttitudeOf(activity) switch
        {
            DriveAttitude.Main => PersonalityBalance.DrivePowerMain,
            DriveAttitude.Sub => PersonalityBalance.DrivePowerSub,
            DriveAttitude.Disliked => PersonalityBalance.DrivePowerDisliked,
            _ => 0,
        };

        /// <summary>1人分の力の倍率＝満足度の段階×(1＋志向の上乗せ)。activity が null なら段階だけ。</summary>
        public static double MemberFactor(Adventurer a, Drive? activity) =>
            StageMultiplier(a) * (1 + (activity is Drive d ? DrivePower(a, d) : 0))
            * (a.Pumped ? 1 + PersonalityBalance.PumpedPowerBonus : 1.0); // 張り切り（§0.102）

        /// <summary>その仕事での成長の判定に足す値（志向＋上機嫌）。任務の判定では×100して%ポイントで足す。</summary>
        public static double GrowthBonus(Adventurer a, Drive activity) =>
            (a.Personality?.AttitudeOf(activity) switch
            {
                DriveAttitude.Main => PersonalityBalance.DriveGrowthMain,
                DriveAttitude.Sub => PersonalityBalance.DriveGrowthSub,
                DriveAttitude.Disliked => PersonalityBalance.DriveGrowthDisliked,
                _ => 0,
            })
            + (a.MoraleStage == MoraleStage.Cheerful ? PersonalityBalance.MoraleCheerfulGrowthBonus : 0);

        /// <summary>研究の手伝いの貯まりの倍率（上機嫌は×1.5）。</summary>
        public static double HelpCreditMultiplier(Adventurer a) =>
            a.MoraleStage == MoraleStage.Cheerful ? PersonalityBalance.MoraleCheerfulHelpCreditMultiplier : 1.0;

        // ==================== 相性の段階 ====================

        public static bool IsBuddy(GameState state, Guid a, Guid b) =>
            CompatibilitySystem.GetCompatibility(state, a, b) >= PersonalityBalance.BuddyThreshold;

        /// <summary>仲間との相性で決まる力の倍率：相棒が同じ部隊にいれば＋、険悪な相手がいれば−（両方なら両方）。state か squad が無ければ1。</summary>
        public static double PairFactor(GameState? state, Adventurer a, IEnumerable<Adventurer>? squad)
        {
            if (state == null || squad == null) return 1.0;
            var mates = squad.Where(m => m.Id != a.Id).ToList();
            double bonus = 0;
            if (mates.Any(m => IsBuddy(state, a.Id, m.Id))) bonus += PersonalityBalance.BuddyPowerBonus;
            if (mates.Any(m => CompatibilitySystem.IsHostile(state, a.Id, m.Id))) bonus += PersonalityBalance.HostilePowerPenalty;
            return 1 + bonus;
        }

        /// <summary>道中のHPの減りの倍率：不満・限界なら×1.1、険悪な相手が同じ部隊にいれば×1.1（両方なら掛け合わせる）。</summary>
        public static double TraversalHpLossMultiplier(GameState? state, Adventurer a, IEnumerable<Adventurer> squad)
        {
            double m = IsLow(a) ? PersonalityBalance.MoraleLowTraversalHpLossMultiplier : 1.0;
            if (state != null && squad.Any(x => x.Id != a.Id && CompatibilitySystem.IsHostile(state, a.Id, x.Id)))
                m *= PersonalityBalance.HostileTraversalHpLossMultiplier;
            return m;
        }

        /// <summary>決戦で除籍・重傷になりそうな子をかばう相棒（同じ部隊でHPが2以上、相性がいちばん高い子）。いなければ null。</summary>
        public static Adventurer? FindGuardian(GameState state, Adventurer member, IEnumerable<Adventurer> squad) =>
            squad.Where(m => m.Id != member.Id && m.CurrentHP > 1 && IsBuddy(state, member.Id, m.Id))
                .OrderByDescending(m => CompatibilitySystem.GetCompatibility(state, member.Id, m.Id))
                .FirstOrDefault();

        // ==================== 相性の素地の帯 ====================

        /// <summary>2人の帯（どちらかに個性が無ければふつう）。カリスマは1つ近く、気難しいは1つ遠く（2人分を足す）。</summary>
        public static AffinityBand BandOf(Adventurer? a, Adventurer? b)
        {
            if (a?.Personality == null || b?.Personality == null) return AffinityBand.Neutral;
            int d = PersonalitySystem.AffinityDistance(a.Personality.Affinity, b.Personality.Affinity);
            var bands = PersonalityBalance.AffinityBandMax;
            int band = d <= bands[0] ? 0 : d <= bands[1] ? 1 : d <= bands[2] ? 2 : 3;
            foreach (var x in new[] { a, b })
            {
                if (x.HasTrait(TraitCatalog.CharismaId)) band--;
                if (x.HasTrait(TraitCatalog.MoodyId)) band++;
            }
            return (AffinityBand)Math.Clamp(band, 0, 3);
        }

        public static double GainMultiplier(AffinityBand band) => PersonalityBalance.AffinityGain[(int)band];
        public static double LossMultiplier(AffinityBand band) => PersonalityBalance.AffinityLoss[(int)band];
        public static int InitialCompatibility(AffinityBand band) => PersonalityBalance.AffinityInitial[(int)band];

        // ==================== 週の判定：さぼり・不満の伝染 ====================

        /// <summary>
        /// 今週さぼる子（訓練・自主練・研究の手伝いの週の、不満・限界の子。確率は不満10%・限界25%、怠け者・飽きっぽい＋5、求道派−5）。
        /// 乱数は対象の子ごとに NextInt(1, 100) を1回。週の過ごし方を決めたあと、訓練・自主練・研究の手伝いを解く前に呼ぶ。
        /// </summary>
        public static HashSet<Guid> RollSlackers(GameState state, IReadOnlyDictionary<Guid, WeekActivity> activities, IRng rng)
        {
            var slackers = new HashSet<Guid>();
            foreach (var a in state.Adventurers.Where(a => !a.IsRetired && IsLow(a)))
            {
                if (!activities.TryGetValue(a.Id, out var act) || act is not (WeekActivity.Training or WeekActivity.SelfTraining or WeekActivity.Help))
                    continue;
                int chance = a.MoraleStage == MoraleStage.Breaking ? PersonalityBalance.SlackChanceBreaking : PersonalityBalance.SlackChanceDiscontent;
                if (a.HasTrait(TraitCatalog.SlothfulId)) chance += PersonalityBalance.SlackTraitModifier;
                if (a.HasTrait(TraitCatalog.FickleId)) chance += PersonalityBalance.SlackTraitModifier;
                if (a.Personality?.Main == Drive.Training) chance -= PersonalityBalance.SlackTraitModifier;
                if (rng.NextInt(1, 100) <= chance)
                    slackers.Add(a.Id);
            }
            return slackers;
        }

        /// <summary>不満の伝染：今週、限界の子と同じ部隊で出撃した子の満足度を下げる（1部隊に限界の子が何人いても1回）。</summary>
        public static void ApplyContagion(GameState state, IReadOnlyList<ActiveDungeonMission> missionsAtStart)
        {
            foreach (var m in missionsAtStart)
            {
                var members = m.Party.Members;
                foreach (var a in members)
                    if (members.Any(x => x.Id != a.Id && x.MoraleStage == MoraleStage.Breaking))
                        SatisfactionSystem.Adjust(a, PersonalityBalance.MoraleBreakingContagion);
            }
        }
    }
}
