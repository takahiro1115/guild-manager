using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>秘薬を処方できるかの判定結果（→ SoulFusionSystem.CheckPrescription）。Ok 以外は処方できない理由。</summary>
    public enum SoulFusionCheck
    {
        Ok,
        /// <summary>研究「魂魄融和の秘薬」が済んでいない。</summary>
        NotUnlocked,
        /// <summary>培養槽が埋まっている。</summary>
        TankBusy,
        /// <summary>同じ人・ギルドにいない（除籍者）・既に親になった・相性が足りない。</summary>
        NotEligiblePair,
        /// <summary>子の職業がどちらの親の職業でもない。</summary>
        InvalidJob,
        /// <summary>触媒にならない素材を指定した。</summary>
        UnknownCatalyst,
        NotEnoughGold,
        NotEnoughCatalyst,
    }

    /// <summary>
    /// 魂魄融和の秘薬（女性同士の配合・継承。→ 03 §5.4・§0.58、2026年10月新設）。
    ///
    /// アルベールがギルドを作った理由である秘薬。相性が最大（→ soul_fusion.csv RequiredCompatibility）に達したペアに処方すると、
    /// 2人の素質を掛け合わせた娘が培養槽で育ち、CultureWeeks 週後に18歳の新人として加わる。
    ///  - 解禁：研究「魂魄融和の秘薬」（→ ResearchIds.SoulFusion）
    ///  - 親：ギルドにいる者（現役・引退者。除籍者は不可）。1人が親になれるのは生涯1回まで（→ Adventurer.HasUsedSoulFusion）。
    ///    培養中も親は出撃できる
    ///  - 費用：PrescriptionGold と、任意で触媒の素材1種（→ soul_fusion_catalysts.csv）
    ///  - 子：職業は処方時にどちらかの親のものを選ぶ。PA は能力ごとに両親の平均＋乱数（100で止める）。
    ///    百合相性（→ GetNyxTier）に応じた確率で能力限界突破が起き、最も高い能力のPAが100を超えうる（上限 BreakthroughPaCap）。
    ///    特性は親から確率で受け継ぎ（障害は受け継がない）、そのうえで採用と同じ先天判定を行う
    ///
    /// 子は処方した時点で生成して培養槽（→ GameState.SoulFusionCultures）に入れ、誕生は週次決算で行う（→ ProcessWeeklyCultures）。
    /// </summary>
    public class SoulFusionSystem
    {
        private readonly IRng _rng;
        private readonly IRng _portraitRng;

        /// <summary>portraitRng を省略したときの既定シード（→ RecruitmentSystem と同じく、顔の抽選は能力値と系列を分ける）。</summary>
        public const int DefaultPortraitSeed = 5821;

        public SoulFusionSystem(IRng rng, IRng? portraitRng = null)
        {
            _rng = rng;
            _portraitRng = portraitRng ?? new SeededRng(DefaultPortraitSeed);
        }

        // ==================== 判定（乱数なし） ====================

        /// <summary>研究で培養槽が開いているか。</summary>
        public static bool IsUnlocked(GameState state) => state.IsResearchCompleted(ResearchIds.SoulFusion);

        /// <summary>培養槽に空きがあるか。</summary>
        public static bool HasFreeTank(GameState state) => state.SoulFusionCultures.Count < SoulFusionBalance.CultureTankCount;

        /// <summary>ギルドにいる者（現役＋引退者）。除籍者は親になれない。</summary>
        public static IEnumerable<Adventurer> GuildMembers(GameState state) => state.Adventurers.Concat(state.RetiredAdventurers);

        /// <summary>
        /// 2人が親のペアになれるか：別人で、2人ともギルドにいて（→ GuildMembers）、どちらもまだ親になっておらず、
        /// 相性が RequiredCompatibility 以上。研究・培養槽・費用は見ない（→ CheckPrescription）。
        /// </summary>
        public static bool IsEligiblePair(GameState state, Adventurer a, Adventurer b)
        {
            if (a.Id == b.Id) return false;
            if (a.HasUsedSoulFusion || b.HasUsedSoulFusion) return false;
            var members = GuildMembers(state).ToList();
            if (!members.Contains(a) || !members.Contains(b)) return false;
            return CompatibilitySystem.GetCompatibility(state, a.Id, b.Id) >= SoulFusionBalance.RequiredCompatibility;
        }

        /// <summary>親になれるペアの一覧（ギルドの並び順：現役→引退者）。画面の候補に使う。</summary>
        public static List<(Adventurer A, Adventurer B)> GetEligiblePairs(GameState state)
        {
            var members = GuildMembers(state).Where(m => !m.HasUsedSoulFusion).ToList();
            var pairs = new List<(Adventurer, Adventurer)>();
            for (int i = 0; i < members.Count; i++)
                for (int j = i + 1; j < members.Count; j++)
                    if (CompatibilitySystem.GetCompatibility(state, members[i].Id, members[j].Id) >= SoulFusionBalance.RequiredCompatibility)
                        pairs.Add((members[i], members[j]));
            return pairs;
        }

        /// <summary>
        /// 百合相性（ニックス）の段階：重戦士か騎士×神官＝運命の一対、前衛×後衛（→ PlacementRules）＝好一対、それ以外＝ふつう。
        /// 構造値のためCSV化しない（確率だけ soul_fusion.csv に置く）。
        /// </summary>
        public static SoulFusionNyxTier GetNyxTier(JobClass a, JobClass b)
        {
            static bool IsHero(JobClass j) => j == JobClass.Warrior || j == JobClass.Knight;
            if ((IsHero(a) && b == JobClass.Cleric) || (IsHero(b) && a == JobClass.Cleric))
                return SoulFusionNyxTier.Destined;
            if (PlacementRules.GetDefault(a) != PlacementRules.GetDefault(b))
                return SoulFusionNyxTier.Complementary;
            return SoulFusionNyxTier.Ordinary;
        }

        /// <summary>百合相性の表示名。</summary>
        public static string GetNyxTierLabel(SoulFusionNyxTier tier) => tier switch
        {
            SoulFusionNyxTier.Destined => "運命の一対",
            SoulFusionNyxTier.Complementary => "好一対",
            _ => "ふつう",
        };

        /// <summary>能力限界突破の確率（%）：百合相性の段階ごとの値＋触媒の加算（0〜100に収める）。</summary>
        public static int GetBreakthroughChance(SoulFusionNyxTier tier, string? catalystMaterialId)
        {
            int baseChance = tier switch
            {
                SoulFusionNyxTier.Destined => SoulFusionBalance.BreakthroughChanceDestined,
                SoulFusionNyxTier.Complementary => SoulFusionBalance.BreakthroughChanceComplementary,
                _ => SoulFusionBalance.BreakthroughChanceOrdinary,
            };
            return Math.Clamp(baseChance + CatalystBonus(catalystMaterialId, SoulFusionCatalystEffect.BreakthroughChanceBonus), 0, 100);
        }

        /// <summary>触媒が指定の効果を持っていればその値、無ければ0。</summary>
        public static int CatalystBonus(string? catalystMaterialId, SoulFusionCatalystEffect effect)
        {
            var catalyst = SoulFusionBalance.FindCatalyst(catalystMaterialId);
            return catalyst != null && catalyst.EffectType == effect ? catalyst.EffectValue : 0;
        }

        /// <summary>処方できるか（理由つき）。catalystMaterialId は null＝触媒なし。</summary>
        public static SoulFusionCheck CheckPrescription(GameState state, Adventurer a, Adventurer b, JobClass childJob, string? catalystMaterialId)
        {
            if (!IsUnlocked(state)) return SoulFusionCheck.NotUnlocked;
            if (!HasFreeTank(state)) return SoulFusionCheck.TankBusy;
            if (!IsEligiblePair(state, a, b)) return SoulFusionCheck.NotEligiblePair;
            if (childJob != a.JobClass && childJob != b.JobClass) return SoulFusionCheck.InvalidJob;

            SoulFusionCatalyst? catalyst = null;
            if (catalystMaterialId != null)
            {
                catalyst = SoulFusionBalance.FindCatalyst(catalystMaterialId);
                if (catalyst == null) return SoulFusionCheck.UnknownCatalyst;
            }

            if (state.Gold < SoulFusionBalance.PrescriptionGold) return SoulFusionCheck.NotEnoughGold;
            if (catalyst != null && (state.Materials.TryGetValue(catalyst.MaterialId, out int have) ? have : 0) < catalyst.Count)
                return SoulFusionCheck.NotEnoughCatalyst;
            return SoulFusionCheck.Ok;
        }

        // ==================== 処方（乱数あり） ====================

        /// <summary>
        /// 秘薬を処方する。CheckPrescription が Ok でなければ何もせず null。成功すると費用と触媒を引き、2人を「親になった」にし、
        /// 子を生成して培養槽へ入れる（→ CreateChild）。返り値は入れた培養の記録。
        /// </summary>
        public SoulFusionCulture? TryPrescribe(GameState state, Adventurer a, Adventurer b, JobClass childJob, string? catalystMaterialId = null)
        {
            if (CheckPrescription(state, a, b, childJob, catalystMaterialId) != SoulFusionCheck.Ok)
                return null;

            state.Gold -= SoulFusionBalance.PrescriptionGold;
            var catalyst = SoulFusionBalance.FindCatalyst(catalystMaterialId);
            if (catalyst != null)
                state.Materials[catalyst.MaterialId] -= catalyst.Count;
            a.HasUsedSoulFusion = true;
            b.HasUsedSoulFusion = true;

            var culture = CreateChild(state, a, b, childJob, catalyst?.MaterialId);
            state.SoulFusionCultures.Add(culture);
            return culture;
        }

        /// <summary>
        /// 子を生成する（状態は変えない。TryPrescribe から呼ぶ。テスト用に公開）。乱数を引く順：
        /// 名前（文化圏・名）→ 能力ごとのばらつき（STR, AGI, VIT, MND, DEX, LDR, INT）→ 限界突破の判定（→ 当たれば上乗せ量）
        /// → 特性の継承（親A の枠順、親B の枠順で1つずつ）→ 先天判定（→ RecruitmentSystem.RollInnateTraits）。顔は別の乱数。
        /// </summary>
        public SoulFusionCulture CreateChild(GameState state, Adventurer a, Adventurer b, JobClass childJob, string? catalystMaterialId)
        {
            var existingNames = new HashSet<string>(state.Adventurers.Select(x => x.Name)
                .Concat(state.SoulFusionCultures.Select(c => c.Child.Name)));
            var culture = NameGenerator.RollCulture(_rng);
            var child = new Adventurer
            {
                Name = NameGenerator.GenerateUniqueFirstName(culture, existingNames, _rng),
                Gender = Gender.Female,
                Age = RecruitmentBalance.MinCandidateAge,
                JobClass = childJob,
                Placement = PlacementRules.GetDefault(childJob),
                ParentIds = new List<Guid> { a.Id, b.Id },
            };

            // ---- 能力の上限（PA）：両親の平均＋ばらつき。限界突破しない能力は100で止める ----
            int varianceMin = Math.Min(SoulFusionBalance.PaVarianceMax,
                SoulFusionBalance.PaVarianceMin + CatalystBonus(catalystMaterialId, SoulFusionCatalystEffect.PaVarianceMinBonus));
            var rawPa = new Dictionary<string, int>();
            foreach (var stat in AdventurerStatAccessor.AllStatNames)
            {
                double average = (AdventurerStatAccessor.GetPa(a, stat) + AdventurerStatAccessor.GetPa(b, stat)) / 2.0;
                rawPa[stat] = (int)Math.Round(average, MidpointRounding.AwayFromZero) + _rng.NextInt(varianceMin, SoulFusionBalance.PaVarianceMax);
            }

            var tier = GetNyxTier(a.JobClass, b.JobClass);
            var breakthroughStats = new List<string>();
            if (_rng.NextInt(1, 100) <= GetBreakthroughChance(tier, catalystMaterialId))
            {
                // 最も高い能力（同値なら AllStatNames の順で先のもの）の上限を外して上乗せする。
                string best = AdventurerStatAccessor.AllStatNames.OrderByDescending(s => rawPa[s]).First();
                rawPa[best] = Math.Min(SoulFusionBalance.BreakthroughPaCap,
                    rawPa[best] + _rng.NextInt(SoulFusionBalance.BreakthroughBonusMin, SoulFusionBalance.BreakthroughBonusMax));
                breakthroughStats.Add(best);
            }

            foreach (var stat in AdventurerStatAccessor.AllStatNames)
            {
                int cap = breakthroughStats.Contains(stat) ? SoulFusionBalance.BreakthroughPaCap : SoulFusionBalance.NormalPaCap;
                int pa = Math.Clamp(rawPa[stat], 1, cap);
                AdventurerStatAccessor.SetPa(child, stat, pa);
                AdventurerStatAccessor.SetStat(child, stat, Math.Max(1, (int)(pa * RecruitmentBalance.YoungestGrowthRatio)));
            }

            // ---- 特性：親から受け継ぎ、そのうえで採用と同じ先天判定 ----
            foreach (var traitId in a.TraitIds.Concat(b.TraitIds).ToList())
            {
                int chance = InheritChance(traitId, catalystMaterialId);
                if (chance <= 0) continue;
                if (_rng.NextInt(1, 100) <= chance)
                    child.TryAddTrait(traitId);
            }
            RecruitmentSystem.RollInnateTraits(child, _rng);
            RecruitmentSystem.FinishNewcomer(child);

            AssignPortrait(state, child, a, b);

            return new SoulFusionCulture
            {
                ParentAId = a.Id,
                ParentBId = b.Id,
                Child = child,
                WeeksRemaining = SoulFusionBalance.CultureWeeks,
                CatalystMaterialId = catalystMaterialId,
                NyxTier = tier,
                BreakthroughStats = breakthroughStats,
            };
        }

        /// <summary>
        /// 親の特性1つを子が受け継ぐ確率（%）。障害特性とカタログに無い特性は0（受け継がない＝乱数も引かない）。
        /// レア特性・欠点・それ以外の長所で確率が分かれ、触媒の加算を足す（0〜100に収める）。
        /// </summary>
        public static int InheritChance(string traitId, string? catalystMaterialId)
        {
            var def = TraitCatalog.FindById(traitId);
            if (def == null || def.IsCurseOrInjury) return 0;
            int chance = def.IsRare
                ? SoulFusionBalance.RareTraitInheritChancePercent + CatalystBonus(catalystMaterialId, SoulFusionCatalystEffect.RareTraitInheritBonus)
                : def.IsFlaw
                    ? SoulFusionBalance.FlawInheritChancePercent + CatalystBonus(catalystMaterialId, SoulFusionCatalystEffect.FlawInheritBonus)
                    : SoulFusionBalance.TraitInheritChancePercent + CatalystBonus(catalystMaterialId, SoulFusionCatalystEffect.TraitInheritBonus);
            return Math.Clamp(chance, 0, 100);
        }

        /// <summary>
        /// 子に顔グラフィックを割り当てる（→ PortraitBalance）。優先順は
        /// ①誰も使っていない・職業に似合う・親の髪色か瞳の色を受け継ぐ → ②誰も使っていない・親の色を受け継ぐ →
        /// ③誰も使っていない・職業に似合う → ④誰も使っていない → ⑤職業に似合う → ⑥プール全体。
        /// 「使っている」は現役ロースターと培養中の子。プールが空なら何もしない（シルエット）。乱数は候補があるときだけ1回引く。
        /// </summary>
        private void AssignPortrait(GameState state, Adventurer child, Adventurer a, Adventurer b)
        {
            var pool = PortraitBalance.All;
            if (pool.Count == 0) return;

            var used = state.Adventurers.Where(x => !x.IsRetired && x.PortraitId != null).Select(x => x.PortraitId!)
                .Concat(state.SoulFusionCultures.Where(c => c.Child.PortraitId != null).Select(c => c.Child.PortraitId!))
                .ToHashSet();
            var parentPortraits = new[] { a, b }.Select(p => PortraitBalance.FindById(p.PortraitId)).OfType<PortraitDefinition>().ToList();
            bool Inherits(PortraitDefinition p) => parentPortraits.Any(pp => pp.HairColor == p.HairColor || pp.EyeColor == p.EyeColor);

            var unused = pool.Where(p => !used.Contains(p.Id)).ToList();
            var candidates = new[]
                {
                    unused.Where(p => p.Suits(child.JobClass) && Inherits(p)).ToList(),
                    unused.Where(Inherits).ToList(),
                    unused.Where(p => p.Suits(child.JobClass)).ToList(),
                    unused,
                    pool.Where(p => p.Suits(child.JobClass)).ToList(),
                    pool.ToList(),
                }
                .First(list => list.Count > 0);

            child.PortraitId = candidates[_portraitRng.NextInt(0, candidates.Count - 1)].Id;
        }

        // ==================== 週次決算（乱数なし） ====================

        /// <summary>
        /// 培養を1週進める（→ WeekProcessingSystem）。残り週数を1減らし（0未満にはしない）、0の子は宿舎に空きがあれば
        /// 処方の古い順に誕生させる：職業の初期装備を着せてロースターへ加える。空きが無ければ培養槽で待つ。
        /// 誕生した培養の記録を返す（週報用）。
        /// </summary>
        public static List<SoulFusionCulture> ProcessWeeklyCultures(GameState state)
        {
            var born = new List<SoulFusionCulture>();
            foreach (var culture in state.SoulFusionCultures.ToList())
            {
                if (culture.WeeksRemaining > 0)
                    culture.WeeksRemaining--;
                if (culture.WeeksRemaining > 0 || RecruitmentSystem.CountOpenSlots(state) <= 0)
                    continue;

                StarterEquipment.Equip(culture.Child);
                state.Adventurers.Add(culture.Child);
                state.SoulFusionCultures.Remove(culture);
                born.Add(culture);
            }
            return born;
        }

        /// <summary>培養の残り週数が0で、宿舎の空きを待っているか（画面の表示用）。</summary>
        public static bool IsWaitingForRoom(SoulFusionCulture culture) => culture.WeeksRemaining <= 0;
    }
}
