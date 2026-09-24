using System;
using System.Collections.Generic;
using System.Linq;
using GuildManager.Core.Balance;
using GuildManager.Core.Data;
using GuildManager.Core.Models;
using GuildManager.Core.Rng;

namespace GuildManager.Core.Systems
{
    /// <summary>
    /// 採用（新春採用試験）システム。仕様書 03 §2.4・§7.3 参照。
    ///
    /// 年1回・新年（各年度の第1週）にのみ応募者が提示される。途中採用なし。
    ///
    /// 他システムに依存する部分のうち、施設Lv連動（雇用枠の宿舎Lv連動）は
    /// §6施設Lv投資システムの実装により接続済み。スカウト顧問ボーナス（→ §7.3）も
    /// GenerateCandidatesの引数経由で接続済み。残り1項目は該当システムが
    /// 依然として未実装のため固定値でスタブしたままにしてある：
    ///  - 応募の質の格付け連動（→ §8.1 ギルド格付け、未実装）→ 格付けに依らない固定の生成分布
    ///
    /// 事前調査メモ（項目32）：旧docs/03 §7に記述のあった「総合PA75以上の新人応募率+25%」は、
    /// スカウト顧問の効果として説明されていたが、§7（顧問制度）自体が本改訂まで未実装だった
    /// ため、コード上は存在しなかった。v1.3改訂では、この+25%を「スカウト未配置時でも適用される
    /// 基礎値」として新規実装し（HighPotentialBaseChance）、スカウト配置時はここに
    /// AdvisorSystem.GetScoutMasterBonusの値を上乗せする設計とした。
    ///
    /// 数値（応募者数・PA生成レンジ・契約金係数等）は → BAL: 採用（recruitment.csv、
    /// RecruitmentBalance）・→ BAL: 経済（economy.csv、EconomyBalance）に集約している
    /// （→ 03 §10.1、項目58）。
    /// </summary>
    public class RecruitmentSystem
    {
        private const int WeeksPerYear = 48; // 仕様書 03 §1.2（構造値）

        /// <summary>採用で抽選する職業の数（＝JobClass列挙型の全要素数）。</summary>
        private static readonly int AllJobClassCount = Enum.GetValues<JobClass>().Length;

        // ---- 有望新人（総合PA75以上）の出現判定（→ 03 §7.3。事前調査メモ参照） ----
        // → BAL: 採用/有望新人。基礎出現率（旧docs記載の「+25%」を基礎値として採用）、
        // 出現時はPA生成レンジを底上げしTotalPA≥75になりやすくする。
        public static double HighPotentialBaseChance => RecruitmentBalance.HighPotentialBaseChance;

        private readonly IRng _rng;

        public RecruitmentSystem(IRng rng)
        {
            _rng = rng;
        }

        /// <summary>
        /// 新春採用試験の週（2年目以降・各年度の第1週）かどうか。
        /// 1年目（ゲーム開始時点の第1週）は初期メンバーが既に配備されているため対象外
        /// （→ 03 §2.4「初期メンバーは採用システム対象外」）。
        /// </summary>
        public bool IsRecruitmentWeek(int weekNumber)
        {
            if (weekNumber <= WeeksPerYear) return false; // 1年目は対象外
            return ((weekNumber - 1) % WeeksPerYear) + 1 == 1;
        }

        /// <summary>
        /// 第1週（ゲーム開始週）限定の「新春ドラフト」の週かどうか（→ 03 §2.4、2026年9月に旧チュートリアル
        /// 採用試験を置き換え）。IsRecruitmentWeekが1年目を丸ごと対象外にしているのとは別枠の、開始週だけの
        /// 一度きりのイベント。呼び出し側（新規ゲーム開始フロー）がこの判定を見て StartInitialDraft を呼ぶ。
        /// </summary>
        public bool IsInitialDraftWeek(int weekNumber) => weekNumber == 1;

        /// <summary>
        /// 新春ドラフトで必ず1名ずつ候補に含める職業（初期3名＝重戦士・斥候・神官に欠けている職）。
        /// 並びは候補一覧の表示順。構造値のためCSV化しない。
        /// </summary>
        public static readonly JobClass[] DraftGuaranteedJobs =
        {
            JobClass.Mage, JobClass.Scholar, JobClass.Knight, JobClass.Thief,
        };

        /// <summary>
        /// 新春ドラフトを開始する（→ 03 §2.4）。候補は DraftGuaranteedJobs の4職を1名ずつ、残り
        /// （DraftCandidateCount − 4 名）は全職業から通常抽選。全員が応募年齢の下限（18歳＝新鋭期）で、
        /// 契約金は0G。採用上限は DraftHireCount 名（→ TryDraftHire）。
        /// </summary>
        public RecruitmentDraft StartInitialDraft(GameState state, double scoutMasterBonus = 0)
        {
            double highPotentialChance = HighPotentialBaseChance + scoutMasterBonus;
            int count = Math.Max(DraftGuaranteedJobs.Length, RecruitmentBalance.DraftCandidateCount);
            var existingNames = new HashSet<string>(state.Adventurers.Select(a => a.Name));

            var offers = new List<RecruitmentOffer>();
            for (int i = 0; i < count; i++)
            {
                JobClass? job = i < DraftGuaranteedJobs.Length ? DraftGuaranteedJobs[i] : null;
                var generated = GenerateOne(i, highPotentialChance, existingNames, job, RecruitmentBalance.MinCandidateAge);
                existingNames.Add(generated.Candidate.Name);
                offers.Add(new RecruitmentOffer(generated.Candidate, 0)); // ドラフトは契約金無料
            }

            return new RecruitmentDraft(offers, RecruitmentBalance.DraftHireCount);
        }

