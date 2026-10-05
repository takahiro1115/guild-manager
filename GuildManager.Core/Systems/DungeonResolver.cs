using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>ギミック1件への備えの段階（画面と週報の表示用。数字は出さない、→ 03 §4.2.3）。</summary>
    public enum ReadinessTier
    {
        /// <summary>無策：備えが0。</summary>
        None,
        /// <summary>一部：備えが0より大きく1未満。</summary>
        Partial,
        /// <summary>万全：備えが1。</summary>
        Full,
    }

    /// <summary>
    /// 階層ボス討伐の解決エンジン（→ ダンジョン攻略システム）。
    ///
    /// 直接指揮UIは作らない：編成を決めて送り出せば、あとは完全自動で判定し
    /// 構造化された結果（→ DungeonResult）を返す。
    ///
    /// 勝敗を分けるのは「事前にどれだけ調べ、どの隊員で備えたか」（2026年10月・§0.68）：
    ///  - 備え（→ Readiness）：ギミックごとに0〜1。そのボスで効く職業の同行で GimmickRoleReadiness（0.5）、
    ///    対策の能力の部隊合計÷基準の値を足して上限1。伝説級の装備（§0.45）があれば1。携行アイテムは撤去した。
    ///  - 備えが足りない分（不足＝1−備え）だけ、ギミックごとの罰が効く（→ BossGimmickType）：
    ///    猛毒・飛行・群れは損耗の加算、重装甲・群れ・魅了は火力の低下、再生は要求火力の上昇、即死級は損耗の下限
    ///    （不足×100%。備えが0なら全損＝戦死）。猛毒は戦闘後の毒状態も残す。
    ///  - 完全解析ボーナス：解析率1.0（→ ScoutingResolver）で与ダメージが上乗せされる。
    ///
    /// HP下限は0で、0に到達した冒険者は強制除籍（恒久ロスト）になる（→ DungeonResult.ForceRetiredAdventurerIds）。
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
        /// 迷宮の異変（→ DungeonAnomalySystem）もこれで効く。nullなら通常どおりボーナス無しで解決する。
        /// </param>
        public DungeonResult Resolve(Party party, FloorBoss boss, GameState? state = null)
        {
            if (party.Members.Count == 0)
                throw new InvalidOperationException("空のパーティはダンジョンに出せません。");

            var result = new DungeonResult();

            // ---- ギミックへの備え ----
            foreach (var gimmick in boss.Gimmicks)
            {
                double readiness = Readiness(gimmick, party.Members);
                result.Readiness[gimmick.Type] = readiness;
                if (readiness >= 1.0)
                    result.CounteredGimmicks.Add(gimmick.Type);
                else
                    result.UncounteredGimmicks.Add(gimmick.Type);
            }

            // 迷宮の異変「瘴気」（→ DungeonAnomalySystem、§0.64）は損耗の倍率に掛ける。
            result.DamageMultiplier = CalculateDamageMultiplier(boss, result, party) * DungeonAnomalySystem.DamageMultiplier(state, boss);

            // ---- 火力判定（ボスのHPを削り切れるか） ----
            // 重装甲ボスなら巨獣狩りの保有者の火力に上乗せが効く（→ DungeonPowerCalculator.MemberPower）。
            // 重装甲・群れ・魅了の備えが足りなければ火力が下がり、再生の備えが足りなければ要求火力が上がる。
            double partyPower = CalculateBossPower(party, boss);
            result.GiantHunterAdventurerIds.AddRange(
                party.Members.Where(m => DungeonPowerCalculator.GiantHunterApplies(m, boss)).Select(m => m.Id));
            result.FullIntelBonusApplied = ScoutingResolver.GetTier(boss.IntelRate) == IntelTier.Complete;
            if (Shortfall(boss, BossGimmickType.Charm, party.Members) > 0)
                result.CharmedAdventurerId = CharmTarget(party.Members, boss)?.Id;

            result.PartyPower = partyPower;
            result.RequiredPower = RequiredPower(boss, state, party);
            result.Outcome = partyPower >= result.RequiredPower ? DungeonOutcome.Victory : DungeonOutcome.Retreat;

            if (result.Outcome == DungeonOutcome.Victory)
            {
                boss.CurrentHp = 0;
                boss.IsDefeated = true;
            }

            double survivalBonus = state != null
                ? ResearchBalance.GetTotalEffectValue(state, ResearchEffectType.SurvivalThresholdBonus)
                : 0;
            ApplyHpLoss(result, party, boss, survivalBonus);
            ApplyPoisonStatus(result, party, boss);
            RollGiantHunterAwakening(result, party, boss);

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
        /// この部隊で挑んだときの要求火力（§0.68）：異変込みの要求火力に、再生の備えの不足分
        /// ×RegenerationRequirementBonus を上乗せする。決戦の判定・出撃前の見立て・扉前の自動判断はこちらを使う。
        /// </summary>
        public static double RequiredPower(FloorBoss boss, GameState? state, Party party) =>
            RequiredPower(boss, state) * (1.0 + BossGimmickBalance.RegenerationRequirementBonus * Shortfall(boss, BossGimmickType.Regeneration, party.Members));

        /// <summary>
        /// そのボスに挑んだ場合の部隊火力。Resolve の火力判定と、大迷宮画面の出撃前の見立てが共通で使う。
        ///  - 隊員ごとの火力（巨獣狩りの上乗せ込み、→ DungeonPowerCalculator.MemberPower）に、
        ///    群れ（後衛×(1−SwarmRearPowerPenalty×不足)）と魅了（一番火力の高い隊員×備え）を掛けて合算
        ///  - 鼓舞の上乗せ（→ DungeonPowerCalculator.PartyPower と同じ）
        ///  - 重装甲：×(1−HeavyArmorPowerPenalty×不足)
        ///  - 完全解析：×(1＋FullIntelDamageBonus)
        /// </summary>
        public static double CalculateBossPower(Party party, FloorBoss boss)
        {
            var members = party.Members;
            double swarm = Shortfall(boss, BossGimmickType.Swarm, members);
            double charm = Shortfall(boss, BossGimmickType.Charm, members);
            var charmed = charm > 0 ? CharmTarget(members, boss) : null;

            double total = 0;
            foreach (var m in members)
            {
                double p = DungeonPowerCalculator.MemberPower(m, boss);
                if (swarm > 0 && PlacementRules.GetDefault(m.JobClass) == Placement.Back)
                    p *= 1.0 - BossGimmickBalance.SwarmRearPowerPenalty * swarm;
                if (m == charmed)
                    p *= 1.0 - charm;
                total += p;
            }
            if (members.Any(m => m.HasTrait(TraitCatalog.InspiringId)))
                total *= 1.0 + TraitBalance.InspiringPartyPowerBonus;

            total *= 1.0 - BossGimmickBalance.HeavyArmorPowerPenalty * Shortfall(boss, BossGimmickType.HeavyArmor, members);
            if (ScoutingResolver.GetTier(boss.IntelRate) == IntelTier.Complete)
                total *= 1.0 + DungeonBalance.FullIntelDamageBonus;
            return total;
        }

        /// <summary>
        /// ギミック1件への部隊の備え（0〜1、§0.68）。伝説級の装備（§0.45、→ Adventurer.CountersGimmickByEquipment）を
        /// 着た隊員がいれば1。それ以外は、そのボスで効く職業（→ BossGimmick.CounterRoles）が1人でも同行していれば
        /// GimmickRoleReadiness、対策の能力の部隊合計÷RequiredCounterStatThreshold を足して上限1。
        /// 対策口が1つも無いギミック（定義の漏れ）は、伝説級の装備が無い限り0（黙って握り潰さない）。
        /// public static にしてあるのは編成画面・大迷宮画面の見立てとテストから直接使えるようにするため。
        /// </summary>
        public static double Readiness(BossGimmick gimmick, IEnumerable<Adventurer> members)
        {
            var list = members as IReadOnlyCollection<Adventurer> ?? members.ToList();
            if (list.Any(m => m.CountersGimmickByEquipment(gimmick.Type)))
                return 1.0;

            double readiness = 0;
            if (gimmick.CounterRoles.Count > 0 && list.Any(m => gimmick.CounterRoles.Contains(m.JobClass)))
                readiness += BossGimmickBalance.GimmickRoleReadiness;
            if (!string.IsNullOrEmpty(gimmick.RequiredCounterStat) && gimmick.RequiredCounterStatThreshold > 0)
                readiness += list.Sum(m => m.GetEffectiveStat(gimmick.RequiredCounterStat!)) / gimmick.RequiredCounterStatThreshold;
            return Math.Clamp(readiness, 0.0, 1.0);
        }

        /// <summary>備えの段階（画面・週報の表示用）。</summary>
        public static ReadinessTier GetReadinessTier(double readiness) =>
            readiness >= 1.0 ? ReadinessTier.Full : readiness > 0 ? ReadinessTier.Partial : ReadinessTier.None;

        /// <summary>そのボスの指定の種類のギミックへの備えの不足（1−備え）。ボスがその種類を持たなければ0。</summary>
        public static double Shortfall(FloorBoss boss, BossGimmickType type, IEnumerable<Adventurer> members)
        {
            var gimmick = boss.Gimmicks.FirstOrDefault(g => g.Type == type);
            return gimmick == null ? 0 : 1.0 - Readiness(gimmick, members);
        }

        /// <summary>魅了で操られる隊員＝部隊で一番火力の高い隊員（同じなら並びが先の隊員）。</summary>
        public static Adventurer? CharmTarget(IEnumerable<Adventurer> members, FloorBoss boss)
        {
            Adventurer? best = null;
            double bestPower = double.MinValue;
            foreach (var m in members)
            {
                double p = DungeonPowerCalculator.MemberPower(m, boss);
                if (p > bestPower) { best = m; bestPower = p; }
            }
            return best;
        }

        /// <summary>
        /// 備えの不足による被ダメージ倍率（1.0＝加算なし）。損耗が増える型（猛毒・飛行・群れ）だけが加算する：
        /// 危険度×UncounteredDamageMultiplierPerDangerLevel×不足（群れはさらに×SwarmDamageFactor）。
        /// 耐毒体質（猛毒）・鷹の目（飛行）の保有者が1人でもいれば、その加算を軽減する（→ 03 §4.5.4・§5.3.2）。
        /// 即死級は倍率ではなく損耗の下限（→ ApplyHpLoss）、重装甲・再生・魅了は火力・要求火力で効く。
        /// </summary>
        private static double CalculateDamageMultiplier(FloorBoss boss, DungeonResult result, Party party)
        {
            double multiplier = 1.0;
            bool resistPoison = party.Members.Any(m => m.HasTrait(TraitCatalog.ResistPoisonId));
            bool hawkEye = party.Members.Any(m => m.HasTrait(TraitCatalog.HawkEyeId));

            foreach (var gimmick in boss.Gimmicks)
            {
                double shortfall = 1.0 - result.Readiness[gimmick.Type];
                if (shortfall <= 0) continue;

                double term = gimmick.DangerLevel * DungeonBalance.UncounteredDamageMultiplierPerDangerLevel * shortfall;
                switch (gimmick.Type)
                {
                    case BossGimmickType.Poison:
                        if (resistPoison)
                        {
                            term *= 1.0 - CombatBalance.ResistPoisonDamageReductionRate;
                            result.ResistPoisonApplied = true;
                        }
                        break;
                    case BossGimmickType.Flying:
                        // 鷹の目（§0.55）：耐毒体質の飛行版。
                        if (hawkEye)
                        {
                            term *= 1.0 - TraitBalance.HawkEyeFlyingDamageReductionRate;
                            result.HawkEyeApplied = true;
                        }
                        break;
                    case BossGimmickType.Swarm:
                        term *= BossGimmickBalance.SwarmDamageFactor;
                        break;
                    default:
                        continue; // 損耗の倍率には効かない種類
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
        /// 後天の障害（毒の後遺症・戦慄、§0.56）のロール。既に持っていればロールしない。乱数は NextInt(1, 100) を1回引き、
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
        /// HP消費の適用。撃破できたかどうかでベースのレンジが変わり、備えの不足による倍率が乗る（§0.68）：
        ///  - 即死級：損耗率の下限が InstantKillUncounteredHpLossPct×不足（備え0なら全損）。危機察知の保有者は
        ///    SixthSenseInstantKillHpLossPct で止まる
        ///  - 魅了：操られた隊員の損耗に CharmExtraHpLossPct×不足を足す
        ///
        /// HP下限は0：0に到達した冒険者は強制除籍（恒久ロスト）となる
        /// （→ DungeonResult.ForceRetiredAdventurerIds。世界観上は「秘薬で一命を取り留めたが
        /// アルベールが登録を抹消した」という扱い）。
        ///
        /// survivalBonus（2026年9月新設、→ ResearchEffectType.SurvivalThresholdBonus）は
        /// 算出後のHP消費率（%）から%ポイントで差し引く（0未満にはしない）。即死級の全損（100%）にも
        /// 適用されるため、事故を軽傷へ和らげる効果として働く。
        /// </summary>
        private void ApplyHpLoss(DungeonResult result, Party party, FloorBoss boss, double survivalBonus = 0)
        {
            double instantKill = result.ShortfallOf(BossGimmickType.InstantKill);
            double charm = result.ShortfallOf(BossGimmickType.Charm);
            double poison = result.ShortfallOf(BossGimmickType.Poison);

            var (minPct, maxPct) = result.Outcome == DungeonOutcome.Victory
                ? (DungeonBalance.BaseHpLossPctMin, DungeonBalance.BaseHpLossPctMax)
                : (DungeonBalance.RetreatHpLossPctMin, DungeonBalance.RetreatHpLossPctMax);

            foreach (var member in party.Members)
            {
                int lossPct = _rng.NextInt(minPct, maxPct);
                lossPct = (int)Math.Clamp(Math.Round(lossPct * result.DamageMultiplier), 0, 100);

                if (instantKill > 0)
                {
                    // 即死級は備えの不足が損耗の下限になる（備え0なら全損＝戦死）。
                    // 危機察知（§0.55）の保有者だけは SixthSenseInstantKillHpLossPct で止まる。
                    int floorPct = (int)Math.Round(DungeonBalance.InstantKillUncounteredHpLossPct * instantKill);
                    if (member.HasTrait(TraitCatalog.SixthSenseId) && floorPct > TraitBalance.SixthSenseInstantKillHpLossPct)
                    {
                        floorPct = TraitBalance.SixthSenseInstantKillHpLossPct;
                        result.SixthSenseAdventurerIds.Add(member.Id);
                    }
                    lossPct = Math.Max(lossPct, floorPct);
                }
                if (charm > 0 && member.Id == result.CharmedAdventurerId)
                    lossPct = Math.Min(100, lossPct + (int)Math.Round(BossGimmickBalance.CharmExtraHpLossPct * charm));

                // 研究の生存ボーナス（部隊全員）と豪胆（本人のみ、→ TraitEffectType.SurvivalThresholdModifier、§0.53）を差し引く。
                double braveBonus = member.SumTraitEffect(TraitEffectType.SurvivalThresholdModifier);
                // 臆病・猪突猛進（§0.56）は同じ効果種別の負の値なので、ここで損耗が増える（100%で頭打ち）。
                lossPct = (int)Math.Clamp(lossPct - survivalBonus - braveBonus, 0, 100);

                int hpLoss = member.MaxHP * lossPct / 100;
                int newHp = Math.Max(0, member.CurrentHP - hpLoss);

                result.HpLostByAdventurer[member.Id] = member.CurrentHP - newHp;
                member.CurrentHP = newHp;

                if (newHp == 0)
                    result.ForceRetiredAdventurerIds.Add(member.Id);
                // 重傷生還の古傷（→ 03 §4.3.2）：撤退で最大HPの10%以下（最低でもHP1、→ CriticalInjury.RetreatHpThreshold）
                // で生還した隊員だけロールする（撃破時は対象外）。
                else
                {
                    if (result.Outcome == DungeonOutcome.Retreat
                        && CriticalInjury.RollOldWound(member, _rng, CriticalInjury.RetreatHpThreshold(member)) is { } grant)
                        result.TraitGrantEvents.Add(grant);
                    // 後天の障害（§0.56）：猛毒に備えが足りずHPが大きく削れた生還者に毒の後遺症、
                    // 即死級に備えが足りず生き延びた隊員に戦慄。確率は備えの不足に比例する（§0.68）。
                    if (poison > 0
                        && member.CurrentHP < member.MaxHP * TraitBalance.PoisonAftereffectHpThresholdPct
                        && RollAcquiredCurse(member, TraitCatalog.PoisonAftereffectId, TraitBalance.PoisonAftereffectChance * poison, TraitGrantCause.PoisonAftereffect) is { } poisonGrant)
                        result.TraitGrantEvents.Add(poisonGrant);
                    if (instantKill > 0
                        && RollAcquiredCurse(member, TraitCatalog.DreadId, TraitBalance.DreadChance * instantKill, TraitGrantCause.Dread) is { } dreadGrant)
                        result.TraitGrantEvents.Add(dreadGrant);
                    // 重傷（§0.53）：撃破・撤退とも、生き残った隊員のHPが最大HPの一定割合未満なら出撃不可の重傷になる。
                    if (CriticalInjury.TryInflictSevere(member, _rng) is { } injury)
                        result.InjuryEvents.Add(injury);
                }
            }
        }

        /// <summary>
        /// 毒状態（§0.68）：猛毒に備えが足りないまま戦って生還した隊員は、全能力（素の値）が
        /// PoisonStatusStatPenalty×不足だけ下がった状態が PoisonStatusWeeks 週続く（耐毒体質の本人は ResistPoisonStatusWeeksRate 倍、
        /// 切り上げ・最低1週）。すでに毒状態なら、低下率・残り週数とも大きい方を残す。乱数は使わない。
        /// </summary>
        private static void ApplyPoisonStatus(DungeonResult result, Party party, FloorBoss boss)
        {
            double poison = result.ShortfallOf(BossGimmickType.Poison);
            if (poison <= 0) return;

            double penalty = BossGimmickBalance.PoisonStatusStatPenalty * poison;
            result.PoisonStatPenalty = penalty;
            foreach (var member in party.Members.Where(m => m.CurrentHP > 0))
            {
                int weeks = BossGimmickBalance.PoisonStatusWeeks;
                if (member.HasTrait(TraitCatalog.ResistPoisonId))
                    weeks = Math.Max(1, (int)Math.Ceiling(weeks * BossGimmickBalance.ResistPoisonStatusWeeksRate));

                member.PoisonWeeksRemaining = Math.Max(member.PoisonWeeksRemaining, weeks);
                member.PoisonStatPenalty = Math.Max(member.PoisonStatPenalty, penalty);
                result.PoisonWeeksByAdventurer[member.Id] = weeks;
            }
        }
    }
}
