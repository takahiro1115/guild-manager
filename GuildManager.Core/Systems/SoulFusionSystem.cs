using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>培養槽の娘に育て方を1回反映した記録（§0.118。月報へ）。Gains は伸びた能力と量。</summary>
    public sealed record NurtureEvent(SoulFusionCulture Culture, NurtureKind Kind, IReadOnlyDictionary<string, int> Gains);

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
    /// 2人の素質を掛け合わせた娘が培養槽で育ち、CultureWeeks 週（2年、§0.118）後に16歳の新人として加わる。その間、季節ごとに選んだ育て方が反映される（→ ApplyNurture）。
    ///  - 解禁：研究「魂魄融和の秘薬」（→ ResearchIds.SoulFusion）
    ///  - 親：現役の子と、役職（教官・顧問・店の働き手）についている引退者（§0.133・§0.134。ほかの引退者・除籍者は不可）。何度でも、どの相手とでも親になれる
    ///    （ParentOnlyOnce が 1 なら生涯1回まで、→ Adventurer.HasUsedSoulFusion）。培養中も親は出撃できる
    ///  - 世代と血の蓄え（§0.133）：娘の世代は親の大きいほう＋1。素質（PA）の上限は世代ごとに GenerationPaCapStep ずつ上がる（→ PaCapOf）。
    ///    近い血（親子・姉妹・祖母と孫・おばと姪・いとこ）で作ると、娘の素質が下がる（→ KinshipOf）
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

        /// <summary>培養槽の数：soul_fusion.csv CultureTankCount＋段階研究（→ ResearchEffectType.CultureTankBonus、§0.60）。</summary>
        public static int GetTankCount(GameState state) =>
            SoulFusionBalance.CultureTankCount + (int)Math.Round(ResearchBalance.GetTotalEffectValue(state, ResearchEffectType.CultureTankBonus));

        /// <summary>培養槽に空きがあるか。</summary>
        public static bool HasFreeTank(GameState state) => state.SoulFusionCultures.Count < GetTankCount(state);

        /// <summary>
        /// 娘のPAの基準（能力ごと、ばらつきの前）：両親のうち高い方×HigherParentWeight＋低い方×(1−HigherParentWeight)、四捨五入（§0.60）。
        /// 高い方へ寄せるので、強い親から生まれた娘を親にすると、血統が世代ごとに伸びる。
        /// </summary>
        public static int BasePa(int paA, int paB)
        {
            double w = SoulFusionBalance.HigherParentWeight;
            return (int)Math.Round(Math.Max(paA, paB) * w + Math.Min(paA, paB) * (1 - w), MidpointRounding.AwayFromZero);
        }

        /// <summary>段階研究（→ ResearchEffectType.SoulFusionPaBonus、§0.60）による娘のPAの上乗せ（pt）。</summary>
        public static int GetResearchPaBonus(GameState state) =>
            (int)Math.Round(ResearchBalance.GetTotalEffectValue(state, ResearchEffectType.SoulFusionPaBonus));

        /// <summary>
        /// 親になれる者：現役の子と、役職（教官・顧問・店の働き手）についてギルドに残っている引退者（§0.133・§0.134。ギルドを去った引退者・除籍者は親になれない）。
        /// 引退した子の相性は引退したときのまま（任務を共にしないので上がらない）。
        /// </summary>
        public static IEnumerable<Adventurer> GuildMembers(GameState state)
        {
            var appointed = GuildPolicySystem.AppointedIds(state);
            return state.Adventurers.Where(a => !a.NeverRetires) // 派遣の教官（§0.84）・ルミナ（§0.90）は親にならない
                .Concat(state.GuildRetirees.Where(r => appointed.Contains(r.Id)));
        }

        // ==================== 世代・血の蓄え・近い血（§0.133） ====================

        /// <summary>その世代の、能力の素質（PA）の上限：100＋GenerationPaCapStep×(世代−1)（GenerationPaCapMaxGen で止める）。限界突破した能力は BreakthroughPaCap に同じだけ足す。</summary>
        public static int PaCapOf(int generation, bool breakthrough)
        {
            int gen = Math.Clamp(generation, 1, SoulFusionBalance.GenerationPaCapMaxGen);
            int cap = SoulFusionBalance.GenerationBasePaCap + SoulFusionBalance.GenerationPaCapStep * (gen - 1);
            return breakthrough ? cap + (SoulFusionBalance.BreakthroughPaCap - SoulFusionBalance.NormalPaCap) : cap;
        }

        /// <summary>
        /// 1世代の素質に縮める（§0.135）：0〜100の素質を「下支え（GenerationFloorPa）＋元×(1世代の上限−下支え)÷100」に縮める（1以上）。
        /// 採用の候補と初めからいる子に使う。有望な新人とふつうの新人の差は残る。
        /// </summary>
        public static int ScaleToFirstGeneration(int pa, int capOffset = 0) =>
            Math.Max(1, (int)Math.Round(SoulFusionBalance.GenerationFloorPa + pa * (SoulFusionBalance.GenerationBasePaCap + capOffset - SoulFusionBalance.GenerationFloorPa) / 100.0, MidpointRounding.AwayFromZero));

        /// <summary>その子のその能力の素質の上限：世代と限界突破から決まる上限＋その子の上限のずれ（§0.135）。</summary>
        public static int PaCapOf(Adventurer a, string stat) => PaCapOf(a.Generation, a.FusionBreakthroughStats.Contains(stat)) + PaCapOffsetOf(a, stat);

        /// <summary>その子の、限界突破していない能力の素質の上限の幅（§0.135。個人の欄に出す）。全能力が限界突破なら限界突破の上限の幅。</summary>
        public static (int Min, int Max) PaCapRangeOf(Adventurer a)
        {
            var stats = AdventurerStatAccessor.AllStatNames.Where(s => !a.FusionBreakthroughStats.Contains(s)).ToList();
            if (stats.Count == 0) stats = AdventurerStatAccessor.AllStatNames.ToList();
            var caps = stats.Select(s => PaCapOf(a, s)).ToList();
            return (caps.Min(), caps.Max());
        }

        /// <summary>その子のその能力の上限のずれ（§0.135）。決めていなければ0。</summary>
        public static int PaCapOffsetOf(Adventurer a, string stat) => a.PaCapOffsets.TryGetValue(stat, out int o) ? o : 0;

        /// <summary>上限のずれを乱数で決める（§0.135。採用の候補・秘薬の娘）。</summary>
        public static void AssignPaCapOffsets(Adventurer a, IRng rng)
        {
            foreach (var stat in AdventurerStatAccessor.AllStatNames)
                a.PaCapOffsets[stat] = rng.NextInt(-SoulFusionBalance.PaCapSpread, SoulFusionBalance.PaCapSpread);
        }

        /// <summary>
        /// 上限のずれを文字列から決める（§0.135。初めからいる3人は名前から、旧セーブは Id から）。乱数を使わないので、同じ子はいつも同じずれになる。
        /// </summary>
        public static void AssignStablePaCapOffsets(Adventurer a, string seed)
        {
            int width = SoulFusionBalance.PaCapSpread * 2 + 1;
            foreach (var stat in AdventurerStatAccessor.AllStatNames)
            {
                uint h = 2166136261; // FNV-1a（string.GetHashCode は起動ごとに変わるので使わない）
                foreach (char c in seed + "/" + stat) { h ^= c; h *= 16777619; }
                a.PaCapOffsets[stat] = (int)(h % (uint)width) - SoulFusionBalance.PaCapSpread;
            }
        }

        /// <summary>
        /// 親の名前（その順の親が見つかればいまの名前、見つからなければ生まれたときに写した名前、どちらも無ければ「？」）。
        /// 退団・移籍した親は記録から消えるため、生まれたときの名前で出す。
        /// </summary>
        public static string ParentName(GameState state, Adventurer child, int index)
        {
            if (index >= child.ParentIds.Count) return "？";
            if (state.FindAdventurer(child.ParentIds[index]) is { } parent) return parent.Name;
            return index < child.ParentNames.Count && !string.IsNullOrEmpty(child.ParentNames[index]) ? child.ParentNames[index] : "？";
        }

        /// <summary>親の名前を持たない秘薬の娘（旧セーブ）に、見つかる親の名前を補う（見つからない親は空のまま）。</summary>
        public static void EnsureParentNames(GameState state)
        {
            foreach (var a in state.Adventurers.Concat(state.RetiredAdventurers).Concat(state.FallenAdventurers)
                .Concat(state.SoulFusionCultures.Select(c => c.Child)))
            {
                a.ParentNames ??= new List<string>();
                if (a.ParentIds.Count == 0 || a.ParentNames.Count == a.ParentIds.Count) continue;
                a.ParentNames = a.ParentIds.Select(id => state.FindAdventurer(id)?.Name ?? "").ToList();
            }
        }

        /// <summary>上限のずれを持たない子（旧セーブ）に、Id から決めたずれを補う（§0.135）。</summary>
        public static void EnsurePaCapOffsets(GameState state)
        {
            foreach (var a in state.Adventurers.Concat(state.RetiredAdventurers).Concat(state.FallenAdventurers)
                .Concat(state.SoulFusionCultures.Select(c => c.Child)))
            {
                a.PaCapOffsets ??= new Dictionary<string, int>();
                if (a.PaCapOffsets.Count == 0) AssignStablePaCapOffsets(a, a.Id.ToString());
            }
        }

        /// <summary>2代さかのぼった先祖（Id → 何代前か。親1・祖母2）。</summary>
        private static Dictionary<Guid, int> Ancestors(GameState state, Adventurer a)
        {
            var result = new Dictionary<Guid, int>();
            foreach (var p in a.ParentIds)
            {
                result.TryAdd(p, 1);
                if (state.FindAdventurer(p) is { } parent)
                    foreach (var g in parent.ParentIds) result.TryAdd(g, 2);
            }
            return result;
        }

        /// <summary>2人の血の近さ：親子・同じ両親の姉妹＝近い（Close）、片親が同じ姉妹・祖母と孫・おばと姪・いとこ＝やや近い（Near）。</summary>
        public static Kinship KinshipOf(GameState state, Adventurer a, Adventurer b)
        {
            var ancA = Ancestors(state, a);
            var ancB = Ancestors(state, b);
            bool parentChild = ancA.TryGetValue(b.Id, out int da) && da == 1 || ancB.TryGetValue(a.Id, out int db) && db == 1;
            bool fullSisters = a.ParentIds.Count == 2 && b.ParentIds.Count == 2 && a.ParentIds.All(b.ParentIds.Contains);
            if (parentChild || fullSisters) return Kinship.Close;
            if (ancA.ContainsKey(b.Id) || ancB.ContainsKey(a.Id) || ancA.Keys.Any(ancB.ContainsKey)) return Kinship.Near;
            return Kinship.None;
        }

        /// <summary>近い血で下がる娘の素質（能力ごと、pt）。</summary>
        public static int InbreedPenalty(Kinship k) => k switch
        {
            Kinship.Close => SoulFusionBalance.InbreedPenaltyClose,
            Kinship.Near => SoulFusionBalance.InbreedPenaltyNear,
            _ => 0,
        };

        /// <summary>血の近さの表示名。</summary>
        public static string KinshipLabel(Kinship k) => k switch
        {
            Kinship.Close => "近い血",
            Kinship.Near => "やや近い血",
            _ => "",
        };

        /// <summary>娘の世代：親の大きいほう＋1。</summary>
        public static int ChildGeneration(Adventurer a, Adventurer b) => Math.Max(a.Generation, b.Generation) + 1;

        /// <summary>
        /// 世代を親から数え直す（旧セーブ。§0.133 より前は世代を持っていない）。親が見つからなければ、その親は1世代として数える。
        /// </summary>
        public static void RecomputeGenerations(GameState state)
        {
            var all = state.Adventurers.Concat(state.RetiredAdventurers).Concat(state.FallenAdventurers)
                .Concat(state.SoulFusionCultures.Select(c => c.Child)).GroupBy(a => a.Id).Select(g => g.First()).ToDictionary(a => a.Id);
            var memo = new Dictionary<Guid, int>();
            int Gen(Adventurer a, int depth)
            {
                if (memo.TryGetValue(a.Id, out int g)) return g;
                if (a.ParentIds.Count == 0 || depth > 30) return memo[a.Id] = 1;
                int best = a.ParentIds.Select(p => all.TryGetValue(p, out var parent) ? Gen(parent, depth + 1) : 1).Max();
                return memo[a.Id] = best + 1;
            }
            foreach (var a in all.Values) a.Generation = Gen(a, 0);
        }

        /// <summary>
        /// 2人が親のペアになれるか：別人で、2人ともギルドにいて（→ GuildMembers）、どちらもまだ親になっておらず、
        /// 相性が RequiredCompatibility 以上。研究・培養槽・費用は見ない（→ CheckPrescription）。
        /// </summary>
        public static bool IsEligiblePair(GameState state, Adventurer a, Adventurer b)
        {
            if (a.Id == b.Id) return false;
            if (SoulFusionBalance.ParentOnlyOnce && (a.HasUsedSoulFusion || b.HasUsedSoulFusion)) return false;
            var members = GuildMembers(state).ToList();
            if (!members.Contains(a) || !members.Contains(b)) return false;
            return CompatibilitySystem.GetCompatibility(state, a.Id, b.Id) >= SoulFusionBalance.RequiredCompatibility;
        }

        /// <summary>親になれるペアの一覧（ギルドの並び順：現役→引退者）。画面の候補に使う。</summary>
        public static List<(Adventurer A, Adventurer B)> GetEligiblePairs(GameState state)
        {
            var members = GuildMembers(state).Where(m => !SoulFusionBalance.ParentOnlyOnce || !m.HasUsedSoulFusion).ToList();
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
        /// → 特性の継承（親A の枠順、親B の枠順で1つずつ）→ 先天判定（→ RecruitmentSystem.RollInnateTraits）→ 魂魄の申し子 → 見立てのずれ → 個性（→ PersonalitySystem.AssignChild）。顔は別の乱数。
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
                Age = SoulFusionBalance.ChildJoinAge, // 培養槽で2年育ち、16歳で加わる（§0.118）
                JobClass = childJob,
                Placement = PlacementRules.GetDefault(childJob),
                ParentIds = new List<Guid> { a.Id, b.Id },
                ParentNames = new List<string> { a.Name, b.Name }, // 親が退団しても名前を出せるように
                Generation = ChildGeneration(a, b), // 世代（§0.133）
            };
            AssignPaCapOffsets(child, _rng); // 素質の上限のずれ（§0.135）
            int inbreed = InbreedPenalty(KinshipOf(state, a, b)); // 近い血（§0.133）

            // ---- 能力の上限（PA）：両親の高い方へ寄せた基準＋ばらつき＋研究の上乗せ（§0.60）。限界突破しない能力は100で止める ----
            int varianceMin = Math.Min(SoulFusionBalance.PaVarianceMax,
                SoulFusionBalance.PaVarianceMin + CatalystBonus(catalystMaterialId, SoulFusionCatalystEffect.PaVarianceMinBonus));
            int researchBonus = GetResearchPaBonus(state);
            var rawPa = new Dictionary<string, int>();
            foreach (var stat in AdventurerStatAccessor.AllStatNames)
            {
                rawPa[stat] = BasePa(AdventurerStatAccessor.GetPa(a, stat), AdventurerStatAccessor.GetPa(b, stat)) + researchBonus - inbreed
                    + _rng.NextInt(varianceMin, SoulFusionBalance.PaVarianceMax);
            }

            var tier = GetNyxTier(a.JobClass, b.JobClass);
            var breakthroughStats = new List<string>();
            if (_rng.NextInt(1, 100) <= GetBreakthroughChance(tier, catalystMaterialId))
            {
                // 最も高い能力（同値なら AllStatNames の順で先のもの）の上限を外して上乗せする。
                string best = AdventurerStatAccessor.AllStatNames.OrderByDescending(s => rawPa[s]).First();
                rawPa[best] = Math.Min(PaCapOf(child.Generation, breakthrough: true) + PaCapOffsetOf(child, best),
                    rawPa[best] + _rng.NextInt(SoulFusionBalance.BreakthroughBonusMin, SoulFusionBalance.BreakthroughBonusMax));
                breakthroughStats.Add(best);
            }

            foreach (var stat in AdventurerStatAccessor.AllStatNames)
            {
                int cap = PaCapOf(child.Generation, breakthroughStats.Contains(stat)) + PaCapOffsetOf(child, stat); // 血の蓄え（§0.133）と上限のずれ（§0.135）
                int pa = Math.Clamp(rawPa[stat], 1, cap);
                AdventurerStatAccessor.SetPa(child, stat, pa);
                AdventurerStatAccessor.SetStat(child, stat, Math.Max(1, (int)(pa * SoulFusionBalance.ChildJoinGrowthRatio)));
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
            // 魂魄の申し子（§0.62）：秘薬で生まれた娘だけの特性。先天判定の後に1回判定する。
            if (_rng.NextInt(1, 100) <= SoulFusionBalance.SoulChildChancePercent)
                child.TryAddTrait(TraitCatalog.SoulChildId);
            PotentialEstimateSystem.AssignOffsets(child, _rng); // 副官の見立てのずれ（§0.74。親が分かっているので幅は半分）
            PersonalitySystem.AssignChild(state, child, a, b, _rng); // 志向・気質、相性の素地は両親の中点の近く（§0.100）
            RecruitmentSystem.FinishNewcomer(state, child);

            AssignPortrait(state, child, a, b);

            // 生まれの記録（誕生後も残る。研究室の培養槽画面の履歴に出す）
            child.FusionCatalystId = catalystMaterialId;
            child.FusionNyxTier = tier;
            child.FusionBreakthroughStats = breakthroughStats.ToList();

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
            if (traitId == TraitCatalog.SoulChildId) return 0; // 魂魄の申し子は親から受け継がず、娘ごとに判定する（§0.62）
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
        public static List<SoulFusionCulture> ProcessWeeklyCultures(GameState state, List<NurtureEvent>? nurtured = null)
        {
            var born = new List<SoulFusionCulture>();
            foreach (var culture in state.SoulFusionCultures.ToList())
            {
                if (culture.WeeksRemaining > 0)
                {
                    culture.WeeksRemaining--;
                    // 育て方（§0.118）：培養の始まりから季節ごとに、選んでいる育て方を1回反映する
                    int elapsed = SoulFusionBalance.CultureWeeks - culture.WeeksRemaining;
                    if (elapsed > 0 && elapsed % SoulFusionBalance.NurtureSeasonWeeks == 0)
                        (nurtured ?? new List<NurtureEvent>()).Add(ApplyNurture(state, culture));
                }
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

        // ==================== 培養槽の2年と育て方（§0.118） ====================

        /// <summary>育て方で伸ばす能力（体＝STR・VIT・AGI、技＝DEX・INT、心＝MND・LDR、まかせる＝全部。母・外の世界は無し）。</summary>
        public static IReadOnlyList<string> NurtureStats(NurtureKind kind) => kind switch
        {
            NurtureKind.Body => new[] { "STR", "VIT", "AGI" },
            NurtureKind.Skill => new[] { "DEX", "INT" },
            NurtureKind.Heart => new[] { "MND", "LDR" },
            NurtureKind.Entrust => AdventurerStatAccessor.AllStatNames,
            _ => Array.Empty<string>(),
        };

        public static string NurtureLabel(NurtureKind kind) => kind switch
        {
            NurtureKind.Body => "体を鍛える",
            NurtureKind.Skill => "技を磨く",
            NurtureKind.Heart => "心を育てる",
            NurtureKind.Mothers => "母に会わせる",
            NurtureKind.World => "外の世界を見せる",
            _ => "アルベールにまかせる",
        };

        /// <summary>育て方の効き目の説明（画面の選択肢の添え書き）。</summary>
        public static string NurtureDescription(NurtureKind kind) => kind switch
        {
            NurtureKind.Body or NurtureKind.Skill or NurtureKind.Heart => $"{string.Join("・", NurtureStats(kind))} +{SoulFusionBalance.NurtureStatGain}（素質で頭打ち）",
            NurtureKind.Mothers => $"母2人との相性 +{SoulFusionBalance.NurtureMotherCompatibility}・母の満足度 +{SoulFusionBalance.NurtureMotherSatisfaction}",
            NurtureKind.World => $"主志向・気質の見立て +{SoulFusionBalance.NurtureWorldExposure}回（加わるときから分かっている）",
            _ => $"全能力 +{SoulFusionBalance.NurtureEntrustGain}",
        };

        /// <summary>
        /// 育て方を1回反映する（季節ごと。ProcessWeeklyCultures から呼ぶ。テスト用に公開）：娘の今の能力を素質まで伸ばす／母との相性と母の満足度／
        /// 主志向・気質の見立て。反映したことを記録して返す。
        /// </summary>
        public static NurtureEvent ApplyNurture(GameState state, SoulFusionCulture culture)
        {
            var child = culture.Child;
            var gains = new Dictionary<string, int>();
            int statGain = culture.Nurture == NurtureKind.Entrust ? SoulFusionBalance.NurtureEntrustGain : SoulFusionBalance.NurtureStatGain;
            foreach (var stat in NurtureStats(culture.Nurture))
            {
                int before = AdventurerStatAccessor.GetStat(child, stat);
                int after = Math.Min(AdventurerStatAccessor.GetPa(child, stat), before + statGain);
                if (after <= before) continue;
                AdventurerStatAccessor.SetStat(child, stat, after);
                gains[stat] = after - before;
            }
            if (culture.Nurture == NurtureKind.Mothers)
                foreach (var motherId in new[] { culture.ParentAId, culture.ParentBId })
                {
                    CompatibilitySystem.AdjustCompatibility(state, child.Id, motherId, SoulFusionBalance.NurtureMotherCompatibility);
                    if (state.Adventurers.FirstOrDefault(a => a.Id == motherId && !a.IsRetired) is { } mother)
                        SatisfactionSystem.Adjust(mother, SoulFusionBalance.NurtureMotherSatisfaction);
                }
            if (culture.Nurture == NurtureKind.World && child.Personality is { } p)
                foreach (var key in new[] { p.Main.ToString(), PersonalitySystem.TemperamentKey })
                    p.Exposure[key] = p.Exposure.GetValueOrDefault(key) + SoulFusionBalance.NurtureWorldExposure;
            culture.NurtureHistory.Add(culture.Nurture);
            return new NurtureEvent(culture, culture.Nurture, gains);
        }

        /// <summary>名簿に加わるまで（宿舎の空きを待つ週は数えない）の残りの言い方：「1年3か月」「2か月」など。</summary>
        public static string RemainingLabel(SoulFusionCulture culture)
        {
            int weeks = Math.Max(0, culture.WeeksRemaining);
            int years = weeks / GameCalendar.WeeksPerYear;
            int months = (weeks % GameCalendar.WeeksPerYear + GameCalendar.WeeksPerMonth - 1) / GameCalendar.WeeksPerMonth;
            if (months == 12) { years++; months = 0; }
            return years > 0 ? (months > 0 ? $"{years}年{months}か月" : $"{years}年") : $"{Math.Max(1, months)}か月";
        }
    }
}
