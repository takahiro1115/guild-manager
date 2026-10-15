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
    /// 直接指揮UIは作らない：編成を決めて送り出せば、あとは完全自動で判定し
    /// 構造化された結果（→ DungeonResult）を返す。
    ///
    /// 番人はギミックを持たない（§0.138。§0.68〜§0.137 の備え・ギミックの罰は削除した）。勝敗は火力÷要求火力の勝率で決まり、
    /// 完全解析（解析率1.0、→ ScoutingResolver）なら与ダメージが上乗せされる。
    ///
    /// HP下限は0。0に到達した冒険者は FatalChancePercent の確率で強制除籍（恒久ロスト、→ DungeonResult.ForceRetiredAdventurerIds）、
    /// 外れれば HP1 で一命を取り留めて重傷になる（§0.125、→ DungeonResult.NearDeathSurvivorIds）。
    /// </summary>
    public class DungeonResolver
    {
        private readonly IRng _rng;
        /// <summary>決戦の勝ち負けだけに使う乱数（§0.98。損耗・開眼などの抽選の順を変えないよう別にする）。</summary>
        private readonly IRng _outcomeRng;
        /// <summary>相棒のかばい合いだけに使う乱数（§0.101。損耗・勝ち負けの抽選の順を変えないよう別にする）。</summary>
        private readonly IRng _guardRng;
        /// <summary>HP0の強制除籍の判定だけに使う乱数（§0.125。損耗・勝ち負け・かばい合いの抽選の順を変えないよう別にする）。</summary>
        private readonly IRng _fatalRng;

        private const int DefaultOutcomeSeed = 1597;
        private const int DefaultGuardSeed = 1601;
        private const int DefaultFatalSeed = 1607;

        public DungeonResolver(IRng rng, IRng? outcomeRng = null, IRng? guardRng = null, IRng? fatalRng = null)
        {
            _rng = rng;
            _outcomeRng = outcomeRng ?? new SeededRng(DefaultOutcomeSeed);
            _guardRng = guardRng ?? new SeededRng(DefaultGuardSeed);
            _fatalRng = fatalRng ?? new SeededRng(DefaultFatalSeed);
        }

        /// <summary>
        /// 決戦の勝率（§0.98）：火力÷要求の比 r から、1÷(1＋e^(−傾き×(r−中心)))。r が BossWinSureRatio 以上なら1、BossWinHopelessRatio 未満なら0
        /// （→ BAL: dungeon.csv BossWin*）。以前は火力が要求以上なら必ず勝ち、未満なら必ず負けた（見立てで答えが分かり、扉前の判断に余地が無かった）。
        /// </summary>
        public static double WinChance(double power, double required)
        {
            if (required <= 0) return 1.0;
            double r = power / required;
            if (r >= DungeonBalance.BossWinSureRatio) return 1.0;
            if (r < DungeonBalance.BossWinHopelessRatio) return 0.0;
            return 1.0 / (1.0 + Math.Exp(-DungeonBalance.BossWinSteepness * (r - DungeonBalance.BossWinCenterRatio)));
        }

        /// <summary>この部隊でこのボスに挑んだときの勝率（ギミック・再生・異変込み）。見立て・扉前の判断・決戦が共通で使う。</summary>
        public static double WinChance(Party party, FloorBoss boss, GameState? state) =>
            WinChance(CalculateBossPower(party, boss, state), RequiredPower(boss, state, party));

        /// <summary>
        /// 完全解析を済ませたあとの勝率の見込み（来月の作戦、§0.98）。まだ完全解析でなければ、火力に完全解析の上乗せ
        /// （FullIntelDamageBonus）を掛けて求める。完全解析済みなら WinChance と同じ。
        /// </summary>
        public static double WinChanceWithFullIntel(Party party, FloorBoss boss, GameState? state)
        {
            double power = CalculateBossPower(party, boss, state);
            if (ScoutingResolver.GetTier(boss.IntelRate) != IntelTier.Complete)
                power *= 1.0 + DungeonBalance.FullIntelDamageBonus;
            return WinChance(power, RequiredPower(boss, state, party));
        }

        /// <summary>
        /// 部隊を階層ボスへ差し向け、討伐を解決する。撃破した場合は boss.IsDefeated を立て、
        /// boss.CurrentHp を0にする（撤退時はボスのHPを元に戻し、次回は仕切り直しとする）。
        /// </summary>
        /// <param name="state">
        /// 省略可能。渡した場合、SurvivalThresholdBonus種別の研究（魂魄安定の霊香等）が
        /// 完了済みなら、完了分すべてのEffectValueを合計してHP消費率（%）から差し引く
        /// （→ Balance.ResearchBalance.GetTotalEffectValue、アルベールの研究室、ApplyHpLoss）。
        /// 迷宮の異変（→ DungeonAnomalySystem）もこれで効く。nullなら通常どおりボーナス無しで解決する。
        /// </param>
        public DungeonResult Resolve(Party party, FloorBoss boss, GameState? state = null)
        {
            if (party.Members.Count == 0)
                throw new InvalidOperationException("空のパーティはダンジョンに出せません。");

            var result = new DungeonResult();

            // 迷宮の異変「瘴気」（→ DungeonAnomalySystem、§0.64）は損耗の倍率に掛ける。
            result.DamageMultiplier = DungeonAnomalySystem.DamageMultiplier(state, boss);

            // ---- 火力判定（ボスのHPを削り切れるか） ----
            // 巨獣狩りの保有者は、どの番人にも火力の上乗せが効く（→ DungeonPowerCalculator.MemberPower、§0.138）。
            double partyPower = CalculateBossPower(party, boss, state);
            result.GiantHunterAdventurerIds.AddRange(
                party.Members.Where(m => DungeonPowerCalculator.GiantHunterApplies(m, boss)).Select(m => m.Id));
            result.FullIntelBonusApplied = ScoutingResolver.GetTier(boss.IntelRate) == IntelTier.Complete;

            result.PartyPower = partyPower;
            result.RequiredPower = RequiredPower(boss, state, party);
            // 勝ち負けは勝率で抽選する（§0.98）。必ず勝つ・必ず負けるときは乱数を引かない
            result.WinChance = WinChance(partyPower, result.RequiredPower);
            bool won = result.WinChance >= 1.0
                || (result.WinChance > 0 && _outcomeRng.NextInt(1, 10000) <= (int)Math.Round(result.WinChance * 10000));
            result.Outcome = won ? DungeonOutcome.Victory : DungeonOutcome.Retreat;

            if (result.Outcome == DungeonOutcome.Victory)
            {
                boss.CurrentHp = 0;
                boss.IsDefeated = true;
            }

            double survivalBonus = state != null
                ? ResearchBalance.GetTotalEffectValue(state, ResearchEffectType.SurvivalThresholdBonus)
                  + FacilityBalance.GetFieldAidSurvivalBonus(state.GetFacilityLevel(FacilityType.Infirmary), state.GetFacilitySpecialty(FacilityType.Infirmary)) // 医務室の戦地救護（§0.76）
                : 0;
            ApplyHpLoss(result, party, survivalBonus, state);
            RollGiantHunterAwakening(result, party);

            return result;
        }

        /// <summary>
        /// ボス討伐の要求火力＝(PartyPowerRequirementBase＋ボス階層×PartyPowerRequirementPerFloor)×フィールド倍率
        /// （→ BAL: dungeon.csv、DungeonBalance.ScaleRequirement。§0.47）。部隊に依らない素の値（再生は含まない）。
        /// </summary>
        public static double RequiredPower(FloorBoss boss) =>
            DungeonBalance.ScaleRequirement(DungeonBalance.PartyPowerRequirementBase, DungeonBalance.PartyPowerRequirementPerFloor, boss.Floor, boss.FieldOrder);

        /// <summary>
        /// 迷宮の異変（主の衰え・主の猛り、→ DungeonAnomalySystem、§0.64）を含めた要求火力。
        /// state が null なら異変なし（＝RequiredPower(boss)）。部隊に依らない（再生は含まない）。
        /// </summary>
        public static double RequiredPower(FloorBoss boss, GameState? state) =>
            RequiredPower(boss) * DungeonAnomalySystem.RequirementMultiplier(state, boss);

        /// <summary>
        /// この部隊で挑んだときの要求火力。§0.138 で再生のギミックを無くしたので、部隊に依らず異変込みの要求火力と同じ
        /// （決戦の判定・出撃前の見立て・扉前の自動判断の呼び出しを変えないために残す）。
        /// </summary>
        public static double RequiredPower(FloorBoss boss, GameState? state, Party party) => RequiredPower(boss, state);

        /// <summary>
        /// そのボスに挑んだ場合の部隊火力。Resolve の火力判定と、大迷宮画面の出撃前の見立てが共通で使う。
        ///  - 隊員ごとの火力（巨獣狩りの上乗せ込み、→ DungeonPowerCalculator.MemberPower）に相棒・険悪を掛けて合算
        ///  - 鼓舞の上乗せ（→ DungeonPowerCalculator.PartyPower と同じ）
        ///  - 完全解析：×(1＋FullIntelDamageBonus)
        /// </summary>
        public static double CalculateBossPower(Party party, FloorBoss boss, GameState? state = null)
        {
            var members = party.Members;
            double total = 0;
            foreach (var m in members)
            {
                double p = DungeonPowerCalculator.MemberPower(m, boss);
                p *= MoraleSystem.PairFactor(state, m, members); // 相棒・険悪（§0.101。state を渡したときだけ）
                total += p;
            }
            if (members.Any(m => m.HasTrait(TraitCatalog.InspiringId)))
                total *= 1.0 + TraitBalance.InspiringPartyPowerBonus;

            if (ScoutingResolver.GetTier(boss.IntelRate) == IntelTier.Complete)
                total *= 1.0 + DungeonBalance.FullIntelDamageBonus;
            return total;
        }

        /// <summary>
        /// 巨獣狩りの後天開眼（→ 03 §4.5.4・§5.3.2、2026年9月・§0.35。§0.138 でどの番人でも）。番人を**撃破**したときだけ、
        /// 生存した隊員（HP1以上＝強制除籍されていない）ごとに GiantHunterAwakeningChance でロールする。
        /// 既に巨獣狩りを持つ隊員・特性5枠満杯の隊員はロールしない（通常特性なので侵食はしない）。
        /// 乱数は対象の隊員ごとに NextInt(1, 100) を1回引き、round(確率×100) 以下で当選。
        /// </summary>
        private void RollGiantHunterAwakening(DungeonResult result, Party party)
        {
            if (result.Outcome != DungeonOutcome.Victory) return;

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
        /// 後天の障害（戦慄、§0.56）のロール。既に持っていればロールしない。乱数は NextInt(1, 100) を1回引き、
        /// round(chance×100) 以下で当選。満杯なら通常特性を侵食して付く（→ Adventurer.TryAddCurseTrait）。
        /// </summary>
        private TraitGrantEvent? RollAcquiredCurse(Adventurer member, string traitId, double chance, TraitGrantCause cause)
        {
            if (member.HasTrait(traitId)) return null;
            if (_rng.NextInt(1, 100) > (int)Math.Round(chance * 100)) return null;
            if (!member.TryAddCurseTrait(traitId, out var eroded)) return null;
            return new TraitGrantEvent(member.Id, member.Name, traitId, eroded, cause);
        }

        /// <summary>
        /// HP消費の適用。撃破できたかどうかでベースのレンジが変わり、迷宮の異変の倍率が乗る（§0.138 でギミックの損耗は無くした）。
        ///
        /// HP下限は0：0に到達した冒険者は FatalChanceFor（FatalChancePercent。危機察知は半分）の確率で強制除籍（恒久ロスト）となる
        /// （→ DungeonResult.ForceRetiredAdventurerIds。世界観上は「秘薬で一命を取り留めたが
        /// アルベールが登録を抹消した」という扱い）。外れれば HP1 で一命を取り留め、重傷になる。
        /// 撃破・撤退にかかわらず古傷のロールもある（§0.125、→ DungeonResult.NearDeathSurvivorIds）。
        ///
        /// survivalBonus（2026年9月新設、→ ResearchEffectType.SurvivalThresholdBonus）は
        /// 算出後のHP消費率（%）から%ポイントで差し引く（0未満にはしない）。
        /// </summary>
        private void ApplyHpLoss(DungeonResult result, Party party, double survivalBonus = 0, GameState? state = null)
        {
            // 神官の加護（§0.72）：損耗率を%ポイントで下げ、重傷になりにくくする（戦う前の顔ぶれで決める）。
            double blessing = ClericBlessing.LossReductionPct(party.Members);
            double severeMultiplier = ClericBlessing.SevereThresholdMultiplier(party.Members);
            result.ClericBlessingPct = blessing;

            var (minPct, maxPct) = result.Outcome == DungeonOutcome.Victory
                ? (DungeonBalance.BaseHpLossPctMin, DungeonBalance.BaseHpLossPctMax)
                : (DungeonBalance.RetreatHpLossPctMin, DungeonBalance.RetreatHpLossPctMax);

            foreach (var member in party.Members)
            {
                int lossPct = _rng.NextInt(minPct, maxPct);
                lossPct = (int)Math.Clamp(Math.Round(lossPct * result.DamageMultiplier), 0, 100);

                // 研究の生存ボーナス（部隊全員）と豪胆（本人のみ、→ TraitEffectType.SurvivalThresholdModifier、§0.53）を差し引く。
                double braveBonus = member.SumTraitEffect(TraitEffectType.SurvivalThresholdModifier);
                if (member.Fretful) lossPct = Math.Min(100, lossPct + PersonalityBalance.FretBossHpLossPct); // 焦り（§0.102）
                // 臆病・猪突猛進（§0.56）は同じ効果種別の負の値なので、ここで損耗が増える（100%で頭打ち）。
                lossPct = (int)Math.Clamp(Math.Round(lossPct - survivalBonus - braveBonus - blessing), 0, 100);

                int hpLoss = member.MaxHP * lossPct / 100;
                int newHp = Math.Max(0, member.CurrentHP - hpLoss);

                // かばい合い（§0.101）：除籍（HP0）や重傷の基準を下回りそうなとき、同じ部隊の相棒（相性80以上）が
                // GuardChancePercent の確率で損耗の GuardShareRate を引き受ける（相棒はHP1より下がらない）。乱数はかばい合い専用。
                if (state != null && hpLoss > 0
                    && newHp < member.MaxHP * CombatBalance.SevereInjuryHpThresholdPct * severeMultiplier
                    && MoraleSystem.FindGuardian(state, member, party.Members) is { } guardian
                    && _guardRng.NextInt(1, 100) <= PersonalityBalance.GuardChancePercent)
                {
                    int share = Math.Min((int)Math.Round(hpLoss * PersonalityBalance.GuardShareRate), guardian.CurrentHP - 1);
                    guardian.CurrentHP -= share;
                    hpLoss -= share;
                    newHp = Math.Max(0, member.CurrentHP - hpLoss);
                    result.GuardEvents.Add(new GuardEvent(guardian.Id, guardian.Name, member.Id, member.Name, share));
                }

                // HP0の強制除籍（§0.125）：FatalChanceFor の確率で強制除籍（危機察知は半分、§0.138）。外れれば HP1 で一命を取り留める（重傷は下で付く）。
                // 乱数は強制除籍の判定専用で、HPが0になった隊員のときだけ引く。
                bool nearDeath = false;
                if (newHp == 0 && _fatalRng.NextInt(1, 100) > FatalChanceFor(member))
                {
                    newHp = 1;
                    nearDeath = true;
                    result.NearDeathSurvivorIds.Add(member.Id);
                    if (member.HasTrait(TraitCatalog.SixthSenseId)) result.SixthSenseAdventurerIds.Add(member.Id);
                }

                result.HpLostByAdventurer[member.Id] = member.CurrentHP - newHp;
                member.CurrentHP = newHp;

                if (newHp == 0)
                    result.ForceRetiredAdventurerIds.Add(member.Id);
                // 重傷生還の古傷（→ 03 §4.3.2）：撤退で最大HPの10%以下（最低でもHP1、→ CriticalInjury.RetreatHpThreshold）
                // で生還した隊員だけロールする（撃破時は対象外）。
                else
                {
                    // 一命を取り留めた隊員（§0.125）は、撃破でもロールする。
                    if ((result.Outcome == DungeonOutcome.Retreat || nearDeath)
                        && CriticalInjury.RollOldWound(member, _rng, CriticalInjury.RetreatHpThreshold(member)) is { } grant)
                        result.TraitGrantEvents.Add(grant);
                    // 後天の障害「戦慄」（§0.56。§0.138 で、HP0から一命を取り留めた隊員に）。毒の後遺症は猛毒が無くなったので付かない。
                    if (nearDeath
                        && RollAcquiredCurse(member, TraitCatalog.DreadId, TraitBalance.DreadChance, TraitGrantCause.Dread) is { } dreadGrant)
                        result.TraitGrantEvents.Add(dreadGrant);
                    // 重傷（§0.53）：撃破・撤退とも、生き残った隊員のHPが最大HPの一定割合未満なら出撃不可の重傷になる。
                    if (CriticalInjury.TryInflictSevere(member, _rng, severeMultiplier) is { } injury)
                        result.InjuryEvents.Add(injury);
                }
            }
        }

        /// <summary>HP0になった隊員が強制除籍される確率（%）。危機察知の保有者は SixthSenseFatalChanceRate 倍（§0.138）。番人戦と迷宮の主の戦いで共通。</summary>
        public static int FatalChanceFor(Adventurer member) =>
            member.HasTrait(TraitCatalog.SixthSenseId)
                ? (int)Math.Round(CombatBalance.FatalChancePercent * TraitBalance.SixthSenseFatalChanceRate)
                : CombatBalance.FatalChancePercent;
    }
}