        /// <summary>
        /// 新春ドラフトの候補を1名採用する。契約金は取らず（所持金は減らない）、職業ごとの初期装備
        /// （→ Data.StarterEquipment）を着せて、装備補正込みの最大HPでHP満タンにしてから名簿へ加える。
        /// ドラフト終了済み・候補外・空き枠なしの場合は何もせず false。
        /// </summary>
        public bool TryDraftHire(GameState state, RecruitmentDraft draft, RecruitmentOffer offer)
        {
            if (draft.IsComplete) return false;
            if (!draft.Offers.Contains(offer)) return false;
            if (GetOpenSlotCount(state) <= 0) return false;

            StarterEquipment.Equip(offer.Candidate);
            state.Adventurers.Add(offer.Candidate);
            draft.Offers.Remove(offer);
            draft.HiresRemaining--;
            return true;
        }

        /// <summary>現役枠の上限（→ 03 §2.4「雇用枠」）。宿舎（Dormitory）の現在Lvに連動する。</summary>
        public int GetActiveSlotCap(GameState state) =>
            FacilityBalance.GetDormitoryCapacity(state.GetFacilityLevel(FacilityType.Dormitory));

        /// <summary>現役枠の空き数。引退済み（顧問化予定）の者は占有しない（→ 03 §2.4・§7）。</summary>
        public int GetOpenSlotCount(GameState state)
        {
            int activeCount = 0;
            foreach (var a in state.Adventurers)
                if (!a.IsRetired) activeCount++;

            return Math.Max(0, GetActiveSlotCap(state) - activeCount);
        }

        /// <summary>
        /// 今年の応募者一覧を生成する。応募者数自体は空き枠の有無に関わらず一定
        /// （空き枠の範囲でプレイヤーが選んで採用する。→ 03 §2.4「選抜」）。
        /// </summary>
        /// <param name="state">
        /// 現在のゲーム状態。氏名ジェネレーター（→ 03 §2.4・NameGenerator）が
        /// 現役ロースターと重複しない名前を生成するために参照する。
        /// </param>
        /// <param name="scoutMasterBonus">
        /// スカウト顧問のボーナス（生涯ピークLDR・DEX平均に比例。→ 03 §7.3・AdvisorSystem.
        /// GetScoutMasterBonus）。HighPotentialBaseChance(基礎25%)に上乗せする。
        /// スカウト未任命なら0を渡す（デフォルト値）。
        /// </param>
        /// <param name="candidateCount">
        /// 提示する応募者数。省略時は通常の新春採用試験と同じ RecruitmentBalance.CandidateCount。
        /// 第1週の新春ドラフトはこのメソッドではなく StartInitialDraft を使う。
        /// </param>
        public List<RecruitmentOffer> GenerateCandidates(GameState state, double scoutMasterBonus = 0, int? candidateCount = null)
        {
            double highPotentialChance = HighPotentialBaseChance + scoutMasterBonus;
            int count = candidateCount ?? RecruitmentBalance.CandidateCount;

            // 氏名の重複回避対象：現役ロースターに加え、同じ採用試験内で既に生成した
            // 候補の名前も含める（同じ回の応募者同士で名前が被らないようにするため）。
            var existingNames = new HashSet<string>(state.Adventurers.Select(a => a.Name));

            var offers = new List<RecruitmentOffer>();
            for (int i = 0; i < count; i++)
            {
                var offer = GenerateOne(i, highPotentialChance, existingNames);
                existingNames.Add(offer.Candidate.Name);
                offers.Add(offer);
            }
            return offers;
        }

        /// <summary>
        /// 応募を採用する。空き枠が無い、または契約金が足りない場合は何もせず false を返す。
        /// </summary>
        public bool TryHire(GameState state, RecruitmentOffer offer)
        {
            if (GetOpenSlotCount(state) <= 0) return false;
            if (state.Gold < offer.SigningBonus) return false;

            state.Gold -= offer.SigningBonus;
            state.Adventurers.Add(offer.Candidate);
            return true;
        }

