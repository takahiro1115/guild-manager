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

            double partyScout = maxScout + avgScout * CombatBalance.ScoutAvgCoefficient + leaderLdr * CombatBalance.ScoutLeaderLdrCoefficient + advisorBonus;
            double deltaS = partyScout - quest.ScoutRequirement;

            int scoutRoll = _rng.NextInt(1, 100);

            // v1.1で追加：帯が消えたり逆転したりしないよう1〜99にクランプ（仕様書03 §4.1。
            // このクランプ幅自体は帯の消失・逆転を防ぐための構造的な安全装置のためCSV化していない）。
            double lowerBound = Clamp(CombatBalance.SurpriseLowerBoundBase + deltaS, 1, 99);
            double upperBound = Clamp(CombatBalance.AmbushUpperBoundBase + deltaS, 1, 99);

            double combatMultiplier = 1.0;
            if (scoutRoll <= lowerBound)
            {
                result.Encounter = EncounterResult.Surprise;
                combatMultiplier = CombatBalance.SurpriseCombatMultiplier; // パーティ戦闘力+30%
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
            // クエスト適性ボーナス（→ 03 §4.2.1、v1.7改訂）：基礎PartyCP算出後、クエスト種別に
            // 応じた適性倍率（1.0以上、ボーナスのみ）を追加で乗算する。
            double partyCp = party.Members.Sum(PersonalCp) * combatMultiplier * GetAptitudeMultiplier(party, quest.QuestType);

            double enemyCp = quest.Difficulty * CombatBalance.EnemyCpCoefficient;
            if (result.Encounter == EncounterResult.Ambushed)
                enemyCp *= CombatBalance.AmbushEnemyMultiplier;

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
                        member.GetEffectiveStat("VIT") + clericMnd * CombatBalance.SurvivalClericMndCoefficient
                        + leaderLdr * CombatBalance.SurvivalLeaderLdrCoefficient + advisorBonus
                        + member.SumTraitEffect(TraitEffectType.SurvivalThresholdModifier),
                        CombatBalance.SurvivalThresholdMin, CombatBalance.SurvivalThresholdMax);
                    int deathRoll = _rng.NextInt(1, 100);

                    if (deathRoll <= survivalThreshold)
                    {
                        // 生存：重傷でHP1に留まる
                        ApplySurvival(member);
                    }
                    else if (deathRoll <= survivalThreshold + CombatBalance.PermanentBand)
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

        /// <summary>
        /// クエスト適性ボーナス（→ 03 §4.2.1）：クエスト種別が定める対象ステータス群の
        /// パーティ平均実効値から、1.0〜1.3倍（ペナルティなし）の適性倍率を算出する。
        /// 討伐は7能力全体が対象＝既存の基礎CPとほぼ同じ考え方になるため、実質ほぼ標準
        /// （倍率が1.0に近い）クエストという位置づけになる（仕様どおり）。
        /// </summary>
        private static double GetAptitudeMultiplier(Party party, QuestType questType)
        {
            var stats = QuestAptitudeBalance.GetAptitudeStats(questType);
            double average = party.Members
                .SelectMany(_ => stats, (member, stat) => member.GetEffectiveStat(stat))
                .Average();
            return QuestAptitudeBalance.GetMultiplier(average);
        }

        /// <summary>致死判定で「生存」と判定された場合の共通処理：重傷でHP1に留まる（→ 03 §4.3・§3.6）。</summary>
        private void ApplySurvival(Adventurer member)
        {
            member.Injury = InjurySeverity.Severe;
            member.InjuryWeeksRemaining = _rng.NextInt(CombatBalance.SevereInjuryWeeksMin, CombatBalance.SevereInjuryWeeksMax); // → 03 §2.3「重傷: 全治3〜8週」
            member.CurrentHP = 1;
        }

        private static double PersonalCp(Adventurer a)
        {
            double hpRatio = (double)a.CurrentHP / a.MaxHP;
            double baseCp = a.GetEffectiveStat("STR") * CombatBalance.WeightSTR + a.GetEffectiveStat("AGI") * CombatBalance.WeightAGI
                            + a.GetEffectiveStat("VIT") * CombatBalance.WeightVIT + a.GetEffectiveStat("DEX") * CombatBalance.WeightDEX
                            + a.GetEffectiveStat("MND") * CombatBalance.WeightMND + a.GetEffectiveStat("INT") * CombatBalance.WeightINT
                            + a.GetEffectiveStat("LDR") * CombatBalance.WeightLDR
                            // 装備（武器・アクセサリー）のCP固定加算（→ 03 §4.2.2）。ステータス由来の
                            // 寄与と同じ扱いとし、負傷時の効率低下(hpRatio)・配置補正の対象にする。
                            + a.GetEquipmentBonus(EquipmentEffectType.PersonalCpBonus);
            double placementCorrection = PlacementBalance.GetPersonalCpCorrection(a.JobClass, a.Placement);
            return baseCp * placementCorrection * hpRatio;
        }

        /// <summary>
        /// Ratioから勝敗区分とHP消費%レンジを求める。仕様書03 §4.2の表に対応。
        /// public static にしてあるのはユニットテストから直接呼べるようにするため。
        /// </summary>
        public static (CombatOutcome Outcome, int MinPct, int MaxPct) ClassifyOutcome(double ratio)
        {
            if (ratio >= CombatBalance.RatioThresholdVictory)
                return (CombatOutcome.Victory, CombatBalance.HpLossPctVictoryMin, CombatBalance.HpLossPctVictoryMax);
            if (ratio >= CombatBalance.RatioThresholdNarrowWin)
                return (CombatOutcome.NarrowWin, CombatBalance.HpLossPctNarrowWinMin, CombatBalance.HpLossPctNarrowWinMax);
            if (ratio >= CombatBalance.RatioThresholdDefeat)
                return (CombatOutcome.Defeat, CombatBalance.HpLossPctDefeatMin, CombatBalance.HpLossPctDefeatMax);
            return (CombatOutcome.Rout, CombatBalance.HpLossPctRoutMin, CombatBalance.HpLossPctRoutMax);
        }

        private static double Clamp(double value, double min, double max) =>
            Math.Max(min, Math.Min(max, value));
    }
}
