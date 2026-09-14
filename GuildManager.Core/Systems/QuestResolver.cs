using System;
using System.Collections.Generic;
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

            // 環境ギミック（→ 環境ギミック刷新仕様）：暗黒（Darkness）はフェーズ1の索敵値に
            // ペナルティ係数を乗算する。対策達成度（→ GimmickEvaluator）に応じて
            // 未充足=そのまま、一部充足=半減、完全充足=無効（×1.0）。
            partyScout *= GimmickEvaluator.GetPhaseMultiplier(quest.EnvironmentTags, GimmickPhase.Scouting, party.Members, party.ConsumableItemIds);

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
            // 個人特性ボーナス（例：田舎育ち×探索）はメンバー単位の加算項として MemberScore 内で、
            // ペア特性シナジー（例：豪胆×注意深い）はパーティ単位の加算項としてここで加算する
            // （→ 03 §4.2.3、項目64）。ペアシナジーは遭遇による戦闘力倍率の対象外とし、
            // パーティ全体スコアへの定額加算として扱う（マイナスのシナジーが奇襲成功で
            // かえって重くなる、といった不自然さを避けるため）。
            double score = QuestScoreCalculator.SumMemberScores(party.Members, quest.QuestType) * combatMultiplier
                           + PairSynergyCalculator.Calculate(party.Members, quest.QuestType);

            // 環境ギミック：霊体（Undead）・隘路（NarrowPath）はフェーズ2の点数にペナルティ係数を
            // 乗算する（→ GimmickPhase.Score）。ペアシナジー加算後の合計点数に対して適用する
            // （個別メンバーの寄与ではなく「その場の状況」への補正のため）。
            score *= GimmickEvaluator.GetPhaseMultiplier(quest.EnvironmentTags, GimmickPhase.Score, party.Members, party.ConsumableItemIds);

            double requirement = QuestScoreCalculator.Requirement(quest);
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
            // 採取任務の報酬は派遣人数に正比例する（→ コアシステム刷新仕様「採取量の変動」）。
            // 「4人で行けば4倍採れるが、その週は他の任務に人を回せない」という
            // 編成上のトレードオフを作るための仕組み（→ QuestScoringBalance.
            // ScalesRewardWithMemberCount）。討伐・探索等は人数を増やしても報酬は増えない。
            result.RewardGold = result.QuestAchieved
                ? GetScaledReward(quest, party.Members.Count)
                : 0;

            // 携帯糧食（→ パーティ携行アイテム刷新仕様）：達成時のみパーティ全員の満足度に加算する。
            if (result.QuestAchieved && party.ConsumableItemIds.Contains(ConsumableCatalog.TravelRationsId))
            {
                foreach (var member in party.Members)
                    member.Satisfaction = (int)Math.Clamp(member.Satisfaction + ConsumableBalance.TravelRationsSatisfactionBonus, 0, 100);
            }

            // 環境ギミック：瘴気（Miasma）・巨躯（Colossal）は損耗（HP消費%）にペナルティ係数を
            // 乗算する（→ GimmickPhase.Attrition）。煙幕弾・高品質傷薬（→ パーティ携行アイテム
            // 刷新仕様）と合わせて、HP消費を適用する箇所（ApplyHpLossAndDeathJudgment・
            // ApplyAdditionalHpLoss）へまとめて渡す。
            double attritionMultiplier = GimmickEvaluator.GetPhaseMultiplier(quest.EnvironmentTags, GimmickPhase.Attrition, party.Members, party.ConsumableItemIds);

            // 致死判定（フェーズ3）の「神官MND」補正用：パーティに神官がいればその実効MNDを使う。
            // 複数いる場合は先頭の神官を採用する。神官が居ない場合は補正0（→ 03 §4.3）。
            var cleric = party.Members.FirstOrDefault(m => m.JobClass == JobClass.Cleric);
            double clericMnd = cleric?.GetEffectiveStat("MND") ?? 0;

            // ============ ランダムイベントの判定（→ 03 §4.2.3、項目65） ============
            // 探索・護衛のみ。発生ロールは3イベント独立で、同じクエストで複数発生しうる。
            // 判定だけをここで済ませ、HP消費への反映は下のHP適用フェーズでまとめて行う
            // （イベント①が発生した回は、通常の軽量HP消費を適用しない＝二重消費の防止）。
            if (!usesCombatResolution)
                ResolveRandomEvents(result, party, quest);

            // ---- HP消費の適用とフェーズ3：負傷・致死判定（仕様書 03 §4.3・§4.2.3） ----
            var strongEnemy = result.Events.StrongEnemy;
            if (strongEnemy != null && strongEnemy.UsedCombatDamage)
            {
                // 強敵と実際に撃ち合った回：その場の物理的な結果として、討伐フローの
                // HP消費レンジ・致死判定をそのまま流用する（通常の軽量HP消費は行わない）。
                var (combatMinPct, combatMaxPct) = strongEnemy.Outcome == StrongEnemyOutcome.PushedThrough
                    ? (CombatBalance.HpLossPctNarrowWinMin, CombatBalance.HpLossPctNarrowWinMax)   // 押し切り＝辛勝相当
                    : (CombatBalance.HpLossPctDefeatMin, CombatBalance.HpLossPctDefeatMax);        // 苦戦＝苦戦敗退相当
                ApplyHpLossAndDeathJudgment(result, party, combatMinPct, combatMaxPct,
                    useCombatDamageRules: true, clericMnd, leaderLdr, advisorBonus, attritionMultiplier);
            }
            else
            {
                ApplyHpLossAndDeathJudgment(result, party, hpLossMinPct, hpLossMaxPct,
                    useCombatDamageRules: usesCombatResolution, clericMnd, leaderLdr, advisorBonus, attritionMultiplier);
            }

            // イベントに伴う追加HP消費（罠・深追いの代償）。致死判定には接続しない。
            ApplyEventExtraHpLoss(result, party, attritionMultiplier);

            // 携行アイテム（消耗品）は出撃解決時に一括消費される（→ パーティ携行アイテム刷新仕様）。
            party.ConsumableItemIds.Clear();

            return result;
        }

        /// <summary>
        /// HP消費の適用とフェーズ3（負傷・致死判定）。仕様書 03 §4.3・§4.2.3。
        ///
        /// useCombatDamageRules=true のとき、HP下限0（＝HP0でダウン→致死判定）となり、
        /// 討伐フローと同じ生存／古傷／戦死の3分岐を行う。falseのときはHP下限1で、
        /// 致死判定を一切行わない（探索・護衛の軽量HP消費）。
        ///
        /// 項目65：「強敵との遭遇」イベントで討伐フローの損害を流用する必要が生じたため、
        /// Resolve本体にあった処理をこのメソッドへ切り出して再利用できるようにした
        /// （討伐の処理内容自体は移動前と同一）。
        /// </summary>
        private void ApplyHpLossAndDeathJudgment(
            WeekResolutionResult result, Party party, int hpLossMinPct, int hpLossMaxPct,
            bool useCombatDamageRules, double clericMnd, double leaderLdr, double advisorBonus,
            double attritionMultiplier)
        {
            foreach (var member in party.Members)
            {
                int lossPct = _rng.NextInt(hpLossMinPct, hpLossMaxPct);
                lossPct = AdjustLossPct(lossPct, attritionMultiplier, party.ConsumableItemIds);

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
                // HPの下限：討伐（および強敵との遭遇で撃ち合った回）は0（＝ダウン→致死判定へ）、
                // 通常の探索・護衛は1（致死判定に一切接続しない低リスク経路のため、HP0による
                // ダウン自体を発生させない。訓練場配置のHP微減が TrainingBalance.MinHp=1 で
                // 下げ止まるのと同じ考え方。→ 03 §4.2.3）。この下限自体は構造的なクランプの
                // ためCSV化していない（→ 03 §10.1）。
                int minHp = useCombatDamageRules ? 0 : NonCombatMinHp;
                member.CurrentHP = Math.Max(minHp, member.CurrentHP - hpLoss);
                result.HpLostByAdventurer[member.Id] = hpLoss;

                // 低危険度任務（採取・巡回・探索・護衛）の消耗判定（→ コアシステム刷新仕様
                // 「負傷判定（即詰み防止）」）。致死判定は一切行わず、HPを大きく削られた者だけが
                // 数週間の軽傷を負う（休養すれば必ず復帰する＝人員を永久に失って詰むことがない）。
                if (!useCombatDamageRules)
                    ApplyLightInjuryIfExhausted(result, member);

                // フェーズ3（致死判定）は討伐フローの損害を適用した時のみ。通常の探索・護衛では
                // 上のminHp=1によりそもそもHP0に到達しないが、意図を明示するため条件にも書いておく。
                if (useCombatDamageRules && member.CurrentHP == 0)
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
        }

        // ================= ランダムイベント（→ 03 §4.2.3、項目65） =================

        /// <summary>
        /// 探索・護衛のランダムイベント3種の発生ロールと判定を行い、結果を result.Events に記録する。
        /// HP消費への反映（イベント①の討伐フロー流用・②③の追加消費）は呼び出し側で行う。
        /// 発生ロールは「①強敵との遭遇 → ②宝物庫の発見 → ③深追い」の順に独立して行う。
        /// </summary>
        private void ResolveRandomEvents(WeekResolutionResult result, Party party, Quest quest)
        {
            if (Occurs(QuestEventBalance.StrongEnemyOccurrenceChancePercent))
                result.Events.StrongEnemy = ResolveStrongEnemyEvent(result, party, quest);

            // 宝物庫の発見は探索のみ（護衛には隊商の道中しかなく、探索対象の遺跡等が無いため）。
            if (quest.QuestType == QuestType.Exploration && Occurs(QuestEventBalance.TreasureVaultOccurrenceChancePercent))
                result.Events.TreasureVault = ResolveTreasureVaultEvent(result, party, quest);

            if (Occurs(QuestEventBalance.PushingOnOccurrenceChancePercent))
                result.Events.PushingOn = ResolvePushingOnEvent(result, party, quest);
        }

        /// <summary>イベントの発生判定。1〜100のロールが確率以下なら発生。</summary>
        private bool Occurs(int chancePercent) => _rng.NextInt(1, 100) <= chancePercent;

        /// <summary>
        /// ①強敵との遭遇。押し切り／退避／苦戦の3値判定（→ 03 §4.2.3）。
        /// 押し切り・苦戦では討伐フローのHP消費・致死判定を流用する（適用は呼び出し側）。
        /// 探索・護衛としての成否（大成功／成功／失敗）はこのイベントでは変更しない。
        /// </summary>
        private static StrongEnemyEventResult ResolveStrongEnemyEvent(WeekResolutionResult result, Party party, Quest quest)
        {
            double leaderLdr = party.Members[0].GetEffectiveStat("LDR");

            double pushThrough = party.Members.Sum(m => m.GetEffectiveStat("STR") + m.GetEffectiveStat("VIT"))
                                 + leaderLdr * QuestEventBalance.StrongEnemyLeaderLdrCoefficient
                                 + (PartyHasTrait(party, TraitCatalog.BraveId) ? QuestEventBalance.StrongEnemyBraveBonus : 0);

            double evade = party.Members.Sum(m => m.GetEffectiveStat("AGI") + m.GetEffectiveStat("INT"))
                           + (PartyHasTrait(party, TraitCatalog.AttentiveId) ? QuestEventBalance.StrongEnemyAttentiveBonus : 0)
                           - (PartyHasTrait(party, TraitCatalog.TraumaId) ? QuestEventBalance.StrongEnemyTraumaPenalty : 0)
                           - (PartyHasTrait(party, TraitCatalog.OldWoundId) ? QuestEventBalance.StrongEnemyOldWoundPenalty : 0);

            double requirement = quest.Difficulty * QuestEventBalance.StrongEnemyRequirementCoefficient;

            StrongEnemyOutcome outcome;
            if (pushThrough >= requirement) outcome = StrongEnemyOutcome.PushedThrough;
            else if (evade >= requirement) outcome = StrongEnemyOutcome.Evaded;
            else outcome = StrongEnemyOutcome.Struggled;

            int bonusGold = outcome == StrongEnemyOutcome.PushedThrough ? QuestEventBalance.StrongEnemyPushThroughRewardGold : 0;
            // イベント由来の追加報酬は、クエスト自体の成否に関わらず加算する
            // （倒した強敵からの戦利品・宝物庫の中身は、任務の達成/未達成とは別の獲得物のため）。
            result.RewardGold += bonusGold;

            return new StrongEnemyEventResult
            {
                Outcome = outcome,
                PushThroughScore = pushThrough,
                EvadeScore = evade,
                Requirement = requirement,
                BonusRewardGold = bonusGold,
                UsedCombatDamage = outcome != StrongEnemyOutcome.Evaded,
            };
        }

        /// <summary>②宝物庫の発見（探索のみ）。DEX+INT中心の判定で追加報酬の大小・罠を決める。</summary>
        private static TreasureVaultEventResult ResolveTreasureVaultEvent(WeekResolutionResult result, Party party, Quest quest)
        {
            double score = party.Members.Sum(m => m.GetEffectiveStat("DEX") + m.GetEffectiveStat("INT"))
                           + (PartyHasTrait(party, TraitCatalog.AttentiveId) ? QuestEventBalance.TreasureVaultAttentiveBonus : 0);
            double requirement = quest.Difficulty * QuestEventBalance.TreasureVaultRequirementCoefficient;
            double ratio = score / requirement;

            var outcome = ClassifyEventOutcome(ratio,
                QuestEventBalance.TreasureVaultRatioThresholdGreatSuccess,
                QuestEventBalance.TreasureVaultRatioThresholdSuccess);

            int bonusGold = outcome switch
            {
                QuestEventOutcome.GreatSuccess => QuestEventBalance.TreasureVaultGreatSuccessRewardGold,
                QuestEventOutcome.Success => QuestEventBalance.TreasureVaultSuccessRewardGold,
                _ => 0,
            };
            result.RewardGold += bonusGold;

            return new TreasureVaultEventResult
            {
                Outcome = outcome,
                Score = score,
                Requirement = requirement,
                Ratio = ratio,
                BonusRewardGold = bonusGold,
                // 罠の抽選（失敗時のみ）は、HP消費の適用と同じタイミングで行う
                // （→ ApplyEventExtraHpLoss。判定と乱数消費の順序を1箇所にまとめるため）。
            };
        }

        /// <summary>
        /// ③深追い（探索・護衛）。VIT+MND中心の判定で、追加報酬とHP消費のトレードオフを決める。
        /// クエストの拘束期間は変更しない（→ 03 §4.0.1「拘束期間の途中で呼び戻せない」方針と
        /// 矛盾させないため、報酬とHP消費だけで表現する）。
        /// </summary>
        private static PushingOnEventResult ResolvePushingOnEvent(WeekResolutionResult result, Party party, Quest quest)
        {
            double score = party.Members.Sum(m => m.GetEffectiveStat("VIT") + m.GetEffectiveStat("MND"));
            double requirement = quest.Difficulty * QuestEventBalance.PushingOnRequirementCoefficient;
            double ratio = score / requirement;

            var outcome = ClassifyEventOutcome(ratio,
                QuestEventBalance.PushingOnRatioThresholdGreatSuccess,
                QuestEventBalance.PushingOnRatioThresholdSuccess);

            int bonusGold = outcome switch
            {
                QuestEventOutcome.GreatSuccess => QuestEventBalance.PushingOnGreatSuccessRewardGold,
                QuestEventOutcome.Success => QuestEventBalance.PushingOnSuccessRewardGold,
                _ => 0,
            };
            result.RewardGold += bonusGold;

            return new PushingOnEventResult
            {
                Outcome = outcome,
                Score = score,
                Requirement = requirement,
                Ratio = ratio,
                BonusRewardGold = bonusGold,
                // 追加HP消費（大成功＝粘った代償／失敗＝それなりの消費）は ApplyEventExtraHpLoss で適用する。
            };
        }

        /// <summary>パーティ内に指定した特性の保有者が1人でもいるか（人数分の重ね掛けはしない）。</summary>
        private static bool PartyHasTrait(Party party, string traitId) => party.Members.Any(m => m.HasTrait(traitId));

        /// <summary>イベント専用の3段階判定（大成功／成功／失敗）。閾値はイベントごとに異なる。</summary>
        private static QuestEventOutcome ClassifyEventOutcome(double ratio, double greatSuccessThreshold, double successThreshold)
        {
            if (ratio >= greatSuccessThreshold) return QuestEventOutcome.GreatSuccess;
            if (ratio >= successThreshold) return QuestEventOutcome.Success;
            return QuestEventOutcome.Failure;
        }

        /// <summary>
        /// イベントに伴う追加HP消費（②宝物庫の罠・③深追いの代償）を適用する。
        /// 致死判定には一切接続せず、HPは下限1で止まる。戦死者（イベント①経由で発生しうる）は対象外。
        /// </summary>
        private void ApplyEventExtraHpLoss(WeekResolutionResult result, Party party, double attritionMultiplier)
        {
            var treasureVault = result.Events.TreasureVault;
            if (treasureVault is { Outcome: QuestEventOutcome.Failure })
            {
                treasureVault.TrapTriggered = Occurs(QuestEventBalance.TreasureVaultTrapChancePercent);
                if (treasureVault.TrapTriggered)
                    ApplyAdditionalHpLoss(result, party,
                        QuestEventBalance.TreasureVaultTrapHpLossPctMin, QuestEventBalance.TreasureVaultTrapHpLossPctMax, attritionMultiplier);
            }

            var pushingOn = result.Events.PushingOn;
            if (pushingOn == null)
                return;

            switch (pushingOn.Outcome)
            {
                case QuestEventOutcome.GreatSuccess: // 粘った代償としてわずかに消費
                    pushingOn.AppliedExtraHpLoss = true;
                    ApplyAdditionalHpLoss(result, party,
                        QuestEventBalance.PushingOnGreatSuccessHpLossPctMin, QuestEventBalance.PushingOnGreatSuccessHpLossPctMax, attritionMultiplier);
                    break;
                case QuestEventOutcome.Failure: // 粘ったが得るものが無かった
                    pushingOn.AppliedExtraHpLoss = true;
                    ApplyAdditionalHpLoss(result, party,
                        QuestEventBalance.PushingOnFailureHpLossPctMin, QuestEventBalance.PushingOnFailureHpLossPctMax, attritionMultiplier);
                    break;
                // 成功：追加HP消費なし
            }
        }

        /// <summary>
        /// イベント由来の追加HP消費。通常のHP消費（ApplyHpLossAndDeathJudgment）とは別に、
        /// その上から重ねて適用する。HP下限は1で、致死判定は行わない。
        /// HpLostByAdventurerには「実際に減った分」を加算する（下限クランプで実際には
        /// 減っていない分まで計上しないため）。
        /// </summary>
        private void ApplyAdditionalHpLoss(WeekResolutionResult result, Party party, int minPct, int maxPct, double attritionMultiplier)
        {
            foreach (var member in party.Members)
            {
                if (result.FallenAdventurerIds.Contains(member.Id))
                    continue; // 戦死者にはこれ以上の消費を適用しない（HP0のまま据え置く）

                int lossPct = _rng.NextInt(minPct, maxPct);
                lossPct = AdjustLossPct(lossPct, attritionMultiplier, party.ConsumableItemIds);
                int hpLoss = member.MaxHP * lossPct / 100;
                int newHp = Math.Max(NonCombatMinHp, member.CurrentHP - hpLoss);

                int actualLoss = member.CurrentHP - newHp;
                member.CurrentHP = newHp;

                result.HpLostByAdventurer.TryGetValue(member.Id, out int already);
                result.HpLostByAdventurer[member.Id] = already + actualLoss;
            }
        }

        /// <summary>
        /// 索敵フェーズ（PartyScout）に使う、このメンバー自身のDEX寄与値。「注意深い」特性
        /// （ScoutingModifier、TargetStat="DEX"）を持っていれば寄与率を補正する（→ 03 §4.1）。
        /// </summary>
        private static double GetScoutingDex(Adventurer a) =>
            a.GetEffectiveStat("DEX") * (1 + a.SumTraitEffect(TraitEffectType.ScoutingModifier, "DEX"));

        /// <summary>
        /// クエスト報酬額。採取任務のみ派遣人数に正比例させる（→ QuestScoringBalance.
        /// ScalesRewardWithMemberCount、コアシステム刷新仕様「採取量の変動」）。
        /// 素材システム自体は未実装（→ docs/04_バランス表/README「市場・素材価格」）のため、
        /// 現時点では採取量をGoldで表現している。素材を導入する際は、この
        /// 「基本量×派遣人数」という比例則をそのまま素材個数へ移植する。
        /// </summary>
        private static int GetScaledReward(Quest quest, int memberCount) =>
            QuestScoringBalance.ScalesRewardWithMemberCount(quest.QuestType)
                ? quest.RewardGold * memberCount
                : quest.RewardGold;

        /// <summary>
        /// 低危険度任務での軽傷付与（→ コアシステム刷新仕様「負傷判定（即詰み防止）」）。
        /// 残HP比率が閾値以下まで削られたメンバーに、数週間の軽傷を負わせる。
        /// 既に負傷中の者は対象外（重傷を軽傷で上書きして全治期間を短縮してしまわないため）。
        /// 回復は既存の InjuryRecoverySystem が担当する（Light/Severeを区別せず週数を消化する）。
        /// </summary>
        private void ApplyLightInjuryIfExhausted(WeekResolutionResult result, Adventurer member)
        {
            if (member.Injury != InjurySeverity.None)
                return;

            double hpRatio = (double)member.CurrentHP / member.MaxHP;
            if (hpRatio > QuestScoringBalance.LightInjuryHpRatioThreshold)
                return;

            member.Injury = InjurySeverity.Light;
            member.InjuryWeeksRemaining = _rng.NextInt(QuestScoringBalance.LightInjuryWeeksMin, QuestScoringBalance.LightInjuryWeeksMax);
            result.LightlyInjuredAdventurerIds.Add(member.Id);
        }

        /// <summary>致死判定で「生存」と判定された場合の共通処理：重傷でHP1に留まる（→ 03 §4.3・§3.6）。</summary>
        private void ApplySurvival(Adventurer member)
        {
            member.Injury = InjurySeverity.Severe;
            member.InjuryWeeksRemaining = _rng.NextInt(CombatBalance.SevereInjuryWeeksMin, CombatBalance.SevereInjuryWeeksMax); // → 03 §2.3「重傷: 全治3〜8週」
            member.CurrentHP = 1;
        }

        // 点数・要求値の算出（MemberScore／装備ボーナス／個人特性ボーナス／配置補正／要求値）は
        // QuestScoreCalculator へ切り出した（→ コアシステム刷新仕様「成功率予測エンジン」）。
        // 出撃前の成功率予測（SuccessRateCalculator）と実際の解決（このクラス）が同じ式を
        // 共有し、「表示された成功率と実際の結果が食い違う」事故を構造的に防ぐため。

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

        /// <summary>
        /// HP消費%へ環境ギミック（損耗系。→ GimmickPhase.Attrition）と携行アイテムの効果を
        /// 適用する（→ 環境ギミック・パーティ携行アイテム刷新仕様）。
        /// 適用順：①ギミックのペナルティ係数を乗算 → ②煙幕弾（ダウン率半減）を乗算 →
        /// ③高品質傷薬（固定量軽減）を減算。0〜100にクランプする。
        /// </summary>
        private static int AdjustLossPct(int lossPct, double attritionMultiplier, IReadOnlyList<string> consumableItemIds)
        {
            double adjusted = lossPct * attritionMultiplier;

            if (consumableItemIds.Contains(ConsumableCatalog.SmokeBombId))
                adjusted *= ConsumableBalance.SmokeBombDownRateMultiplier;

            if (consumableItemIds.Contains(ConsumableCatalog.QualityHealingSalveId))
                adjusted -= ConsumableBalance.QualityHealingSalveDamageReductionPct;

            return (int)Clamp(Math.Round(adjusted), 0, 100);
        }
    }
}