        private RecruitmentOffer GenerateOne(int index, double highPotentialChance, HashSet<string> existingNames,
            JobClass? fixedJob = null, int? fixedAge = null)
        {
            int age = fixedAge ?? _rng.NextInt(RecruitmentBalance.MinCandidateAge, RecruitmentBalance.MaxCandidateAge);

            // ageT: 0（年齢レンジの下限＝伸びしろ最大）〜 1（上限＝即戦力寄り）。
            // 8年稼働モデル（→ 03 §3.7）では加入年齢を18歳に統一したためレンジ幅が0になる。
            // その場合は0除算を避け、ageT=0（伸びしろ最大）として扱う
            // ＝「全員が同じ18歳で入ってきて、同じだけの伸びしろを持つ」という新モデルの前提。
            int ageSpan = RecruitmentBalance.MaxCandidateAge - RecruitmentBalance.MinCandidateAge;
            double ageT = ageSpan == 0
                ? 0.0
                : (double)(age - RecruitmentBalance.MinCandidateAge) / ageSpan;
            int ageBonus = (int)Math.Round(RecruitmentBalance.YoungestAgePaBonus * (1 - ageT));
            double growthRatio = RecruitmentBalance.YoungestGrowthRatio + (RecruitmentBalance.OldestGrowthRatio - RecruitmentBalance.YoungestGrowthRatio) * ageT;

            // 職業は定義済みの全職業から等確率で抽選する（7職業化で Knight/Thief/Scholar を追加）。
            // 旧実装は NextInt(0, 3) と4職固定の数値で書かれており、職業を増やしても採用で
            // 一切出現しない状態になっていた。列挙型の要素数から範囲を導出し、
            // 今後職業を増減しても抽選範囲の更新漏れが起きないようにしている。
            // 新春ドラフトの保証枠（→ StartInitialDraft）は職業を固定し、抽選しない。
            var jobClass = fixedJob ?? (JobClass)_rng.NextInt(0, AllJobClassCount - 1);

            // 氏名ジェネレーター（→ 03 §2.4・NameGenerator）：文化圏（洋名80%/和名20%）を
            // ランダムに決定し、対応する名前プールから現役ロースターと重複しない
            // ファーストネームを選ぶ。
            // 性別の抽選は行わない：本作の冒険者は全員女性であり、Gender列挙型にも
            // Female以外の値が存在しない（→ Models.Gender・女性限定ギルドの構造化）。
            var culture = NameGenerator.RollCulture(_rng);
            var name = NameGenerator.GenerateUniqueFirstName(culture, existingNames, _rng);

            var candidate = new Adventurer
            {
                Name = name,
                Gender = Gender.Female,
                Age = age,
                JobClass = jobClass,
                Placement = PlacementRules.GetDefault(jobClass), // → 03 §4.2：配置の初期値は職業から自動決定
            };

            // 有望新人（高PA）の出現判定（→ 03 §7.3）。判定に成功した候補は
            // PA生成レンジを底上げする（通常40〜80 → 高潜在70〜100）。
            bool isHighPotential = _rng.NextInt(1, 100) <= highPotentialChance * 100;
            int minPa = isHighPotential ? RecruitmentBalance.HighPotentialMinPa : RecruitmentBalance.MinGeneratedPa;
            int maxPa = isHighPotential ? RecruitmentBalance.HighPotentialMaxPa : RecruitmentBalance.MaxGeneratedPa;

            foreach (var stat in AdventurerStatAccessor.AllStatNames)
            {
                int pa = Math.Min(100, _rng.NextInt(minPa, maxPa) + ageBonus);
                int actual = Math.Max(1, (int)(pa * growthRatio));
                AdventurerStatAccessor.SetPa(candidate, stat, pa);
                AdventurerStatAccessor.SetStat(candidate, stat, actual);
            }

            candidate.CurrentHP = candidate.MaxHP;
            candidate.WeeklyWage = Math.Max(1, (int)(candidate.TotalPA * EconomyBalance.WeeklyWageCoefficient));

            // 先天特性の付与判定（→ 03 §5.3.2）。いずれも独立判定（1人が複数持つこともありうる）。
            // 項目64で田舎育ち・知識人を追加。付与確率は既存3種と同じ定数を再利用している
            // （特性ごとに出現率を変える必要が出た時点で、recruitment.csv側にキーを分ければよい）。
            if (_rng.NextInt(1, 100) <= RecruitmentBalance.InnateTraitChancePercent)
                candidate.TryAddTrait(TraitCatalog.BraveId);
            if (_rng.NextInt(1, 100) <= RecruitmentBalance.InnateTraitChancePercent)
                candidate.TryAddTrait(TraitCatalog.AttentiveId);
            if (_rng.NextInt(1, 100) <= RecruitmentBalance.InnateTraitChancePercent)
                candidate.TryAddTrait(TraitCatalog.BeautifulId);
            if (_rng.NextInt(1, 100) <= RecruitmentBalance.InnateTraitChancePercent)
                candidate.TryAddTrait(TraitCatalog.CountryBredId);
            if (_rng.NextInt(1, 100) <= RecruitmentBalance.InnateTraitChancePercent)
                candidate.TryAddTrait(TraitCatalog.ScholarId);

            int signingBonus = (int)(candidate.TotalPA * candidate.Age * EconomyBalance.SigningBonusCoefficient);

            return new RecruitmentOffer(candidate, signingBonus);
        }
    }
}
