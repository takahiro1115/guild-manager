using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 週送り時の遠征解決エンジン。仕様書 03 §4（フェーズ1・フェーズ2）を実装する。
    ///
    /// MVPの範囲（→ docs/06_タスクリスト.md Phase 1）：
    ///  - フェーズ1：索敵・遭遇判定
    ///  - フェーズ2：戦闘比率とHP消費
    ///  - 致死判定は簡易版（HP0になったら重傷扱いにするだけ）。
    ///    詳細な致死判定（不可逆障害・戦死、仕様書03 §4.3）はPhase 3で追加する。
    /// </summary>
    public class QuestResolver
    {
        private readonly IRng _rng;

        // ---- 個人CP重み係数。→ BAL: 戦闘/CP重み（現状は仮値の直書き。後で外部化する） ----
        private const double WeightSTR = 0.8;
        private const double WeightAGI = 0.5;
        private const double WeightEND = 0.6;
        private const double WeightMAG = 0.8;
        private const double WeightLDR = 0.4;
        private const double EnemyCpCoefficient = 3.0; // → BAL: 戦闘/敵CP係数

        public QuestResolver(IRng rng)
        {
            _rng = rng;
        }

        /// <summary>
        /// パーティをクエストに派遣し、フェーズ1・2を解決する。
        /// </summary>
        public WeekResolutionResult Resolve(Party party, Quest quest)
        {
            if (party.Members.Count == 0)
                throw new InvalidOperationException("空のパーティは遠征に出せません。");

            var result = new WeekResolutionResult();

            // ================= フェーズ1：索敵・遭遇判定（仕様書 03 §4.1） =================
            var scoutValues = party.Members.Select(a => a.SCT).ToList();
            int maxScout = scoutValues.Max();
            double avgScout = scoutValues.Average();
            int leaderLdr = party.Members[0].LDR; // MVP: 先頭メンバーを隊長とみなす

            double partyScout = maxScout + avgScout * 0.3 + leaderLdr * 0.2;
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

            // ---- HP消費の適用（簡易版：詳細な致死判定はPhase 3で追加） ----
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
                    // MVP簡易版：戦死・不可逆障害は判定せず、重傷でHP1に留まる扱いにする
                    member.Injury = InjurySeverity.Severe;
                    member.InjuryWeeksRemaining = _rng.NextInt(3, 8); // → 03 §2.3「重傷: 全治3〜8週」
                    member.CurrentHP = 1;
                    result.DownedAdventurerIds.Add(member.Id); // → 03 §4.3の致死判定接続時に使う想定（現状は未参照）
                }
            }

            return result;
        }

        private static double PersonalCp(Adventurer a)
        {
            double hpRatio = (double)a.CurrentHP / a.MaxHP;
            double baseCp = a.STR * WeightSTR + a.AGI * WeightAGI + a.END * WeightEND
                            + a.MAG * WeightMAG + a.LDR * WeightLDR;
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
