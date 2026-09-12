using System;
using System.Collections.Generic;
using GuildManager.Core.Balance;
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
    /// 数値（応募者数・PA生成レンジ・契約金係数等）は本来 → BAL: 採用 に集約する値だが、
    /// 04_バランス表.xlsx はまだコードから読み込めない（Phase 4で外部化予定）ため、
    /// 他のSystemと同様に現状は仮値を定数として直書きする。
    /// </summary>
    public class RecruitmentSystem
    {
        private const int WeeksPerYear = 48; // 仕様書 03 §1.2

        private const int CandidateCount = 5; // → BAL: 採用/応募者数。現状は仮値

        private const int MinCandidateAge = 15;
        private const int MaxCandidateAge = 18;

        // → BAL: 採用/年齢別PA補正。現状は仮値（15歳で+10、18歳で+0へ線形補間）。
        private const int YoungestAgePaBonus = 10;

        // → BAL: 採用（PA自体の生成レンジ）。現状は仮値。
        private const int MinGeneratedPa = 40;
        private const int MaxGeneratedPa = 80;

        // ---- 有望新人（総合PA75以上）の出現判定（→ 03 §7.3。事前調査メモ参照） ----
        // → BAL: 採用/有望新人。現状は仮値：基礎出現率25%（旧docs記載の「+25%」を基礎値として採用）、
        // 出現時はPA生成レンジを底上げしTotalPA≥75になりやすくする。
        public const double HighPotentialBaseChance = 0.25;
        private const int HighPotentialMinPa = 70;
        private const int HighPotentialMaxPa = 100;

        // 実効値/PA の比率。15歳ほどPAから遠く（伸びしろ最大）、18歳ほどPAに近い（即戦力）
        // （→ 03 §2.4）。→ BAL: 採用。現状は仮値（15歳:30%実現 〜 18歳:70%実現）。
        private const double YoungestGrowthRatio = 0.3;
        private const double OldestGrowthRatio = 0.7;

        private const double SigningBonusCoefficient = 3.0; // → BAL: 採用/契約金。現状は仮値
        private const double WeeklyWageCoefficient = 0.6;   // → BAL: 経済/週給基準。現状は仮値

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
        /// <param name="scoutMasterBonus">
        /// スカウト顧問のボーナス（生涯ピークLDR・DEX平均に比例。→ 03 §7.3・AdvisorSystem.
        /// GetScoutMasterBonus）。HighPotentialBaseChance(基礎25%)に上乗せする。
        /// スカウト未任命なら0を渡す（デフォルト値）。
        /// </param>
        public List<RecruitmentOffer> GenerateCandidates(double scoutMasterBonus = 0)
        {
            double highPotentialChance = HighPotentialBaseChance + scoutMasterBonus;
            var offers = new List<RecruitmentOffer>();
            for (int i = 0; i < CandidateCount; i++)
                offers.Add(GenerateOne(i, highPotentialChance));
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

        private RecruitmentOffer GenerateOne(int index, double highPotentialChance)
        {
            int age = _rng.NextInt(MinCandidateAge, MaxCandidateAge);

            // ageT: 0(15歳) 〜 1(18歳)
            double ageT = (double)(age - MinCandidateAge) / (MaxCandidateAge - MinCandidateAge);
            int ageBonus = (int)Math.Round(YoungestAgePaBonus * (1 - ageT));
            double growthRatio = YoungestGrowthRatio + (OldestGrowthRatio - YoungestGrowthRatio) * ageT;

            var jobClass = (JobClass)_rng.NextInt(0, 3);
            var candidate = new Adventurer
            {
                // 氏名ジェネレータは別タスク（→ docs/06_タスクリスト.md Phase 4）。ここでは仮の識別名。
                Name = $"新人応募者{(char)('A' + index)}",
                Age = age,
                JobClass = jobClass,
                Placement = PlacementRules.GetDefault(jobClass), // → 03 §4.2：配置の初期値は職業から自動決定
            };

            // 有望新人（高PA）の出現判定（→ 03 §7.3）。判定に成功した候補は
            // PA生成レンジを底上げする（通常40〜80 → 高潜在70〜100）。
            bool isHighPotential = _rng.NextInt(1, 100) <= highPotentialChance * 100;
            int minPa = isHighPotential ? HighPotentialMinPa : MinGeneratedPa;
            int maxPa = isHighPotential ? HighPotentialMaxPa : MaxGeneratedPa;

            foreach (var stat in AdventurerStatAccessor.AllStatNames)
            {
                int pa = Math.Min(100, _rng.NextInt(minPa, maxPa) + ageBonus);
                int actual = Math.Max(1, (int)(pa * growthRatio));
                AdventurerStatAccessor.SetPa(candidate, stat, pa);
                AdventurerStatAccessor.SetStat(candidate, stat, actual);
            }

            candidate.CurrentHP = candidate.MaxHP;
            candidate.WeeklyWage = Math.Max(1, (int)(candidate.TotalPA * WeeklyWageCoefficient));

            int signingBonus = (int)(candidate.TotalPA * candidate.Age * SigningBonusCoefficient);

            return new RecruitmentOffer(candidate, signingBonus);
        }
    }
}
