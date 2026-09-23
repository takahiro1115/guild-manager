using System;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 大迷宮「探索（採取）」の解決エンジン（→ 第3の任務、ダンジョン攻略システム）。
    ///
    /// 調査任務（ScoutingResolver）・道中進軍（DungeonTraversalResolver）と並ぶ低リスク経路。
    /// 特定のボスではなく**フィールドそのもの**を対象にする（→ ActiveDungeonMission.Boss はnull）。
    /// 成果は素材（→ Balance.MaterialBalance、フィールドごとの定義）と少量の換金ゴールド。
    /// HP下限1で止まり、致死判定には一切接続しない（→ ScoutingResolver.MinHpと同じ考え方）。
    /// </summary>
    public class GatheringResolver
    {
        /// <summary>採取でのHP下限。致死判定に接続しないため0にはしない。</summary>
        private const int MinHp = 1;

        private readonly IRng _rng;

        public GatheringResolver(IRng rng)
        {
            _rng = rng;
        }

        /// <summary>
        /// 部隊をフィールドへ探索に差し向け、素材・ゴールドを算出する。
        /// </summary>
        /// <param name="state">
        /// 省略可能。渡した場合、GatheringYieldBonus種別の研究（特殊保存嚢等）が完了済みなら、
        /// 完了分すべてのEffectValueを合計して獲得数に加算する（→ Balance.ResearchBalance.
        /// GetTotalEffectValue、アルベールの研究室。同種の研究が複数完了していても加算で
        /// 重複できる）。nullなら通常どおりボーナス無しで解決する（既存の呼び出し側・
        /// テストとの互換用）。
        /// </param>
        public GatheringResult Resolve(Party party, DungeonField field, GameState? state = null)
        {
            if (party.Members.Count == 0)
                throw new InvalidOperationException("空のパーティは探索任務に出せません。");

            var result = new GatheringResult();
            double score = CalculateGatheringScore(party);
            result.Score = score;
            result.ScoreBreakdown = BreakDownGatheringScore(party);

            string materialId = RollMaterial(field);
            int baseYield = MaterialBalance.Find(materialId)?.BaseYield ?? 0;
            result.BaseYield = baseYield;
            result.ScoreYield = ScoreYield(score);
            result.FloorYield = FloorYield(field);

            int materialCount = Math.Max(1, baseYield + result.ScoreYield + result.FloorYield);

            // 研究バフ：GatheringYieldBonus種別の研究が完了済みなら、合計EffectValueを獲得数へ
            // 加算する（→ アルベールの研究室）。
            result.ResearchYield = ResearchYield(state);
            materialCount += result.ResearchYield;

            result.MaterialId = materialId;
            result.MaterialCount = materialCount;
            result.GoldEarned = (int)Math.Round(score * GatheringBalance.GoldPerScore);
            result.RelicDropPercent = RelicBalance.GetGatheringDropPercent(score, field.ReachedFloor);
            result.UnidentifiedItemFound = RollRelic(field, result.RelicDropPercent);

            ApplyHpLoss(result, party);

            return result;
        }

        /// <summary>
        /// 未鑑定の古代遺物のドロップ判定（→ 03 §4.7）。採取スコア（DEX/AGI/隊長LDR由来）と
        /// フィールドの最高到達階層が高いほど確率が上がる（→ RelicBalance.GetGatheringDropPercent、
        /// 基礎5%〜上限15%）。ドロップしなければnull。
        ///
        /// ここではGameStateを書き換えず、結果（GatheringResult）に記録するだけに留める
        /// （素材・ゴールドと同じく、ギルドへの反映は週次解決側の責務。→ GatheringResolver冒頭の方針）。
        /// </summary>
        private UnidentifiedItem? RollRelic(DungeonField field, int dropPct)
        {
            if (_rng.NextInt(1, 100) > dropPct)
                return null;

            return AppraisalSystem.CreateRelic(_rng, field.Id, field.ReachedFloor);
        }

        /// <summary>
        /// 採取スコア＝(Σ(AGI×係数＋DEX×係数)＋部隊長LDR×係数＋盗賊・斥候ボーナス)×部隊の平均HP比率。
        /// 空の部隊は0。public static にしてあるのは出撃前のプレビュー（UI）とテストから同じ式を
        /// 使うため（→ ScoutingResolver.CalculateStealthScoreと同じ考え方）。
        /// </summary>
        public static double CalculateGatheringScore(Party party)
        {
            if (party.IsEmpty) return 0;

            double statSum = party.Members.Sum(m =>
                    m.GetEffectiveStat("AGI") * GatheringBalance.AgiCoefficient
                    + m.GetEffectiveStat("DEX") * GatheringBalance.DexCoefficient)
                + party.Members[0].GetEffectiveStat("LDR") * GatheringBalance.LeaderLdrCoefficient;

            // 盗賊・斥候ボーナス：地形の見極め・目利きに長けた職業が1名いるごとに固定ボーナスを加算する。
            double classBonus = party.Members.Count(m => m.JobClass is JobClass.Thief or JobClass.Ranger)
                * GatheringBalance.ThiefRangerScoreBonus;

            double hpRatio = party.Members.Average(m => (double)m.CurrentHP / m.MaxHP);

            return (statSum + classBonus) * hpRatio;
        }

        /// <summary>
        /// 採取スコアの内訳（UIの出撃前プレビュー・週報の開示用）。合計は CalculateGatheringScore と同じ式。
        /// </summary>
        public static GatheringScoreBreakdown BreakDownGatheringScore(Party party)
        {
            if (party.IsEmpty) return new GatheringScoreBreakdown(0, 0, 0, 0, 1);

            return new GatheringScoreBreakdown(
                AgiPart: party.Members.Sum(m => m.GetEffectiveStat("AGI") * GatheringBalance.AgiCoefficient),
                DexPart: party.Members.Sum(m => m.GetEffectiveStat("DEX") * GatheringBalance.DexCoefficient),
                LdrPart: party.Members[0].GetEffectiveStat("LDR") * GatheringBalance.LeaderLdrCoefficient,
                ClassBonus: party.Members.Count(m => m.JobClass is JobClass.Thief or JobClass.Ranger) * GatheringBalance.ThiefRangerScoreBonus,
                HpRatio: party.Members.Average(m => (double)m.CurrentHP / m.MaxHP));
        }

        /// <summary>獲得数のうち採取スコア由来の枠＝(int)(採取スコア÷MaterialYieldDivisor)。</summary>
        public static int ScoreYield(double score) => (int)(score / GatheringBalance.MaterialYieldDivisor);

        /// <summary>獲得数のうち到達階層由来の枠＝到達階層÷ReachedFloorDivisor（切り捨て）。</summary>
        public static int FloorYield(DungeonField field) => field.ReachedFloor / GatheringBalance.ReachedFloorDivisor;

        /// <summary>獲得数のうち研究（GatheringYieldBonus）由来の枠。stateがnullなら0。</summary>
        public static int ResearchYield(GameState? state) =>
            state == null ? 0 : (int)ResearchBalance.GetTotalEffectValue(state, ResearchEffectType.GatheringYieldBonus);

        /// <summary>
        /// フィールド・到達階層で抽選対象になる素材（→ MaterialBalance.GetEligibleMaterials）から
        /// 1種を等確率で選ぶ。対象が空（未定義のフィールドId・到達階層がどの素材のMinFloorにも
        /// 届いていない）場合は空文字を返す。
        /// </summary>
        private string RollMaterial(DungeonField field)
        {
            var eligible = MaterialBalance.GetEligibleMaterials(field.Id, field.ReachedFloor);
            if (eligible.Count == 0)
                return "";

            int index = _rng.NextInt(0, eligible.Count - 1);
            return eligible[index].Id;
        }

        /// <summary>
        /// 採取のHP消費。ランクの区別はなく一律（→ BAL: gathering.csv）。
        /// HPは下限1で止まり、致死判定・負傷状態には一切接続しない。
        /// </summary>
        private void ApplyHpLoss(GatheringResult result, Party party)
        {
            foreach (var member in party.Members)
            {
                int lossPct = _rng.NextInt(GatheringBalance.HpLossPctMin, GatheringBalance.HpLossPctMax);
                int hpLoss = member.MaxHP * lossPct / 100;
                int newHp = Math.Max(MinHp, member.CurrentHP - hpLoss);

                result.HpLostByAdventurer[member.Id] = member.CurrentHP - newHp;
                member.CurrentHP = newHp;
            }
        }
    }
}
