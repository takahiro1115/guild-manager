using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 週送り時の遠征解決エンジン。仕様書 03 §4（フェーズ1〜3）を実装する。
    ///
    ///  - フェーズ1：索敵・遭遇判定
    ///  - フェーズ2：戦闘比率とHP消費
    ///  - フェーズ3：負傷・致死判定（生存／不可逆障害＝古傷／戦死の3分岐。→ 03 §4.3）。
    ///    古傷は汎用の特性(Trait)エンジン上の「古傷」特性として付与する
    ///    （Adventurer.TryAddTrait経由）。重複禁止のため、既に古傷を持つ冒険者が
    ///    再び不可逆障害の判定域に入った場合は新たな付与をせず「生存（重傷）」に丸める。
    ///    戦死はこのクラスではロースターから除外しない（WeekResolutionResult.
    ///    FallenAdventurerIdsで呼び出し側に通知し、QuestDispatchSystem側で
    ///    GameState.FallenAdventurersへの移動・満足度ペナルティを行う）。
    /// </summary>
    public class QuestResolver
    {
        private readonly IRng _rng;

        // ---- 個人CP重み係数。→ BAL: 戦闘/CP重み（現状は仮値の直書き。後で外部化する） ----
        private const double WeightSTR = 0.8;
        private const double WeightAGI = 0.5;
        private const double WeightVIT = 0.6;
        private const double WeightDEX = 0.4; // → BAL: 戦闘/CP重み（暫定。v1.2改訂：DEXは索敵専任に加え個人CPにも参加）
        private const double WeightMND = 0.8;
        private const double WeightINT = 0.7; // → BAL: 戦闘/CP重み（暫定。v1.2改訂：予約フィールドから活性化）
        private const double WeightLDR = 0.4;
        private const double EnemyCpCoefficient = 3.0; // → BAL: 戦闘/敵CP係数

        // ---- フェーズ3：負傷・致死判定（→ 03 §4.3） ----
        private const int PermanentBand = 20; // → BAL: 戦闘/不可逆帯幅

        public QuestResolver(IRng rng)
        {
            _rng = rng;
        }

        /// <summary>
        /// パーティをクエストに派遣し、フェーズ1〜3を解決する。
        /// </summary>
        /// <param name="party">派遣パーティ。</param>
        /// <param name="quest">対象クエスト。</param>
        /// <param name="advisorBonus">
        /// 作戦参謀のボーナス（生涯ピーク7能力平均に比例。→ 03 §7.2・AdvisorSystem.
        /// GetAdvisorBonus）。PartyScout・SurvivalThresholdの両方に同じ値を加算する。
        /// 参謀が未配置なら0を渡す（デフォルト値）。
        /// </param>
        public WeekResolutionResult Resolve(Party party, Quest quest, double advisorBonus = 0)
        {
            if (party.Members.Count == 0)
                throw new InvalidOperationException("空のパーティは遠征に出せません。");

            var result = new WeekResolutionResult();

            // ================= フェーズ1：索敵・遭遇判定（仕様書 03 §4.1） =================
            // 「注意深い」特性（ScoutingModifier）は各メンバー自身のDEX寄与率を補正する
            // （例：+0.10で+10%）。v1.4改訂。
            var scoutValues = party.Members.Select(GetScoutingDex).ToList();
            double maxScout = scoutValues.Max();
            double avgScout = scoutValues.Average();
            double leaderLdr = party.Members[0].GetEffectiveStat("LDR"); // MVP: 先頭メンバーを隊長とみなす

            double partyScout = maxScout + avgScout * 0.3 + leaderLdr * 0.2 + advisorBonus;
            double deltaS = partyScout - quest.ScoutRequirement;

            int scoutRoll = _rng.NextInt(1, 100);

            // v1.1で追加：帯が消えたり逆転したりしないよう1〜99にクランプ（仕様書03 §4.1）
            double lowerBound = Clamp(15 + deltaS, 1, 99);
            double upperBound = Clamp(80 + deltaS, 1, 99);

            double combatMultiplier = 1.0;
            if (scoutRoll <= lowerBound)
            {
                result.Encounter = EncounterResult.Surprise;
                combatMultiplier = 1.3; // パーティ戦闘力+30%
            }
            else if (scoutRoll <= upperBound)
            {
                result.Encounter = EncounterResult.Normal;
            }
            else
            {
                result.Encounter = EncounterResult.Ambushed; // 敵戦力+30%は下のEnemyCPに反映
            }

            // ================= フェーズ2：戦闘比率計算（仕様書 03 §4.2） =================
            // 疲労（Fatigue）は廃止済み（→ 03 §3.5改）。HPの影響は PersonalCp の ×(現在HP/最大HP) で保持。
            double partyCp = party.Members.Sum(PersonalCp) * combatMultiplier;

            double enemyCp = quest.Difficulty * EnemyCpCoefficient;
            if (result.Encounter == EncounterResult.Ambushed)
                enemyCp *= 1.3;

            double ratio = partyCp / enemyCp;
            result.Ratio = ratio;

            var (outcome, hpLossMinPct, hpLossMaxPct) = ClassifyOutcome(ratio);
            result.Outcome = outcome;
            result.QuestAchieved = outcome == CombatOutcome.Victory || outcome == CombatOutcome.NarrowWin;
            result.RewardGold = result.QuestAchieved ? quest.RewardGold : 0;

            // 致死判定（フェーズ3）の「神官MND」補正用：パーティに神官がいればその実効MNDを使う。
            // 複数いる場合は先頭の神官を採用する。神官が居ない場合は補正0（→ 03 §4.3）。
            var cleric = party.Members.FirstOrDefault(m => m.JobClass == JobClass.Cleric);
            double clericMnd = cleric?.GetEffectiveStat("MND") ?? 0;

            // ---- HP消費の適用とフェーズ3：負傷・致死判定（仕様書 03 §4.3） ----
            foreach (var member in party.Members)
            {
                int lossPct = _rng.NextInt(hpLossMinPct, hpLossMaxPct);

                // 不意打ち時のみ、配置に応じて被弾ウェイトを掛ける（§4.1↔§4.2の接続）。
                // 奇襲成功・通常交戦では前衛/後衛による差はつけない。
                if (result.Encounter == EncounterResult.Ambushed)
                {
                    double weight = member.Placement == Placement.Back
                        ? PlacementBalance.AmbushBackRowWeight
                        : PlacementBalance.AmbushFrontRowWeight;
                    lossPct = Math.Min(100, (int)Math.Round(lossPct * weight));
                }

                int hpLoss = member.MaxHP * lossPct / 100;
                member.CurrentHP = Math.Max(0, member.CurrentHP - hpLoss);
                result.HpLostByAdventurer[member.Id] = hpLoss;

                if (member.CurrentHP == 0)
                {
                    result.DownedAdventurerIds.Add(member.Id);

                    // 「豪胆」特性（SurvivalThresholdModifier）は生存判定対象者自身の
                    // SurvivalThresholdに固定加算される（v1.4改訂）。
                    double survivalThreshold = Clamp(
                        member.GetEffectiveStat("VIT") + clericMnd * 0.4 + leaderLdr * 0.2 + advisorBonus
                        + member.SumTraitEffect(TraitEffectType.SurvivalThresholdModifier), 5, 90);
                    int deathRoll = _rng.NextInt(1, 100);

                    if (deathRoll <= survivalThreshold)
                    {
                        // 生存：重傷でHP1に留まる
                        ApplySurvival(member);
                    }
                    else if (deathRoll <= survivalThreshold + PermanentBand)
                    {
                        // 不可逆障害の判定域：古傷を付与（重複していれば「生存（重傷）」に丸める）
                        member.TryAddTrait(TraitCatalog.OldWoundId);
                        ApplySurvival(member);
                    }
                    else
                    {
                        // 戦死：HPは0のまま。ロースターからの除外・満足度ペナルティは
                        // QuestDispatchSystem側でFallenAdventurerIdsを見て行う。
                        result.FallenAdventurerIds.Add(member.Id);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 索敵フェーズ（PartyScout）に使う、このメンバー自身のDEX寄与値。「注意深い」特性
        /// （ScoutingModifier、TargetStat="DEX"）を持っていれば寄与率を補正する（→ 03 §4.1）。
        /// </summary>
        private static double GetScoutingDex(Adventurer a) =>
            a.GetEffectiveStat("DEX") * (1 + a.SumTraitEffect(TraitEffectType.ScoutingModifier, "DEX"));

        /// <summary>致死判定で「生存」と判定された場合の共通処理：重傷でHP1に留まる（→ 03 §4.3・§3.6）。</summary>
        private void ApplySurvival(Adventurer member)
        {
            member.Injury = InjurySeverity.Severe;
            member.InjuryWeeksRemaining = _rng.NextInt(3, 8); // → 03 §2.3「重傷: 全治3〜8週」
            member.CurrentHP = 1;
        }

        private static double PersonalCp(Adventurer a)
        {
            double hpRatio = (double)a.CurrentHP / a.MaxHP;
            double baseCp = a.GetEffectiveStat("STR") * WeightSTR + a.GetEffectiveStat("AGI") * WeightAGI
                            + a.GetEffectiveStat("VIT") * WeightVIT + a.GetEffectiveStat("DEX") * WeightDEX
                            + a.GetEffectiveStat("MND") * WeightMND + a.GetEffectiveStat("INT") * WeightINT
                            + a.GetEffectiveStat("LDR") * WeightLDR;
            double placementCorrection = PlacementBalance.GetPersonalCpCorrection(a.JobClass, a.Placement);
            return baseCp * placementCorrection * hpRatio;
        }

        /// <summary>
        /// Ratioから勝敗区分とHP消費%レンジを求める。仕様書03 §4.2の表に対応。
        /// public static にしてあるのはユニットテストから直接呼べるようにするため。
        /// </summary>
        public static (CombatOutcome Outcome, int MinPct, int MaxPct) ClassifyOutcome(double ratio)
        {
            if (ratio >= 1.8) return (CombatOutcome.Victory, 5, 15);
            if (ratio >= 1.0) return (CombatOutcome.NarrowWin, 20, 45);
            if (ratio >= 0.6) return (CombatOutcome.Defeat, 40, 70);
            return (CombatOutcome.Rout, 70, 100);
        }

        private static double Clamp(double value, double min, double max) =>
            Math.Max(min, Math.Min(max, value));
    }
}
