using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 調査任務（スカウティング）の解決エンジン（→ ダンジョン攻略システム）。
    ///
    /// 目的は「階層ボスへ突撃する前に情報を買う」こと。討伐と違い、ここでの成果は
    /// 報酬ではなく **FloorBoss.IntelRate の上昇**（＝次の討伐が安全になる）である。
    ///
    /// 判定は2つ：
    ///  1. 隠密・生還：Σ(AGI+DEX) ＋ 部隊長LDR×係数 vs 階層×要求値。
    ///     失敗すると見つかり、HP消費が重くなり、解析の成果も1段階下がる。
    ///  2. 解析・情報収集：Σ(INT) vs 階層×要求値。Ratioに応じて大成功／成功／失敗。
    ///  3. 護衛（2026年9月新設）：部隊護衛力（→ CalculateGuardPower）÷要求護衛値の比率で
    ///     4段階（余裕／十分／充足／不足、→ GuardTier）。解析率上昇量に段階ごとの倍率を掛け、
    ///     各員のHP消費（最大HP×段階ごとの%）もこの段階だけで決まる。
    ///
    /// 低リスク経路として設計する：致死判定には一切接続せず、HPは下限1で止まる
    /// （→ QuestResolverの探索・護衛と同じ扱い。調査で人を失うと「調べてから挑む」
    /// という本システムの導線自体が機能しなくなるため）。
    /// </summary>
    public class ScoutingResolver
    {
        /// <summary>調査でのHP下限。致死判定に接続しないため0にはしない（→ QuestResolver.NonCombatMinHp と同じ考え方）。</summary>
        private const int MinHp = 1;

        /// <summary>
        /// rng は現在使っていない（HP消費が乱数幅から護衛段階ごとの固定率に変わったため、2026年9月改訂）。
        /// 他のResolverと生成方法を揃え、既存の呼び出し側を変えずに済むよう引数だけ残している。
        /// </summary>
        public ScoutingResolver(IRng rng)
        {
        }

        /// <summary>
        /// 調査隊をボスへ差し向け、解析率を更新する。boss.IntelRate は本メソッドが直接書き換える。
        /// </summary>
        /// <param name="state">
        /// 省略可能。渡した場合、IntelRateBonus種別の研究（生体蛍光試薬・古代戦術録の解読等）が
        /// 完了済みなら、完了分すべてのEffectValueを合計して(1+合計)倍を解析率上昇量に掛ける
        /// （→ Balance.ResearchBalance.GetTotalEffectValue、アルベールの研究室。同種の研究が
        /// 複数完了していても加算で重複できる）。nullなら通常どおりボーナス無しで解決する
        /// （既存の呼び出し側・テストとの互換用）。
        /// </param>
        public ScoutingResult Resolve(Party party, FloorBoss boss, GameState? state = null)
        {
            if (party.Members.Count == 0)
                throw new InvalidOperationException("空のパーティは調査に出せません。");

            var result = new ScoutingResult();
            var tierBefore = GetTier(boss.IntelRate);

            // ---- 判定1：隠密・生還（AGI+DEX ＋ 部隊長LDRによる事故防止） ----
            result.StealthSucceeded = CalculateStealthScore(party) >= StealthRequirement(boss);

            // ---- 判定2：解析・情報収集（INT） ----
            double analysisRequirement = AnalysisRequirement(boss);
            double analysisRatio = analysisRequirement <= 0 ? double.MaxValue : CalculateAnalysisScore(party) / analysisRequirement;

            var outcome = ClassifyAnalysis(analysisRatio);

            // 見つかった調査隊は落ち着いて観察できない：解析の成果を1段階下げる
            // （隠密が「情報の質」に効くことで、AGI/DEX型とINT型の両方を編成する動機になる）。
            if (!result.StealthSucceeded)
                outcome = Downgrade(outcome);

            result.AnalysisOutcome = outcome;

            // ---- 解析率の更新 ----
            double gain = outcome switch
            {
                SurveyOutcome.GreatSuccess => ScoutingBalance.IntelGainGreatSuccess,
                SurveyOutcome.Success => ScoutingBalance.IntelGainSuccess,
                _ => ScoutingBalance.IntelGainPartial,
            };

            // 研究バフ：IntelRateBonus種別の研究が完了済みなら、合計EffectValueで解析率上昇量に
            // (1+合計)倍を掛ける（→ アルベールの研究室。EffectValue=0.25なら「+25%」＝1.25倍、
            // HpRecoveryBonusと同じ加算率の考え方）。
            // 参謀（→ AdvisorSystem.GetAdvisorSurveyIntelBonus、2026年9月再配線）：作戦分析による加算率を
            // 研究ボーナスと合算する。上昇量＝基礎×(1＋研究ボーナス＋参謀解析ボーナス)×護衛倍率。
            if (state != null)
            {
                result.AdvisorIntelBonus = AdvisorSystem.GetAdvisorSurveyIntelBonus(state);
                result.AdvisorName = result.AdvisorIntelBonus > 0 ? AdvisorSystem.GetAssignedAdvisor(state)?.Name : null;
                gain *= 1 + ResearchBalance.GetTotalEffectValue(state, ResearchEffectType.IntelRateBonus) + result.AdvisorIntelBonus;
            }

            // ---- 判定3：護衛（段階ごとに解析成果へ倍率。不足なら成果0＝調査隊が潰走） ----
            double guardRequirement = RequiredGuardPower(boss);
            result.GuardRatio = guardRequirement <= 0 ? double.MaxValue : CalculateGuardPower(party) / guardRequirement;
            result.GuardTier = ClassifyGuard(result.GuardRatio);
            gain *= GuardIntelMultiplier(result.GuardTier);

            double before = boss.IntelRate;
            boss.IntelRate = Math.Min(ScoutingBalance.IntelTierComplete, boss.IntelRate + gain);

            result.IntelGained = boss.IntelRate - before; // 上限クランプ後の実際の増分
            result.IntelRateAfter = boss.IntelRate;
            result.TierAfter = GetTier(boss.IntelRate);
            result.TierAdvanced = result.TierAfter != tierBefore;

            ApplyHpLoss(result, party);

            return result;
        }

        /// <summary>
        /// 隠密スコア＝Σ(AGI+DEX)×係数 ＋ 部隊長LDR×係数。空の部隊は0。
        /// public static にしてあるのは出撃前のプレビュー（UI）とテストから同じ式を使うため。
        /// </summary>
        public static double CalculateStealthScore(Party party)
        {
            if (party.IsEmpty) return 0;

            return party.Members.Sum(m => m.GetEffectiveStat("AGI") + m.GetEffectiveStat("DEX"))
                * ScoutingBalance.StealthStatCoefficient
                + party.Members[0].GetEffectiveStat("LDR") * ScoutingBalance.LeaderPanicPreventionCoefficient;
        }

        /// <summary>解析スコア＝Σ(INT)×係数。空の部隊は0。</summary>
        public static double CalculateAnalysisScore(Party party) =>
            party.Members.Sum(m => m.GetEffectiveStat("INT")) * ScoutingBalance.AnalysisStatCoefficient;

        /// <summary>隠密の要求値＝階層×係数（深い階層ほど見つかりやすい）。</summary>
        public static double StealthRequirement(FloorBoss boss) => boss.Floor * ScoutingBalance.StealthRequirementPerFloor;

        /// <summary>解析の要求値＝階層×係数。</summary>
        public static double AnalysisRequirement(FloorBoss boss) => boss.Floor * ScoutingBalance.AnalysisRequirementPerFloor;

        /// <summary>解析Ratioから3区分を求める。public static にしてあるのはテストから直接呼べるようにするため。</summary>
        public static SurveyOutcome ClassifyAnalysis(double ratio)
        {
            if (ratio >= ScoutingBalance.RatioThresholdGreatSuccess) return SurveyOutcome.GreatSuccess;
            if (ratio >= ScoutingBalance.RatioThresholdSuccess) return SurveyOutcome.Success;
            return SurveyOutcome.Failure;
        }

        /// <summary>隠密失敗時に解析成果を1段階下げる（大成功→成功→失敗）。</summary>
        private static SurveyOutcome Downgrade(SurveyOutcome outcome) => outcome switch
        {
            SurveyOutcome.GreatSuccess => SurveyOutcome.Success,
            SurveyOutcome.Success => SurveyOutcome.Failure,
            _ => SurveyOutcome.Failure,
        };

        /// <summary>
        /// 解析率から情報開示段階を求める（→ Models.IntelTier）。
        /// public static にしてあるのはUI（開示内容の出し分け）とテストの両方から使うため。
        /// </summary>
        public static IntelTier GetTier(double intelRate)
        {
            if (intelRate >= ScoutingBalance.IntelTierComplete) return IntelTier.Complete;
            if (intelRate >= ScoutingBalance.IntelTierCountermeasures) return IntelTier.Countermeasures;
            if (intelRate >= ScoutingBalance.IntelTierHazards) return IntelTier.Hazards;
            if (intelRate >= ScoutingBalance.IntelTierBasic) return IntelTier.Basic;
            return IntelTier.Unknown;
        }

        // ==================== 護衛判定（2026年9月新設） ====================

        /// <summary>
        /// 部隊護衛力＝出撃メンバー全員の中で最大の max(STR, VIT, INT)（装備補正込みの実効値）。
        /// 腕っぷし（STR）・頑健さ（VIT）・魔導（INT）のいずれかで魔物の残党を退けられる
        /// 護衛役が1人いれば足りる、という考え方。空の部隊は0。
        /// public static にしてあるのは出撃前のプレビュー（UI）とテストから同じ式を使うため。
        /// </summary>
        public static double CalculateGuardPower(Party party) =>
            party.IsEmpty
                ? 0
                : party.Members.Max(m => Math.Max(m.GetEffectiveStat("STR"),
                    Math.Max(m.GetEffectiveStat("VIT"), m.GetEffectiveStat("INT"))));

        /// <summary>要求護衛値＝BaseRequiredGuardPower（10F区間の基準）×ボス階層÷10。</summary>
        public static double RequiredGuardPower(FloorBoss boss) =>
            ScoutingBalance.BaseRequiredGuardPower * boss.Floor / 10.0;

        /// <summary>護衛比率から4段階（余裕／十分／充足／不足）を求める。</summary>
        public static GuardTier ClassifyGuard(double ratio)
        {
            if (ratio >= ScoutingBalance.GuardRatioAbundant) return GuardTier.Abundant;
            if (ratio >= ScoutingBalance.GuardRatioSufficient) return GuardTier.Sufficient;
            if (ratio >= ScoutingBalance.GuardRatioMarginal) return GuardTier.Marginal;
            return GuardTier.Deficient;
        }

        /// <summary>部隊と対象ボスから護衛段階を求める（UIの事前プレビュー用）。</summary>
        public static GuardTier PreviewGuardTier(Party party, FloorBoss boss)
        {
            double requirement = RequiredGuardPower(boss);
            return ClassifyGuard(requirement <= 0 ? double.MaxValue : CalculateGuardPower(party) / requirement);
        }

        public static double GuardIntelMultiplier(GuardTier tier) => tier switch
        {
            GuardTier.Abundant => ScoutingBalance.GuardIntelMultiplierAbundant,
            GuardTier.Sufficient => ScoutingBalance.GuardIntelMultiplierSufficient,
            GuardTier.Marginal => ScoutingBalance.GuardIntelMultiplierMarginal,
            _ => ScoutingBalance.GuardIntelMultiplierDeficient,
        };

        public static int GuardHpLossPercent(GuardTier tier) => tier switch
        {
            GuardTier.Abundant => ScoutingBalance.GuardHpLossPercentAbundant,
            GuardTier.Sufficient => ScoutingBalance.GuardHpLossPercentSufficient,
            GuardTier.Marginal => ScoutingBalance.GuardHpLossPercentMarginal,
            _ => ScoutingBalance.GuardHpLossPercentDeficient,
        };

        /// <summary>
        /// 調査のHP消費＝各員の最大HP×護衛段階ごとの%（→ GuardHpLossPercent）。
        /// HPは下限1で止まり、致死判定・負傷状態・強制除籍には一切接続しない。
        /// </summary>
        private static void ApplyHpLoss(ScoutingResult result, Party party)
        {
            int lossPct = GuardHpLossPercent(result.GuardTier);

            foreach (var member in party.Members)
            {
                int hpLoss = member.MaxHP * lossPct / 100;
                int newHp = Math.Max(MinHp, member.CurrentHP - hpLoss);

                result.HpLostByAdventurer[member.Id] = member.CurrentHP - newHp;
                member.CurrentHP = newHp;
            }
        }
    }
}
