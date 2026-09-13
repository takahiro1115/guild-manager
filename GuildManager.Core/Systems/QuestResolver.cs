using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 週送り時の遠征解決エンジン。仕様書 03 §4（フェーズ1〜3）・§4.2.3 を実装する。
    ///
    /// 項目63改訂：討伐・探索・護衛を「対象ステータス・重みが違う同一の点数計算式」に統一した
    /// （→ 03 §4.2.3・QuestScoringBalance）。フェーズ1（索敵・遭遇判定）とフェーズ2の
    /// 点数／要求値／Ratio算出はすべての種別で共通、Ratio分岐後は種別で完全に分かれる：
    ///  - 討伐（Subjugation）：既存どおり4区分（完全勝利/辛勝/苦戦敗退/戦線崩壊）＋致死判定
    ///    （生存／古傷／戦死）。装備ボーナス・配置補正・索敵の隊長LDR補正も討伐のみ適用する。
    ///  - 探索（Exploration）・護衛（Escort）：3区分（大成功/成功/失敗）＋軽量HP消費。
    ///    致死判定・古傷・戦死は発生しない。LDRは点数式側で直接評価するため、索敵の
    ///    隊長LDR補正は二重評価を避けて適用しない。
    ///
    ///  - フェーズ1：索敵・遭遇判定
    ///  - フェーズ2：点数（討伐ではCP）・要求値・Ratio算出とHP消費
    ///  - フェーズ3：負傷・致死判定（生存／不可逆障害＝古傷／戦死の3分岐。→ 03 §4.3。討伐のみ）。
    ///    古傷は汎用の特性(Trait)エンジン上の「古傷」特性として付与する
    ///    （Adventurer.TryAddTrait経由）。重複禁止のため、既に古傷を持つ冒険者が
    ///    再び不可逆障害の判定域に入った場合は新たな付与をせず「生存（重傷）」に丸める。
    ///    戦死はこのクラスではロースターから除外しない（WeekResolutionResult.
    ///    FallenAdventurerIdsで呼び出し側に通知し、QuestDispatchSystem側で
    ///    GameState.FallenAdventurersへの移動・満足度ペナルティを行う）。
    /// </summary>
    public class QuestResolver
    {
        /// <summary>
        /// 探索・護衛でのHP下限（→ 03 §4.2.3）。致死判定に接続しない低リスク経路のため、
        /// HP0（ダウン）自体を発生させない。訓練場配置の TrainingBalance.MinHp と同じ考え方の
        /// 構造的なクランプのため、CSV化せずコード側に置く（→ 03 §10.1）。
        /// </summary>
        private const int NonCombatMinHp = 1;

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

            // 討伐系（4区分・致死判定あり）か、探索・護衛系（3区分・軽量HP消費）か（→ 03 §4.2.3）。
            bool usesCombatResolution = QuestScoringBalance.UsesCombatResolution(quest.QuestType);

            // ================= フェーズ1：索敵・遭遇判定（仕様書 03 §4.1） =================
            // 「注意深い」特性（ScoutingModifier）は各メンバー自身のDEX寄与率を補正する
            // （例：+0.10で+10%）。v1.4改訂。
            var scoutValues = party.Members.Select(GetScoutingDex).ToList();
            double maxScout = scoutValues.Max();
            double avgScout = scoutValues.Average();
            double leaderLdr = party.Members[0].GetEffectiveStat("LDR"); // MVP: 先頭メンバーを隊長とみなす

            // 隊長LDR補正は討伐のみ（→ 03 §4.2.3）。探索・護衛はLDRを点数計算式側で直接
            // 評価する（重み1.0）ため、索敵側でも加算すると二重評価になる。
            // 索敵フェーズ自体（不意打ち判定・戦闘力補正）はすべての種別で従来どおり実行する。
            double leaderScoutBonus = usesCombatResolution ? leaderLdr * CombatBalance.ScoutLeaderLdrCoefficient : 0;

            double partyScout = maxScout + avgScout * CombatBalance.ScoutAvgCoefficient + leaderScoutBonus + advisorBonus;
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

            // ============ フェーズ2：統一点数計算式（仕様書 03 §4.2・§4.2.3） ============
            // 点数   = Σ(各メンバーの対象ステータス合計 × HP比率) + 装備ボーナス（討伐のみ） × 配置補正（討伐のみ）
            // 要求値 = クエストDifficulty × 種別ごとの要求係数
            // 疲労（Fatigue）は廃止済み（→ 03 §3.5改）。HPの影響は MemberScore の ×(現在HP/最大HP) で保持。
            //
            // 拡張ポイント（→ 後続の指示書②）：ペア特性シナジーはパーティ単位の加算項として
            // ここ（Σの後）に、個人特性ボーナスはメンバー単位の加算項として MemberScore 内に
            // それぞれ差し込める形にしてある。
            double score = party.Members.Sum(m => MemberScore(m, quest.QuestType)) * combatMultiplier;

            double requirement = quest.Difficulty * QuestScoringBalance.GetRequirementCoefficient(quest.QuestType);
            if (result.Encounter == EncounterResult.Ambushed)
                requirement *= CombatBalance.AmbushEnemyMultiplier;

            double ratio = score / requirement;
            result.Ratio = ratio;

            // Ratio分岐後の扱いは種別で完全に分ける（→ 03 §4.2.3）。
            int hpLossMinPct;
            int hpLossMaxPct;
            if (usesCombatResolution)
            {
                var (outcome, minPct, maxPct) = ClassifyOutcome(ratio);
                result.Outcome = outcome;
                result.QuestAchieved = outcome == CombatOutcome.Victory || outcome == CombatOutcome.NarrowWin;
                (hpLossMinPct, hpLossMaxPct) = (minPct, maxPct);
            }
            else
            {
                var (outcome, minPct, maxPct) = ClassifyNonCombatOutcome(ratio);
                result.NonCombatOutcome = outcome;
                result.QuestAchieved = outcome != Systems.NonCombatOutcome.Failure; // 大成功・成功＝達成
                (hpLossMinPct, hpLossMaxPct) = (minPct, maxPct);
            }
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
                // HPの下限：討伐は0（＝ダウン→致死判定へ）、探索・護衛は1（致死判定に一切
                // 接続しない低リスク経路のため、HP0によるダウン自体を発生させない。
                // 訓練場配置のHP微減が TrainingBalance.MinHp=1 で下げ止まるのと同じ考え方。
                // → 03 §4.2.3「致死判定・古傷・戦死は発生しない」）。この下限自体は構造的な
                // クランプのためCSV化していない（→ 03 §10.1）。
                int minHp = usesCombatResolution ? 0 : NonCombatMinHp;
                member.CurrentHP = Math.Max(minHp, member.CurrentHP - hpLoss);
                result.HpLostByAdventurer[member.Id] = hpLoss;

                // フェーズ3（致死判定）は討伐のみ。探索・護衛では上のminHp=1により
                // そもそもHP0に到達しないが、意図を明示するため条件にも書いておく。
                if (usesCombatResolution && member.CurrentHP == 0)
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

        /// <summary>致死判定で「生存」と判定された場合の共通処理：重傷でHP1に留まる（→ 03 §4.3・§3.6）。</summary>
        private void ApplySurvival(Adventurer member)
        {
            member.Injury = InjurySeverity.Severe;
            member.InjuryWeeksRemaining = _rng.NextInt(CombatBalance.SevereInjuryWeeksMin, CombatBalance.SevereInjuryWeeksMax); // → 03 §2.3「重傷: 全治3〜8週」
            member.CurrentHP = 1;
        }

        /// <summary>
        /// 統一点数計算式における、メンバー1名分の点数（→ 03 §4.2.3）。
        /// 討伐ではこれが従来の「個人CP」と完全に同じ式になる（対象ステータスの重みは
        /// quest_type_weights.csvのSubjugation行が旧CombatBalance.WeightXXXの値をそのまま持つ）。
        ///
        /// 拡張ポイント（→ 後続の指示書②）：個人特性ボーナスはここに加算項として差し込む。
        /// </summary>
        private static double MemberScore(Adventurer a, QuestType questType)
        {
            double hpRatio = (double)a.CurrentHP / a.MaxHP;

            double statSum = 0;
            foreach (var (stat, weight) in QuestScoringBalance.GetStatWeights(questType))
                statSum += a.GetEffectiveStat(stat) * weight;

            double baseScore = statSum + GetEquipmentBonus(a, questType);
            return baseScore * GetPlacementCorrection(a, questType) * hpRatio;
        }

        /// <summary>
        /// 装備ボーナス。討伐では装備（武器・アクセサリー）のCP固定加算（→ 03 §4.2.2）を
        /// ステータス由来の寄与と同じ扱いで加算し、負傷時の効率低下(hpRatio)・配置補正の対象にする。
        ///
        /// 探索・護衛は「予約枠」として常に0を返す（→ 03 §4.2.3）。探索・護衛で効く装備効果
        /// （調査道具・護衛用装備等）は今回実装しないが、将来追加する際はこのメソッドが
        /// 拡張点になる。
        /// </summary>
        private static double GetEquipmentBonus(Adventurer a, QuestType questType) =>
            QuestScoringBalance.UsesCombatResolution(questType)
                ? a.GetEquipmentBonus(EquipmentEffectType.PersonalCpBonus)
                : 0;

        /// <summary>
        /// 配置補正（前衛/後衛）。討伐のみ適用し、探索・護衛には適用しない（→ 03 §4.2.3）。
        /// </summary>
        private static double GetPlacementCorrection(Adventurer a, QuestType questType) =>
            QuestScoringBalance.UsesCombatResolution(questType)
                ? PlacementBalance.GetPersonalCpCorrection(a.JobClass, a.Placement)
                : 1.0;

        /// <summary>
        /// Ratioから勝敗区分とHP消費%レンジを求める（討伐のみ）。仕様書03 §4.2の表に対応。
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

        /// <summary>
        /// Ratioから探索・護衛の判定区分と軽量HP消費%レンジを求める（→ 03 §4.2.3）。
        /// 討伐の4区分（ClassifyOutcome）とは別概念：致死判定には一切接続しない。
        /// public static にしてあるのはユニットテストから直接呼べるようにするため。
        /// </summary>
        public static (NonCombatOutcome Outcome, int MinPct, int MaxPct) ClassifyNonCombatOutcome(double ratio)
        {
            if (ratio >= QuestScoringBalance.RatioThresholdGreatSuccess)
                return (NonCombatOutcome.GreatSuccess, QuestScoringBalance.HpLossPctGreatSuccessMin, QuestScoringBalance.HpLossPctGreatSuccessMax);
            if (ratio >= QuestScoringBalance.RatioThresholdSuccess)
                return (NonCombatOutcome.Success, QuestScoringBalance.HpLossPctSuccessMin, QuestScoringBalance.HpLossPctSuccessMax);
            return (NonCombatOutcome.Failure, QuestScoringBalance.HpLossPctFailureMin, QuestScoringBalance.HpLossPctFailureMax);
        }

        private static double Clamp(double value, double min, double max) =>
            Math.Max(min, Math.Min(max, value));
    }
}
