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
    /// 成果は素材（→ Models.MaterialCatalog、フィールドごとの抽選表）と少量の換金ゴールド。
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
        /// 省略可能。渡した場合、研究「拡張採取袋」（→ Models.ResearchIds.GatheringBag）が
        /// 完了済みなら獲得数にボーナスを加算する（→ アルベールの研究室）。
        /// nullなら通常どおりボーナス無しで解決する（既存の呼び出し側・テストとの互換用）。
        /// </param>
        public GatheringResult Resolve(Party party, DungeonField field, GameState? state = null)
        {
            if (party.Members.Count == 0)
                throw new InvalidOperationException("空のパーティは探索任務に出せません。");

            var result = new GatheringResult();
            double score = CalculateGatheringScore(party);

            int materialCount = Math.Max(1, (int)(score / GatheringBalance.MaterialYieldDivisor))
                + field.ReachedFloor / GatheringBalance.ReachedFloorDivisor;

            // 研究バフ：拡張採取袋が完了済みなら、獲得数にボーナスを加算する（→ アルベールの研究室）。
            if (state != null && state.IsResearchCompleted(ResearchIds.GatheringBag))
            {
                var research = ResearchBalance.Find(ResearchIds.GatheringBag);
                if (research != null)
                    materialCount += (int)research.EffectValue;
            }

            result.MaterialId = RollMaterial(field);
            result.MaterialCount = materialCount;
            result.GoldEarned = (int)Math.Round(score * GatheringBalance.GoldPerScore);

            ApplyHpLoss(result, party);

            return result;
        }

        /// <summary>
        /// 採取スコア＝(Σ(AGI×係数＋DEX×係数)＋部隊長LDR×係数)×部隊の平均HP比率。空の部隊は0。
        /// public static にしてあるのは出撃前のプレビュー（UI）とテストから同じ式を使うため
        /// （→ ScoutingResolver.CalculateStealthScoreと同じ考え方）。
        /// </summary>
        public static double CalculateGatheringScore(Party party)
        {
            if (party.IsEmpty) return 0;

            double statSum = party.Members.Sum(m =>
                    m.GetEffectiveStat("AGI") * GatheringBalance.AgiCoefficient
                    + m.GetEffectiveStat("DEX") * GatheringBalance.DexCoefficient)
                + party.Members[0].GetEffectiveStat("LDR") * GatheringBalance.LeaderLdrCoefficient;

            double hpRatio = party.Members.Average(m => (double)m.CurrentHP / m.MaxHP);

            return statSum * hpRatio;
        }

        /// <summary>
        /// フィールド固有の抽選表（→ MaterialCatalog.GetFieldDrops）から素材を1種選ぶ。
        /// 抽選表が空（未定義のフィールドId）の場合は空文字を返す。
        /// </summary>
        private string RollMaterial(DungeonField field)
        {
            var drops = MaterialCatalog.GetFieldDrops(field.Id);
            if (drops.Count == 0)
                return "";

            int roll = _rng.NextInt(1, 100);
            int cumulative = 0;
            foreach (var drop in drops)
            {
                cumulative += drop.Percent;
                if (roll <= cumulative)
                    return drop.MaterialId;
            }

            return drops[^1].MaterialId; // 端数の丸め対策（合計が100をわずかに割り込む場合の保険）
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
