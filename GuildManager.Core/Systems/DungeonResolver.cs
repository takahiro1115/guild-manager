using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 階層ボス討伐の解決エンジン（→ ダンジョン攻略システム）。
    ///
    /// 直接指揮UIは作らない：編成と携行品を決めて送り出せば、あとは完全自動で判定し
    /// 構造化された結果（→ DungeonResult）を返す（既存のQuestResolverと同じ方針）。
    ///
    /// 勝敗を分けるのは「事前にどれだけ調べ、どれだけ対策を積んだか」：
    ///  - ギミック対策：各ギミックについて「対策ロール／対策ステータス合算／対策アイテム」の
    ///    いずれかを満たしていれば対策成立（OR条件。唯一解のパズルにしないため）。
    ///  - 未対策ペナルティ：未対策1件につき危険度に比例して被ダメージ倍率が積み上がる。
    ///    即死級（InstantKill）を未対策で踏むとHPを全損する＝無調査での突撃は壊滅する。
    ///  - 完全解析ボーナス：解析率1.0（→ ScoutingResolver）で与ダメージが上乗せされる。
    ///
    /// 討伐クエスト（QuestResolver）との違い：こちらはHP下限0で、0に到達した冒険者は
    /// 強制除籍（恒久ロスト）になる（→ DungeonResult.ForceRetiredAdventurerIds）。
    /// </summary>
    public class DungeonResolver
    {
        private readonly IRng _rng;

        public DungeonResolver(IRng rng)
        {
            _rng = rng;
        }

        /// <summary>
        /// 部隊を階層ボスへ差し向け、討伐を解決する。撃破した場合は boss.IsDefeated を立て、
        /// boss.CurrentHp を0にする（撤退時はボスのHPを元に戻し、次回は仕切り直しとする）。
        /// </summary>
        /// <param name="state">
        /// 省略可能。渡した場合、SurvivalThresholdBonus種別の研究（魂魄安定の霊香等）が
        /// 完了済みなら、完了分すべてのEffectValueを合計してHP消費率（%）から差し引く
        /// （→ Balance.ResearchBalance.GetTotalEffectValue、アルベールの研究室、ApplyHpLoss）。
        /// nullなら通常どおりボーナス無しで解決する（既存の呼び出し側・テストとの互換用）。
        /// </param>
        public DungeonResult Resolve(Party party, FloorBoss boss, GameState? state = null)
        {
            if (party.Members.Count == 0)
                throw new InvalidOperationException("空のパーティはダンジョンに出せません。");

            var result = new DungeonResult();

            // ---- ギミック対策の判定 ----
            foreach (var gimmick in boss.Gimmicks)
            {
                if (IsCountered(gimmick, party))
                    result.CounteredGimmicks.Add(gimmick.Type);
                else
                    result.UncounteredGimmicks.Add(gimmick.Type);
            }

            result.DamageMultiplier = CalculateDamageMultiplier(boss, result, party);

            // ---- 火力判定（ボスのHPを削り切れるか） ----
            // 重装甲ボスなら巨獣狩りの保有者の火力に上乗せが効く（→ DungeonPowerCalculator.MemberPower）。
            // 完全解析なら弱点を突ける（→ ScoutingResolver で解析率を1.0まで上げた場合）。
            double partyPower = CalculateBossPower(party, boss);
            result.GiantHunterAdventurerIds.AddRange(
                party.Members.Where(m => DungeonPowerCalculator.GiantHunterApplies(m, boss)).Select(m => m.Id));
            result.FullIntelBonusApplied = ScoutingResolver.GetTier(boss.IntelRate) == IntelTier.Complete;

            result.PartyPower = partyPower;
            result.RequiredPower = RequiredPower(boss);
            result.Outcome = partyPower >= result.RequiredPower ? DungeonOutcome.Victory : DungeonOutcome.Retreat;

            if (result.Outcome == DungeonOutcome.Victory)
            {
                boss.CurrentHp = 0;
                boss.IsDefeated = true;
            }

            double survivalBonus = state != null
                ? ResearchBalance.GetTotalEffectValue(state, ResearchEffectType.SurvivalThresholdBonus)
                : 0;
            ApplyHpLoss(result, party, survivalBonus);
            RollGiantHunterAwakening(result, party, boss);

            // 携行アイテムは使い切り（→ QuestResolver.Resolve と同じ扱い）。
            party.ConsumableItemIds.Clear();

            return result;
        }

        /// <summary>
        /// ボス討伐の要求火力＝ボス階層×PartyPowerRequirementPerFloor（→ BAL: dungeon.csv）。
        /// Resolve と、大迷宮画面の出撃前の見立て（→ DungeonPanel）が共通で使う。
        /// </summary>
        public static double RequiredPower(FloorBoss boss) => boss.Floor * DungeonBalance.PartyPowerRequirementPerFloor;

        /// <summary>
        /// そのボスに挑んだ場合の部隊火力（巨獣狩りの上乗せ・完全解析の弱点ボーナス込み）。
        /// Resolve の火力判定と、大迷宮画面の出撃前の見立てが共通で使う。
        /// </summary>
        public static double CalculateBossPower(Party party, FloorBoss boss)
        {
            double power = DungeonPowerCalculator.PartyPower(party.Members, boss);
            if (ScoutingResolver.GetTier(boss.IntelRate) == IntelTier.Complete)
                power *= 1.0 + DungeonBalance.FullIntelDamageBonus;
            return power;
        }

        /// <summary>
        /// ギミック1件が対策できているか。3つの対策口（職業・ステータス合算・携行アイテム）の
        /// いずれかを満たせば対策成立（OR条件）。対策口が1つも設定されていないギミックは
        /// 「対策不能」として常に未対策になる（ボス定義側の設定漏れを黙って握り潰さないため）。
        /// public static にしてあるのは編成画面のプレビューとテストから直接使えるようにするため。
        /// </summary>
        public static bool IsCountered(BossGimmick gimmick, Party party)
        {
            if (gimmick.RequiredCounterRole.HasValue &&
                party.Members.Any(m => m.JobClass == gimmick.RequiredCounterRole.Value))
                return true;

            if (!string.IsNullOrEmpty(gimmick.RequiredCounterStat))
            {
                double sum = party.Members.Sum(m => m.GetEffectiveStat(gimmick.RequiredCounterStat!));
                if (sum >= gimmick.RequiredCounterStatThreshold)
                    return true;
            }

            if (!string.IsNullOrEmpty(gimmick.RequiredItemId) &&
                party.ConsumableItemIds.Contains(gimmick.RequiredItemId!))
                return true;

            return false;
        }

        /// <summary>
        /// 未対策ギミックによる被ダメージ倍率。未対策1件につき危険度に比例して積み上がる。
        /// すべて対策済みなら1.0（＝通常の討伐並みの損害で済む）。
        /// 未対策の「猛毒」については、部隊に耐毒体質（→ TraitCatalog.ResistPoison）の保有者が1人でもいれば、
        /// その猛毒分の加算を ResistPoisonDamageReductionRate だけ減らす（→ 03 §4.5.4・§5.3.2）。
        /// 猛毒が対策済みなら猛毒分の加算はもともと無いため、耐毒体質は効かない。
        /// </summary>
        private static double CalculateDamageMultiplier(FloorBoss boss, DungeonResult result, Party party)
        {
            double multiplier = 1.0;
            bool resistPoison = party.Members.Any(m => m.HasTrait(TraitCatalog.ResistPoisonId));

            foreach (var gimmick in boss.Gimmicks)
            {
                if (result.CounteredGimmicks.Contains(gimmick.Type))
                    continue;

                double term = gimmick.DangerLevel * DungeonBalance.UncounteredDamageMultiplierPerDangerLevel;
                if (gimmick.Type == BossGimmickType.Poison && resistPoison)
                {
                    term *= 1.0 - CombatBalance.ResistPoisonDamageReductionRate;
                    result.ResistPoisonApplied = true;
                }
                multiplier += term;
            }

            return multiplier;
        }

        /// <summary>
        /// 巨獣狩りの後天開眼（→ 03 §4.5.4・§5.3.2、2026年9月・§0.35）。「重装甲」ギミックを持つボスを**撃破**したときだけ、
        /// 生存した隊員（HP1以上＝強制除籍されていない）ごとに GiantHunterAwakeningChance でロールする。
        /// 既に巨獣狩りを持つ隊員・特性5枠満杯の隊員はロールしない（通常特性なので侵食はしない）。
        /// 乱数は対象の隊員ごとに NextInt(1, 100) を1回引き、round(確率×100) 以下で当選。
        /// </summary>
        private void RollGiantHunterAwakening(DungeonResult result, Party party, FloorBoss boss)
        {
            if (result.Outcome != DungeonOutcome.Victory) return;
            if (!boss.Gimmicks.Any(g => g.Type == BossGimmickType.HeavyArmor)) return;

            int threshold = (int)Math.Round(CombatBalance.GiantHunterAwakeningChance * 100);
            foreach (var member in party.Members)
            {
                if (member.CurrentHP <= 0) continue;
                if (!member.CanAddTrait(TraitCatalog.GiantHunterId)) continue; // 所持済み・5枠満杯
                if (_rng.NextInt(1, 100) > threshold) continue;

                if (member.TryAddTrait(TraitCatalog.GiantHunterId))
                    result.TraitGrantEvents.Add(new TraitGrantEvent(
                        member.Id, member.Name, TraitCatalog.GiantHunterId, null, TraitGrantCause.Awakening));
            }
        }

        /// <summary>
        /// HP消費の適用。撃破できたかどうかでベースのレンジが変わり、未対策ギミックの
        /// 倍率が乗る。即死級を未対策で踏んだ場合はレンジを無視して全損させる。
        ///
        /// HP下限は0：0に到達した冒険者は強制除籍（恒久ロスト）となる
        /// （→ DungeonResult.ForceRetiredAdventurerIds。世界観上は「秘薬で一命を取り留めたが
        /// アルベールが登録を抹消した」という扱い）。
        ///
        /// survivalBonus（2026年9月新設、→ ResearchEffectType.SurvivalThresholdBonus）は
        /// 算出後のHP消費率（%）から%ポイントで差し引く（0未満にはしない）。即死級ギミック
        /// 未対策時の全損（100%）にも適用されるため、事故を軽傷へ和らげる効果として働く。
        /// </summary>
        private void ApplyHpLoss(DungeonResult result, Party party, double survivalBonus = 0)
        {
            bool instantKillTriggered = result.UncounteredGimmicks.Contains(BossGimmickType.InstantKill);

            var (minPct, maxPct) = result.Outcome == DungeonOutcome.Victory
                ? (DungeonBalance.BaseHpLossPctMin, DungeonBalance.BaseHpLossPctMax)
                : (DungeonBalance.RetreatHpLossPctMin, DungeonBalance.RetreatHpLossPctMax);

            foreach (var member in party.Members)
            {
                int lossPct;
                if (instantKillTriggered)
                {
                    // 即死級ギミックは対策の有無が生死を分ける（倍率ではなく固定で全損させる）。
                    lossPct = DungeonBalance.InstantKillUncounteredHpLossPct;
                }
                else
                {
                    lossPct = _rng.NextInt(minPct, maxPct);
                    lossPct = (int)Math.Clamp(Math.Round(lossPct * result.DamageMultiplier), 0, 100);
                }

                lossPct = (int)Math.Max(0, lossPct - survivalBonus);

                int hpLoss = member.MaxHP * lossPct / 100;
                int newHp = Math.Max(0, member.CurrentHP - hpLoss);

                result.HpLostByAdventurer[member.Id] = member.CurrentHP - newHp;
                member.CurrentHP = newHp;

                if (newHp == 0)
                    result.ForceRetiredAdventurerIds.Add(member.Id);
                // 重傷生還の古傷（→ 03 §4.3.2）：撤退で最大HPの10%以下（最低でもHP1、→ CriticalInjury.RetreatHpThreshold）
                // で生還した隊員だけロールする（撃破時は対象外）。
                else if (result.Outcome == DungeonOutcome.Retreat
                         && CriticalInjury.RollOldWound(member, _rng, CriticalInjury.RetreatHpThreshold(member)) is { } grant)
                    result.TraitGrantEvents.Add(grant);
            }
        }
    }
}
